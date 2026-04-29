# Codebase Structure

**Analysis Date:** 2026-04-29

## Directory Layout

```text
AutoQACSharp/
├── AutoQAC/                    # Avalonia Windows desktop app
│   ├── App.axaml(.cs)          # Application bootstrap and DI composition
│   ├── Program.cs              # Desktop process entry point
│   ├── AutoQAC.csproj          # App project and package references
│   ├── Assets/                 # Avalonia resources
│   ├── AutoQAC Data/           # App-bundled default YAML data copied to output
│   ├── Converters/             # Avalonia binding converters
│   ├── Infrastructure/         # DI registration and logging infrastructure
│   ├── Models/                 # Domain records/enums/config models
│   ├── Services/               # Business, I/O, process, config, state, and UI services
│   ├── ViewModels/             # MVVM ViewModels and main-window sub-ViewModels
│   └── Views/                  # Avalonia windows and code-behind interaction handlers
├── AutoQAC.Tests/              # xUnit tests for AutoQAC app code
│   ├── Integration/            # Integration-flow tests
│   ├── Models/                 # Model tests
│   ├── Services/               # Service tests
│   ├── TestInfrastructure/     # Shared test helpers
│   ├── TestProcessHelper/      # Helper process project for process tests
│   ├── ViewModels/             # ViewModel tests
│   └── Views/                  # View lifecycle tests
├── QueryPlugins/               # Standalone Mutagen-backed plugin analysis library
│   ├── Detectors/              # Generic and game-specific issue detectors
│   ├── Models/                 # Analysis result and issue models
│   └── PluginQueryService.cs   # Library orchestration entry point
├── QueryPlugins.Tests/         # xUnit tests for QueryPlugins
├── AutoQAC Data/               # Root-level source YAML config/data files
├── docs/                       # Project documentation and Mutagen lookup docs
├── openspec/                   # OpenSpec change/spec workflow artifacts
├── prompts/                    # Prompt/reference material
├── Release/                    # Release artifacts or release support files
├── Mutagen/                    # Read-only referenced submodule; do not scan/build/modify
├── .planning/                  # GSD planning and codebase map documents
├── .claude/skills/             # Project-local OpenSpec skills
├── AutoQACSharp.slnx           # Solution containing app, tests, and QueryPlugins projects
├── AGENTS.md                   # Agent guidance for this repository
├── README.md                   # User/developer overview
└── ROADMAP.md                  # Project roadmap
```

## Directory Purposes

**`AutoQAC/`:**
- Purpose: Main Avalonia desktop application for safely running xEdit Quick Auto Clean.
- Contains: App entry point, Avalonia resources, domain models, services, ViewModels, Views, and application project file.
- Key files: `AutoQAC/Program.cs`, `AutoQAC/App.axaml.cs`, `AutoQAC/AutoQAC.csproj`, `AutoQAC/ViewLocator.cs`.

**`AutoQAC/Infrastructure/`:**
- Purpose: Cross-cutting application infrastructure.
- Contains: DI registration and logging abstractions/implementation.
- Key files: `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`, `AutoQAC/Infrastructure/Logging/LoggingService.cs`, `AutoQAC/Infrastructure/Logging/ILoggingService.cs`.

**`AutoQAC/Services/`:**
- Purpose: Business and platform service layer.
- Contains: Service families grouped by responsibility: `Backup`, `Cleaning`, `Configuration`, `GameDetection`, `MO2`, `Monitoring`, `Plugin`, `Process`, `State`, and `UI`.
- Key files: `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`, `AutoQAC/Services/Process/ProcessExecutionService.cs`, `AutoQAC/Services/State/StateService.cs`, `AutoQAC/Services/Configuration/ConfigurationService.cs`.

**`AutoQAC/Services/Backup/`:**
- Purpose: Plugin backup, session metadata, backup retention, deletion, and path containment.
- Contains: `BackupService`, file copier, session deleter, options, containment helpers, and interfaces.
- Key files: `AutoQAC/Services/Backup/BackupService.cs`, `AutoQAC/Services/Backup/BackupFileCopier.cs`, `AutoQAC/Services/Backup/BackupPathContainment.cs`.

