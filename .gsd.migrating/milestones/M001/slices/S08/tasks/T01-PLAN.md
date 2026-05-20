# T01: 07-backup-restore-retention-safety 01

**Slice:** S08 — **Milestone:** M001

## Description

Create the shared backup/restore/retention contracts and cancellable file-copy foundation that all later Phase 7 plans build on.

Purpose: Phase 7 cannot safely report partial restore, retention warning, or copy cancellation outcomes while service APIs only throw or return a boolean-style backup result.
Output: Result records/enums, injectable copier, DI registration, and targeted tests proving cancellation deletes partial files.

## Must-Haves

- [ ] "D-03/D-05/D-07/D-08: backup and restore file-copy work reports Complete, Partial, Failed, Canceled, and progress-ready outcomes."
- [ ] "D-04: user-facing failure reasons are constrained to concise row-level labels, not raw exception text."
- [ ] "D-07/D-08/D-10: active copy cancellation removes only the in-progress output/temp file, preserves existing restore targets, and reports byte progress when file size is available."

## Files

- `AutoQAC/Models/BackupOperationResults.cs`
- `AutoQAC/Services/Backup/IBackupFileCopier.cs`
- `AutoQAC/Services/Backup/BackupFileCopier.cs`
- `AutoQAC/Services/Backup/BackupCopyOptions.cs`
- `AutoQAC/Services/Backup/IBackupService.cs`
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC.Tests/Services/BackupFileCopierTests.cs`
- `AutoQAC.Tests/Models/BackupOperationResultTests.cs`
