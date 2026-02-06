# Codebase Structure

**Analysis Date:** 2026-02-06

## Directory Layout

```
AutoQACSharp/
├── AutoQAC/                        # Main application
│   ├── Models/                     # Domain entities and value objects
│   │   ├── Configuration/          # YAML configuration model classes
│   │   ├── AppState.cs             # Central immutable app state record
│   │   ├── GameType.cs             # Game enum (Skyrim, Fallout, etc.)
│   │   ├── PluginInfo.cs           # Plugin metadata record
│   │   ├── CleaningResult.cs        # Single plugin result
│   │   ├── CleaningSessionResult.cs # Session summary with multiple results
│   │   └── PluginCleaningResult.cs  # Detailed per-plugin cleaning data
│   ├── Services/                   # Business logic and orchestration
│   │   ├── Cleaning/               # xEdit subprocess and parsing
│   │   │   ├── ICleaningOrchestrator.cs
│   │   │   ├── CleaningOrchestrator.cs  # Sequential workflow
│   │   │   ├── ICleaningService.cs
│   │   │   ├── CleaningService.cs
│   │   │   ├── XEditCommandBuilder.cs
│   │   │   └── XEditOutputParser.cs
│   │   ├── Configuration/          # YAML config file I/O
│   │   │   ├── IConfigurationService.cs
│   │   │   └── ConfigurationService.cs
│   │   ├── GameDetection/          # Detect game from executable
│   │   │   ├── IGameDetectionService.cs
│   │   │   └── GameDetectionService.cs
│   │   ├── Plugin/                 # Load plugins from various sources
│   │   │   ├── IPluginLoadingService.cs
│   │   │   ├── PluginLoadingService.cs
│   │   │   ├── IPluginValidationService.cs
│   │   │   └── PluginValidationService.cs
│   │   ├── Process/                # Subprocess execution
│   │   │   ├── IProcessExecutionService.cs
│   │   │   └── ProcessExecutionService.cs
│   │   ├── State/                  # Central state and observables
│   │   │   ├── IStateService.cs
│   │   │   └── StateService.cs
│   │   ├── MO2/                    # Mod Organizer 2 integration
│   │   │   ├── IMO2ValidationService.cs
│   │   │   └── MO2ValidationService.cs
│   │   └── UI/                     # Dialog services
│   │       ├── IFileDialogService.cs
│   │       ├── FileDialogService.cs
│   │       ├── IMessageDialogService.cs
│   │       └── MessageDialogService.cs
│   ├── ViewModels/                 # Presentation logic
│   │   ├── ViewModelBase.cs        # Base class extending ReactiveObject
│   │   ├── MainWindowViewModel.cs  # Orchestrates UI and services
│   │   ├── ProgressViewModel.cs    # Real-time cleaning progress
│   │   ├── SettingsViewModel.cs    # Game path and option settings
│   │   ├── SkipListViewModel.cs    # Skip list management UI
│   │   ├── CleaningResultsViewModel.cs # Results summary
│   │   ├── PartialFormsWarningViewModel.cs # Partial forms dialog
│   │   └── MessageDialogViewModel.cs # Generic message dialog
│   ├── Views/                      # XAML UI and minimal code-behind
│   │   ├── MainWindow.axaml        # Primary window with plugin list
│   │   ├── MainWindow.axaml.cs     # Interaction handlers for dialogs
│   │   ├── ProgressWindow.axaml    # Real-time cleaning progress
│   │   ├── SettingsWindow.axaml    # Configuration window
│   │   ├── SkipListWindow.axaml    # Skip list editor
│   │   ├── CleaningResultsWindow.axaml # Results display
│   │   ├── MessageDialog.axaml     # Generic message dialog
│   │   └── PartialFormsWarningDialog.axaml # Partial forms warning
│   ├── Infrastructure/             # Cross-cutting concerns
│   │   ├── Logging/                # Logging abstractions
│   │   │   ├── ILoggingService.cs
│   │   │   └── LoggingService.cs
│   │   ├── ServiceCollectionExtensions.cs # DI registration
│   │   └── (Converters moved to separate dir)
│   ├── Converters/                 # XAML value converters
│   │   └── GameTypeDisplayConverter.cs # Game type enum to string
│   ├── Assets/                     # Images, icons, embedded resources
│   ├── AutoQAC Data/               # YAML config files (user-writable)
│   │   ├── AutoQAC Main.yaml       # Game skip lists and metadata
│   │   ├── AutoQAC Config.yaml     # User settings (paths, game choice)
│   │   └── AutoQAC Ignore.yaml     # Additional ignore list
│   ├── logs/                       # Runtime log files (generated)
│   ├── App.axaml                   # Application XAML root
│   ├── App.axaml.cs                # Application startup and DI setup
│   ├── Program.cs                  # Entry point
│   ├── ViewLocator.cs              # Auto-resolve ViewModels to Views
│   ├── AutoQAC.csproj              # Project configuration
│   └── Properties/                 # Metadata and signing
│
├── AutoQAC.Tests/                  # Test project (xUnit)
│   ├── Services/                   # Service layer tests
│   │   ├── CleaningOrchestratorTests.cs
│   │   ├── CleaningServiceTests.cs
│   │   ├── ConfigurationServiceSkipListTests.cs
│   │   └── (more service tests)
│   ├── ViewModels/                 # ViewModel tests
│   ├── Models/                     # Model tests
│   │   ├── AppStateTests.cs
│   │   ├── CleaningSessionResultTests.cs
│   │   └── PluginCleaningResultTests.cs
│   ├── Integration/                # End-to-end tests
│   │   ├── DependencyInjectionTests.cs
│   │   └── GameSelectionIntegrationTests.cs
│   └── AutoQAC.Tests.csproj
│
├── Code_To_Port/                   # Reference implementations (temporary)
│   ├── CLAUDE.md                   # Architecture of Python/Qt version
│   ├── AutoQACLib/                 # Python source for reference
│   └── (Rust/Slint version also present)
│
├── docs/                           # Documentation
├── openspec/                       # OpenSpec proposal templates
├── CLAUDE.md                       # Project-specific AI assistant guidelines
├── AGENTS.md                       # OpenSpec agent documentation
├── README.md                       # Project overview
├── ROADMAP.md                      # Development roadmap
└── .planning/codebase/             # Codebase analysis documents (this directory)
    ├── ARCHITECTURE.md             # This file's counterpart
    ├── STRUCTURE.md                # This file
    ├── CONVENTIONS.md              # (generated on quality focus)
    ├── TESTING.md                  # (generated on quality focus)
    ├── STACK.md                    # (generated on tech focus)
    └── INTEGRATIONS.md             # (generated on tech focus)
```

