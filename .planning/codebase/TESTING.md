# Testing Patterns

**Analysis Date:** 2026-03-30

## Test Framework Stack

**Runner:**
- xUnit 2.9.3
- Microsoft.NET.Test.Sdk 18.0.1
- xunit.runner.visualstudio 3.1.5
- Config: MSBuild properties in `.csproj` files (no separate `xunit.runner.json`)

**Assertion Library:**
- FluentAssertions 8.8.0
- Global `using FluentAssertions;` in `QueryPlugins.Tests` (not in `AutoQAC.Tests`)
- Global `using Xunit;` in both test projects

**Mocking Library:**
- NSubstitute 5.3.0
- NSubstitute.Analyzers.CSharp 1.0.17 (compile-time correctness checks)

**Run Commands:**
```bash
dotnet test AutoQACSharp.slnx           # Run all tests with coverage
dotnet test AutoQAC.Tests               # Run AutoQAC tests only
dotnet test QueryPlugins.Tests          # Run QueryPlugins tests only
```

## Coverage Configuration

**Tooling:**
- coverlet.collector 8.0.0 + coverlet.msbuild 8.0.0
- Auto-collected on every `dotnet test` run (MSBuild properties in `.csproj`)

**AutoQAC.Tests coverage config** (`AutoQAC.Tests/AutoQAC.Tests.csproj`):
```xml
<CollectCoverage>true</CollectCoverage>
<CoverletOutputFormat>cobertura</CoverletOutputFormat>
<CoverletOutput>./TestResults/coverage/</CoverletOutput>
<Include>[AutoQAC]*</Include>
<Exclude>[AutoQAC.Tests]*</Exclude>
<ExcludeByAttribute>ExcludeFromCodeCoverage</ExcludeByAttribute>
```

**QueryPlugins.Tests coverage config** (`QueryPlugins.Tests/QueryPlugins.Tests.csproj`):
```xml
<CollectCoverage>true</CollectCoverage>
<CoverletOutputFormat>cobertura</CoverletOutputFormat>
<CoverletOutput>./TestResults/coverage/</CoverletOutput>
<Include>[QueryPlugins]*</Include>
<Exclude>[QueryPlugins.Tests]*</Exclude>
<ExcludeByAttribute>ExcludeFromCodeCoverage</ExcludeByAttribute>
```

**Output Location:** `TestResults/coverage/` within each test project directory.

**View Coverage:**
```bash
dotnet test AutoQACSharp.slnx /p:CollectCoverage=true
# Cobertura XML outputs to each test project's TestResults/coverage/
```

**No enforced coverage threshold** -- coverage is collected but no minimum percentage is gated.

## Test File Organization

**Location:** Separate test projects mirror the source project structure.

**Naming:**
- Test class: `{ClassName}Tests` (e.g., `ConfigurationServiceTests`, `AppStateTests`)
- Test method: `{Method}_Should{Expected}_When{Condition}` or `{Method}_{Scenario}_{ExpectedResult}`
  - Examples: `LoadUserConfig_ShouldCreateDefault_WhenFileNotFound`
  - Examples: `BuildCommand_ReturnsNull_WhenXEditPathIsEmpty`
  - Shorter forms also used: `InitialState_IsEmpty`, `StateChanged_EmitsOnUpdate`

