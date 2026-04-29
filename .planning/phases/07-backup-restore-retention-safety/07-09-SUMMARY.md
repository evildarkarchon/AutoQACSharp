---
phase: 07-backup-restore-retention-safety
plan: 09
subsystem: backup
tags: [backup, restore, filesystem-safety, cancellation, testing, gap-closure]

# Dependency graph
requires:
  - phase: 07-backup-restore-retention-safety
    provides: structured backup/restore outcomes, cancellable copy service, restore UI summaries, and prior Phase 7 verification gaps
provides:
  - backup destination validation for rooted, traversing, or multi-segment PluginInfo.FileName values
  - restore metadata policy enforcing simple plugin file names, local-drive rooted targets, filename equality, and active plugin extensions
  - mixed failed+canceled restore aggregate status and inline RestoreWindow copy preserving cancellation visibility
affects: [backup-service, restore-ui, phase-07-verification, restore-metadata-policy]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - shared simple session-relative file-name validation before filesystem path composition
    - restore target policy checks before File.Exists, Directory.CreateDirectory, File.Copy, or IBackupFileCopier.CopyAsync
    - aggregate restore status prioritizes any restored rows as Partial and cancellation visibility when no rows restored

key-files:
  created:
    - .planning/phases/07-backup-restore-retention-safety/07-09-SUMMARY.md
  modified:
    - AutoQAC/Services/Backup/BackupService.cs
    - AutoQAC/ViewModels/RestoreViewModel.cs
    - AutoQAC.Tests/Services/BackupServiceTests.cs
    - AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs

key-decisions:
  - "Treat PluginInfo.FileName and BackupPluginEntry metadata as untrusted filesystem input until validated as simple non-rooted names."
  - "Use the approved Phase 7 restore policy: entry.FileName must equal the OriginalPath file name, and both backup and target names must use .esm/.esp/.esl."
  - "Mixed failed+canceled restore sessions with no restored rows report aggregate Canceled so cancellation remains visible in service and UI summaries."

patterns-established:
  - "Backup validation resolves Path.GetFullPath(Path.Combine(sessionRoot, plugin.FileName)) only after simple-name validation, then checks containment under the normalized session root."
  - "Legacy RestorePlugin and async RestorePluginAsync share ValidateRestoreEntry before any target directory creation or copy work."
  - "RestoreViewModel canceled summary copy includes restored, failed, and canceled counts."

requirements-completed: [SAF-04, TEST-04, PERF-04]

# Metrics
duration: 7 min
completed: 2026-04-29
---

# Phase 07 Plan 09: Backup Restore Retention Safety Gap Closure Summary

**Backup and restore filesystem metadata is now validated before copy operations, and mixed failed+canceled restore sessions preserve cancellation in service status and UI copy.**

## Performance

- **Duration:** 7 min
- **Started:** 2026-04-29T09:14:21Z
- **Completed:** 2026-04-29T09:21:16Z
- **Tasks:** 3
- **Files modified:** 4

## Accomplishments

- Added TDD regression coverage for sync and async backup filename traversal/rooted-name rejection before copy operations.
- Added shared backup destination validation that rejects unsafe `PluginInfo.FileName` values and resolves destinations only inside the selected backup session directory.
- Added restore metadata tests and validation for filename mismatches, non-plugin extensions, legacy sync compatibility, safe missing-backup behavior, and positive safe restore behavior.
- Updated legacy `RestorePlugin` to share restore metadata validation before `File.Exists`, target directory creation, or `File.Copy`.
- Added mixed failed+canceled restore session and ViewModel summary regressions, then updated aggregate status semantics so cancellation remains visible when no plugins restored.
- Ran targeted backup service tests, restore ViewModel tests, full solution tests, and the no-parallel-cleaning source invariant successfully.

## Task Commits

Each task was committed atomically using TDD RED/GREEN gates:

1. **Task 1 RED: Reject unsafe backup destination file names** - `a16c387` (test)
2. **Task 1 GREEN: Validate backup destination filenames** - `4838cd6` (feat)
3. **Task 2 RED: Constrain restore target metadata before overwrite** - `9cdb5dd` (test)
4. **Task 2 GREEN: Enforce restore target metadata policy** - `8243239` (feat)
5. **Task 3 RED: Preserve mixed cancellation aggregate status** - `6be5fd2` (test)
6. **Task 3 GREEN: Preserve mixed restore cancellation status** - `67a626b` (fix)

**Plan metadata:** committed separately after state/roadmap updates.

## Files Created/Modified

- `AutoQAC/Services/Backup/BackupService.cs` - Adds `IsSafeSessionRelativeName`, `ValidateBackupDestination`, stricter `ValidateRestoreEntry`, sync restore validation, local-drive target checks, and mixed cancellation status semantics.
- `AutoQAC.Tests/Services/BackupServiceTests.cs` - Adds backup traversal/rooted-name tests, restore target policy tests, sync compatibility tests, safe positive restore coverage, and mixed failed+canceled aggregate coverage.
- `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs` - Adds restore-all inline copy regression for failed+canceled canceled aggregate results.
- `.planning/phases/07-backup-restore-retention-safety/07-09-SUMMARY.md` - Documents gap closure, verification, exclusions, and metadata.

## Decisions Made

