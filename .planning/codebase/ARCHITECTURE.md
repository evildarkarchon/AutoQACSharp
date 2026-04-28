<!-- refreshed: 2026-04-28 -->
# Architecture

**Analysis Date:** 2026-04-28

## System Overview

```text
┌─────────────────────────────────────────────────────────────┐
│                  Avalonia Desktop UI Layer                   │
├──────────────────┬──────────────────┬───────────────────────┤
│   Views/AXAML    │    ViewModels    │   UI Services          │
│ `AutoQAC/Views`  │ `AutoQAC/ViewModels` │ `AutoQAC/Services/UI` │
└────────┬─────────┴────────┬─────────┴──────────┬────────────┘
         │                  │                     │
         ▼                  ▼                     ▼
┌─────────────────────────────────────────────────────────────┐
│                    Application Service Layer                 │
│  `AutoQAC/Services/Cleaning`, `Plugin`, `Configuration`,     │
│  `GameDetection`, `Process`, `Backup`, `Monitoring`, `MO2`  │
└────────┬──────────────────────┬─────────────────────────────┘
         │                      │
         ▼                      ▼
┌─────────────────────────────┐  ┌─────────────────────────────┐
│ Shared Runtime State         │  │ Plugin Analysis Library      │
│ `AutoQAC/Services/State`     │  │ `QueryPlugins/`              │
│ `AutoQAC/Models/AppState.cs` │  │ Mutagen-backed detectors     │
└────────┬────────────────────┘  └──────────────┬──────────────┘
         │                                      │
         ▼                                      ▼
┌─────────────────────────────────────────────────────────────┐
│       Files, xEdit Processes, Logs, Backups, YAML Config     │
│ `AutoQAC Data/`, xEdit executable, `AutoQAC Backups/`, logs  │
└─────────────────────────────────────────────────────────────┘
```

## Component Responsibilities

| Component | Responsibility | File |
|-----------|----------------|------|
| Avalonia host | Creates the Windows desktop app and enables DEBUG DevTools. | `AutoQAC/Program.cs` |
| Application bootstrapper | Builds the DI container, creates `MainWindow`, starts config watching, migration, log retention, and shutdown cleanup. | `AutoQAC/App.axaml.cs` |
| DI registration | Registers infrastructure, configuration, state, business logic, UI services, ViewModels, and Views. | `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` |
| Main window code-behind | Owns window/dialog interactions and registers ViewModel `Interaction` handlers. | `AutoQAC/Views/MainWindow.axaml.cs` |
| Main window ViewModel | Composes `ConfigurationViewModel`, `PluginListViewModel`, and `CleaningCommandsViewModel`; routes shared state changes. | `AutoQAC/ViewModels/MainWindowViewModel.cs` |
| Configuration ViewModel | Manages path selection, game selection, plugin refresh, skip-list reaction, and autosave triggers. | `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs` |
| Plugin list ViewModel | Maintains visible plugin rows and selection exclusions. | `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs` |
| Cleaning commands ViewModel | Validates pre-clean state, starts preview/cleaning, handles stop escalation prompts, and triggers dialog interactions. | `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` |
| State service | Central mutable state hub with observable streams for app state, progress, detailed plugin results, and completion. | `AutoQAC/Services/State/StateService.cs` |
| Cleaning orchestrator | End-to-end workflow for validation, game/variant detection, skip filtering, backup, sequential xEdit launches, log parsing, stop handling, and session finalization. | `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` |
| Cleaning service | Builds per-plugin commands and delegates process execution. | `AutoQAC/Services/Cleaning/CleaningService.cs` |
| Process execution | Enforces one xEdit process slot, tracks PIDs, handles timeouts, cancellation, graceful termination, and force kill. | `AutoQAC/Services/Process/ProcessExecutionService.cs` |
| Configuration service | Loads/saves YAML configuration, debounces user config writes, flushes pending saves, and serves skip lists. | `AutoQAC/Services/Configuration/ConfigurationService.cs` |
| Plugin loading | Uses Mutagen for supported games and file-based loading for unsupported games. | `AutoQAC/Services/Plugin/PluginLoadingService.cs` |
| Plugin issue approximation | Runs Mutagen-backed `QueryPlugins` analysis for supported games and merges approximate ITM/UDR/navmesh counts into state. | `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs` |
| QueryPlugins service | Coordinates ITM and game-specific detectors and returns consolidated issue counts. | `QueryPlugins/PluginQueryService.cs` |

