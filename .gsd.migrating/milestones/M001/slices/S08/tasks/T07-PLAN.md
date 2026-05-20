# T07: 07-backup-restore-retention-safety 07

**Slice:** S08 — **Milestone:** M001

## Description

Close backup-service verification gaps for unsafe restore metadata, hollow retention cleanup progress, and missing TEST-04 failure coverage.

Purpose: Phase 7 restore and retention work must be safe when reading backup metadata, visibly progressing during cleanup, and test-covered for permission and deletion-failure outcomes.
Output: Backup service validation/progress fixes plus regression tests for traversal, absolute backup filenames, unsafe restore targets, access-denied/write failure mapping, and cleanup deletion warning after retry.

## Must-Haves

- [ ] "Restore rejects unsafe backup metadata FileName values before copying."
- [ ] "Restore rejects unsafe OriginalPath targets that are not rooted under an approved restore root policy."
- [ ] "Retention cleanup reports count progress beyond the initial static band."
- [ ] "TEST-04 includes permission/access-denied and cleanup deletion failure after retry coverage."

## Files

- `AutoQAC/Services/Backup/BackupService.cs`
- `AutoQAC.Tests/Services/BackupServiceTests.cs`
- `AutoQAC.Tests/Services/BackupFileCopierTests.cs`
