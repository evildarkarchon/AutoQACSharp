---
phase: 07-backup-restore-retention-safety
plan: 10
subsystem: backup
tags: [backup, file-copy, ownership, cancellation, tdd, gap-closure]

# Dependency graph
requires:
  - phase: 07-backup-restore-retention-safety
    provides: cancellable backup file copier and Phase 07 verification gap report from prior plans
provides:
  - create-new backup copy ownership tracking before partial-output deletion
  - regression coverage proving existing create-new backup destinations are preserved
  - atomic restore cancellation coverage proving attempt-owned temp files are still deleted
affects: [backup-file-copier, backup-safety, phase-07-verification]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - copy-attempt output ownership flag set only after destination stream open succeeds
    - partial-output cleanup gated by ownership before File.Delete
    - TDD RED/GREEN commits for backup copy safety gap closure

key-files:
  created:
    - .planning/phases/07-backup-restore-retention-safety/07-10-SUMMARY.md
  modified:
    - AutoQAC/Services/Backup/BackupFileCopier.cs
    - AutoQAC.Tests/Services/BackupFileCopierTests.cs

key-decisions:
  - "Track BackupFileCopier output ownership with a local createdOutput flag rather than changing backup destination naming or copy policy."
  - "Set ownership through an explicit onDestinationOpened callback immediately after destination FileStream construction succeeds."
  - "Keep the existing fixed .autoqac-tmp suffix for atomic restore temp files in this gap-closure plan."

patterns-established:
  - "Create-new copy failures that occur before destination stream ownership do not delete actualOutputPath."
  - "Atomic restore temp cleanup remains active because the temp path is owned once the destination stream opens."

requirements-completed: [SAF-04, TEST-04, PERF-04]

# Metrics
duration: 2 min
completed: 2026-04-29
---

# Phase 07 Plan 10: Create-New Backup Copy Ownership Tracking Summary

**BackupFileCopier now deletes partial outputs only when the active copy attempt opened the output stream, preserving pre-existing create-new backup files.**

## Performance

- **Duration:** 2 min
- **Started:** 2026-04-29T09:51:32Z
- **Completed:** 2026-04-29T09:53:20Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments

- Added a RED regression test proving `BackupCopyOptions.CreateNewBackup` returns `TargetWriteFailed` while leaving an existing destination file and contents intact.
- Added explicit atomic restore cancellation coverage with the required `CopyAsync_CanceledAtomicReplace_DeletesTempFile_RegardlessOfOwnershipFlag` test name.
- Added `createdOutput` ownership tracking in `BackupFileCopier.CopyAsync`, set only through `onDestinationOpened` after the destination `FileStream` constructor succeeds.
- Updated every partial-output cleanup path to pass ownership into `DeletePartialOutput`, which now returns before `File.Delete` when this copy attempt did not create the output.
- Preserved cancellation cleanup for create-new partial files and atomic restore `.autoqac-tmp` files while closing the verified existing-destination deletion gap.

## Task Commits

Each task was committed atomically:

1. **Task 1 RED: Capture existing-destination preservation regression** - `5339aa0` (test)
2. **Task 2 GREEN: Delete partial outputs only when this copy created them** - `7fbcf2d` (feat)

**Plan metadata:** committed separately after state/roadmap updates.

## Files Created/Modified

- `AutoQAC.Tests/Services/BackupFileCopierTests.cs` - Adds existing-destination preservation and atomic temp cleanup regression tests.
- `AutoQAC/Services/Backup/BackupFileCopier.cs` - Adds output ownership tracking and ownership-gated cleanup.
- `.planning/phases/07-backup-restore-retention-safety/07-10-SUMMARY.md` - Documents plan execution, verification, and remaining suffix limitation.

## Decisions Made

- Used the plan's preferred callback approach so ownership flips at the exact stream-open boundary without restructuring the copy loop.
- Left unique temp-file naming out of scope; atomic restore still uses the fixed same-directory `destination + ".autoqac-tmp"` suffix noted by the plan.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- `AutoQAC/Services/Backup/` is covered by a broad ignore rule, so the tracked production file required explicit staging handling during the GREEN commit.

## Known Stubs

None. The stub scan found only the existing local nullable initialization `long? totalBytes = null`, which is internal copy-result state and not a UI/data-source placeholder.

## Verification

- RED: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupFileCopierTests.CopyAsync_CreateNewDestinationAlreadyExists_PreservesExistingDestination` failed before production changes because the existing destination was deleted.
- GREEN targeted: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupFileCopierTests` passed (7 tests).
- Full solution: `dotnet test AutoQACSharp.slnx` passed (744 AutoQAC.Tests, 59 QueryPlugins.Tests).
- Acceptance strings verified for `createdOutput`, `onDestinationOpened`, ownership-aware `DeletePartialOutput(...)` calls, and `if (!createdOutput)`.

## TDD Gate Compliance

- RED gate present: `5339aa0` (`test(07-10): add failing backup copy ownership regression`).
- GREEN gate present after RED: `7fbcf2d` (`feat(07-10): track backup copy output ownership`).
- REFACTOR gate not needed; no behavior-neutral cleanup commit was produced.

## Threat Flags

None - no new network endpoints, auth paths, schema boundaries, or additional filesystem trust boundaries were introduced. This plan mitigated the existing backup destination filesystem delete boundary.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Plan 07-11 can address the remaining restore-root containment gap from `07-VERIFICATION.md`; the create-new backup destination deletion blocker is closed and full solution tests pass.

## Self-Check: PASSED

- Modified files verified on disk: `BackupFileCopier.cs`, `BackupFileCopierTests.cs`, and this summary.
- Task commits verified in git history: `5339aa0` and `7fbcf2d`.

---
*Phase: 07-backup-restore-retention-safety*
*Completed: 2026-04-29*
