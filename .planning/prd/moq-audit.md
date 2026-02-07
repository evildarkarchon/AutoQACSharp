# Moq Usage Audit Report

**Project:** AutoQACSharp (`AutoQAC.Tests`)
**Moq Version:** 4.20.72
**Date:** 2026-02-07
**Purpose:** Comprehensive catalog of all Moq usage to inform Moq-to-NSubstitute migration

---

## 1. Summary Statistics

| Metric | Count |
|--------|-------|
| Total test files in project | ~30 |
| Test files using Moq | **20** |
| Test files NOT using Moq | ~10 |
| Unique interfaces mocked | **16** |
| Approximate total `.Setup()` calls | **~150** |
| Approximate total `.Verify()` calls | **~75** |
| MockBehavior.Strict usage | **0** (all default/Loose) |

---

## 2. Moq Patterns Used (Exhaustive)

### Found in Codebase

| Pattern | Used? | Prevalence | Example Locations |
|---------|-------|------------|-------------------|
| `new Mock<T>()` | Yes | Very High (20 files) | All Moq-using test files |
| `Mock.Of<T>()` | Yes | Medium (9 files) | ConfigurationServiceTests, GameDetectionServiceTests, PluginValidationServiceTests, ConfigurationServiceSkipListTests, LegacyMigrationServiceTests, BackupServiceTests, XEditLogFileServiceTests, HangDetectionServiceTests, ProcessExecutionServiceTests |
| `.Setup(expr)` | Yes | Very High | All files with behavioral mocks |
| `.Returns(value)` | Yes | Very High | XEditCommandBuilderTests, MainWindowViewModelTests, etc. |
| `.ReturnsAsync(value)` | Yes | Very High | CleaningServiceTests, CleaningOrchestratorTests, etc. |
| `.Returns(lambda)` | Yes | High | SkipListViewModelTests (`() => _stateSubject.Value`), ProcessExecutionServiceTests (async lambda with TCS) |
| `.ReturnsAsync(factory_lambda)` | Yes | Medium | CleaningOrchestratorTests (factory lambda for sequential simulation) |
| `.ThrowsAsync(exception)` | Yes | Medium | CleaningServiceTests, ErrorDialogTests, LogRetentionServiceTests |
| `.Verify(expr, Times)` | Yes | Very High | All files with verification |
| `.Callback<T1,T2,...>()` | Yes | Low (1 file) | ErrorDialogTests (delegate capture) |
| `It.IsAny<T>()` | Yes | Very High | Nearly all files with Setup/Verify |
| `It.Is<T>(predicate)` | Yes | Medium | CleaningServiceTests, CleaningOrchestratorTests, CleaningResultsViewModelTests, ErrorDialogTests |
| `Times.Once` | Yes | Very High | Most files with Verify |
| `Times.Never` | Yes | High | CleaningServiceTests, CleaningOrchestratorTests, ErrorDialogTests, etc. |
| `Times.AtLeastOnce` | Yes | Medium | MainWindowViewModelTests, LogRetentionServiceTests, MainWindowViewModelInitializationTests |
| `Times.AtLeast(N)` | Yes | Low | CleaningOrchestratorTests |
| `Times.Exactly(N)` | Yes | Low | CleaningOrchestratorTests |
| `.Object` | Yes | Very High | All files (to extract mock instance) |

### NOT Found in Codebase

| Pattern | Used? | Notes |
|---------|-------|-------|
| `MockBehavior.Strict` | No | All mocks use default (Loose) |
| `MockBehavior.Loose` (explicit) | No | Never explicitly stated, just default |
| `.SetupSequence()` | No | Sequential behavior done via factory lambdas in `.ReturnsAsync()` instead |
| `.SetupGet()` | No | Property access mocked via `.Setup(x => x.Property)` |
| `.SetupSet()` | No | Not used anywhere |
| `.Protected()` | No | No protected member mocking |
| `.As<T>()` | No | No interface casting on mocks |
| `.Raises()` | No | No event raising via Moq |
| `.SetupAdd()` / `.SetupRemove()` | No | No event subscription mocking |
| `DefaultValue` | No | Never configured |
| `CallBase` | No | Never used |
| `MockRepository` | No | Not used |
| `.Verifiable()` | No | All verification is explicit `.Verify()` |
| `.VerifyAll()` | No | Not used |
| `.VerifyNoOtherCalls()` | No | Not used |
| `It.IsIn<T>()` | No | Not used |
| `It.IsInRange<T>()` | No | Not used |
| `It.IsRegex()` | No | Not used |
| `It.IsNotNull<T>()` | No | `It.IsAny<T>()` used instead |
| `Match.Create<T>()` | No | No custom matchers |

