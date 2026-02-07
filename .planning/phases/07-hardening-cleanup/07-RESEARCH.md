# Phase 7: Hardening & Cleanup - Research

**Researched:** 2026-02-07
**Domain:** Test coverage, dependency management, coverage tooling, reference code cleanup
**Confidence:** HIGH

## Summary

This phase covers four non-feature work streams: filling test coverage gaps for phases 1-6 features, updating all NuGet dependencies to latest stable versions, integrating coverage tooling (Coverlet + ReportGenerator), and removing the `Code_To_Port/` reference directory with CLAUDE.md cleanup.

The project already has 475 tests across 26 test files, with strong coverage for core services (StateService, ConfigurationService, CleaningOrchestrator, PluginValidationService, GameDetectionService). However, several services and ViewModels added in phases 1-6 have zero dedicated test files. The requirements TEST-01 through TEST-05 target specific edge case gaps, while TEST-06 is the broad coverage sweep.

The project already targets .NET 10 and has Avalonia 11.3.11. The main dependency concern is Mutagen.Bethesda 0.52.0 which has newer Kernel (0.53.0) available. YamlDotNet 16.3.0 is current. Coverlet.collector 6.0.4 is already in the test project but not configured for automatic collection.

**Primary recommendation:** Split into two plans -- Plan 1 handles targeted gap tests (TEST-01 through TEST-05) plus coverage tooling setup; Plan 2 handles broad feature coverage (TEST-06), dependency updates (DEP-01, DEP-02), and Code_To_Port cleanup (POST-01).

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- Claude analyzes existing test gaps and prioritizes based on both risk and coverage density
- Fill gaps in both happy-path and failure/edge-case paths equally
- Unit tests only (with mocks/stubs) -- no integration tests with real file I/O or process spawning
- 80% is a reasonable target on critical paths, not a hard floor -- some classes (UI wiring, etc.) can fall below if hard to test
- Update ALL NuGet packages to latest stable versions (not just Mutagen and YamlDotNet)
- If a dependency update introduces breaking API changes, fix the code immediately to use the new API -- don't leave deprecated patterns
- Bump target framework to latest stable .NET if available (e.g., .NET 10) -- verify Avalonia/ReactiveUI support first
- Run a feature parity audit of Code_To_Port/ against implemented C# features before deletion
- Claude decides how to handle gaps: critical ones get ported, minor ones get documented as future work
- Nothing to preserve from Code_To_Port/ -- git history is sufficient
- Full CLAUDE.md cleanup: remove all porting guidelines, translation tables, and Code_To_Port/ references after deletion
- Coverlet for coverage collection -- integrates with dotnet test
- Track both line and branch coverage; 80% line coverage is the primary target
- Coverage integrated into `dotnet test` via MSBuild properties (automatic every test run)
- Output formats: Cobertura XML (for tooling) + HTML via ReportGenerator (for human review)

### Claude's Discretion
- Which specific classes/methods to prioritize for test coverage based on gap analysis
- How to handle minor unported features found during parity audit
- Coverlet configuration details and MSBuild property setup
- Test file organization and naming conventions for new tests

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope
</user_constraints>

## Standard Stack

### Core (Testing)
| Library | Version | Purpose | Already Installed |
|---------|---------|---------|-------------------|
| xUnit | 2.9.3 | Test framework | Yes |
| Moq | 4.20.72 | Mocking framework | Yes |
| FluentAssertions | 8.8.0 | Assertion library | Yes |
| coverlet.collector | 6.0.4 | Coverage data collector (VSTest) | Yes |
| coverlet.msbuild | 6.0.4 | Coverage via MSBuild (automatic) | **No -- ADD** |

### Core (Coverage Reporting)
| Tool | Version | Purpose | Install Method |
|------|---------|---------|----------------|
| dotnet-reportgenerator-globaltool | 5.5.1 | HTML coverage reports | `dotnet tool install -g` |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| coverlet.msbuild | coverlet.collector only | collector needs `--collect:"XPlat Code Coverage"` flag each run; msbuild auto-runs on every `dotnet test` |
| ReportGenerator | Manual Cobertura XML review | HTML is far more usable for humans; no alternative needed |

