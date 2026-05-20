# T08: 07-backup-restore-retention-safety 08

**Slice:** S08 — **Milestone:** M001

## Description

Close the three remaining Phase 7 verification gaps from `07-VERIFICATION.md`: restore progress UI-thread marshalling, structured backup session directory creation failures, and structured retention cleanup cancellation.

Purpose: Phase 7 cannot pass verification while real async restore progress can mutate UI-bound state off the Avalonia thread or filesystem/cancellation paths can bypass structured user-visible outcomes.
Output: Source/test changes that make the failed truths in `07-VERIFICATION.md` pass, plus `.planning/phases/07-backup-restore-retention-safety/07-08-SUMMARY.md`.

## Must-Haves

- [ ] "GAP-01: Restore progress callbacks from real async copy work are marshaled through IUiDispatcher before any RestoreViewModel UI-bound property mutation."
- [ ] "GAP-02: BackupPluginAsync maps expected backup session directory creation failures to BackupCreateResult so backup failure choice handling receives structured failures."
- [ ] "GAP-03: CleanupOldSessionsAsync converts cancellation during classification or immediately before deletion into BackupRetentionCleanupResult(Canceled) with rows/counts instead of throwing."

## Files

- `AutoQAC/ViewModels/RestoreViewModel.cs`
- `AutoQAC/Views/MainWindow.axaml.cs`
- `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs`
- `AutoQAC/Services/Backup/BackupService.cs`
- `AutoQAC.Tests/Services/BackupServiceTests.cs`
- `.planning/phases/07-backup-restore-retention-safety/07-08-SUMMARY.md`
