---
phase: 11-user-facing-diagnostics-boundaries
plan: 03
subsystem: diagnostics
tags: [csharp, dotnet, avalonia, diagnostics, security, tdd]

# Dependency graph
requires:
  - phase: 11-user-facing-diagnostics-boundaries
    provides: Shared DiagnosticTextFormatter safe file, folder, operation, and latest-log copy primitives
  - phase: 10-configuration-persistence-hardening
    provides: ConfigPersistenceFailure.SafeSummary typed persistence failure payloads
provides:
  - Safe configuration browse diagnostics for selected load-order and game data folder failures
  - Safe restore backup-session load failure status with latest-log guidance
  - Settings persistence write/read regression coverage preserving ConfigPersistenceFailure.SafeSummary
affects: [ui-diagnostics, settings-ui, restore-ui, configuration-browse, phase-11]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - ViewModel catch/status boundaries log exceptions but show safe latest-log copy
    - Browse-path UI maps raw paths to DiagnosticTextFormatter safe identifiers before display
    - Settings persistence banners preserve typed SafeSummary category text

key-files:
  created:
    - .planning/phases/11-user-facing-diagnostics-boundaries/11-03-SUMMARY.md
  modified:
    - AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs
    - AutoQAC/ViewModels/RestoreViewModel.cs
    - AutoQAC/ViewModels/SettingsViewModel.cs
    - AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs
    - AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs
    - AutoQAC.Tests/ViewModels/SettingsViewModelTests.cs

key-decisions:
  - "Configuration selected load-order technical failures now identify Load Order File (plugins.txt) and point to the latest AutoQAC log instead of showing selected paths or exception text."
  - "Selected game data folder browse failures use game-specific safe folder copy, falling back to selected game data folder when no game context is available."
  - "Settings persistence write/read banners preserve ConfigPersistenceFailure.SafeSummary instead of replacing typed safe categories with generic text."

patterns-established:
  - "Use DiagnosticTextFormatter.SafeFileIdentifier for file browse diagnostics that can otherwise expose profile-root paths."
  - "Use DiagnosticTextFormatter.SafeFolderIssue with a game display label for selected folder problems rather than a folder basename/path."
  - "Treat ConfigPersistenceFailure.SafeSummary as the authoritative user-facing persistence category when surfacing Settings write/read failures."

requirements-completed: [SEC-01]

# Metrics
duration: 6 min
completed: 2026-05-01
---

# Phase 11 Plan 03: Configuration, Restore, and Settings Diagnostics Summary

**Configuration browse, restore session loading, and Settings persistence failures now use safe identifiers, typed safe summaries, and latest-log guidance without raw path-bearing exception text.**

## Performance

- **Duration:** 6 min
- **Started:** 2026-05-01T04:37:26Z
- **Completed:** 2026-05-01T04:43:27Z
- **Tasks:** 3 completed
- **Files modified:** 6

## Accomplishments

- Added TDD regression tests for configuration load-order parse failures and missing selected game data folders, proving user-facing text excludes selected paths and raw exception messages.
- Updated `ConfigurationViewModel` to use shared safe file/folder diagnostics for browse failures while preserving plugin refresh, skip-list, selection, and persistence behavior.
- Added and implemented restore session loading coverage so backup session enumeration failures show exact latest-log guidance instead of `ex.Message`.
- Added Settings persistence write/read tests with unsafe path-bearing detail and preserved `ConfigPersistenceFailure.SafeSummary` in the visible banner.

## Task Commits

Each TDD task was committed atomically:

1. **Task 1 RED: Safe configuration browse failure copy tests** - `b829d5a` (test)
2. **Task 1 GREEN: Safe configuration browse diagnostics** - `a922145` (feat)
3. **Task 2 RED: Safe restore session loading failure test** - `d9659dd` (test)
4. **Task 2 GREEN: Safe restore session load status** - `e4267c8` (feat)
5. **Task 3 RED: Settings safe-summary disclosure tests** - `a6f3f96` (test)
6. **Task 3 GREEN: Settings persistence SafeSummary mapping** - `0e46c2a` (feat)

**Plan metadata:** final docs commit for this summary/state update.

## Files Created/Modified

- `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs` - Maps selected load-order and game-data folder browse failures through safe diagnostic formatter copy.
- `AutoQAC/ViewModels/RestoreViewModel.cs` - Replaces backup session load exception status text with exact latest-log guidance.
- `AutoQAC/ViewModels/SettingsViewModel.cs` - Preserves `ConfigPersistenceFailure.SafeSummary` for Settings save write failures and reload read failures.
- `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs` - Adds negative-disclosure coverage for load-order and game-data folder browse failures.
- `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs` - Adds backup session loading failure disclosure regression coverage.
- `AutoQAC.Tests/ViewModels/SettingsViewModelTests.cs` - Adds Settings write/read unsafe-detail tests and updates save write-failure expectation to SafeSummary.

## Decisions Made

- Used a private game-display helper in `ConfigurationViewModel` rather than adding a new service dependency, keeping the change scoped to browse diagnostics.
- Kept simple missing selected game-data folder copy free of latest-log guidance, matching D-08's distinction between missing-path validation and technical failures.
- Preserved cleaning-specific flush failure copy in Settings while switching direct Settings save write failures and reload read failures to `SafeSummary`.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- Existing configuration parsing error test expected the literal word `Error` in status text; it was tightened to accept the new safe `failed` latest-log wording required by the plan.
- The load-order disclosure test uses a temporary `plugins.txt` file while injecting the `C:\Users\Alice` sentinel through the thrown exception, avoiding fragile assumptions about creating files under a real user profile path.

## TDD Gate Compliance

- RED commits present: `b829d5a`, `d9659dd`, `a6f3f96`.
- GREEN commits present after their RED commits: `a922145`, `e4267c8`, `0e46c2a`.
- REFACTOR commit: not needed; no behavior-neutral cleanup was made after GREEN.

## Verification

- PASS: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~Configuration" --nologo`
- PASS: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~RestoreViewModelTests --nologo`
- PASS: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~SettingsViewModelTests --nologo`
- PASS: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~RestoreViewModelTests|FullyQualifiedName~SettingsViewModelTests|FullyQualifiedName~Configuration" --nologo`

## Known Stubs

None. Stub-pattern scan matches were existing nullable field/test variables, designed null-clearing assignments, or collection initializers; no UI-facing placeholder/mock data was introduced.

## Threat Flags

None - no new network endpoints, auth paths, file access patterns, schema changes, or new trust-boundary surfaces were introduced. Existing file/folder browse and persistence failure surfaces were hardened.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Ready for Plan 11-04 to continue diagnostics-boundary hardening on migration/startup or result/export surfaces using the same safe formatter patterns.

## Self-Check: PASSED

- FOUND: `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`
- FOUND: `AutoQAC/ViewModels/RestoreViewModel.cs`
- FOUND: `AutoQAC/ViewModels/SettingsViewModel.cs`
- FOUND: `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs`
- FOUND: `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs`
- FOUND: `AutoQAC.Tests/ViewModels/SettingsViewModelTests.cs`
- FOUND: commit `b829d5a`
- FOUND: commit `a922145`
- FOUND: commit `d9659dd`
- FOUND: commit `e4267c8`
- FOUND: commit `a6f3f96`
- FOUND: commit `0e46c2a`

---
*Phase: 11-user-facing-diagnostics-boundaries*
*Completed: 2026-05-01*