**Structure:**
```
AutoQAC.Tests/
  Integration/
    DependencyInjectionTests.cs           # DI container wiring tests
    GameSelectionIntegrationTests.cs      # Cross-service integration tests
  Models/
    AppStateTests.cs                      # Record computed property tests
    CleaningSessionResultTests.cs         # Result model tests
    PluginCleaningResultTests.cs          # Result model tests
  Services/
    BackupServiceTests.cs                 # File-system-based backup tests
    CleaningOrchestratorTests.cs          # Core orchestration flow tests
    CleaningServiceTests.cs               # Plugin cleaning tests
    ConfigurationServiceTests.cs          # Config load/save/skip list tests
    ConfigurationServiceSkipListTests.cs  # Skip list merging tests
    ConfigWatcherServiceTests.cs          # File watcher tests
    GameDetectionServiceTests.cs          # Game detection from executables
    HangDetectionServiceTests.cs          # CPU monitoring tests
    LegacyMigrationServiceTests.cs        # Config migration tests
    LogRetentionServiceTests.cs           # Log cleanup tests
    LogRetentionServicePathTests.cs       # Log path resolution tests
    PluginIssueApproximationServiceTests.cs  # Mutagen analysis tests
    PluginLoadingServiceTests.cs          # Plugin discovery tests
    PluginValidationServiceTests.cs       # File validation tests
    ProcessExecutionServiceTests.cs       # Process lifecycle tests
    StateServiceTests.cs                  # State management tests
    XEditCommandBuilderTests.cs           # Command construction tests
    XEditLogFileServiceTests.cs           # Log parsing tests
    XEditOutputParserTests.cs             # Output parsing tests
    AppShutdownDisposalTests.cs           # Service disposal on shutdown
  TestInfrastructure/
    RxAppSchedulerTestCollection.cs       # RxApp scheduler test helpers
  ViewModels/
    CleaningResultsViewModelTests.cs
    ErrorDialogTests.cs
    MainWindowViewModelTests.cs
    MainWindowViewModelInitializationTests.cs
    MainWindowThreadingTests.cs           # Threading/scheduler tests
    PartialFormsWarningViewModelTests.cs
    ProgressViewModelTests.cs
    SkipListViewModelTests.cs
  Views/
    ViewSubscriptionLifecycleTests.cs     # Source-code assertion tests

QueryPlugins.Tests/
  Detectors/
    Games/
      Fallout4DetectorTests.cs
      OblivionDetectorTests.cs
      SkyrimDetectorTests.cs
      StarfieldDetectorTests.cs
    ItmDetectorTests.cs
  Models/
    PluginAnalysisResultTests.cs
```

## Test Patterns

### Arrange-Act-Assert

All tests follow strict AAA pattern with `// Arrange`, `// Act`, `// Assert` comments:

```csharp
[Fact]
public async Task LoadUserConfig_ShouldCreateDefault_WhenFileNotFound()
{
    // Arrange
    var service = new ConfigurationService(Substitute.For<ILoggingService>(), _testDirectory);

    // Act
    var config = await service.LoadUserConfigAsync();
    await service.FlushPendingSavesAsync();

    // Assert
    config.Should().NotBeNull();
    File.Exists(expectedPath).Should().BeTrue();
}
```

### Constructor Setup Pattern

Most test classes create mocks and the SUT in the constructor:

```csharp
public sealed class CleaningOrchestratorTests
{
    private readonly ICleaningService _cleaningServiceMock;
    private readonly IStateService _stateServiceMock;
    // ... all mock fields ...
    private readonly CleaningOrchestrator _orchestrator;

    public CleaningOrchestratorTests()
    {
        _cleaningServiceMock = Substitute.For<ICleaningService>();
        _stateServiceMock = Substitute.For<IStateService>();
        // ... create all mocks ...
        _orchestrator = new CleaningOrchestrator(/* all mocks */);
    }
}
```

### Factory Method Pattern

Some test classes use a `CreateViewModel()` or `CreateSut()` factory for flexible setup:

```csharp
private ProgressViewModel CreateViewModel()
{
    return new ProgressViewModel(_stateServiceMock, _orchestratorMock);
}
```

### SUT Naming

System Under Test is named either `_sut` (for simpler services) or by the actual type name:

```csharp
private readonly XEditOutputParser _sut;           // Simple service
private readonly CleaningOrchestrator _orchestrator; // Complex orchestrator
```

## NSubstitute Mocking Patterns

### Basic Mock Setup

```csharp
var logger = Substitute.For<ILoggingService>();
var stateService = Substitute.For<IStateService>();
```

### Return Value Setup

```csharp
// Simple return
_stateServiceMock.CurrentState.Returns(new AppState { XEditExecutablePath = "xEdit.exe" });

// Async return
_configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
    .Returns(new UserConfiguration());

// Return with argument matching
_configServiceMock.GetSkipListAsync(
    Arg.Any<GameType>(),
    Arg.Any<GameVariant>(),
    Arg.Any<CancellationToken>())
    .Returns(new List<string>());
```

### Observable Mock Setup

For services exposing `IObservable<T>`, use `BehaviorSubject<T>` or `Subject<T>`:

