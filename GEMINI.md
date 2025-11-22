# AutoQACSharp Context & Guidelines

## 1. Project Overview
**AutoQACSharp** is a C#/.NET 8 implementation of the "XEdit-PACT" tool, utilizing the **Avalonia UI** framework (v11.3.8) and **ReactiveUI**. It automates the cleaning of Bethesda game plugins (Skyrim, Fallout, etc.) using xEdit's Quick Auto Clean (QAC) functionality.

*   **Goal**: Achieve feature parity with the reference Python/Qt implementation located in `Code_To_Port/`.
*   **Current Status**: **Foundation Stage**. The skeleton exists, but business logic, models, and services are largely unimplemented.
*   **Key Constraint**: Sequential processing only (one plugin at a time) due to xEdit file locking.

## 2. Architecture & Patterns
*   **MVVM (Model-View-ViewModel)**: Strict separation of concerns.
    *   `Views/`: XAML + minimal code-behind.
    *   `ViewModels/`: Presentation logic, inheriting `ViewModelBase`, using ReactiveUI.
    *   `Models/`: Pure data records/classes (currently empty).
*   **Dependency Injection**: Services must be defined via interfaces and injected (e.g., `Microsoft.Extensions.DependencyInjection`).
*   **Reactive Programming**: Use `ReactiveCommand`, `ObservableAsPropertyHelper`, and `WhenAnyValue` for UI logic.
*   **Platform**: Cross-platform targeting, but file paths and executables may be Windows-centric (games/xEdit).

## 3. Directory Structure
```text
J:\AutoQACSharp\
├── AutoQACSharp.slnx          # Solution file (XML format)
├── AutoQAC/                   # Main Application Project
│   ├── App.axaml(.cs)         # Entry point, DI setup (TODO)
│   ├── Models/                # Data models (Empty - TODO)
│   ├── ViewModels/            # ViewModels (MainWindowViewModel exists)
│   ├── Views/                 # Views (MainWindow exists)
│   ├── Services/              # Service interfaces & impl (Missing - TODO)
│   └── Program.cs             # Main entry
├── AutoQAC.Tests/             # xUnit Test Project
└── Code_To_Port/              # REFERENCE implementation (Python/Rust)
    ├── AutoQACLib/            # Python source code to analyze
    └── AutoQAC_Interface.py   # Python entry point
```

## 4. Development Roadmap (Immediate Priorities)
Refer to `FEATURE_PARITY_ROADMAP.md` for implementation details.

*   **Phase 1: Infrastructure (CRITICAL)**
    *   [ ] Add NuGet packages: `YamlDotNet`, `Serilog`, `DI`, `FluentAssertions`.
    *   [ ] Implement `LoggingService` (Serilog wrapper).
    *   [ ] Setup Dependency Injection in `App.axaml.cs`.
    *   [ ] Create base Models: `CleaningResult`, `GameType`, `PluginInfo`.
*   **Phase 2: Configuration**
    *   [ ] Create YAML mapping models (`MainConfiguration`, `UserConfiguration`).
    *   [ ] Implement `ConfigurationService` (read/write YAML).
    *   [ ] Implement `StateService` (Reactive state management).
*   **Phase 3: Business Logic**
    *   [ ] `GameDetectionService` (Detect game from exe/load order).
    *   [ ] `PluginValidationService` (Parse `plugins.txt`).
    *   [ ] `ProcessExecutionService` (Async wrapper for xEdit processes).

## 5. Build & Run Commands
*   **Restore**: `dotnet restore`
*   **Build**: `dotnet build`
*   **Run**: `dotnet run --project AutoQAC`
*   **Test**: `dotnet test`

## 6. Coding Conventions
*   **Style**: Standard C# .NET conventions (PascalCase for public members, camelCase for locals/_private fields).
*   **Nullability**: Enable nullable reference types (`<Nullable>enable</Nullable>`). Use `required` keyword for models where appropriate.
*   **Async**: Use `async/await` for all I/O and long-running operations. Never block the UI thread.
*   **Logging**: Use structured logging (Serilog) via the `ILoggingService` abstraction.

## 7. Reference Material
*   **Roadmap**: `FEATURE_PARITY_ROADMAP.md` (Contains specific class designs).
*   **Python Logic**: Check `Code_To_Port/AutoQACLib/` when porting specific logic (e.g., regex for parsing xEdit output).
