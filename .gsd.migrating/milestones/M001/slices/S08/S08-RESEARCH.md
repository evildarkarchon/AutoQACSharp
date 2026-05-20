# Phase 7: Backup Restore & Retention Safety - Research

**Researched:** 2026-04-29
**Status:** Complete

## Research Question

What does the planner need to know to implement backup restore, backup copy, and retention cleanup safety with progress, cancellation, structured outcomes, and tests while preserving sequential xEdit cleaning?

## Findings

### Existing implementation shape

- `AutoQAC/Services/Backup/BackupService.cs` is the primary service. It currently uses synchronous `File.Copy`, synchronous `Directory.Delete`, throwing restore methods, and logging-only retention cleanup failures.
- `AutoQAC/Services/Backup/IBackupService.cs` exposes synchronous restore and retention APIs, so Phase 7 needs async/cancellable service contracts.
- `AutoQAC/ViewModels/RestoreViewModel.cs` already owns restore session loading, selected/all restore commands, delete-session command, `StatusText`, and dialog calls. It must stay dialog/service oriented and not manipulate controls directly.
- `AutoQAC/Views/RestoreWindow.axaml` already has a two-pane session/plugin layout. The UI-SPEC requires preserving that layout and adding a lower inline progress/result area.
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` creates per-plugin backups immediately before xEdit launch and runs retention cleanup before session result emission. This is the correct integration point; do not move to all-upfront backup.
- `AutoQAC/ViewModels/ProgressViewModel.cs` consumes `IStateService` and exposes the existing cleaning progress/result surface. Backup/retention progress should extend this path instead of adding another cleaning progress window.

### File copy progress and cancellation options

- Microsoft `CopyFileExW` supports progress callbacks, a cancellation flag, and `PROGRESS_CANCEL` semantics where the partially copied destination is deleted. This exactly matches the Phase 7 requirement that cancellation removes partial files and reports byte progress.
- `FileStream.CopyToAsync(Stream, int, CancellationToken)` supports cancellation and avoids UI-thread blocking, but it does not guarantee partial destination deletion; the implementation must delete partial files explicitly in `OperationCanceledException` and I/O failure paths.
- Recommended approach for this codebase: implement a small injectable `IBackupFileCopier` using managed async stream copy first, because it is easier to unit test without P/Invoke. It must report byte progress after each read/write chunk and must delete the destination on cancellation or failed incomplete copy. If stream-copy cannot satisfy responsiveness in execution, a later implementation can swap internals for `CopyFileExW` behind the same interface without changing ViewModels or orchestrator.

### Structured outcome model

- Current restore throws exceptions and restore-all stops at the first failure. Phase 7 needs result objects that represent expected file-system failures without using thrown exceptions for control flow.
- Use explicit statuses: `Complete`, `Partial`, `Failed`, `Canceled`, and `Warning` where appropriate.
- Row-level restore outcomes should include plugin file name, status (`Restored`, `Failed`, `Canceled`), concise reason, and byte counts when available.
- Failure reasons required by CONTEXT/UI-SPEC: `Missing backup file`, `Access denied`, `Target folder creation failed`, `Target write failed`, `Canceled`, `Cleanup deletion failed`.

### Retention cleanup rules

- Retention must never delete the current session and must keep the newest configured `MaxSessions` among non-current sessions.
- `Directory.Delete(path, recursive: true)` can fail with `IOException` when files/directories are in use and with `UnauthorizedAccessException` when permissions/read-only files block deletion. It can also fail when Explorer has the directory open.
- Retention cleanup should retry deletion once for transient lock-style failures, then keep the session and report it as not deleted.
- Cancellation should stop remaining deletion work and report deleted/skipped/remaining counts. Retention warning/cancel outcomes must be visible before final session completion.

### UI/MVVM implications

- Restore confirmations must use the existing dialog-service pattern, with copy text from `07-UI-SPEC.md`.
- Restore results must remain inline in `RestoreWindow`: no success popup and no auto-close on success or partial failure.
- Restore work needs an active cancellation source owned by `RestoreViewModel`, disposed at completion/cancel/dispose.
- Cleaning backup/retention cancellation should not overload the existing xEdit Stop button. Add non-xEdit operation state to `IStateService` so `ProgressViewModel` can expose `Cancel Backup` / `Cancel Cleanup` commands separately from xEdit `Stop`.

### Testing strategy

- Keep tests in `AutoQAC.Tests` with xUnit, FluentAssertions, NSubstitute, temp directories, `TaskCompletionSource`, and `try/finally`/`IDisposable` cleanup.
- Expand `BackupServiceTests` for missing target recreation, target folder creation failure, missing backup file, access denied/write failure, restore-all partial results, copy cancellation cleanup, retention current-session protection, newest-session retention, deletion retry, deletion failure reporting, and deletion cancellation.
- Add ViewModel tests for restore confirmations, inline result rows, cancellation, command disablement while active, and concise messages.
- Add orchestrator/progress tests proving backup remains per-plugin before each xEdit launch, backup cancellation prevents xEdit launch for that plugin, retention warning/cancel is emitted before final completion, and sequential xEdit launch ordering remains unchanged.

## Architecture Pattern

### Recommended internal API shape

```csharp
public interface IBackupFileCopier
{
    Task<BackupCopyResult> CopyAsync(
        string sourcePath,
        string destinationPath,
        IProgress<BackupCopyProgress>? progress,
        CancellationToken cancellationToken);
}
```

```csharp
public sealed record BackupCopyProgress(
    string Operation,
    string FileName,
    int FilesCompleted,
    int TotalFiles,
    long BytesCopied,
    long? TotalBytes);
```

`IBackupService` should expose async methods such as `BackupPluginAsync`, `RestorePluginAsync`, `RestoreSessionAsync`, and `CleanupOldSessionsAsync` while keeping legacy synchronous wrappers only if needed by untouched callers during transition.

## Security Notes

- Trust boundaries are local filesystem paths from backup metadata and user configuration into file copy/delete APIs, and service outcomes into UI text.
- Mitigate tampered metadata/path disclosure by using concise row-level reasons in UI and logging technical exception details separately.
- Do not show raw exception text, stack traces, full command lines, or avoidable configured paths in restore/progress UI.

## Validation Architecture

Phase 7 validation should sample every behavior cluster with automated tests:

- **Service contract tests:** `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupServiceTests`
- **Restore ViewModel tests:** `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~RestoreViewModelTests`
- **Cleaning/progress integration tests:** `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningOrchestratorTests|FullyQualifiedName~ProgressViewModelTests"`
- **Full verification:** `dotnet test AutoQACSharp.slnx`

Coverage must include SAF-04, TEST-04, and PERF-04. Each plan should add/extend tests before or alongside implementation, and final verification must run the full solution.

## Source Coverage Audit Inputs

- ROADMAP goal: restore backups and backup/retention work have clear failure reporting, cancellation, progress, and sequential xEdit preservation.
- Requirements: `SAF-04`, `TEST-04`, `PERF-04`.
- CONTEXT decisions: `D-01` through `D-20`; none deferred.
- UI-SPEC: restore inline result/progress area, cleaning progress backup/retention state, cancel affordances, required copy, row reasons, outcome treatments.

## RESEARCH COMPLETE