<!-- OPENSPEC:START -->
# OpenSpec Instructions

These instructions are for AI assistants working in this project.

Always open `@/openspec/AGENTS.md` when the request:
- Mentions planning or proposals (words like proposal, spec, change, plan)
- Introduces new capabilities, breaking changes, architecture shifts, or big performance/security work
- Sounds ambiguous and you need the authoritative spec before coding

Use `@/openspec/AGENTS.md` to learn:
- How to create and apply change proposals
- Spec format and conventions
- Project structure and guidelines

Keep this managed block so 'openspec update' can refresh the instructions.

<!-- OPENSPEC:END -->

# CLAUDE.md

This file provides guidance to Claude Code when working with this C# Avalonia MVVM project.

## Project Overview

This is a C# implementation of XEdit-PACT (Plugin Auto Cleaning Tool) using Avalonia UI framework with MVVM architecture.

## Essential Commands

```bash
# Build the project
dotnet build

# Run the application
dotnet run --project AutoQAC

# Run tests (with automatic coverage collection)
dotnet test

# Build release version
dotnet build -c Release

# Clean build artifacts
dotnet clean
```

## Technology Stack

- **.NET 10**: Target framework (LTS)
- **C# 13**: With nullable reference types enabled
- **Avalonia UI 11.3.11**: Cross-platform XAML-based UI framework
- **ReactiveUI 11.3.8**: MVVM framework for reactive programming
- **Fluent Design**: Microsoft Fluent theme for Avalonia

## Architecture & Design Principles

### MVVM Pattern (Strict Adherence Required)

This project follows strict MVVM architecture:

1. **Models** (`AutoQAC/Models/`):
   - Pure business logic and data structures
   - No UI dependencies (no Avalonia, no ReactiveUI)
   - Reusable, testable business logic
   - Data transfer objects and domain models

2. **ViewModels** (`AutoQAC/ViewModels/`):
   - Inherit from `ReactiveObject` or `ViewModelBase`
   - Presentation logic only
   - Command handling via `ReactiveCommand`
   - Property change notifications via `RaiseAndSetIfChanged`
   - No direct UI manipulation (no controls, no windows)
   - Communicate with Models for business logic

3. **Views** (`AutoQAC/Views/`):
   - XAML files with code-behind
   - Pure UI rendering
   - Data binding to ViewModels
   - No business logic
   - Minimal code-behind (only UI-specific logic)

### Key Architectural Rules

1. **Reactive Programming**:
   - Use ReactiveUI's `ReactiveCommand` for all commands
   - Use `RaiseAndSetIfChanged` for property setters
   - Use `WhenAnyValue` for computed properties
   - Use `ObservableAsPropertyHelper` for derived properties

2. **Async/Await Patterns**:
   - All I/O operations must be async
   - Use `Task` and `async/await` for background operations
   - Never block the UI thread
   - Use `ReactiveCommand.CreateFromTask` for async commands

3. **Dependency Injection**:
   - Services injected via constructor parameters
   - No static dependencies or service locators
   - Use interfaces for testability
   - Register services in `Program.cs` or `App.axaml.cs`

4. **Thread Safety**:
   - All ViewModel properties must be thread-safe
   - Use `Dispatcher.UIThread.InvokeAsync` for cross-thread UI updates
   - Protect shared state with proper synchronization
   - Avoid `lock` statements; prefer async patterns

5. **Error Handling**:
   - Use structured exception handling
   - Log errors appropriately
   - Show user-friendly error messages via dialogs
   - Never swallow exceptions silently

### Project Structure

```
AutoQAC/
├── Models/                  # Business logic and data models
│   ├── Configuration/       # Config file management (YAML)
│   ├── GameDetection/       # Game and xEdit detection
│   ├── PluginCleaning/      # Core cleaning logic (sequential!)
│   └── Logging/             # Logging infrastructure
├── ViewModels/              # Presentation logic
│   ├── MainWindowViewModel.cs
│   ├── ProgressViewModel.cs
│   └── SettingsViewModel.cs
├── Views/                   # XAML UI
│   ├── MainWindow.axaml
│   ├── ProgressWindow.axaml
│   └── SettingsWindow.axaml
├── Assets/                  # Images, icons, resources
├── App.axaml               # Application definition
├── App.axaml.cs            # Application startup and DI setup
├── Program.cs              # Entry point
└── ViewLocator.cs          # Automatic View resolution
```

## C# and Avalonia Best Practices

### Avalonia-Specific Guidelines

1. **Data Binding**:
   - Use compiled bindings by default: `{Binding Property}`
   - For complex scenarios, use `{CompiledBinding Property}`
   - Avoid code-behind data access; use ViewModels

2. **XAML Styling**:
   - Use Fluent theme as the base
   - Define styles in `App.axaml` or separate resource dictionaries
   - Use `StyleInclude` for modular styles

3. **Controls and Layout**:
   - Prefer `Grid`, `StackPanel`, `DockPanel` for layouts
   - Use `ItemsControl`, `ListBox`, `DataGrid` for data display
   - Leverage `ContentControl` for dynamic content

4. **Dialogs and Windows**:
   - Use `Window.ShowDialog()` for modal dialogs
   - Use `Window.Show()` for non-modal windows
   - Return results via `Close(result)` pattern

### C# Code Guidelines

1. **Nullable Reference Types**:
   - Nullable reference types are ENABLED
   - Always annotate nullability: `string?` vs `string`
   - Use null-forgiving operator `!` sparingly and only when certain

2. **Async/Await**:
   - Async methods must end with `Async` suffix
   - Always `await` async operations
   - Never use `.Result` or `.Wait()` (causes deadlocks)
   - Use `ConfigureAwait(false)` in library code

