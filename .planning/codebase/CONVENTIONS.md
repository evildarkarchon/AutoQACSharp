# Coding Conventions

**Analysis Date:** 2026-02-06

## Naming Patterns

**Files:**
- Class files match class name exactly: `AppState.cs`, `CleaningService.cs`
- Interface files prefixed with `I`: `IConfigurationService.cs`, `ICleaningService.cs`
- Test files suffixed with `Tests`: `AppStateTests.cs`, `CleaningServiceTests.cs`
- ViewModel files suffixed with `ViewModel`: `MainWindowViewModel.cs`, `ProgressViewModel.cs`
- View files match ViewModel but with `View` or window type: `MainWindow.axaml`, `ProgressWindow.axaml`

**Functions/Methods:**
- PascalCase for all public methods: `CleanPluginAsync()`, `LoadMainConfigAsync()`
- Async methods must end with `Async` suffix: `CleanPluginAsync()`, `ExecuteAsync()`, `LoadMainConfigAsync()`
- Command properties end with `Command`: `StartCleaningCommand`, `ConfigureLoadOrderCommand`, `ExportReportCommand`
- Observable helper properties follow pattern: `_isMutagenSupported` (private backing field)

**Variables:**
- camelCase for local variables and parameters: `maxProcesses`, `pluginPath`, `outputLines`
- PascalCase for public properties: `LoadOrderPath`, `XEditPath`, `Mo2Path`
- Private fields prefixed with underscore: `_configService`, `_logger`, `_fileDialog`
- Private backing fields for observables: `_isMutagenSupported`, `_requiresLoadOrderFile`

**Types/Classes:**
- PascalCase for all type names: `AppState`, `CleaningService`, `ProcessExecutionService`
- Interface names start with `I`: `IConfigurationService`, `IStateService`, `ILoggingService`
- Records use PascalCase: `AppState`, `CleaningResult`, `ProcessResult`
- Enums use PascalCase values: `GameType.SkyrimSe`, `CleaningStatus.Cleaned`

**Constants:**
- Not widely used; when present, use UPPER_CASE (limited examples in codebase)

## Code Style

**Formatting:**
- Implicit usings enabled via `<ImplicitUsings>enable</ImplicitUsings>`
- Top-level namespace declarations: `namespace AutoQAC.ViewModels;` (not enclosed in braces)
- Nullable reference types enabled: `<Nullable>enable</Nullable>`
- Type annotations always include nullability: `string?` vs `string`
- Null-forgiving operator `!` used sparingly and only when certain of non-null value

**Linting:**
- No explicit .editorconfig or ESLint configuration detected
- Code follows implicit .NET conventions and Visual Studio defaults
- Consistent spacing and indentation (4 spaces)

## Import Organization

**Order:**
1. System namespaces: `using System;`, `using System.Collections.Generic;`
2. System.* extended namespaces: `using System.Reactive;`, `using System.Reactive.Linq;`
3. Third-party libraries: `using YamlDotNet.Serialization;`, `using Serilog;`
4. Avalonia/ReactiveUI: `using Avalonia.Controls;`, `using ReactiveUI;`
5. Project namespaces: `using AutoQAC.Models;`, `using AutoQAC.Services.Cleaning;`

**Path Aliases:**
- No global using aliases detected
- Namespaces fully qualified throughout codebase

## Error Handling

**Patterns:**
- Structured try-catch blocks with specific exception types
- Exceptions logged via `ILoggingService` with `Error()` and `Fatal()` methods
- User-facing errors shown via `IMessageDialogService` dialogs
- Exceptions generally not swallowed—errors propagated with logging

Example pattern from `CleaningService.cs`:
```csharp
try
{
    if (plugin.IsInSkipList)
    {
        return new CleaningResult { Success = true, Status = CleaningStatus.Skipped };
    }
    // ... processing ...
}
catch (Exception ex)
{
    _logger.Error(ex, "Error cleaning plugin: {PluginName}", plugin.FileName);
    return new CleaningResult
    {
        Success = false,
        Status = CleaningStatus.Failed,
        Message = ex.Message
    };
}
```

**Async Exception Handling:**
- `async/await` used throughout; no `.Result` or `.Wait()` calls
- `ConfigureAwait(false)` used in service layer code: `.ConfigureAwait(false)`
- CancellationToken passed through all async chains: `async Task Method(CancellationToken ct)`

## Logging

**Framework:** Serilog

**Configuration:** `LoggingService.cs` initializes Serilog with:
- Minimum level: Debug
- Console output restricted to Warning level
- Rolling file logs: `logs/autoqac-.log` with daily rolling, 5MB size limit, 5 files retained
- Output template includes timestamp, level, message, and exception

**Patterns:**
- Log at entry/exit points: `_logger.Information()`, `_logger.Debug()`
- Use structured logging with named parameters: `_logger.Information("Loading config for game: {GameType}", gameType)`
- Log errors with exception: `_logger.Error(ex, "Error message: {Details}", details)`
- Level mapping:
  - `Debug`: Low-level diagnostic info, flow tracing
  - `Information`: Important business events
  - `Warning`: Recoverable issues
  - `Error`: Exceptions caught and handled
  - `Fatal`: Critical unrecoverable failures

## Comments

