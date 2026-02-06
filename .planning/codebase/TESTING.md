# Testing Patterns

**Analysis Date:** 2026-02-06

## Test Framework

**Runner:**
- xUnit 2.9.3
- Config: `AutoQAC.Tests.csproj` with implicit global usings
- Test project targets: `net10.0-windows10.0.19041.0`

**Assertion Library:**
- FluentAssertions 8.8.0
- All assertions use fluent API: `.Should().Be()`, `.Should().NotBeNull()`, `.Should().HaveCount()`

**Run Commands:**
```bash
dotnet test                    # Run all tests
dotnet test --watch           # Watch mode (re-run on changes)
dotnet test --collect:"XPlat Code Coverage"  # Coverage collection
dotnet build && dotnet test   # Build then test
```

## Test File Organization

**Location:**
- Tests co-located in `AutoQAC.Tests/` project parallel to source
- Folder structure mirrors source structure:
  - Source: `AutoQAC/Models/` → Tests: `AutoQAC.Tests/Models/`
  - Source: `AutoQAC/Services/Cleaning/` → Tests: `AutoQAC.Tests/Services/`
  - Source: `AutoQAC/ViewModels/` → Tests: `AutoQAC.Tests/ViewModels/`

**Naming:**
- Pattern: `{ClassName}Tests.cs`
- Examples: `AppStateTests.cs`, `CleaningServiceTests.cs`, `MainWindowViewModelTests.cs`

**Structure:**
```
AutoQAC.Tests/
├── Integration/              # Integration tests
│   ├── DependencyInjectionTests.cs
│   └── GameSelectionIntegrationTests.cs
├── Models/                   # Model unit tests
│   ├── AppStateTests.cs
│   ├── CleaningSessionResultTests.cs
│   └── PluginCleaningResultTests.cs
├── Services/                 # Service unit tests
│   ├── CleaningServiceTests.cs
│   ├── ConfigurationServiceTests.cs
│   ├── XEditCommandBuilderTests.cs
│   └── [17 service test files]
└── ViewModels/               # ViewModel unit tests
    ├── MainWindowViewModelTests.cs
    ├── ProgressViewModelTests.cs
    └── [4 ViewModel test files]
```

## Test Structure

**Suite Organization:**
```csharp
public sealed class AppStateTests
{
    #region Computed Property Tests

    [Theory]
    [InlineData("plugins.txt", true)]
    [InlineData(null, false)]
    public void IsLoadOrderConfigured_ShouldReturnCorrectValue(string? path, bool expected)
    {
        // Arrange
        var state = new AppState { LoadOrderPath = path };

        // Assert
        state.IsLoadOrderConfigured.Should().Be(expected);
    }

    #endregion

    #region Default Values Tests

    [Fact]
    public void Constructor_ShouldHaveCorrectDefaults()
    {
        // Act
        var state = new AppState();

        // Assert
        state.Progress.Should().Be(0);
    }

    #endregion
}
```

**Patterns:**
- Class declaration marked `sealed`: `public sealed class AppStateTests`
- Test methods use descriptive names: `{MethodBeingTested}_{Scenario}_{ExpectedOutcome}`
- Region markers organize related tests: `#region Computed Property Tests`
- XML documentation on public test methods

**Arrange-Act-Assert Pattern:**
- Every test follows AAA: comment headers mark each section
- Arrange: Set up test fixtures and mocks
- Act: Execute the code under test
- Assert: Verify results with FluentAssertions

## Mocking

**Framework:** Moq 4.20.72

**Patterns:**
```csharp
// Setup mocks in constructor
private readonly Mock<IConfigurationService> _mockConfig;

public CleaningServiceTests()
{
    _mockConfig = new Mock<IConfigurationService>();
}

// Setup return values
_mockConfig.Setup(s => s.LoadMainConfigAsync(It.IsAny<CancellationToken>()))
    .ReturnsAsync(mainConfig);

// Setup async returns
_mockProcess.Setup(p => p.ExecuteAsync(startInfo, It.IsAny<IProgress<string>>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(processResult);

// Verify calls
_mockProcess.Verify(p => p.ExecuteAsync(...), Times.Once);
_mockProcess.Verify(p => p.ExecuteAsync(...), Times.Never);

// Pass mock object to class under test
var service = new CleaningService(_mockConfig.Object, _mockState.Object);
```

