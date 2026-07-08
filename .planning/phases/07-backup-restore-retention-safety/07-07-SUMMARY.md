---
phase: 07-backup-restore-retention-safety
plan: 07
subsystem: backup
tags: [backup, restore, retention, metadata-validation, progress, testing]

# Dependency graph
requires:
  - phase: 07-backup-restore-retention-safety
    provides: structured backup/restore/retention contracts, cancellable copy service, and progress UI state from Plans 07-01 through 07-06
provides:
  - restore metadata validation that rejects unsafe backup FileName values before copying
  - rooted restore target validation and normalized target paths before overwrite attempts
  - retention cleanup count progress that flows through the existing backup operation progress path
  - regression coverage for malicious metadata, cleanup deletion warnings, access-denied mapping, and target write failures
affects: [backup-service, backup-file-copier, cleaning-progress-ui, phase-07-verification]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - untrusted backup metadata validation before filesystem copy
    - count-only retention progress through BackupCopyProgress
    - TDD RED/GREEN commits for backup safety gap closure

key-files:
  created:
    - .planning/phases/07-backup-restore-retention-safety/07-07-SUMMARY.md
  modified:
    - AutoQAC/Services/Backup/BackupService.cs
    - AutoQAC/Services/Backup/BackupFileCopier.cs
    - AutoQAC/Models/BackupOperationResults.cs
    - AutoQAC/Services/Cleaning/CleaningOrchestrator.cs
    - AutoQAC.Tests/Services/BackupServiceTests.cs
    - AutoQAC.Tests/Services/BackupFileCopierTests.cs

key-decisions:
  - "Treat backup session metadata as untrusted input: FileName must be a simple non-rooted name and the resolved backup path must stay inside the selected session directory."
  - "Use rooted OriginalPath plus Path.GetFullPath normalization as the restore target safety policy available at the BackupService boundary."
  - "Carry retention count progress on BackupCopyProgress optional count fields so the existing AppState.BackupOperation UI path can render cleanup progress without a new model."

patterns-established:
  - "Restore path validation happens before File.Exists, Directory.CreateDirectory, or IBackupFileCopier.CopyAsync."
  - "Retention cleanup reports progress after classification and after each kept/deleted/failed/canceled row."
  - "BackupFileCopier no longer creates missing destination directories; callers own target directory validation and creation."

requirements-completed: [SAF-04, TEST-04, PERF-04]

# Metrics
duration: 5 min
completed: 2026-04-29
---

# Phase 07 Plan 07: Backup Restore Metadata and Retention Gap Closure Summary

**Backup restore now rejects unsafe session metadata before copying, retention cleanup emits count progress, and TEST-04 covers permission/write and deletion-warning failures.**

## Performance

- **Duration:** 5 min
- **Started:** 2026-04-29T07:48:14Z
- **Completed:** 2026-04-29T07:52:58Z
- **Tasks:** 3
- **Files modified:** 6

## Accomplishments

- Added malicious restore metadata regression tests for `..\\` FileName traversal, absolute FileName values, and unrooted OriginalPath targets.
- Added `ValidateRestoreEntry` so async restore rejects unsafe metadata before path existence checks, directory creation, or copy attempts.
- Added count fields to `BackupCopyProgress`, retention cleanup `ReportRetentionProgress`, and orchestrator mapping so cleanup progress advances beyond the initial static band.
- Added cleanup deletion failure-after-retry coverage proving `Warning`, two attempts, and `Cleanup deletion failed` row output.
- Added BackupFileCopier target write failure coverage and access-denied restore row mapping coverage without machine-specific ACL mutations.

## Task Commits

Each task was committed atomically:

1. **Task 1 RED: Add unsafe metadata and deletion-warning regression tests** - `dff11de` (test)
2. **Task 2 GREEN: Validate restore metadata and report retention count progress** - `f3b1df6` (feat)
3. **Task 3 RED: Add access/write failure copy coverage** - `d17fbd2` (test)
4. **Task 3 GREEN: Map missing copy targets to write failure** - `35a9ac2` (fix)

**Plan metadata:** committed separately after state/roadmap updates.

## Files Created/Modified

- `AutoQAC/Services/Backup/BackupService.cs` - Adds restore metadata validation, normalized restore paths, and retention count progress reporting.
- `AutoQAC/Services/Backup/BackupFileCopier.cs` - Stops implicitly creating missing destination directories so missing parents map to `TargetWriteFailed`.
- `AutoQAC/Models/BackupOperationResults.cs` - Extends `BackupCopyProgress` with optional file/session count fields while preserving existing byte progress callers.
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` - Maps retention cleanup count progress into `BackupOperationState.FilesCompleted` and `TotalFiles`.
- `AutoQAC.Tests/Services/BackupServiceTests.cs` - Adds restore metadata, access-denied mapping, retention progress, and deletion-warning regression coverage.
- `AutoQAC.Tests/Services/BackupFileCopierTests.cs` - Adds missing destination directory write-failure coverage.

## Decisions Made

- Treat session metadata as hostile local input and fail unsafe restore source metadata with the existing `Missing backup file` label rather than exposing path details.
- Use rooted/normalized OriginalPath validation at the service boundary because no game data root is available in the restore metadata contract.
- Extend the existing progress record rather than introducing a separate retention progress type, minimizing changes to the orchestrator and ProgressViewModel path.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- Running two dotnet test commands in parallel briefly caused a SourceLink file lock in `QueryPlugins`. Re-running the Phase 7 targeted cluster sequentially passed.

## Known Stubs

None.

## Threat Flags

None - no new network endpoints, auth paths, or schema trust boundaries were introduced; the existing backup metadata filesystem trust boundary was mitigated per the plan.

## Verification

- RED verification: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupServiceTests` failed on the three unsafe restore metadata tests before the production fix.
- Task 2 verification: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupServiceTests` passed (31 tests).
- RED verification: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~BackupServiceTests|FullyQualifiedName~BackupFileCopierTests"` failed on `CopyAsync_DestinationDirectoryMissing_ReturnsTargetWriteFailed` before the copier fix.
- Required verification: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~BackupServiceTests|FullyQualifiedName~BackupFileCopierTests"` passed (37 tests).
- Phase 7 targeted cluster: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~BackupServiceTests|FullyQualifiedName~BackupFileCopierTests|FullyQualifiedName~ProgressViewModelTests"` passed (63 tests) after sequential rerun.
- Acceptance strings verified for `ValidateRestoreEntry`, `Path.GetFullPath(sessionDir)`, `BackupFailureReason.MissingBackupFile`, `ReportRetentionProgress`, `progress?.Report`, malicious metadata test names, `CopyAsync_DestinationDirectoryMissing_ReturnsTargetWriteFailed`, and `Access denied` coverage.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Phase 07 backup/restore/retention safety gaps are closed and ready for phase verification or milestone completion.

## Self-Check: PASSED

- Modified files verified on disk: `BackupService.cs`, `BackupFileCopier.cs`, `BackupOperationResults.cs`, `CleaningOrchestrator.cs`, `BackupServiceTests.cs`, `BackupFileCopierTests.cs`, and this summary.
- Task commits verified in git history: `dff11de`, `f3b1df6`, `d17fbd2`, and `35a9ac2`.

---
*Phase: 07-backup-restore-retention-safety*
*Completed: 2026-04-29*
