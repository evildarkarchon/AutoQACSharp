# T04: 07-backup-restore-retention-safety 04

**Slice:** S08 — **Milestone:** M001

## Description

Implement RestoreWindow confirmation, progress, cancellation, and inline result reporting against the structured restore APIs.

Purpose: Restore safety is user-facing; users need overwrite confirmation and inspectable complete/partial/failed/canceled rows without raw technical details.
Output: Restore ViewModel/window changes plus ViewModel tests.

## Must-Haves

- [ ] "D-17/D-18: Restore Selected and Restore All require overwrite confirmation with session timestamp and plugin count/name."
- [ ] "D-19/D-20: restore results stay inline in the restore window for success, partial, failed, and canceled outcomes without extra success popups or auto-close."
- [ ] "D-07/D-09/D-11: restore copy progress and Cancel Restore are visible in the restore window."

## Files

- `AutoQAC/ViewModels/RestoreViewModel.cs`
- `AutoQAC/Views/RestoreWindow.axaml`
- `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs`
