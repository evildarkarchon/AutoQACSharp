---
phase: 07-backup-restore-retention-safety
reviewed: 2026-04-29T00:00:00Z
depth: standard
files_reviewed: 26
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
  critical: 3
  warning: 5
  info: 0
  total: 8
status: issues_found
---

# Phase 7: Code Review Report

**Reviewed:** 2026-04-29T00:00:00Z
**Depth:** standard
**Files Reviewed:** 26
**Status:** issues_found

## Summary

Reviewed the Phase 7 backup/restore/retention safety changes, including backup copy semantics, restore UI flows, cleanup orchestration, and associated tests. The implementation has several ship-blocking behavioral and security defects: one user choice leaves the app permanently in cleaning state, another skip path corrupts progress/session accounting, and restore trusts session metadata paths without containment checks. Additional warnings cover unhandled backup directory failures, nonfunctional retention progress, UI-thread hazards, cancellation responsiveness, and misleading success semantics.

## Critical Issues

### CR-01: BLOCKER - Aborting after a backup failure leaves cleaning state active forever

**File:** `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:324-338`
**Issue:** When `BackupFailureChoice.AbortSession` is selected, `StartCleaningAsync` returns from inside the main `try` block after optional metadata writing. That bypasses all normal `FinishCleaningWithResults(...)` calls, so `StateService` is never told that cleaning ended. The `finally` block clears CTS/process flags but does not set `IsCleaning = false`; the progress window can remain stuck in an active cleaning state with no final result.
**Fix:** Do not return before finalizing state. Record cancellation/abort and break out through the normal final-result path, or finalize immediately before returning.

```csharp
case BackupFailureChoice.AbortSession:
    logger.Information("User chose to abort session after backup failure for: {Plugin}", plugin.FileName);
    wasCancelled = true;
    if (backupEntries.Count > 0)
    {
        await WritePartialBackupSessionAsync(sessionDir, gameType, backupEntries, cts.Token)
            .ConfigureAwait(false);
    }
    goto FinishSession; // or set a flag and break the plugin loop
```

Add a regression test asserting `FinishCleaningWithResults` is called and `WasCancelled` (or an explicit abort status) is represented when the callback returns `AbortSession`.

### CR-02: BLOCKER - Skipping a plugin after backup failure does not increment progress or session results

**File:** `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:315-323`
**Issue:** The `SkipPlugin` branch only mutates `SkippedPlugins` and then `continue`s. It never adds a `PluginCleaningResult` and never calls `AddDetailedCleaningResult`, so `Progress` is not incremented and the final `CleaningSessionResult.PluginResults` omits a plugin that was part of `TotalPlugins`. This can leave the progress bar below 100% and produce an inaccurate session summary.
**Fix:** Use the same result path as backup cancellation: create a skipped `PluginCleaningResult`, add it to `pluginResults`, and publish it through `stateService.AddDetailedCleaningResult(...)`.

```csharp
case BackupFailureChoice.SkipPlugin:
    var skippedResult = new PluginCleaningResult
    {
        PluginName = plugin.FileName,
        Status = CleaningStatus.Skipped,
        Success = false,
        Message = $"Backup failed: {GetBackupFailureReasonText(backupResult)}"
    };
    pluginResults.Add(skippedResult);
    stateService.AddDetailedCleaningResult(skippedResult);
    continue;
```

Add a test for `BackupFailureChoice.SkipPlugin` that asserts xEdit is not launched, progress advances, and the final session includes the skipped row.

### CR-03: BLOCKER - Restore trusts `session.json` file names and original paths without containment validation

**File:** `AutoQAC/Services/Backup/BackupService.cs:378-400`
**Issue:** Restore builds `backupPath` directly from `sessionDir` plus `entry.FileName`, and writes to `entry.OriginalPath` from `session.json`. A malformed or malicious session metadata file can use path traversal in `file_name` (for example `..\\..\\somefile`) to copy a file outside the backup session, and can choose an arbitrary rooted `original_path` as the overwrite target. Since restore sessions are loaded from disk, the service must treat metadata as untrusted.
**Fix:** Reject metadata entries whose `FileName` is not a simple file name, ensure the resolved backup path remains under the resolved session directory, and validate restore targets against an expected game data root or at least require safe rooted local paths before copying.

```csharp
private static bool IsSafeSessionFileName(string fileName) =>
    !string.IsNullOrWhiteSpace(fileName) &&
    string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal);

private static string ResolveContainedBackupPath(string sessionDir, string fileName)
{
    if (!IsSafeSessionFileName(fileName))
        throw new InvalidDataException("Backup metadata contains an invalid plugin file name.");

    var root = Path.GetFullPath(sessionDir) + Path.DirectorySeparatorChar;
    var candidate = Path.GetFullPath(Path.Combine(sessionDir, fileName));
    if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Backup file path escapes the session directory.");
    return candidate;
}
```

