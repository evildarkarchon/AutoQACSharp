# Codebase Structure

**Analysis Date:** 2026-04-28

## Directory Layout

```text
AutoQACSharp/
├── .claude/skills/          # Project-local OpenSpec agent skills
├── .opencode/skills/        # Alternate runtime copies of OpenSpec skills
├── .planning/               # GSD planning state and generated codebase maps
├── AutoQAC/                 # Avalonia Windows desktop app
├── AutoQAC.Tests/           # xUnit tests for the desktop app
├── QueryPlugins/            # Mutagen-based plugin issue analysis library
├── QueryPlugins.Tests/      # xUnit tests for QueryPlugins
├── AutoQAC Data/            # Source YAML defaults copied/used by the app
├── docs/                    # Reference docs, including curated Mutagen docs
├── Mutagen/                 # Read-only Mutagen submodule/reference source
├── openspec/                # OpenSpec changes and specs
├── Release/                 # Release artifacts/scripts
├── AutoQACSharp.slnx        # Solution containing app, library, and test projects
├── AGENTS.md                # Repository guidance for coding agents
└── README.md                # User/project documentation
```

## Directory Purposes

**`AutoQAC/`:**
- Purpose: Windows-only Avalonia desktop application for safe xEdit Quick Auto Clean automation.
- Contains: App bootstrap, AXAML views, ViewModels, services, models, converters, assets, and app-local data.
- Key files: `AutoQAC/AutoQAC.csproj`, `AutoQAC/Program.cs`, `AutoQAC/App.axaml.cs`, `AutoQAC/App.axaml`

**`AutoQAC/Infrastructure/`:**
- Purpose: DI wiring and infrastructure services.
- Contains: Service registration extensions and logging adapter classes.
- Key files: `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`, `AutoQAC/Infrastructure/Logging/LoggingService.cs`, `AutoQAC/Infrastructure/Logging/ILoggingService.cs`, `AutoQAC/Infrastructure/Logging/LogFilePaths.cs`

**`AutoQAC/Services/`:**
- Purpose: Main business logic and platform integration services.
- Contains: Feature-area subdirectories for backup, cleaning, configuration, game detection, MO2, monitoring, plugin handling, process handling, state, and UI services.
- Key files: `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`, `AutoQAC/Services/Process/ProcessExecutionService.cs`, `AutoQAC/Services/Configuration/ConfigurationService.cs`, `AutoQAC/Services/State/StateService.cs`

**`AutoQAC/Services/Backup/`:**
- Purpose: Plugin backup, restore, session metadata, and backup retention.
- Contains: Backup service interface/implementation.
- Key files: `AutoQAC/Services/Backup/IBackupService.cs`, `AutoQAC/Services/Backup/BackupService.cs`

**`AutoQAC/Services/Cleaning/`:**
- Purpose: Cleaning workflow orchestration, xEdit command building, log file reading, output parsing, and cleaning service facade.
- Contains: `ICleaningOrchestrator`, `CleaningOrchestrator`, `ICleaningService`, `CleaningService`, xEdit command/log helpers.
- Key files: `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`, `AutoQAC/Services/Cleaning/CleaningService.cs`, `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`, `AutoQAC/Services/Cleaning/XEditLogFileService.cs`, `AutoQAC/Services/Cleaning/XEditOutputParser.cs`

**`AutoQAC/Services/Configuration/`:**
- Purpose: YAML configuration loading/saving, config file watching, legacy migration, and log retention.
- Contains: Config service interfaces and implementations.
- Key files: `AutoQAC/Services/Configuration/ConfigurationService.cs`, `AutoQAC/Services/Configuration/ConfigWatcherService.cs`, `AutoQAC/Services/Configuration/LegacyMigrationService.cs`, `AutoQAC/Services/Configuration/LogRetentionService.cs`

**`AutoQAC/Services/GameDetection/`:**
- Purpose: Detect base games and game variants from xEdit executable names or load-order master files.
- Contains: Game detection service interface/implementation.
- Key files: `AutoQAC/Services/GameDetection/IGameDetectionService.cs`, `AutoQAC/Services/GameDetection/GameDetectionService.cs`

**`AutoQAC/Services/MO2/`:**
- Purpose: Validate Mod Organizer 2 configuration and support MO2 execution mode.
- Contains: MO2 validation service interface/implementation.
- Key files: `AutoQAC/Services/MO2/IMO2ValidationService.cs`, `AutoQAC/Services/MO2/MO2ValidationService.cs`