## Pattern Overview

**Overall:** Layered MVVM desktop application with DI-managed services, observable runtime state, and a separate Mutagen analysis library.

**Key Characteristics:**
- Use constructor injection through `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`; register app services as singletons unless per-window/per-dialog lifetime is needed.
- Keep Views and window/dialog ownership in `AutoQAC/Views/*.axaml.cs`; ViewModels request UI through `Interaction<TInput,TOutput>` or UI services, not direct control manipulation.
- Use `IStateService` (`AutoQAC/Services/State/IStateService.cs`) and immutable `AppState` updates (`AutoQAC/Models/AppState.cs`) for shared runtime state.
- Keep xEdit process execution sequential; `AutoQAC/Services/Process/ProcessExecutionService.cs` uses a `SemaphoreSlim(1, 1)` and `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` iterates plugins one at a time.
- Keep `QueryPlugins/` independent of Avalonia and app state; it accepts Mutagen objects and returns analysis models.

## Layers

**Presentation:**
- Purpose: Render the desktop UI, collect user input, and own window/dialog lifetime.
- Location: `AutoQAC/Views`, `AutoQAC/App.axaml`, `AutoQAC/ViewLocator.cs`
- Contains: AXAML views and code-behind for dialog creation, event subscription cleanup, and Avalonia host integration.
- Depends on: ViewModels, Avalonia controls, UI services for file/message dialogs.
- Used by: `AutoQAC/App.axaml.cs` and Avalonia runtime.

**ViewModel:**
- Purpose: Expose bindable state and commands, react to `IStateService`, and coordinate UI interactions through interaction objects.
- Location: `AutoQAC/ViewModels`, especially `AutoQAC/ViewModels/MainWindow/`
- Contains: CommunityToolkit.Mvvm source-generator properties and relay commands.
- Depends on: Service interfaces under `AutoQAC/Services/**`, models under `AutoQAC/Models`, `IUiDispatcher` for UI-thread marshaling.
- Used by: Views and `AutoQAC/App.axaml.cs`.

**Application Services:**
- Purpose: Implement business workflows, I/O, process execution, configuration, backup, plugin discovery, validation, and game detection.
- Location: `AutoQAC/Services`
- Contains: Interface/implementation pairs such as `ICleaningOrchestrator`/`CleaningOrchestrator`, `IConfigurationService`/`ConfigurationService`, `IProcessExecutionService`/`ProcessExecutionService`.
- Depends on: Models, logging, file system, Mutagen, YamlDotNet, Serilog, Windows registry APIs where needed.
- Used by: ViewModels and other services via DI.

**State:**
- Purpose: Central source of truth for configuration paths, selected game, plugin list, exclusions, cleaning progress, and session results.
- Location: `AutoQAC/Services/State`, `AutoQAC/Models/AppState.cs`
- Contains: `BehaviorSubject<AppState>` and additional observable streams for progress/result notifications.
- Depends on: `System.Reactive`, immutable record updates, frozen sets.
- Used by: ViewModels, cleaning services, configuration/plugin workflows.

**Analysis Library:**
- Purpose: Detect ITMs, deleted references, and deleted navmeshes from Mutagen plugin objects.
- Location: `QueryPlugins/`
- Contains: `PluginQueryService`, `ItmDetector`, game-specific detectors under `QueryPlugins/Detectors/Games`, and result models.
- Depends on: Mutagen only; no Avalonia or AutoQAC UI dependencies.
- Used by: `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs`.