## Directory Purposes

**Models/**
- Purpose: Domain entities, value objects, configuration DTOs
- Contains: C# records, enums, immutable data structures
- Key files: `AppState.cs` (central state object), `GameType.cs` (supported games enum), `Configuration/` subdirectory for YAML mapping classes
- Pattern: Immutable records with init-only or derived properties

**Services/**
- Purpose: Business logic, orchestration, external integrations
- Contains: Interfaces and implementations for all domain operations
- Subdirectories mirror concerns: Cleaning, Configuration, GameDetection, Plugin, Process, State, MO2, UI
- Pattern: Interface-first design, dependency injection via constructor, async/await throughout

**Services/Cleaning/**
- Purpose: Manage xEdit subprocess execution and output parsing
- Key abstractions: `ICleaningOrchestrator` (high-level workflow), `ICleaningService` (subprocess execution), `XEditCommandBuilder` (command-line generation), `XEditOutputParser` (output parsing)
- Sequential processing: Loop through plugins one at a time (CRITICAL CONSTRAINT)

**Services/Configuration/**
- Purpose: YAML file I/O for game metadata and user settings
- Loads: `MainConfiguration` (game skip lists) and `UserConfiguration` (user paths)
- Observable: SkipListChanged and UserConfigurationChanged for reactive UI

**Services/Plugin/**
- Purpose: Load plugins from various sources with polymorphism
- Mutagen-first: SkyrimLE, SkyrimSE, SkyrimVR, Fallout4, Fallout4VR via Mutagen library
- Fallback: File-based loading for unsupported games (Oblivion, Fallout3, FalloutNewVegas)
- Output: List<PluginInfo> with skip list status pre-calculated

**Services/State/**
- Purpose: Central state management and reactive observables
- Pattern: Single `IStateService` singleton with `AppState` immutable record and functional updates
- Observables: StateChanged, ProgressChanged, PluginProcessed, CleaningCompleted for reactive binding

**ViewModels/**
- Purpose: Presentation logic and command handling
- Base: `ViewModelBase` extends `ReactiveObject` for property change notifications
- Patterns: `RaiseAndSetIfChanged` for properties, `ObservableAsPropertyHelper` for computed properties, `ReactiveCommand` for user actions, `Interaction<TInput, TOutput>` for cross-window communication
- Key ViewModel: `MainWindowViewModel` - orchestrator between UI and services

**Views/**
- Purpose: XAML UI rendering and minimal code-behind
- Pattern: DataContext bound to ViewModel, interaction handlers registered in code-behind constructor
- Code-behind responsibilities: Interaction handler registration (dialogs, results display), dependency injection of services for callback handlers
- No business logic: All logic delegated to ViewModels and Services

**Infrastructure/**
- Purpose: Cross-cutting concerns and framework setup
- Logging: `ILoggingService` interface with implementation
- DI Setup: `ServiceCollectionExtensions.cs` registers all services and ViewModels
- Converters: XAML value converters (GameTypeDisplayConverter)

**Assets/**
- Purpose: Application resources (images, icons, fonts)
- Location: Referenced in XAML via `avares://AutoQAC/Assets/`

**AutoQAC Data/**
- Purpose: User-writable YAML configuration files
- Not committed: Listed in .gitignore
- Files: `AutoQAC Main.yaml`, `AutoQAC Config.yaml`, `AutoQAC Ignore.yaml`

**logs/**
- Purpose: Runtime log files
- Not committed: Listed in .gitignore
- Generated at runtime by LoggingService

**AutoQAC.Tests/**
- Purpose: Unit and integration test coverage
- Structure: Mirrors main project (Services/, ViewModels/, Models/)
- Framework: xUnit with FluentAssertions
- Key tests: DependencyInjectionTests, GameSelectionIntegrationTests, CleaningOrchestratorTests

## Key File Locations

**Entry Points:**
- `AutoQAC/Program.cs`: Application entry point, builds Avalonia AppBuilder
- `AutoQAC/App.axaml.cs`: Framework initialization, dependency injection setup, MainWindow instantiation
- `AutoQAC/Views/MainWindow.axaml`: Primary UI window

**Configuration & Startup:**
- `AutoQAC/AutoQAC.csproj`: Project file (target .NET 9, Avalonia 11.3.8, ReactiveUI)
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`: DI registration for all services

**Core Logic:**
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`: Main cleaning workflow orchestrator (sequential processing)
- `AutoQAC/Services/Configuration/ConfigurationService.cs`: YAML config loading and caching
- `AutoQAC/Services/State/StateService.cs`: Central state store with observable streams
- `AutoQAC/Models/AppState.cs`: Immutable state definition

**ViewModel Layer:**
- `AutoQAC/ViewModels/MainWindowViewModel.cs`: Orchestrates UI, commands, and service interactions
- `AutoQAC/ViewModels/ViewModelBase.cs`: Base class for all ViewModels

**Test Entry Points:**
- `AutoQAC.Tests/Integration/DependencyInjectionTests.cs`: Verifies DI setup
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`: Tests cleaning workflow
- `AutoQAC.Tests/Models/CleaningSessionResultTests.cs`: Tests result aggregation

## Naming Conventions

**Files:**
- PascalCase matching class name: `CleaningService.cs` contains `CleaningService` class
- Interface files: `ICleaningService.cs` for `ICleaningService` interface
- XAML files: `MainWindow.axaml`, `MainWindow.axaml.cs` for code-behind
- Test files: `{ClassUnderTest}Tests.cs`, e.g., `CleaningOrchestratorTests.cs`

**Directories:**
- PascalCase for namespaces: `AutoQAC.Services.Cleaning`, `AutoQAC.ViewModels`
- Subdirectories mirror functionality domains
- YAML config files: "AutoQAC {Name}.yaml"

**C# Naming:**
- Classes: `PascalCase` (e.g., `CleaningOrchestrator`, `MainWindowViewModel`)
- Interfaces: `IPascalCase` (e.g., `ICleaningService`, `IStateService`)
- Private fields: `_camelCase` (e.g., `_cleaningService`, `_logger`)
- Properties: `PascalCase` (e.g., `CurrentState`, `PluginsToClean`)
- Public methods: `PascalCase` (e.g., `StartCleaningAsync`, `AddDetailedCleaningResult`)
- Async methods: Suffix with `Async` (e.g., `GetPluginsAsync`, `ValidatePathsAsync`)
- Records: `PascalCase` with immutable init-only properties (e.g., `PluginInfo`, `CleaningSessionResult`)

**Enums:**
- Enum type: `PascalCase` (e.g., `GameType`, `CleaningStatus`)
- Enum values: `PascalCase` (e.g., `GameType.SkyrimSe`, `CleaningStatus.Cleaned`)

**Constants:**
- UPPER_CASE (limited usage in codebase, prefer readonly properties)

## Where to Add New Code

**New Feature - Cleaning Enhancement:**
- Primary code: `AutoQAC/Services/Cleaning/` (new interface + implementation)
- Models: `AutoQAC/Models/` if new domain entity needed
- Tests: `AutoQAC.Tests/Services/` with corresponding test class
- Integration: Register in `Infrastructure/ServiceCollectionExtensions.cs` → `AddBusinessLogic()`

**New ViewModel & Dialog Window:**
- ViewModel: `AutoQAC/ViewModels/{FeatureName}ViewModel.cs` inheriting `ViewModelBase`
- View: `AutoQAC/Views/{FeatureName}Window.axaml` and `.axaml.cs`
- Registration: `ServiceCollectionExtensions.cs` → `AddViewModels()` and `AddViews()` (transient)
- Interaction: Define `Interaction<TInput, TOutput>` in ViewModel, register handler in MainWindow or parent View

**New Model/Value Object:**
- Location: `AutoQAC/Models/{FeatureName}.cs`
- Pattern: Immutable record or sealed class
- Usage: No external dependencies (except YamlDotNet for configuration models)

**New Service Interface:**
- Interface: `AutoQAC/Services/{Domain}/INewService.cs`
- Implementation: `AutoQAC/Services/{Domain}/NewService.cs`
- Registration: `ServiceCollectionExtensions.cs` as Singleton in `AddBusinessLogic()`
- Dependencies: Injected via constructor

**Configuration/Metadata:**
- YAML config: Add to `AutoQAC Data/AutoQAC Main.yaml`
- Model class: Update `Models/Configuration/MainConfiguration.cs` or `UserConfiguration.cs`
- Access: Through `IConfigurationService`

**Testing:**
- Unit test: `AutoQAC.Tests/Services/{Service}Tests.cs` or `AutoQAC.Tests/ViewModels/{ViewModel}Tests.cs`
- Framework: xUnit with FluentAssertions
- Mocking: Moq for service dependencies
- Pattern: Arrange-Act-Assert with descriptive test names

**UI Assets/Resources:**
- Images/Icons: `AutoQAC/Assets/` (organized by type if many)
- Referenced in XAML: `<Image Source="avares://AutoQAC/Assets/{filename}" />`

## Special Directories

**AutoQAC Data/:**
- Purpose: User-writable YAML configuration storage
- Generated: At first run or when user configures paths
- Committed: No (listed in .gitignore)
- Files: `AutoQAC Main.yaml`, `AutoQAC Config.yaml`, `AutoQAC Ignore.yaml`

**logs/:**
- Purpose: Runtime application logs
- Generated: At runtime by LoggingService
- Committed: No (listed in .gitignore)
- Pattern: Rotating logs per session

**Code_To_Port/:**
- Purpose: Reference implementations (Python/Qt and Rust/Slint) for feature porting
- Committed: Yes (temporary until feature parity achieved)
- Status: To be removed once C# implementation complete
- Usage: Read-only reference during development

**Properties/:**
- Purpose: Project metadata and assembly information
- Key file: `AssemblyInfo.cs` (auto-generated from .csproj)

**.planning/codebase/:**
- Purpose: Codebase analysis documents for future Claude instances
- Documents: ARCHITECTURE.md, STRUCTURE.md, CONVENTIONS.md, TESTING.md, STACK.md, INTEGRATIONS.md, CONCERNS.md
- Committed: Yes (reference only, not auto-generated)

---

*Structure analysis: 2026-02-06*