**`AutoQAC/Services/Cleaning/`:**
- Purpose: End-to-end cleaning orchestration, single-plugin cleaning, xEdit command building, log reading, and output parsing.
- Contains: Orchestrator/service interfaces, `CleaningOrchestrator`, `CleaningService`, `XEditCommandBuilder`, `XEditLogFileService`, `XEditOutputParser`, and stop-result types.
- Key files: `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`, `AutoQAC/Services/Cleaning/CleaningService.cs`, `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`.

**`AutoQAC/Services/Configuration/`:**
- Purpose: YAML config persistence, config watching, legacy migration, and log retention.
- Contains: `ConfigurationService`, `ConfigWatcherService`, `LegacyMigrationService`, `LogRetentionService`, and interfaces.
- Key files: `AutoQAC/Services/Configuration/ConfigurationService.cs`, `AutoQAC/Services/Configuration/ConfigWatcherService.cs`, `AutoQAC/Services/Configuration/LegacyMigrationService.cs`.

**`AutoQAC/Services/GameDetection/`:**
- Purpose: Detect game type from xEdit executable/load-order files and detect variants like TTW and Enderal.
- Contains: `GameDetectionService` and `IGameDetectionService`.
- Key files: `AutoQAC/Services/GameDetection/GameDetectionService.cs`.

**`AutoQAC/Services/MO2/`:**
- Purpose: Validate Mod Organizer 2 configuration and executable paths.
- Contains: `MO2ValidationService` and `IMO2ValidationService`.
- Key files: `AutoQAC/Services/MO2/MO2ValidationService.cs`.

**`AutoQAC/Services/Monitoring/`:**
- Purpose: Monitor xEdit processes for CPU-based hangs.
- Contains: `HangDetectionService` and `IHangDetectionService`.
- Key files: `AutoQAC/Services/Monitoring/HangDetectionService.cs`.

**`AutoQAC/Services/Plugin/`:**
- Purpose: Plugin validation, load-order loading, Mutagen-backed discovery, and issue approximation.
- Contains: `PluginLoadingService`, `PluginValidationService`, `PluginIssueApproximationService`, and interfaces.
- Key files: `AutoQAC/Services/Plugin/PluginLoadingService.cs`, `AutoQAC/Services/Plugin/PluginValidationService.cs`.

**`AutoQAC/Services/Process/`:**
- Purpose: xEdit/MO2 process execution, PID persistence, process-session IDs, single-instance guard, orphan cleanup, and termination.
- Contains: Process execution service, PID store abstractions, session ID provider, default PID path provider, and single-instance guard.
- Key files: `AutoQAC/Services/Process/ProcessExecutionService.cs`, `AutoQAC/Services/Process/JsonPidStore.cs`, `AutoQAC/Services/Process/SingleInstanceGuard.cs`.

**`AutoQAC/Services/State/`:**
- Purpose: Central runtime state and event streams.
- Contains: `StateService` and `IStateService`.
- Key files: `AutoQAC/Services/State/StateService.cs`, `AutoQAC/Services/State/IStateService.cs`, `AutoQAC/Models/AppState.cs`.

**`AutoQAC/Services/UI/`:**
- Purpose: UI abstractions for dispatching, dialogs, file pickers, interactions, and observable callback helpers.
- Contains: `AvaloniaUiDispatcher`, `FileDialogService`, `MessageDialogService`, interaction types, and `CallbackObserver`.
- Key files: `AutoQAC/Services/UI/IUiDispatcher.cs`, `AutoQAC/Services/UI/AvaloniaUiDispatcher.cs`, `AutoQAC/Services/UI/Interactions/Interaction.cs`.

