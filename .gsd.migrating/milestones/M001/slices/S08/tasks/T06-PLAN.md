# T06: 07-backup-restore-retention-safety 06

**Slice:** S08 — **Milestone:** M001

## Description

Close the verification gap where backup failure decisions can leave cleaning progress/session reporting incomplete.

Purpose: Phase 7 cannot satisfy clear backup failure reporting if SkipPlugin silently drops a plugin result or AbortSession exits before publishing a final session result.
Output: Production fixes plus targeted regression tests for `BackupFailureChoice.SkipPlugin` and `BackupFailureChoice.AbortSession`.

## Must-Haves

- [ ] "BackupFailureChoice.SkipPlugin creates a skipped PluginCleaningResult, publishes it via AddDetailedCleaningResult, and keeps session/progress accounting complete."
- [ ] "BackupFailureChoice.AbortSession writes partial metadata when present and finalizes through FinishCleaningWithResults before StartCleaningAsync returns."

## Files

- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`
