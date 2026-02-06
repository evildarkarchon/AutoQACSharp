# Architecture

**Analysis Date:** 2026-02-06

## Pattern Overview

**Overall:** Strict MVVM (Model-View-ViewModel) with Reactive layers and Service-oriented Domain Logic

**Key Characteristics:**
- Three-tier separation: Models (business logic), Services (orchestration), ViewModels (presentation), Views (UI)
- Reactive event-driven communication using ReactiveUI and RxNET observables
- Dependency Injection via Microsoft.Extensions.DependencyInjection
- Sequential (not parallel) plugin processing enforced at service level
- YAML-based configuration system for game/plugin metadata

## Layers

**Models Layer:**
- Purpose: Pure business logic, data structures, domain concepts
- Location: `AutoQAC/Models/`
- Contains: Value objects, domain entities, configuration DTOs
- Depends on: YamlDotNet (for configuration classes)
- Used by: Services and ViewModels
- Files: `AppState.cs` (immutable record), `GameType.cs` (enum), `PluginInfo.cs` (record), `CleaningSessionResult.cs` (record), `CleaningResult.cs` (record), `Configuration/` subdirectory for YAML mapping classes

**Infrastructure Layer:**
- Purpose: Cross-cutting concerns and framework setup
- Location: `AutoQAC/Infrastructure/`
- Contains: Logging service, dependency injection configuration, converters
- Depends on: Serilog (implied), Microsoft.Extensions.DependencyInjection, ReactiveUI
- Used by: All layers
- Key Files: `ServiceCollectionExtensions.cs` (DI registration), `Logging/ILoggingService.cs`, `Logging/LoggingService.cs`

**Services Layer (Business Logic):**
- Purpose: Coordinate domain operations and orchestrate workflows
- Location: `AutoQAC/Services/`
- Depends on: Models, Infrastructure, external process/file I/O
- Used by: ViewModels
- Subdirectories and responsibilities:
  - `Cleaning/` — Clean plugins via xEdit subprocess: `ICleaningService`, `CleaningOrchestrator`, `XEditCommandBuilder`, `XEditOutputParser`
  - `Configuration/` — Load/save YAML configs, provide skip lists and game metadata: `IConfigurationService`
  - `GameDetection/` — Detect game type from xEdit executable path or plugins
  - `Plugin/` — Load plugins from load order file or Mutagen: `IPluginLoadingService`, `IPluginValidationService`
  - `Process/` — Execute subprocesses (xEdit) and capture output: `IProcessExecutionService`
  - `State/` — Central state store with reactive observables: `IStateService`
  - `MO2/` — Validate Mod Organizer 2 integration: `IMO2ValidationService`
  - `UI/` — File dialogs, message dialogs: `IFileDialogService`, `IMessageDialogService`

**ViewModel Layer:**
- Purpose: Presentation logic, command handling, UI state management
- Location: `AutoQAC/ViewModels/`
- Contains: ReactiveCommand definitions, RaiseAndSetIfChanged properties, ObservableAsPropertyHelper computed properties, Interactions for cross-window communication
- Inherits from: `ViewModelBase` which extends `ReactiveObject`
- Depends on: Models, Services, Infrastructure.Logging
- Used by: Views (via DataContext binding)
- Key ViewModels: `MainWindowViewModel.cs` (orchestrator), `ProgressViewModel.cs` (progress display), `SettingsViewModel.cs` (configuration UI), `SkipListViewModel.cs` (skip list management), `CleaningResultsViewModel.cs` (results display)

**View Layer:**
- Purpose: UI rendering only
- Location: `AutoQAC/Views/`
- Contains: XAML files and minimal code-behind for interaction registration
- Code-Behind Pattern: Constructor injection of ViewModel and Services, interaction handler registration (see `MainWindow.axaml.cs` lines 28-54)
- Depends on: ViewModels (data binding), Services (for interaction callbacks)
- Used by: Application root (MainWindow)
- Files: `MainWindow.axaml`, `ProgressWindow.axaml`, `SettingsWindow.axaml`, `SkipListWindow.axaml`, `CleaningResultsWindow.axaml`, `MessageDialog.axaml`, `PartialFormsWarningDialog.axaml`

