---
phase: 07-backup-restore-retention-safety
reviewed: 2026-04-29T10:07:24Z
depth: standard
files_reviewed: 26
files_reviewed_list:
  - AutoQAC/Models/BackupOperationResults.cs
  - AutoQAC/Services/Backup/IBackupFileCopier.cs
  - AutoQAC/Services/Backup/BackupFileCopier.cs
  - AutoQAC/Services/Backup/BackupCopyOptions.cs
  - AutoQAC/Services/Backup/IBackupService.cs
  - AutoQAC/Infrastructure/ServiceCollectionExtensions.cs
  - AutoQAC.Tests/Services/BackupFileCopierTests.cs
  - AutoQAC.Tests/Models/BackupOperationResultTests.cs
  - AutoQAC/Services/Backup/BackupService.cs
  - AutoQAC/Services/Backup/IBackupSessionDeleter.cs
  - AutoQAC/Services/Backup/DirectoryBackupSessionDeleter.cs
  - AutoQAC.Tests/Services/BackupServiceTests.cs
  - AutoQAC/Models/AppState.cs
  - AutoQAC/Models/CleaningSessionResult.cs
  - AutoQAC/Services/State/IStateService.cs
  - AutoQAC/Services/State/StateService.cs
  - AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs
  - AutoQAC/Services/Cleaning/CleaningOrchestrator.cs
  - AutoQAC.Tests/Services/CleaningOrchestratorTests.cs
  - AutoQAC/ViewModels/RestoreViewModel.cs
  - AutoQAC/Views/RestoreWindow.axaml
  - AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs
  - AutoQAC/ViewModels/ProgressViewModel.cs
  - AutoQAC/Views/ProgressWindow.axaml
  - AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs
  - AutoQAC/Views/MainWindow.axaml.cs
findings:
  critical: 3
  warning: 0
  info: 0
  total: 3
status: issues_found
---

# Phase 07: Code Review Report

**Reviewed:** 2026-04-29T10:07:24Z
**Depth:** standard
**Files Reviewed:** 26
**Status:** issues_found

## Summary

Reviewed the backup/restore/retention safety changes, related state/progress UI integration, and tests. The implementation still contains three ship-blocking defects: restore cancellation can be reported as a partial success, the progress window result Close button is not wired for the normal cleaning window, and backup session deletion performs an unvalidated recursive delete from UI-bound state.

## Critical Issues

### CR-01: BLOCKER - Restore sessions with restored then canceled rows are reported as Partial instead of Canceled

**File:** `AutoQAC/Services/Backup/BackupService.cs:703-717`

**Issue:** `GetRestoreStatus` checks for any restored row before checking for canceled rows. If Restore All restores one plugin and the user cancels before a later plugin, the aggregate status becomes `Partial`, not `Canceled`. The inline UI then uses the partial-success title and summary instead of the cancellation path, even though cancellation was requested and canceled rows are present. Existing tests cover failed+canceled rows, but not restored+canceled rows, so this regression is currently untested.

**Fix:** Prioritize canceled rows before partial success, and add a regression test for restored+canceled restore sessions.

```csharp
private static BackupOperationStatus GetRestoreStatus(IReadOnlyCollection<BackupRestoreRowResult> rows)
{
    if (rows.Count == 0 || rows.All(row => row.Status == BackupRestoreRowStatus.Restored))
    {
        return BackupOperationStatus.Complete;
    }

    if (rows.Any(row => row.Status == BackupRestoreRowStatus.Canceled))
    {
        return BackupOperationStatus.Canceled;
    }

    return rows.Any(row => row.Status == BackupRestoreRowStatus.Restored)
        ? BackupOperationStatus.Partial
        : BackupOperationStatus.Failed;
}
```

### CR-02: BLOCKER - Normal progress window Close button never closes and subscriptions are left alive

**File:** `AutoQAC/Views/MainWindow.axaml.cs:136-142`

**Issue:** `ShowProgressAsync` creates a `ProgressViewModel` but never subscribes to `CloseRequested` and never disposes it when the window closes. The results panel Close button in `ProgressWindow.axaml` invokes `CloseCommand`, which only raises `CloseRequested`; for the normal cleaning progress window, no handler closes the window. The same missing disposal leaves state/orchestrator subscriptions alive after the window is closed, causing stale view models to keep receiving session updates. `ShowPreviewAsync` wires `CloseRequested`, but the primary cleaning path does not.

**Fix:** Mirror the preview path and dispose the view model on window close.

```csharp
var progressViewModel = new ProgressViewModel(_stateService, _orchestrator, _uiDispatcher);
var progressWindow = new ProgressWindow
{
    DataContext = progressViewModel
};

progressViewModel.CloseRequested += (_, _) => progressWindow.Close();
progressWindow.Closed += (_, _) => progressViewModel.Dispose();

progressWindow.Show(this);
```

Add a view/controller-level test or interaction lifecycle test that verifies `ShowProgressAsync` wires `CloseRequested` and disposes the `ProgressViewModel` when the window closes.

### CR-03: BLOCKER - Delete Session can recursively delete an arbitrary path from mutable UI state

**File:** `AutoQAC/ViewModels/RestoreViewModel.cs:303-307`

**Issue:** `DeleteSessionAsync` recursively deletes `SelectedSession.SessionDirectory` directly. `SelectedSession` is public bindable state and `BackupSession.SessionDirectory` originates from deserialized/session data once loaded; the delete path is not revalidated against `_backupRoot` before `Directory.Delete(..., recursive: true)`. A stale or maliciously injected `BackupSession` can therefore point deletion outside the configured backup root, creating a data-loss risk.

**Fix:** Fail closed unless the selected session directory normalizes under the loaded backup root, and test both traversal/sibling-prefix rejection and normal deletion.

```csharp
private static bool IsSessionDirectoryInsideBackupRoot(string sessionDirectory, string? backupRoot)
{
    if (string.IsNullOrWhiteSpace(backupRoot) || string.IsNullOrWhiteSpace(sessionDirectory))
    {
        return false;
    }

    var root = Path.GetFullPath(backupRoot);
    root = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
    var candidate = Path.GetFullPath(sessionDirectory);
    return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
}

// Before Directory.Delete:
if (!IsSessionDirectoryInsideBackupRoot(dirToDelete, _backupRoot))
{
    _logger.Warning("Rejected backup session delete outside backup root: {SessionDirectory}", dirToDelete);
    await _messageDialog.ShowErrorAsync("Delete Failed", "Backup session path is not safe to delete.");
    return;
}
```

---

_Reviewed: 2026-04-29T10:07:24Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: standard_
