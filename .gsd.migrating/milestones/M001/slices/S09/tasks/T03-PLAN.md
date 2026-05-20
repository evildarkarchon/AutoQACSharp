# T03: 08-cleaning-orchestrator-decomposition 03

**Slice:** S09 — **Milestone:** M001

## Description

Extract backup-session lifecycle into `IBackupSessionCoordinator`/`BackupSessionCoordinator` (D-06). Coordinator owns `_backupOperationCts`, `_backupOperationLock`, the `BackupPluginAsync` and `CleanupOldSessionsAsync` private helpers, the backup-failure choice switch, and metadata writes. Facade dispatches on a `PluginBackupOutcome` enum.

Purpose: Second extraction. Already-isolated helpers (BackupPluginAsync, CleanupOldSessionsAsync) move atomically. Phase 7 invariants (backup cancellation, retention, MO2 skip) are preserved by lifting code verbatim.

Output: 3 new production files, 1 new test file, modified facade, modified DI. ~300 more lines removed from `CleaningOrchestrator.cs`.

## Must-Haves

- [ ] "IBackupSessionCoordinator owns BeginSessionAsync, RunPluginBackupAsync, FinalizeSessionAsync, WritePartialMetadataAsync, CancelActiveOperationAsync (D-06)."
- [ ] "Phase 7 backup cancellation, restore/retention warning/canceled semantics preserved — MO2 backup-skip preserved."
- [ ] "Phase 7 D-Phase07 lock preserved: state.SetBackupOperation/ClearBackupOperation called on the same boundaries as today (set before await, cleared in finally)."
- [ ] "PluginBackupOutcome enum surfaces all 5 outcomes (Succeeded, Canceled, UserSkipped, AbortSession, ContinueWithoutBackup); facade dispatches on this without re-implementing backup-failure logic."
- [ ] "Facade no longer references _backupOperationCts or _backupOperationLock directly — backup CTS lifecycle is owned by the coordinator."
- [ ] "AbortSession control stays with the facade: coordinator returns the outcome; facade calls WritePartialMetadataAsync + builds CleaningSessionResult."
- [ ] "Per R-09: AbortSession facade dispatch is the FULL sequence from CleaningOrchestrator.cs:333-362 verbatim — WritePartialMetadataAsync → wasCancelled = true → CleaningSessionResult { ... } → stateService.FinishCleaningWithResults(sessionResult) → LogSessionSummary(sessionResult) → return. None of these steps may be omitted (LogSessionSummary in particular is easy to drop and would silently change observable session-end logging)."
- [ ] "ICleaningOrchestrator public surface unchanged (Wave 0 snapshot test green)."
- [ ] "Sequential xEdit cleaning preserved — `ProcessExecutionService` single-slot semaphore stays in the launch path."
- [ ] "D-11 honored — if a non-REF-01 bug is found during extraction, executor MUST stop and ask, not silently fix."

## Files

- `AutoQAC/Services/Cleaning/IBackupSessionCoordinator.cs`
- `AutoQAC/Services/Cleaning/BackupSessionCoordinator.cs`
- `AutoQAC/Services/Cleaning/BackupSessionModels.cs`
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC.Tests/Services/Cleaning/BackupSessionCoordinatorTests.cs`
