# Testing Patterns

**Analysis Date:** 2026-04-28

## Test Framework

**Runner:**
- xUnit 2.9.3 with `Microsoft.NET.Test.Sdk` 18.0.1.
- Config: package and test-project settings live in `AutoQAC.Tests/AutoQAC.Tests.csproj` and `QueryPlugins.Tests/QueryPlugins.Tests.csproj`.
- Test projects target `net10.0-windows10.0.19041.0` for `AutoQAC.Tests` and `net10.0` for `QueryPlugins.Tests`.

**Assertion Library:**
- FluentAssertions 8.8.0 is the default assertion library: `AutoQAC.Tests/ViewModels/PluginListViewModelTests.cs`, `QueryPlugins.Tests/Detectors/ItmDetectorTests.cs`.
- xUnit assertions are not the primary pattern; prefer `.Should()` assertions with reason strings for behavior intent.

**Run Commands:**
```bash
dotnet test AutoQACSharp.slnx              # Run all tests
dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj              # Run AutoQAC tests only
dotnet test QueryPlugins.Tests/QueryPlugins.Tests.csproj    # Run QueryPlugins tests only
dotnet test AutoQACSharp.slnx /p:CollectCoverage=true       # Run tests with coverlet coverage
```

## Test File Organization

**Location:**
- Tests are in separate test projects rather than co-located with source: `AutoQAC.Tests/` and `QueryPlugins.Tests/`.
- `AutoQAC.Tests` mirrors major production areas with `Models`, `Services`, `ViewModels`, `Views`, `Integration`, and `TestInfrastructure` folders.
- `QueryPlugins.Tests` mirrors detector and model areas with `Detectors`, `Detectors/Games`, and `Models`.

**Naming:**
- Test classes are named `<Subject>Tests`: `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`, `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`, `QueryPlugins.Tests/Detectors/Games/SkyrimDetectorTests.cs`.
- Test methods use `Subject_Condition_ExpectedOutcome`: `LoadUserConfig_ShouldCreateDefault_WhenFileNotFound`, `IdenticalOverride_IsFlagged_AsItmRecord`, `Selection_ShouldSurviveApproximationMerge`.
- Regression tests name the bug or invariant in the test name and comment: `Selection_ShouldNotLeakAcrossPluginListReplacement_WhenFileNamesCollide` in `AutoQAC.Tests/ViewModels/PluginListViewModelTests.cs`.

**Structure:**
```
AutoQAC.Tests/
├── Integration/          # DI and cross-service flows
├── Models/               # record/result behavior
├── Services/             # service unit tests and service-level integration
├── Services/UI/          # UI service tests
├── TestInfrastructure/   # reusable test doubles
├── ViewModels/           # MVVM state and command behavior
└── Views/                # view/code-behind lifecycle regression tests

QueryPlugins.Tests/
├── Detectors/            # game-agnostic detector tests
├── Detectors/Games/      # per-game detector tests
└── Models/               # analysis result/model tests
```

## Test Structure

**Suite Organization:**
```csharp
public sealed class ConfigurationServiceTests : IDisposable
{
    private readonly string _testDirectory;

    public ConfigurationServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "AutoQACTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testDirectory);
    }

    [Fact]
    public async Task LoadUserConfig_ShouldCreateDefault_WhenFileNotFound()
    {
        // Arrange
        var service = new ConfigurationService(Substitute.For<ILoggingService>(), _testDirectory);

        // Act
        var config = await service.LoadUserConfigAsync();

        // Assert
        config.Should().NotBeNull();
    }
}
```

**Patterns:**
- Use Arrange/Act/Assert comments in service and integration tests: `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`, `AutoQAC.Tests/Integration/DependencyInjectionTests.cs`.
- Use constructor-created fixtures for reusable setup and `IDisposable` for cleanup: `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`, `AutoQAC.Tests/Services/BackupServiceTests.cs`.
- Use `try/finally` in tests that create disposable production objects inside a single test: `AutoQAC.Tests/ViewModels/PluginListViewModelTests.cs`.
- Group large test classes with `#region` for behavior areas: `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`.
- Use `TaskCompletionSource` with `TaskCreationOptions.RunContinuationsAsynchronously` for async coordination: `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`, `AutoQAC.Tests/ViewModels/MainWindowThreadingTests.cs`.

## Mocking

**Framework:** NSubstitute 5.3.0 with `NSubstitute.Analyzers.CSharp` enabled in test projects.

**Patterns:**
```csharp
var logger = Substitute.For<ILoggingService>();
var configService = Substitute.For<IConfigurationService>();

configService.GetSkipListAsync(
        Arg.Any<GameType>(),
        Arg.Any<GameVariant>(),
        Arg.Any<CancellationToken>())
    .Returns(new List<string>());

await processServiceMock.Received(1)
    .CleanOrphanedProcessesAsync(Arg.Any<CancellationToken>());
```

**What to Mock:**
- Mock service dependencies for ViewModel and orchestration tests: `IConfigurationService`, `IStateService`, `ILoggingService`, `IProcessExecutionService` in `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs` and `AutoQAC.Tests/ViewModels/SkipListViewModelTests.cs`.
- Mock logging and dialog/file services when only behavior or state is under test: `AutoQAC.Tests/ViewModels/ErrorDialogTests.cs`, `AutoQAC.Tests/Services/UI/FileDialogServiceTests.cs`.
- Mock process execution at orchestrator level rather than launching real xEdit or shell processes: `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`.
- Use simple handwritten test doubles when thread or dispatcher semantics matter: `SynchronousUiDispatcher` in `AutoQAC.Tests/TestInfrastructure/SynchronousUiDispatcher.cs`, `ThreadCapturingUiDispatcher` in `AutoQAC.Tests/ViewModels/MainWindowThreadingTests.cs`.