## Data Flow

**Configuration Loading at Startup:**

1. `App.xaml.cs` calls `OnFrameworkInitializationCompleted()` (line 26)
2. ServiceCollection configured via `ServiceCollectionExtensions.cs` extension methods
3. All services registered as Singletons (except ViewModels as Transient)
4. `MainWindowViewModel` created with all service dependencies
5. Observables subscribed: StateService.StateChanged, StateService.ConfigurationValidChanged, StateService.PluginProcessed
6. User loads game from UI or auto-detection occurs

**Plugin Cleaning Session (Sequential):**

1. MainWindow user clicks "Start Cleaning" → `StartCleaningCommand` fires
2. `MainWindowViewModel.StartCleaningCommand` calls `ICleaningOrchestrator.StartCleaningAsync()`
3. CleaningOrchestrator validates configuration via `IConfigurationService`
4. Detects game type via `IGameDetectionService.DetectFromExecutable()`
5. Loads plugins via `IPluginLoadingService` (Mutagen if supported, fallback to file-based)
6. Applies skip lists from `IConfigurationService.GetSkipListAsync()`
7. Updates state via `IStateService.StartCleaning()` (fires `PluginProcessed` observable)
8. **Sequential loop** (CRITICAL):
   - For each plugin in `AppState.PluginsToClean`:
     - Call `ICleaningService.CleanPluginAsync(plugin)` - **waits for completion**
     - Parse xEdit output via `XEditOutputParser`
     - Update state with `IStateService.AddDetailedCleaningResult()`
     - Emit progress observable
9. Call `IStateService.FinishCleaningWithResults()` with final `CleaningSessionResult`
10. Emit `StateService.CleaningCompleted` observable
11. MainWindowViewModel catches via `ShowCleaningResultsInteraction`
12. MainWindow displays results in `CleaningResultsWindow`

**State Management:**

- Centralized in `IStateService` → `StateService` implementation
- AppState is immutable record with functional updates: `UpdateState(Func<AppState, AppState> updateFunc)`
- Subscribers bind to observables: `IObservable<AppState> StateChanged`, `IObservable<bool> ConfigurationValidChanged`, `IObservable<(int, int)> ProgressChanged`, `IObservable<(string, CleaningStatus)> PluginProcessed`
- LastSessionResult cached: `IStateService.LastSessionResult` property

## Key Abstractions

**IStateService:**
- Purpose: Single source of truth for application state and reactive notifications
- Location: `AutoQAC/Services/State/IStateService.cs`
- Provides: Current AppState, observable streams for state changes, progress, plugin results, session completion
- Implementation: `StateService.cs` uses ReactiveSubject for streams

**ICleaningOrchestrator:**
- Purpose: High-level cleaning workflow (validation → detection → loading → sequential cleaning)
- Location: `AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs`
- Delegates to: ICleaningService, IPluginValidationService, IGameDetectionService, IConfigurationService
- **CRITICAL:** Enforces sequential processing via synchronous foreach loop (see `CleaningOrchestrator.cs` line ~100)

**IConfigurationService:**
- Purpose: YAML config I/O, skip list management, game metadata queries
- Location: `AutoQAC/Services/Configuration/IConfigurationService.cs`
- Loads: MainConfiguration (game skip lists, xEdit executable names) and UserConfiguration (user paths and settings)
- Observables: SkipListChanged, UserConfigurationChanged for reactive UI updates