---

## 3. Every Mocked Interface (with File Counts)

| Interface | Files Mocking It | Test Files |
|-----------|-----------------|------------|
| `ILoggingService` | **14** | CleaningServiceTests, CleaningOrchestratorTests, MainWindowViewModelTests, ProgressViewModelTests (indirectly via sub-VM), ConfigurationServiceTests, ProcessExecutionServiceTests, GameDetectionServiceTests, PluginLoadingServiceTests, PluginValidationServiceTests, LegacyMigrationServiceTests, HangDetectionServiceTests, BackupServiceTests, XEditLogFileServiceTests, LogRetentionServiceTests |
| `IConfigurationService` | **8** | CleaningServiceTests, CleaningOrchestratorTests, MainWindowViewModelTests, MainWindowViewModelInitializationTests, SkipListViewModelTests, ErrorDialogTests, ConfigurationServiceSkipListTests (Mock.Of only), LogRetentionServiceTests |
| `IStateService` | **8** | CleaningServiceTests, CleaningOrchestratorTests, MainWindowViewModelTests, MainWindowViewModelInitializationTests, ProgressViewModelTests, SkipListViewModelTests, XEditCommandBuilderTests, ErrorDialogTests |
| `IPluginValidationService` | **5** | CleaningOrchestratorTests, MainWindowViewModelTests, MainWindowViewModelInitializationTests, PluginLoadingServiceTests, ErrorDialogTests |
| `ICleaningOrchestrator` | **4** | MainWindowViewModelTests, MainWindowViewModelInitializationTests, ProgressViewModelTests, ErrorDialogTests |
| `IFileDialogService` | **4** | MainWindowViewModelTests, MainWindowViewModelInitializationTests, CleaningResultsViewModelTests, ErrorDialogTests |
| `IPluginLoadingService` | **4** | MainWindowViewModelTests, MainWindowViewModelInitializationTests, ErrorDialogTests, (referenced in ProcessExecutionServiceTests helper) |
| `IGameDetectionService` | **3** | CleaningServiceTests, CleaningOrchestratorTests, ProcessExecutionServiceTests |
| `IMessageDialogService` | **3** | MainWindowViewModelTests, MainWindowViewModelInitializationTests, ErrorDialogTests |
| `IProcessExecutionService` | **3** | CleaningServiceTests, CleaningOrchestratorTests, ProcessExecutionServiceTests |
| `ICleaningService` | **3** | CleaningOrchestratorTests, ProcessExecutionServiceTests (helper), ErrorDialogTests (indirectly) |
| `IXEditOutputParser` | **2** | CleaningServiceTests, CleaningOrchestratorTests |
| `IXEditLogFileService` | **2** | CleaningOrchestratorTests, ProcessExecutionServiceTests |
| `IBackupService` | **2** | CleaningOrchestratorTests, ProcessExecutionServiceTests |
| `IHangDetectionService` | **2** | CleaningOrchestratorTests, ProcessExecutionServiceTests |
| `IXEditCommandBuilder` | **1** | CleaningServiceTests |

**Total unique interfaces:** 16

---

## 4. Per-File Detailed Breakdown

### 4.1 Service Tests

#### CleaningServiceTests.cs
- **Mocks (7):** IConfigurationService, IGameDetectionService, IStateService, ILoggingService, IProcessExecutionService, IXEditCommandBuilder, IXEditOutputParser
- **Patterns:** `new Mock<T>()`, `.Setup().Returns()`, `.Setup().ReturnsAsync()`, `.Setup().ThrowsAsync()`, `.Verify(Times.Once)`, `.Verify(Times.Never)`, `It.IsAny<T>()`, `It.Is<T>(pred)`
- **Setups:** ~12 | **Verifies:** ~5
- **Tests:** 9