**`AutoQAC/Models/`:**
- Purpose: Domain types shared between services, ViewModels, and tests.
- Contains: State, plugin, cleaning, backup, game, validation, process, and configuration models.
- Key files: `AutoQAC/Models/AppState.cs`, `AutoQAC/Models/PluginInfo.cs`, `AutoQAC/Models/CleaningSessionResult.cs`, `AutoQAC/Models/Configuration/UserConfiguration.cs`.

**`AutoQAC/ViewModels/`:**
- Purpose: Bindable MVVM state and commands.
- Contains: Main-window orchestrator ViewModel, child ViewModels, dialog ViewModels, progress/results ViewModels, and base ViewModel.
- Key files: `AutoQAC/ViewModels/MainWindowViewModel.cs`, `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`, `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs`, `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`.

**`AutoQAC/ViewModels/MainWindow/`:**
- Purpose: Keep the main window ViewModel split by feature area.
- Contains: `ConfigurationViewModel`, `PluginListViewModel`, `CleaningCommandsViewModel`, and plugin list row model.
- Key files: `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`, `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs`, `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`, `AutoQAC/ViewModels/MainWindow/PluginListItem.cs`.

**`AutoQAC/Views/`:**
- Purpose: Avalonia windows and their code-behind interaction handlers.
- Contains: Main, progress, settings, skip list, restore, results, about, message, and warning dialogs.
- Key files: `AutoQAC/Views/MainWindow.axaml`, `AutoQAC/Views/MainWindow.axaml.cs`, `AutoQAC/Views/ProgressWindow.axaml`, `AutoQAC/Views/SettingsWindow.axaml`.

**`AutoQAC/Converters/`:**
- Purpose: Avalonia binding converters.
- Contains: converter classes used by `.axaml` views.
- Key files: `AutoQAC/Converters/`.

**`QueryPlugins/`:**
- Purpose: Standalone plugin issue analysis library.
- Contains: service entry point, detector interfaces, generic ITM detector, game-specific detectors, and result models.
- Key files: `QueryPlugins/PluginQueryService.cs`, `QueryPlugins/IPluginQueryService.cs`, `QueryPlugins/Detectors/ItmDetector.cs`.

**`QueryPlugins/Detectors/Games/`:**
- Purpose: Game-specific deleted reference/navmesh detection.
- Contains: `SkyrimDetector`, `Fallout4Detector`, `StarfieldDetector`, and `OblivionDetector`.
- Key files: `QueryPlugins/Detectors/Games/SkyrimDetector.cs`, `QueryPlugins/Detectors/Games/Fallout4Detector.cs`.

**`AutoQAC.Tests/`:**
- Purpose: Test coverage for the desktop app.
- Contains: model, service, ViewModel, view lifecycle, integration, and process-helper tests.
- Key files: `AutoQAC.Tests/AutoQAC.Tests.csproj`, `AutoQAC.Tests/TestInfrastructure/`, `AutoQAC.Tests/TestProcessHelper/AutoQAC.TestProcessHelper.csproj`.

**`QueryPlugins.Tests/`:**
- Purpose: Test coverage for the QueryPlugins library.
- Contains: detector and model tests.
- Key files: `QueryPlugins.Tests/QueryPlugins.Tests.csproj`, `QueryPlugins.Tests/Detectors/`, `QueryPlugins.Tests/Models/`.

**`AutoQAC Data/`:**
- Purpose: Source YAML configuration/data used by the app.
- Contains: main and user settings YAML files.
- Key files: `AutoQAC Data/AutoQAC Main.yaml`, `AutoQAC Data/AutoQAC Settings.yaml`.

**`docs/`:**
- Purpose: Developer/user documentation and local reference material.
- Contains: project docs and Mutagen lookup docs.
- Key files: `docs/mutagen/`.

**`openspec/`:**
- Purpose: OpenSpec capability specs and active/archived change artifacts.
- Contains: spec/change workflow files used by project-local OpenSpec skills.
- Key files: `openspec/`.

**`Mutagen/`:**
- Purpose: Referenced upstream submodule for Mutagen source reference only.
- Contains: external repository content.
- Key files: `.gitmodules` declares `Mutagen/`; do not scan, build, modify, or add files under this directory.

