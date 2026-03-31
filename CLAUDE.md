# CLAUDE.md

Guidance for Claude Code when working in this repository.

## Project Overview

- `AutoQAC` is a Windows-only Avalonia desktop app for running xEdit Quick Auto Clean (`-QAC`) safely, one plugin at a time.
- `QueryPlugins` is a separate Mutagen-based analysis library for detecting ITMs, deleted references, and deleted navmeshes.
- The solution currently includes `AutoQAC`, `AutoQAC.Tests`, `QueryPlugins`, and `QueryPlugins.Tests`.

## Essential Commands

```bash
dotnet build AutoQACSharp.slnx
dotnet test AutoQACSharp.slnx
dotnet run --project AutoQAC/AutoQAC.csproj
dotnet build AutoQAC/AutoQAC.csproj -c Release
dotnet clean AutoQACSharp.slnx
```

## Current Stack

- .NET 10
- C# 13 with nullable reference types enabled
- `AutoQAC`: `net10.0-windows10.0.19041.0`
- Avalonia 11.3.12
- ReactiveUI.Avalonia 11.3.8
- Microsoft.Extensions.DependencyInjection 10.0.3
- Serilog 4.3.1 with console and file sinks
- YamlDotNet 16.3.0
- Mutagen 0.53.1
- xUnit 2.9.3, FluentAssertions 8.8.0, NSubstitute 5.3.0, coverlet 8.0.0

## Current Architecture

- `AutoQAC/Infrastructure` contains DI wiring and logging.
- `AutoQAC/Services` contains the main business logic, grouped into `Backup`, `Cleaning`, `Configuration`, `GameDetection`, `MO2`, `Monitoring`, `Plugin`, `Process`, `State`, and `UI`.
- `AutoQAC/ViewModels/MainWindow` splits the main window into `ConfigurationViewModel`, `PluginListViewModel`, and `CleaningCommandsViewModel`, coordinated by `MainWindowViewModel`.
- `MainWindow.axaml.cs` owns dialog and window interactions; ViewModels should not directly manipulate controls.
- `IStateService` and `AppState` are the shared runtime state hub for cleaning progress, plugin lists, and session results.
- `App.axaml.cs` builds the service provider, starts config watching, runs legacy config migration, and triggers log retention cleanup on startup.

## Runtime Behavior To Preserve

- Sequential cleaning is a hard requirement. Do not parallelize plugin cleaning or xEdit launches.
- `ProcessExecutionService` intentionally uses a single process slot.
- `CleaningOrchestrator` owns the end-to-end session flow: flush pending config, validate environment, detect game and variant, apply skip lists, optionally back up plugins, launch xEdit, parse results, and finalize the session.
- Stop behavior is two-stage: graceful cancellation first, then force termination if needed.
- Hang detection is CPU-based and flows through `IHangDetectionService` into the progress UI.
- MO2 mode wraps xEdit with `ModOrganizer.exe run`.
- Backups are skipped in MO2 mode because MO2 uses a virtual filesystem.
- Mutagen-backed plugin discovery is used for `SkyrimLe`, `SkyrimSe`, `SkyrimVr`, `Fallout4`, and `Fallout4Vr`.
- `Fallout3`, `FalloutNewVegas`, and `Oblivion` currently rely on file-based load-order loading.
- Skip list merging includes bundled defaults, user overrides, and variant-specific handling for TTW and Enderal.

## Coding Guidelines

- Maintain strict MVVM boundaries.
- Use `ReactiveCommand`, `RaiseAndSetIfChanged`, `WhenAnyValue`, and `ObservableAsPropertyHelper` for reactive state.
- Keep I/O and process work async; never block the UI thread with `.Result` or `.Wait()`.
- Use constructor injection through `ServiceCollectionExtensions`; avoid static mutable state and service locators.
- Respect Windows-specific assumptions when touching registry probing, executable paths, or process handling.
- If you touch Partial Forms support, verify end-to-end state flow first. The command-line flags exist, but the feature remains experimental.
- Do not modify `Mutagen/`; treat it as read-only.

## Testing Notes

- `AutoQAC.Tests` covers models, services, view models, integration flows, and view subscription lifecycle behavior.
- `QueryPlugins.Tests` covers the standalone detector library.
- `dotnet test` auto-collects Cobertura coverage into each test project's `TestResults/coverage/` directory.
- Use NSubstitute for mocks, and match optional parameters explicitly in substitute setups and assertions.
- There is no separate Avalonia.Headless test project in the current solution. Do not document or depend on one unless you add it intentionally.