#### CleaningOrchestratorTests.cs
- **Mocks (11):** ICleaningService, IPluginValidationService, IGameDetectionService, IStateService, IConfigurationService, ILoggingService, IProcessExecutionService, IXEditLogFileService, IXEditOutputParser, IBackupService, IHangDetectionService
- **Patterns:** `new Mock<T>()`, `.Setup().Returns()`, `.Setup().ReturnsAsync()`, `.ReturnsAsync(factory_lambda)`, `.Returns(async_lambda)`, `.Verify(Times.Once/Never/Exactly/AtLeast)`, `It.IsAny<T>()`, `It.Is<T>(pred)`
- **Advanced:** Factory lambda in `.ReturnsAsync(() => { ... })` for sequential behavior simulation; `Thread.Sleep` in mock callbacks for parallelism detection; `TaskCompletionSource` coordination
- **Setups:** ~40 | **Verifies:** ~25
- **Tests:** 14
- **Complexity:** HIGH -- most complex mock setup in the codebase

#### ConfigurationServiceTests.cs
- **Mocks (1):** ILoggingService (via `Mock.Of<T>()` only)
- **Patterns:** `Mock.Of<T>()` for constructor injection only
- **Setups:** 0 | **Verifies:** 0
- **Tests:** 18

#### ProcessExecutionServiceTests.cs
- **Mocks (2 direct + 11 in helper):** ILoggingService, IStateService (direct); full orchestrator mock set in `CreateOrchestrator()` helper
- **Patterns:** `new Mock<T>()`, `Mock.Of<T>()`, `.Verify(Times.Once/Never)`, `.Returns(async_lambda)` with TaskCompletionSource
- **Setups:** ~20 | **Verifies:** ~5
- **Tests:** 5

#### GameDetectionServiceTests.cs
- **Mocks (1):** ILoggingService (via `Mock.Of<T>()` only)
- **Patterns:** `Mock.Of<T>()` for constructor injection only
- **Setups:** 0 | **Verifies:** 0
- **Tests:** 20

#### PluginLoadingServiceTests.cs
- **Mocks (2):** IPluginValidationService, ILoggingService
- **Patterns:** `new Mock<T>()`, `.Setup().ReturnsAsync()`, `.Verify(Times.Once)`
- **Setups:** ~2 | **Verifies:** ~1
- **Tests:** 14

#### PluginValidationServiceTests.cs
- **Mocks (1):** ILoggingService (via `Mock.Of<T>()` only)
- **Patterns:** `Mock.Of<T>()` for constructor injection only
- **Setups:** 0 | **Verifies:** 0
- **Tests:** 18

#### XEditCommandBuilderTests.cs
- **Mocks (1):** IStateService
- **Patterns:** `new Mock<T>()`, `.Setup(x => x.CurrentState).Returns(new AppState {...})`
- **Setups:** ~6 | **Verifies:** 0
- **Tests:** 6

#### LegacyMigrationServiceTests.cs
- **Mocks (1):** ILoggingService (via `new Mock<T>()`)
- **Patterns:** `new Mock<T>()` for constructor injection only
- **Setups:** 0 | **Verifies:** 0
- **Tests:** 5

#### ConfigurationServiceSkipListTests.cs
- **Mocks (1):** ILoggingService (via `Mock.Of<T>()` only)
- **Patterns:** `Mock.Of<T>()` for constructor injection only
- **Setups:** 0 | **Verifies:** 0
- **Tests:** 22

#### HangDetectionServiceTests.cs
- **Mocks (1):** ILoggingService (via `new Mock<T>()`)
- **Patterns:** `new Mock<T>()` for constructor injection only
- **Setups:** 0 | **Verifies:** 0
- **Tests:** 4

#### BackupServiceTests.cs
- **Mocks (1):** ILoggingService (via `new Mock<T>()`)
- **Patterns:** `new Mock<T>()` for constructor injection only
- **Setups:** 0 | **Verifies:** 0
- **Tests:** 12

#### XEditLogFileServiceTests.cs
- **Mocks (1):** ILoggingService (via `new Mock<T>()`)
- **Patterns:** `new Mock<T>()` for constructor injection only
- **Setups:** 0 | **Verifies:** 0
- **Tests:** 5

