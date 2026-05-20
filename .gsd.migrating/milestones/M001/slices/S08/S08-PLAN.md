# S08: Backup Restore Retention Safety

**Goal:** Create the shared backup/restore/retention contracts and cancellable file-copy foundation that all later Phase 7 plans build on.
**Demo:** Create the shared backup/restore/retention contracts and cancellable file-copy foundation that all later Phase 7 plans build on.

## Must-Haves


## Tasks

- [x] **T01: 07-backup-restore-retention-safety 01** `est:6 min`
  - Create the shared backup/restore/retention contracts and cancellable file-copy foundation that all later Phase 7 plans build on.

Purpose: Phase 7 cannot safely report partial restore, retention warning, or copy cancellation outcomes while service APIs only throw or return a boolean-style backup result.
Output: Result records/enums, injectable copier, DI registration, and targeted tests proving cancellation deletes partial files.
- [x] **T02: 07-backup-restore-retention-safety 02** `est:8 min`
  - Implement the async structured restore and retention service APIs using the contracts from Plan 07-01.

Purpose: Restore safety and retention cleanup must become recoverable batch operations instead of thrown exceptions and logging-only cleanup failures.
Output: `BackupService` async behavior plus expanded service tests for restore and retention safety.
- [x] **T03: 07-backup-restore-retention-safety 03** `est:11 min`
  - Wire structured backup and retention operations into the sequential cleaning session without changing xEdit launch ordering.

Purpose: Users need visible, cancellable backup/retention work, and retention warnings must be part of the final session outcome rather than hidden log-only cleanup.
Output: State/orchestrator support for backup and retention progress, separate cancellation, retention warnings/cancel outcomes, and integration tests.
- [x] **T04: 07-backup-restore-retention-safety 04** `est:5 min`
  - Implement RestoreWindow confirmation, progress, cancellation, and inline result reporting against the structured restore APIs.

Purpose: Restore safety is user-facing; users need overwrite confirmation and inspectable complete/partial/failed/canceled rows without raw technical details.
Output: Restore ViewModel/window changes plus ViewModel tests.
- [x] **T05: 07-backup-restore-retention-safety 05** `est:5 min`
  - Complete cleaning progress UI integration and final Phase 7 automated verification.

Purpose: The service layer can produce backup/retention progress only if the existing progress window shows it clearly with non-xEdit cancel controls and final warning/canceled summaries.
Output: ProgressViewModel/window bindings, tests, and full solution verification.
- [x] **T06: 07-backup-restore-retention-safety 06** `est:2 min`
  - Close the verification gap where backup failure decisions can leave cleaning progress/session reporting incomplete.

Purpose: Phase 7 cannot satisfy clear backup failure reporting if SkipPlugin silently drops a plugin result or AbortSession exits before publishing a final session result.
Output: Production fixes plus targeted regression tests for `BackupFailureChoice.SkipPlugin` and `BackupFailureChoice.AbortSession`.
- [x] **T07: 07-backup-restore-retention-safety 07** `est:5 min`
  - Close backup-service verification gaps for unsafe restore metadata, hollow retention cleanup progress, and missing TEST-04 failure coverage.

Purpose: Phase 7 restore and retention work must be safe when reading backup metadata, visibly progressing during cleanup, and test-covered for permission and deletion-failure outcomes.
Output: Backup service validation/progress fixes plus regression tests for traversal, absolute backup filenames, unsafe restore targets, access-denied/write failure mapping, and cleanup deletion warning after retry.
- [x] **T08: 07-backup-restore-retention-safety 08** `est:2 min`
  - Close the three remaining Phase 7 verification gaps from `07-VERIFICATION.md`: restore progress UI-thread marshalling, structured backup session directory creation failures, and structured retention cleanup cancellation.

Purpose: Phase 7 cannot pass verification while real async restore progress can mutate UI-bound state off the Avalonia thread or filesystem/cancellation paths can bypass structured user-visible outcomes.
Output: Source/test changes that make the failed truths in `07-VERIFICATION.md` pass, plus `.planning/phases/07-backup-restore-retention-safety/07-08-SUMMARY.md`.
- [x] **T09: 07-backup-restore-retention-safety 09** `est:7 min`
  - Close the remaining Phase 7 filesystem-safety and restore aggregate-status gaps found by verification, incorporating cross-AI review feedback for Plan 07-09 before execution.

