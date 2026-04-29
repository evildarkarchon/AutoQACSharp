---
phase: 06-command-launch-escaping
plan: 02
subsystem: process-launch
tags: [dotnet, processstartinfo, argumentlist, utf8-json, integration-tests]

# Dependency graph
requires:
  - phase: 06-command-launch-escaping
    provides: command-builder ArgumentList contracts from Plan 06-01
  - phase: 05-process-stop-pid-safety
    provides: reusable helper process and process lifecycle integration tests
provides:
  - ProcessExecutionService clone preserving ArgumentList through real Process.Start
  - argv-echo helper mode emitting UTF-8 JSON argv evidence
  - Integration coverage for quotes, Unicode, shell-sensitive characters, combined worst-case argv, and legacy Arguments fallback
affects: [process-execution, command-launch-escaping, integration-tests]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - ArgumentList takes precedence over Arguments when cloning ProcessStartInfo
    - Helper process stdout uses UTF-8 JSON for exact argv assertions

key-files:
  created: []
  modified:
    - AutoQAC/Services/Process/ProcessExecutionService.cs
    - AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs
    - AutoQAC.Tests/TestProcessHelper/Program.cs

key-decisions:
  - "ProcessExecutionService now clones ArgumentList entries when present and only falls back to Arguments for legacy callers with an empty ArgumentList."
  - "Process-start debug logging reports ArgumentList entry count instead of relying on or disclosing a full argument string."

patterns-established:
  - "Real process-boundary tests capture redirected stdout through one reader started in onProcessStarted."
  - "Helper argv evidence is serialized as UTF-8 JSON and parsed by tests rather than compared as delimiter-sensitive text."

requirements-completed: [SAF-03, TEST-02]

# Metrics
duration: 5 min
completed: 2026-04-29
---

# Phase 06 Plan 02: Process Boundary ArgumentList Preservation Summary

**ProcessExecutionService now preserves parsed ArgumentList argv through its internal clone and verifies exact helper-process receipt for quotes, Unicode, shell punctuation, and legacy Arguments fallback.**

## Performance

- **Duration:** 5 min
- **Started:** 2026-04-29T04:48:40Z
- **Completed:** 2026-04-29T04:53:47Z
- **Tasks:** 2/2 complete
- **Files modified:** 3

## Accomplishments

- Added `argv-echo` to the existing `AutoQAC.TestProcessHelper`, emitting `args.Skip(1)` as UTF-8 JSON for delimiter-safe argv proof.
- Added integration tests proving exact parsed argv receipt for embedded quotes, accented/CJK/emoji Unicode, shell-sensitive punctuation, and a combined worst-case plugin-style value.
- Updated `ProcessExecutionService` to clone `ArgumentList` entries exactly when present while preserving legacy `Arguments` behavior when `ArgumentList` is empty.

## Task Commits

Each task was committed atomically:

1. **Task 1: Add argv-echo helper mode and failing preservation tests** - `54f3f52` (test)
2. **Task 2: Preserve ArgumentList in ProcessExecutionService clone** - `51304b4` (feat)

**Plan metadata:** pending final docs commit

_Note: This was a task-level TDD flow: RED test/helper commit followed by GREEN implementation commit._

## Files Created/Modified

- `AutoQAC.Tests/TestProcessHelper/Program.cs` - Adds `argv-echo` mode with UTF-8 JSON stdout.
- `AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs` - Adds real helper-process assertions for ArgumentList preservation and legacy Arguments fallback.
- `AutoQAC/Services/Process/ProcessExecutionService.cs` - Clones `ArgumentList` entries through the launch boundary and summarizes ArgumentList starts without full command-line disclosure.

## Decisions Made

- `ArgumentList` entries are authoritative when present; `Arguments` is copied only for legacy callers with an empty `ArgumentList`, matching Microsoft guidance that the two APIs should not be mixed.
- Debug process-start logging now reports an ArgumentList entry count for parsed-argv launches, avoiding stale reliance on `startInfo.Arguments` and avoiding broader full-command disclosure.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- The RED verification failed as expected before Task 2: the helper exited with code 2 because the process-service clone dropped `ArgumentList`, so `argv-echo` never reached the helper.

## Known Stubs

None. Stub scan produced only nullable/default parameter false positives in existing process/test code; no placeholder UI/data stubs were added.

## User Setup Required

None - no external service configuration required.

## Threat Flags

None - changes stay within the planned local process-start and test-helper stdout trust boundaries.

## TDD Gate Compliance

- RED gate: `54f3f52` added helper-process argv preservation tests that failed before the service clone copied `ArgumentList`.
- GREEN gate: `51304b4` implemented `ArgumentList` preservation and made the targeted tests pass.
- REFACTOR gate: Not needed; no behavior-preserving cleanup commit was required after GREEN.

## Verification

- `dotnet test AutoQACSharp.slnx --filter FullyQualifiedName~ProcessExecutionIntegrationTests` — RED expected failure after Task 1, then PASS after Task 2 (7 AutoQAC process integration tests; QueryPlugins project had no matching tests).
- `dotnet test AutoQACSharp.slnx` — PASS (59 QueryPlugins.Tests, 671 AutoQAC.Tests).

## Self-Check: PASSED

- Found summary file: `.planning/phases/06-command-launch-escaping/06-02-SUMMARY.md`.
- Found task commits: `54f3f52`, `51304b4`.
- Found modified files: `AutoQAC/Services/Process/ProcessExecutionService.cs`, `AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs`, `AutoQAC.Tests/TestProcessHelper/Program.cs`.

## Next Phase Readiness

Plan 06-02 is ready for Plan 06-03 to integrate safe command-build failure messaging and final Phase 6 verification.

---
*Phase: 06-command-launch-escaping*
*Completed: 2026-04-29*