#### LogRetentionServiceTests.cs
- **Mocks (2):** IConfigurationService, ILoggingService
- **Patterns:** `new Mock<T>()`, `.Setup().ReturnsAsync()`, `.Setup().ThrowsAsync()`, `.Verify(Times.AtLeastOnce)`, `It.IsAny<T>()`
- **Setups:** ~5 | **Verifies:** ~1
- **Tests:** 5

### 4.2 ViewModel Tests

#### MainWindowViewModelTests.cs
- **Mocks (8):** IConfigurationService, IStateService, ICleaningOrchestrator, ILoggingService, IFileDialogService, IMessageDialogService, IPluginValidationService, IPluginLoadingService
- **Patterns:** `.Setup().Returns(observable)` with `Observable.Never<T>()`, BehaviorSubject integration, `.Setup().ReturnsAsync()`, `.Verify(Times.Once/Never/AtLeastOnce)`
- **Setups:** ~30 | **Verifies:** ~20
- **Tests:** 16
- **Complexity:** HIGH -- extensive observable/reactive mock wiring

#### ProgressViewModelTests.cs
- **Mocks (2):** IStateService, ICleaningOrchestrator
- **Patterns:** `.Setup().Returns(subject)` for observables, `new Subject<T>()` integration, `new BehaviorSubject<T>()` integration
- **Setups:** ~8 | **Verifies:** ~1
- **Tests:** 17

#### MainWindowViewModelInitializationTests.cs
- **Mocks (8):** IConfigurationService, IStateService, ICleaningOrchestrator, ILoggingService, IFileDialogService, IMessageDialogService, IPluginValidationService, IPluginLoadingService
- **Patterns:** `.Setup().Returns()`, `.Setup().ReturnsAsync()`, `.Verify(Times.Once/AtLeastOnce)`, Observable.Never integration
- **Setups:** ~8 | **Verifies:** ~3
- **Tests:** 1

#### SkipListViewModelTests.cs
- **Mocks (3):** IConfigurationService, IStateService, ILoggingService
- **Patterns:** `.Setup().Returns()`, `.Setup().ReturnsAsync()`, `.Verify(Times.Once)`, BehaviorSubject integration, `() => _stateSubject.Value` lambda in Returns
- **Setups:** ~15 | **Verifies:** ~1
- **Tests:** 22

#### CleaningResultsViewModelTests.cs
- **Mocks (2):** ILoggingService, IFileDialogService
- **Patterns:** `.Setup().ReturnsAsync()`, `.Verify(Times.Once/Never)`, `It.IsAny<T>()`, `It.Is<T>(pred)`
- **Setups:** ~3 | **Verifies:** ~5
- **Tests:** 16

#### ErrorDialogTests.cs
- **Mocks (8):** IConfigurationService, IStateService, ICleaningOrchestrator, ILoggingService, IFileDialogService, IMessageDialogService, IPluginValidationService, IPluginLoadingService
- **Patterns:** `.Setup().Returns()`, `.Setup().ReturnsAsync()`, `.Setup().ThrowsAsync()`, `.Verify(Times.Once/Never)`, `.Callback<T1,T2,T3>()` for delegate capture, BehaviorSubject integration, Observable.Never
- **Advanced:** `.Callback<TimeoutRetryCallback?, BackupFailureCallback?, CancellationToken>()` -- most advanced Moq pattern in the codebase, used to capture callback delegates passed to `RunCleaningAsync`
- **Setups:** ~20 | **Verifies:** ~10
- **Tests:** 10
- **Complexity:** HIGH -- delegate capture via `.Callback<>()` is the most advanced Moq usage

---

## 5. Complex/Advanced Moq Usage

### 5.1 Callback Delegate Capture (ErrorDialogTests.cs)

The most advanced Moq pattern in the codebase. Captures callback delegates passed as parameters to verify they are invoked correctly:

```csharp
_mockOrchestrator.Setup(o => o.RunCleaningAsync(
    It.IsAny<TimeoutRetryCallback?>(),
    It.IsAny<BackupFailureCallback?>(),
    It.IsAny<CancellationToken>()))
    .Callback<TimeoutRetryCallback?, BackupFailureCallback?, CancellationToken>(
        (timeoutCb, backupCb, ct) =>
        {
            capturedTimeoutCallback = timeoutCb;
            capturedBackupCallback = backupCb;
        })
    .Returns(Task.CompletedTask);
```

