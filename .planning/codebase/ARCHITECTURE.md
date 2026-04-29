<!-- refreshed: 2026-04-29 -->
# Architecture

**Analysis Date:** 2026-04-29

## System Overview

```text
┌─────────────────────────────────────────────────────────────┐
│                  Avalonia Desktop UI Layer                  │
├──────────────────┬──────────────────┬───────────────────────┤
│ Main window shell│ Dialog windows   │ ViewModel composition  │
│ `AutoQAC/Views` │ `AutoQAC/Views`  │ `AutoQAC/ViewModels`   │
└────────┬─────────┴────────┬─────────┴──────────┬────────────┘
         │ interactions      │ data binding        │ state events
         ▼                   ▼                     ▼
┌─────────────────────────────────────────────────────────────┐
│             Application Services / Orchestration             │
│ `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`          │
│ `AutoQAC/Services/*` via DI in `AutoQAC/Infrastructure`      │
└────────┬──────────────────┬──────────────────────┬──────────┘
         │                  │                      │
         ▼                  ▼                      ▼
┌─────────────────┐ ┌──────────────────┐ ┌────────────────────┐
│ Shared AppState │ │ xEdit/MO2 process │ │ Plugin/config I/O  │
│ `AutoQAC/Services/State` │ `AutoQAC/Services/Process` │ `AutoQAC Data/` │
└─────────────────┘ └──────────────────┘ └────────────────────┘
         │                                          │
         ▼                                          ▼
┌─────────────────────────────────────────────────────────────┐
│         External analysis and referenced libraries           │
│ `QueryPlugins/` uses Mutagen packages; `Mutagen/` is a       │
│ read-only referenced submodule and is not application code.  │
└─────────────────────────────────────────────────────────────┘
```

## Component Responsibilities

| Component | Responsibility | File |
|-----------|----------------|------|
| Avalonia startup | Creates the desktop lifetime and configures platform, Inter font, debug DevTools, and trace logging. | `AutoQAC/Program.cs` |
| Application bootstrap | Builds the DI container, resolves main window dependencies, starts config watching, runs legacy migration and log retention cleanup, and disposes services on shutdown. | `AutoQAC/App.axaml.cs` |
| DI registration | Registers infrastructure, configuration, state, business services, UI services, ViewModels, and Views. | `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` |
| Main shell ViewModel | Composes `ConfigurationViewModel`, `PluginListViewModel`, and `CleaningCommandsViewModel`; owns UI interactions and dispatches state changes to child ViewModels. | `AutoQAC/ViewModels/MainWindowViewModel.cs` |
| Main window code-behind | Owns window/dialog creation and registers interaction handlers; ViewModels request dialogs through interaction abstractions rather than manipulating controls. | `AutoQAC/Views/MainWindow.axaml.cs` |
| Configuration UI | Loads/saves settings, validates paths, responds to selected game changes, refreshes plugin lists, and handles file dialogs. | `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs` |
| Plugin list UI | Maintains visible plugin rows, selection/exclusion state, and select/deselect commands. | `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs` |
| Cleaning commands UI | Validates pre-clean conditions, starts dry-run or cleaning workflows, handles stop/force-stop prompts, and opens secondary workflows. | `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` |
| Runtime state hub | Stores immutable `AppState`, publishes Rx streams for state/progress/results, and serializes state mutation through a lock. | `AutoQAC/Services/State/StateService.cs` |
| Cleaning workflow coordinator | Owns end-to-end session flow: config flush, validation, game detection, skip lists, optional backups, sequential xEdit launches, log parsing, result finalization, stopping, and hang monitoring. | `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` |
| Per-plugin cleaner | Builds commands and delegates process execution for a single plugin. | `AutoQAC/Services/Cleaning/CleaningService.cs` |
| xEdit/MO2 command builder | Creates direct xEdit or MO2-wrapped `ProcessStartInfo` with argument-list based quoting. | `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs` |
| Process execution | Enforces a single xEdit process slot, tracks PIDs, waits with timeout/cancellation, and performs graceful/force termination. | `AutoQAC/Services/Process/ProcessExecutionService.cs` |
| Configuration persistence | Reads YAML config, debounces user-config saves, flushes pending saves before cleaning, publishes config/skip-list changes, and caches main configuration. | `AutoQAC/Services/Configuration/ConfigurationService.cs` |
| Plugin discovery | Uses Mutagen for supported games and file-based loading for unsupported games. | `AutoQAC/Services/Plugin/PluginLoadingService.cs` |
| Game detection | Maps xEdit executable names and load-order master files to `GameType`; detects TTW and Enderal variants. | `AutoQAC/Services/GameDetection/GameDetectionService.cs` |
| QueryPlugins library | Performs Mutagen-backed issue analysis through ITM and game-specific detectors. | `QueryPlugins/PluginQueryService.cs` |

## Pattern Overview

**Overall:** Avalonia MVVM application with DI-managed services, Rx-style state notifications, and a service-oriented orchestration layer.

**Key Characteristics:**
- Use strict MVVM boundaries: put view-specific dialog/window operations in `AutoQAC/Views/*.axaml.cs`, not in `AutoQAC/ViewModels/*`.
- Use constructor injection through `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`; register new services by interface in the relevant `Add*` extension.
- Treat `IStateService` plus immutable `AppState` as the shared runtime state hub; mutate state through `StateService.UpdateState` or focused state methods.
- Keep cleaning sequential. `CleaningOrchestrator.StartCleaningAsync` loops plugins one at a time, and `ProcessExecutionService` hard-limits process execution with a single `SemaphoreSlim` slot.
- Keep long-running I/O and process work async. UI-facing state changes from service streams are marshaled through `IUiDispatcher` before ViewModels update bound properties.

## Layers

**Presentation / Views:**
- Purpose: Render Avalonia windows and own platform UI operations such as dialogs, child windows, and window lifetime events.
- Location: `AutoQAC/Views/`
- Contains: `.axaml` markup and `.axaml.cs` code-behind such as `AutoQAC/Views/MainWindow.axaml` and `AutoQAC/Views/MainWindow.axaml.cs`.
- Depends on: ViewModels and UI/application services resolved by DI.
- Used by: Avalonia desktop lifetime in `AutoQAC/App.axaml.cs`.

**ViewModels:**
- Purpose: Expose bindable state and commands using CommunityToolkit.Mvvm source generators.
- Location: `AutoQAC/ViewModels/`
- Contains: `ViewModelBase`, main-window sub-ViewModels, dialog ViewModels, and progress/result ViewModels.
- Depends on: service interfaces in `AutoQAC/Services/*` and model records in `AutoQAC/Models/`.
- Used by: Views through `DataContext` and the `AutoQAC/ViewLocator.cs` ViewModel-to-View convention.

**Application services:**
- Purpose: Implement business workflows, process control, config persistence, plugin discovery, validation, backups, logging, monitoring, and UI abstraction.
- Location: `AutoQAC/Services/`
- Contains: grouped service families under `Backup`, `Cleaning`, `Configuration`, `GameDetection`, `MO2`, `Monitoring`, `Plugin`, `Process`, `State`, and `UI`.
- Depends on: models, external packages, filesystem/process APIs, and other service interfaces.
- Used by: ViewModels, `App.axaml.cs`, and other services through DI.

**State model:**
- Purpose: Centralize runtime state, progress, plugin selections, and session results.
- Location: `AutoQAC/Models/AppState.cs` and `AutoQAC/Services/State/StateService.cs`
- Contains: immutable `AppState`, backup-operation state, Rx subjects, result streams, and state mutation helpers.
- Depends on: domain models in `AutoQAC/Models/`.
- Used by: almost every UI and orchestration component.

**Domain models:**
- Purpose: Provide records/enums for game types, plugin info, cleaning results, backup metadata, validation errors, configuration, and tracked processes.
- Location: `AutoQAC/Models/`
- Contains: `PluginInfo`, `CleaningSessionResult`, `PluginCleaningResult`, `TerminationResult`, `GameType`, `GameVariant`, and `Configuration/*`.
- Depends on: only framework/library types where needed.
- Used by: ViewModels, services, and tests.

**QueryPlugins analysis library:**
- Purpose: Standalone Mutagen-based detector library for ITM/deleted-reference/deleted-navmesh analysis.
- Location: `QueryPlugins/`
- Contains: `PluginQueryService`, detector interfaces, shared detector code, game-specific detectors, and analysis result models.
- Depends on: Mutagen NuGet packages.
- Used by: `AutoQAC` through project reference and by `QueryPlugins.Tests`.

**Tests:**
- Purpose: Validate application services, models, ViewModels, view lifecycle behavior, integration flows, and QueryPlugins detectors.
- Location: `AutoQAC.Tests/`, `QueryPlugins.Tests/`
- Contains: xUnit test projects, test infrastructure, and a test process helper project.
- Depends on: app/library projects, xUnit, FluentAssertions, NSubstitute, coverlet.
- Used by: `dotnet test AutoQACSharp.slnx`.

## Data Flow

### Primary Cleaning Request Path

1. User invokes the Start Cleaning command through the bound command generated from `StartCleaningAsync` (`AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs:116`).
2. `CleaningCommandsViewModel` validates UI/config preconditions and opens the progress window through `ShowProgressInteraction` (`AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs:122`, `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs:133`).
3. `MainWindow` handles the interaction and creates `ProgressWindow` plus `ProgressViewModel` (`AutoQAC/Views/MainWindow.axaml.cs:129`).
4. `CleaningCommandsViewModel` calls `ICleaningOrchestrator.StartCleaningAsync` with timeout/backup callbacks (`AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs:136`).
5. `CleaningOrchestrator` cleans orphaned processes, flushes pending config saves, validates environment, detects game and variant, loads skip lists, filters selections, and starts stateful cleaning (`AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:80`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:85`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:88`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:130`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:174`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:225`).
6. For each plugin, optional backup runs first, then `CleaningService.CleanPluginAsync` launches one xEdit process (`AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:265`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:284`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:409`).
7. `CleaningService` builds an xEdit/MO2 command and delegates execution to `ProcessExecutionService.ExecuteAsync` (`AutoQAC/Services/Cleaning/CleaningService.cs:54`, `AutoQAC/Services/Cleaning/CleaningService.cs:84`).
8. `ProcessExecutionService` acquires the single process slot, starts xEdit/MO2, tracks PID evidence, waits for exit or timeout/cancellation, and releases the slot (`AutoQAC/Services/Process/ProcessExecutionService.cs:20`, `AutoQAC/Services/Process/ProcessExecutionService.cs:46`, `AutoQAC/Services/Process/ProcessExecutionService.cs:64`, `AutoQAC/Services/Process/ProcessExecutionService.cs:76`, `AutoQAC/Services/Process/ProcessExecutionService.cs:101`, `AutoQAC/Services/Process/ProcessExecutionService.cs:153`).
9. `CleaningOrchestrator` reads xEdit logs by captured offsets, parses statistics, produces `PluginCleaningResult`, and emits result/progress through `IStateService` (`AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:404`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:463`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:475`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:502`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:517`).
10. At session end, `CleaningOrchestrator` writes backup metadata/retention cleanup if needed, emits `CleaningSessionResult`, and logs the summary (`AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:527`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:549`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:559`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:560`).

### Plugin Discovery / Configuration Path

1. Startup creates `ConfigurationService`, resolves configuration directory, and starts `ConfigWatcherService` (`AutoQAC/App.axaml.cs:40`, `AutoQAC/App.axaml.cs:81`).
2. `MainWindowViewModel` creates `ConfigurationViewModel` and calls `InitializeAsync` fire-and-forget (`AutoQAC/ViewModels/MainWindowViewModel.cs:51`, `AutoQAC/ViewModels/MainWindowViewModel.cs:69`).
3. When a supported game is selected, `ConfigurationViewModel` saves selection and refreshes plugins (`AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs:151`, `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs:162`, `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs:163`).
4. `PluginLoadingService.TryGetPluginsAsync` uses Mutagen for `SkyrimLe`, `SkyrimSe`, `SkyrimVr`, `Fallout4`, and `Fallout4Vr`; unsupported games return `UnsupportedGame` and use file-based loading (`AutoQAC/Services/Plugin/PluginLoadingService.cs:33`, `AutoQAC/Services/Plugin/PluginLoadingService.cs:90`, `AutoQAC/Services/Plugin/PluginLoadingService.cs:162`).
5. Loaded plugins are placed in shared state through `IStateService.SetPluginsToClean`; the parent `MainWindowViewModel` dispatches `OnStateChanged` to child ViewModels on the UI thread (`AutoQAC/Services/State/StateService.cs:82`, `AutoQAC/ViewModels/MainWindowViewModel.cs:64`, `AutoQAC/ViewModels/MainWindowViewModel.cs:72`).

### Dry-Run Preview Path

1. User invokes `PreviewAsync` from the command ViewModel (`AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs:161`).
2. The same pre-clean validation path runs, then `ICleaningOrchestrator.RunDryRunAsync` is called (`AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs:167`, `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs:179`).
3. Dry-run flushes config, detects game without mutating state, evaluates skip lists, selection exclusions, MO2 mode, and file validation (`AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:972`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:979`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:1010`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:1024`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:1045`).
4. Results are shown through `ShowPreviewInteraction` by reusing the progress surface in preview mode (`AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs:181`, `AutoQAC/Views/MainWindow.axaml.cs:165`).

### Stop / Termination Path

1. User invokes `StopCleaningAsync` from `CleaningCommandsViewModel` (`AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs:209`).
2. `CleaningOrchestrator.StopCleaningAsync` marks termination state, cancels the session CTS, and asks `ProcessExecutionService` for graceful termination of the current process (`AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:822`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:823`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:837`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:866`).
3. If the grace period expires, the ViewModel prompts the user before force termination; a second stop click escalates immediately (`AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs:216`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:815`).
4. `ForceStopCleaningAsync` kills the process tree through `ProcessExecutionService.TerminateProcessAsync(forceKill: true)` (`AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:891`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:933`).

**State Management:**
- Use `StateService.UpdateState` to produce new immutable `AppState` values. It updates `_currentState` inside a lock and calls `BehaviorSubject.OnNext` outside the lock to avoid subscriber deadlocks (`AutoQAC/Services/State/StateService.cs:60`).
- UI state subscribers must be dispatched to the UI thread through `IUiDispatcher` before touching bindable collections or properties (`AutoQAC/ViewModels/MainWindowViewModel.cs:64`).
- Store plugin deselection as exclusions in `AppState.ExcludedPluginPaths`; do not store a duplicate selected-list model (`AutoQAC/Models/AppState.cs:79`).

## Key Abstractions

**Service interfaces:**
- Purpose: Decouple ViewModels and orchestrators from concrete implementations and enable tests/substitutes.
- Examples: `AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs`, `AutoQAC/Services/State/IStateService.cs`, `AutoQAC/Services/Process/IProcessExecutionService.cs`, `AutoQAC/Services/Configuration/IConfigurationService.cs`.
- Pattern: Register interface-to-implementation pairs in `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` and inject interfaces into consumers.

**Interactions:**
- Purpose: Allow ViewModels to request windows/dialogs without directly referencing controls.
- Examples: `AutoQAC/Services/UI/Interactions/Interaction.cs`, `AutoQAC/Services/UI/Interactions/Unit.cs`, `AutoQAC/ViewModels/MainWindowViewModel.cs`, `AutoQAC/Views/MainWindow.axaml.cs`.
- Pattern: Define an `Interaction<TInput, TOutput>` on the owning ViewModel, register a handler in the view code-behind, and return results asynchronously.

**AppState:**
- Purpose: Immutable snapshot of runtime state, paths, selected game, progress, plugins, backup operation, and result sets.
- Examples: `AutoQAC/Models/AppState.cs`, `AutoQAC/Services/State/StateService.cs`.
- Pattern: Update by `with` expressions through `StateService`; publish derived streams for progress, configuration validity, processed plugins, detailed results, and completion.

**Cleaning orchestrator:**
- Purpose: Coordinate many services into one safe session flow while preserving sequential xEdit execution and cancellation/termination semantics.
- Examples: `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`, `AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs`.
- Pattern: Keep workflow policy here; keep single-plugin command/process details in `CleaningService`, `XEditCommandBuilder`, and `ProcessExecutionService`.

**Process execution contract:**
- Purpose: Isolate process launch, PID tracking, timeout, cancellation, graceful termination, and force kill behavior.
- Examples: `AutoQAC/Services/Process/ProcessExecutionService.cs`, `AutoQAC/Models/TerminationResult.cs`, `AutoQAC/Models/TrackedProcess.cs`.
- Pattern: Always go through `IProcessExecutionService.ExecuteAsync`; never start xEdit directly from ViewModels or other services.

**Plugin loading strategy:**
- Purpose: Support Mutagen-backed load orders for supported games and file-based fallbacks for Fallout 3, Fallout New Vegas, and Oblivion.
- Examples: `AutoQAC/Services/Plugin/PluginLoadingService.cs`, `AutoQAC/Services/Plugin/PluginValidationService.cs`.
- Pattern: Query `IPluginLoadingService.IsGameSupportedByMutagen` before deciding whether a load-order file is required.

**QueryPlugins detector registry:**
- Purpose: Consolidate generic ITM detection and game-specific deleted-reference/navmesh detectors.
- Examples: `QueryPlugins/PluginQueryService.cs`, `QueryPlugins/Detectors/ItmDetector.cs`, `QueryPlugins/Detectors/Games/SkyrimDetector.cs`.
- Pattern: Add a new detector implementing `IGameSpecificDetector`, then include it in the default `PluginQueryService` constructor.

## Entry Points

**Desktop application:**
- Location: `AutoQAC/Program.cs`
- Triggers: Windows desktop process launch.
- Responsibilities: Create Avalonia app builder and start classic desktop lifetime.

**Application initialization:**
- Location: `AutoQAC/App.axaml.cs`
- Triggers: Avalonia framework initialization.
- Responsibilities: Build DI container, enforce single instance, create `MainWindow`, start watchers, run startup maintenance, and dispose resources.

**Main cleaning workflow:**
- Location: `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- Triggers: `CleaningCommandsViewModel.StartCleaningAsync`.
- Responsibilities: Run complete safe cleaning session from validation through result completion.

**Dry-run workflow:**
- Location: `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- Triggers: `CleaningCommandsViewModel.PreviewAsync`.
- Responsibilities: Evaluate which plugins will clean or skip without launching xEdit.

**Configuration watcher:**
- Location: `AutoQAC/Services/Configuration/ConfigWatcherService.cs`
- Triggers: Started during app initialization.
- Responsibilities: Watch YAML config for external edits and trigger reload/update behavior.

**QueryPlugins library entry:**
- Location: `QueryPlugins/PluginQueryService.cs`
- Triggers: Consumers call `Analyse(IModGetter, ILinkCache, GameRelease)`.
- Responsibilities: Run ITM and game-specific detectors and return `PluginAnalysisResult`.

## Architectural Constraints

- **Threading:** Avalonia UI updates must occur on the UI thread; `MainWindowViewModel` marshals `IStateService.StateChanged` through `IUiDispatcher` before child ViewModels update bound state (`AutoQAC/ViewModels/MainWindowViewModel.cs:64`).
- **Sequential cleaning:** Never parallelize plugin cleaning. The orchestrator loops sequentially (`AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:265`) and process execution uses one slot (`AutoQAC/Services/Process/ProcessExecutionService.cs:20`).
- **Process safety:** xEdit and MO2 launches must go through `XEditCommandBuilder`, `CleaningService`, and `ProcessExecutionService`; do not launch process APIs from UI code (`AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`, `AutoQAC/Services/Cleaning/CleaningService.cs`, `AutoQAC/Services/Process/ProcessExecutionService.cs`).
- **Global state:** Shared mutable state is centralized in singleton services: `StateService` (`AutoQAC/Services/State/StateService.cs`), `ConfigurationService` (`AutoQAC/Services/Configuration/ConfigurationService.cs`), and process/session tracking services under `AutoQAC/Services/Process/`.
- **Configuration flush:** Always call `IConfigurationService.FlushPendingSavesAsync` before launching xEdit or evaluating dry-run state (`AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:85`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:973`).
- **MO2 mode:** MO2 mode wraps xEdit with `ModOrganizer.exe run`, skips plugin file-existence validation and backup behavior, and relies on MO2 virtual filesystem resolution (`AutoQAC/Services/Cleaning/XEditCommandBuilder.cs:35`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:190`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:260`).
- **Mutagen boundary:** Use Mutagen packages from NuGet and docs/reference as needed; do not modify or build files under the `Mutagen/` submodule. The application source references Mutagen from `AutoQAC/AutoQAC.csproj` and `QueryPlugins/QueryPlugins.csproj`.
- **Windows assumptions:** The app targets `net10.0-windows10.0.19041.0`, uses Windows process behavior, registry probing, executable paths, and desktop lifetime (`AutoQAC/AutoQAC.csproj`, `AutoQAC/Services/Plugin/PluginLoadingService.cs`, `AutoQAC/Services/Process/ProcessExecutionService.cs`).
- **Circular imports:** Not detected from inspected application structure. Keep dependency direction from Views → ViewModels → Services → Models, with service-to-service dependencies expressed through interfaces.

## Anti-Patterns

### Dialog Logic in ViewModels

**What happens:** A ViewModel directly creates or manipulates Avalonia windows/controls.
**Why it's wrong:** It breaks the MVVM boundary and bypasses the interaction pattern used by `MainWindowViewModel` and `MainWindow`.
**Do this instead:** Add or reuse an `Interaction<TInput, TOutput>` in `AutoQAC/ViewModels/MainWindowViewModel.cs` or a UI service interface, then implement the window/dialog creation in `AutoQAC/Views/MainWindow.axaml.cs`.

### Parallel Plugin Cleaning

**What happens:** Plugins are processed concurrently or multiple xEdit processes are started at once.
**Why it's wrong:** xEdit is treated as single-instance/file-lock sensitive, and `ProcessExecutionService` intentionally exposes only one process slot.
**Do this instead:** Keep the sequential `foreach` policy in `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` and call `IProcessExecutionService.ExecuteAsync` for one plugin at a time.

### Bypassing AppState

**What happens:** Components maintain independent plugin/progress/session state instead of using `IStateService`.
**Why it's wrong:** UI surfaces subscribe to `IStateService` streams, so duplicate state creates stale selections, progress mismatches, or missed completion events.
**Do this instead:** Store shared runtime state in `AutoQAC/Models/AppState.cs` and update it through `AutoQAC/Services/State/StateService.cs` methods.

### Direct Process Launch from Workflow/UI Code

**What happens:** New code calls `System.Diagnostics.Process.Start` outside the process service.
**Why it's wrong:** It skips PID tracking, timeout, cancellation, single-slot enforcement, orphan cleanup, and termination safety.
**Do this instead:** Build the `ProcessStartInfo` in `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs` and launch through `AutoQAC/Services/Process/ProcessExecutionService.cs`.

### Treating All Games as File-Based Load Orders

**What happens:** New code requires a `plugins.txt` for every game.
**Why it's wrong:** `PluginLoadingService` uses Mutagen-backed discovery for Skyrim LE/SE/VR and Fallout 4/VR.
**Do this instead:** Check `IPluginLoadingService.IsGameSupportedByMutagen` in `AutoQAC/Services/Plugin/PluginLoadingService.cs` before requiring a load-order path.

## Error Handling

**Strategy:** Catch exceptions at workflow and UI command boundaries, log technical details through `ILoggingService`, return domain result objects where possible, and surface user-actionable messages through `IMessageDialogService` or validation collections.

**Patterns:**
- UI command boundaries catch `InvalidOperationException` separately for configuration validation and display `ValidationError` entries (`AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs:139`).
- Long-running workflow catches `OperationCanceledException` as a non-error and preserves partial results (`AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:562`).
- Services log and return safe failed result objects for recoverable per-plugin failures (`AutoQAC/Services/Cleaning/CleaningService.cs:132`).
- Startup background work catches/logs exceptions and uses warning banners for migration issues (`AutoQAC/App.axaml.cs:140`, `AutoQAC/App.axaml.cs:154`).
- Configuration persistence retries writes and falls back to last known good config on repeated failure (`AutoQAC/Services/Configuration/ConfigurationService.cs:235`).

## Cross-Cutting Concerns

**Logging:** Use `ILoggingService` registered in `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`; concrete logging lives under `AutoQAC/Infrastructure/Logging/`. Startup logs session diagnostics in `AutoQAC/App.axaml.cs`.

**Validation:** UI pre-clean validation belongs in `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`; environment and plugin validation belongs in services such as `AutoQAC/Services/Cleaning/CleaningService.cs`, `AutoQAC/Services/Plugin/PluginValidationService.cs`, and `AutoQAC/Services/MO2/MO2ValidationService.cs`.

**Authentication:** Not applicable; this is a local desktop application with filesystem/process integrations.

**Configuration:** YAML config lives in `AutoQAC Data/AutoQAC Main.yaml` and `AutoQAC Data/AutoQAC Settings.yaml`; runtime config service resolves source config in debug and output config in production (`AutoQAC/Services/Configuration/ConfigurationService.cs:80`).

**External process control:** `AutoQAC/Services/Process/ProcessExecutionService.cs` owns single-slot execution, PID tracking, orphan cleanup, timeout, graceful termination, and force kill.

---

*Architecture analysis: 2026-04-29*