```csharp
var stateSubject = new BehaviorSubject<AppState>(new AppState());
_stateServiceMock.StateChanged.Returns(stateSubject);

// Emit state changes in tests:
stateSubject.OnNext(new AppState { IsCleaning = true });

// For observables that should never emit:
_stateServiceMock.CleaningCompleted.Returns(Observable.Never<CleaningSessionResult>());
```

### Verification

```csharp
// Verify call was made
await _orchestratorMock.Received(1)
    .StartCleaningAsync(
        Arg.Any<TimeoutRetryCallback>(),
        Arg.Any<BackupFailureCallback>(),
        Arg.Any<CancellationToken>());

// Verify log message pattern
logger.Received().Error(
    Arg.Any<Exception>(),
    Arg.Is<string>(msg => msg.Contains("Save failed after")),
    Arg.Any<object[]>());
```

### Match Optional Parameters Explicitly

Per project conventions, always match optional parameters explicitly in substitute setups:

```csharp
// Correct: explicit Arg.Any for optional CancellationToken
_configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
    .Returns(new UserConfiguration());

// Correct: explicit Arg.Any for optional GameVariant
_configServiceMock.GetSkipListAsync(
    Arg.Any<GameType>(),
    Arg.Any<GameVariant>(),
    Arg.Any<CancellationToken>())
    .Returns(new List<string>());
```

## Test Infrastructure

### RxApp Scheduler Management (`AutoQAC.Tests/TestInfrastructure/RxAppSchedulerTestCollection.cs`)

ViewModel tests that use ReactiveUI scheduling require special setup:

**Collection Attribute** -- disables parallelization for tests sharing `RxApp.MainThreadScheduler`:
```csharp
[Collection(RxAppSchedulerCollection.Name)]
public sealed class MainWindowViewModelTests : ImmediateMainThreadSchedulerTestBase
```

**ImmediateMainThreadSchedulerTestBase** -- sets `RxApp.MainThreadScheduler = Scheduler.Immediate` for synchronous execution in tests:
```csharp
public abstract class ImmediateMainThreadSchedulerTestBase : IDisposable
{
    private readonly RxAppMainThreadSchedulerScope _schedulerScope = new(Scheduler.Immediate);
    public void Dispose() { _schedulerScope.Dispose(); DisposeCore(); }
    protected virtual void DisposeCore() { }
}
```

**RxAppEventLoopMainThreadSchedulerScope** -- for threading tests that need a real background scheduler thread:
```csharp
using var mainThreadScheduler = new RxAppEventLoopMainThreadSchedulerScope();
var mainThreadId = await WaitForSignalAsync(mainThreadScheduler.ThreadIdTask);
```

**When to use which:**
- Most ViewModel tests: extend `ImmediateMainThreadSchedulerTestBase` and add `[Collection(RxAppSchedulerCollection.Name)]`
- Threading correctness tests: use `RxAppEventLoopMainThreadSchedulerScope` directly
- Non-reactive service tests: no scheduler setup needed

### Async Signal Helpers

Several test classes define reusable signal/wait helpers:

```csharp
private static TaskCompletionSource<bool> CreateSignal()
{
    return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
}

private static async Task WaitForSignalAsync(Task signalTask, string because)
{
    var completedTask = await Task.WhenAny(signalTask, Task.Delay(TimeSpan.FromSeconds(2)));
    completedTask.Should().Be(signalTask, because);
    await signalTask;
}
```

These are defined locally per test class (not in a shared base). Use them for:
- Waiting for observable emissions
- Waiting for cancellation tokens to fire
- Coordinating async test flows with 2-second timeout safety net

### File System Test Isolation

Tests that touch the file system create isolated temp directories:

```csharp
public sealed class ConfigurationServiceTests : IDisposable
{
    private readonly string _testDirectory;

    public ConfigurationServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "AutoQACTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            try { Directory.Delete(_testDirectory, true); }
            catch { /* Ignore cleanup errors */ }
        }
    }
}
```

Pattern: `IDisposable` with temp directory cleanup. `BackupServiceTests` uses the same pattern.

## Test Types

### Unit Tests (Majority)

