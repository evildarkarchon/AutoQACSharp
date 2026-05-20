# T02: 07-backup-restore-retention-safety 02

**Slice:** S08 — **Milestone:** M001

## Description

Implement the async structured restore and retention service APIs using the contracts from Plan 07-01.

Purpose: Restore safety and retention cleanup must become recoverable batch operations instead of thrown exceptions and logging-only cleanup failures.
Output: `BackupService` async behavior plus expanded service tests for restore and retention safety.

## Must-Haves

- [ ] "D-01/D-03: Restore All continues past individual plugin failures and reports Complete, Partial, Failed, or Canceled with counts and failed plugin names."
- [ ] "D-02/D-04: missing target folders are recreated when possible; creation/write failures become concise per-plugin failure reasons."
- [ ] "D-14/D-15/D-16: retention never deletes the current session, keeps newest MaxSessions, retries deletion once, and reports canceled/deletion-failure counts."

## Files

- `AutoQAC/Services/Backup/BackupService.cs`
- `AutoQAC/Services/Backup/IBackupService.cs`
- `AutoQAC/Services/Backup/IBackupSessionDeleter.cs`
- `AutoQAC/Services/Backup/DirectoryBackupSessionDeleter.cs`
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC.Tests/Services/BackupServiceTests.cs`
