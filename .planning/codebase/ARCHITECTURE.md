# Architecture

**Analysis Date:** 2026-03-30

## Pattern Overview

**Overall:** MVVM (Model-View-ViewModel) desktop application with a service layer, using ReactiveUI for reactive state binding, and Microsoft.Extensions.DependencyInjection for composition.

**Key Characteristics:**
- Strict MVVM with ReactiveUI: ViewModels never touch Views directly; all dialog/window interactions go through `Interaction<TInput, TOutput>` handlers registered in code-behind
- Centralized immutable state hub (`AppState` record + `IStateService`) with reactive `IObservable` streams driving all UI updates
- Sequential, single-process cleaning pipeline: one xEdit instance at a time, enforced by a `SemaphoreSlim(1,1)` process slot
- Two-project architecture: `AutoQAC` (desktop app) depends on `QueryPlugins` (standalone Mutagen analysis library)

## Project Dependency Graph

```
AutoQAC.csproj  ──references──>  QueryPlugins.csproj
    │                                  │
    ├── Avalonia 11.3.12               ├── Mutagen.Bethesda 0.53.1
    ├── ReactiveUI.Avalonia 11.3.8     ├── Mutagen.Bethesda.Skyrim 0.53.1
    ├── Microsoft.Extensions.DI 10.0.3 ├── Mutagen.Bethesda.Fallout4 0.53.1
    ├── Serilog 4.3.1                  ├── Mutagen.Bethesda.Starfield 0.53.1
    ├── YamlDotNet 16.3.0             └── Mutagen.Bethesda.Oblivion 0.53.1
    ├── Mutagen.Bethesda 0.53.1
    ├── Mutagen.Bethesda.Skyrim 0.53.1
    └── Mutagen.Bethesda.Fallout4 0.53.1
```

- `AutoQAC` is the WinExe desktop app targeting `net10.0-windows10.0.19041.0`
- `QueryPlugins` is a pure library targeting `net10.0` (no UI dependencies)
- `AutoQAC` references `QueryPlugins` via project reference
- `QueryPlugins` has no dependency on `AutoQAC` -- the dependency is one-directional
- The `Mutagen/` directory is a read-only git submodule for reference only; it is NOT built or referenced by the solution

## Dependency Injection

**Composition root:** `AutoQAC/App.axaml.cs` in `OnFrameworkInitializationCompleted()`

**Registration file:** `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`

**Registration groups (called in order):**

1. `AddInfrastructure()` -- `ILoggingService` (Singleton)
2. `AddConfiguration()` -- `IConfigurationService`, `IConfigWatcherService`, `ILegacyMigrationService`, `ILogRetentionService` (all Singleton)
3. `AddState()` -- `IStateService` (Singleton)
4. `AddBusinessLogic()` -- All domain services (all Singleton):
   - `IGameDetectionService`, `IPluginValidationService`, `IPluginLoadingService`, `IPluginIssueApproximationService`
   - `IProcessExecutionService`, `IMo2ValidationService`
   - `IXEditCommandBuilder`, `IXEditOutputParser`, `IXEditLogFileService`
   - `ICleaningService`, `IBackupService`, `IHangDetectionService`, `ICleaningOrchestrator`
5. `AddUiServices()` -- `IFileDialogService`, `IMessageDialogService` (Singleton)
6. `AddViewModels()` -- `MainWindowViewModel` (Singleton), all others **Transient**
7. `AddViews()` -- All views **Transient** (windows/dialogs created per-show)

**Service lifetimes:**
- Nearly all services are **Singleton** because they share a single `IStateService` instance and maintain long-lived subscriptions
- `MainWindowViewModel` is Singleton (lives for the app lifetime, composes sub-VMs)
- Secondary ViewModels (`ProgressViewModel`, `SettingsViewModel`, `RestoreViewModel`, etc.) are Transient (created per dialog open)
- All Views are Transient

**MainWindow is manually constructed** in `App.axaml.cs` (not resolved from DI) to pass all required interaction handler dependencies directly.

## MVVM Pattern

### ViewModel Hierarchy

```
MainWindowViewModel (Singleton, slim orchestrator)
├── ConfigurationViewModel       -- paths, game selection, auto-save, plugin loading
├── PluginListViewModel          -- plugin collection, select/deselect all
└── CleaningCommandsViewModel    -- start/stop/preview, validation, status during cleaning
```