Service tests with mocked dependencies using NSubstitute:
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs` -- orchestration logic with 11+ mocked dependencies
- `AutoQAC.Tests/Services/XEditCommandBuilderTests.cs` -- command construction
- `AutoQAC.Tests/Services/GameDetectionServiceTests.cs` -- detection from executable names

Model tests for records and computed properties:
- `AutoQAC.Tests/Models/AppStateTests.cs` -- `[Theory]` with `[InlineData]` for computed booleans
- `AutoQAC.Tests/Models/CleaningSessionResultTests.cs`

ViewModel tests with mocked services:
- `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs`
- `AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs`
- `AutoQAC.Tests/ViewModels/SkipListViewModelTests.cs`

### Integration Tests (`AutoQAC.Tests/Integration/`)

**DI Container Tests** (`DependencyInjectionTests.cs`):
- Verify all services resolve from the real DI container
- Verify singleton vs transient lifetime behavior
- Use actual `ServiceCollection` with all `Add*()` extension methods

**Cross-Service Integration** (`GameSelectionIntegrationTests.cs`):
- Build real service provider, resolve real services
- Test Mutagen support detection, game selection persistence
- Use temp directories for config isolation

### Source-Code Assertion Tests (`AutoQAC.Tests/Views/ViewSubscriptionLifecycleTests.cs`)

A unique pattern that reads source `.cs` and `.axaml` files and asserts structural invariants:
```csharp
[Fact]
public void ProgressWindow_ShouldUnsubscribePreviousViewModel_AndGuardDoubleDispose()
{
    var source = File.ReadAllText(GetRepoFilePath("AutoQAC/Views/ProgressWindow.axaml.cs"));
    source.Should().Contain("ProgressViewModel? _subscribedViewModel");
    source.Should().Contain("bool _disposeHandled");
    source.Should().Contain("DisposeViewModelIfNeeded()");
}
```

Purpose: Enforce that Views track subscriptions and dispose correctly -- structural contracts without Avalonia.Headless.

### QueryPlugins Tests

Use in-memory Mutagen mod construction (no file I/O):
```csharp
var mod = new SkyrimMod(PluginKey, SkyrimRelease.SkyrimSE);
var cell = AddInteriorCell(mod);
var placedObj = new PlacedObject(mod) { IsDeleted = true };
cell.Persistent.Add(placedObj);

var issues = _sut.FindDeletedReferences(mod).ToList();
issues.Should().HaveCount(1);
```

Pattern: Build Mutagen mods in memory, run detectors, assert issue counts and types.

### Concurrency Tests

```csharp
[Fact]
public async Task ConcurrentLoadAndSave_ShouldNotCorruptConfigurationState()
{
    var writer = Task.Run(async () => { /* 100 save iterations */ });
    var readers = Enumerable.Range(0, 6).Select(_ => Task.Run(async () => { /* 100 read iterations */ }));
    await Task.WhenAll(readers.Append(writer));
    errors.Should().BeEmpty();
}
```

### Threading Tests

```csharp
[Fact]
public async Task MainWindowViewModel_ShouldMarshalCleaningCommandStateChangesToMainThreadScheduler()
{
    using var mainThreadScheduler = new RxAppEventLoopMainThreadSchedulerScope();
    var mainThreadId = await WaitForSignalAsync(mainThreadScheduler.ThreadIdTask);
    // ... push state from background thread, verify observation on main thread ...
    (await WaitForSignalAsync(observedThread.Task)).Should().Be(mainThreadId);
}
```

## Common FluentAssertions Patterns

```csharp
// Basic value assertions
result.Should().Be(expected);
result.Should().BeNull();
result.Should().NotBeNull();
result.Should().BeTrue();

// Collection assertions
list.Should().BeEmpty();
list.Should().ContainSingle();
list.Should().HaveCount(2);
list.Should().Contain("item");
list.Should().NotContain("item");
list.Should().OnlyContain(x => x != null);

// Exception assertions (async)
Func<Task> act = () => service.LoadMainConfigAsync();
await act.Should().ThrowAsync<Exception>("corrupted YAML should cause an exception");

// Exception assertions (sync)
FluentActions.Invoking(() => service.Dispose())
    .Should().NotThrow("dispose should complete without exception");

// Range assertions
value.Should().BeInRange(1000, 1099);
value.Should().BeGreaterThan(0);

// String assertions
result.Arguments.Should().Contain("-QAC");
result.Arguments.Should().NotContain("-SSE");