**Migration complexity: MEDIUM** -- NSubstitute handles this differently with `.When(...).Do(...)` or argument capture via `Arg.Do<T>()`.

### 5.2 Factory Lambda in ReturnsAsync (CleaningOrchestratorTests.cs)

Uses factory lambdas for sequential return simulation:

```csharp
mockCleaningService.Setup(s => s.CleanPluginAsync(...))
    .ReturnsAsync(() => new PluginCleaningResult { ... });
```

**Migration complexity: LOW** -- NSubstitute supports this natively with `.Returns(callInfo => ...)`.

### 5.3 Async Lambda Returns (ProcessExecutionServiceTests.cs)

Uses async lambdas with TaskCompletionSource for timing control:

```csharp
.Returns(async (string pluginName, ..., CancellationToken ct) =>
{
    await tcs.Task;
    return new PluginCleaningResult { ... };
});
```

**Migration complexity: LOW** -- NSubstitute supports `.Returns(async callInfo => ...)`.

### 5.4 Observable/BehaviorSubject Integration (ViewModel Tests)

Multiple ViewModel test files wire up BehaviorSubject/Subject instances through mock Setups to test reactive pipelines:

```csharp
_stateSubject = new BehaviorSubject<AppState>(initialState);
_mockStateService.Setup(s => s.StateChanged).Returns(_stateSubject);
_mockStateService.Setup(s => s.CurrentState).Returns(() => _stateSubject.Value);
```

**Migration complexity: LOW** -- NSubstitute property returns work identically.

### 5.5 Thread.Sleep in Mock Callback (CleaningOrchestratorTests.cs)

Used to detect unwanted parallel execution:

```csharp
.ReturnsAsync(() =>
{
    Thread.Sleep(50); // Simulate work to detect parallelism
    return new PluginCleaningResult { ... };
});
```

**Migration complexity: LOW** -- Standard lambda return in NSubstitute.

---

## 6. Moq-Specific Workarounds and Pain Points

### 6.1 Optional Parameter Matching (DOCUMENTED IN MEMORY.md)

**Pain point:** Moq does NOT treat C# optional parameters as truly optional in mock expressions -- it matches by exact parameter count. When adding optional parameters to interface methods, ALL mock Setup AND Verify calls must be updated to include the new parameter matcher.

This was a real bug encountered during development and is documented in the project memory. NSubstitute handles optional parameters more naturally.

**Impact:** Every file with Setup/Verify calls for methods with optional parameters is affected. This is a systemic issue across the codebase.

### 6.2 Mock.Of<T>() for Fire-and-Forget Dependencies

9 files use `Mock.Of<T>()` to create mocks where only the interface contract is needed (typically `ILoggingService`) with no behavioral setup. This pattern is slightly verbose compared to NSubstitute's `Substitute.For<T>()`.

### 6.3 Verbose Async Setup

Moq requires distinct `.ReturnsAsync()` vs `.Returns(Task.FromResult(...))` patterns. Multiple files show both styles inconsistently.

### 6.4 No Strict Mode Usage

All 20 files use the default `MockBehavior.Loose`. No test uses `MockBehavior.Strict`, meaning unexpected calls are silently ignored rather than throwing. This is a deliberate project choice but worth noting for migration -- NSubstitute is also permissive by default.

---

## 7. Test Files NOT Using Moq

These files test without any mocking framework:

| File | Reason |
|------|--------|
| StateServiceTests.cs | Tests real StateService directly (no dependencies) |
| PartialFormsWarningViewModelTests.cs | ViewModel has no dependencies |
| MO2ValidationServiceTests.cs | Tests real service with file system |
| XEditOutputParserTests.cs | Pure function testing (string parsing) |
| FileDialogServiceTests.cs | Uses reflection to test private method |
| DependencyInjectionTests.cs | Integration test with real DI container |
| GameSelectionIntegrationTests.cs | Integration test with real services |
| PluginCleaningResultTests.cs | Model/record tests |
| CleaningSessionResultTests.cs | Model/record tests |
| AppStateTests.cs | Model tests |

---

## 8. Migration Risk Assessment

### Low Risk (14 files)
Files using only `new Mock<T>()` or `Mock.Of<T>()` for constructor injection with zero or minimal Setup/Verify calls:

