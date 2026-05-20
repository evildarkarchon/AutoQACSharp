---
id: T02
parent: S12
milestone: M001
provides:
  - Safe cleaning and preview unexpected-failure dialog/status copy
  - Safe xEdit, MO2, and load-order pre-clean validation identifiers
  - Regression coverage for command-boundary and validation disclosure limits
requires: []
affects: []
key_files: []
key_decisions: []
patterns_established: []
observability_surfaces: []
drill_down_paths: []
duration: 7 min
verification_result: passed
completed_at: 2026-05-01
blocker_discovered: false
---
# T02: 11-user-facing-diagnostics-boundaries 02

**# Phase 11 Plan 02: Main Cleaning Command Diagnostics Boundary Summary**

## What Happened

# Phase 11 Plan 02: Main Cleaning Command Diagnostics Boundary Summary

**Cleaning/preview command failures and pre-clean path validation now use safe latest-log copy and sanitized basename identifiers instead of raw exception, stack, command, or local path details.**

## Performance

- **Duration:** 7 min
- **Started:** 2026-05-01T04:26:30Z
- **Completed:** 2026-05-01T04:33:08Z
- **Tasks:** 3 completed
- **Files modified:** 2

## Accomplishments

- Added failing command-boundary tests proving unexpected cleaning and preview exceptions with path/command/stack sentinels do not cross into dialog/status text.
- Replaced raw `ex.Message`, `ex.StackTrace`, `Error:`, and `Stack Trace:` UI copy in cleaning/preview catches with `DiagnosticTextFormatter.OperationFailed(...)` and `LatestLogDetails`.
- Added and passed validation-row tests for xEdit, MO2, non-Mutagen load-order files, Mutagen false-positive prevention, and unsafe basename fallback behavior.
- Updated pre-clean validation rows to show safe setting identifiers such as `xEdit Path (SSEEdit.exe)`, `MO2 Path (ModOrganizer.exe)`, and `Load Order File (plugins.txt)` with direct fix guidance.

## Task Commits

Each task was committed atomically, with additional RED/GREEN commits where the TDD gate required them:

1. **Task 1: RED — prove cleaning and preview failures hide unsafe exception details** - `d72253d` (test)
2. **Task 2: GREEN — replace raw exception dialog/status copy** - `431b218` (feat)
3. **Task 3: Safe pre-clean validation identifiers for configured paths** - `2377a42` (test RED), `d3364d1` (feat GREEN)

**Plan metadata:** `e1cee8b` (docs)

_Note: This TDD plan produced RED and GREEN commits for command failure copy and validation identifier behavior._

## Files Created/Modified

- `AutoQAC.Tests/ViewModels/ErrorDialogTests.cs` - Adds disclosure-boundary tests for unexpected cleaning/preview errors and xEdit/MO2/load-order validation rows.
- `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` - Uses shared safe diagnostic formatter copy for cleaning/preview failures and pre-clean path validation identifiers.

## Decisions Made

- Unexpected cleaning and preview exceptions are still logged through `ILoggingService`, but every user-facing dialog/status value is now deterministic safe copy from `DiagnosticTextFormatter`.
- Invalid operation command-boundary failures now avoid displaying raw exception text in validation rows, preserving SEC-01 even when validation exceptions carry path or command details.
- Simple missing-path validation intentionally omits latest-log guidance; rows identify the setting and safe basename plus the exact corrective action.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- RED command-boundary tests failed because `StatusText` and dialog details used raw exception text/stack content, as expected for the TDD gate.
- RED validation tests failed because xEdit, MO2, and load-order rows displayed full configured paths, as expected before the GREEN implementation.

## TDD Gate Compliance

- RED commit present: `d72253d` (`test(11-02): add failing cleaning diagnostics tests`)
- GREEN commit present after RED: `431b218` (`feat(11-02): use safe command failure diagnostics`)
- Additional RED commit present: `2377a42` (`test(11-02): add failing validation disclosure tests`)
- Additional GREEN commit present after validation RED: `d3364d1` (`feat(11-02): sanitize pre-clean validation paths`)
- REFACTOR commit: not needed; no behavior-neutral cleanup was made after GREEN.

## Verification

- PASS: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~ErrorDialogTests --nologo`
- PASS: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ErrorDialogTests|FullyQualifiedName~CleaningCommandsViewModel" --nologo`

## Known Stubs

None. Null/captured-callback matches found during the stub scan are existing test setup values, not UI stubs or placeholder data flows.

## Threat Flags

None - no new network endpoints, auth paths, file access patterns, or schema trust boundaries were introduced.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Ready for Plan 11-03 to continue applying the shared diagnostics formatter to the next user-facing boundary without changing cleaning workflow behavior.

## Self-Check: PASSED

- FOUND: `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`
- FOUND: `AutoQAC.Tests/ViewModels/ErrorDialogTests.cs`
- FOUND: `.planning/phases/11-user-facing-diagnostics-boundaries/11-02-SUMMARY.md`
- FOUND: commit `d72253d`
- FOUND: commit `431b218`
- FOUND: commit `2377a42`
- FOUND: commit `d3364d1`

---
*Phase: 11-user-facing-diagnostics-boundaries*
*Completed: 2026-05-01*