Purpose: Phase 7 cannot be considered complete while untrusted plugin names or backup metadata can redirect file operations outside intended locations, while legacy sync behavior can drift from async safety, or while mixed cancellation outcomes are hidden behind a failed aggregate title.
Output: One focused gap-closure implementation and regression test update for `BackupService` and restore inline result copy.
- [x] **T10: 07-backup-restore-retention-safety 10** `est:2 min`
  - Close the verification gap where `BackupFileCopier.CopyAsync` can delete a pre-existing create-new backup destination when `FileMode.CreateNew` fails before this copy attempt creates output.

Purpose: Backup creation must be fail-safe; an attempted backup must not remove an older backup file at the same destination.
Output: Ownership-aware partial cleanup in `BackupFileCopier` plus a failing-then-passing regression test.
- [x] **T11: 07-backup-restore-retention-safety 11** `est:8 min`
  - Close the verification gap where tampered restore metadata can overwrite a same-named `.esm`, `.esp`, or `.esl` file outside the approved restore root.

Purpose: Restore operations must only overwrite plugins under the current game Data folder passed from the restore UI, while keeping legacy service callers safe by failing closed when no trusted root is provided.
Output: Restore contracts accept a trusted restore root, `BackupService` enforces containment, `RestoreViewModel` passes the configured Data folder, and tests prove out-of-root same-name targets are rejected before directory creation/copy.
- [x] **T12: 07-backup-restore-retention-safety 12** `est:3 min`
  - Close the verification gap where the normal cleaning progress window can display completed results but its Close command does not have explicit normal-path close/disposal wiring.

Purpose: PERF-04 requires visible cancellable backup/retention progress without stale UI subscriptions after completion. This gap closure ensures the normal progress-window path has the same lifecycle guarantees as the preview path while explicitly documenting how the new wiring composes with the existing `ProgressWindow.axaml.cs` cleanup helper.

Output: Source-level regression coverage (using loosened regex assertions) plus normal `ShowProgressAsync` lifecycle wiring that closes the window from `ProgressViewModel.CloseRequested` and disposes the ViewModel when the window closes, with an explicit defense-in-depth code comment so future readers do not mistake the dual subscription for redundant work.
- [x] **T13: 07-backup-restore-retention-safety 13** `est:7 min`
  - Close the verification gap where RestoreWindow Delete Session can recursively delete a mutable selected-session path without validating it remains under the loaded backup root, AND resolve the cross-AI reviewer consensus that filesystem work must move out of the ViewModel layer (per CLAUDE.md "All business logic lives in services, not ViewModels").

Purpose: SAF-04 and TEST-04 require restore/backup management safety across failure paths. Delete Session is part of the restore window's backup-management surface and must treat session metadata as untrusted local filesystem input before recursive deletion. This plan also closes the architectural debt the reviewers (the agent + Codex MEDIUM-HIGH; Gemini accepting if normalization is consistent) flagged: ViewModels should not call `System.IO.Directory.Delete` directly when an injectable service seam already exists.

Output:
1. New `IBackupService.DeleteSessionAsync(BackupSession session, string backupRoot, CancellationToken ct)` returning a structured `BackupSessionDeleteResult`. The implementation validates containment via `BackupPathContainment.IsContained` (from Plan 07-14) and routes the actual recursive delete through the existing `IBackupSessionDeleter` (from Plan 07-02).
2. `RestoreViewModel.DeleteSessionAsync` calls the new service method, consumes the structured result, updates `StatusText` / `Sessions` / `SelectedSession`, and shows one canonical user-facing error sentence on rejection.
3. `DeleteSessionCommand`'s `CanExecute` predicate is gated on BOTH `_backupRoot` non-null/non-whitespace AND `HasTrustedRestoreRoot`, unifying Delete Session safety with the Restore Selected/All gating already added in Plan 07-11.
4. Tests prove traversal/sibling-prefix/null-`_backupRoot` rejection plus normal contained deletion at both the service layer and the ViewModel layer.
- [x] **T14: 07-backup-restore-retention-safety 14** `est:4 min`
  - Extract a single shared path-containment helper used by both `BackupService` (restore target containment) and (in Plan 07-13) `RestoreViewModel` (Delete Session containment). This closes the architectural debt the cross-AI reviewers (Gemini "consistent normalization", the agent + Codex "helper duplication") flagged in their consensus review of Plans 07-09, 07-11, and 07-13.