## Key File Locations

**Entry Points:**
- `AutoQAC/Program.cs`: Process entry point and Avalonia builder.
- `AutoQAC/App.axaml.cs`: Application startup composition, main window creation, startup tasks, and shutdown cleanup.
- `QueryPlugins/PluginQueryService.cs`: Public analysis entry point for QueryPlugins consumers.

**Configuration:**
- `AutoQACSharp.slnx`: Solution containing `AutoQAC`, `AutoQAC.Tests`, `QueryPlugins`, `QueryPlugins.Tests`, and test helper project.
- `AutoQAC/AutoQAC.csproj`: Desktop app target framework, package references, resources, and project reference to QueryPlugins.
- `QueryPlugins/QueryPlugins.csproj`: Library target framework and Mutagen package references.
- `AutoQAC.Tests/AutoQAC.Tests.csproj`: App test project, coverage collection, and app/library references.
- `QueryPlugins.Tests/QueryPlugins.Tests.csproj`: QueryPlugins test project and coverage collection.
- `AutoQAC Data/AutoQAC Main.yaml`: Root source main configuration.
- `AutoQAC Data/AutoQAC Settings.yaml`: Root source user settings.
- `AutoQAC/AutoQAC Data/`: App project data folder copied to output by `AutoQAC/AutoQAC.csproj`.
- `.gitmodules`: Declares `Mutagen/` as a submodule.

**Core Logic:**
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`: Service registration and lifetimes.
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`: Full cleaning session orchestration.
- `AutoQAC/Services/Cleaning/CleaningService.cs`: Single-plugin cleaning.
- `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`: xEdit and MO2 command construction.
- `AutoQAC/Services/Process/ProcessExecutionService.cs`: Process launch, timeout, PID tracking, and termination.
- `AutoQAC/Services/Configuration/ConfigurationService.cs`: YAML config load/save and debounced persistence.
- `AutoQAC/Services/Plugin/PluginLoadingService.cs`: Mutagen/file-based plugin loading.
- `AutoQAC/Services/State/StateService.cs`: Runtime state hub.
- `AutoQAC/Services/GameDetection/GameDetectionService.cs`: Game and variant detection.
- `QueryPlugins/PluginQueryService.cs`: Issue detector orchestration.

**UI Logic:**
- `AutoQAC/ViewModels/MainWindowViewModel.cs`: Main window ViewModel composition and interactions.
- `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`: Configuration/game/plugin refresh UI state.
- `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs`: Plugin list selection state.
- `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`: Start/preview/stop commands and validation messages.
- `AutoQAC/Views/MainWindow.axaml.cs`: Dialog/window interaction handlers.
- `AutoQAC/Views/MainWindow.axaml`: Main window layout.

**Models:**
- `AutoQAC/Models/AppState.cs`: Runtime state snapshot and backup-operation state.
- `AutoQAC/Models/PluginInfo.cs`: Plugin metadata used by loading, UI, and cleaning.
- `AutoQAC/Models/CleaningSessionResult.cs`: Session summary model.
- `AutoQAC/Models/PluginCleaningResult.cs`: Per-plugin result model.
- `AutoQAC/Models/Configuration/UserConfiguration.cs`: User settings model.
- `QueryPlugins/Models/PluginAnalysisResult.cs`: QueryPlugins analysis result.
- `QueryPlugins/Models/PluginIssue.cs`: QueryPlugins issue model.

**Testing:**
- `AutoQAC.Tests/`: App test root.
- `AutoQAC.Tests/TestInfrastructure/`: Shared testing helpers and fakes.
- `AutoQAC.Tests/TestProcessHelper/`: Helper executable/library used by process tests.
- `QueryPlugins.Tests/`: QueryPlugins test root.

## Naming Conventions

