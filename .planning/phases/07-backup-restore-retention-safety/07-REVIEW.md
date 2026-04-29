---
phase: 07-backup-restore-retention-safety
reviewed: 2026-04-29T00:00:00Z
depth: standard
files_reviewed: 27
files_reviewed_list:
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
  - AutoQAC/Views/ProgressWindow.axaml
  - AutoQAC/Views/RestoreWindow.axaml
  - AutoQAC.Tests/Models/BackupOperationResultTests.cs
  - AutoQAC.Tests/Services/BackupFileCopierTests.cs
  - AutoQAC.Tests/Services/BackupServiceTests.cs
  - AutoQAC.Tests/Services/CleaningOrchestratorTests.cs
  - AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs
  - AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs
  - AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs
findings:
  critical: 1
  warning: 5
  info: 0
  total: 6
status: issues_found
---

# Phase 07: Code Review Report

**Reviewed:** 2026-04-29T00:00:00Z  
**Depth:** standard  
**Files Reviewed:** 27  
**Status:** issues_found

## Summary

Reviewed backup/restore/retention source, progress UI bindings, orchestrator integration, state service changes, and related tests. The implementation has one shippability blocker in restore progress threading that can break Avalonia UI updates, plus several robustness gaps where backup/retention operations can escape their structured result contracts and bypass the user-facing recovery flows.

## Critical Issues

### CR-01: [BLOCKER] Restore progress mutates UI-bound ViewModel state from the copy worker thread

**File:** `AutoQAC/ViewModels/RestoreViewModel.cs:366-428`

**Issue:** `CreateRestoreProgressReporter` returns a custom `RestoreProgressReporter` whose `Report` method synchronously calls `UpdateRestoreProgress`. The backup service invokes `progress.Report(...)` from `BackupFileCopier.CopyFileContentsAsync`, which runs continuations with `ConfigureAwait(false)`. That means restore progress can update `RestoreProgressText`, `RestoreBytesCopied`, and other bindable state from a thread-pool thread instead of the Avalonia UI thread. Avalonia UI-bound collections/properties are not safe to mutate this way and can throw cross-thread exceptions during real restore progress, even though the tests pass because they use synchronous substitutes.

**Fix:** Marshal restore progress through `IUiDispatcher` (or use `Progress<T>` constructed on the UI thread) and add a test with an asynchronous progress callback to prove updates are dispatched.

```csharp
private readonly IUiDispatcher _uiDispatcher;

public RestoreViewModel(
    IBackupService backupService,
    IMessageDialogService messageDialog,
    ILoggingService logger,
    IUiDispatcher uiDispatcher)
{
    _backupService = backupService;
    _messageDialog = messageDialog;
    _logger = logger;
    _uiDispatcher = uiDispatcher;
}

private IProgress<BackupCopyProgress> CreateRestoreProgressReporter(IReadOnlyList<BackupPluginEntry> plugins) =>
    new Progress<BackupCopyProgress>(progress =>
        _uiDispatcher.Post(() => UpdateRestoreProgress(progress, plugins)));
```

## Warnings

### WR-01: [WARNING] Source length is read outside the copier error-handling boundary

**File:** `AutoQAC/Services/Backup/BackupFileCopier.cs:33-35`

**Issue:** `new FileInfo(sourcePath).Length` runs before the `try` block. If the source is deleted between `File.Exists` and `FileInfo.Length`, or if metadata access throws an `IOException`/`UnauthorizedAccessException`, the exception escapes `CopyAsync` instead of returning a structured `BackupCopyResult`. During cleaning this can abort the whole session and bypass the backup failure callback.

**Fix:** Move output-path calculation and source length probing inside the `try`, and map source races to `SourceMissing` or `AccessDenied`/`TargetWriteFailed` as appropriate.

```csharp
var copiedBytes = 0L;
long? totalBytes = null;
var actualOutputPath = GetOutputPath(destinationPath, options);

try
{
    totalBytes = new FileInfo(sourcePath).Length;
    await CopyFileContentsAsync(..., totalBytes.Value, ...).ConfigureAwait(false);
    ...
}
catch (FileNotFoundException)
{
    return BackupCopyResult.Failed(sourcePath, destinationPath, BackupFailureReason.SourceMissing, copiedBytes, totalBytes);
}
catch (UnauthorizedAccessException)
{
    ...
}
```

### WR-02: [WARNING] Backup session directory creation failures bypass structured backup results

**File:** `AutoQAC/Services/Backup/BackupService.cs:111`