Purpose: SAF-04 and TEST-04 require restore/backup management safety. The duplication of normalization-with-trailing-separator + `Path.GetFullPath` + ordinal-ignore-case `StartsWith` + try/catch for the same exception types in multiple files (`BackupService.IsRestoreTargetInsideTrustedRoot` and the previously planned `RestoreViewModel.IsSessionDirectoryInsideBackupRoot`) is a real maintenance hazard that risks divergent containment behavior between backup-creation, restore-target, and delete-session paths.

Output: A new internal static class `BackupPathContainment` with `IsContained(string? candidatePath, string? rootPath)` that encapsulates the canonical containment policy, plus a refactored `BackupService.IsRestoreTargetInsideTrustedRoot` that delegates to it. The helper is consumed by Plan 07-13 to avoid duplicating containment logic in the ViewModel layer.

## Files Likely Touched

- `AutoQAC/Models/BackupOperationResults.cs`
- `AutoQAC/Services/Backup/IBackupFileCopier.cs`
- `AutoQAC/Services/Backup/BackupFileCopier.cs`
- `AutoQAC/Services/Backup/BackupCopyOptions.cs`
- `AutoQAC/Services/Backup/IBackupService.cs`
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC.Tests/Services/BackupFileCopierTests.cs`
- `AutoQAC.Tests/Models/BackupOperationResultTests.cs`
- `AutoQAC/Services/Backup/BackupService.cs`
- `AutoQAC/Services/Backup/IBackupService.cs`
- `AutoQAC/Services/Backup/IBackupSessionDeleter.cs`
- `AutoQAC/Services/Backup/DirectoryBackupSessionDeleter.cs`
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC.Tests/Services/BackupServiceTests.cs`
- `AutoQAC/Models/AppState.cs`
- `AutoQAC/Models/CleaningSessionResult.cs`
- `AutoQAC/Services/State/IStateService.cs`
- `AutoQAC/Services/State/StateService.cs`
- `AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs`
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`
- `AutoQAC/ViewModels/RestoreViewModel.cs`
- `AutoQAC/Views/RestoreWindow.axaml`
- `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs`
- `AutoQAC/ViewModels/ProgressViewModel.cs`
- `AutoQAC/Views/ProgressWindow.axaml`
- `AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs`
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`
- `AutoQAC/Services/Backup/BackupService.cs`
- `AutoQAC.Tests/Services/BackupServiceTests.cs`
- `AutoQAC.Tests/Services/BackupFileCopierTests.cs`
- `AutoQAC/ViewModels/RestoreViewModel.cs`
- `AutoQAC/Views/MainWindow.axaml.cs`
- `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs`
- `AutoQAC/Services/Backup/BackupService.cs`
- `AutoQAC.Tests/Services/BackupServiceTests.cs`
- `.planning/phases/07-backup-restore-retention-safety/07-08-SUMMARY.md`
- `AutoQAC/Services/Backup/BackupService.cs`
- `AutoQAC/ViewModels/RestoreViewModel.cs`
- `AutoQAC.Tests/Services/BackupServiceTests.cs`
- `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs`
- `AutoQAC/Services/Backup/BackupFileCopier.cs`
- `AutoQAC.Tests/Services/BackupFileCopierTests.cs`
- `AutoQAC/Services/Backup/IBackupService.cs`
- `AutoQAC/Services/Backup/BackupService.cs`
- `AutoQAC/ViewModels/RestoreViewModel.cs`
- `AutoQAC/Views/MainWindow.axaml.cs`
- `AutoQAC.Tests/Services/BackupServiceTests.cs`
- `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs`
- `AutoQAC/Views/MainWindow.axaml.cs`
- `AutoQAC.Tests/Views/ViewSubscriptionLifecycleTests.cs`
- `AutoQAC/Services/Backup/IBackupService.cs`
- `AutoQAC/Services/Backup/BackupService.cs`
- `AutoQAC/Models/BackupOperationResults.cs`
- `AutoQAC/ViewModels/RestoreViewModel.cs`
- `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs`
- `AutoQAC.Tests/Services/BackupServiceTests.cs`
- `AutoQAC/Services/Backup/BackupPathContainment.cs`
- `AutoQAC/Services/Backup/BackupService.cs`
- `AutoQAC.Tests/Services/Backup/BackupPathContainmentTests.cs`
