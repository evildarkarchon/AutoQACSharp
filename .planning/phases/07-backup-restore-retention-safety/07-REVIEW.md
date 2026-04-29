---
phase: 07-backup-restore-retention-safety
reviewed: 2026-04-29T09:25:33Z
depth: standard
files_reviewed: 26
files_reviewed_list:
  - AutoQAC.Tests/Models/BackupOperationResultTests.cs
  - AutoQAC.Tests/Services/BackupFileCopierTests.cs
  - AutoQAC.Tests/Services/BackupServiceTests.cs
  - AutoQAC.Tests/Services/CleaningOrchestratorTests.cs
  - AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs
  - AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs
  - AutoQAC/Infrastructure/ServiceCollectionExtensions.cs
  - AutoQAC/Models/AppState.cs
  - AutoQAC/Models/BackupOperationResults.cs
  - AutoQAC/Models/CleaningSessionResult.cs
  - AutoQAC/Services/Backup/BackupCopyOptions.cs
  - AutoQAC/Services/Backup/BackupFileCopier.cs
  - AutoQAC/Services/Backup/BackupService.cs
  - AutoQAC/Services/Backup/DirectoryBackupSessionDeleter.cs
  - AutoQAC/Services/Backup/IBackupFileCopier.cs
  - AutoQAC/Services/Backup/IBackupService.cs
  - AutoQAC/Services/Backup/IBackupSessionDeleter.cs
  - AutoQAC/Services/Cleaning/CleaningOrchestrator.cs
  - AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs
  - AutoQAC/Services/State/IStateService.cs
  - AutoQAC/Services/State/StateService.cs
  - AutoQAC/ViewModels/ProgressViewModel.cs
  - AutoQAC/ViewModels/RestoreViewModel.cs
  - AutoQAC/Views/MainWindow.axaml.cs
  - AutoQAC/Views/ProgressWindow.axaml
  - AutoQAC/Views/RestoreWindow.axaml
findings:
  critical: 2
  warning: 1
  info: 0
  total: 3
status: issues_found
---

# Phase 07: Code Review Report

**Reviewed:** 2026-04-29T09:25:33Z
**Depth:** standard
**Files Reviewed:** 26
**Status:** issues_found

## Summary

Reviewed the backup/restore/retention implementation, tests, state propagation, and Avalonia UI wiring. The implementation contains two ship-blocking filesystem safety defects: a failed create-new backup copy can delete an existing destination file, and restore metadata can still direct overwrites to arbitrary local plugin-extension paths outside the current game data root. One UI visibility issue should also be fixed to avoid overlaying the active progress surface.

## Critical Issues

### CR-01: BLOCKER - Create-new backup failure can delete an existing destination file

**File:** `AutoQAC/Services/Backup/BackupFileCopier.cs:84-88,106-116,154-160`

**Issue:** `BackupCopyOptions.CreateNewBackup` uses `FileMode.CreateNew`, but every `IOException` path calls `DeletePartialOutput(actualOutputPath, ...)`. If the destination backup file already exists, `new FileStream(..., FileMode.CreateNew, ...)` throws before this call created anything, then `DeletePartialOutput` deletes the pre-existing file. This violates the `FailIfExists` policy and creates a data-loss path for existing backups.

**Fix:** Only delete files that this copy attempt actually created, or use a unique temp file for all copy modes. For example:

```csharp
var outputCreated = false;
try
{
    await using var destination = new FileStream(actualOutputPath, fileMode, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
    outputCreated = true;
    // copy loop...
}
catch (IOException ex)
{
    if (outputCreated || options.ExistingTargetPolicy == BackupCopyExistingTargetPolicy.ReplaceAtomically)
    {
        DeletePartialOutput(actualOutputPath, sourcePath, destinationPath);
    }

    return BackupCopyResult.Failed(sourcePath, destinationPath, BackupFailureReason.TargetWriteFailed, copiedBytes, totalBytes);
}
```

Add a test where `destinationPath` already exists under `CreateNewBackup`; the result should be failed and the original destination contents must remain unchanged.

### CR-02: BLOCKER - Restore metadata can overwrite arbitrary local plugin-extension paths

**File:** `AutoQAC/Services/Backup/BackupService.cs:592-608,622-638`

**Issue:** `ValidateRestoreEntry` treats session metadata as untrusted, but `IsSafeRestoreTarget` only requires a rooted local-drive path, matching file name, and `.esm/.esp/.esl` extension. A crafted `session.json` inside a backup session can set `OriginalPath` to any local path such as `C:\Users\...\Important.esp`; Restore Selected/All will create the containing directory and atomically overwrite that file. Restore targets must be constrained to the expected game Data directory (or another explicit trusted restore root), not merely any local drive.

**Fix:** Persist or pass the expected game data folder/restore root and require the resolved `OriginalPath` to remain under it before directory creation/copy. For example:

```csharp
var restoreRoot = EnsureTrailingDirectorySeparator(Path.GetFullPath(expectedDataFolder));
var targetPath = Path.GetFullPath(entry.OriginalPath);
if (!targetPath.StartsWith(restoreRoot, StringComparison.OrdinalIgnoreCase))
{
    failureReason = BackupFailureReason.TargetFolderCreationFailed;
    return false;
}
```

Expose that root through the restore API or include it in `BackupSession` metadata and validate it against the current configured data folder. Add tests for a malicious session whose `OriginalPath` is a matching-name `.esp` outside the game Data folder; it must fail before creating directories or copying.

## Warnings

### WR-01: WARNING - Results panel remains visible during active cleaning and can overlay the progress UI

**File:** `AutoQAC/Views/ProgressWindow.axaml:236-237`

**Issue:** The results summary `Grid` is visible whenever `!IsPreviewMode`, even while `IsShowingResults` is false during active cleaning. Because it is a later child in the root `Panel`, it can sit above the active cleaning grid; even with mostly hidden children, this creates a fragile hit-test/layout overlay that can interfere with the Stop/Cancel controls.

**Fix:** Gate the entire results grid on an explicit view-model property such as `IsResultsSummaryVisible => IsShowingResults && !IsPreviewMode`, or restructure the panels so only one top-level mode panel is visible at a time.

---

_Reviewed: 2026-04-29T09:25:33Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: standard_