**What NOT to Mock:**
- Do not mock pure state objects when a real implementation gives better regression coverage. Use `StateService` directly for selection and state transition tests in `AutoQAC.Tests/ViewModels/PluginListViewModelTests.cs`.
- Do not spawn real processes in unit tests for process lifecycle behavior; tests document this constraint in `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`.
- Do not mock Mutagen models for detector tests. Build in-memory Mutagen mods in `QueryPlugins.Tests/Detectors/ItmDetectorTests.cs` and `QueryPlugins.Tests/Detectors/Games/SkyrimDetectorTests.cs`.
- Do not depend on Avalonia.Headless; no separate headless test project is present in `AutoQACSharp.slnx`.

## Fixtures and Factories

**Test Data:**
```csharp
private static (SkyrimMod master, SkyrimMod plugin, ILinkCache cache)
    BuildLoadOrder(Action<Npc>? overrideAction, bool addNewToPlugin = false)
{
    var masterMod = new SkyrimMod(MasterKey, SkyrimRelease.SkyrimSE);
    var originalNpc = masterMod.Npcs.AddNew("OriginalNpc");
    var pluginMod = new SkyrimMod(PluginKey, SkyrimRelease.SkyrimSE);
    var cache = new ISkyrimModGetter[] { masterMod, pluginMod }.ToImmutableLinkCache();
    return (masterMod, pluginMod, cache);
}
```

**Location:**
- Keep helpers private and close to the tests that need them: `BuildLoadOrder` in `QueryPlugins.Tests/Detectors/ItmDetectorTests.cs`, `CreateOrchestrator` in `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`.
- Reusable infrastructure belongs under `AutoQAC.Tests/TestInfrastructure/`, currently `AutoQAC.Tests/TestInfrastructure/SynchronousUiDispatcher.cs`.
- Temporary file/directory fixtures use `Path.GetTempPath()` plus `Guid.NewGuid()` and clean up in `Dispose`: `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`, `AutoQAC.Tests/Services/XEditLogFileServiceTests.cs`, `AutoQAC.Tests/Services/BackupServiceTests.cs`.

## Coverage

**Requirements:** No numeric threshold is enforced.

**View Coverage:**
```bash
dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj
dotnet test QueryPlugins.Tests/QueryPlugins.Tests.csproj
```
- Coverlet is configured to auto-collect Cobertura coverage into `TestResults/coverage/` in both `AutoQAC.Tests/AutoQAC.Tests.csproj` and `QueryPlugins.Tests/QueryPlugins.Tests.csproj`.
- `AutoQAC.Tests` includes `[AutoQAC]*` and excludes `[AutoQAC.Tests]*`; `QueryPlugins.Tests` includes `[QueryPlugins]*` and excludes `[QueryPlugins.Tests]*`.
- Coverage output is created under each test project's `TestResults/coverage/` directory.

## Test Types

**Unit Tests:**
- Service unit tests validate file handling, parsing, process wrapper behavior, backup retention, game detection, and command builders: `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`, `AutoQAC.Tests/Services/XEditOutputParserTests.cs`, `AutoQAC.Tests/Services/XEditCommandBuilderTests.cs`.
- ViewModel tests validate command enablement, state synchronization, UI-thread dispatch expectations, and dialog interactions without manipulating Avalonia controls directly: `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs`, `AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs`.
- QueryPlugins detector tests validate in-memory plugin analysis logic: `QueryPlugins.Tests/Detectors/ItmDetectorTests.cs`, `QueryPlugins.Tests/Detectors/Games/Fallout4DetectorTests.cs`.

**Integration Tests:**
- DI integration tests build the real service collection and verify registrations/scopes in `AutoQAC.Tests/Integration/DependencyInjectionTests.cs`.
- Game selection and orchestration flows are covered in `AutoQAC.Tests/Integration/GameSelectionIntegrationTests.cs` and `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`.
- View subscription lifecycle behavior is checked by source-level regression assertions in `AutoQAC.Tests/Views/ViewSubscriptionLifecycleTests.cs`.

**E2E Tests:**
- Not used. There is no UI automation or Avalonia.Headless project in `AutoQACSharp.slnx`.
- xEdit process launches are not exercised end-to-end by tests; preserve process mocking and service-level boundaries.

## Common Patterns

**Async Testing:**
```csharp
private static TaskCompletionSource<bool> CreateSignal()
{
    return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
}

await signal.Task.WaitAsync(TimeSpan.FromSeconds(2));
```
- Use `TaskCompletionSource` for cancellation and cross-thread coordination: `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`, `AutoQAC.Tests/ViewModels/MainWindowThreadingTests.cs`.
- Use async lambdas in substitute returns when a dependency must block until cancellation: `CleanPluginAsync` setup in `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`.
- Flush debounced configuration saves before file assertions: `FlushPendingSavesAsync` in `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`.

**Error Testing:**
```csharp
Func<Task> act = () => service.LoadUserConfigAsync();

await act.Should().ThrowAsync<Exception>(
    "corrupted YAML should cause an exception");
```
- Use `Func<Task>` plus `ThrowAsync<T>` for async failures: `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`.
- Use `Action`/lambda plus `.Throw<T>()` for synchronous enumerable validation: `QueryPlugins.Tests/Detectors/ItmDetectorTests.cs`, `QueryPlugins.Tests/Detectors/Games/SkyrimDetectorTests.cs`.
- Assert parameter names and message fragments when they document public contract: `FindItmRecords_PluginMissingFromCache_ThrowsArgumentException` in `QueryPlugins.Tests/Detectors/ItmDetectorTests.cs`.
- Verify logs for expected operational errors when behavior includes observability: startup failure in `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`.

---

*Testing analysis: 2026-04-28*