**Tests:**
- Purpose: Validate models, services, ViewModels, integration flows, and library detectors.
- Location: `AutoQAC.Tests/`, `QueryPlugins.Tests/`
- Contains: xUnit tests grouped by `Services`, `ViewModels`, `Models`, `Integration`, and detector areas.
- Depends on: xUnit, FluentAssertions, NSubstitute, coverlet.
- Used by: `dotnet test AutoQACSharp.slnx`.

## Data Flow

### Primary Cleaning Request Path

1. User clicks Start Cleaning in the main UI; the command is handled by `CleaningCommandsViewModel.StartCleaningAsync` (`AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs:117`).
2. Pre-clean validation reads `IStateService.CurrentState` and file system state in `ValidatePreClean` (`AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs:325`).
3. The ViewModel opens the progress UI through `ShowProgressInteraction` and calls `ICleaningOrchestrator.StartCleaningAsync` (`AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs:134`).
4. `CleaningOrchestrator` clears orphan processes, flushes pending config saves, validates environment, detects game and variant, applies skip/exclusion filters, and starts state tracking (`AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:77`).
5. For each plugin, `CleaningOrchestrator` optionally backs up the file, captures xEdit log offsets, then calls `ICleaningService.CleanPluginAsync` sequentially (`AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:262`).
6. `CleaningService` builds an xEdit or MO2-wrapped command via `IXEditCommandBuilder.BuildCommand` and delegates to `IProcessExecutionService.ExecuteAsync` (`AutoQAC/Services/Cleaning/CleaningService.cs:52`).
7. `ProcessExecutionService` acquires the single process slot, launches xEdit, tracks the PID, waits with timeout/cancellation, and untracks the PID (`AutoQAC/Services/Process/ProcessExecutionService.cs:31`).
8. The orchestrator reads only appended xEdit log content by offsets, parses statistics with `XEditOutputParser`, stores detailed per-plugin results in `StateService`, and builds a `CleaningSessionResult` (`AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:405`).
9. `StateService.FinishCleaningWithResults` emits `CleaningCompleted`, which subscribers such as progress/results ViewModels consume (`AutoQAC/Services/State/StateService.cs:218`).

### Plugin Loading and Approximation Flow

1. Game/path changes originate in `ConfigurationViewModel` commands and property-change handlers (`AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs:151`).
2. Mutagen-supported games are loaded by `PluginLoadingService.TryGetPluginsAsync`; Fallout 3, Fallout New Vegas, and Oblivion use `GetPluginsFromFileAsync` (`AutoQAC/Services/Plugin/PluginLoadingService.cs:85`).
3. Skip list status is applied in the configuration ViewModel before the plugin list is pushed into `IStateService.SetPluginsToClean` (`AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs:262`).
4. Supported games can run `PluginIssueApproximationService.GetApproximationsAsync`, which creates a Mutagen load-order/link-cache context and calls `QueryPlugins.PluginQueryService.Analyse` (`AutoQAC/Services/Plugin/PluginIssueApproximationService.cs:43`).
5. Approximation results are merged into `AppState.PluginsToClean` via `StateService.MergePluginApproximations` (`AutoQAC/Services/State/StateService.cs:106`).

### Configuration Persistence Flow

1. ViewModels call `IConfigurationService.SaveUserConfigAsync` or specific setters after user edits (`AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs:217`).
2. `ConfigurationService` clones the config into `_pendingConfig`, emits `UserConfigurationChanged`, and sends a save request to a throttled Rx pipeline (`AutoQAC/Services/Configuration/ConfigurationService.cs:219`).
3. The debounce pipeline writes `AutoQAC Data/AutoQAC Settings.yaml` with retry and last-known-good fallback (`AutoQAC/Services/Configuration/ConfigurationService.cs:235`).
4. Before xEdit launches, `CleaningOrchestrator` calls `FlushPendingSavesAsync` to force disk consistency (`AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:80`).