**`AutoQAC/Services/Monitoring/`:**
- Purpose: Monitor xEdit processes for CPU-based hangs.
- Contains: Hang detection service interface/implementation.
- Key files: `AutoQAC/Services/Monitoring/IHangDetectionService.cs`, `AutoQAC/Services/Monitoring/HangDetectionService.cs`

**`AutoQAC/Services/Plugin/`:**
- Purpose: Load plugin lists, validate plugin files, and approximate plugin issues through Mutagen/QueryPlugins.
- Contains: Plugin loading, validation, and issue approximation services.
- Key files: `AutoQAC/Services/Plugin/PluginLoadingService.cs`, `AutoQAC/Services/Plugin/PluginValidationService.cs`, `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs`

**`AutoQAC/Services/Process/`:**
- Purpose: Launch, track, cancel, gracefully terminate, force-kill, and clean up xEdit processes.
- Contains: Process execution interface/implementation.
- Key files: `AutoQAC/Services/Process/IProcessExecutionService.cs`, `AutoQAC/Services/Process/ProcessExecutionService.cs`

**`AutoQAC/Services/State/`:**
- Purpose: Shared runtime state hub and observable event streams.
- Contains: State service interface/implementation.
- Key files: `AutoQAC/Services/State/IStateService.cs`, `AutoQAC/Services/State/StateService.cs`

**`AutoQAC/Services/UI/`:**
- Purpose: UI-thread dispatching, file/message dialog abstractions, callback observers, and interaction primitives.
- Contains: Dispatcher and dialog services plus interaction infrastructure.
- Key files: `AutoQAC/Services/UI/IUiDispatcher.cs`, `AutoQAC/Services/UI/AvaloniaUiDispatcher.cs`, `AutoQAC/Services/UI/FileDialogService.cs`, `AutoQAC/Services/UI/MessageDialogService.cs`, `AutoQAC/Services/UI/Interactions/Interaction.cs`

**`AutoQAC/ViewModels/`:**
- Purpose: MVVM state and command surface for Avalonia bindings.
- Contains: Main window composition ViewModel, dialog/window ViewModels, and base class.
- Key files: `AutoQAC/ViewModels/MainWindowViewModel.cs`, `AutoQAC/ViewModels/ViewModelBase.cs`, `AutoQAC/ViewModels/ProgressViewModel.cs`, `AutoQAC/ViewModels/SettingsViewModel.cs`, `AutoQAC/ViewModels/RestoreViewModel.cs`

**`AutoQAC/ViewModels/MainWindow/`:**
- Purpose: Split main window responsibilities into focused sub-ViewModels.
- Contains: Configuration, plugin list, command orchestration, and plugin row wrapper ViewModels.
- Key files: `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`, `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs`, `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`, `AutoQAC/ViewModels/MainWindow/PluginListItem.cs`

**`AutoQAC/Views/`:**
- Purpose: Avalonia windows and dialogs.
- Contains: `.axaml` layout files and `.axaml.cs` code-behind files.
- Key files: `AutoQAC/Views/MainWindow.axaml`, `AutoQAC/Views/MainWindow.axaml.cs`, `AutoQAC/Views/ProgressWindow.axaml`, `AutoQAC/Views/SettingsWindow.axaml`, `AutoQAC/Views/RestoreWindow.axaml`

**`AutoQAC/Models/`:**
- Purpose: Domain/result/configuration models shared across services and ViewModels.
- Contains: Records/enums/classes for state, plugins, cleaning results, backups, validation, game types, and configuration models.
- Key files: `AutoQAC/Models/AppState.cs`, `AutoQAC/Models/PluginInfo.cs`, `AutoQAC/Models/CleaningSessionResult.cs`, `AutoQAC/Models/GameType.cs`, `AutoQAC/Models/Configuration/UserConfiguration.cs`

**`QueryPlugins/`:**
- Purpose: Standalone plugin issue detector library.
- Contains: Service facade, detector interfaces/implementations, game-specific detector strategies, and library result models.
- Key files: `QueryPlugins/QueryPlugins.csproj`, `QueryPlugins/IPluginQueryService.cs`, `QueryPlugins/PluginQueryService.cs`, `QueryPlugins/Detectors/ItmDetector.cs`, `QueryPlugins/Detectors/Games/SkyrimDetector.cs`

**`AutoQAC.Tests/`:**
- Purpose: App test project.
- Contains: `Services`, `ViewModels`, `Models`, `Integration`, `Views`, and `TestInfrastructure` test areas.
- Key files: `AutoQAC.Tests/AutoQAC.Tests.csproj`, `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`, `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs`, `AutoQAC.Tests/Integration/DependencyInjectionTests.cs`

