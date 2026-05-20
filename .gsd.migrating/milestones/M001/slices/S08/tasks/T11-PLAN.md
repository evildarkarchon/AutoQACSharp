# T11: 07-backup-restore-retention-safety 11

**Slice:** S08 — **Milestone:** M001

## Description

Close the verification gap where tampered restore metadata can overwrite a same-named `.esm`, `.esp`, or `.esl` file outside the approved restore root.

Purpose: Restore operations must only overwrite plugins under the current game Data folder passed from the restore UI, while keeping legacy service callers safe by failing closed when no trusted root is provided.
Output: Restore contracts accept a trusted restore root, `BackupService` enforces containment, `RestoreViewModel` passes the configured Data folder, and tests prove out-of-root same-name targets are rejected before directory creation/copy.

## Must-Haves

- [ ] "Restore metadata cannot redirect restores to arbitrary rooted filesystem targets outside the current game Data folder."
- [ ] "Restore UI passes the configured game Data folder as the trusted restore root for selected and all restore operations."
- [ ] "Restore commands are disabled when no trusted restore root is loaded, avoiding opaque fail-closed restore attempts from the UI."

## Files

- `AutoQAC/Services/Backup/IBackupService.cs`
- `AutoQAC/Services/Backup/BackupService.cs`
- `AutoQAC/ViewModels/RestoreViewModel.cs`
- `AutoQAC/Views/MainWindow.axaml.cs`
- `AutoQAC.Tests/Services/BackupServiceTests.cs`
- `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs`