**State Management:**
- Use `AppState` as an immutable record and update it via `IStateService.UpdateState` (`AutoQAC/Services/State/StateService.cs:60`).
- Emit `BehaviorSubject<AppState>.OnNext` outside the state lock to avoid subscriber deadlocks (`AutoQAC/Services/State/StateService.cs:68`).
- Marshal state changes to the UI thread through `IUiDispatcher` before updating bindable ViewModel state (`AutoQAC/ViewModels/MainWindowViewModel.cs:64`).

## Key Abstractions

**`IStateService`:**
- Purpose: Shared state and event stream hub.
- Examples: `AutoQAC/Services/State/IStateService.cs`, `AutoQAC/Services/State/StateService.cs`, `AutoQAC/Models/AppState.cs`
- Pattern: Immutable state snapshots plus Rx observables.

**`ICleaningOrchestrator`:**
- Purpose: Owns the full session lifecycle and is the only place that sequences plugin cleaning.
- Examples: `AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- Pattern: Application workflow coordinator over smaller service interfaces.

**`IProcessExecutionService`:**
- Purpose: Encapsulates xEdit process launch, timeout, PID tracking, and termination semantics.
- Examples: `AutoQAC/Services/Process/IProcessExecutionService.cs`, `AutoQAC/Services/Process/ProcessExecutionService.cs`
- Pattern: Single-slot async process executor.

**`Interaction<TInput,TOutput>`:**
- Purpose: Lets ViewModels request modal/non-modal UI without direct view references.
- Examples: `AutoQAC/Services/UI/Interactions/Interaction.cs`, `AutoQAC/ViewModels/MainWindowViewModel.cs`, `AutoQAC/Views/MainWindow.axaml.cs`
- Pattern: View-registered async handler abstraction.

**Mutagen detectors:**
- Purpose: Keep plugin issue analysis composable by game and issue type.
- Examples: `QueryPlugins/Detectors/IItmDetector.cs`, `QueryPlugins/Detectors/IGameSpecificDetector.cs`, `QueryPlugins/Detectors/Games/SkyrimDetector.cs`
- Pattern: Detector strategy registry keyed by `GameRelease` in `QueryPlugins/PluginQueryService.cs`.

## Entry Points

**Desktop application:**
- Location: `AutoQAC/Program.cs`
- Triggers: Windows process start.
- Responsibilities: Configure Avalonia, fonts, platform detection, developer tools, and classic desktop lifetime.

**Application initialization:**
- Location: `AutoQAC/App.axaml.cs`
- Triggers: Avalonia framework initialization.
- Responsibilities: Build DI, instantiate main window, start config watcher, run migration and log cleanup, dispose services on shutdown.

**Main UI command entry:**
- Location: `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`
- Triggers: Bound UI commands in `AutoQAC/Views/MainWindow.axaml`.
- Responsibilities: Pre-clean validation, preview, start, stop, settings, skip-list, restore, and about commands.

**QueryPlugins API:**
- Location: `QueryPlugins/IPluginQueryService.cs`, `QueryPlugins/PluginQueryService.cs`
- Triggers: `PluginIssueApproximationService` or library callers with Mutagen `IModGetter` and `ILinkCache` objects.
- Responsibilities: Return consolidated `PluginAnalysisResult` issue counts.

## Architectural Constraints

- **Threading:** The UI runs on Avalonia's STA thread from `AutoQAC/Program.cs`; service I/O and process work should remain async. ViewModel state updates from service observables must be marshaled through `IUiDispatcher` (`AutoQAC/Services/UI/IUiDispatcher.cs`).
- **Sequential cleaning:** Do not parallelize plugin cleaning or xEdit launches. `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` owns the per-plugin loop, and `AutoQAC/Services/Process/ProcessExecutionService.cs` hard-limits execution to one process slot.
- **Global state:** Shared runtime state is centralized in the singleton `StateService` (`AutoQAC/Services/State/StateService.cs`). `PluginQueryService.Default` is a library-level default singleton in `QueryPlugins/PluginQueryService.cs`; prefer constructor injection in app code when substitutability matters.
- **Circular imports:** No source-level circular project references are present in `AutoQACSharp.slnx`; `AutoQAC` references `QueryPlugins`, while `QueryPlugins` does not reference `AutoQAC`.
- **Windows-specific behavior:** Registry probing and executable/process assumptions live in `AutoQAC/Services/Plugin/PluginLoadingService.cs`, `AutoQAC/Services/Process/ProcessExecutionService.cs`, and `AutoQAC/AutoQAC.csproj` (`net10.0-windows10.0.19041.0`).
- **Mutagen submodule:** Treat `Mutagen/` as read-only reference source; use NuGet package references in `AutoQAC/AutoQAC.csproj` and `QueryPlugins/QueryPlugins.csproj` for builds.

## Anti-Patterns

### Parallel xEdit Execution

**What happens:** Starting multiple xEdit processes or cleaning multiple plugins concurrently bypasses `CleaningOrchestrator` and the single-slot process executor.
**Why it's wrong:** xEdit enforces single-instance/file-locking behavior, and AutoQAC's safety model records one current process and one current plugin at a time.
**Do this instead:** Add workflow steps inside `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` and keep process launches delegated to `AutoQAC/Services/Process/ProcessExecutionService.cs`.

### ViewModels Creating Windows Directly

**What happens:** A ViewModel instantiates `Window` classes or manipulates Avalonia controls.
**Why it's wrong:** It breaks MVVM boundaries and bypasses the testable interaction pattern.
**Do this instead:** Add an `Interaction<TInput,TOutput>` to `AutoQAC/ViewModels/MainWindowViewModel.cs` or use an existing UI service, then register the handler in `AutoQAC/Views/MainWindow.axaml.cs`.

### Bypassing Pending Config Flush Before xEdit

**What happens:** Cleaning starts while debounced YAML saves are still pending.
**Why it's wrong:** xEdit launch behavior can diverge from the user's latest settings.
**Do this instead:** Keep `await configService.FlushPendingSavesAsync(ct)` before validation/launch in `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`.

### Mutagen Assumptions for Every Game

**What happens:** New plugin-loading logic assumes every supported game uses Mutagen load order APIs.
**Why it's wrong:** `Fallout3`, `FalloutNewVegas`, and `Oblivion` rely on file-based load-order loading.
**Do this instead:** Check `PluginLoadingService.IsGameSupportedByMutagen` in `AutoQAC/Services/Plugin/PluginLoadingService.cs` and use `GetPluginsFromFileAsync` for unsupported games.

## Error Handling

**Strategy:** Convert expected user/config/process conditions into validation messages or result models; log unexpected failures with context; preserve partial session results during cancellation and failures.

**Patterns:**
- Catch `InvalidOperationException` at command boundaries and surface actionable validation errors in `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`.
- Catch `OperationCanceledException` separately in long-running workflows and treat cancellation as a controlled outcome in `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`.
- Use result models for recoverable service outcomes: `AutoQAC/Models/CleaningResult.cs`, `AutoQAC/Models/PluginLoadingResult.cs`, `AutoQAC/Models/BackupResult.cs`.
- Log process/config/file failures through `ILoggingService` implementations under `AutoQAC/Infrastructure/Logging/`.

## Cross-Cutting Concerns

**Logging:** Serilog-backed `ILoggingService` in `AutoQAC/Infrastructure/Logging/LoggingService.cs`; startup, config, process, backup, hang detection, and cleaning workflows log structured messages.
**Validation:** UI pre-clean validation lives in `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`; service-level environment and plugin validation live in `AutoQAC/Services/Cleaning/CleaningService.cs` and `AutoQAC/Services/Plugin/PluginValidationService.cs`.
**Authentication:** Not applicable; this is a local Windows desktop application with no detected identity provider.
**Configuration:** YAML configuration is loaded from `AutoQAC Data/AutoQAC Main.yaml` and `AutoQAC Data/AutoQAC Settings.yaml` by `AutoQAC/Services/Configuration/ConfigurationService.cs`.
**External processes:** xEdit and MO2 process wrapping are centralized in `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs` and `AutoQAC/Services/Process/ProcessExecutionService.cs`.

---

*Architecture analysis: 2026-04-28*
