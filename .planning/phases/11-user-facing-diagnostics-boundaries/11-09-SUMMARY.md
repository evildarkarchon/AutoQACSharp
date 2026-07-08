---
phase: 11-user-facing-diagnostics-boundaries
plan: 09
subsystem: diagnostics-security
tags: [csharp, dotnet, reports, diagnostics, security, tdd]

requires:
  - phase: 11-user-facing-diagnostics-boundaries
    provides: Shared DiagnosticTextFormatter, report disclaimer, failed-row fallback, and Phase 11 sentinel guards from Plans 11-01, 11-04, and 11-06
provides:
  - Safe plugin display names in generated cleaning reports
  - Regression coverage for path-like, control-character, quote/backtick, separator, and command-flag plugin names
  - Failed-row fallback formatting based on sanitized plugin prefixes
affects: [cleaning-reports, plugin-results, diagnostics-boundaries, sec-01]

tech-stack:
  added: []
  patterns:
    - TDD RED/GREEN commits for report display-name boundary hardening
    - Report rows derive display prefixes from DiagnosticTextFormatter.SafePluginName while preserving raw PluginName model data
    - SafePluginName strips known command-flag suffixes only at user-facing display boundaries

key-files:
  created:
    - .planning/phases/11-user-facing-diagnostics-boundaries/11-09-SUMMARY.md
  modified:
    - AutoQAC/Models/CleaningSessionResult.cs
    - AutoQAC/Models/Diagnostics/DiagnosticTextFormatter.cs
    - AutoQAC.Tests/Models/CleaningSessionResultTests.cs
    - AutoQAC.Tests/Models/Phase11ReportBoundaryTests.cs

key-decisions:
  - "Generated reports use sanitized plugin basenames for cleaned, already-clean, skipped, and failed row prefixes; raw PluginName remains internal model data."
  - "SafePluginName removes known command-flag suffixes such as -QAC and -autoload when they appear as display-name suffixes before an extension."

patterns-established:
  - "Report display code should compute safePluginName once for failed rows, use it for fallback construction, duplicate-prefix comparison, and final prefix rendering."
  - "Phase 11 report tests should assert both positive safe basenames and negative absence of raw paths/control/command fragments."

requirements-completed: [SEC-01]

duration: 4 min
completed: 2026-05-01
---

# Phase 11 Plan 09: Report Plugin Display Boundary Summary

**Generated cleaning reports now render sanitized plugin basenames in every row section, including failed fallback summaries, without exposing raw path-like plugin names or command flags.**

## Performance

- **Duration:** 4 min
- **Started:** 2026-05-01T06:37:00Z
- **Completed:** 2026-05-01T06:41:28Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments

- Added RED regression tests for cleaned, already-clean, skipped, and failed report rows with unsafe plugin-name characters and path-like values.
- Added a Phase 11 report guard proving the shared report disclaimer remains present exactly once while unsafe plugin-name sentinels stay out of report text.
- Updated report generation to use `DiagnosticTextFormatter.SafePluginName(result.PluginName)` for every plugin display prefix.
- Tightened plugin display sanitization for known command-flag suffixes so `-QAC` and `-autoload` cannot survive in user-facing plugin basenames.

## Task Commits

Each task was committed atomically:

1. **Task 1: Prove report plugin-name prefixes are sanitized** - `1280401` (test)
2. **Task 2: Sanitize plugin display names across report rows** - `128f309` (fix)

**Plan metadata:** pending final docs commit

## Files Created/Modified

- `AutoQAC.Tests/Models/CleaningSessionResultTests.cs` - Adds unsafe plugin-name report regressions and negative assertions for paths, quotes, controls, separators, `-QAC`, and `-autoload`.
- `AutoQAC.Tests/Models/Phase11ReportBoundaryTests.cs` - Adds a phase-level unsafe plugin-name guard with disclaimer exact-once verification.
- `AutoQAC/Models/CleaningSessionResult.cs` - Uses sanitized plugin display names for cleaned, already-clean, skipped, and failed report rows.
- `AutoQAC/Models/Diagnostics/DiagnosticTextFormatter.cs` - Removes known command-flag suffixes from plugin display names before sanitizing user-facing basenames.