- `BackupFailureReason.SourceMissing` remains the compatibility mapping for unsafe async backup filename rejection, per plan, while UI-facing restore labels stay concise.
- Restore target metadata policy intentionally rejects `.esp.ghost` and other non-active plugin extensions in this phase; the approved allow-list is `.esm`, `.esp`, and `.esl` only.
- UNC, device roots, and alternate rooted target forms are rejected for restore metadata by requiring a normal local drive root shape such as `C:\`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Preserved backup session directory setup failure mapping**
- **Found during:** Task 3 (final targeted BackupServiceTests verification)
- **Issue:** Initial Task 1 validation moved async destination validation before session directory creation, causing the existing `BackupPluginAsync_SessionDirectoryCreationFailure_ReturnsStructuredFailure` regression to return `SourceMissing` instead of `TargetFolderCreationFailed` for an invalid session directory path.
- **Fix:** Kept plugin filename validation before copy/path use but restored async session directory creation/error mapping before destination resolution, preserving Plan 07-08 structured setup-failure behavior.
- **Files modified:** `AutoQAC/Services/Backup/BackupService.cs`
- **Verification:** `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~BackupServiceTests"` passed (44 tests).
- **Committed in:** `67a626b`

---

**Total deviations:** 1 auto-fixed (1 bug)
**Impact on plan:** The fix preserved earlier Phase 7 behavior while keeping the new unsafe filename checks before copy operations. No scope expansion.

## Issues Encountered

- `RestoreAllCommand_FailedThenCanceledResult_ShowsCanceledSummaryWithFailedCount` passed during RED because the canceled summary copy already included restored/failed/canceled counts; the service aggregate test provided the required failing RED gate for Task 3.

## Known Stubs

None.

## Threat Flags

None - no new network endpoints, auth paths, schema boundaries, or additional filesystem trust boundaries were introduced. This plan mitigated the existing backup/restore metadata filesystem trust boundaries from the plan threat model.

## Explicit Exclusions / Remaining Risks

- Ghosted plugins such as `.esp.ghost` are intentionally rejected by the active-plugin extension policy and can be revisited in a future restore-policy phase if needed.
- NTFS reparse-point/symlink target escape checks remain out of scope; Phase 7 validates metadata strings and local-drive root shape, not filesystem identity after directory traversal through reparse points.
- Half-deleted retention sessions and unique restore temp-file naming remain review-noted future hardening items outside the three verified Plan 07-09 gaps.

## Verification

- RED Task 1: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~BackupPlugin_FileNameTraversal_ReturnsFailureAndDoesNotEscapeSession|FullyQualifiedName~BackupPluginAsync_FileNameTraversal_ReturnsStructuredFailureAndDoesNotCopy|FullyQualifiedName~BackupPluginAsync_RootedFileName_ReturnsStructuredFailureAndDoesNotCopy"` failed before production changes (3 expected failures).
- GREEN Task 1: same command passed (3 tests).
- RED Task 2: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~RestorePluginAsync_OriginalPathFileNameMismatch_ReturnsTargetFolderCreationFailedAndDoesNotCopy|FullyQualifiedName~RestorePluginAsync_NonPluginOriginalPathExtension_ReturnsTargetFolderCreationFailedAndDoesNotCopy|FullyQualifiedName~RestorePluginAsync_NormalPluginPath_StillRestoresSuccessfully|FullyQualifiedName~RestorePlugin_FileNameMismatch_ThrowsBeforeCopying|FullyQualifiedName~RestorePlugin_MissingBackupFile_WithSafeMetadata_StillThrowsFileNotFoundException"` failed before production changes (3 expected failures, 2 compatibility tests already passing).
- GREEN Task 2: same command passed (5 tests).
- RED Task 3: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~RestoreSessionAsync_FailedThenCanceledRows_ReturnsCanceled|FullyQualifiedName~RestoreAllCommand_FailedThenCanceledResult_ShowsCanceledSummaryWithFailedCount"` failed before production status change (service aggregate failure; ViewModel copy already satisfied).
- GREEN Task 3: same command passed (2 tests).
- Targeted service verification: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~BackupServiceTests"` — passed (44 tests).
- Targeted ViewModel verification: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~RestoreViewModelTests"` — passed (16 tests).
- Full solution verification: `dotnet test AutoQACSharp.slnx` — passed (742 AutoQAC.Tests, 59 QueryPlugins.Tests).
- Secondary source invariant: `Select-String -Path AutoQAC/Services/Cleaning/*.cs -Pattern 'Task\.WhenAll|Parallel\.ForEachAsync|Task\.Run'` — no matches.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Phase 07 gap closure is ready for verification: backup creation cannot escape the selected session directory, restore metadata cannot redirect overwrites through mismatched/non-plugin/root policy failures, mixed failed+canceled restores preserve cancellation visibility, and full solution tests pass.

## Self-Check: PASSED

- Created/modified files verified on disk: `BackupService.cs`, `BackupServiceTests.cs`, `RestoreViewModelTests.cs`, and this summary.
- Task commits verified in git history: `a16c387`, `4838cd6`, `9cdb5dd`, `8243239`, `6be5fd2`, and `67a626b`.

---
*Phase: 07-backup-restore-retention-safety*
*Completed: 2026-04-29*
