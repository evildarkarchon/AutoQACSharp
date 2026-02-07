# PRD: Moq to NSubstitute Migration

**Project:** AutoQACSharp
**Date:** 2026-02-07
**Status:** Ready for Execution
**Scope:** `AutoQAC.Tests` only (no production code changes)

---

## 1. Executive Summary

### Why Migrate

AutoQACSharp's test suite currently uses Moq 4.20.72 for mocking across 20 test files with 510+ tests. While Moq is functional, NSubstitute offers a materially better developer experience:

- **Cleaner syntax**: NSubstitute eliminates the `.Object` property ceremony, `Setup()` lambda wrappers, and `Verify()` lambda wrappers. Tests become shorter and more readable.
- **Better error messages**: NSubstitute's `ReceivedCallsException` output highlights non-matching arguments with `*` markers, making test failures faster to diagnose.
- **Active, trustworthy maintenance**: NSubstitute is actively maintained (v5.3.0, Oct 2024) under BSD-3-Clause with no SponsorLink or telemetry concerns.
- **Compile-time safety**: NSubstitute.Analyzers.CSharp provides Roslyn diagnostics that catch common mocking mistakes before runtime.

### Expected Benefits

- Reduced test boilerplate (estimated ~15-20% fewer mock-related lines of code)
- Faster failure diagnosis through improved error messages
- Compile-time detection of mocking misuse via Roslyn analyzers
- Elimination of the documented optional-parameter pain point (both frameworks have the same constraint, but NSubstitute's lighter syntax reduces the surface area of the issue)

### Risk Profile

**Low overall risk.** The codebase uses no advanced Moq features that lack NSubstitute equivalents (no Strict mocks, no Protected(), no SetupSequence, no VerifyNoOtherCalls, no event raising). The one complex pattern -- `.Callback<T1,T2,T3>()` delegate capture in ErrorDialogTests -- has a clean NSubstitute equivalent via `Arg.Do<T>()`.

---

## 2. Current State -- Moq Usage Summary

### Metrics

| Metric | Value |
|--------|-------|
| Moq version | 4.20.72 |
| Test files using Moq | 20 of ~30 |
| Unique interfaces mocked | 16 |
| Total `.Setup()` calls | ~150 |
| Total `.Verify()` calls | ~75 |
| Total `It.IsAny<T>()` usages | ~100+ |
| Total `It.Is<T>(pred)` usages | ~15 |
| MockBehavior.Strict usage | 0 (all Loose) |
| SetupSequence usage | 0 |
| Protected() usage | 0 |
| VerifyNoOtherCalls() usage | 0 |
| Event raising (.Raises) usage | 0 |

### Moq Patterns in Use

| Pattern | Prevalence | Files |
|---------|------------|-------|
| `new Mock<T>()` | Very High | 20 files |
| `Mock.Of<T>()` | Medium | 9 files |
| `.Setup().Returns(value)` | Very High (~40) | Most files |
| `.Setup().ReturnsAsync(value)` | Very High (~50) | Service + ViewModel tests |
| `.Setup().Returns(lambda)` | High (~15) | SkipListVM, ProcessExec |
| `.Setup().ReturnsAsync(lambda)` | Medium (~10) | CleaningOrchestrator |
| `.Setup().ThrowsAsync(ex)` | Medium (~5) | CleaningService, ErrorDialog, LogRetention |
| `.Setup().Returns(observable)` | Medium (~20) | ViewModel tests |
| `.Callback<T>()` | Low (~3) | ErrorDialogTests only |
| `.Verify(Times.Once)` | Very High (~35) | Most files |
| `.Verify(Times.Never)` | High (~20) | Most files |
| `.Verify(Times.AtLeastOnce)` | Medium (~10) | MainWindowVM, LogRetention |
| `.Verify(Times.AtLeast(N))` | Low (~3) | CleaningOrchestrator |
| `.Verify(Times.Exactly(N))` | Low (~5) | CleaningOrchestrator |
| `It.IsAny<T>()` | Very High (~100+) | Nearly all files |
| `It.Is<T>(predicate)` | Medium (~15) | CleaningService, Orchestrator, Results, ErrorDialog |
| `.Object` | Very High | All 20 files |

### Patterns NOT in Use (No Migration Concern)

MockBehavior.Strict, SetupSequence, SetupGet/SetupSet, Protected(), As<T>(), Raises(), SetupAdd/SetupRemove, DefaultValue, CallBase, MockRepository, Verifiable(), VerifyAll(), VerifyNoOtherCalls(), It.IsIn, It.IsInRange, It.IsRegex, It.IsNotNull, Match.Create.

### Most-Mocked Interfaces

| Interface | File Count | Role |
|-----------|-----------|------|
| ILoggingService | 14 | Ubiquitous logger dependency |
| IConfigurationService | 8 | App configuration |
| IStateService | 8 | Reactive state management |
| IPluginValidationService | 5 | Plugin validation |
| ICleaningOrchestrator | 4 | Cleaning workflow |
| IFileDialogService | 4 | File dialogs |
| IPluginLoadingService | 4 | Plugin loading |
| IGameDetectionService | 3 | Game detection |
| IMessageDialogService | 3 | Message dialogs |
| IProcessExecutionService | 3 | Process execution |

---

## 3. Target State

### Package Configuration

Replace Moq with NSubstitute in `AutoQAC.Tests.csproj`:

```xml
<!-- REMOVE -->
<PackageReference Include="Moq" Version="4.20.72" />

<!-- ADD -->
<PackageReference Include="NSubstitute" Version="5.3.0" />
<PackageReference Include="NSubstitute.Analyzers.CSharp" Version="1.0.17">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

### Using Directives

In every affected test file:

```csharp
// REMOVE
using Moq;

// ADD
using NSubstitute;
using NSubstitute.ExceptionExtensions;  // Only in files using Throws/ThrowsAsync
```

### Coding Standard After Migration

- Substitute variables should be typed as the interface (`IService sub`), not a wrapper type
- Use `Arg.Any<T>()` and `Arg.Is<T>()` for argument matching
- Use `.Returns()` for all return value configuration (including async)
- Use `.Received(N)` / `.DidNotReceive()` for verification
- Use `Arg.Do<T>()` for argument capture (preferred over `When..Do` for simple cases)
- Use `When(x => x.Method()).Do(info => ...)` for void method callbacks

---

## 4. Migration Scope

### Files to Modify (20 test files)

#### High Complexity (2 files) -- Require careful manual review

| File | Mocks | Setups | Verifies | Key Complexity |
|------|-------|--------|----------|----------------|
| CleaningOrchestratorTests.cs | 11 | ~40 | ~25 | Factory lambdas in ReturnsAsync, async lambdas with TCS, Thread.Sleep detection, Times.AtLeast/Exactly |
| ErrorDialogTests.cs | 8 | ~20 | ~10 | `.Callback<T1,T2,T3>()` delegate capture, observable wiring |

#### Medium Complexity (4 files)

| File | Mocks | Setups | Verifies | Key Complexity |
|------|-------|--------|----------|----------------|
| MainWindowViewModelTests.cs | 8 | ~30 | ~20 | Observable/BehaviorSubject integration, Times.AtLeastOnce |
| CleaningServiceTests.cs | 7 | ~12 | ~5 | ThrowsAsync, It.Is predicates |
| ProcessExecutionServiceTests.cs | 2+11 helper | ~20 | ~5 | Async lambda with TCS, Mock.Of in helper |
| CleaningResultsViewModelTests.cs | 2 | ~3 | ~5 | It.Is predicates |

#### Low Complexity (14 files) -- Mechanical substitution

| File | Mocks | Setups | Verifies | Pattern |
|------|-------|--------|----------|---------|
| ConfigurationServiceTests.cs | 1 | 0 | 0 | Mock.Of only |
| GameDetectionServiceTests.cs | 1 | 0 | 0 | Mock.Of only |
| PluginValidationServiceTests.cs | 1 | 0 | 0 | Mock.Of only |
| ConfigurationServiceSkipListTests.cs | 1 | 0 | 0 | Mock.Of only |
| LegacyMigrationServiceTests.cs | 1 | 0 | 0 | Constructor injection only |
| HangDetectionServiceTests.cs | 1 | 0 | 0 | Constructor injection only |
| BackupServiceTests.cs | 1 | 0 | 0 | Constructor injection only |
| XEditLogFileServiceTests.cs | 1 | 0 | 0 | Constructor injection only |
| XEditCommandBuilderTests.cs | 1 | ~6 | 0 | Simple Setup/Returns |
| PluginLoadingServiceTests.cs | 2 | ~2 | ~1 | Simple async Setup/Verify |
| LogRetentionServiceTests.cs | 2 | ~5 | ~1 | ThrowsAsync, Times.AtLeastOnce |
| ProgressViewModelTests.cs | 2 | ~8 | ~1 | Observable/Subject integration |
| MainWindowViewModelInitializationTests.cs | 8 | ~8 | ~3 | Standard Setup/Verify |
| SkipListViewModelTests.cs | 3 | ~15 | ~1 | BehaviorSubject integration |

### Files NOT Affected (no Moq usage)

StateServiceTests, PartialFormsWarningViewModelTests, MO2ValidationServiceTests, XEditOutputParserTests, FileDialogServiceTests, DependencyInjectionTests, GameSelectionIntegrationTests, PluginCleaningResultTests, CleaningSessionResultTests, AppStateTests.

### Estimated Effort

| Category | Files | Effort per File | Total |
|----------|-------|-----------------|-------|
| Low complexity | 14 | ~5 min each (mechanical regex + review) | ~70 min |
| Medium complexity | 4 | ~15 min each (manual adjustments) | ~60 min |
| High complexity | 2 | ~30 min each (careful manual migration) | ~60 min |
| Project config (.csproj) | 1 | ~5 min | ~5 min |
| Full test suite verification | 1 | ~15 min | ~15 min |
| **Total** | **21** | | **~3.5 hours** |

---

## 5. Pattern Translation Guide

This section provides the exact Moq-to-NSubstitute translation for every pattern found in the codebase.

### 5.1 Mock Creation

```csharp
// BEFORE (Moq)
private readonly Mock<ILoggingService> _mockLogger = new();
private readonly Mock<IStateService> _mockState = new();
var sut = new MyService(_mockLogger.Object, _mockState.Object);

// AFTER (NSubstitute)
private readonly ILoggingService _mockLogger = Substitute.For<ILoggingService>();
private readonly IStateService _mockState = Substitute.For<IStateService>();
var sut = new MyService(_mockLogger, _mockState);
```

### 5.2 Mock.Of<T>() (Fire-and-Forget)

```csharp
// BEFORE (Moq) -- 9 files
var logger = Mock.Of<ILoggingService>();
var sut = new ConfigurationService(logger);

// AFTER (NSubstitute)
var logger = Substitute.For<ILoggingService>();
var sut = new ConfigurationService(logger);
```

### 5.3 Setup with Return Value

```csharp
// BEFORE (Moq)
_mockConfig.Setup(c => c.GetSettingsAsync()).ReturnsAsync(settings);
_mockState.Setup(s => s.CurrentState).Returns(appState);
_mockState.Setup(s => s.StateChanged).Returns(behaviorSubject);

// AFTER (NSubstitute)
_mockConfig.GetSettingsAsync().Returns(settings);
_mockState.CurrentState.Returns(appState);
_mockState.StateChanged.Returns(behaviorSubject);
```

### 5.4 Setup with Lambda Return

```csharp
// BEFORE (Moq)
_mockState.Setup(s => s.CurrentState).Returns(() => _stateSubject.Value);

// AFTER (NSubstitute)
_mockState.CurrentState.Returns(_ => _stateSubject.Value);
```

### 5.5 ReturnsAsync with Factory Lambda

```csharp
// BEFORE (Moq) -- CleaningOrchestratorTests
_mockCleaningService.Setup(s => s.CleanPluginAsync(
        It.IsAny<string>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(() => new PluginCleaningResult { ... });

// AFTER (NSubstitute)
_mockCleaningService.CleanPluginAsync(
        Arg.Any<string>(), Arg.Any<CancellationToken>())
    .Returns(_ => Task.FromResult(new PluginCleaningResult { ... }));
// Or more concisely if NSubstitute auto-wraps:
_mockCleaningService.CleanPluginAsync(
        Arg.Any<string>(), Arg.Any<CancellationToken>())
    .Returns(_ => new PluginCleaningResult { ... });
```

### 5.6 Async Lambda with TaskCompletionSource

```csharp
// BEFORE (Moq) -- ProcessExecutionServiceTests
_mockService.Setup(s => s.CleanPluginAsync(It.IsAny<string>(), ...))
    .Returns(async (string name, ..., CancellationToken ct) =>
    {
        await tcs.Task;
        return new PluginCleaningResult { ... };
    });

// AFTER (NSubstitute)
_mockService.CleanPluginAsync(Arg.Any<string>(), ...)
    .Returns(async callInfo =>
    {
        await tcs.Task;
        return new PluginCleaningResult { ... };
    });
```

### 5.7 ThrowsAsync

```csharp
// BEFORE (Moq)
_mockService.Setup(s => s.RunAsync(It.IsAny<CancellationToken>()))
    .ThrowsAsync(new InvalidOperationException("test"));

// AFTER (NSubstitute)
// Requires: using NSubstitute.ExceptionExtensions;
_mockService.RunAsync(Arg.Any<CancellationToken>())
    .ThrowsAsync(new InvalidOperationException("test"));
```

### 5.8 Callback Delegate Capture (Most Complex Pattern)

```csharp
// BEFORE (Moq) -- ErrorDialogTests
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

// AFTER (NSubstitute) -- using Arg.Do<T>() for capture
_mockOrchestrator.RunCleaningAsync(
        Arg.Do<TimeoutRetryCallback?>(cb => capturedTimeoutCallback = cb),
        Arg.Do<BackupFailureCallback?>(cb => capturedBackupCallback = cb),
        Arg.Any<CancellationToken>())
    .Returns(Task.CompletedTask);
```

### 5.9 Argument Matchers

```csharp
// BEFORE (Moq)
It.IsAny<string>()
It.Is<string>(s => s.Contains("plugin"))

// AFTER (NSubstitute)
Arg.Any<string>()
Arg.Is<string>(s => s.Contains("plugin"))
```

### 5.10 Verification -- Times.Once

```csharp
// BEFORE (Moq)
_mockLogger.Verify(l => l.LogInformation(It.IsAny<string>()), Times.Once);

// AFTER (NSubstitute)
_mockLogger.Received(1).LogInformation(Arg.Any<string>());
```

### 5.11 Verification -- Times.Never

```csharp
// BEFORE (Moq)
_mockService.Verify(s => s.CleanPluginAsync(It.IsAny<string>(),
    It.IsAny<CancellationToken>()), Times.Never);

// AFTER (NSubstitute)
_mockService.DidNotReceive().CleanPluginAsync(Arg.Any<string>(),
    Arg.Any<CancellationToken>());
```

### 5.12 Verification -- Times.AtLeastOnce

```csharp
// BEFORE (Moq)
_mockLogger.Verify(l => l.LogInformation(It.IsAny<string>()), Times.AtLeastOnce);

// AFTER (NSubstitute)
_mockLogger.Received().LogInformation(Arg.Any<string>());
// Note: Received() without a count means "at least once" (default behavior)
```

### 5.13 Verification -- Times.AtLeast(N) and Times.Exactly(N)

```csharp
// BEFORE (Moq) -- CleaningOrchestratorTests
_mockService.Verify(s => s.CleanPluginAsync(...), Times.Exactly(3));
_mockService.Verify(s => s.CleanPluginAsync(...), Times.AtLeast(2));

// AFTER (NSubstitute)
// Times.Exactly(N) maps directly:
_mockService.Received(3).CleanPluginAsync(...);

// Times.AtLeast(N) requires a workaround:
_mockService.ReceivedCalls()
    .Count(c => c.GetMethodInfo().Name == nameof(ICleaningService.CleanPluginAsync))
    .Should().BeGreaterOrEqualTo(2);
```

### 5.14 Verification with Predicate Matchers

```csharp
// BEFORE (Moq)
_mockService.Verify(s => s.SaveResult(
    It.Is<CleaningResult>(r => r.Status == CleaningStatus.Success)),
    Times.Once);

// AFTER (NSubstitute)
_mockService.Received(1).SaveResult(
    Arg.Is<CleaningResult>(r => r.Status == CleaningStatus.Success));
```

### 5.15 Observable/BehaviorSubject Wiring (ViewModel Tests)

```csharp
// BEFORE (Moq)
_stateSubject = new BehaviorSubject<AppState>(initialState);
_mockStateService.Setup(s => s.StateChanged).Returns(_stateSubject);
_mockStateService.Setup(s => s.CurrentState).Returns(() => _stateSubject.Value);
_mockOrchestrator.Setup(o => o.Progress).Returns(Observable.Never<CleaningProgress>());

// AFTER (NSubstitute) -- identical structure, cleaner syntax
_stateSubject = new BehaviorSubject<AppState>(initialState);
_mockStateService.StateChanged.Returns(_stateSubject);
_mockStateService.CurrentState.Returns(_ => _stateSubject.Value);
_mockOrchestrator.Progress.Returns(Observable.Never<CleaningProgress>());
```

---

## 6. Risks and Mitigations

### Risk 1: `.Object` Replacement False Positives
**Risk**: Find-and-replace of `.Object` may accidentally modify non-Moq code (e.g., `result.Object`, LINQ `.Object` property).
**Mitigation**: Do NOT use global regex replacement for `.Object`. Instead, handle it per-file: replace `_mockFoo.Object` patterns where `_mockFoo` is a known `Mock<T>` variable. The type declaration change from `Mock<T>` to `T` naturally eliminates the need for `.Object`.
**Likelihood**: Medium | **Impact**: Low (compilation error catches it)

### Risk 2: Times.AtLeast(N) Workaround Verbosity
**Risk**: 3 usages of `Times.AtLeast(N)` in CleaningOrchestratorTests require a `ReceivedCalls()` workaround that is more verbose.
**Mitigation**: Create a small extension method or simply inline the `ReceivedCalls().Count().Should()` pattern. Only affects 1 file.
**Likelihood**: Certain | **Impact**: Very Low (cosmetic)

### Risk 3: Thread Safety with xUnit Parallel Execution
**Risk**: NSubstitute argument matchers use ambient state. Misplaced `Arg.Any`/`Arg.Is` can leak between parallel tests, causing `UnexpectedArgumentMatcherException`.
**Mitigation**: Install `NSubstitute.Analyzers.CSharp` which catches these at compile time. The project already uses argument matchers correctly with Moq, and the NSubstitute equivalents have the same constraints.
**Likelihood**: Low | **Impact**: Medium (flaky tests if triggered)

### Risk 4: Sequential Return Behavior Difference
**Risk**: NSubstitute's `Returns(a, b, c)` repeats the last value after exhaustion, while Moq's `SetupSequence` returns `default(T)`. Tests relying on post-sequence `default` would break.
**Mitigation**: **Not applicable.** The audit confirms the codebase does NOT use `SetupSequence`. Sequential behavior is achieved via factory lambdas, which translate directly.
**Likelihood**: None | **Impact**: N/A

### Risk 5: Callback Delegate Capture Migration
**Risk**: ErrorDialogTests uses `.Callback<T1,T2,T3>()` which has no syntactic equivalent in NSubstitute.
**Mitigation**: Use `Arg.Do<T>()` per-argument capture (see Section 5.8). This is actually cleaner and more readable. Requires manual translation but is straightforward.
**Likelihood**: Certain | **Impact**: Low (well-defined migration path)

### Rollback Plan

1. All changes are in `AutoQAC.Tests/` only -- production code is untouched.
2. Create a feature branch (`migrate/moq-to-nsubstitute`) before starting.
3. If migration fails or introduces regressions, `git checkout main` restores the original test suite.
4. Moq and NSubstitute can technically coexist during incremental migration if needed (both use Castle.Core).

---

## 7. Migration Strategy

### Recommended: Single-Branch, File-by-File Migration

Given the manageable scope (20 files, ~3.5 hours), a single-branch migration is recommended over a multi-phase approach. The patterns are mechanical enough that incremental migration adds coordination overhead without meaningful risk reduction.

### Execution Steps

#### Step 1: Branch and Package Setup (~5 min)
```bash
git checkout -b migrate/moq-to-nsubstitute
```
- Remove `Moq` package from `AutoQAC.Tests.csproj`
- Add `NSubstitute` 5.3.0 and `NSubstitute.Analyzers.CSharp` 1.0.17
- Run `dotnet restore`

#### Step 2: Low-Complexity Files First (14 files, ~70 min)
Migrate the 14 low-complexity files using mechanical find-and-replace:
1. Replace `using Moq;` with `using NSubstitute;`
2. Replace `new Mock<T>()` with `Substitute.For<T>()`
3. Replace `Mock.Of<T>()` with `Substitute.For<T>()`
4. Change field types from `Mock<T>` to `T`
5. Remove all `.Object` on mock variable references
6. Replace `.Setup(x => x.Method()).Returns(val)` with `.Method().Returns(val)`
7. Replace `.Setup(x => x.Method()).ReturnsAsync(val)` with `.Method().Returns(val)`
8. Replace `It.IsAny<T>()` with `Arg.Any<T>()`
9. Replace `It.Is<T>(pred)` with `Arg.Is<T>(pred)`
10. Replace `.Verify(x => x.Method(), Times.Once)` with `.Received(1).Method()`
11. Replace `.Verify(x => x.Method(), Times.Never)` with `.DidNotReceive().Method()`
12. Verify compilation after each file

#### Step 3: Medium-Complexity Files (4 files, ~60 min)
Migrate MainWindowViewModelTests, CleaningServiceTests, ProcessExecutionServiceTests, CleaningResultsViewModelTests with the same mechanical steps plus:
- Add `using NSubstitute.ExceptionExtensions;` where `ThrowsAsync` is used
- Convert `Times.AtLeastOnce` to `Received()` (no count)
- Convert async lambda returns to NSubstitute `Returns(async callInfo => ...)`
- Verify observable/BehaviorSubject wiring compiles

#### Step 4: High-Complexity Files (2 files, ~60 min)
Migrate CleaningOrchestratorTests and ErrorDialogTests with careful manual attention:
- **CleaningOrchestratorTests**: Convert factory lambdas, `Times.AtLeast(N)` to `ReceivedCalls()` assertions, `Times.Exactly(N)` to `Received(N)`
- **ErrorDialogTests**: Convert `.Callback<T1,T2,T3>()` to `Arg.Do<T>()` per-argument capture

#### Step 5: Full Verification (~15 min)
```bash
dotnet build AutoQAC.Tests
dotnet test
```
- All 510+ tests must pass
- Zero NSubstitute analyzer warnings
- Zero Moq references remaining

#### Step 6: Cleanup
- Verify no `using Moq;` references remain: `grep -r "using Moq" AutoQAC.Tests/`
- Verify no `Mock<` references remain: `grep -r "Mock<" AutoQAC.Tests/`
- Verify Moq package is fully removed from `.csproj`
- Update `CLAUDE.md` to reference NSubstitute instead of Moq

---

## 8. Acceptance Criteria

### Must Have (Migration Complete)

- [ ] Moq NuGet package removed from `AutoQAC.Tests.csproj`
- [ ] NSubstitute 5.3.0 added to `AutoQAC.Tests.csproj`
- [ ] NSubstitute.Analyzers.CSharp 1.0.17 added to `AutoQAC.Tests.csproj`
- [ ] Zero `using Moq;` directives in any test file
- [ ] Zero `Mock<T>` type references in any test file
- [ ] Zero `.Object` Moq property accesses in any test file
- [ ] All 510+ tests pass (`dotnet test` exit code 0)
- [ ] `dotnet build AutoQAC.Tests` compiles with zero errors
- [ ] Zero NSubstitute.Analyzers warnings (no misplaced argument matchers)

### Should Have (Quality)

- [ ] CLAUDE.md updated to reference NSubstitute (testing section, mocking library, Moq parameter matching note)
- [ ] MEMORY.md updated to replace Moq references with NSubstitute equivalents
- [ ] Test count unchanged (no tests removed or skipped during migration)

### Must NOT Have

- [ ] No changes to any file in `AutoQAC/` (production code untouched)
- [ ] No new test files added (migration only, no new tests)
- [ ] No test logic changes (only mock framework syntax changes)
- [ ] No disabled or skipped tests

---

## 9. Dependencies

### NuGet Package Changes

| Action | Package | Version | Notes |
|--------|---------|---------|-------|
| **Remove** | Moq | 4.20.72 | Current mocking library |
| **Add** | NSubstitute | 5.3.0 | Replacement mocking library |
| **Add** | NSubstitute.Analyzers.CSharp | 1.0.17 | Compile-time analyzers (dev-only) |

### Compatibility Matrix

| Dependency | Version | Compatible with NSubstitute 5.3.0? |
|------------|---------|-------------------------------------|
| .NET | 10 (LTS) | Yes (targets .NET Standard 2.0 + .NET 6.0) |
| C# | 13 | Yes |
| xUnit | 2.9.3 | Yes (framework-agnostic) |
| FluentAssertions | 8.8.0 | Yes (independent library) |
| ReactiveUI | 11.3.8 | Yes (no interaction) |
| Castle.Core | (transitive) | Already used by Moq; NSubstitute also uses it |

### Shared Transitive Dependency

Both Moq and NSubstitute depend on **Castle.Core** (DynamicProxy) for runtime proxy generation. The transition does not introduce any new transitive dependency. Castle.Core version may change slightly but this is transparent.

---

## 10. Out of Scope

This migration explicitly does **NOT** include:

1. **Production code changes** -- Zero modifications to `AutoQAC/` project files
2. **New test coverage** -- No new tests are written; this is a framework swap only
3. **Test refactoring** -- Tests retain their existing structure and assertions; only mock syntax changes
4. **Architecture changes** -- No changes to interfaces, DI registration, or service contracts
5. **Other test library changes** -- xUnit, FluentAssertions, and other test dependencies remain unchanged
6. **Performance optimization** -- No test performance tuning (NSubstitute and Moq have comparable performance)
7. **CI/CD changes** -- Build pipeline needs no modification (same `dotnet test` command)
8. **Code coverage tooling** -- Coverlet configuration unchanged
9. **Migration of future patterns** -- Only patterns currently in the codebase are addressed; the NSubstitute research document covers additional patterns for future reference

---

## Appendix A: Automated Migration Tooling

Three tools exist that can accelerate the mechanical portion of this migration:

### A.1 moq-to-nsub CLI Tool
- **NuGet**: `Moq2NSubstitute` (dotnet tool)
- **Usage**: `moq2nsub convert --project-path J:\AutoQACSharp\AutoQAC.Tests`
- **Accuracy**: ~80% (handles common patterns, requires manual cleanup)
- **Recommendation**: Consider running first for the 14 low-complexity files, then manually clean up

### A.2 MoqToNSubstituteConverter Web Tool
- **URL**: https://moqtonsubstitute.azurewebsites.net/
- **Usage**: Paste Moq code, get NSubstitute code (~90% accuracy)
- **Recommendation**: Useful for complex individual methods or classes

### A.3 Regex Patterns for IDE Find-and-Replace

| Find (Regex) | Replace |
|--------------|---------|
| `using Moq;` | `using NSubstitute;` |
| `new Mock<(.+?)>\(\)` | `Substitute.For<$1>()` |
| `Mock\.Of<(.+?)>\(\)` | `Substitute.For<$1>()` |
| `Mock<(.+?)>` | `$1` |
| `\.ReturnsAsync\(` | `.Returns(` |
| `It\.IsAny` | `Arg.Any` |
| `It\.Is` | `Arg.Is` |

**Warning**: The `.Object` removal and `.Setup()`/`.Verify()` unwrapping cannot be reliably done with regex. Handle these manually or per-variable.

---

## Appendix B: Reference Documents

- **Moq Audit**: `.planning/prd/moq-audit.md` -- Complete catalog of every Moq usage in the codebase
- **NSubstitute Research**: `.planning/prd/nsubstitute-research.md` -- Comprehensive NSubstitute documentation with all pattern equivalents, gotchas, and best practices