**Installation:**
```bash
# Add coverlet.msbuild to test project for automatic coverage
dotnet add AutoQAC.Tests package coverlet.msbuild

# Install ReportGenerator as global tool
dotnet tool install -g dotnet-reportgenerator-globaltool
```

## Architecture Patterns

### Test File Organization (Recommendation)

Follow existing conventions established in the project:

```
AutoQAC.Tests/
├── Models/                          # Model unit tests
│   ├── AppStateTests.cs
│   ├── CleaningSessionResultTests.cs
│   └── PluginCleaningResultTests.cs
├── Services/                        # Service unit tests
│   ├── BackupServiceTests.cs        # NEW
│   ├── CleaningOrchestratorTests.cs
│   ├── CleaningServiceTests.cs
│   ├── ConfigurationServiceTests.cs
│   ├── ConfigurationServiceSkipListTests.cs
│   ├── ConfigWatcherServiceTests.cs  # NEW (if testable with mocks)
│   ├── GameDetectionServiceTests.cs
│   ├── HangDetectionServiceTests.cs
│   ├── LegacyMigrationServiceTests.cs  # NEW
│   ├── LogRetentionServiceTests.cs     # NEW
│   ├── MO2ValidationServiceTests.cs
│   ├── PluginLoadingServiceTests.cs
│   ├── PluginValidationServiceTests.cs
│   ├── ProcessExecutionServiceTests.cs
│   ├── XEditCommandBuilderTests.cs
│   ├── XEditLogFileServiceTests.cs     # NEW
│   ├── XEditOutputParserTests.cs
│   └── UI/
│       └── FileDialogServiceTests.cs
├── ViewModels/                      # ViewModel unit tests
│   ├── CleaningResultsViewModelTests.cs
│   ├── ErrorDialogTests.cs
│   ├── MainWindowViewModelInitializationTests.cs
│   ├── MainWindowViewModelTests.cs
│   ├── PartialFormsWarningViewModelTests.cs
│   ├── ProgressViewModelTests.cs
│   ├── SettingsViewModelTests.cs       # NEW (if testable)
│   └── SkipListViewModelTests.cs
└── Integration/
    ├── DependencyInjectionTests.cs
    └── GameSelectionIntegrationTests.cs
```

**Naming convention:** `{ClassName}Tests.cs` -- already established, continue it.

### Test Pattern: Mocking for Service Tests

All tests use constructor-injected mock dependencies via Moq. Standard pattern:

```csharp
public sealed class SomeServiceTests
{
    private readonly Mock<IDependency> _mockDep;
    private readonly SomeService _sut;

    public SomeServiceTests()
    {
        _mockDep = new Mock<IDependency>();
        _sut = new SomeService(_mockDep.Object);
    }

    [Fact]
    public async Task MethodName_WhenCondition_ShouldExpectedBehavior()
    {
        // Arrange
        _mockDep.Setup(d => d.SomeMethod()).Returns(expectedValue);

        // Act
        var result = await _sut.MethodUnderTest();

        // Assert
        result.Should().Be(expected);
        _mockDep.Verify(d => d.SomeMethod(), Times.Once);
    }
}
```

