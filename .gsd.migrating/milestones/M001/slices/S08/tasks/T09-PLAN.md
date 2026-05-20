# T09: 07-backup-restore-retention-safety 09

**Slice:** S08 — **Milestone:** M001

## Description

Close the remaining Phase 7 filesystem-safety and restore aggregate-status gaps found by verification, incorporating cross-AI review feedback for Plan 07-09 before execution.

Purpose: Phase 7 cannot be considered complete while untrusted plugin names or backup metadata can redirect file operations outside intended locations, while legacy sync behavior can drift from async safety, or while mixed cancellation outcomes are hidden behind a failed aggregate title.
Output: One focused gap-closure implementation and regression test update for `BackupService` and restore inline result copy.

## Must-Haves

- [ ] "Backup creation rejects rooted, traversing, or multi-segment PluginInfo.FileName values before constructing a destination path."
- [ ] "Restore metadata rejects arbitrary rooted targets by enforcing shared plugin file-name, local-drive rooted target, and extension policy before copy or directory creation."
- [ ] "Legacy synchronous restore callers are audited and either use the shared validation helper or are documented as test-only compatibility paths."
- [ ] "Restore session aggregate status and inline UI copy preserve cancellation visibility when failed and canceled rows are mixed."

## Files

- `AutoQAC/Services/Backup/BackupService.cs`
- `AutoQAC/ViewModels/RestoreViewModel.cs`
- `AutoQAC.Tests/Services/BackupServiceTests.cs`
- `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs`