- `MainWindowViewModel` owns `Interaction<TInput, TOutput>` instances that the code-behind registers handlers for
- `MainWindowViewModel` subscribes to `IStateService.StateChanged` and dispatches `OnStateChanged(AppState)` to all three sub-VMs
- Each sub-VM has its own `CompositeDisposable` and implements `IDisposable`

### Standalone ViewModels (Transient, created per dialog):

| ViewModel | Purpose | Created in |
|-----------|---------|------------|
| `ProgressViewModel` | Live cleaning progress, dry-run preview | `MainWindow.axaml.cs` |
| `SettingsViewModel` | Settings editing dialog | `MainWindow.axaml.cs` |
| `SkipListViewModel` | Skip list editing dialog | `MainWindow.axaml.cs` |
| `RestoreViewModel` | Backup restore browser | `MainWindow.axaml.cs` |
| `CleaningResultsViewModel` | Post-session results display | `MainWindow.axaml.cs` |
| `PartialFormsWarningViewModel` | Experimental feature warning | `MainWindow.axaml.cs` |
| `MessageDialogViewModel` | Generic message/error dialogs | `MessageDialogService` |
| `AboutViewModel` | About window | `MainWindow.axaml.cs` |

### View-ViewModel Binding Approach

- `DataContext` is set in code-behind, not in XAML
- Avalonia compiled bindings are enabled by default (`AvaloniaUseCompiledBindingsByDefault=true` in csproj)
- `ViewModelBase` extends `ReactiveObject` for `INotifyPropertyChanged` support
- Dialog results flow through `Interaction<TInput, TOutput>` -- ViewModel raises the interaction, View code-behind handles it by creating/showing the dialog window

### Reactive Patterns Used

| Pattern | Usage |
|---------|-------|
| `RaiseAndSetIfChanged` | All mutable ViewModel properties |
| `ReactiveCommand` | All commands (sync and async variants) |
| `WhenAnyValue` | Computed properties and auto-save triggers |
| `ObservableAsPropertyHelper` | Derived properties like `IsMutagenSupported`, `RequiresLoadOrderFile` |
| `Interaction<TInput, TOutput>` | All ViewModel-to-View dialog interactions |
| `BehaviorSubject<T>` | State service broadcasting (`StateChanged`, `IsTerminatingChanged`) |
| `Subject<T>` | Event streams (`PluginProcessed`, `CleaningCompleted`, `HangDetected`) |
| `CompositeDisposable` | Lifecycle cleanup in all ViewModels |
| `ObserveOn(RxApp.MainThreadScheduler)` | Thread marshaling for UI updates from background state changes |

## Service Layer

### Service Groups and Responsibilities

**Configuration (`AutoQAC/Services/Configuration/`):**
- `IConfigurationService` / `ConfigurationService` -- YAML read/write with debounced saves (500ms throttle), SHA256 change detection, skip list management, per-game data folder overrides, per-game load order overrides. File: `AutoQAC/Services/Configuration/ConfigurationService.cs`
- `IConfigWatcherService` / `ConfigWatcherService` -- FileSystemWatcher for external YAML edits; compares hash to detect non-app changes and calls `ReloadFromDiskAsync`. File: `AutoQAC/Services/Configuration/ConfigWatcherService.cs`
- `ILegacyMigrationService` / `LegacyMigrationService` -- One-time migration from legacy config format. File: `AutoQAC/Services/Configuration/LegacyMigrationService.cs`
- `ILogRetentionService` / `LogRetentionService` -- Cleanup of old log files on startup. File: `AutoQAC/Services/Configuration/LogRetentionService.cs`

**State (`AutoQAC/Services/State/`):**
- `IStateService` / `StateService` -- Central state hub. Owns `AppState` (immutable record), provides thread-safe `UpdateState(Func<AppState, AppState>)` with lock-protected read-modify-write. Emits state changes via `BehaviorSubject`. File: `AutoQAC/Services/State/StateService.cs`

**Plugin (`AutoQAC/Services/Plugin/`):**
- `IPluginLoadingService` / `PluginLoadingService` -- Loads plugins via Mutagen for supported games (SkyrimLE/SE/VR, Fallout4/4VR) or from load order text files for older games (Oblivion, FO3, FNV). File: `AutoQAC/Services/Plugin/PluginLoadingService.cs`
- `IPluginValidationService` / `PluginValidationService` -- Validates plugin files on disk (existence, readability, extension, zero-byte). File: `AutoQAC/Services/Plugin/PluginValidationService.cs`
- `IPluginIssueApproximationService` / `PluginIssueApproximationService` -- Uses `QueryPlugins.IPluginQueryService` to run Mutagen-based ITM/UDR/navmesh analysis for preview counts. Streams results per-plugin via callback. File: `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs`