## Important Files

- `AutoQAC/App.axaml.cs`
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- `AutoQAC/Services/Process/ProcessExecutionService.cs`
- `AutoQAC/Services/Configuration/ConfigurationService.cs`
- `AutoQAC/Services/Plugin/PluginLoadingService.cs`
- `AutoQAC Data/AutoQAC Main.yaml`
- `AutoQAC Data/AutoQAC Settings.yaml`

## Mutagen Reference

- Check `docs/mutagen/` first for fast lookups.
- The curated docs are useful, but package references in this repo are on Mutagen 0.53.1. If something looks stale or mismatched, verify against the read-only `Mutagen/` submodule.
- Do not build, modify, or add files under `Mutagen/`.

## Common Pitfalls

- Do not parallelize cleaning work.
- Do not assume every game uses a file-based load order; check `PluginLoadingService` first.
- Do not bypass `FlushPendingSavesAsync` before launching xEdit.
- Do not claim UI test infrastructure that is not present.
- Do not revert unrelated working-tree changes.

<!-- GSD:project-start source:PROJECT.md -->
## Project

**AutoQAC — xEdit Log Parsing Fix**

AutoQAC is a Windows-only Avalonia desktop app that runs xEdit Quick Auto Clean (`-QAC`) safely, one plugin at a time, with Mutagen-based plugin analysis. This milestone fixes the fundamental bug where the app tries to parse xEdit's stdout/stderr for cleaning results, when xEdit actually writes its output to log files in its install directory.

**Core Value:** Correctly parse xEdit cleaning results from log files so users get accurate feedback on what was cleaned, skipped, removed, or undeleted.

### Constraints

