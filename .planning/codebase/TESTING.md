# Testing Patterns

**Analysis Date:** 2026-04-29

## Test Framework

**Runner:**
- xUnit 2.9.3 with `Microsoft.NET.Test.Sdk` 18.0.1.
- Config: test settings are embedded in project files rather than a separate runner config: `AutoQAC.Tests/AutoQAC.Tests.csproj`, `QueryPlugins.Tests/QueryPlugins.Tests.csproj`.
- Solution entry point: `AutoQACSharp.slnx` includes `AutoQAC.Tests/AutoQAC.Tests.csproj`, `QueryPlugins.Tests/QueryPlugins.Tests.csproj`, and `AutoQAC.Tests/TestProcessHelper/AutoQAC.TestProcessHelper.csproj`.

**Assertion Library:**
- FluentAssertions 8.8.0 in both test projects.
- xUnit assertions are not the dominant style; prefer `Should()` assertions with because-clauses for behavior intent, as in `AutoQAC.Tests/Services/BackupServiceTests.cs` and `QueryPlugins.Tests/Detectors/ItmDetectorTests.cs`.

**Run Commands:**
```bash
dotnet test AutoQACSharp.slnx                    # Run all tests
dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj   # Run AutoQAC tests
dotnet test QueryPlugins.Tests/QueryPlugins.Tests.csproj # Run QueryPlugins tests
dotnet test AutoQACSharp.slnx --filter FullyQualifiedName~ProcessExecutionServiceTests # Filter by test class/name
dotnet test AutoQACSharp.slnx                    # Coverage is auto-collected by coverlet settings
```

## Test File Organization

**Location:**
- Tests live in separate sibling projects: `AutoQAC.Tests/` for `AutoQAC/`, `QueryPlugins.Tests/` for `QueryPlugins/`.
- Test folders mirror production categories: `AutoQAC.Tests/Services/`, `AutoQAC.Tests/ViewModels/`, `AutoQAC.Tests/Models/`, `AutoQAC.Tests/Integration/`, `AutoQAC.Tests/Views/`, `QueryPlugins.Tests/Detectors/`, `QueryPlugins.Tests/Models/`.
- Shared test doubles live under `AutoQAC.Tests/TestInfrastructure/`, such as `AutoQAC.Tests/TestInfrastructure/SynchronousUiDispatcher.cs`.
- A helper executable for process-related tests lives in `AutoQAC.Tests/TestProcessHelper/` and is referenced by `AutoQAC.Tests/AutoQAC.Tests.csproj` without compiling helper sources into the main test assembly.

**Naming:**
- Test classes use `{Subject}Tests`: `BackupServiceTests`, `CleaningOrchestratorTests`, `PluginAnalysisResultTests`.
- Test methods use `Subject_Condition_ExpectedResult`: `LoadUserConfig_ShouldCreateDefault_WhenFileNotFound` in `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`, `MiddleOverride_IdenticalToPreviousVersion_IsFlagged` in `QueryPlugins.Tests/Detectors/ItmDetectorTests.cs`.
- Use `[Theory]` with `[InlineData]` for input variations: restore root cases in `AutoQAC.Tests/Services/BackupServiceTests.cs`, parameterized ViewModel tests in `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs`.

**Structure:**
```
AutoQAC.Tests/
├── Integration/                 # DI and cross-service behavior
├── Models/                      # Record/result model behavior
├── Services/                    # Service, process, configuration, backup tests
├── TestInfrastructure/          # Test doubles/helpers
├── TestProcessHelper/           # Helper executable project for process scenarios
├── ViewModels/                  # ViewModel state/command/threading tests
└── Views/                       # Window subscription/lifecycle tests

QueryPlugins.Tests/
├── Detectors/                   # Mutagen-backed detector tests
│   └── Games/                   # Game-specific detector tests
└── Models/                      # QueryPlugins model tests
```

## Test Structure

**Suite Organization:**
```csharp
public sealed class BackupServiceTests : IDisposable
{
    private readonly ILoggingService _mockLogger;
    private readonly BackupService _sut;
    private readonly string _testRoot;

    public BackupServiceTests()
    {
        _mockLogger = Substitute.For<ILoggingService>();
        _sut = new BackupService(_mockLogger);
        _testRoot = Path.Combine(Path.GetTempPath(), $"autoqac_backup_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
    }

    [Fact]
    public void BackupPlugin_ValidPlugin_CopiesFileSuccessfully()
    {
        // Arrange
        var sourceFile = Path.Combine(_testRoot, "TestPlugin.esp");
        File.WriteAllText(sourceFile, "fake plugin data for testing");

        // Act
        var result = _sut.BackupPlugin(plugin, sessionDir);

        // Assert
        result.Success.Should().BeTrue();
    }
}
```