**Files:**
- Service implementations use `[Name]Service.cs`: `AutoQAC/Services/Configuration/ConfigurationService.cs`, `AutoQAC/Services/Plugin/PluginLoadingService.cs`.
- Service interfaces use `I[Name]Service.cs`: `AutoQAC/Services/Configuration/IConfigurationService.cs`, `AutoQAC/Services/State/IStateService.cs`.
- ViewModels use `[WindowOrFeature]ViewModel.cs`: `AutoQAC/ViewModels/SettingsViewModel.cs`, `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs`.
- Avalonia views use `[WindowOrDialog].axaml` with optional `[WindowOrDialog].axaml.cs`: `AutoQAC/Views/ProgressWindow.axaml`, `AutoQAC/Views/ProgressWindow.axaml.cs`.
- Models use domain nouns: `AutoQAC/Models/PluginInfo.cs`, `AutoQAC/Models/DryRunResult.cs`, `AutoQAC/Models/TerminationResult.cs`.
- Tests mirror the production area under `AutoQAC.Tests/Services/`, `AutoQAC.Tests/ViewModels/`, `AutoQAC.Tests/Models/`, and `QueryPlugins.Tests/Detectors/`.

**Directories:**
- Service directories are PascalCase responsibility areas under `AutoQAC/Services/`: `Cleaning`, `Process`, `Configuration`, `Plugin`.
- ViewModel feature splits live under `AutoQAC/ViewModels/MainWindow/` when they are part of the main window.
- QueryPlugins detector implementations are separated into generic detectors at `QueryPlugins/Detectors/` and game-specific detectors at `QueryPlugins/Detectors/Games/`.
- Build artifacts (`bin/`, `obj/`, `TestResults/`) are generated and should not receive source files.

## Where to Add New Code

**New UI command on the main window:**
- Primary code: add command state/logic to the relevant child ViewModel in `AutoQAC/ViewModels/MainWindow/`.
- Dialog/window handling: add an interaction to `AutoQAC/ViewModels/MainWindowViewModel.cs` and implement it in `AutoQAC/Views/MainWindow.axaml.cs`.
- Markup: update `AutoQAC/Views/MainWindow.axaml`.
- Tests: add ViewModel tests under `AutoQAC.Tests/ViewModels/` and view lifecycle tests under `AutoQAC.Tests/Views/` if interaction disposal/lifetime changes.

**New application service:**
- Interface: place `I[Name]Service.cs` in the matching `AutoQAC/Services/<Area>/` directory.
- Implementation: place `[Name]Service.cs` in the same directory.
- Registration: add it to `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` in the appropriate `Add*` method.
- Tests: add tests under `AutoQAC.Tests/Services/<Area>/`.

**New cleaning workflow behavior:**
- Session policy: update `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`.
- Single-plugin process behavior: update `AutoQAC/Services/Cleaning/CleaningService.cs` or `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`.
- Process lifecycle/termination behavior: update `AutoQAC/Services/Process/ProcessExecutionService.cs`.
- Tests: add focused tests under `AutoQAC.Tests/Services/Cleaning/` or `AutoQAC.Tests/Services/Process/`.

**New runtime state:**
- Model: add immutable property to `AutoQAC/Models/AppState.cs` or a focused model in `AutoQAC/Models/`.
- Mutation: add helper methods to `AutoQAC/Services/State/IStateService.cs` and `AutoQAC/Services/State/StateService.cs` when many callers need the update.
- UI consumption: dispatch state changes in `AutoQAC/ViewModels/MainWindowViewModel.cs` and apply state in the relevant child ViewModel.
- Tests: add state and ViewModel tests under `AutoQAC.Tests/Services/State/` and `AutoQAC.Tests/ViewModels/`.

**New configuration setting:**
- Model: update `AutoQAC/Models/Configuration/UserConfiguration.cs` or related config models in `AutoQAC/Models/Configuration/`.
- Persistence/defaults: update `AutoQAC/Services/Configuration/ConfigurationService.cs` and YAML defaults in `AutoQAC Data/` and `AutoQAC/AutoQAC Data/` when applicable.
- UI: update `AutoQAC/ViewModels/SettingsViewModel.cs`, `AutoQAC/Views/SettingsWindow.axaml`, or `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs` depending on where the setting is edited.
- Tests: add config persistence and ViewModel tests under `AutoQAC.Tests/Services/Configuration/` and `AutoQAC.Tests/ViewModels/`.

