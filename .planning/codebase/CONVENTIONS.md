# Coding Conventions

**Analysis Date:** 2026-03-30

## Naming Patterns

**Files:**
- One public type per file, file named to match the type: `CleaningOrchestrator.cs`, `ICleaningOrchestrator.cs`
- Interfaces get their own file prefixed with `I`: `ICleaningService.cs`, `IStateService.cs`
- AXAML code-behind matches the view name: `MainWindow.axaml` / `MainWindow.axaml.cs`
- Test files mirror source path with `Tests` suffix: `Services/CleaningOrchestrator.cs` -> `Services/CleaningOrchestratorTests.cs`

**Classes:**
- PascalCase for all types: `CleaningOrchestrator`, `PluginLoadingService`, `GameDetectionService`
- Sealed by default for service implementations: `public sealed class ConfigurationService`
- Sealed records for immutable data: `public sealed record AppState`, `public sealed record CleaningResult`
- `sealed` on ViewModels: `public sealed class MainWindowViewModel`, `public sealed class CleaningCommandsViewModel`

**Interfaces:**
- Standard `I` prefix: `ICleaningService`, `IStateService`, `ILoggingService`
- Placed in the same namespace as implementations

**Methods:**
- PascalCase: `StartCleaningAsync`, `ValidateEnvironmentAsync`, `DetectFromExecutable`
- Async suffix on all async methods: `LoadUserConfigAsync`, `FlushPendingSavesAsync`, `CleanPluginAsync`
- Boolean methods use `Is`/`Has`/`Can` prefix: `IsGameSupportedByMutagen`, `HasApproximationPreview`

**Properties:**
- PascalCase: `CurrentState`, `IsCleaning`, `PluginsToClean`
- Boolean properties use `Is`/`Has`/`Can` prefix: `IsLoadOrderConfigured`, `HasMigrationWarning`, `CanStartCleaning`
- Nullable booleans for tri-state validation: `bool? IsXEditPathValid` (null = untouched, true = valid, false = invalid)