**`QueryPlugins.Tests/`:**
- Purpose: Library test project for detector behavior and analysis models.
- Contains: Detector tests and model tests.
- Key files: `QueryPlugins.Tests/QueryPlugins.Tests.csproj`, `QueryPlugins.Tests/Detectors/ItmDetectorTests.cs`, `QueryPlugins.Tests/Detectors/Games/Fallout4DetectorTests.cs`

## Key File Locations

**Entry Points:**
- `AutoQAC/Program.cs`: Desktop process entry point and Avalonia builder.
- `AutoQAC/App.axaml.cs`: DI/bootstrap entry point after Avalonia initialization.
- `QueryPlugins/PluginQueryService.cs`: Library facade for plugin issue analysis.

**Configuration:**
- `AutoQACSharp.slnx`: Solution project list.
- `AutoQAC/AutoQAC.csproj`: Avalonia app target/framework/resources/packages/project reference.
- `QueryPlugins/QueryPlugins.csproj`: Standalone detector library target/framework/packages.
- `AutoQAC.Tests/AutoQAC.Tests.csproj`: App test dependencies and coverlet setup.
- `QueryPlugins.Tests/QueryPlugins.Tests.csproj`: Library test dependencies and coverlet setup.
- `AutoQAC Data/AutoQAC Main.yaml`: Bundled main/default configuration source.
- `AutoQAC Data/AutoQAC Settings.yaml`: User settings seed/source file.
- `AutoQAC/AutoQAC Data/`: App project data copied to output by `AutoQAC/AutoQAC.csproj`.

**Core Logic:**
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`: DI registration for every app layer.
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`: Primary workflow coordinator.
- `AutoQAC/Services/Process/ProcessExecutionService.cs`: Process execution/termination and PID tracking.
- `AutoQAC/Services/Configuration/ConfigurationService.cs`: YAML persistence and skip-list access.
- `AutoQAC/Services/Plugin/PluginLoadingService.cs`: Game-specific plugin discovery.
- `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs`: Mutagen/QueryPlugins bridge.
- `AutoQAC/Services/State/StateService.cs`: Runtime state/event hub.
- `QueryPlugins/Detectors/`: Plugin analysis detector implementations.

**UI:**
- `AutoQAC/Views/MainWindow.axaml`: Main window layout.
- `AutoQAC/Views/MainWindow.axaml.cs`: Dialog/window interaction handlers.
- `AutoQAC/ViewModels/MainWindowViewModel.cs`: Main ViewModel composition root.
- `AutoQAC/ViewModels/MainWindow/`: Main window sub-ViewModels.
- `AutoQAC/Services/UI/Interactions/Interaction.cs`: ViewModel-to-View interaction primitive.

**Testing:**
- `AutoQAC.Tests/Services/`: Service tests matching app service names.
- `AutoQAC.Tests/ViewModels/`: ViewModel tests.
- `AutoQAC.Tests/Integration/`: DI and cross-service integration tests.
- `AutoQAC.Tests/TestInfrastructure/SynchronousUiDispatcher.cs`: UI-dispatch test helper.
- `QueryPlugins.Tests/Detectors/`: Detector tests matching `QueryPlugins/Detectors/`.

## Naming Conventions

**Files:**
- Interface/implementation pairs use `I{Name}.cs` and `{Name}.cs`: `AutoQAC/Services/Cleaning/ICleaningService.cs` with `AutoQAC/Services/Cleaning/CleaningService.cs`.
- Avalonia views use `{WindowName}.axaml` plus `{WindowName}.axaml.cs`: `AutoQAC/Views/SettingsWindow.axaml` and `AutoQAC/Views/SettingsWindow.axaml.cs`.
- ViewModels end in `ViewModel.cs`: `AutoQAC/ViewModels/ProgressViewModel.cs`.
- Tests end in `Tests.cs`: `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`.
- Models use descriptive singular names: `AutoQAC/Models/PluginInfo.cs`, `AutoQAC/Models/CleaningResult.cs`.

**Directories:**
- Service directories group by feature responsibility: `AutoQAC/Services/Cleaning`, `AutoQAC/Services/Plugin`, `AutoQAC/Services/Configuration`.
- Test directories mirror source responsibilities: `AutoQAC.Tests/Services`, `AutoQAC.Tests/ViewModels`, `QueryPlugins.Tests/Detectors`.
- QueryPlugins game strategies live under `QueryPlugins/Detectors/Games`.

## Where to Add New Code

**New App Service:**
- Primary code: add interface and implementation under the matching feature folder in `AutoQAC/Services/<Feature>/`.
- DI registration: add the service in `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`.
- Tests: add matching tests under `AutoQAC.Tests/Services/` or a feature-specific test subfolder if one exists.

