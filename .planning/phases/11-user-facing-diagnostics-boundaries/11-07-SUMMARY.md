---
phase: 11-user-facing-diagnostics-boundaries
plan: 07
subsystem: security
tags: [diagnostics, migration, ui-boundary, tdd, sec-01]

requires:
  - phase: 11-user-facing-diagnostics-boundaries
    provides: DiagnosticTextFormatter safe failure boundary and Phase 11 diagnostic disclosure policy
provides:
  - Safe legacy migration warning copy with latest-log guidance
  - Defensive startup warning boundary before MainWindowViewModel.ShowMigrationWarning
  - Regression coverage rejecting path, command, exception, and stack sentinel disclosure
affects: [startup, legacy-migration, diagnostics, sec-01]

tech-stack:
  added: []
  patterns: [safe-warning-copy, DiagnosticTextFormatter.SafeFailureSummary, tdd-red-green]

key-files:
  created:
    - .planning/phases/11-user-facing-diagnostics-boundaries/11-07-SUMMARY.md
  modified:
    - AutoQAC/App.axaml.cs
    - AutoQAC/Services/Configuration/LegacyMigrationService.cs
    - AutoQAC.Tests/Services/LegacyMigrationServiceTests.cs
    - AutoQAC.Tests/Integration/DependencyInjectionTests.cs

key-decisions:
  - "Legacy migration warnings now use fixed category copy with latest-log guidance rather than interpolated exception messages."
  - "Startup migration warning display defensively applies DiagnosticTextFormatter.SafeFailureSummary before calling ShowMigrationWarning."

patterns-established:
  - "Migration warning tests assert positive actionable copy and shared negative-disclosure sentinels."
  - "UI warning boundaries sanitize service-provided summaries before ViewModel display."

requirements-completed: [SEC-01]

duration: 2m 7s
completed: 2026-05-01
---

# Phase 11 Plan 07: Legacy Migration Warning Boundary Summary

**Legacy migration warnings now use safe category copy with latest-log guidance plus a defensive startup sanitization boundary.**

## Performance

- **Duration:** 2m 7s
- **Started:** 2026-05-01T06:25:28Z
- **Completed:** 2026-05-01T06:27:34Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments

- Added RED regression tests proving legacy migration warnings reject path, command, exception, and stack-frame sentinel fragments.
- Replaced parse, write, backup, and delete migration warning messages with fixed safe latest-log guidance.
- Wrapped startup migration warning display with `DiagnosticTextFormatter.SafeFailureSummary` before calling `ShowMigrationWarning`.

## Task Commits

1. **Task 1: Prove legacy migration warnings exclude raw exception details** - `6469171` (test)
2. **Task 2: Replace migration warning UI copy with safe categories** - `a8ad466` (fix)

**Plan metadata:** pending final docs commit

## Files Created/Modified

- `AutoQAC/App.axaml.cs` - Sanitizes migration warning text before forwarding to `MainWindowViewModel.ShowMigrationWarning`.
- `AutoQAC/Services/Configuration/LegacyMigrationService.cs` - Uses safe fixed warning copy for parse, write, backup, and delete outcomes while preserving local logging.
- `AutoQAC.Tests/Services/LegacyMigrationServiceTests.cs` - Adds migration warning safety assertions and updates empty-file warning expectations to the safe parse category.
- `AutoQAC.Tests/Integration/DependencyInjectionTests.cs` - Guards startup source so direct `result.WarningMessage` forwarding does not regress.
- `.planning/phases/11-user-facing-diagnostics-boundaries/11-07-SUMMARY.md` - Records plan execution results.

## Decisions Made

- Legacy migration warnings now use fixed category copy with latest-log guidance rather than interpolated exception messages.
- Startup migration warning display defensively applies `DiagnosticTextFormatter.SafeFailureSummary` before calling `ShowMigrationWarning`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical] Aligned empty legacy file warnings with safe parse copy**
- **Found during:** Task 2 (Replace migration warning UI copy with safe categories)
- **Issue:** The existing empty-file warning was safe from raw exception details but lacked the required latest-log guidance for migration warning UI.
- **Fix:** Reused the same safe parse-failure copy for null/empty deserialization results and updated the existing unit test to assert the shared safety contract.
- **Files modified:** `AutoQAC/Services/Configuration/LegacyMigrationService.cs`, `AutoQAC.Tests/Services/LegacyMigrationServiceTests.cs`
- **Verification:** Targeted migration/DI test command passed.
- **Committed in:** `a8ad466`

---

**Total deviations:** 1 auto-fixed (1 missing critical)
**Impact on plan:** Required for consistent SEC-01 latest-log guidance across legacy migration warnings; no scope creep.

## Issues Encountered

- The source guard initially required `DiagnosticTextFormatter.SafeFailureSummary(result.WarningMessage` as a contiguous substring, so the production call was formatted to keep the first argument on the same line.

## User Setup Required

None - no external service configuration required.

## Known Stubs

None.

## Auth Gates

None.

## Threat Flags

None.

## Verification

- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~LegacyMigrationServiceTests|FullyQualifiedName~DependencyInjectionTests" --nologo` — passed, 13/13 tests.

## TDD Gate Compliance

- RED gate: `6469171` added failing regression tests before production changes.
- GREEN gate: `a8ad466` implemented the safe warning copy and boundary; targeted tests passed.

## Next Phase Readiness

- Verification gap #1 is closed for SEC-01.
- Plans 11-08 and 11-09 can proceed without depending on raw migration warning behavior.

## Self-Check: PASSED

- Confirmed all modified/source files and this summary exist.
- Confirmed task commits `6469171` and `a8ad466` exist in git history.

---
*Phase: 11-user-facing-diagnostics-boundaries*
*Completed: 2026-05-01*
