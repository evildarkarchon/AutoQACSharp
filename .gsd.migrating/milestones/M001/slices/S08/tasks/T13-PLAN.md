# T13: 07-backup-restore-retention-safety 13

**Slice:** S08 — **Milestone:** M001

## Description

Close the verification gap where RestoreWindow Delete Session can recursively delete a mutable selected-session path without validating it remains under the loaded backup root, AND resolve the cross-AI reviewer consensus that filesystem work must move out of the ViewModel layer (per CLAUDE.md "All business logic lives in services, not ViewModels").

Purpose: SAF-04 and TEST-04 require restore/backup management safety across failure paths. Delete Session is part of the restore window's backup-management surface and must treat session metadata as untrusted local filesystem input before recursive deletion. This plan also closes the architectural debt the reviewers (the agent + Codex MEDIUM-HIGH; Gemini accepting if normalization is consistent) flagged: ViewModels should not call `System.IO.Directory.Delete` directly when an injectable service seam already exists.

Output:
1. New `IBackupService.DeleteSessionAsync(BackupSession session, string backupRoot, CancellationToken ct)` returning a structured `BackupSessionDeleteResult`. The implementation validates containment via `BackupPathContainment.IsContained` (from Plan 07-14) and routes the actual recursive delete through the existing `IBackupSessionDeleter` (from Plan 07-02).
2. `RestoreViewModel.DeleteSessionAsync` calls the new service method, consumes the structured result, updates `StatusText` / `Sessions` / `SelectedSession`, and shows one canonical user-facing error sentence on rejection.
3. `DeleteSessionCommand`'s `CanExecute` predicate is gated on BOTH `_backupRoot` non-null/non-whitespace AND `HasTrustedRestoreRoot`, unifying Delete Session safety with the Restore Selected/All gating already added in Plan 07-11.
4. Tests prove traversal/sibling-prefix/null-`_backupRoot` rejection plus normal contained deletion at both the service layer and the ViewModel layer.

## Must-Haves

- [ ] "RestoreWindow Delete Session cannot recursively delete a session directory outside the loaded backup root."
- [ ] "Unsafe backup session deletion fails closed with one canonical user-facing sentence while technical path details stay in logs, preserving D-04's concise reason pattern."
- [ ] "Normal Delete Session still removes sessions under the loaded backup root after user confirmation."
- [ ] "DeleteSessionCommand is disabled when `_backupRoot` is null/empty/whitespace OR when no trusted restore root is loaded — this unifies Delete Session safety with the Restore Selected/All gating from Plan 07-11."
- [ ] "Recursive `Directory.Delete` no longer runs from the ViewModel layer; it runs through `IBackupService.DeleteSessionAsync(BackupSession session, string backupRoot, CancellationToken ct)` which delegates filesystem work to `IBackupSessionDeleter` and uses `BackupPathContainment.IsContained` for the safety boundary."
- [ ] "No path-containment logic is duplicated between `BackupService.cs` and `RestoreViewModel.cs` — `grep -nE \"Path\\.GetFullPath.*StartsWith\" AutoQAC/Services/Backup/BackupService.cs AutoQAC/ViewModels/RestoreViewModel.cs` returns zero matches; both files delegate to `BackupPathContainment.IsContained`."
- [ ] "Sequential xEdit cleaning invariant is preserved — no `Task.WhenAll`, `Parallel.ForEachAsync`, or `Task.Run` is added under `AutoQAC/Services/Cleaning` by this plan."

## Files

- `AutoQAC/Services/Backup/IBackupService.cs`
- `AutoQAC/Services/Backup/BackupService.cs`
- `AutoQAC/Models/BackupOperationResults.cs`
- `AutoQAC/ViewModels/RestoreViewModel.cs`
- `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs`
- `AutoQAC.Tests/Services/BackupServiceTests.cs`