### Anti-Patterns to Avoid
- **Real file I/O in unit tests:** User explicitly prohibited integration tests. For services that do file I/O (LegacyMigrationService, LogRetentionService, BackupService), test through interface mocks or use temp files with cleanup.
- **Testing private methods directly:** Test through public API. ProcessExecutionService.IsXEditProcess is private and should be tested through CleanOrphanedProcessesAsync behavior.
- **Ignoring Moq parameter matching:** Per project MEMORY.md -- ALL mock Setup AND Verify calls must include every parameter (Moq doesn't treat C# optional parameters as truly optional).

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Coverage collection | Custom test instrumentation | coverlet.msbuild + MSBuild properties | Battle-tested, zero friction |
| Coverage reporting | Manual XML parsing | ReportGenerator global tool | Rich HTML, line-by-line visualization |
| Test data factories | Complex object builders | Simple factory methods (already used) | Project already has `CreatePluginInfoList` helpers |

## Common Pitfalls

### Pitfall 1: ProcessExecutionService Tests Spawn Real Processes
**What goes wrong:** Existing ProcessExecutionServiceTests.cs runs `cmd.exe` -- these are integration tests, not unit tests. They pass but are slow and environment-dependent.
**Why it happens:** ProcessExecutionService directly creates `System.Diagnostics.Process` which is hard to mock.
**How to avoid:** For TEST-01 (process termination edge cases), test TerminateProcessAsync behavior by creating mock Process objects where possible. For methods that call `Process.Start()`, test through CleaningOrchestrator with mocked IProcessExecutionService instead.
**Warning signs:** Tests that need `cmd.exe` or create real processes.

### Pitfall 2: LegacyMigrationService Has File System Dependencies
**What goes wrong:** LegacyMigrationService uses `File.Exists`, `File.ReadAllTextAsync`, `File.Copy`, `File.Delete` directly.
**Why it happens:** Constructor accepts `configDirectory` parameter for testability, but still uses System.IO.
**How to avoid:** Use temp directories for test fixtures. The constructor's `configDirectory` parameter enables this pattern -- pass a temp dir, create fixture files, assert on outcomes, clean up in Dispose.
**Warning signs:** Tests that assume specific paths exist or leave files behind.

### Pitfall 3: coverlet.msbuild + coverlet.collector Conflict
**What goes wrong:** Having both packages can cause confusing double-counting or output conflicts.
**Why it happens:** Project already has coverlet.collector 6.0.4.
**How to avoid:** Use coverlet.msbuild for automatic collection (via MSBuild properties in csproj). Remove or leave coverlet.collector -- it won't conflict if you don't also use `--collect:"XPlat Code Coverage"`. Best: keep both, use msbuild properties for day-to-day runs.
**Warning signs:** Two coverage output files or unexpected coverage numbers.

### Pitfall 4: xUnit v2 Must Stay (Not v3)
**What goes wrong:** Upgrading to xUnit v3 breaks Avalonia.Headless.XUnit compatibility.
**Why it happens:** Per STATE.md: "xUnit v2 required for Avalonia.Headless.XUnit -- do not migrate to xUnit v3."
**How to avoid:** When updating ALL NuGet packages, explicitly skip xUnit to v3. Current xunit 2.9.3 is the latest v2 line. xunit.runner.visualstudio 3.1.5 is fine (runner is v3-compatible with v2 tests).
**Warning signs:** `xunit` package version starting with 3.x.

### Pitfall 5: Mutagen Version Mismatch Across Sub-Packages
**What goes wrong:** Mutagen.Bethesda, Mutagen.Bethesda.Skyrim, and Mutagen.Bethesda.Fallout4 must be version-aligned.
**Why it happens:** NuGet shows Mutagen.Bethesda at 0.51.5 (latest main), but game-specific packages at 0.52.0.
**How to avoid:** Check `dotnet list package --outdated` output carefully. Update all Mutagen packages together. Run full test suite after update.
**Warning signs:** `dotnet restore` warnings about version conflicts.

### Pitfall 6: Moq Optional Parameter Mismatch
**What goes wrong:** When new optional parameters were added to interfaces in phases 1-6, some mock setups may not match the new signatures.
**Why it happens:** Moq matches by exact parameter count, not by C# default values.
**How to avoid:** Verify all existing tests pass before adding new ones. Any test that broke during phases 1-6 should already be fixed, but double-check.
**Warning signs:** `Moq.MockException: Expected invocation ... but was never performed`.

## Code Examples

### Coverlet MSBuild Configuration (csproj)

```xml
<!-- In AutoQAC.Tests.csproj PropertyGroup -->
<PropertyGroup>
  <!-- ... existing properties ... -->

  <!-- Coverage: auto-collect on every dotnet test run -->
  <CollectCoverage>true</CollectCoverage>
  <CoverletOutputFormat>cobertura</CoverletOutputFormat>
  <CoverletOutput>./TestResults/coverage/</CoverletOutput>
  <Include>[AutoQAC]*</Include>
  <Exclude>[AutoQAC.Tests]*</Exclude>
  <ExcludeByAttribute>ExcludeFromCodeCoverage</ExcludeByAttribute>
</PropertyGroup>
```

**Source:** [Coverlet MSBuild Integration docs](https://github.com/coverlet-coverage/coverlet/blob/master/Documentation/MSBuildIntegration.md)

### ReportGenerator Usage

```bash
# Generate HTML report from Cobertura XML
reportgenerator -reports:"AutoQAC.Tests/TestResults/coverage/coverage.cobertura.xml" -targetdir:"coverage-report" -reporttypes:Html
```

**Source:** [ReportGenerator GitHub](https://github.com/danielpalme/ReportGenerator)

### Test Pattern: Services That Use File System (LegacyMigrationService)

```csharp
public sealed class LegacyMigrationServiceTests : IDisposable
{
    private readonly Mock<ILoggingService> _mockLogger;
    private readonly string _tempDir;
    private readonly LegacyMigrationService _sut;

    public LegacyMigrationServiceTests()
    {
        _mockLogger = new Mock<ILoggingService>();
        _tempDir = Path.Combine(Path.GetTempPath(), $"autoqac_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _sut = new LegacyMigrationService(_mockLogger.Object, _tempDir);
    }

    [Fact]
    public async Task MigrateIfNeeded_WhenNoLegacyFile_ReturnsNotNeeded()
    {
        var result = await _sut.MigrateIfNeededAsync();
        result.Attempted.Should().BeFalse();
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task MigrateIfNeeded_WhenLegacyExists_MigratesAndBackups()
    {
        File.WriteAllText(Path.Combine(_tempDir, "AutoQAC Config.yaml"), "Settings: {}");

        var result = await _sut.MigrateIfNeededAsync();

        result.Attempted.Should().BeTrue();
        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "AutoQAC Settings.yaml")).Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
```

### Test Pattern: Concurrent State Updates (Already Established)

```csharp
[Fact]
public async Task ConcurrentUpdates_ShouldMaintainStateConsistency()
{
    const int numThreads = 10;
    const int updatesPerThread = 100;
    var barrier = new Barrier(numThreads);

    var tasks = Enumerable.Range(0, numThreads).Select(threadId => Task.Run(() =>
    {
        barrier.SignalAndWait();
        for (int i = 0; i < updatesPerThread; i++)
        {
            _sut.UpdateState(s => s with { Progress = threadId * 1000 + i });
        }
    }));

    await Task.WhenAll(tasks);

    var finalState = _sut.CurrentState;
    finalState.Should().NotBeNull();
}
```

**Source:** Existing pattern in `AutoQAC.Tests/Services/StateServiceTests.cs`

## Test Coverage Gap Analysis

### Services WITH NO dedicated test files (HIGH priority for TEST-06)

| Service | Lines | Risk Level | Testability | Notes |
|---------|-------|------------|-------------|-------|
| LegacyMigrationService | 169 | HIGH | Good (configDirectory ctor param) | TEST-02: migration failure paths |
| BackupService | 219 | HIGH | Good (pure logic + file ops) | Phase 5 feature, critical path |
| LogRetentionService | 125 | MEDIUM | Good (mockable config) | Phase 4 feature |
| XEditLogFileService | 84 | MEDIUM | Good (pure file logic) | Phase 3 feature |
| ConfigWatcherService | 244 | LOW | Hard (FSW + Rx internals) | Complex reactive pipeline |

### Services WITH test files but SPECIFIC gaps (TEST-01 through TEST-05)

| Requirement | Service | Current Tests | Gap |
|-------------|---------|---------------|-----|
| TEST-01 | ProcessExecutionService | 7 tests (all integration) | No unit tests for TerminateProcessAsync, CleanOrphanedProcessesAsync, PID tracking |
| TEST-02 | ConfigurationService | 21 tests | No migration failure tests (LegacyMigrationService has 0 tests) |
| TEST-03 | ConfigurationService (skip list) | 29 tests | No explicit test for GameType.Unknown skip list loading |
| TEST-04 | StateService | 25 tests (5 concurrent) | Concurrent tests exist but could add edge cases |
| TEST-05 | PluginValidationService | 25 tests | Has NonRootedPath test but only 1; could add null FullPath, relative path variants |

### ViewModels WITH NO test files (lower priority)

| ViewModel | Lines | Risk | Notes |
|-----------|-------|------|-------|
| SettingsViewModel | ~400+ | LOW | UI-heavy, path validation logic |
| ConfigurationViewModel | ~300+ | LOW | UI wiring, file dialogs |
| PluginListViewModel | ~200+ | LOW | List management |
| CleaningCommandsViewModel | ~200+ | LOW | Command wiring |
| AboutViewModel | ~100 | VERY LOW | Static info display |
| RestoreViewModel | ~200+ | LOW | Backup browsing UI |

**Recommendation for Claude's Discretion:** Focus testing effort on Services (high business logic density) over ViewModels (mostly UI wiring). SettingsViewModel has some validation logic worth testing if time allows.

## Dependency Update Analysis

### Current vs Latest Versions

| Package | Current | Latest Stable | Action | Risk |
|---------|---------|---------------|--------|------|
| Avalonia (all) | 11.3.11 | 11.3.11 | None needed | - |
| ReactiveUI.Avalonia | 11.3.8 | 11.3.8 | None needed | - |
| Microsoft.Extensions.DI | 10.0.2 | Check with `dotnet list` | Update if newer | LOW |
| Serilog | 4.3.0 | 4.3.0 | None needed | - |
| Serilog.Sinks.Console | 6.1.1 | Check with `dotnet list` | Update if newer | LOW |
| Serilog.Sinks.File | 7.0.0 | Check with `dotnet list` | Update if newer | LOW |
| YamlDotNet | 16.3.0 | 16.3.0 | None needed | - |
| Mutagen.Bethesda | 0.52.0 | 0.51.5 main / 0.52.0 game | Verify alignment | MEDIUM |
| xunit | 2.9.3 | 2.9.3 (DO NOT upgrade to v3) | None needed | - |
| FluentAssertions | 8.8.0 | 8.8.0 | None needed | - |
| Moq | 4.20.72 | 4.20.72 | None needed | - |
| coverlet.collector | 6.0.4 | 6.0.4 | None needed | - |
| Microsoft.NET.Test.Sdk | 18.0.1 | Check with `dotnet list` | Update if newer | LOW |
| xunit.runner.visualstudio | 3.1.5 | Check with `dotnet list` | Update if newer | LOW |

**Framework:** Project already targets `net10.0-windows10.0.19041.0`. .NET 10 was released November 2025 as LTS. Avalonia 11.3.11 supports .NET 10. No framework bump needed.

**Key concern:** Run `dotnet list package --outdated` at execution time to get authoritative latest versions from the project's configured NuGet feeds.

## Feature Parity Audit Strategy

### Python Reference (Code_To_Port/) vs C# Implementation

| Python Module | C# Equivalent | Status |
|---------------|---------------|--------|
| `state_manager.py` | `StateService.cs` | Complete |
| `config_manager.py` | `ConfigurationService.cs` | Complete |
| `yaml_manager.py` | `ConfigurationService.cs` (integrated) | Complete |
| `cleaning_service.py` | `CleaningService.cs` + `CleaningOrchestrator.cs` | Complete |
| `cleaning_worker.py` | `CleaningOrchestrator.cs` (async/await replaces QThread) | Complete |
| `gui_controller.py` | ViewModels (MainWindowViewModel + sub-VMs) | Complete |
| `game_detection.py` | `GameDetectionService.cs` | Complete |
| `plugin_validator.py` | `PluginValidationService.cs` | Complete |
| `process_utils.py` | `ProcessExecutionService.cs` | Complete |
| `migration.py` | `LegacyMigrationService.cs` | Complete |
| `logging_config.py` | `LoggingService.cs` | Complete |
| `configuration_dialogs.py` | `SettingsWindow.axaml` + `SettingsViewModel.cs` | Complete |
| `ui/main_window.py` + mixins | `MainWindow.axaml` + decomposed sub-VMs | Complete |
| `ui/dialogs/cleaning_progress.py` | `ProgressWindow.axaml` + `ProgressViewModel.cs` | Complete |
| `ui/dialogs/partial_forms.py` | `PartialFormsWarningDialog.axaml` | Complete |

**Assessment:** All Python modules have C# equivalents. The C# implementation has ADDITIONAL features not in Python (dry-run preview, backup/restore, hang detection, config file watching, log retention). Full parity has been achieved plus enhancements.

**Recommendation:** The parity audit should be a quick verification pass during execution, not a deep analysis. The table above can be used as a checklist. Any minor gaps found should be documented as future work per user decision.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| coverlet.collector only | coverlet.msbuild for auto-collection | coverlet 6.0 | Coverage runs automatically on every `dotnet test` |
| Manual coverage review | ReportGenerator HTML | Ongoing | Line-by-line visual coverage |
| xUnit v2 | xUnit v3 available | 2024+ | DO NOT migrate (Avalonia.Headless.XUnit dependency) |
| .NET 9 | .NET 10 LTS | Nov 2025 | Already migrated, 3-year support |

## Open Questions

1. **Mutagen version alignment**
   - What we know: Mutagen.Bethesda main package shows 0.51.5 on NuGet, but game-specific packages (Skyrim, Fallout4) show 0.52.0. Project currently uses 0.52.0 for all.
   - What's unclear: Whether 0.52.0 is correct for Mutagen.Bethesda base package (may be a NuGet listing lag)
   - Recommendation: Run `dotnet list package --outdated` at execution time for authoritative versions. Keep all Mutagen packages at same version.

2. **ConfigWatcherService testability**
   - What we know: Uses FileSystemWatcher + Rx pipeline. Hard to unit test without real filesystem.
   - What's unclear: Whether the hash comparison logic can be unit tested through the public API.
   - Recommendation: Test the hash gate logic where possible; accept that FSW integration testing is out of scope per user decision (unit tests only).

## Sources

### Primary (HIGH confidence)
- [NuGet Gallery - Avalonia 11.3.11](https://www.nuget.org/packages/Avalonia/) - Version verified
- [NuGet Gallery - coverlet.msbuild 6.0.4](https://www.nuget.org/packages/coverlet.msbuild/) - Version verified
- [NuGet Gallery - ReportGenerator 5.5.1](https://www.nuget.org/packages/dotnet-reportgenerator-globaltool) - Version verified
- [Coverlet MSBuild Integration docs](https://github.com/coverlet-coverage/coverlet/blob/master/Documentation/MSBuildIntegration.md) - Configuration verified
- [ReportGenerator GitHub](https://github.com/danielpalme/ReportGenerator) - Usage verified
- Project source code analysis - Test file inventory, service analysis, dependency versions

### Secondary (MEDIUM confidence)
- [Avalonia .NET 10 support](https://avaloniaui.net/whats-new/11-12) - Avalonia 11.12 adds .NET 10 templates
- [.NET 10 LTS release](https://devblogs.microsoft.com/dotnet/announcing-dotnet-10/) - Released Nov 2025
- [xUnit v2 deprecation](https://www.nuget.org/packages/xunit/) - v2.9.3 marked deprecated, but required for project

### Tertiary (LOW confidence)
- [Mutagen.Bethesda versions](https://www.nuget.org/packages/Mutagen.Bethesda/) - NuGet shows 0.51.5 as latest main; discrepancy with game packages at 0.52.0

## Metadata

**Confidence breakdown:**
- Test gap analysis: HIGH - Direct source code and test file inspection
- Coverage tooling: HIGH - Official Coverlet and ReportGenerator documentation
- Dependency versions: MEDIUM - NuGet Gallery checked, but `dotnet list package --outdated` at execution time is authoritative
- Feature parity audit: HIGH - Both Python and C# codebases examined
- Architecture patterns: HIGH - Follows existing project conventions

**Research date:** 2026-02-07
**Valid until:** 2026-03-07 (stable domain, 30 days)
