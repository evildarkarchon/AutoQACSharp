# Codebase Structure

**Analysis Date:** 2026-03-30

## Directory Layout

```
AutoQACSharp/
├── AutoQAC/                    # Main desktop application (WinExe)
│   ├── Assets/                 # App icons and resources
│   ├── AutoQAC Data/           # Bundled YAML configs (copied to output)
│   ├── Converters/             # Avalonia value converters
│   ├── Infrastructure/         # DI wiring and logging
│   │   └── Logging/            # Serilog wrapper
│   ├── Models/                 # Domain models and records
│   │   └── Configuration/      # YAML-mapped config models
│   ├── Properties/             # Build properties and publish profiles
│   │   └── PublishProfiles/    # Publish configuration
│   ├── Services/               # Business logic organized by domain
│   │   ├── Backup/             # Plugin backup and restore
│   │   ├── Cleaning/           # xEdit orchestration and parsing
│   │   ├── Configuration/      # YAML config read/write/watch
│   │   ├── GameDetection/      # Game type detection from exe/load order
│   │   ├── MO2/                # Mod Organizer 2 validation
│   │   ├── Monitoring/         # Process hang detection
│   │   ├── Plugin/             # Plugin loading and validation
│   │   ├── Process/            # Process execution and PID tracking
│   │   ├── State/              # Centralized reactive state
│   │   └── UI/                 # File/message dialog abstractions
│   ├── ViewModels/             # MVVM ViewModels
│   │   └── MainWindow/         # Sub-ViewModels for main window
│   ├── Views/                  # Avalonia XAML views
│   ├── App.axaml               # Application XAML
│   ├── App.axaml.cs            # Composition root
│   ├── app.manifest            # Windows app manifest
│   └── AutoQAC.csproj          # Project file
│
├── AutoQAC.Tests/              # Unit and integration tests for AutoQAC
│   ├── Integration/            # DI container and game selection tests
│   ├── Models/                 # Model record tests
│   ├── Services/               # Service-level tests (one file per service)
│   │   └── UI/                 # UI service tests
│   ├── TestInfrastructure/     # Test collection definitions (Rx scheduler)
│   ├── ViewModels/             # ViewModel tests
│   └── Views/                  # View subscription lifecycle tests
│
├── QueryPlugins/               # Standalone Mutagen analysis library
│   ├── Detectors/              # Issue detector interfaces
│   │   └── Games/              # Per-game detector implementations
│   ├── Models/                 # Analysis result models
│   ├── IPluginQueryService.cs  # Top-level service interface
│   ├── PluginQueryService.cs   # Service implementation
│   └── QueryPlugins.csproj     # Project file
│
├── QueryPlugins.Tests/         # Unit tests for QueryPlugins
│   ├── Detectors/              # Detector tests
│   │   └── Games/              # Per-game detector tests
│   └── Models/                 # Model tests
│
├── AutoQAC Data/               # Runtime data folder (top-level copy)
├── Mutagen/                    # READ-ONLY git submodule (reference only)
├── docs/                       # Documentation
│   └── mutagen/                # Curated Mutagen API docs
├── Release/                    # Published build output
├── AutoQACSharp.slnx           # Solution file (XML format)
└── CLAUDE.md                   # AI assistant instructions
```

## Directory Purposes

**`AutoQAC/Infrastructure/`:**
- Purpose: DI composition and cross-cutting infrastructure
- Contains: Service registration extensions, logging abstraction
- Key files:
  - `ServiceCollectionExtensions.cs` -- All DI registrations grouped by concern
  - `Logging/ILoggingService.cs` -- Logging interface
  - `Logging/LoggingService.cs` -- Serilog wrapper implementation
  - `Logging/LogFilePaths.cs` -- Log file path resolution

**`AutoQAC/Models/`:**
- Purpose: Immutable domain records and enums
- Contains: All data types shared across services and ViewModels
- Key files:
  - `AppState.cs` -- Central application state record
  - `PluginInfo.cs` -- Plugin metadata with skip list and approximation status
  - `GameType.cs` -- Enum of supported games (8 values + Unknown)
  - `GameVariant.cs` -- Enum for TTW and Enderal variants
  - `CleaningResult.cs` -- Single-plugin cleaning result + `CleaningStatus` enum + `CleaningStatistics`
  - `CleaningSessionResult.cs` -- Full session result with aggregated stats
  - `PluginIssueApproximation.cs` -- Mutagen-based ITM/UDR/navmesh preview data
  - `ValidationError.cs` -- Structured pre-clean validation error
  - `DryRunResult.cs` -- Dry-run preview result per plugin
  - `BackupResult.cs` -- Backup operation result
  - `BackupSession.cs` -- Backup session metadata (serialized to session.json)
  - `TerminationResult.cs` -- Process termination outcome enum