**Patterns:**
- Use constructor setup for shared test doubles and defaults: `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`, `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`, `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`.
- Use `IDisposable` to clean temporary directories, dispose services, and release process handles: `AutoQAC.Tests/Services/BackupServiceTests.cs`, `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`, `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`.
- Use `// Arrange`, `// Act`, `// Assert` comments inside tests, including integration tests such as `AutoQAC.Tests/Integration/DependencyInjectionTests.cs`.
- Use helper methods to encapsulate repeated async synchronization or fixture construction: `CreateSignal`, `WaitForSignalAsync`, and `WaitForCancellationAsync` in `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`; `BuildLoadOrder` and `BuildThreeModLoadOrder` in `QueryPlugins.Tests/Detectors/ItmDetectorTests.cs`.
- Use `TaskCompletionSource<T>` with `TaskCreationOptions.RunContinuationsAsynchronously` for cancellation/threading tests: `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`, `AutoQAC.Tests/ViewModels/MainWindowThreadingTests.cs`.

## Mocking

**Framework:** NSubstitute 5.3.0 with `NSubstitute.Analyzers.CSharp` 1.0.17.

**Patterns:**
```csharp
var stateService = Substitute.For<IStateService>();
stateService.StateChanged.Returns(stateSubject);
stateService.CurrentState.Returns(_ => currentState);

await _cleaningServiceMock.Received(1).CleanPluginAsync(
    Arg.Is<PluginInfo>(p => p.FileName == "Plugin1.esp"),
    Arg.Any<CancellationToken>(),
    Arg.Any<Action<System.Diagnostics.Process>?>());

await _messageDialog.DidNotReceive().ShowErrorAsync(
    Arg.Any<string>(),
    Arg.Any<string>(),
    Arg.Any<string?>());
```

**What to Mock:**
- Mock service interfaces at ViewModel and orchestrator boundaries: `IConfigurationService`, `IStateService`, `ICleaningOrchestrator`, `IPluginLoadingService`, and `IMessageDialogService` in `AutoQAC.Tests/ViewModels/MainWindowThreadingTests.cs` and `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs`.
- Mock logging with `Substitute.For<ILoggingService>()` rather than asserting on concrete Serilog output: `AutoQAC.Tests/Services/BackupServiceTests.cs`, `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`.
- Mock process execution behind `IProcessExecutionService` for orchestrator-level stop/termination behavior: `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`.
- Use custom in-memory fakes where stateful behavior is clearer than mocks: `InMemoryPidStore` in `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`, `CountingBackupFileCopier` in `AutoQAC.Tests/Services/BackupServiceTests.cs`.

**What NOT to Mock:**
- Do not mock `IUiDispatcher` when the test requires actual dispatch flow; use synchronous/capturing test doubles in `AutoQAC.Tests/TestInfrastructure/SynchronousUiDispatcher.cs` and `AutoQAC.Tests/ViewModels/MainWindowThreadingTests.cs`.
- Do not spawn real processes in unit tests for `ProcessExecutionService` unless the scenario is explicitly isolated and cleaned up. `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs` documents non-spawning unit test constraints; `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs` includes best-effort process cleanup for real-process helper scenarios.
- Do not mock Mutagen record equality or link-cache behavior for detector logic. `QueryPlugins.Tests/Detectors/ItmDetectorTests.cs` builds in-memory Mutagen mods to exercise real semantics.
- Do not mock the DI container in integration tests; build a real `ServiceCollection` in `AutoQAC.Tests/Integration/DependencyInjectionTests.cs`.

## Fixtures and Factories

**Test Data:**
```csharp
private static (SkyrimMod master, SkyrimMod plugin, ILinkCache cache)
    BuildLoadOrder(Action<Npc>? overrideAction, bool addNewToPlugin = false)
{
    var masterMod = new SkyrimMod(MasterKey, SkyrimRelease.SkyrimSE);
    var originalNpc = masterMod.Npcs.AddNew("OriginalNpc");

    var pluginMod = new SkyrimMod(PluginKey, SkyrimRelease.SkyrimSE);
    if (addNewToPlugin)
    {
        pluginMod.Npcs.AddNew("NewNpc");
    }

    var cache = new ISkyrimModGetter[] { masterMod, pluginMod }.ToImmutableLinkCache();
    return (masterMod, pluginMod, cache);
}
```

**Location:**
- Small fixtures and factories are private nested helpers inside the test class that uses them: Mutagen load-order builders in `QueryPlugins.Tests/Detectors/ItmDetectorTests.cs`, `TrackingIssueList` in `QueryPlugins.Tests/Models/PluginAnalysisResultTests.cs`.
- Cross-test infrastructure lives in `AutoQAC.Tests/TestInfrastructure/`, currently including `SynchronousUiDispatcher`.
- File-system fixtures use per-test temp roots under `Path.GetTempPath()` with `Guid.NewGuid()` for isolation: `AutoQAC.Tests/Services/BackupServiceTests.cs`, `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`.

