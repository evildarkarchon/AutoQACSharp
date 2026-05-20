# T01: 12-process-stop-verification-progress-flow-closure 01

**Slice:** S13 — **Milestone:** M001

## Description

Create the shared stop outcome text and dialog-label contract used by every Stop surface.

Purpose: Phase 12 decisions D-01 through D-04 require main Stop and Progress Stop to share exact, Phase 11-safe copy and explicit `Force Terminate` / `Leave Running` action labels so the two surfaces cannot drift.
Output: Shared stop copy constants, a narrow dialog service custom-choice API, updated main Stop caller, and tests proving the main Stop path uses the shared contract.

## Must-Haves

- [ ] "D-01/D-02: Main Stop and Progress Stop have one shared exact text contract for confirmation, force-failure, and leave-running outcomes."
- [ ] "D-03: Stop confirmation presents action labels exactly as Force Terminate and Leave Running, not generic Yes/No."
- [ ] "D-04: Stop outcome copy is Phase 11-safe and excludes raw exceptions, stack traces, full paths, and command lines."
- [ ] "D-08: Main Stop status and dialogs use the shared outcome language so they do not contradict Progress Stop wording."

## Files

- `AutoQAC/Models/StopTerminationDialogContent.cs`
- `AutoQAC/Services/UI/IMessageDialogService.cs`
- `AutoQAC/Services/UI/MessageDialogService.cs`
- `AutoQAC/ViewModels/MessageDialogViewModel.cs`
- `AutoQAC/Views/MessageDialog.axaml`
- `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`
- `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs`