// "because" clause for readable failure messages
result.Should().Be(expected, $"LoadOrderPath '{path ?? "null"}' should result in ...");
list.Should().Contain("Skyrim.esm", "user skip list plugins should be included");
```

## Key Test Areas

**Well-Tested:**
- `ConfigurationService` -- load, save, skip lists, debouncing, concurrency, error recovery, per-game overrides (`AutoQAC.Tests/Services/ConfigurationServiceTests.cs`, `ConfigurationServiceSkipListTests.cs`)
- `CleaningOrchestrator` -- full session flow, cancellation, retry, backup integration, MO2 mode (`AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`)
- `StateService` -- state transitions, observable emissions (`AutoQAC.Tests/Services/StateServiceTests.cs`)
- `XEditOutputParser` -- output line parsing, edge cases (`AutoQAC.Tests/Services/XEditOutputParserTests.cs`)
- `XEditCommandBuilder` -- command construction for all game types (`AutoQAC.Tests/Services/XEditCommandBuilderTests.cs`)
- `GameDetectionService` -- executable-based and load-order-based detection (`AutoQAC.Tests/Services/GameDetectionServiceTests.cs`)
- `BackupService` -- backup, restore, session management, retention (`AutoQAC.Tests/Services/BackupServiceTests.cs`)
- `MainWindowViewModel` -- command execution, state propagation, threading (`AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs`, `MainWindowThreadingTests.cs`)
- `ProgressViewModel` -- progress tracking, per-plugin stats (`AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs`)
- QueryPlugins detectors -- Skyrim, Fallout4, Oblivion, Starfield, ITM detection (`QueryPlugins.Tests/Detectors/`)
- DI container wiring (`AutoQAC.Tests/Integration/DependencyInjectionTests.cs`)
- View subscription lifecycle (`AutoQAC.Tests/Views/ViewSubscriptionLifecycleTests.cs`)

**Under-Tested or Not Tested:**
- `CleaningService` -- has tests but complex process-spawning paths are hard to test without real xEdit
- `ProcessExecutionService` -- only non-process-spawning paths tested (startup failure, disposal); no real process tests
- `ConfigWatcherService` -- file system watcher behavior (inherently racy)
- `HangDetectionService` -- limited to observable creation and already-exited process; hard to test real CPU monitoring
- `PluginIssueApproximationService` -- tested with injectable factory but limited coverage of real Mutagen analysis paths
- `PluginLoadingService` -- depends on real game installations for Mutagen registry probing
- `LegacyMigrationService` -- tested but limited scenarios
- `LogRetentionService` -- tested for path resolution and cleanup logic
- View code-behind (`.axaml.cs`) -- no Avalonia.Headless test project; structural assertions only
- Converters (`GameTypeDisplayConverter`, `NullableBoolConverters`) -- no dedicated test files
- `SettingsViewModel`, `RestoreViewModel`, `AboutViewModel` -- no dedicated test files found
- `MessageDialogService`, `FileDialogService` -- UI-dependent, not easily unit-testable

## Writing New Tests

**For a new service:**
1. Create `AutoQAC.Tests/Services/{ServiceName}Tests.cs`
2. Mock all dependencies with `Substitute.For<T>()`
3. Set up default mock returns in constructor (especially for optional parameters)
4. Follow `// Arrange` / `// Act` / `// Assert` pattern
5. Use `#region` blocks to group related test scenarios

**For a new ViewModel:**
1. Create `AutoQAC.Tests/ViewModels/{ViewModelName}Tests.cs`
2. Add `[Collection(RxAppSchedulerCollection.Name)]` attribute
3. Extend `ImmediateMainThreadSchedulerTestBase`
4. Mock services and set up `BehaviorSubject<AppState>` for `StateChanged`
5. Set up `Observable.Never<T>()` for unused observables to prevent test hangs
6. Use `CreateViewModel()` factory if setup varies between tests

**For QueryPlugins tests:**
1. Create test in `QueryPlugins.Tests/Detectors/Games/{GameName}DetectorTests.cs`
2. Build Mutagen mods in-memory with `new SkyrimMod(key, release)`
3. Add records programmatically, set flags (e.g., `IsDeleted = true`)
4. Run detector methods, assert issue counts and types
5. Always test: empty mod, wrong mod type, deleted records, non-deleted records

---

*Testing analysis: 2026-03-30*