**What to Mock:**
- External dependencies injected via constructor: `IConfigurationService`, `ILoggingService`, `IProcessExecutionService`
- Database/file system I/O services
- Long-running async operations
- Services with complex initialization

**What NOT to Mock:**
- Models and records: Test with real instances
- Value objects and data structures
- Code under test (SUT - System Under Test)
- Simple utility methods

## Fixtures and Factories

**Test Data:**
No dedicated fixture or factory classes detected. Test data created inline within tests:

```csharp
// Example: Create test model directly
var plugin = new PluginInfo
{
    FileName = "Mod.esp",
    FullPath = "Mod.esp",
    DetectedGameType = GameType.SkyrimSe,
    IsInSkipList = false
};

// Example: Create app state for testing
var appState = new AppState
{
    CurrentGameType = GameType.SkyrimSe,
    Progress = 5,
    TotalPlugins = 10,
    IsCleaning = true
};

// Example: Create test data collections
var lines = new List<string>
{
    "Undeleting: Some record",
    "Removing: Bad record",
    "Done."
};
```

**Location:**
- Test data created within test methods (Arrange section)
- No shared fixture builders or factories
- Simple inline construction for clarity and test isolation

## Coverage

**Requirements:**
- Coverlet 6.0.4 available but no enforced coverage target detected
- No minimum coverage percentage configured in project file
- Coverage collection available via: `dotnet test --collect:"XPlat Code Coverage"`

**View Coverage:**
```bash
# After running tests with coverage collection, coverage data is in TestResults/
# Coverage report location: TestResults/{guid}/coverage.cobertura.xml
```

## Test Types

**Unit Tests:**
- Scope: Test a single class method in isolation with mocked dependencies
- Approach: Mock all external services, assert on return values and side effects
- Examples: `AppStateTests.cs`, `XEditOutputParserTests.cs`, `PluginLoadingServiceTests.cs`
- Count: ~15 unit test files

**Integration Tests:**
- Scope: Test multiple components together (e.g., DI container, service interactions)
- Approach: Real service instances, minimal mocking, test end-to-end flows
- Examples:
  - `DependencyInjectionTests.cs` - Verifies all services resolve and correct lifetimes
  - `GameSelectionIntegrationTests.cs` - Tests game selection workflow
- Count: ~2 integration test files

**E2E Tests:**
- Status: Not implemented
- Framework: Could use Avalonia.Headless for UI automation
- Current focus: Unit and integration tests only

## Common Patterns

**Async Testing:**
```csharp
[Fact]
public async Task CleanPluginAsync_ShouldCallProcessAndReturnSuccess()
{
    // Arrange
    var service = new CleaningService(...);
    var plugin = new PluginInfo { ... };

    // Act
    var result = await service.CleanPluginAsync(plugin);

    // Assert
    result.Success.Should().BeTrue();
}
```

**Theory Tests (Parameterized):**
```csharp
[Theory]
[InlineData("plugins.txt", true)]
[InlineData("C:\\Games\\plugins.txt", true)]
[InlineData("", false)]
[InlineData(null, false)]
public void IsLoadOrderConfigured_ShouldReturnCorrectValue(string? path, bool expected)
{
    // Arrange & Act & Assert
    var state = new AppState { LoadOrderPath = path };
    state.IsLoadOrderConfigured.Should().Be(expected);
}
```

**Error Testing:**
```csharp
[Fact]
public async Task CleanPluginAsync_WhenSkipped_ShouldReturnSkipped()
{
    // Arrange
    var service = new CleaningService(...);
    var plugin = new PluginInfo { IsInSkipList = true };

    // Act
    var result = await service.CleanPluginAsync(plugin);

    // Assert
    result.Status.Should().Be(CleaningStatus.Skipped);
    result.Success.Should().BeTrue();
}
```

