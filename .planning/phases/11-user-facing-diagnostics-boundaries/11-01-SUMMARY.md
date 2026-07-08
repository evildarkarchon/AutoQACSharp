---
phase: 11-user-facing-diagnostics-boundaries
plan: 01
subsystem: diagnostics
tags: [csharp, dotnet, diagnostics, security, tdd]

# Dependency graph
requires:
  - phase: 10-configuration-persistence-hardening
    provides: safe typed configuration persistence failure patterns
provides:
  - Shared DiagnosticTextFormatter safe text boundary for Phase 11 consumers
  - Negative-disclosure sentinel tests for paths, commands, exceptions, and stack-like text
affects: [ui-diagnostics, cleaning-results, exports, process-logs, startup-diagnostics]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Public static diagnostics formatter in AutoQAC.Models.Diagnostics
    - Case-insensitive unsafe detail classification before UI/export text
    - TDD RED/GREEN gate commits for diagnostics primitives

key-files:
  created:
    - AutoQAC/Models/Diagnostics/DiagnosticTextFormatter.cs
    - AutoQAC.Tests/Models/DiagnosticTextFormatterTests.cs
  modified:
    - AutoQAC.Tests/Models/DiagnosticTextFormatterTests.cs

key-decisions:
  - "DiagnosticTextFormatter lives under AutoQAC.Models.Diagnostics so models, services, ViewModels, and startup code can share safe copy without a service-layer dependency."
  - "Unsafe failure summaries fall back on any detected paths, command flags, exception names, stack markers, executable command markers, or control whitespace."

patterns-established:
  - "Safe basename identifiers: Path.GetFileName plus display sanitization preserves filenames while dropping path directories and command-like characters."
  - "Shared latest-log copy constants keep dialogs, rows, and reports aligned with the Phase 11 UI contract."

requirements-completed: [SEC-01, SEC-02]

# Metrics
duration: 3 min
completed: 2026-05-01
---

# Phase 11 Plan 01: Shared Safe Diagnostics Formatter Summary

**Shared safe diagnostics formatter with sentinel-tested copy primitives for path, command, exception, plugin, and latest-log boundaries.**

## Performance

- **Duration:** 3 min
- **Started:** 2026-05-01T04:23:00Z
- **Completed:** 2026-05-01T04:25:39Z
- **Tasks:** 2 completed
- **Files modified:** 2

## Accomplishments

- Added focused RED tests covering exact UI-SPEC copy, sanitized basenames, plugin failure rows, xEdit error rows, and unsafe failure-summary sentinels.
- Implemented `DiagnosticTextFormatter` with public constants and helpers for safe operation, file, folder, plugin, and failure-summary text.
- Verified focused formatter tests pass and the full solution builds without warnings or errors.

## Task Commits

Each task was committed atomically:

1. **Task 1: RED — lock shared diagnostic text behavior** - `0f691bd` (test)
2. **Task 2: GREEN — implement shared diagnostic text formatter** - `2dffa1f` (feat)

**Plan metadata:** `047f760` (docs)

_Note: This TDD plan produced the required RED and GREEN commits._

## Files Created/Modified

- `AutoQAC.Tests/Models/DiagnosticTextFormatterTests.cs` - Locks expected safe diagnostic copy and negative-disclosure sentinel behavior.
- `AutoQAC/Models/Diagnostics/DiagnosticTextFormatter.cs` - Provides shared safe formatting helpers and constants for later Phase 11 consumers.

## Decisions Made

- Diagnostic text primitives were implemented as a public static model-layer helper to avoid introducing a DI/service dependency into models and report generation.
- Unsafe failure summary detection is intentionally conservative and case-insensitive: if a candidate includes path, command, exception, executable, stack-frame, or control-whitespace markers, callers get the safe fallback.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Replaced unsupported FluentAssertions string-comparison overload**
- **Found during:** Task 2 (GREEN — implement shared diagnostic text formatter)
- **Issue:** The RED tests used `NotContain(string, StringComparison)`, which is unavailable in the installed FluentAssertions version and blocked the GREEN compile.
- **Fix:** Switched the assertion to `IndexOf(..., StringComparison.OrdinalIgnoreCase).Should().Be(-1)` while preserving the same case-insensitive negative-disclosure behavior.
- **Files modified:** `AutoQAC.Tests/Models/DiagnosticTextFormatterTests.cs`
- **Verification:** `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~DiagnosticTextFormatterTests --nologo` passed.
- **Committed in:** `2dffa1f` (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** The auto-fix preserved the intended test behavior and removed a test-framework compatibility blocker. No product scope changed.

## Issues Encountered

- Focused RED test command failed before Task 2 because `AutoQAC.Models.Diagnostics.DiagnosticTextFormatter` did not exist, as required by the TDD gate.

## TDD Gate Compliance

- RED commit present: `0f691bd` (`test(11-01): add failing diagnostic formatter contract tests`)
- GREEN commit present after RED: `2dffa1f` (`feat(11-01): implement safe diagnostic text formatter`)
- REFACTOR commit: not needed; no behavior-neutral cleanup was made after GREEN.

## Verification

- PASS: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~DiagnosticTextFormatterTests --nologo`
- PASS: `dotnet build AutoQACSharp.slnx --nologo`

## Known Stubs

None.

## Threat Flags

None - no new network endpoints, auth paths, file access patterns, or schema trust boundaries were introduced.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Ready for Plan 11-02 to consume `DiagnosticTextFormatter` in cleaning/preview dialogs, status text, and pre-clean validation identifiers.

## Self-Check: PASSED

- FOUND: `AutoQAC/Models/Diagnostics/DiagnosticTextFormatter.cs`
- FOUND: `AutoQAC.Tests/Models/DiagnosticTextFormatterTests.cs`
- FOUND: commit `0f691bd`
- FOUND: commit `2dffa1f`

---
*Phase: 11-user-facing-diagnostics-boundaries*
*Completed: 2026-05-01*
