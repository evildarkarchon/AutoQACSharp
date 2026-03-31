---
phase: 03-integration-log-first-parsing
plan: 02
subsystem: cleaning
tags: [log-parsing, offset-based, orchestrator, xedit, cleaning-results]

# Dependency graph
requires:
  - phase: 03-integration-log-first-parsing
    plan: 01
    provides: "CleaningStatus.AlreadyClean enum, CleaningService returns null Statistics"
  - phase: 01-log-file-foundation
    provides: "XEditLogFileService with offset-based CaptureOffset + ReadLogContentAsync API"
  - phase: 02-process-cleanup
    provides: "ProcessExecutionService without stdout redirect"
provides:
  - "Orchestrator reads xEdit results from log files using offset-based per-plugin isolation"
  - "Force-kill guard prevents invalid log reads when process was terminated"
  - "Nothing-to-clean detection sets AlreadyClean when completion line present but zero stats"
  - "Exception log content surfaces through LogParseWarning field with Failed status"
  - "Legacy ReadLogFileAsync calls fully removed from orchestrator and all test mocks"
affects: [phase-04-cleanup]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Per-plugin offset capture inside retry do-while loop (prevents double-counting on retries)"
    - "finalStatus variable pattern: result.Status overridden by log analysis before PluginCleaningResult creation"
    - "Guard pattern: _isStopRequested || Skipped status skips log reading entirely"

key-files:
  created: []
  modified:
    - AutoQAC/Services/Cleaning/CleaningOrchestrator.cs
    - AutoQAC.Tests/Services/CleaningOrchestratorTests.cs
    - AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs

key-decisions:
  - "Final-pass-only stats for multi-pass retries: offset capture inside do-while means only last attempt's log content is read"
  - "Broadened log reading condition: reads logs for any non-killed, non-skipped result (not just Success+Cleaned)"

patterns-established:
  - "Offset-based log isolation: CaptureOffset before xEdit launch, ReadLogContentAsync after exit"
  - "Exception log overrides: ExceptionContent sets status to Failed regardless of process exit status"
  - "Guard-before-read: check _isStopRequested and result.Status before attempting log I/O"

requirements-completed: [PAR-01, PAR-02, PAR-03, ORC-01, ORC-02]

# Metrics
duration: 5min
completed: 2026-03-31
---

# Phase 03 Plan 02: Orchestrator Log Integration Summary

**Rewired CleaningOrchestrator to read xEdit results from log files using offset-based per-plugin isolation, replacing broken stdout-based pipeline**

## Performance

- **Duration:** 5 min
- **Started:** 2026-03-31T06:55:33Z
- **Completed:** 2026-03-31T07:00:37Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments
- Replaced legacy timestamp-based `ReadLogFileAsync` with offset-based `CaptureOffset` + `ReadLogContentAsync` in the orchestrator's per-plugin cleaning loop
- Added force-kill guard, nothing-to-clean detection (AlreadyClean), and exception log surfacing (Failed + LogParseWarning)
- Added 4 new orchestrator tests covering log parsing, AlreadyClean, exception surfacing, and skip-on-cancel scenarios
- Fixed ProcessExecutionServiceTests to use the new offset-based mock API (deviation Rule 3)
- All 625 solution tests pass with 0 failures

## Task Commits

Each task was committed atomically:

1. **Task 1: Rewire CleaningOrchestrator with offset-based log reading** - `61d1e90` (feat)
2. **Task 2: Update CleaningOrchestratorTests for offset-based log reading** - `fb41807` (test)

## Files Created/Modified
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` - Replaced lines 338-339 (timestamp) + 400-423 (legacy log enrichment) with offset-based capture/read cycle, force-kill guard, AlreadyClean detection, exception surfacing, finalStatus variable
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs` - Updated default mock setup to offset-based API, added 4 new tests (ParseStats, AlreadyClean, ExceptionLog, SkipLogRead)
- `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs` - Updated two legacy ReadLogFileAsync mock setups to offset-based API in CreateOrchestrator helper and StopCleaning test

## Decisions Made
- Final-pass-only stats for multi-pass retries: by capturing offsets inside the do-while loop, each retry gets fresh offsets and only the final attempt's log content is read/parsed. This prevents double-counting items across retries.
- Broadened log reading condition from `{ Success: true, Status: Cleaned }` to `!_isStopRequested && Status != Skipped`, allowing log reading even for failed results (which may contain useful exception information).
- xEditDir derived once before the do-while loop (does not change between retries), while offsets are captured inside the loop (must be fresh per attempt).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Updated ProcessExecutionServiceTests legacy mock to offset-based API**
- **Found during:** Task 2 (test execution)
- **Issue:** `ProcessExecutionServiceTests.CreateOrchestrator()` and the `Orchestrator_StopCleaning_CancelsCts` test both mocked the legacy `ReadLogFileAsync` method. After Task 1 changed the orchestrator to call `ReadLogContentAsync`, these returned null (NSubstitute default), causing NullReferenceException at line 419.
- **Fix:** Replaced both legacy mock setups with `GetLogFilePath`, `GetExceptionLogFilePath`, `CaptureOffset`, and `ReadLogContentAsync` mocks returning empty `LogReadResult`.
- **Files modified:** `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`
- **Verification:** All 625 tests pass
- **Committed in:** `fb41807` (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Necessary fix -- the ProcessExecutionServiceTests were broken by the orchestrator change. No scope creep.

## Issues Encountered
None beyond the auto-fixed deviation above.

## User Setup Required
None - no external service configuration required.

## Known Stubs
None - all offset-based log reading is fully wired end-to-end. The orchestrator now exclusively uses the game-aware, offset-based API for all log reading.

## Next Phase Readiness
- Phase 03 is now complete: all plans executed, all requirements satisfied
- Phase 04 (cleanup) can proceed to remove legacy `ReadLogFileAsync` method and `[Obsolete]` markers from `IXEditLogFileService`
- Phase 04 can also remove the unused `IXEditOutputParser` parameter from `CleaningService`
- The only remaining `ReadLogFileAsync` references are in `ProcessExecutionServiceTests` (now using the new API for the mock but the old method still exists in the interface with `[Obsolete]`)

## Self-Check: PASSED

- AutoQAC/Services/Cleaning/CleaningOrchestrator.cs: FOUND
- AutoQAC.Tests/Services/CleaningOrchestratorTests.cs: FOUND
- AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs: FOUND
- Commit 61d1e90 (Task 1): FOUND
- Commit fb41807 (Task 2): FOUND

---
*Phase: 03-integration-log-first-parsing*
*Completed: 2026-03-31*