**When to Comment:**
- XML documentation (`///`) on public types and methods: `/// <summary>`, `/// <param>`, `/// <returns>`
- Strategic inline comments for non-obvious logic (rare in this codebase)
- Region markers used to organize large classes: `#region Summary Properties`

**JSDoc/TSDoc:**
- C# style with XML documentation tags
- Summary, parameter descriptions, and return type documentation on public members
- Example from `CleaningResultsViewModel.cs`:
```csharp
/// <summary>
/// ViewModel for the cleaning results summary window.
/// </summary>
public sealed class CleaningResultsViewModel : ViewModelBase
{
    /// <summary>
    /// Design-time constructor for XAML previewer.
    /// </summary>
    public CleaningResultsViewModel()
```

## Function Design

**Size:**
- Most public methods 20-60 lines
- Complex logic extracted into private helper methods
- Single responsibility principle strictly enforced

**Parameters:**
- Dependency injection via constructor parameters (all services)
- Method parameters use CancellationToken as last parameter: `(PluginInfo plugin, IProgress<string>? progress = null, CancellationToken ct = default)`
- Optional parameters use null-coalescing defaults: `IProgress<string>? progress = null`

**Return Values:**
- Explicit return types (no implicit returns)
- Async methods return `Task` or `Task<T>`: `Task<CleaningResult>`, `Task<ProcessResult>`
- Methods returning multiple values use dedicated Result records: `CleaningResult`, `ProcessResult`
- Observables returned for reactive properties: `IObservable<UserConfiguration>`

## Module Design

**Exports:**
- Public interfaces define contracts: `IConfigurationService`, `ICleaningService`
- Implementation classes marked `sealed`: `sealed class ConfigurationService`, `sealed class CleaningService`
- Private fields injected via constructor, never exposed
- Public properties expose reactive state, never raw fields

**Barrel Files:**
- Not widely used in this codebase
- Services organized into logical folders: `Services/Cleaning/`, `Services/Configuration/`, `Services/UI/`

## MVVM Strict Adherence

**Models Layer** (`AutoQAC/Models/`):
- Pure data structures with no UI or service dependencies
- Records used for immutability: `public sealed record AppState`
- Properties use init-only setters: `public string? LoadOrderPath { get; init; }`
- Computed properties derive from other properties: `public bool IsLoadOrderConfigured => !string.IsNullOrEmpty(LoadOrderPath)`

**ViewModels Layer** (`AutoQAC/ViewModels/`):
- Inherit from `ViewModelBase` which extends `ReactiveObject`
- All property changes via `RaiseAndSetIfChanged`: `this.RaiseAndSetIfChanged(ref _loadOrderPath, value)`
- Computed properties via `ObservableAsPropertyHelper`: `private readonly ObservableAsPropertyHelper<bool> _isMutagenSupported;`
- Commands are `ReactiveCommand`: `ReactiveCommand.Create()`, `ReactiveCommand.CreateFromTask()`
- No direct UI manipulation; no Avalonia control references
- Services injected via constructor, stored as private readonly fields

**Views Layer** (`AutoQAC/Views/`):
- XAML files with minimal code-behind
- Data binding to ViewModels: `{Binding PropertyName}`
- No business logic; UI rendering only

## Reactive Programming Patterns

**Observables:**
- `IObservable<T>` returned by services for reactive events
- Example: `IObservable<UserConfiguration>` for configuration changes
- Subscribed in ViewModels with `WhenAnyValue()`: `.WhenAnyValue(x => x.Property)`

**Subscriptions:**
- Subscriptions managed via `CompositeDisposable` in ViewModels
- Added to disposable collection: `subscription.DisposeWith(_disposables)`
- `OnActivated()` pattern for subscription lifecycle

**Commands:**
- `ReactiveCommand.Create()` for synchronous operations
- `ReactiveCommand.CreateFromTask()` for async operations
- Commands can be disabled based on observables via `.WhenAnyValue()` conditions

## Dependency Injection

**Service Registration:**
- Centralized in `ServiceCollectionExtensions.cs` with extension methods
- Methods organized by layer: `AddInfrastructure()`, `AddConfiguration()`, `AddState()`, `AddBusinessLogic()`, `AddUiServices()`, `AddViewModels()`, `AddViews()`
- Scoped lifetimes:
  - Singleton: `IStateService`, `IConfigurationService` (shared state)
  - Transient: ViewModels (new instance per request)
  - Transient: Services that operate on state

**Constructor Injection:**
- All services injected via constructor parameters
- No static dependencies or service locators
- Interfaces used for all external dependencies (testability)

## Thread Safety & Async Patterns

**Async/Await:**
- All I/O operations async: file reads, process execution, network calls
- `async/await` used exclusively; never `.Result` or `.Wait()`
- `ConfigureAwait(false)` in library code to avoid UI thread capture
- `CancellationToken` threaded through all async chains

**Thread Synchronization:**
- `SemaphoreSlim` for resource pooling: `_fileLock = new(1, 1)` in ConfigurationService
- `SemaphoreSlim` for process slot management: `_processSlots = new(maxProcesses, maxProcesses)`
- Async patterns preferred over locks for UI-safe operations

**UI Thread Handling:**
- `Dispatcher.UIThread.InvokeAsync()` for cross-thread UI updates (when needed)
- ReactiveUI handles most threading automatically via RxApp.MainThreadScheduler

---

*Convention analysis: 2026-02-06*
