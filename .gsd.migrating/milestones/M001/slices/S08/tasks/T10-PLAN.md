# T10: 07-backup-restore-retention-safety 10

**Slice:** S08 — **Milestone:** M001

## Description

Close the verification gap where `BackupFileCopier.CopyAsync` can delete a pre-existing create-new backup destination when `FileMode.CreateNew` fails before this copy attempt creates output.

Purpose: Backup creation must be fail-safe; an attempted backup must not remove an older backup file at the same destination.
Output: Ownership-aware partial cleanup in `BackupFileCopier` plus a failing-then-passing regression test.

## Must-Haves

- [ ] "Create-new backup copy failures never delete an existing destination backup file."
- [ ] "Canceled create-new copies still delete only the partial file created by the active copy attempt."
- [ ] "Atomic restore temp-file cleanup still deletes the attempt-owned temp file on cancellation or failed incomplete copy."

## Files

- `AutoQAC/Services/Backup/BackupFileCopier.cs`
- `AutoQAC.Tests/Services/BackupFileCopierTests.cs`
