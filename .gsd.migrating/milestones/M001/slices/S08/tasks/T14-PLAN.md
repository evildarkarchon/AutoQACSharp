# T14: 07-backup-restore-retention-safety 14

**Slice:** S08 — **Milestone:** M001

## Description

Extract a single shared path-containment helper used by both `BackupService` (restore target containment) and (in Plan 07-13) `RestoreViewModel` (Delete Session containment). This closes the architectural debt the cross-AI reviewers (Gemini "consistent normalization", the agent + Codex "helper duplication") flagged in their consensus review of Plans 07-09, 07-11, and 07-13.

Purpose: SAF-04 and TEST-04 require restore/backup management safety. The duplication of normalization-with-trailing-separator + `Path.GetFullPath` + ordinal-ignore-case `StartsWith` + try/catch for the same exception types in multiple files (`BackupService.IsRestoreTargetInsideTrustedRoot` and the previously planned `RestoreViewModel.IsSessionDirectoryInsideBackupRoot`) is a real maintenance hazard that risks divergent containment behavior between backup-creation, restore-target, and delete-session paths.

Output: A new internal static class `BackupPathContainment` with `IsContained(string? candidatePath, string? rootPath)` that encapsulates the canonical containment policy, plus a refactored `BackupService.IsRestoreTargetInsideTrustedRoot` that delegates to it. The helper is consumed by Plan 07-13 to avoid duplicating containment logic in the ViewModel layer.

## Must-Haves

- [ ] "A single shared helper, `BackupPathContainment.IsContained(string? candidatePath, string? rootPath)`, owns string-level containment validation for backup/restore/delete safety boundaries."
- [ ] "BackupService.IsRestoreTargetInsideTrustedRoot delegates to BackupPathContainment.IsContained instead of duplicating normalization, trailing-separator, or try/catch logic."
- [ ] "The shared helper rejects null/whitespace inputs, traversal-normalized paths that escape the root, sibling-prefix collisions, and malformed paths that throw ArgumentException/IOException/NotSupportedException/UnauthorizedAccessException."
- [ ] "All trusted-restore-root tests added in Plan 07-11 (sibling-prefix, missing-root, Data2 collision) continue to pass after the refactor."
- [ ] "Sequential xEdit cleaning invariant remains protected — no `Task.WhenAll`, `Parallel.ForEachAsync`, or `Task.Run` is added under `AutoQAC/Services/Cleaning` by this plan."

## Files

- `AutoQAC/Services/Backup/BackupPathContainment.cs`
- `AutoQAC/Services/Backup/BackupService.cs`
- `AutoQAC.Tests/Services/Backup/BackupPathContainmentTests.cs`