**Observable/Reactive Testing:**
```csharp
// From MainWindowViewModelTests.cs
var stateSubject = new BehaviorSubject<AppState>(new AppState());
_stateServiceMock.Setup(s => s.StateChanged).Returns(stateSubject);

// Setup RxApp for testing
RxApp.MainThreadScheduler = Scheduler.Immediate;
```

**ViewModel Testing with Mocks:**
```csharp
public sealed class MainWindowViewModelTests
{
    private readonly Mock<IConfigurationService> _configServiceMock;
    private readonly Mock<IStateService> _stateServiceMock;
    private readonly Mock<ICleaningOrchestrator> _orchestratorMock;
    // ... other mocks

    public MainWindowViewModelTests()
    {
        // Setup mocks in constructor
        _stateServiceMock.Setup(s => s.StateChanged)
            .Returns(Observable.Never<AppState>());
        _configServiceMock.Setup(s => s.SkipListChanged)
            .Returns(Observable.Never<GameType>());

        RxApp.MainThreadScheduler = Scheduler.Immediate;
    }

    [Fact]
    public async Task StartCleaningCommand_ShouldCallOrchestrator_WhenCanStart()
    {
        // Arrange
        var vm = new MainWindowViewModel(
            _configServiceMock.Object,
            _stateServiceMock.Object,
            _orchestratorMock.Object,
            // ... other services
        );

        // Act
        await vm.StartCleaningCommand.Execute();

        // Assert
        _orchestratorMock.Verify(x => x.StartCleaningAsync(...), Times.Once);
    }
}
```

## Test Naming & Documentation

**Naming Convention:** `{MethodUnderTest}_{ScenarioOrCondition}_{ExpectedBehavior}`

Examples:
- `IsLoadOrderConfigured_ShouldReturnCorrectValue`
- `CleanPluginAsync_ShouldCallProcessAndReturnSuccess`
- `CleanPluginAsync_WhenSkipped_ShouldReturnSkipped`
- `ParseOutput_CountsCorrectly`
- `ParseOutput_ShouldHandleMalformedLines`
- `Constructor_ShouldHaveCorrectDefaults`

**Documentation:**
```csharp
/// <summary>
/// Verifies that IsLoadOrderConfigured returns true when LoadOrderPath has a value.
/// </summary>
[Theory]
[InlineData("plugins.txt", true)]
[InlineData(null, false)]
public void IsLoadOrderConfigured_ShouldReturnCorrectValue(string? path, bool expected)
```

## Setup and Teardown

**Setup:**
- Constructor runs before each test (xUnit default)
- Mocks created in constructor: `_mockConfig = new Mock<IConfigurationService>()`
- Default mock behavior set in constructor

**Teardown:**
- Not explicitly used (xUnit handles disposal)
- Services implementing `IDisposable` not explicitly disposed in tests
- Mocks are garbage collected after test completion

**Fixture Scope:**
- Constructor runs before EACH test (per-test isolation)
- No class-level shared state (prevents test pollution)
- CompositeDisposable pattern in ViewModels cleaned up via `IDisposable`

## Best Practices Observed

1. **Test Isolation:** Each test creates fresh mocks via constructor
2. **Single Responsibility:** Each test verifies one behavior
3. **Readable Assertions:** FluentAssertions provide descriptive failure messages
4. **Async Awareness:** Async methods properly awaited with `async Task`
5. **Null Handling:** Tests explicitly handle nullable reference types
6. **Edge Cases:** Tests include boundary conditions (null, empty, valid values)
7. **Mocking Strategy:** Mock external dependencies, test internal logic
8. **No Test Interdependence:** Tests can run in any order
9. **Sealed Classes:** Test classes marked `sealed` (minor performance optimization)

---

*Testing analysis: 2026-02-06*