**`AutoQAC/Models/Configuration/`:**
- Purpose: YAML-mapped configuration models (deserialized with YamlDotNet)
- Key files:
  - `UserConfiguration.cs` -- User settings, paths, skip lists, per-game overrides (maps to `AutoQAC Settings.yaml`)
  - `MainConfiguration.cs` -- Bundled read-only config with default skip lists and xEdit names (maps to `AutoQAC Main.yaml`)
  - `RetentionSettings.cs` -- Log retention configuration
  - `BackupSettings.cs` -- Backup configuration (enabled, max sessions)

**`AutoQAC/Services/Cleaning/`:**
- Purpose: xEdit cleaning orchestration and output parsing
- Key files:
  - `CleaningOrchestrator.cs` -- Full session coordinator (~870 lines)
  - `ICleaningOrchestrator.cs` -- Orchestrator interface with start/stop/preview methods
  - `CleaningService.cs` -- Single-plugin cleaning via ProcessExecutionService
  - `ICleaningService.cs` -- Cleaning interface
  - `XEditCommandBuilder.cs` -- Builds ProcessStartInfo with xEdit flags
  - `XEditOutputParser.cs` -- Parses xEdit output for statistics
  - `XEditLogFileService.cs` -- Reads xEdit log files
  - `IXEditLogFileService.cs` -- Log file reading interface

**`AutoQAC/Services/Configuration/`:**
- Purpose: YAML config persistence, external change detection, migrations
- Key files:
  - `ConfigurationService.cs` -- Core read/write with debounced saves
  - `IConfigurationService.cs` -- Full configuration interface (~80 lines)
  - `ConfigWatcherService.cs` -- FileSystemWatcher for external YAML edits
  - `LegacyMigrationService.cs` -- One-time legacy config migration
  - `LogRetentionService.cs` -- Old log file cleanup

**`AutoQAC/Services/Plugin/`:**
- Purpose: Plugin discovery, validation, and Mutagen-based analysis
- Key files:
  - `PluginLoadingService.cs` -- Mutagen and file-based plugin loading
  - `IPluginLoadingService.cs` -- Plugin loading interface
  - `PluginValidationService.cs` -- File existence and extension validation
  - `PluginIssueApproximationService.cs` -- Mutagen-based ITM/UDR/navmesh preview
  - `IPluginIssueApproximationService.cs` -- Approximation interface

**`AutoQAC/Services/State/`:**
- Purpose: Centralized reactive application state
- Key files:
  - `StateService.cs` -- Thread-safe state hub with observable streams
  - `IStateService.cs` -- State interface (~67 lines)

**`AutoQAC/Services/Process/`:**
- Purpose: Single-slot process execution with PID tracking
- Key files:
  - `ProcessExecutionService.cs` -- Process launcher with timeout, orphan cleanup, termination
  - `IProcessExecutionService.cs` -- Process execution interface + `ProcessResult` record

**`AutoQAC/Services/Monitoring/`:**
- Purpose: xEdit hang detection via CPU polling
- Key files:
  - `HangDetectionService.cs` -- CPU-based hang detection with Rx observable
  - `IHangDetectionService.cs` -- Hang detection interface

**`AutoQAC/Services/Backup/`:**
- Purpose: Pre-cleaning plugin backup and restore
- Key files:
  - `BackupService.cs` -- Session directory management, copy, restore, retention
  - `IBackupService.cs` -- Backup interface

**`AutoQAC/Services/GameDetection/`:**
- Purpose: Game type detection from xEdit name and load order
- Key files:
  - `GameDetectionService.cs` -- Detection logic including variant detection
  - `IGameDetectionService.cs` -- Detection interface

**`AutoQAC/Services/MO2/`:**
- Purpose: Mod Organizer 2 validation
- Key files:
  - `Mo2ValidationService.cs` -- MO2 path validation
  - `IMo2ValidationService.cs` -- MO2 validation interface

**`AutoQAC/Services/UI/`:**
- Purpose: Dialog service abstractions (testable)
- Key files:
  - `FileDialogService.cs` -- Avalonia file/folder dialog wrapper
  - `IFileDialogService.cs` -- File dialog interface
  - `MessageDialogService.cs` -- Error/warning/confirm/retry dialog service
  - `IMessageDialogService.cs` -- Message dialog interface