## Decisions Made

- Generated reports sanitize plugin display names at the report/export boundary rather than mutating `PluginCleaningResult.PluginName`, preserving raw model data for internal callers.
- Failed report rows build fallback text from the same sanitized prefix used in duplicate-prefix comparison and final rendering.
- `SafePluginName` strips only known command-flag suffixes (`-QAC`, `-autoload`) before the extension/end of string to avoid broadly removing useful dashes from normal plugin filenames.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical] Removed command-flag suffixes in SafePluginName**
- **Found during:** Task 2 (Sanitize plugin display names across report rows)
- **Issue:** Existing `SafePluginName` removed quotes, backticks, controls, separators, and invalid filename characters but left suffix-style `-QAC`/`-autoload` fragments in display names, while the plan acceptance criteria required those fragments to be absent from report output.
- **Fix:** Added a narrow `PluginCommandFlagPattern` that removes known command-flag suffixes only when they appear before a file extension or end of string.
- **Files modified:** `AutoQAC/Models/Diagnostics/DiagnosticTextFormatter.cs`
- **Verification:** `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningSessionResultTests|FullyQualifiedName~Phase11ReportBoundaryTests" --nologo` passed.
- **Committed in:** `128f309`

---

**Total deviations:** 1 auto-fixed (1 missing critical).
**Impact on plan:** The change was required to satisfy SEC-01/D-07 display-boundary requirements and remained limited to display-name sanitization.

## Issues Encountered

- RED verification failed as expected before implementation because reports still printed raw `result.PluginName` prefixes and because command-flag suffixes remained in safe plugin display names.

## Verification

- RED: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningSessionResultTests|FullyQualifiedName~Phase11ReportBoundaryTests" --nologo` failed before Task 2 with raw plugin paths/prefixes in generated report output.
- GREEN/final: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningSessionResultTests|FullyQualifiedName~Phase11ReportBoundaryTests" --nologo` passed, 29/29 tests.
- PASS: `AutoQAC/Models/CleaningSessionResult.cs` contains `DiagnosticTextFormatter.SafePluginName(result.PluginName)`.
- PASS: `AutoQAC/Models/CleaningSessionResult.cs` no longer contains `sb.AppendLine($"  {result.PluginName}`.
- PASS: `AutoQAC/Models/CleaningSessionResult.cs` no longer contains `var prefix = $"{result.PluginName}:"`.

## TDD Gate Compliance

- RED gate: `1280401` added failing report/display-name regression tests before production changes.
- GREEN gate: `128f309` implemented sanitized report display names and targeted tests passed.
- REFACTOR gate: not needed; no behavior-neutral cleanup was made after GREEN.

## Known Stubs

None. Stub-pattern scans found only existing null/empty collection patterns in model/test code; no UI-rendered placeholders or mock data were introduced.

## Auth Gates

None.

## Threat Flags

None - this plan modified an existing report/export trust boundary already covered by the plan threat model and introduced no new network endpoints, auth paths, file access patterns, or schema boundaries.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Verification gap #3 is closed for SEC-01 report/result display boundaries.
- Phase 11 gap-closure execution is complete and ready for phase verification/milestone wrap-up.

## Self-Check: PASSED

- FOUND: `AutoQAC/Models/CleaningSessionResult.cs`
- FOUND: `AutoQAC/Models/Diagnostics/DiagnosticTextFormatter.cs`
- FOUND: `AutoQAC.Tests/Models/CleaningSessionResultTests.cs`
- FOUND: `AutoQAC.Tests/Models/Phase11ReportBoundaryTests.cs`
- FOUND: `.planning/phases/11-user-facing-diagnostics-boundaries/11-09-SUMMARY.md`
- FOUND: commit `1280401`
- FOUND: commit `128f309`

---
*Phase: 11-user-facing-diagnostics-boundaries*
*Completed: 2026-05-01*
