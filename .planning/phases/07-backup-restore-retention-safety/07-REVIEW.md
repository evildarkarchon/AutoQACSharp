---
phase: 07-backup-restore-retention-safety
reviewed: 2026-04-29T00:00:00Z
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
  critical: 2
  warning: 4
  info: 0
  total: 6
status: issues_found
---

# Phase 07: Code Review Report

**Reviewed:** 2026-04-29T00:00:00Z  
**Depth:** standard  
**Files Reviewed:** 26  
**Status:** issues_found

## Summary

Reviewed backup copy semantics, backup/restore/retention services, orchestrator integration, state/progress ViewModels, Avalonia markup, and related tests. The main defects are in filesystem safety: backup creation and restore still trust metadata/path fields in ways that can write outside the intended backup session or overwrite arbitrary rooted paths. Additional behavioral gaps lose cancellation status, misorder sessions, and leak progress-window subscriptions.

## Critical Issues

### CR-01: [BLOCKER] Backup creation allows plugin file-name path traversal outside the session directory

**File:** `AutoQAC/Services/Backup/BackupService.cs:81,133`

**Issue:** Both `BackupPlugin` and `BackupPluginAsync` build the destination with `Path.Combine(sessionDir, plugin.FileName)` without validating that `plugin.FileName` is a simple file name. A malformed `PluginInfo` with `FileName = "..\\..\\victim.esp"` or a rooted file name can escape the backup session directory. The async path then writes through `IBackupFileCopier` with create-new semantics, so it can create files outside `sessionDir`; the legacy path has the same traversal risk. This breaks the retention/restore safety model and can write user-accessible files in unintended locations.

**Fix:** Reject rooted, empty, or multi-segment plugin file names before combining paths, and verify the resolved destination remains under the normalized session root. Add tests for traversal and rooted `PluginInfo.FileName` in both sync and async backup paths.

```csharp
private static bool TryGetSessionFilePath(string sessionDir, string fileName, out string path)
{
    path = string.Empty;
    if (string.IsNullOrWhiteSpace(fileName) ||
        Path.IsPathRooted(fileName) ||
        !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
    {
        return false;
    }

    var root = EnsureTrailingDirectorySeparator(Path.GetFullPath(sessionDir));
    var resolved = Path.GetFullPath(Path.Combine(root, fileName));
    if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    path = resolved;
    return true;
}
```

### CR-02: [BLOCKER] Restore trusts session metadata target paths and can overwrite arbitrary rooted files

**File:** `AutoQAC/Services/Backup/BackupService.cs:506-512,439-444`

**Issue:** `ValidateRestoreEntry` only requires `entry.OriginalPath` to be rooted before passing it to `BackupCopyOptions.AtomicReplace`. A corrupted or malicious `session.json` can point `OriginalPath` at any file the current user can write, and the restore flow will overwrite it using the backup file from the selected session. The legacy `RestorePlugin` path is even less constrained (`BackupService.cs:210-223`) and performs no metadata validation at all. Rooted/normalized is not equivalent to safe; it simply makes the arbitrary overwrite deterministic.

**Fix:** Treat restore metadata as untrusted. At minimum, require `Path.GetFileName(entry.OriginalPath)` to match `entry.FileName` and allow only plugin extensions; preferably pass the expected game Data folder/backup root into restore operations and require the target to remain under that root. Apply the same validation to the legacy restore method or remove/privatize it so callers cannot bypass the safe path.

```csharp
var targetPath = Path.GetFullPath(entry.OriginalPath);
if (!string.Equals(Path.GetFileName(targetPath), entry.FileName, StringComparison.OrdinalIgnoreCase) ||
    !PluginExtensions.Contains(Path.GetExtension(targetPath), StringComparer.OrdinalIgnoreCase) ||
    !targetPath.StartsWith(EnsureTrailingDirectorySeparator(dataFolderRoot), StringComparison.OrdinalIgnoreCase))
{
    failureReason = BackupFailureReason.TargetFolderCreationFailed;
    return false;
}
```

## Warnings

### WR-01: [WARNING] Mixed failed+canceled restores are reported as Failed instead of Canceled/Partial