**`AutoQAC/ViewModels/`:**
- Purpose: All MVVM ViewModels
- Key files:
  - `ViewModelBase.cs` -- Base class extending `ReactiveObject`
  - `MainWindowViewModel.cs` -- Slim orchestrator composing 3 sub-VMs
  - `ProgressViewModel.cs` -- Live cleaning progress and dry-run preview
  - `SettingsViewModel.cs` -- Settings editing
  - `SkipListViewModel.cs` -- Skip list editing
  - `RestoreViewModel.cs` -- Backup restore browser
  - `CleaningResultsViewModel.cs` -- Post-session results
  - `PartialFormsWarningViewModel.cs` -- Experimental feature warning
  - `MessageDialogViewModel.cs` -- Generic dialog ViewModel
  - `AboutViewModel.cs` -- About dialog

**`AutoQAC/ViewModels/MainWindow/`:**
- Purpose: Sub-ViewModels that compose `MainWindowViewModel`
- Key files:
  - `ConfigurationViewModel.cs` -- Paths, game selection, auto-save, plugin loading (~950 lines)
  - `PluginListViewModel.cs` -- Plugin collection with efficient diffing
  - `CleaningCommandsViewModel.cs` -- Start/stop/preview commands and validation

**`AutoQAC/Views/`:**
- Purpose: Avalonia XAML views and code-behind
- Key files:
  - `MainWindow.axaml` + `MainWindow.axaml.cs` -- Main window with interaction handler registration
  - `ProgressWindow.axaml` + `.cs` -- Live progress display
  - `SettingsWindow.axaml` + `.cs` -- Settings dialog
  - `SkipListWindow.axaml` + `.cs` -- Skip list editor
  - `RestoreWindow.axaml` + `.cs` -- Backup restore browser
  - `CleaningResultsWindow.axaml` + `.cs` -- Post-session results display
  - `AboutWindow.axaml` + `.cs` -- About dialog
  - `MessageDialog.axaml` + `.cs` -- Generic message dialog
  - `PartialFormsWarningDialog.axaml` + `.cs` -- Experimental feature warning

**`AutoQAC/Converters/`:**
- Purpose: Avalonia XAML value converters
- Key files:
  - `GameTypeDisplayConverter.cs` -- Converts `GameType` enum to display string
  - `IntEqualsConverter.cs` -- Integer equality comparison for binding
  - `NullableBoolConverters.cs` -- Nullable bool tri-state converters

**`QueryPlugins/`:**
- Purpose: Standalone Mutagen-based plugin analysis library
- Key files:
  - `IPluginQueryService.cs` -- Top-level analysis interface
  - `PluginQueryService.cs` -- Orchestrates ITM + game-specific detectors
  - `Detectors/IItmDetector.cs` -- ITM detection interface
  - `Detectors/ItmDetector.cs` -- Cross-game ITM detection via link cache
  - `Detectors/IGameSpecificDetector.cs` -- Per-game UDR/navmesh interface
  - `Detectors/Games/SkyrimDetector.cs` -- Skyrim LE/SE/VR detector
  - `Detectors/Games/Fallout4Detector.cs` -- Fallout 4/4VR detector
  - `Detectors/Games/StarfieldDetector.cs` -- Starfield detector
  - `Detectors/Games/OblivionDetector.cs` -- Oblivion detector
  - `Models/PluginAnalysisResult.cs` -- Aggregated analysis result
  - `Models/PluginIssue.cs` -- Single issue record (FormKey, EditorID, IssueType)
  - `Models/IssueType.cs` -- Issue category enum (ITM, DeletedReference, DeletedNavmesh)

## Key File Locations

**Entry Points:**
- `AutoQAC/App.axaml.cs` -- Application startup, DI composition root
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` -- All service registrations

**Configuration:**
- `AutoQAC Data/AutoQAC Main.yaml` -- Bundled defaults: skip lists, xEdit names, version info
- `AutoQAC Data/AutoQAC Settings.yaml` -- User settings: paths, game selection, per-game overrides
- `AutoQAC/AutoQAC.csproj` -- Project dependencies, TFM, build settings

**Core Logic:**
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` -- Full cleaning session flow
- `AutoQAC/Services/State/StateService.cs` -- Central state management
- `AutoQAC/Services/Plugin/PluginLoadingService.cs` -- Plugin discovery
- `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs` -- Mutagen analysis bridge
- `AutoQAC/Services/Configuration/ConfigurationService.cs` -- YAML persistence