**Cleaning (`AutoQAC/Services/Cleaning/`):**
- `ICleaningOrchestrator` / `CleaningOrchestrator` -- End-to-end session coordinator. Steps: flush pending config, validate environment, detect game/variant, apply skip lists, backup plugins, launch xEdit sequentially, parse results, finalize session. File: `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- `ICleaningService` / `CleaningService` -- Cleans a single plugin by building xEdit command and executing via ProcessExecutionService. File: `AutoQAC/Services/Cleaning/CleaningService.cs`
- `IXEditCommandBuilder` / `XEditCommandBuilder` -- Builds `ProcessStartInfo` for xEdit with correct flags (-QAC, -autoexit, -autoload, game type flag, MO2 wrapping, partial forms). File: `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`
- `IXEditOutputParser` / `XEditOutputParser` -- Parses xEdit stdout/log file output into `CleaningStatistics` (ITMs removed, UDRs fixed). File: `AutoQAC/Services/Cleaning/XEditOutputParser.cs`
- `IXEditLogFileService` / `XEditLogFileService` -- Reads xEdit log files for enriched statistics (preferred over stdout). File: `AutoQAC/Services/Cleaning/XEditLogFileService.cs`

**Process (`AutoQAC/Services/Process/`):**
- `IProcessExecutionService` / `ProcessExecutionService` -- Single-slot (`SemaphoreSlim(1,1)`) process executor with timeout, PID tracking file (`autoqac-pids.json`), orphan cleanup, and two-stage termination (graceful CloseMainWindow with 2.5s grace, then force kill). File: `AutoQAC/Services/Process/ProcessExecutionService.cs`

**GameDetection (`AutoQAC/Services/GameDetection/`):**
- `IGameDetectionService` / `GameDetectionService` -- Detects game type from xEdit executable name or load order file contents. Detects game variants (TTW, Enderal) from marker plugins in the load order. File: `AutoQAC/Services/GameDetection/GameDetectionService.cs`

**MO2 (`AutoQAC/Services/MO2/`):**
- `IMo2ValidationService` / `Mo2ValidationService` -- Validates MO2 executable path. File: `AutoQAC/Services/MO2/MO2ValidationService.cs`

**Monitoring (`AutoQAC/Services/Monitoring/`):**
- `IHangDetectionService` / `HangDetectionService` -- Polls process CPU usage; emits hang state when near-zero CPU persists for 60+ seconds. File: `AutoQAC/Services/Monitoring/HangDetectionService.cs`

**Backup (`AutoQAC/Services/Backup/`):**
- `IBackupService` / `BackupService` -- Pre-cleaning plugin backup with session directories, `session.json` metadata, restore capability, and session retention cleanup. Skipped in MO2 mode (MO2 manages files via VFS). File: `AutoQAC/Services/Backup/BackupService.cs`

**UI (`AutoQAC/Services/UI/`):**
- `IFileDialogService` / `FileDialogService` -- Wraps Avalonia file/folder dialogs. File: `AutoQAC/Services/UI/FileDialogService.cs`
- `IMessageDialogService` / `MessageDialogService` -- Shows error/warning/confirm/retry dialogs. File: `AutoQAC/Services/UI/MessageDialogService.cs`

### Service Interaction Patterns

- Services communicate through `IStateService` as the shared state hub, not by calling each other's methods directly for state queries
- `CleaningOrchestrator` is the primary coordinator -- it depends on most other services and orchestrates the full cleaning session
- `ConfigurationService` uses a debounced save pipeline (Rx `Throttle` + `Switch`) to coalesce rapid config changes
- `ConfigWatcherService` monitors the YAML file for external edits and triggers `ReloadFromDiskAsync` on `ConfigurationService`
- `PluginIssueApproximationService` bridges `AutoQAC` and `QueryPlugins` by creating Mutagen load order contexts and delegating to `IPluginQueryService`

## State Management

### AppState Record

**File:** `AutoQAC/Models/AppState.cs`

`AppState` is a sealed `record` (immutable with `with` expressions). Contains:
- Configuration paths: `LoadOrderPath`, `Mo2ExecutablePath`, `XEditExecutablePath`
- Computed validity: `IsLoadOrderConfigured`, `IsMo2Configured`, `IsXEditConfigured`
- Runtime state: `IsCleaning`, `CurrentPlugin`, `CurrentOperation`
- Progress: `Progress`, `TotalPlugins`, `PluginsToClean` (IReadOnlyList)
- Results: `CleanedPlugins`, `FailedPlugins`, `SkippedPlugins` (IReadOnlySet via FrozenSet)
- Settings: `CleaningTimeout`, `Mo2ModeEnabled`, `PartialFormsEnabled`, `CurrentGameType`

### StateService

**File:** `AutoQAC/Services/State/StateService.cs`

- Thread-safe via `Lock` (System.Threading.Lock) for the `_currentState` field
- `UpdateState(Func<AppState, AppState>)` does read-modify-write inside the lock, then emits the new state outside the lock via `BehaviorSubject.OnNext`
- Exposes multiple observable streams:
  - `StateChanged` -- full `AppState` on every update (subscribed by `MainWindowViewModel`)
  - `ConfigurationValidChanged` -- distinct bool when config validity changes
  - `ProgressChanged` -- distinct (current, total) tuple
  - `PluginProcessed` -- per-plugin status events
  - `DetailedPluginResult` -- per-plugin cleaning results with statistics (for `ProgressViewModel`)
  - `CleaningCompleted` -- full `CleaningSessionResult` when session ends
  - `IsTerminatingChanged` -- termination state for UI blocking

### State Flow

```
User Action / Service Event
        │
        ▼
  IStateService.UpdateState(s => s with { ... })
        │
        ▼
  BehaviorSubject<AppState>.OnNext(newState)
        │
        ▼
  MainWindowViewModel subscribes (ObserveOn MainThread)
        │
        ├──> ConfigurationViewModel.OnStateChanged(state)
        ├──> PluginListViewModel.OnStateChanged(state)
        └──> CleaningCommandsViewModel.OnStateChanged(state)
                │
                ▼
          ReactiveUI property notifications → Avalonia compiled bindings → UI