**File:** `AutoQAC/Services/Backup/BackupService.cs:544-559`

**Issue:** `GetRestoreStatus` returns `Failed` whenever there are no restored rows and not every row is canceled. If one restore row fails (for example, missing backup file) and the user cancels before remaining rows run, the aggregate status becomes `Failed` even though cancellation occurred. The UI then shows “Restore Failed” rather than “Restore Canceled” and cancellation guidance is lost.

**Fix:** Make cancellation participate in aggregate status whenever any row is canceled. If there are successes plus failures/cancellations, return `Partial`; if there are cancellations and no successes, return `Canceled` unless product requirements explicitly define a separate mixed-failure status.

```csharp
if (rows.Any(row => row.Status == BackupRestoreRowStatus.Canceled))
{
    return rows.Any(row => row.Status == BackupRestoreRowStatus.Restored)
        ? BackupOperationStatus.Partial
        : BackupOperationStatus.Canceled;
}
```

### WR-02: [WARNING] Backup session listing sorts by directory name instead of metadata timestamp

**File:** `AutoQAC/Services/Backup/BackupService.cs:168-170,189-193`

**Issue:** `GetBackupSessionsAsync` claims to enumerate newest first, but it orders directories by `Path.GetFileName(dir)` before reading `session.json`. Retention cleanup correctly uses `BackupSession.Timestamp`, so the restore UI and cleanup policy can disagree if a session directory is renamed, manually copied, or recovered from another location. Users can see an older session above the true newest backup.

**Fix:** Deserialize sessions first, then order the returned `BackupSession` instances by `Timestamp` descending with a directory-name tie breaker.

```csharp
return sessions
    .OrderByDescending(session => session.Timestamp)
    .ThenByDescending(session => Path.GetFileName(session.SessionDirectory), StringComparer.OrdinalIgnoreCase)
    .ToList();
```

### WR-03: [WARNING] Progress windows leak subscriptions because their ViewModels are never disposed on close

**File:** `AutoQAC/Views/MainWindow.axaml.cs:136-144,154-165`

**Issue:** `ShowProgressAsync` and `ShowPreviewAsync` create `ProgressViewModel` instances, but never dispose them when the `ProgressWindow` closes. `ProgressViewModel` subscribes to state, detailed result, completion, hang, and termination observables. Closing progress/preview windows leaves those subscriptions alive, causing stale ViewModels to keep receiving updates and retaining UI objects longer than intended.

**Fix:** Dispose the ViewModel from the window `Closed` event for both cleaning progress and dry-run preview windows. Also wire `CloseRequested` for the normal progress window if the close command is visible in result mode.

```csharp
var progressWindow = new ProgressWindow { DataContext = progressViewModel };
progressWindow.Closed += (_, _) => progressViewModel.Dispose();
progressViewModel.CloseRequested += (_, _) => progressWindow.Close();
progressWindow.Show(this);
```

### WR-04: [WARNING] Delete Session bypasses backup service safety and exposes raw exception details

**File:** `AutoQAC/ViewModels/RestoreViewModel.cs:294-312`

**Issue:** `DeleteSessionAsync` deletes `SelectedSession.SessionDirectory` directly from the ViewModel instead of going through a backup service/deleter method with containment checks, retry behavior, and sanitized result mapping. It also passes `ex.Message` directly to the user-facing error dialog, while the rest of this phase intentionally avoids raw filesystem exception text in restore/retention rows. This creates inconsistent deletion semantics and can leak full local paths or OS error details.

**Fix:** Add a structured backup-session delete method to `IBackupService` (or reuse an injected deletion service through the backup service), validate the selected directory is under `_backupRoot`, log technical exceptions, and show concise failure copy in the ViewModel.

```csharp
var result = await _backupService.DeleteSessionAsync(_backupRoot, SelectedSession.SessionDirectory, ct);
if (!result.Success)
{
    await _messageDialog.ShowErrorAsync(
        "Delete Failed",
        "Failed to delete the backup session.",
        "Technical details were written to the log.");
}
```

---

_Reviewed: 2026-04-29T00:00:00Z_  
_Reviewer: the agent (gsd-code-reviewer)_  
_Depth: standard_