**New plugin loading behavior:**
- Mutagen-supported loading: update `AutoQAC/Services/Plugin/PluginLoadingService.cs` and add any required Mutagen package references to `AutoQAC/AutoQAC.csproj`.
- File-based validation/loading: update `AutoQAC/Services/Plugin/PluginValidationService.cs`.
- Game mapping/detection: update `AutoQAC/Models/GameType.cs` and `AutoQAC/Services/GameDetection/GameDetectionService.cs`.
- Tests: add tests under `AutoQAC.Tests/Services/Plugin/` and `AutoQAC.Tests/Services/GameDetection/`.

**New QueryPlugins detector:**
- Interface/implementation: add the detector under `QueryPlugins/Detectors/` or `QueryPlugins/Detectors/Games/`.
- Registry: wire it into `QueryPlugins/PluginQueryService.cs` if it should be part of the default analyzer.
- Models: add result/issue types under `QueryPlugins/Models/` only when existing `PluginIssue`/`IssueType` is insufficient.
- Tests: add tests under `QueryPlugins.Tests/Detectors/`.

**New backup behavior:**
- Service logic: update `AutoQAC/Services/Backup/BackupService.cs`, `AutoQAC/Services/Backup/BackupFileCopier.cs`, or `AutoQAC/Services/Backup/DirectoryBackupSessionDeleter.cs`.
- Session/progress models: update `AutoQAC/Models/BackupSession.cs`, `AutoQAC/Models/BackupResult.cs`, or `AutoQAC/Models/AppState.cs`.
- Orchestration: update `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` only when backup timing or session policy changes.
- Tests: add tests under `AutoQAC.Tests/Services/Backup/`.

**Utilities:**
- Shared UI utility: use `AutoQAC/Services/UI/` when it abstracts Avalonia or dispatching behavior.
- Domain helper: place near the owning service area, e.g. `AutoQAC/Services/Backup/BackupPathContainment.cs`.
- Cross-cutting infrastructure helper: use `AutoQAC/Infrastructure/`.

## Special Directories

**`Mutagen/`:**
- Purpose: External referenced Mutagen repository for source reference only.
- Generated: No, but external/submodule-managed.
- Committed: As a submodule reference via `.gitmodules`.
- Rule: Do not scan broadly, build, modify, or add files under `Mutagen/`.

**`AutoQAC/bin/`, `AutoQAC/obj/`, `QueryPlugins/bin/`, `QueryPlugins/obj/`, `AutoQAC.Tests/bin/`, `AutoQAC.Tests/obj/`, `QueryPlugins.Tests/bin/`, `QueryPlugins.Tests/obj/`:**
- Purpose: .NET build outputs and intermediates.
- Generated: Yes.
- Committed: No.

**`AutoQAC.Tests/TestResults/`, `QueryPlugins.Tests/TestResults/`:**
- Purpose: coverlet/xUnit test and Cobertura coverage outputs.
- Generated: Yes.
- Committed: No.

**`.planning/`:**
- Purpose: GSD planning state, roadmap, phase artifacts, and codebase maps.
- Generated: Partially; maintained by planning workflow.
- Committed: Project-dependent planning artifacts.

**`.claude/skills/`:**
- Purpose: Project-local OpenSpec skills that define workflow commands and constraints.
- Generated: Managed by skill/OpenSpec tooling.
- Committed: Yes when project workflows require shared skills.

**`openspec/`:**
- Purpose: OpenSpec specs, active changes, and archived change artifacts.
- Generated: Managed by OpenSpec workflow.
- Committed: Yes when specs/changes are part of project governance.

**`logs/` and `AutoQAC/logs/`:**
- Purpose: Runtime log output locations.
- Generated: Yes.
- Committed: No.

---

*Structure analysis: 2026-04-29*
