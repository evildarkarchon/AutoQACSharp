---
phase: 06-command-launch-escaping
plan: 03
subsystem: process-launch
tags: [dotnet, cleaning-service, failure-flow, command-launch-escaping, testing]

# Dependency graph
requires:
  - phase: 06-command-launch-escaping
    provides: command-builder ArgumentList contracts from Plan 06-01
  - phase: 06-command-launch-escaping
    provides: process-boundary ArgumentList preservation from Plan 06-02
provides:
  - Safe command-build failure messages for direct xEdit and MO2 mode
  - Regression tests proving command-build failures do not start processes
  - Mocked launch-start failure coverage for existing concise failed flow
affects: [cleaning-service, command-launch-escaping, user-facing-diagnostics]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Single AppState snapshot for command-build mode, game type, and timeout consistency
    - Concise CleaningResult failure messages with structured logs for technical details

key-files:
  created:
    - .planning/phases/06-command-launch-escaping/06-03-SUMMARY.md
  modified:
    - AutoQAC/Services/Cleaning/CleaningService.cs
    - AutoQAC.Tests/Services/CleaningServiceTests.cs

key-decisions:
  - "Command-build failures now name plugin and launch mode while stating no process was started and pointing users to logs instead of exposing configured paths or command lines."
  - "Mocked launch-start failures remain in the existing `xEdit exited with code -1` failed flow without adding a new session policy for Phase 6."

patterns-established:
  - "Failure-flow tests assert against concrete configured xEdit/MO2 path values to prevent accidental user-facing disclosure."
  - "CleaningService snapshots AppState once before command building so launch-mode failure text matches the command-build mode."

requirements-completed: [SAF-03, TEST-02]

# Metrics
duration: 2 min
completed: 2026-04-29
---

# Phase 06 Plan 03: Safe Command-Build Failure Flow Summary

**CleaningService now fails direct and MO2 command-build errors before process start with concise plugin/mode messages, structured logs, and regression coverage for non-disclosure.**

## Performance

- **Duration:** 2 min
- **Started:** 2026-04-29T04:56:09Z
- **Completed:** 2026-04-29T04:58:03Z
- **Tasks:** 2/2 complete
- **Files modified:** 3

## Accomplishments

- Added TDD coverage for direct xEdit and MO2 command-build failures, including concrete negative assertions for configured executable paths, nested payload text, `run `, and `-a`.
- Added a mocked launch-start failure regression that keeps `ProcessResult { ExitCode = -1 }` on the existing failed flow with `xEdit exited with code -1` and no path/command disclosure.
- Updated `CleaningService` to snapshot `AppState` once, derive launch mode from the same snapshot used for command building, log a structured warning, and return a concise no-process-started failure message.
- Completed Phase 6 targeted regression verification and full solution verification.

## Task Commits

Each task was committed atomically:

1. **Task 1: Lock command-build failure behavior in CleaningService tests** - `fc66b18` (test)
2. **Task 2: Implement concise command-build failure messaging and final verification** - `86fda99` (feat)

**Plan metadata:** pending final docs commit

_Note: This plan followed TDD gates: RED test commit followed by GREEN implementation commit._

## Files Created/Modified

- `AutoQAC.Tests/Services/CleaningServiceTests.cs` - Adds direct and MO2 command-build failure tests plus mocked launch-start failure disclosure assertions.
- `AutoQAC/Services/Cleaning/CleaningService.cs` - Snapshots state once and maps null command builds to concise plugin/mode/no-process failure results with structured warning logs.
- `.planning/phases/06-command-launch-escaping/06-03-SUMMARY.md` - Documents completed work, verification, and phase readiness.

## Decisions Made

- Command-build failures stay in the existing `CleaningStatus.Failed` result flow and do not introduce new per-plugin or session policy.
- User-facing command-build failure text includes plugin filename, `direct xEdit` or `MO2`, `No process was started`, and `See logs for technical details`, but omits configured paths and full command-line fragments.
- Real xEdit `-autoload` split-argv compatibility remains the accepted Phase 6 assumption from Plan 06-01; no real xEdit/MO2 manual smoke test was available during automated execution.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- RED verification failed as expected after Task 1: direct and MO2 command-build failure tests received the old generic `Failed to build xEdit command.` message.

## Known Stubs

None.

## User Setup Required

None - no external service configuration required.

## Threat Flags

None - changes stay within the planned local command-build failure and process-launch failure result boundaries.

## TDD Gate Compliance

- RED gate: `fc66b18` added failing CleaningService failure-flow tests.
- GREEN gate: `86fda99` implemented safe failure messaging and made targeted/full-suite tests pass.
- REFACTOR gate: Not needed; no behavior-preserving cleanup commit was required after GREEN.

## Verification

- `dotnet test AutoQACSharp.slnx --filter FullyQualifiedName~CleaningServiceTests` — RED expected failure after Task 1, then covered by targeted Phase 6 pass after Task 2.
- `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~XEditCommandBuilderTests|FullyQualifiedName~ProcessExecutionIntegrationTests|FullyQualifiedName~CleaningServiceTests"` — PASS (32 AutoQAC targeted tests; QueryPlugins project had no matching tests).
- `dotnet test AutoQACSharp.slnx` — PASS (59 QueryPlugins.Tests, 673 AutoQAC.Tests).

## Self-Check: PASSED

- Found modified files: `AutoQAC/Services/Cleaning/CleaningService.cs`, `AutoQAC.Tests/Services/CleaningServiceTests.cs`.
- Found summary file: `.planning/phases/06-command-launch-escaping/06-03-SUMMARY.md`.
- Found task commits: `fc66b18`, `86fda99`.

## Next Phase Readiness

Phase 6 is complete and ready for `/gsd-verify-work` or Phase 7 planning. The only documented residual product risk is the accepted Plan 06-01 assumption that real xEdit accepts split parsed `-autoload` argv tokens.

---
*Phase: 06-command-launch-escaping*
*Completed: 2026-04-29*