- ConfigurationServiceTests.cs
- GameDetectionServiceTests.cs
- PluginValidationServiceTests.cs
- ConfigurationServiceSkipListTests.cs
- LegacyMigrationServiceTests.cs
- HangDetectionServiceTests.cs
- BackupServiceTests.cs
- XEditLogFileServiceTests.cs
- XEditCommandBuilderTests.cs
- PluginLoadingServiceTests.cs
- LogRetentionServiceTests.cs
- ProgressViewModelTests.cs
- MainWindowViewModelInitializationTests.cs
- SkipListViewModelTests.cs

### Medium Risk (4 files)
Files with moderate Setup/Verify usage and standard patterns:

- CleaningServiceTests.cs
- CleaningResultsViewModelTests.cs
- MainWindowViewModelTests.cs
- ProcessExecutionServiceTests.cs

### High Risk (2 files)
Files with complex patterns (callback capture, factory lambdas, extensive verification):

- **CleaningOrchestratorTests.cs** -- 11 mocks, ~40 setups, ~25 verifies, factory lambdas, async lambdas, Thread.Sleep detection
- **ErrorDialogTests.cs** -- 8 mocks, delegate callback capture via `.Callback<T1,T2,T3>()`, extensive observable wiring

---

## 9. Migration Pattern Cheat Sheet

| Moq Pattern | NSubstitute Equivalent |
|-------------|----------------------|
| `new Mock<T>()` | `Substitute.For<T>()` |
| `Mock.Of<T>()` | `Substitute.For<T>()` |
| `mock.Object` | (direct reference, no `.Object` needed) |
| `.Setup(x => x.Method()).Returns(val)` | `sub.Method().Returns(val)` |
| `.Setup(x => x.Method()).ReturnsAsync(val)` | `sub.Method().Returns(Task.FromResult(val))` |
| `.Setup(x => x.Method()).ThrowsAsync(ex)` | `sub.Method().ThrowsAsync(ex)` (or `.Returns<T>(x => throw ex)`) |
| `.Setup(x => x.Prop).Returns(val)` | `sub.Prop.Returns(val)` |
| `.Verify(x => x.Method(), Times.Once)` | `sub.Received(1).Method()` |
| `.Verify(x => x.Method(), Times.Never)` | `sub.DidNotReceive().Method()` |
| `.Verify(x => x.Method(), Times.AtLeastOnce)` | `sub.Received().Method()` |
| `.Verify(x => x.Method(), Times.Exactly(n))` | `sub.Received(n).Method()` |
| `.Verify(x => x.Method(), Times.AtLeast(n))` | Custom: check `sub.ReceivedCalls()` count |
| `It.IsAny<T>()` | `Arg.Any<T>()` |
| `It.Is<T>(pred)` | `Arg.Is<T>(pred)` |
| `.Callback<T>((arg) => ...)` | `sub.When(x => x.Method(Arg.Any<T>())).Do(info => ...)` or `Arg.Do<T>(arg => ...)` |
| `.Returns(() => val)` | `.Returns(info => val)` |
| `.ReturnsAsync(() => val)` | `.Returns(info => Task.FromResult(val))` |

---

## 10. Appendix: Raw Counts by Category

### Setup Pattern Distribution
- `.Setup().Returns(value)`: ~40 occurrences
- `.Setup().ReturnsAsync(value)`: ~50 occurrences
- `.Setup().Returns(lambda)`: ~15 occurrences
- `.Setup().ReturnsAsync(lambda)`: ~10 occurrences
- `.Setup().ThrowsAsync()`: ~5 occurrences
- `.Setup().Returns(observable)`: ~20 occurrences (ViewModel tests)
- `.Callback<>()`: ~3 occurrences (ErrorDialogTests only)

### Verify Pattern Distribution
- `Times.Once`: ~35 occurrences
- `Times.Never`: ~20 occurrences
- `Times.AtLeastOnce`: ~10 occurrences
- `Times.AtLeast(N)`: ~3 occurrences
- `Times.Exactly(N)`: ~5 occurrences

### Matcher Distribution
- `It.IsAny<T>()`: ~100+ occurrences (ubiquitous)
- `It.Is<T>(predicate)`: ~15 occurrences