## Coverage

**Requirements:** No numeric coverage threshold is enforced.

**View Coverage:**
```bash
dotnet test AutoQACSharp.slnx
# AutoQAC coverage output: AutoQAC.Tests/TestResults/coverage/coverage.cobertura.xml
# QueryPlugins coverage output: QueryPlugins.Tests/TestResults/coverage/coverage.cobertura.xml
```

- Coverage is auto-collected through `CollectCoverage`, `CoverletOutputFormat`, and `CoverletOutput` in `AutoQAC.Tests/AutoQAC.Tests.csproj` and `QueryPlugins.Tests/QueryPlugins.Tests.csproj`.
- `AutoQAC.Tests/AutoQAC.Tests.csproj` includes `[AutoQAC]*` and excludes `[AutoQAC.Tests]*`.
- `QueryPlugins.Tests/QueryPlugins.Tests.csproj` includes `[QueryPlugins]*` and excludes `[QueryPlugins.Tests]*`.
- Both test projects exclude code marked with `ExcludeFromCodeCoverage`.

## Test Types

**Unit Tests:**
- Service tests dominate and validate business rules, file safety, process behavior, configuration persistence, and parsing: `AutoQAC.Tests/Services/BackupServiceTests.cs`, `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`, `AutoQAC.Tests/Services/XEditOutputParserTests.cs`, `AutoQAC.Tests/Services/PluginValidationServiceTests.cs`.
- ViewModel tests validate observable state, command enablement, interactions, and disposal: `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs`, `AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs`, `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs`.
- QueryPlugins tests validate detector and model behavior with in-memory Mutagen objects: `QueryPlugins.Tests/Detectors/ItmDetectorTests.cs`, `QueryPlugins.Tests/Detectors/Games/SkyrimDetectorTests.cs`, `QueryPlugins.Tests/Models/PluginAnalysisResultTests.cs`.

**Integration Tests:**
- DI composition tests build the real service provider and verify service/ViewModel resolution and lifetimes in `AutoQAC.Tests/Integration/DependencyInjectionTests.cs`.
- Game selection and orchestrator-flow tests live in `AutoQAC.Tests/Integration/GameSelectionIntegrationTests.cs` and service-level orchestration tests such as `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`.
- File-system integration is exercised with real temp directories in backup/configuration/log-retention tests, including `AutoQAC.Tests/Services/BackupServiceTests.cs` and `AutoQAC.Tests/Services/LogRetentionServiceTests.cs`.

**E2E Tests:**
- No separate Avalonia.Headless or UI automation test project is present.
- View lifecycle behavior is tested through code-level window subscription tests in `AutoQAC.Tests/Views/ViewSubscriptionLifecycleTests.cs`.
- Process helper support exists in `AutoQAC.Tests/TestProcessHelper/`, but tests should preserve the documented single-process and cleanup constraints in `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs` and `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`.

## Common Patterns

**Async Testing:**
```csharp
private static async Task WaitForSignalAsync(Task signalTask, string because)
{
    var completedTask = await Task.WhenAny(signalTask, Task.Delay(TimeSpan.FromSeconds(2)));
    completedTask.Should().Be(signalTask, because);
    await signalTask;
}

await service.SaveUserConfigAsync(config);
await service.FlushPendingSavesAsync();
var loaded = await service.LoadUserConfigAsync();
loaded.Settings.CleaningTimeout.Should().Be(777);
```
- Use finite timeouts for signals and cancellation tests to avoid hanging the test runner: `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`, `AutoQAC.Tests/ViewModels/MainWindowThreadingTests.cs`.
- Flush debounced configuration saves before asserting on disk: `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`.
- Await NSubstitute verification for async methods: `await _messageDialog.Received(1).ShowConfirmAsync(...)` in `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs`.

**Error Testing:**
```csharp
var act = () => _sut.FindItmRecords(pluginMod, cache);

act.Should().Throw<ArgumentException>()
    .WithParameterName("linkCache")
    .WithMessage("*does not contain analyzed plugin*");

await FluentActions.Awaiting(() => service.ExecuteAsync(startInfo))
    .Should().ThrowAsync<ObjectDisposedException>(
        "disposed service should not allow execution");
```
- Use `FluentActions.Awaiting` for async exception assertions: `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`.
- Assert exception parameter names and message fragments for API guard clauses: `QueryPlugins.Tests/Detectors/ItmDetectorTests.cs`.
- Assert failure result objects for recoverable service errors instead of expecting exceptions when the production API returns status: `AutoQAC.Tests/Services/BackupServiceTests.cs`, `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs` startup failure cases.

---

*Testing analysis: 2026-04-29*
