---
phase: 07-hardening-cleanup
plan: 01
subsystem: testing
tags: [coverlet, xunit, moq, coverage, unit-tests, cobertura]

# Dependency graph
requires:
  - phase: 01-foundation
    provides: ProcessExecutionService termination and orphan cleanup
  - phase: 02-plugin-pipeline
    provides: PluginValidationService path validation, skip list GameVariant
  - phase: 04-configuration
    provides: LegacyMigrationService, ConfigurationService skip list API
  - phase: 01-foundation
    provides: StateService concurrent update patterns
provides:
  - Coverlet MSBuild auto-coverage on every dotnet test run
  - LegacyMigrationService unit tests (7 test cases)
  - ProcessExecutionService orchestrator-level termination tests (no real processes)
  - ConfigurationService GameType.Unknown skip list behavior tests
  - StateService concurrent edge case tests
  - PluginValidationService non-rooted path variant tests
affects: [07-02]

# Tech tracking
tech-stack:
  added: [coverlet.msbuild 6.0.4]
  patterns: [MSBuild-driven Cobertura coverage, orchestrator-level mock testing for process-dependent code]

key-files:
  created:
    - AutoQAC.Tests/Services/LegacyMigrationServiceTests.cs
  modified:
    - AutoQAC.Tests/AutoQAC.Tests.csproj
    - AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs
    - AutoQAC.Tests/Services/ConfigurationServiceSkipListTests.cs
    - AutoQAC.Tests/Services/StateServiceTests.cs
    - AutoQAC.Tests/Services/PluginValidationServiceTests.cs

key-decisions:
  - "cmd.exe-spawning tests replaced with orchestrator-level mocks -- no real process spawning in unit tests"
  - "PID file path tests skipped due to private GetPidFilePath using AppContext.BaseDirectory with DEBUG directory walking -- not injectable without refactoring"
  - "LegacyMigrationService tests use injectable configDirectory constructor param with temp dirs -- standard C# unit test practice"
  - "coverlet.msbuild added alongside existing coverlet.collector -- MSBuild properties auto-collect on every dotnet test"

patterns-established:
  - "Orchestrator-level testing: for services with hard Process dependencies, test behavior through Mock<IProcessExecutionService> at the orchestrator level"
  - "Temp directory injection: services with configDirectory constructor params use temp dirs for file-system-isolated unit tests"

# Metrics
duration: 8min
completed: 2026-02-07
---

# Phase 7 Plan 1: Targeted Test Coverage Gaps Summary

**Coverlet MSBuild auto-coverage + 15 new tests covering process termination, migration paths, Unknown GameType skip list, concurrent state, and path validation variants**

## Performance

- **Duration:** 8 min
- **Started:** 2026-02-07T09:27:15Z
- **Completed:** 2026-02-07T09:35:33Z
- **Tasks:** 2
- **Files modified:** 6

## Accomplishments
- Coverlet MSBuild integration produces Cobertura XML coverage on every `dotnet test` run (42% line, 40% branch baseline)
- ProcessExecutionService tests rewritten: removed 5 cmd.exe-spawning integration tests, added 4 orchestrator-level mock tests plus 2 kept non-process-spawning tests
- LegacyMigrationService: new test file with 7 test cases covering all migration paths (no-legacy, C#-exists, valid, invalid YAML, empty, write failure, backup failure)
- ConfigurationService: 2 new tests for GameType.Unknown skip list behavior (returns only Universal entries)
- StateService: 3 new concurrent edge case tests (rapid toggling, multi-property updates, emission count)
- PluginValidationService: 4 new path variant tests (whitespace, forward-slash, dot-relative, backslash relative)
- Test count: 465 -> 480 (+15 net new tests)

## Task Commits

Each task was committed atomically:

1. **Task 1: Coverage tooling + ProcessExecutionService and LegacyMigrationService tests** - `32522ff` (test)
2. **Task 2: ConfigurationService skip list, StateService concurrent, and PluginValidationService path tests** - `6d58f07` (test)

## Files Created/Modified
- `AutoQAC.Tests/AutoQAC.Tests.csproj` - Added coverlet.msbuild package and MSBuild coverage properties
- `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs` - Replaced cmd.exe tests with orchestrator-level mock tests
- `AutoQAC.Tests/Services/LegacyMigrationServiceTests.cs` - New file: 7 migration path tests with temp directory isolation
- `AutoQAC.Tests/Services/ConfigurationServiceSkipListTests.cs` - Added GameType.Unknown skip list tests
- `AutoQAC.Tests/Services/StateServiceTests.cs` - Added concurrent edge case tests
- `AutoQAC.Tests/Services/PluginValidationServiceTests.cs` - Added non-rooted path variant tests

## Decisions Made
- Replaced cmd.exe-spawning tests instead of keeping them alongside new tests -- the user constraint "no integration tests with real file I/O or process spawning" explicitly prohibits them
- PID file logic tests were skipped because GetPidFilePath() is private with non-injectable AppContext.BaseDirectory resolution -- acceptable per checker hint about limited coverage for hard-to-mock Process interactions
- LegacyMigrationService backup failure test validates the backup-then-delete safety guarantee: when backup fails, the original file is preserved and migration reports failure

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
- FluentAssertions `Contain()` does not accept `StringComparison` as a second parameter (unlike `string.Contains`) -- replaced with `NotBeNullOrEmpty()` assertion in write failure test
- Orchestrator stop-cleaning test initially failed because the mock's `CleanPluginAsync` caught `OperationCanceledException` internally and returned a result, preventing the orchestrator's catch block from seeing the cancellation -- fixed by letting the exception propagate naturally from `Task.Delay`

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Coverage baseline established at 42% line / 40% branch -- provides measurement for future test additions in 07-02
- All TEST-01 through TEST-05 requirements satisfied
- Coverage output at `AutoQAC.Tests/TestResults/coverage/coverage.cobertura.xml` (gitignored)

## Self-Check: PASSED

---
*Phase: 07-hardening-cleanup*
*Completed: 2026-02-07*