**Issue:** `BackupPluginAsync` calls `Directory.CreateDirectory(sessionDir)` outside any error mapping. An invalid, inaccessible, or deleted backup root throws directly from the service instead of returning `BackupCreateResult` with `TargetFolderCreationFailed`/`AccessDenied`. In `CleaningOrchestrator`, that skips `onBackupFailure` entirely and ends the cleaning workflow through the generic exception path.

**Fix:** Wrap session-directory creation in a targeted `try/catch`, log the technical exception, and return a structured failed backup result so the caller can offer Skip/Abort/Continue.

```csharp
try
{
    Directory.CreateDirectory(sessionDir);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
{
    _logger.Warning("Failed to create backup session directory for {Plugin}: {Error}", plugin.FileName, ex.Message);
    return new BackupCreateResult(
        BackupOperationStatus.Failed,
        plugin.FileName,
        0,
        null,
        ex is UnauthorizedAccessException ? BackupFailureReason.AccessDenied : BackupFailureReason.TargetFolderCreationFailed);
}
```

### WR-03: [WARNING] Timestamp-only session directory names can collide within one second

**File:** `AutoQAC/Services/Backup/BackupService.cs:50-52`

**Issue:** `CreateSessionDirectory` uses only `yyyy-MM-dd_HH-mm-ss`. Two sessions started in the same second reuse the same directory because `Directory.CreateDirectory` is idempotent. That can cause duplicate backup files to fail with `CreateNew`, mix metadata from separate cleaning attempts, or make retention treat distinct sessions as one directory.

**Fix:** Preserve the readable timestamp but create a unique directory when the base name already exists, either by adding sub-second precision or a numeric suffix.

```csharp
var baseName = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
var sessionDir = Path.Combine(backupRoot, baseName);
for (var suffix = 1; Directory.Exists(sessionDir); suffix++)
{
    sessionDir = Path.Combine(backupRoot, $"{baseName}-{suffix:00}");
}
Directory.CreateDirectory(sessionDir);
```

### WR-04: [WARNING] Retention cancellation can throw instead of returning a canceled cleanup result

**File:** `AutoQAC/Services/Backup/BackupService.cs:317,349`

**Issue:** `CleanupOldSessionsAsync` promises structured cleanup status, but cancellation during classification (`ClassifyRetentionDirectoriesAsync`) or just before a deletion (`ct.ThrowIfCancellationRequested()` in the delete loop) propagates `OperationCanceledException`. The orchestrator then falls into its outer cancellation handler with `backupCleanup == null`, so the user loses the retention rows and the session summary cannot report “Backup cleanup canceled”.

**Fix:** Catch expected cancellation inside `CleanupOldSessionsAsync`, add remaining directories/candidates as kept with `Canceled`, publish final progress, and return `BackupRetentionCleanupResult(BackupOperationStatus.Canceled, rows)`.

```csharp
try
{
    var validSessions = await ClassifyRetentionDirectoriesAsync(backupRoot, rows, ct).ConfigureAwait(false);
    ...
    ct.ThrowIfCancellationRequested();
}
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    AddDirectoryRowsAsRemaining(
        Directory.GetDirectories(backupRoot).Where(dir => rows.All(row => !IsSamePath(row.SessionDirectory, dir))),
        rows);
    return new BackupRetentionCleanupResult(BackupOperationStatus.Canceled, rows);
}
```

### WR-05: [WARNING] Restore and backup byte units disagree for the same copy progress model

**File:** `AutoQAC/ViewModels/ProgressViewModel.cs:260-263`; `AutoQAC/ViewModels/RestoreViewModel.cs:404-416`

**Issue:** `ProgressViewModel` formats backup bytes as binary MiB while `RestoreViewModel` formats restore bytes as decimal MB/KB/GB. Both consume `BackupCopyProgress`, so the same 120,000,000-byte copy displays as different values depending on whether it is backup or restore. This is a quality defect in the user-facing progress contract and makes tests encode inconsistent semantics.

**Fix:** Use one shared formatter for `BackupCopyProgress` consumers and update both test suites to assert the same convention.

```csharp
internal static class BackupProgressTextFormatter
{
    public static string FormatBytes(long bytes)
    {
        const decimal kb = 1_000m;
        const decimal mb = kb * 1_000m;
        const decimal gb = mb * 1_000m;
        return bytes switch
        {
            >= 1_000_000_000 => $"{bytes / gb:F1} GB",
            >= 1_000_000 => $"{bytes / mb:F1} MB",
            >= 1_000 => $"{bytes / kb:F1} KB",
            _ => $"{bytes} B"
        };
    }
}
```

---

_Reviewed: 2026-04-29T00:00:00Z_  
_Reviewer: the agent (gsd-code-reviewer)_  
_Depth: standard_