- **Platform**: Windows-only — xEdit and its log paths are Windows filesystem
- **Sequential**: One xEdit process at a time — no parallelization
- **Read-only Mutagen/**: Do not modify the Mutagen submodule
- **MVVM boundaries**: Service layer reads logs; ViewModels receive parsed results via state
<!-- GSD:project-end -->

<!-- GSD:stack-start source:codebase/STACK.md -->
## Technology Stack

## Languages
- C# 13 with nullable reference types (`<Nullable>enable</Nullable>` in all projects)
- XAML (Avalonia AXAML) for UI layout and resource definitions
## Runtime
- .NET 10 (`net10.0` and `net10.0-windows10.0.19041.0`)
- Windows 10+ only for the main app (`AutoQAC.csproj` targets `net10.0-windows10.0.19041.0`)
- `QueryPlugins` is cross-platform capable (`net10.0`) but only consumed by the Windows app
- NuGet (via `dotnet` CLI)
- No `nuget.config` present; uses default NuGet feeds
- No `global.json` present; relies on whatever SDK is installed
- No `Directory.Build.props`; each `.csproj` is self-contained
## Frameworks
- Avalonia 11.3.12 - Cross-platform UI framework (Windows-only deployment)
- ReactiveUI.Avalonia 11.3.8 - MVVM framework with reactive extensions
- Microsoft.Extensions.DependencyInjection 10.0.3 - IoC container
- xUnit 2.9.3 - Test runner and assertions
- xunit.runner.visualstudio 3.1.5 - VS test adapter
- FluentAssertions 8.8.0 - Fluent assertion library
- NSubstitute 5.3.0 - Mocking framework
- NSubstitute.Analyzers.CSharp 1.0.17 - Static analysis for NSubstitute usage
- Microsoft.NET.Test.Sdk 18.0.1 - Test platform host
- `dotnet build` / `dotnet test` / `dotnet run` (standard .NET CLI)
- Solution format: `.slnx` (XML-based lightweight solution format)
- `AutoQAC/app.manifest` declares Windows 10 compatibility
## Key Dependencies
| Package | Version | Purpose |
|---------|---------|---------|
| Avalonia | 11.3.12 | UI framework (core rendering, controls, themes) |
| Avalonia.Desktop | 11.3.12 | Desktop platform backend |
| Avalonia.Themes.Fluent | 11.3.12 | Fluent Design theme |
| Avalonia.Fonts.Inter | 11.3.12 | Inter font family |
| Avalonia.Controls.DataGrid | 11.3.12 | DataGrid control for plugin lists |
| Avalonia.Diagnostics | 11.3.12 | Dev-only diagnostic overlay (excluded from Release builds) |
| ReactiveUI.Avalonia | 11.3.8 | Reactive MVVM bindings for Avalonia |
| Mutagen.Bethesda | 0.53.1 | Core Bethesda plugin handling (load orders, game locations) |
| Mutagen.Bethesda.Skyrim | 0.53.1 | Skyrim-specific record types (LE, SE, VR, Enderal) |
| Mutagen.Bethesda.Fallout4 | 0.53.1 | Fallout 4 specific record types |
| Mutagen.Bethesda.Starfield | 0.53.1 | Starfield record types (QueryPlugins only) |
| Mutagen.Bethesda.Oblivion | 0.53.1 | Oblivion record types (QueryPlugins only) |
| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.Extensions.DependencyInjection | 10.0.3 | Service container (constructor injection) |
| Serilog | 4.3.1 | Structured logging framework |
| Serilog.Sinks.Console | 6.1.1 | Console log output (Warning+ in production) |
| Serilog.Sinks.File | 7.0.0 | Rolling file log output (5MB limit, 5 retained) |
| YamlDotNet | 16.3.0 | YAML serialization for configuration files |
| Package | Version | Purpose |
|---------|---------|---------|
| coverlet.collector | 8.0.0 | Code coverage collector |
| coverlet.msbuild | 8.0.0 | MSBuild coverage integration (auto-collects on `dotnet test`) |
## Configuration
- `AutoQAC/AutoQAC.csproj`: `WinExe` output, compiled Avalonia bindings enabled, COM interop enabled
- `AutoQAC/AutoQAC.csproj`: Embeds build date as `AssemblyMetadata` via MSBuild `$([System.DateTime]::UtcNow)`
- `AutoQAC/AutoQAC.csproj`: Copies `AutoQAC Data/` folder to output directory (`PreserveNewest`)
- Debug vs Release: `Avalonia.Diagnostics` is included only in Debug builds
- `AutoQAC Data/AutoQAC Main.yaml`: Bundled read-only config (skip lists, xEdit executable names, version info)
- `AutoQAC Data/AutoQAC Settings.yaml`: User-editable config (game selection, paths, timeouts, MO2 mode)
- No `.env` files; all configuration is file-based YAML
- Config directory resolution walks up from `AppContext.BaseDirectory` in Debug mode to find the source `AutoQAC Data` folder
- Auto-collected via MSBuild properties: `<CollectCoverage>true</CollectCoverage>`
- Format: Cobertura XML
- Output: `./TestResults/coverage/` per test project
- Include/Exclude filters scope coverage to the project under test
## Platform Requirements
- .NET 10 SDK (no `global.json` pins a specific version)
- Windows 10+ (Windows SDK 10.0.19041.0 for the main app)
- `dotnet build AutoQACSharp.slnx` builds all four projects
- `dotnet test AutoQACSharp.slnx` runs tests with auto-coverage
- `dotnet run --project AutoQAC/AutoQAC.csproj` launches the app
- Windows 10 or later (declared in `app.manifest`)
- .NET 10 runtime (framework-dependent deployment)
- Pre-built release in `Release/` directory includes `AutoQAC.exe` plus native Avalonia/Skia dependencies
- Native DLLs bundled: `libSkiaSharp.dll`, `libHarfBuzzSharp.dll`, `av_libglesv2.dll`, `D3DCompiler_47_cor3.dll`, WPF interop DLLs
## Mutagen Submodule
- Git submodule at `Mutagen/` pinned to tag `0.53.1` (commit `bdbb6ff`)
- Read-only reference; do not build or modify
- Local docs available in `docs/mutagen/` for fast lookups
- Package references in project files match the submodule version (0.53.1)
## Solution Structure
- `AutoQAC` depends on `QueryPlugins`
- `AutoQAC.Tests` depends on both `AutoQAC` and `QueryPlugins`
- `QueryPlugins.Tests` depends on `QueryPlugins`
<!-- GSD:stack-end -->

<!-- GSD:conventions-start source:CONVENTIONS.md -->
## Conventions

## Naming Patterns
- One public type per file, file named to match the type: `CleaningOrchestrator.cs`, `ICleaningOrchestrator.cs`
- Interfaces get their own file prefixed with `I`: `ICleaningService.cs`, `IStateService.cs`
- AXAML code-behind matches the view name: `MainWindow.axaml` / `MainWindow.axaml.cs`
- Test files mirror source path with `Tests` suffix: `Services/CleaningOrchestrator.cs` -> `Services/CleaningOrchestratorTests.cs`
- PascalCase for all types: `CleaningOrchestrator`, `PluginLoadingService`, `GameDetectionService`
- Sealed by default for service implementations: `public sealed class ConfigurationService`
- Sealed records for immutable data: `public sealed record AppState`, `public sealed record CleaningResult`
- `sealed` on ViewModels: `public sealed class MainWindowViewModel`, `public sealed class CleaningCommandsViewModel`
- Standard `I` prefix: `ICleaningService`, `IStateService`, `ILoggingService`
- Placed in the same namespace as implementations
- PascalCase: `StartCleaningAsync`, `ValidateEnvironmentAsync`, `DetectFromExecutable`
- Async suffix on all async methods: `LoadUserConfigAsync`, `FlushPendingSavesAsync`, `CleanPluginAsync`
- Boolean methods use `Is`/`Has`/`Can` prefix: `IsGameSupportedByMutagen`, `HasApproximationPreview`
- PascalCase: `CurrentState`, `IsCleaning`, `PluginsToClean`
- Boolean properties use `Is`/`Has`/`Can` prefix: `IsLoadOrderConfigured`, `HasMigrationWarning`, `CanStartCleaning`
- Nullable booleans for tri-state validation: `bool? IsXEditPathValid` (null = untouched, true = valid, false = invalid)
- `_camelCase` with underscore prefix: `_stateService`, `_disposables`, `_cleaningCts`
- Some newer code uses `field` keyword with semi-auto properties (C# 13): `set => this.RaiseAndSetIfChanged(ref field, value);`
- PascalCase: `PidFileName`, `GracePeriodMs`, `MainConfigFile`
- Follow directory structure: `AutoQAC.Services.Cleaning`, `AutoQAC.ViewModels.MainWindow`, `AutoQAC.Models.Configuration`
- File-scoped namespace declarations: `namespace AutoQAC.Services.Cleaning;`
## Code Style
- No `.editorconfig` in the project root (only in the Mutagen submodule)
- Indentation: 4 spaces
- Opening braces on new line for types and methods (Allman style)
- Opening braces on same line for lambdas and short blocks
- Expression-bodied members for simple single-expression methods:
- NSubstitute.Analyzers.CSharp included in test projects for mock correctness
- No explicit Roslyn analyzers or StyleCop configured
- Nullable reference types enabled globally (`<Nullable>enable</Nullable>`)
- C# 13 semi-auto properties (field keyword): `set => this.RaiseAndSetIfChanged(ref field, value);`
- Records with `with` expressions for immutable state: `s with { IsCleaning = true }`
- Pattern matching: `is { IsInSkipList: false, IsSelected: true }`, `is not null`
- Collection expressions: `return [];`
- FrozenSet for immutable collections: `Enumerable.Empty<string>().ToFrozenSet()`
- Switch expressions: `gameType switch { GameType.SkyrimSe => "SSE", ... }`
- Primary constructors on services: `public sealed class ProcessExecutionService(ILoggingService logger)`
- Target-typed `new()`: `private readonly Lock _lock = new();`
## Import Organization
- No path aliases configured; all imports use fully qualified namespace references
- `AutoQAC.Tests` project has global `using Xunit;`
- `QueryPlugins.Tests` project has global `using Xunit;` and `using FluentAssertions;`
- Main projects do not use implicit usings (no `<ImplicitUsings>enable</ImplicitUsings>` in `AutoQAC.csproj`)
- Test projects and QueryPlugins have `<ImplicitUsings>enable</ImplicitUsings>`
## Reactive Patterns
- Use `ReactiveCommand.CreateFromTask` for async commands:
- Use `ReactiveCommand.Create` for synchronous commands:
- Guard commands with `canExecute` observables derived from `WhenAnyValue`:
- Two patterns coexist in the codebase:
- `WhenAnyValue` for observing single property changes:
- `CombineLatest` for multi-property guards:
- `ObservableAsPropertyHelper<T>` for derived read-only properties:
- Every ViewModel that subscribes to observables must own a `CompositeDisposable`:
- Add subscriptions to the composite: `_disposables.Add(subscription);`
- Dispose in `Dispose()`: `_disposables.Dispose();`
- `Interaction<TInput, TOutput>` declared on the parent ViewModel (e.g., `MainWindowViewModel`)
- Registered in the View code-behind (e.g., `MainWindow.axaml.cs`)
- Passed to child ViewModels as constructor parameters
- ViewModels invoke: `await _showProgressInteraction.Handle(Unit.Default);`
## Async Patterns
- All I/O and process work is async: `Task<T>` return types with `Async` suffix
- Use `ConfigureAwait(false)` on all awaits in service layer code:
- ViewModel code does NOT use `ConfigureAwait(false)` (stays on UI thread)
- Never block with `.Result` or `.Wait()`
- Accept `CancellationToken ct = default` as the last parameter on all async service methods
- Thread through to inner calls: `await File.ReadAllTextAsync(path, ct)`
- Use linked tokens for composite cancellation: `CancellationTokenSource.CreateLinkedTokenSource(ct)`
- Check `ct.ThrowIfCancellationRequested()` at iteration boundaries
- Discard prefix for intentional fire-and-forget: `_ = RunMigrationAsync(migrationService, viewModel, logger);`
- Always wrap in try/catch to avoid unobserved exceptions
## Error Handling
- Services throw `InvalidOperationException` for configuration/validation failures with actionable messages:
- `ArgumentException` for invalid input: `ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);`
- `ObjectDisposedException` when using disposed services: `ThrowIfDisposed()` guard pattern
- `OperationCanceledException` is caught separately and treated as non-error
- Use `ILoggingService` (wraps Serilog) for all logging
- Structured logging with Serilog message templates:
- Log levels: `Debug` for internal details, `Information` for workflow events, `Warning` for recoverable issues, `Error` for failures
- Tag-prefix pattern for subsystem logs: `[Config]`, `[Termination]`, `[Startup]`, `[Migration]`, `[LogRetention]`
- Session delimiters: `logger.Information("=== AutoQAC Session Start ===");`
- Catch exceptions in command handlers, display via `IMessageDialogService`:
- Separate catch for `InvalidOperationException` (show validation errors) vs general `Exception` (show error dialog)
## DI Patterns
- Extension methods on `IServiceCollection` grouped by layer in `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`:
- **Singleton:** All services, `MainWindowViewModel` (shared state hub)
- **Transient:** All other ViewModels (`ProgressViewModel`, `SettingsViewModel`, etc.) and Views
- No scoped registrations
- Constructor injection everywhere; no service locator pattern
- Primary constructors for simple services: `public sealed class ProcessExecutionService(ILoggingService logger)`
- Traditional constructors for complex setup: `ConfigurationService`, `CleaningOrchestrator`
- Optional dependencies with `= null` default: `IPluginIssueApproximationService? pluginIssueApproximationService = null`
## MVVM Conventions
- Own reactive properties, commands, and business logic coordination
- Never reference Avalonia UI types or controls
- Use `Interaction<TInput, TOutput>` for dialog triggers (not direct window references)
- Manage subscriptions via `CompositeDisposable`
- Implement `IDisposable` when holding subscriptions or resources
- Register `Interaction` handlers that create and show windows/dialogs
- Own dialog/window lifecycle (open, close, result handling)
- `MainWindow.axaml.cs` is the Interaction registration hub
- Views can receive services via constructor for interaction handling (e.g., `IFileDialogService`)
- Track subscriptions in `CompositeDisposable` and dispose on window close
- Split into sub-ViewModels: `ConfigurationViewModel`, `PluginListViewModel`, `CleaningCommandsViewModel`
- Parent orchestrates cross-VM state via `OnStateChanged(AppState state)` callbacks
- Sub-ViewModels receive dependencies via constructor, not parent
- All business logic lives in services, not ViewModels
- Services communicate state via `IStateService` (BehaviorSubject-backed)
- Services are stateless where possible; shared state flows through `AppState` record
- Use `sealed record` for immutable data: `AppState`, `CleaningResult`, `PluginInfo`
- Use `with` expressions for state transitions: `state with { IsCleaning = true }`
- Configuration models use `sealed class` with YAML attributes: `UserConfiguration`
- Enums for categorical values: `GameType`, `CleaningStatus`, `PluginWarningKind`
## Disposal Patterns
- Implement `IDisposable` and/or `IAsyncDisposable` when holding resources
- Use `Interlocked.CompareExchange` for thread-safe dispose guards:
- Flush pending work before disposing (e.g., `ConfigurationService.FlushPendingSavesAsync`)
- Dispose `CompositeDisposable` in `Dispose()`
- Propagate disposal to child ViewModels:
- Override `OnClosed` to dispose subscriptions
- Guard against double-dispose with boolean flags: `bool _disposeHandled`
## Thread Safety
- `Lock _lock` protects `_currentState` read-modify-write
- `BehaviorSubject.OnNext` called outside the lock to prevent subscriber deadlocks
- `volatile` on `_currentState` for visibility across threads
- `SemaphoreSlim _fileLock` for file I/O serialization
- `Lock _stateLock` for in-memory state
- Debounced save pipeline via `Subject<T>.Throttle` + `Observable.Switch`
- `SemaphoreSlim _processSlots = new(1, 1)` enforces single xEdit process
- Locks around `_currentProcess` and `_cleaningCts` references
<!-- GSD:conventions-end -->

<!-- GSD:architecture-start source:ARCHITECTURE.md -->
## Architecture

## Pattern Overview
- Strict MVVM with ReactiveUI: ViewModels never touch Views directly; all dialog/window interactions go through `Interaction<TInput, TOutput>` handlers registered in code-behind
- Centralized immutable state hub (`AppState` record + `IStateService`) with reactive `IObservable` streams driving all UI updates
- Sequential, single-process cleaning pipeline: one xEdit instance at a time, enforced by a `SemaphoreSlim(1,1)` process slot
- Two-project architecture: `AutoQAC` (desktop app) depends on `QueryPlugins` (standalone Mutagen analysis library)
## Project Dependency Graph
```
```
- `AutoQAC` is the WinExe desktop app targeting `net10.0-windows10.0.19041.0`
- `QueryPlugins` is a pure library targeting `net10.0` (no UI dependencies)
- `AutoQAC` references `QueryPlugins` via project reference
- `QueryPlugins` has no dependency on `AutoQAC` -- the dependency is one-directional
- The `Mutagen/` directory is a read-only git submodule for reference only; it is NOT built or referenced by the solution
## Dependency Injection
- Nearly all services are **Singleton** because they share a single `IStateService` instance and maintain long-lived subscriptions
- `MainWindowViewModel` is Singleton (lives for the app lifetime, composes sub-VMs)
- Secondary ViewModels (`ProgressViewModel`, `SettingsViewModel`, `RestoreViewModel`, etc.) are Transient (created per dialog open)
- All Views are Transient
## MVVM Pattern
### ViewModel Hierarchy
```
```
- `MainWindowViewModel` owns `Interaction<TInput, TOutput>` instances that the code-behind registers handlers for
- `MainWindowViewModel` subscribes to `IStateService.StateChanged` and dispatches `OnStateChanged(AppState)` to all three sub-VMs
- Each sub-VM has its own `CompositeDisposable` and implements `IDisposable`
### Standalone ViewModels (Transient, created per dialog):
| ViewModel | Purpose | Created in |
|-----------|---------|------------|
| `ProgressViewModel` | Live cleaning progress, dry-run preview | `MainWindow.axaml.cs` |
| `SettingsViewModel` | Settings editing dialog | `MainWindow.axaml.cs` |
| `SkipListViewModel` | Skip list editing dialog | `MainWindow.axaml.cs` |
| `RestoreViewModel` | Backup restore browser | `MainWindow.axaml.cs` |
| `CleaningResultsViewModel` | Post-session results display | `MainWindow.axaml.cs` |
| `PartialFormsWarningViewModel` | Experimental feature warning | `MainWindow.axaml.cs` |
| `MessageDialogViewModel` | Generic message/error dialogs | `MessageDialogService` |
| `AboutViewModel` | About window | `MainWindow.axaml.cs` |
### View-ViewModel Binding Approach
- `DataContext` is set in code-behind, not in XAML
- Avalonia compiled bindings are enabled by default (`AvaloniaUseCompiledBindingsByDefault=true` in csproj)
- `ViewModelBase` extends `ReactiveObject` for `INotifyPropertyChanged` support
- Dialog results flow through `Interaction<TInput, TOutput>` -- ViewModel raises the interaction, View code-behind handles it by creating/showing the dialog window
### Reactive Patterns Used
| Pattern | Usage |
|---------|-------|
| `RaiseAndSetIfChanged` | All mutable ViewModel properties |
| `ReactiveCommand` | All commands (sync and async variants) |
| `WhenAnyValue` | Computed properties and auto-save triggers |
| `ObservableAsPropertyHelper` | Derived properties like `IsMutagenSupported`, `RequiresLoadOrderFile` |
| `Interaction<TInput, TOutput>` | All ViewModel-to-View dialog interactions |
| `BehaviorSubject<T>` | State service broadcasting (`StateChanged`, `IsTerminatingChanged`) |
| `Subject<T>` | Event streams (`PluginProcessed`, `CleaningCompleted`, `HangDetected`) |
| `CompositeDisposable` | Lifecycle cleanup in all ViewModels |
| `ObserveOn(RxApp.MainThreadScheduler)` | Thread marshaling for UI updates from background state changes |
## Service Layer
### Service Groups and Responsibilities
- `IConfigurationService` / `ConfigurationService` -- YAML read/write with debounced saves (500ms throttle), SHA256 change detection, skip list management, per-game data folder overrides, per-game load order overrides. File: `AutoQAC/Services/Configuration/ConfigurationService.cs`
- `IConfigWatcherService` / `ConfigWatcherService` -- FileSystemWatcher for external YAML edits; compares hash to detect non-app changes and calls `ReloadFromDiskAsync`. File: `AutoQAC/Services/Configuration/ConfigWatcherService.cs`
- `ILegacyMigrationService` / `LegacyMigrationService` -- One-time migration from legacy config format. File: `AutoQAC/Services/Configuration/LegacyMigrationService.cs`
- `ILogRetentionService` / `LogRetentionService` -- Cleanup of old log files on startup. File: `AutoQAC/Services/Configuration/LogRetentionService.cs`
- `IStateService` / `StateService` -- Central state hub. Owns `AppState` (immutable record), provides thread-safe `UpdateState(Func<AppState, AppState>)` with lock-protected read-modify-write. Emits state changes via `BehaviorSubject`. File: `AutoQAC/Services/State/StateService.cs`
- `IPluginLoadingService` / `PluginLoadingService` -- Loads plugins via Mutagen for supported games (SkyrimLE/SE/VR, Fallout4/4VR) or from load order text files for older games (Oblivion, FO3, FNV). File: `AutoQAC/Services/Plugin/PluginLoadingService.cs`
- `IPluginValidationService` / `PluginValidationService` -- Validates plugin files on disk (existence, readability, extension, zero-byte). File: `AutoQAC/Services/Plugin/PluginValidationService.cs`
- `IPluginIssueApproximationService` / `PluginIssueApproximationService` -- Uses `QueryPlugins.IPluginQueryService` to run Mutagen-based ITM/UDR/navmesh analysis for preview counts. Streams results per-plugin via callback. File: `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs`
- `ICleaningOrchestrator` / `CleaningOrchestrator` -- End-to-end session coordinator. Steps: flush pending config, validate environment, detect game/variant, apply skip lists, backup plugins, launch xEdit sequentially, parse results, finalize session. File: `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- `ICleaningService` / `CleaningService` -- Cleans a single plugin by building xEdit command and executing via ProcessExecutionService. File: `AutoQAC/Services/Cleaning/CleaningService.cs`
- `IXEditCommandBuilder` / `XEditCommandBuilder` -- Builds `ProcessStartInfo` for xEdit with correct flags (-QAC, -autoexit, -autoload, game type flag, MO2 wrapping, partial forms). File: `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`
- `IXEditOutputParser` / `XEditOutputParser` -- Parses xEdit stdout/log file output into `CleaningStatistics` (ITMs removed, UDRs fixed). File: `AutoQAC/Services/Cleaning/XEditOutputParser.cs`
- `IXEditLogFileService` / `XEditLogFileService` -- Reads xEdit log files for enriched statistics (preferred over stdout). File: `AutoQAC/Services/Cleaning/XEditLogFileService.cs`
- `IProcessExecutionService` / `ProcessExecutionService` -- Single-slot (`SemaphoreSlim(1,1)`) process executor with timeout, PID tracking file (`autoqac-pids.json`), orphan cleanup, and two-stage termination (graceful CloseMainWindow with 2.5s grace, then force kill). File: `AutoQAC/Services/Process/ProcessExecutionService.cs`
- `IGameDetectionService` / `GameDetectionService` -- Detects game type from xEdit executable name or load order file contents. Detects game variants (TTW, Enderal) from marker plugins in the load order. File: `AutoQAC/Services/GameDetection/GameDetectionService.cs`
- `IMo2ValidationService` / `Mo2ValidationService` -- Validates MO2 executable path. File: `AutoQAC/Services/MO2/MO2ValidationService.cs`
- `IHangDetectionService` / `HangDetectionService` -- Polls process CPU usage; emits hang state when near-zero CPU persists for 60+ seconds. File: `AutoQAC/Services/Monitoring/HangDetectionService.cs`
- `IBackupService` / `BackupService` -- Pre-cleaning plugin backup with session directories, `session.json` metadata, restore capability, and session retention cleanup. Skipped in MO2 mode (MO2 manages files via VFS). File: `AutoQAC/Services/Backup/BackupService.cs`
- `IFileDialogService` / `FileDialogService` -- Wraps Avalonia file/folder dialogs. File: `AutoQAC/Services/UI/FileDialogService.cs`
- `IMessageDialogService` / `MessageDialogService` -- Shows error/warning/confirm/retry dialogs. File: `AutoQAC/Services/UI/MessageDialogService.cs`
### Service Interaction Patterns
- Services communicate through `IStateService` as the shared state hub, not by calling each other's methods directly for state queries
- `CleaningOrchestrator` is the primary coordinator -- it depends on most other services and orchestrates the full cleaning session
- `ConfigurationService` uses a debounced save pipeline (Rx `Throttle` + `Switch`) to coalesce rapid config changes
- `ConfigWatcherService` monitors the YAML file for external edits and triggers `ReloadFromDiskAsync` on `ConfigurationService`
- `PluginIssueApproximationService` bridges `AutoQAC` and `QueryPlugins` by creating Mutagen load order contexts and delegating to `IPluginQueryService`
## State Management
### AppState Record
- Configuration paths: `LoadOrderPath`, `Mo2ExecutablePath`, `XEditExecutablePath`
- Computed validity: `IsLoadOrderConfigured`, `IsMo2Configured`, `IsXEditConfigured`
- Runtime state: `IsCleaning`, `CurrentPlugin`, `CurrentOperation`
- Progress: `Progress`, `TotalPlugins`, `PluginsToClean` (IReadOnlyList)
- Results: `CleanedPlugins`, `FailedPlugins`, `SkippedPlugins` (IReadOnlySet via FrozenSet)
- Settings: `CleaningTimeout`, `Mo2ModeEnabled`, `PartialFormsEnabled`, `CurrentGameType`
### StateService
- Thread-safe via `Lock` (System.Threading.Lock) for the `_currentState` field
- `UpdateState(Func<AppState, AppState>)` does read-modify-write inside the lock, then emits the new state outside the lock via `BehaviorSubject.OnNext`
- Exposes multiple observable streams:
### State Flow
```
```
## Key Workflows
### Cleaning Orchestration Flow
- First click: Cancel CTS + graceful `CloseMainWindow` with 2.5s grace
- Second click (during grace): Immediate force kill of process tree
- Grace period expired without second click: ViewModel prompts user to confirm force kill
### Plugin Loading Flow
### Configuration Management Flow
- `AutoQAC/Services/Configuration/ConfigurationService.cs` -- core read/write
- `AutoQAC/Services/Configuration/ConfigWatcherService.cs` -- external change detection
- `AutoQAC Data/AutoQAC Settings.yaml` -- user settings (YAML)
- `AutoQAC Data/AutoQAC Main.yaml` -- bundled read-only config (skip lists, xEdit names, version)
## QueryPlugins Architecture
- `IItmDetector` -- Cross-game ITM detection via Mutagen link cache comparison
- `IGameSpecificDetector` -- Per-game UDR and navmesh detection, each declares its `SupportedReleases`
- `SkyrimDetector` -- SkyrimLE, SkyrimSE, SkyrimVR
- `Fallout4Detector` -- Fallout4, Fallout4VR
- `StarfieldDetector` -- Starfield
- `OblivionDetector` -- Oblivion
## Entry Points
## Error Handling
- `CleaningOrchestrator` catches `OperationCanceledException` separately from general `Exception`, preserving partial results in both cases
- Services log errors via `ILoggingService` and propagate exceptions to callers
- ViewModels catch service exceptions and display them via `IMessageDialogService` or validation error UI
- `ConfigurationService` uses retry logic for file I/O failures
- `ProcessExecutionService` tracks PIDs and cleans orphaned processes on startup
## Cross-Cutting Concerns
<!-- GSD:architecture-end -->

<!-- GSD:workflow-start source:GSD defaults -->
## GSD Workflow Enforcement

Before using Edit, Write, or other file-changing tools, start work through a GSD command so planning artifacts and execution context stay in sync.

Use these entry points:
- `/gsd:quick` for small fixes, doc updates, and ad-hoc tasks
- `/gsd:debug` for investigation and bug fixing
- `/gsd:execute-phase` for planned phase work

Do not make direct repo edits outside a GSD workflow unless the user explicitly asks to bypass it.
<!-- GSD:workflow-end -->

<!-- GSD:profile-start -->
## Developer Profile

> Profile not yet configured. Run `/gsd:profile-user` to generate your developer profile.
> This section is managed by `generate-claude-profile` -- do not edit manually.
<!-- GSD:profile-end -->