Map rejected rows to structured restore failures instead of throwing, and add tests with `..\\` and absolute-path `FileName` values.

## Warnings

### WR-01: WARNING - Backup target directory creation can throw and abort the cleaning session instead of returning a structured backup failure

**File:** `AutoQAC/Services/Backup/BackupService.cs:111-118`
**Issue:** `BackupPluginAsync` calls `Directory.CreateDirectory(sessionDir)` outside any exception mapping. Access denied, invalid path, or I/O failures here escape the backup result contract and bubble into `CleaningOrchestrator` as a session-level exception, bypassing the backup-failure callback and potentially stopping cleaning unexpectedly.
**Fix:** Catch expected filesystem exceptions around session directory creation and return `BackupCreateResult` with `TargetFolderCreationFailed`.

```csharp
try
{
    Directory.CreateDirectory(sessionDir);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
{
    _logger.Warning("Failed to create backup session directory for {Plugin}: {Error}", plugin.FileName, ex.Message);
    return new BackupCreateResult(BackupOperationStatus.Failed, plugin.FileName, 0, null,
        BackupFailureReason.TargetFolderCreationFailed);
}
```

### WR-02: WARNING - Retention cleanup progress is wired but never reported

**File:** `AutoQAC/Services/Backup/BackupService.cs:297-302`
**Issue:** `CleanupOldSessionsAsync` accepts an `IProgress<BackupCopyProgress>? progress`, and `CleaningOrchestrator` creates UI progress state for retention cleanup, but the backup service never calls `progress.Report(...)`. The progress band therefore remains at the initial `0 files` state until it disappears, so the new Cancel Cleanup UI has no meaningful progress feedback.
**Fix:** Report count-based cleanup progress after classification and after each keep/delete/failure row. If `BackupCopyProgress` is a poor fit for count-only work, introduce a dedicated retention progress model instead of overloading byte-copy progress.

### WR-03: WARNING - Restore progress updates ViewModel properties from the copy worker thread

**File:** `AutoQAC/ViewModels/RestoreViewModel.cs:366-428`
**Issue:** `RestoreProgressReporter.Report` invokes `UpdateRestoreProgress` synchronously. `BackupFileCopier.CopyAsync` uses `ConfigureAwait(false)`, so progress callbacks can run from a thread-pool continuation and raise Avalonia-bound property changes off the UI thread. This is a UI-thread affinity hazard that can cause intermittent binding exceptions or stale UI updates.
**Fix:** Dispatch restore progress back to the UI thread, either by injecting `IUiDispatcher` as done in `ProgressViewModel`, or by using `Progress<BackupCopyProgress>` constructed on the UI thread rather than a custom synchronous reporter.

```csharp
private IProgress<BackupCopyProgress> CreateRestoreProgressReporter(IReadOnlyList<BackupPluginEntry> plugins) =>
    new Progress<BackupCopyProgress>(progress => UpdateRestoreProgress(progress, plugins));
```

If tests require synchronous behavior, use a test dispatcher abstraction instead of bypassing UI dispatch in production code.

### WR-04: WARNING - Cancel Cleanup cannot interrupt a large recursive directory delete once deletion starts

**File:** `AutoQAC/Services/Backup/DirectoryBackupSessionDeleter.cs:13-17`
**Issue:** The deleter checks cancellation only before `Directory.Delete(..., recursive: true)`. For large backup sessions or slow/locked filesystem operations, pressing Cancel Cleanup after deletion begins cannot stop the operation until the synchronous recursive delete returns.
**Fix:** Implement cancellable recursive deletion that enumerates files/directories and checks `ct.ThrowIfCancellationRequested()` between delete operations, or document and surface that cancellation is best-effort once a directory deletion has begun.

### WR-05: WARNING - `CleaningSessionResult.IsSuccess` ignores backup cleanup cancellation and warnings

**File:** `AutoQAC/Models/CleaningSessionResult.cs:107-109`
**Issue:** `IsSuccess` returns true whenever cleaning was not canceled and no plugin failed, even if `BackupCleanup.Status` is `Warning` or `Canceled`. The summary text explicitly treats cleanup warning/cancel as notable outcomes, but any caller using `IsSuccess` will incorrectly report the overall session as fully successful.
**Fix:** Include backup cleanup terminal status in the success predicate.

```csharp
public bool IsSuccess =>
    !WasCancelled &&
    FailedCount == 0 &&
    BackupCleanup?.Status is not BackupOperationStatus.Warning and not BackupOperationStatus.Canceled and not BackupOperationStatus.Failed;
```

---

_Reviewed: 2026-04-29T00:00:00Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: standard_