**IPluginLoadingService:**
- Purpose: Polymorphic plugin loading (Mutagen-first, fallback to file-based)
- Location: `AutoQAC/Services/Plugin/IPluginLoadingService.cs`
- Routes: Mutagen for SkyrimLE, SkyrimSE, SkyrimVR, Fallout4, Fallout4VR; file-based for Oblivion, Fallout3, FalloutNewVegas
- Returns: List<PluginInfo> with IsInSkipList flag pre-populated

**XEditCommandBuilder & XEditOutputParser:**
- Purpose: Build xEdit subprocess command lines and parse output for ITMs/UDRs
- Location: `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`, `XEditOutputParser.cs`
- Features: Handles MO2 wrapper, partial forms flags, timeout handling, output parsing for detailed results

## Entry Points

**Application Entry:**
- Location: `AutoQAC/Program.cs`
- Triggers: `dotnet run` or executable invocation
- Responsibilities: Build Avalonia AppBuilder with ReactiveUI support, configure logging, start classic desktop lifetime

**Framework Initialization:**
- Location: `AutoQAC/App.axaml.cs` → `OnFrameworkInitializationCompleted()`
- Triggers: Avalonia startup after XAML loader initialization
- Responsibilities: Build service provider via DI, instantiate MainWindow with dependencies, assign DataContext

**MainWindow View Resolution:**
- Location: `ViewLocator.cs`
- Pattern: Avalonia auto-resolves ViewModels to Views by naming convention (ViewModelBase → View)
- Manual wiring in App.cs for complex dependencies (line 44-51)

**User Interactions:**
- File path selection: MainWindowViewModel.ConfigureLoadOrderCommand → IFileDialogService.OpenFileDialog()
- Start cleaning: MainWindowViewModel.StartCleaningCommand → ICleaningOrchestrator.StartCleaningAsync()
- Dialog display: MainWindowViewModel interactions (ShowProgressInteraction, ShowCleaningResultsInteraction) → MainWindow interaction handlers

## Error Handling

**Strategy:** Structured exception handling with logging and user-facing error messages via dialogs

**Patterns:**
- ILoggingService.Error(Exception ex, string message) for errors with stack traces
- IMessageDialogService for user-facing error dialogs (non-modal blocking operations)
- CancellationToken support throughout async chains for graceful cancellation
- CleaningStatus enum: Success, Failed, Skipped for per-plugin tracking
- CleaningSessionResult.IsSuccess computed property checks FailedCount == 0 && !WasCancelled

## Cross-Cutting Concerns

**Logging:**
- Service: `ILoggingService` via `Infrastructure/Logging/LoggingService.cs`
- Integration: Injected into ViewModels and Services
- Pattern: Structured logging (Debug, Information, Warning, Error, Fatal levels)
- Output: Rotating logs in `logs/` directory

**Validation:**
- Configuration validation via `IConfigurationService.ValidatePathsAsync()` before cleaning
- Plugin validation via `IPluginValidationService` (checks file existence, readable status)
- Game detection validation: Unknown returned if both xEdit and file-based detection fail

**Authentication & Authorization:**
- None at application level (no user accounts)
- MO2 integration checked via `IMO2ValidationService` for process availability

**Dependency Injection:**
- Bootstrapped in `App.xaml.cs` → `ServiceCollectionExtensions.cs`
- Services registered in dedicated extension methods: `AddInfrastructure()`, `AddConfiguration()`, `AddState()`, `AddBusinessLogic()`, `AddUiServices()`, `AddViewModels()`, `AddViews()`
- Constructor injection enforced; no service locators

**Threading:**
- UI thread: Avalonia dispatcher handles UI updates
- Background threads: `ReactiveCommand.CreateFromTask()` automatically marshals long-running operations
- Cross-thread safety: `IStateService` uses Subject<T> for thread-safe observable emissions

**Resource Cleanup:**
- IDisposable support: `CleaningOrchestrator` implements IDisposable for CancellationTokenSource cleanup
- CompositeDisposable pattern in MainWindowViewModel for subscription cleanup

---

*Architecture analysis: 2026-02-06*