**New Cleaning Workflow Behavior:**
- Primary code: add orchestration steps to `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` when it affects session flow.
- Process launch behavior: add to `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs` or `AutoQAC/Services/Process/ProcessExecutionService.cs` based on responsibility.
- Tests: add/update `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`, `AutoQAC.Tests/Services/CleaningServiceTests.cs`, `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`, or `AutoQAC.Tests/Services/XEditCommandBuilderTests.cs`.

**New Main Window UI Feature:**
- ViewModel state/commands: add to the focused sub-ViewModel in `AutoQAC/ViewModels/MainWindow/` or create a new sub-ViewModel there.
- View layout: update `AutoQAC/Views/MainWindow.axaml`.
- Dialog/window ownership: add interaction handling to `AutoQAC/Views/MainWindow.axaml.cs`, not directly to the ViewModel.
- Tests: add/update tests under `AutoQAC.Tests/ViewModels/` and view lifecycle tests under `AutoQAC.Tests/Views/` if subscriptions/disposal are involved.

**New Dialog/Window:**
- View: add `AutoQAC/Views/<Name>Window.axaml` and `AutoQAC/Views/<Name>Window.axaml.cs`.
- ViewModel: add `AutoQAC/ViewModels/<Name>ViewModel.cs`.
- DI: register transient ViewModel/View in `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` when constructed through DI.
- Interaction: expose an `Interaction` from `AutoQAC/ViewModels/MainWindowViewModel.cs` or an appropriate parent ViewModel.

**New Plugin Analysis Capability:**
- Library code: add detector/model code under `QueryPlugins/Detectors/` or `QueryPlugins/Models/`.
- Game-specific code: add strategy code under `QueryPlugins/Detectors/Games/` and register it in `QueryPlugins/PluginQueryService.cs`.
- App bridge: update `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs` only when the desktop app needs to expose the new analysis.
- Tests: add detector tests under `QueryPlugins.Tests/Detectors/` and app bridge tests under `AutoQAC.Tests/Services/` if state merging/UI display changes.

**New Domain Model:**
- App model: add under `AutoQAC/Models/` or `AutoQAC/Models/Configuration/` for config-specific types.
- Library model: add under `QueryPlugins/Models/` for analysis-only types.
- Tests: add matching model tests under `AutoQAC.Tests/Models/` or `QueryPlugins.Tests/Models/`.

**Utilities:**
- UI utilities: add under `AutoQAC/Services/UI/` or `AutoQAC/Converters/` based on binding/service responsibility.
- Logging/path helpers: add under `AutoQAC/Infrastructure/Logging/` when logging-specific.
- Avoid cross-cutting static utility dumps; prefer feature-local private helpers or injected services.

## Special Directories

**`Mutagen/`:**
- Purpose: Reference submodule/source checkout for Mutagen.
- Generated: No.
- Committed: Yes, as a submodule/reference tree.
- Rule: Treat as read-only; do not build, modify, or add files under `Mutagen/`.

**`AutoQAC Data/`:**
- Purpose: Source configuration data files used by the application and copied/located for runtime configuration.
- Generated: No.
- Committed: Yes.
- Rule: Preserve YAML structure expected by `AutoQAC/Services/Configuration/ConfigurationService.cs`.

**`AutoQAC/bin/`, `AutoQAC/obj/`, `AutoQAC.Tests/bin/`, `AutoQAC.Tests/obj/`, `QueryPlugins/bin/`, `QueryPlugins/obj/`:**
- Purpose: .NET build outputs and intermediate files.
- Generated: Yes.
- Committed: No.
- Rule: Do not add source files here.

**`AutoQAC.Tests/TestResults/`:**
- Purpose: Test output and Cobertura coverage generated by `dotnet test`.
- Generated: Yes.
- Committed: No.
- Rule: Use for local verification artifacts only.

**`logs/` and `AutoQAC/logs/`:**
- Purpose: Runtime log output.
- Generated: Yes.
- Committed: No.
- Rule: Do not rely on log files for committed state.

**`.planning/`:**
- Purpose: GSD planning, roadmap, phase, and codebase-map artifacts.
- Generated: Partially.
- Committed: Project-dependent planning artifacts are tracked by workflow.
- Rule: Codebase maps belong in `.planning/codebase/`; implementation code does not.

**`openspec/`:**
- Purpose: OpenSpec active changes, archived changes, and specs.
- Generated: Partially.
- Committed: Yes.
- Rule: Use OpenSpec skills/workflows when creating or applying change artifacts.

---

*Structure analysis: 2026-04-28*
