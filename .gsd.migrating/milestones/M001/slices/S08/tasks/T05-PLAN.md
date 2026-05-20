# T05: 07-backup-restore-retention-safety 05

**Slice:** S08 — **Milestone:** M001

## Description

Complete cleaning progress UI integration and final Phase 7 automated verification.

Purpose: The service layer can produce backup/retention progress only if the existing progress window shows it clearly with non-xEdit cancel controls and final warning/canceled summaries.
Output: ProgressViewModel/window bindings, tests, and full solution verification.

## Must-Haves

- [ ] "D-09/D-11: backup and retention progress appear in the existing cleaning progress surface with separate Cancel Backup/Cancel Cleanup buttons."
- [ ] "D-10: progress surfaces show file counts and byte progress when available."
- [ ] "D-12/D-13/D-16: final cleaning results show retention warning/canceled summaries before the phase is considered complete."

## Files

- `AutoQAC/ViewModels/ProgressViewModel.cs`
- `AutoQAC/Views/ProgressWindow.axaml`
- `AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs`
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`
