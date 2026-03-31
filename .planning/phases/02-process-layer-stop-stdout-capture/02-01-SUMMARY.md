---
phase: 02-process-layer-stop-stdout-capture
plan: 01
subsystem: process
tags: [process-execution, stdout, stderr, xedit, cleanup]

# Dependency graph
requires:
  - phase: 01-xedit-log-file-service
    provides: "Game-aware log file service for reading xEdit output from log files"
provides:
  - "ProcessExecutionService without stdout/stderr redirection"
  - "Clean process lifecycle (PID tracking, timeouts, termination) preserved"
affects: [03-integration-wire-log-parsing, 04-cleanup-dead-code]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Empty collection expression [] for dead output fields pending interface cleanup"

key-files:
  created: []
  modified:
    - "AutoQAC/Services/Process/ProcessExecutionService.cs"
    - "AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs"

key-decisions:
  - "Kept ErrorLines = [ex.Message] on startup failure path -- useful diagnostic that costs nothing"
  - "Preserved IProgress<string> parameter in signature for Phase 4 interface cleanup"

patterns-established:
  - "Minimal-scope removal: strip internals, keep interface fields empty for later cleanup phase"

requirements-completed: [PRC-01, PRC-02]

# Metrics
duration: 2min
completed: 2026-03-31
---

# Phase 2 Plan 1: Remove Stdout/Stderr Redirect Plumbing Summary

**Removed dead stdout/stderr capture from ProcessExecutionService -- xEdit output now exclusively read from log files**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-31T06:24:49Z
- **Completed:** 2026-03-31T06:26:55Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments
- Removed all stdout/stderr redirection plumbing (RedirectStandardOutput, RedirectStandardError, ConcurrentQueue buffers, event handlers, BeginOutputReadLine/BeginErrorReadLine)
- ProcessResult.OutputLines and ErrorLines now return empty lists (fields retained for Phase 4 interface cleanup)
- All process lifecycle behavior preserved: PID tracking, timeout handling, termination, MO2 wrapping, slot management
- Deleted the dead `ExecuteAsync_ShouldCaptureConcurrentStdoutAndStderrWithoutLoss` test that validated removed behavior
- All 680 tests pass (59 QueryPlugins + 621 AutoQAC)

## Task Commits

Each task was committed atomically:

1. **Task 1: Remove stdout/stderr redirect plumbing from ProcessExecutionService.ExecuteAsync** - `67f6948` (feat)
2. **Task 2: Delete dead stdout capture test and verify all tests pass** - `9a06281` (test)

## Files Created/Modified
- `AutoQAC/Services/Process/ProcessExecutionService.cs` - Removed redirect flags, capture queues, event handlers, begin-read calls; returns empty OutputLines/ErrorLines
- `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs` - Deleted dead stdout/stderr capture test method

## Decisions Made
- Kept `ErrorLines = [ex.Message]` on the startup failure path (line 70) -- this is a useful diagnostic that costs nothing, and the field is already in the record. Only runtime capture queues were removed.
- Preserved `IProgress<string>? outputProgress` parameter in method signature -- not called, but interface stays intact per D-01 for Phase 4 cleanup.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Known Stubs
None - OutputLines/ErrorLines returning empty lists is intentional dead output (Phase 4 will clean up the interface fields).

## Next Phase Readiness
- ProcessExecutionService is now clean of dead stdout plumbing
- Phase 3 (integration) can wire log file parsing into the cleaning pipeline, reading xEdit output from log files after process exit instead of from the now-empty stdout streams
- MO2 wrapping continues to work identically (UseShellExecute=false, PID tracking, WaitForExitAsync all preserved)

## Self-Check: PASSED

- All files exist: ProcessExecutionService.cs, ProcessExecutionServiceTests.cs, 02-01-SUMMARY.md
- All commits verified: 67f6948, 9a06281
- Solution builds: 0 errors
- All tests pass: 680/680

---
*Phase: 02-process-layer-stop-stdout-capture*
*Completed: 2026-03-31*