**Private Fields:**
- `_camelCase` with underscore prefix: `_stateService`, `_disposables`, `_cleaningCts`
- Some newer code uses `field` keyword with semi-auto properties (C# 13): `set => this.RaiseAndSetIfChanged(ref field, value);`

**Constants:**
- PascalCase: `PidFileName`, `GracePeriodMs`, `MainConfigFile`

**Namespaces:**
- Follow directory structure: `AutoQAC.Services.Cleaning`, `AutoQAC.ViewModels.MainWindow`, `AutoQAC.Models.Configuration`
- File-scoped namespace declarations: `namespace AutoQAC.Services.Cleaning;`

## Code Style

**Formatting:**
- No `.editorconfig` in the project root (only in the Mutagen submodule)
- Indentation: 4 spaces
- Opening braces on new line for types and methods (Allman style)
- Opening braces on same line for lambdas and short blocks
- Expression-bodied members for simple single-expression methods:
  ```csharp
  private static bool RequiresFileLoadOrder(GameType gameType) => gameType switch { ... };
  ```

**Linting:**
- NSubstitute.Analyzers.CSharp included in test projects for mock correctness
- No explicit Roslyn analyzers or StyleCop configured
- Nullable reference types enabled globally (`<Nullable>enable</Nullable>`)

**C# Version Features Used:**
- C# 13 semi-auto properties (field keyword): `set => this.RaiseAndSetIfChanged(ref field, value);`
- Records with `with` expressions for immutable state: `s with { IsCleaning = true }`
- Pattern matching: `is { IsInSkipList: false, IsSelected: true }`, `is not null`
- Collection expressions: `return [];`
- FrozenSet for immutable collections: `Enumerable.Empty<string>().ToFrozenSet()`
- Switch expressions: `gameType switch { GameType.SkyrimSe => "SSE", ... }`
- Primary constructors on services: `public sealed class ProcessExecutionService(ILoggingService logger)`
- Target-typed `new()`: `private readonly Lock _lock = new();`

## Import Organization

**Order:**
1. `System.*` namespaces
2. Third-party namespaces (Avalonia, ReactiveUI, YamlDotNet, Serilog, Mutagen)
3. Project namespaces (`AutoQAC.*`, `QueryPlugins.*`)

**Path Aliases:**
- No path aliases configured; all imports use fully qualified namespace references

**Global Usings:**
- `AutoQAC.Tests` project has global `using Xunit;`
- `QueryPlugins.Tests` project has global `using Xunit;` and `using FluentAssertions;`
- Main projects do not use implicit usings (no `<ImplicitUsings>enable</ImplicitUsings>` in `AutoQAC.csproj`)
- Test projects and QueryPlugins have `<ImplicitUsings>enable</ImplicitUsings>`

## Reactive Patterns

**ReactiveCommand Usage:**
- Use `ReactiveCommand.CreateFromTask` for async commands:
  ```csharp
  StartCleaningCommand = ReactiveCommand.CreateFromTask(StartCleaningAsync, canStart);
  ```
- Use `ReactiveCommand.Create` for synchronous commands:
  ```csharp
  ExitCommand = ReactiveCommand.Create(Exit);
  DismissValidationCommand = ReactiveCommand.Create(() => { ... });
  ```
- Guard commands with `canExecute` observables derived from `WhenAnyValue`:
  ```csharp
  var canStart = this.WhenAnyValue(x => x.CanStartCleaning);
  StartCleaningCommand = ReactiveCommand.CreateFromTask(StartCleaningAsync, canStart);
  ```

**RaiseAndSetIfChanged:**
- Two patterns coexist in the codebase:
  1. Traditional backing field (used in `ProgressViewModel`, `ConfigurationViewModel`):
     ```csharp
     private string? _currentPlugin;
     public string? CurrentPlugin
     {
         get => _currentPlugin;
         set => this.RaiseAndSetIfChanged(ref _currentPlugin, value);
     }
     ```
  2. C# 13 semi-auto property with `field` keyword (used in `CleaningCommandsViewModel`, `PluginListViewModel`):
     ```csharp
     public string StatusText
     {
         get;
         set => this.RaiseAndSetIfChanged(ref field, value);
     } = "Ready";
     ```
  - New code should prefer the semi-auto property pattern (pattern 2) when the backing field serves no other purpose.

**WhenAnyValue / ObservableAsPropertyHelper:**
- `WhenAnyValue` for observing single property changes:
  ```csharp
  var canShowSkipList = this.WhenAnyValue(x => x.IsCleaning).Select(cleaning => !cleaning);
  ```
- `CombineLatest` for multi-property guards:
  ```csharp
  var canSelectPlugins = hasPlugins.CombineLatest(isCleaning, (hasP, cleaning) => hasP && !cleaning);
  ```
- `ObservableAsPropertyHelper<T>` for derived read-only properties:
  ```csharp
  private readonly ObservableAsPropertyHelper<bool> _isMutagenSupported;
  public bool IsMutagenSupported => _isMutagenSupported.Value;
  ```

**CompositeDisposable:**
- Every ViewModel that subscribes to observables must own a `CompositeDisposable`:
  ```csharp
  private readonly CompositeDisposable _disposables = new();
  ```
- Add subscriptions to the composite: `_disposables.Add(subscription);`
- Dispose in `Dispose()`: `_disposables.Dispose();`

**Interaction Pattern:**
- `Interaction<TInput, TOutput>` declared on the parent ViewModel (e.g., `MainWindowViewModel`)
- Registered in the View code-behind (e.g., `MainWindow.axaml.cs`)
- Passed to child ViewModels as constructor parameters
- ViewModels invoke: `await _showProgressInteraction.Handle(Unit.Default);`

## Async Patterns

**Async/Await:**
- All I/O and process work is async: `Task<T>` return types with `Async` suffix
- Use `ConfigureAwait(false)` on all awaits in service layer code:
  ```csharp
  await configService.FlushPendingSavesAsync(ct).ConfigureAwait(false);
  ```
- ViewModel code does NOT use `ConfigureAwait(false)` (stays on UI thread)
- Never block with `.Result` or `.Wait()`

**CancellationToken:**
- Accept `CancellationToken ct = default` as the last parameter on all async service methods
- Thread through to inner calls: `await File.ReadAllTextAsync(path, ct)`
- Use linked tokens for composite cancellation: `CancellationTokenSource.CreateLinkedTokenSource(ct)`
- Check `ct.ThrowIfCancellationRequested()` at iteration boundaries

**Fire-and-Forget:**
- Discard prefix for intentional fire-and-forget: `_ = RunMigrationAsync(migrationService, viewModel, logger);`
- Always wrap in try/catch to avoid unobserved exceptions

## Error Handling

**Exception Strategy:**
- Services throw `InvalidOperationException` for configuration/validation failures with actionable messages:
  ```csharp
  throw new InvalidOperationException(
      "Cannot start cleaning: game type could not be determined. " +
      "Please select a game type in Settings...");
  ```
- `ArgumentException` for invalid input: `ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);`
- `ObjectDisposedException` when using disposed services: `ThrowIfDisposed()` guard pattern
- `OperationCanceledException` is caught separately and treated as non-error

**Logging:**
- Use `ILoggingService` (wraps Serilog) for all logging
- Structured logging with Serilog message templates:
  ```csharp
  logger.Information("Detected game type: {GameType}", detectedGame);
  logger.Error(ex, "Error during cleaning workflow");
  ```
- Log levels: `Debug` for internal details, `Information` for workflow events, `Warning` for recoverable issues, `Error` for failures
- Tag-prefix pattern for subsystem logs: `[Config]`, `[Termination]`, `[Startup]`, `[Migration]`, `[LogRetention]`
- Session delimiters: `logger.Information("=== AutoQAC Session Start ===");`

**ViewModel Error Handling:**
- Catch exceptions in command handlers, display via `IMessageDialogService`:
  ```csharp
  catch (Exception ex)
  {
      StatusText = $"Error: {ex.Message}";
      _logger.Error(ex, "StartCleaningAsync failed");
      await _messageDialog.ShowErrorAsync("Cleaning Failed", "An error occurred...", $"Error: {ex.Message}");
  }
  ```
- Separate catch for `InvalidOperationException` (show validation errors) vs general `Exception` (show error dialog)

## DI Patterns

**Registration:**
- Extension methods on `IServiceCollection` grouped by layer in `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`:
  - `AddInfrastructure()` - logging
  - `AddConfiguration()` - config, watchers, migration, log retention
  - `AddState()` - state service
  - `AddBusinessLogic()` - all domain services
  - `AddUiServices()` - file dialogs, message dialogs
  - `AddViewModels()` - all view models
  - `AddViews()` - all views

**Lifetime Choices:**
- **Singleton:** All services, `MainWindowViewModel` (shared state hub)
- **Transient:** All other ViewModels (`ProgressViewModel`, `SettingsViewModel`, etc.) and Views
- No scoped registrations

**Injection:**
- Constructor injection everywhere; no service locator pattern
- Primary constructors for simple services: `public sealed class ProcessExecutionService(ILoggingService logger)`
- Traditional constructors for complex setup: `ConfigurationService`, `CleaningOrchestrator`
- Optional dependencies with `= null` default: `IPluginIssueApproximationService? pluginIssueApproximationService = null`

## MVVM Conventions

**ViewModel Responsibilities:**
- Own reactive properties, commands, and business logic coordination
- Never reference Avalonia UI types or controls
- Use `Interaction<TInput, TOutput>` for dialog triggers (not direct window references)
- Manage subscriptions via `CompositeDisposable`
- Implement `IDisposable` when holding subscriptions or resources

**View Responsibilities (code-behind):**
- Register `Interaction` handlers that create and show windows/dialogs
- Own dialog/window lifecycle (open, close, result handling)
- `MainWindow.axaml.cs` is the Interaction registration hub
- Views can receive services via constructor for interaction handling (e.g., `IFileDialogService`)
- Track subscriptions in `CompositeDisposable` and dispose on window close

**MainWindowViewModel Decomposition:**
- Split into sub-ViewModels: `ConfigurationViewModel`, `PluginListViewModel`, `CleaningCommandsViewModel`
- Parent orchestrates cross-VM state via `OnStateChanged(AppState state)` callbacks
- Sub-ViewModels receive dependencies via constructor, not parent

**Service Responsibilities:**
- All business logic lives in services, not ViewModels
- Services communicate state via `IStateService` (BehaviorSubject-backed)
- Services are stateless where possible; shared state flows through `AppState` record

**Model Conventions:**
- Use `sealed record` for immutable data: `AppState`, `CleaningResult`, `PluginInfo`
- Use `with` expressions for state transitions: `state with { IsCleaning = true }`
- Configuration models use `sealed class` with YAML attributes: `UserConfiguration`
- Enums for categorical values: `GameType`, `CleaningStatus`, `PluginWarningKind`

## Disposal Patterns

**Service Disposal:**
- Implement `IDisposable` and/or `IAsyncDisposable` when holding resources
- Use `Interlocked.CompareExchange` for thread-safe dispose guards:
  ```csharp
  if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0) return;
  ```
- Flush pending work before disposing (e.g., `ConfigurationService.FlushPendingSavesAsync`)

**ViewModel Disposal:**
- Dispose `CompositeDisposable` in `Dispose()`
- Propagate disposal to child ViewModels:
  ```csharp
  public void Dispose()
  {
      _disposables.Dispose();
      Configuration.Dispose();
      PluginList.Dispose();
      Commands.Dispose();
  }
  ```

**View Disposal:**
- Override `OnClosed` to dispose subscriptions
- Guard against double-dispose with boolean flags: `bool _disposeHandled`

## Thread Safety

**State Service:**
- `Lock _lock` protects `_currentState` read-modify-write
- `BehaviorSubject.OnNext` called outside the lock to prevent subscriber deadlocks
- `volatile` on `_currentState` for visibility across threads

**Configuration Service:**
- `SemaphoreSlim _fileLock` for file I/O serialization
- `Lock _stateLock` for in-memory state
- Debounced save pipeline via `Subject<T>.Throttle` + `Observable.Switch`

**Process Service:**
- `SemaphoreSlim _processSlots = new(1, 1)` enforces single xEdit process
- Locks around `_currentProcess` and `_cleaningCts` references

---

*Convention analysis: 2026-03-30*
