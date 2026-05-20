# T03: 07-backup-restore-retention-safety 03

**Slice:** S08 — **Milestone:** M001

## Description

Wire structured backup and retention operations into the sequential cleaning session without changing xEdit launch ordering.

Purpose: Users need visible, cancellable backup/retention work, and retention warnings must be part of the final session outcome rather than hidden log-only cleanup.
Output: State/orchestrator support for backup and retention progress, separate cancellation, retention warnings/cancel outcomes, and integration tests.

## Must-Haves

- [ ] "D-05/D-06/D-09: cleaning backups remain per-plugin immediately before xEdit launch and publish progress through existing cleaning state."
- [ ] "D-07/D-08: canceling backup copy cancels only the active non-xEdit file operation, prevents that plugin's xEdit launch, deletes the partial backup file, and does not trigger xEdit stop/kill behavior."
- [ ] "D-12/D-13/D-16: retention completes, warns, or cancels before final session completion is emitted."

## Files

- `AutoQAC/Models/AppState.cs`
- `AutoQAC/Models/CleaningSessionResult.cs`
- `AutoQAC/Services/State/IStateService.cs`
- `AutoQAC/Services/State/StateService.cs`
- `AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs`
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`