```

## Key Workflows

### Cleaning Orchestration Flow

**Entry point:** `CleaningOrchestrator.StartCleaningAsync()` in `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`

1. Clean orphaned xEdit processes from previous runs
2. Flush pending config saves (`FlushPendingSavesAsync`)
3. Validate configuration (xEdit path, load order if needed)
4. Detect game type from xEdit executable name or load order
5. Detect game variant (TTW, Enderal) from marker plugins
6. Apply skip list filtering (bundled defaults + user overrides + variant-specific)
7. Validate plugin files on disk (skipped in MO2 mode)
8. Update state: `stateService.StartCleaning(pluginsToClean)`
9. Create backup session directory (if backup enabled, not MO2 mode)
10. **Sequential loop** over each plugin:
    a. Backup plugin file to session directory
    b. Build xEdit command (`XEditCommandBuilder`)
    c. Execute via `ProcessExecutionService` (single slot)
    d. Start hang monitoring (`HangDetectionService`)
    e. Parse results from xEdit log file or stdout
    f. Record result in state via `stateService.AddDetailedCleaningResult()`
    g. Timeout retry logic (up to 3 attempts with user prompt)
11. Write backup session metadata (`session.json`)
12. Run backup retention cleanup
13. Create `CleaningSessionResult` and call `stateService.FinishCleaningWithResults()`

**Stop behavior is two-stage:**
- First click: Cancel CTS + graceful `CloseMainWindow` with 2.5s grace
- Second click (during grace): Immediate force kill of process tree
- Grace period expired without second click: ViewModel prompts user to confirm force kill

### Plugin Loading Flow

**Entry point:** `ConfigurationViewModel.RefreshPluginsForGameAsync()` in `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`

1. If game is `Unknown`, clear plugins and return
2. Update `CurrentGameType` in state
3. Resolve data folder (auto-detect via Mutagen/registry + per-game override)
4. **Mutagen path** (SkyrimLE/SE/VR, Fallout4/4VR):
   a. Call `IPluginLoadingService.TryGetPluginsAsync(gameType, customDataFolder)`
   b. Apply skip list status to returned plugins
   c. Start background approximation refresh (ITM/UDR/navmesh counts via `PluginIssueApproximationService`)
   d. Stream approximation results per-plugin to update UI live
5. **File-based fallback** (Oblivion, FO3, FNV, or Mutagen failure):
   a. Resolve load order path (per-game override or auto-detect from My Games folder)
   b. Call `IPluginLoadingService.GetPluginsFromFileAsync(loadOrderPath, dataFolderPath)`
   c. Apply skip list status

### Configuration Management Flow

**Files:**
- `AutoQAC/Services/Configuration/ConfigurationService.cs` -- core read/write
- `AutoQAC/Services/Configuration/ConfigWatcherService.cs` -- external change detection
- `AutoQAC Data/AutoQAC Settings.yaml` -- user settings (YAML)
- `AutoQAC Data/AutoQAC Main.yaml` -- bundled read-only config (skip lists, xEdit names, version)

**Save pipeline:**
1. ViewModel property change (e.g., MO2Mode toggle)
2. Rx subscription calls `SaveConfigurationAsync()` which pushes to `_saveRequests` Subject
3. `Throttle(500ms)` coalesces rapid changes
4. `Switch()` cancels superseded in-flight saves
5. `SaveToDiskWithRetryAsync()` acquires `SemaphoreSlim` file lock, serializes YAML, writes atomically, stores SHA256 hash

**External edit detection:**
1. `ConfigWatcherService` monitors `AutoQAC Settings.yaml` via `FileSystemWatcher`
2. On change, compares file hash against `ConfigurationService.GetLastWrittenHash()`
3. If hash differs (external edit), calls `ConfigurationService.ReloadFromDiskAsync()`

## QueryPlugins Architecture

**Standalone analysis library** -- no dependency on AutoQAC or any UI framework.

**Entry point:** `QueryPlugins/IPluginQueryService.cs` + `QueryPlugins/PluginQueryService.cs`

**Detector pattern:**
- `IItmDetector` -- Cross-game ITM detection via Mutagen link cache comparison
- `IGameSpecificDetector` -- Per-game UDR and navmesh detection, each declares its `SupportedReleases`

**Game detectors (each in `QueryPlugins/Detectors/Games/`):**
- `SkyrimDetector` -- SkyrimLE, SkyrimSE, SkyrimVR
- `Fallout4Detector` -- Fallout4, Fallout4VR
- `StarfieldDetector` -- Starfield
- `OblivionDetector` -- Oblivion

**Integration with AutoQAC:** `PluginIssueApproximationService` creates Mutagen load order + link cache, iterates plugins, calls `IPluginQueryService.Analyse()` for each, and streams results back to the UI.

## Entry Points

**Application entry:** `AutoQAC/App.axaml.cs` -- builds DI container, creates MainWindow, starts config watcher, runs legacy migration and log retention cleanup

**Cleaning entry:** `ICleaningOrchestrator.StartCleaningAsync()` -- called from `CleaningCommandsViewModel`

**Plugin analysis entry:** `IPluginQueryService.Analyse()` -- called from `PluginIssueApproximationService`

## Error Handling

**Strategy:** Catch-log-and-continue for non-critical failures; throw for configuration validation failures.

**Patterns:**
- `CleaningOrchestrator` catches `OperationCanceledException` separately from general `Exception`, preserving partial results in both cases
- Services log errors via `ILoggingService` and propagate exceptions to callers
- ViewModels catch service exceptions and display them via `IMessageDialogService` or validation error UI
- `ConfigurationService` uses retry logic for file I/O failures
- `ProcessExecutionService` tracks PIDs and cleans orphaned processes on startup

## Cross-Cutting Concerns

**Logging:** Serilog with console and file sinks, wrapped behind `ILoggingService`. Log files in `AutoQAC/logs/`. Log retention cleanup on startup.

**Validation:** Pre-clean validation in `CleaningCommandsViewModel.ValidatePreClean()` checks xEdit path, load order, plugins, MO2 config. Returns structured `ValidationError` objects with title, message, and suggestion.

**Authentication:** Not applicable (local desktop application).

**Thread Safety:** `StateService` uses `System.Threading.Lock` for state mutations. `ConfigurationService` uses `SemaphoreSlim` for file I/O. `ProcessExecutionService` uses `SemaphoreSlim(1,1)` for single-process enforcement. `CleaningOrchestrator` uses dedicated locks for CTS and process references.

---

*Architecture analysis: 2026-03-30*