**Testing:**
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs` -- Orchestrator tests
- `AutoQAC.Tests/Services/StateServiceTests.cs` -- State management tests
- `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs` -- ViewModel tests
- `AutoQAC.Tests/Integration/DependencyInjectionTests.cs` -- DI container resolution tests
- `QueryPlugins.Tests/Detectors/` -- Detector tests by game

## Naming Conventions

**Files:**
- Services: `{Name}Service.cs` with matching `I{Name}Service.cs` interface
- ViewModels: `{Name}ViewModel.cs`
- Views: `{Name}.axaml` + `{Name}.axaml.cs` code-behind
- Models: PascalCase matching the type name
- Tests: `{ServiceName}Tests.cs` (no `Test` suffix on folders)

**Directories:**
- Service groups: Singular noun matching the domain (`Backup/`, `Cleaning/`, `Plugin/`)
- Test mirrors: Flat `Services/`, `ViewModels/`, `Models/`, `Views/`, `Integration/`

## Where to Add New Code

**New Service:**
1. Create `I{Name}Service.cs` (interface) and `{Name}Service.cs` (implementation) in the appropriate `AutoQAC/Services/{Domain}/` subdirectory
2. Register in `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` under the appropriate `Add*()` method
3. Add tests in `AutoQAC.Tests/Services/{Name}ServiceTests.cs`
4. Use constructor injection -- all services are registered as Singleton

**New ViewModel:**
1. Create `{Name}ViewModel.cs` in `AutoQAC/ViewModels/`
2. Extend `ViewModelBase` (which extends `ReactiveObject`)
3. Register in `ServiceCollectionExtensions.AddViewModels()` as Transient
4. If it needs dialog interaction, add an `Interaction<TInput, TOutput>` to `MainWindowViewModel` and register the handler in `MainWindow.axaml.cs`
5. Add tests in `AutoQAC.Tests/ViewModels/{Name}ViewModelTests.cs`

**New View:**
1. Create `{Name}.axaml` + `{Name}.axaml.cs` in `AutoQAC/Views/`
2. Register in `ServiceCollectionExtensions.AddViews()` as Transient
3. Set `DataContext` in code-behind constructor, not in XAML
4. Register any interaction handlers in `MainWindow.axaml.cs`

**New Model:**
1. Create in `AutoQAC/Models/` (or `AutoQAC/Models/Configuration/` for YAML-mapped types)
2. Use `sealed record` for immutable data with `with` expression support
3. Use `sealed class` only for YAML-mapped config models (YamlDotNet requires mutable setters)

**New Converter:**
1. Create in `AutoQAC/Converters/`
2. Implement `IValueConverter` or `IMultiValueConverter`

**New Game Detector (QueryPlugins):**
1. Create in `QueryPlugins/Detectors/Games/{Game}Detector.cs`
2. Implement `IGameSpecificDetector`
3. Register in `PluginQueryService` default constructor
4. Add tests in `QueryPlugins.Tests/Detectors/Games/{Game}DetectorTests.cs`

**New Integration Test:**
1. Create in `AutoQAC.Tests/Integration/`

## Special Directories

**`Mutagen/`:**
- Purpose: Read-only git submodule of the Mutagen library source
- Generated: No (submodule checkout)
- Committed: As submodule reference only
- Do NOT build, modify, or add files here

**`AutoQAC Data/` (inside `AutoQAC/`):**
- Purpose: Bundled YAML configs and app icons
- Copied to output directory via `<None Include="AutoQAC Data\**" CopyToOutputDirectory="PreserveNewest" />`
- Contains `AutoQAC Main.yaml` (bundled defaults) and icon files

**`AutoQAC Data/` (at repo root):**
- Purpose: Top-level copy of runtime data for development and Release builds
- Contains the same files as `AutoQAC/AutoQAC Data/`

**`docs/mutagen/`:**
- Purpose: Curated Mutagen API documentation for quick reference
- Check here first before consulting the `Mutagen/` submodule source

**`Release/`:**
- Purpose: Published build output directory
- Generated: Yes (by `dotnet publish`)
- Not committed to git

**`AutoQAC/logs/`:**
- Purpose: Serilog file sink output at runtime
- Generated: Yes (at runtime)
- Not committed to git

---

*Structure analysis: 2026-03-30*