3. **Code Organization**:
   - One class per file
   - File name matches class name
   - Use meaningful namespaces matching folder structure
   - Keep classes focused (Single Responsibility Principle)

4. **Naming Conventions**:
   - PascalCase for public members and types
   - camelCase for private fields (with `_` prefix)
   - UPPER_CASE for constants
   - Descriptive names over abbreviations

## Critical Application Constraints

### Sequential Processing Requirement

**⚠ CRITICAL**: This application can only clean **one plugin at a time** due to xEdit's file locking mechanisms.

- Never attempt parallel or concurrent cleaning operations
- Ensure only one xEdit process runs at any time
- If multiple xEdit windows open, this is a critical bug
- Process plugins in a strict sequential queue

### Implementation Guidelines for Sequential Processing

```csharp
// Example: Sequential processing pattern
public async Task CleanPluginsAsync(IEnumerable<string> plugins, CancellationToken cancellationToken)
{
    // CORRECT: Sequential processing
    foreach (var plugin in plugins)
    {
        await CleanSinglePluginAsync(plugin, cancellationToken);
    }

    // WRONG: DO NOT use parallel processing
    // await Task.WhenAll(plugins.Select(p => CleanSinglePluginAsync(p, cancellationToken)));
}
```

## Critical Technical Constraints

- **Target Framework**: .NET 10 (LTS)
- **C# Version**: 13 with nullable reference types
- **UI Framework**: Avalonia 11.3.11
- **MVVM Framework**: ReactiveUI 11.3.8
- **YAML Library**: YamlDotNet 16.3.0
- **Logging**: Serilog with Console and File sinks
- **Testing**: xUnit 2.9.3 with FluentAssertions 8.8.0 and NSubstitute 5.3.0
- **DI**: Microsoft.Extensions.DependencyInjection 10.0.2
- **Coverage**: Coverlet (MSBuild + Collector) with Cobertura XML output

## xEdit Integration

The application interfaces with xEdit (SSEEdit/FO4Edit) via `System.Diagnostics.Process`:

- Commands built with `-QAC` flag for Quick Auto Clean
- Support MO2 integration via `ModOrganizer.exe run` command wrapper
- Optional `-iknowwhatimdoing -allowmakepartial` flags for Partial Forms
- Timeout handling (default 300s per plugin)
- Async output parsing for ITMs, UDRs, and deleted navmeshes
- **CRITICAL**: Only one xEdit process at a time

### Process Execution Pattern

```csharp
// Example pattern for xEdit subprocess (sequential only!)
public async Task<CleaningResult> CleanPluginAsync(string pluginPath, CancellationToken cancellationToken)
{
    var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = xEditPath,
            Arguments = $"-QAC -autoload \"{pluginName}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        }
    };

    // Async output reading
    process.OutputDataReceived += (sender, args) => { /* Parse output */ };
    process.Start();
    process.BeginOutputReadLine();

    // Wait with timeout
    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

    await process.WaitForExitAsync(linkedCts.Token);

    return ParseResults(/* captured output */);
}
```

## Configuration Files

Located in `AutoQAC Data/`:

- `AutoQAC Main.yaml`: Game configurations, plugin skip lists
- `AutoQAC Config.yaml`: User settings, file paths
- `AutoQAC Ignore.yaml`: Additional ignore list

Use YamlDotNet for deserialization with proper error handling.

## Logging Strategy

- Use structured logging (Serilog recommended)
- Rotating file logs in `logs/` directory
- Log levels: Debug, Information, Warning, Error, Fatal
- Include timestamps, thread IDs, and context
- Never log sensitive user data

## Testing Requirements

- **Unit Tests**: Test ViewModels and Models independently (510+ tests)
- **UI Tests**: Avalonia.Headless for automated UI testing (xUnit v2 required)
- **Coverage Target**: Minimum 80% for critical paths
- **Coverage Tooling**: Coverlet MSBuild auto-collects on every `dotnet test` run
- **Mocking**: NSubstitute for dependency mocking -- ALL optional parameters must be matched in substitute calls (same constraint as Moq). Use NSubstitute.Analyzers.CSharp for compile-time misuse detection.

## Common Pitfalls to Avoid

1. **Parallel Processing**: NEVER attempt to clean multiple plugins simultaneously
2. **UI Thread Deadlocks**: Always use `async/await`, never `.Result` or `.Wait()`
3. **Memory Leaks**: Dispose of `IDisposable` objects, especially event subscriptions
4. **Mixing MVVM Layers**: Keep Models, ViewModels, and Views strictly separated
5. **Blocking Operations**: Never block the UI thread; use `Task.Run()` or async commands
6. **Improper Binding**: Use compiled bindings; avoid string-based bindings
7. **Static State**: Avoid static mutable state; use dependency injection
8. **Exception Swallowing**: Always log exceptions and handle them gracefully

## Development Workflow

1. **Implement Models First**: Business logic before UI
2. **Build ViewModels**: Presentation logic with ReactiveUI
3. **Create Views Last**: XAML UI with data binding
4. **Test Incrementally**: Verify each layer before moving on
5. **Maintain Sequential Processing**: Always process plugins one at a time

## Resources

- **Avalonia Documentation**: https://docs.avaloniaui.net/
- **ReactiveUI Documentation**: https://www.reactiveui.net/docs/
- **C# Async Best Practices**: https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/
- **MVVM Pattern**: https://learn.microsoft.com/en-us/dotnet/architecture/maui/mvvm

## Notes for Claude Code

- Follow the MVVM pattern strictly
- Use ReactiveUI patterns for all ViewModels
- Keep async operations truly asynchronous
- **CRITICAL**: Ensure sequential processing - only one plugin at a time
- Write clean, maintainable C# code with proper error handling
