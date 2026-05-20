# S13: Process Stop Verification Progress Flow Closure

**Goal:** Create the shared stop outcome text and dialog-label contract used by every Stop surface.
**Demo:** Create the shared stop outcome text and dialog-label contract used by every Stop surface.

## Must-Haves


## Tasks

- [x] **T01: 12-process-stop-verification-progress-flow-closure 01** `est:5 min`
  - Create the shared stop outcome text and dialog-label contract used by every Stop surface.

Purpose: Phase 12 decisions D-01 through D-04 require main Stop and Progress Stop to share exact, Phase 11-safe copy and explicit `Force Terminate` / `Leave Running` action labels so the two surfaces cannot drift.
Output: Shared stop copy constants, a narrow dialog service custom-choice API, updated main Stop caller, and tests proving the main Stop path uses the shared contract.
- [x] **T02: 12-process-stop-verification-progress-flow-closure 02** `est:5 min`
  - Wire the Progress-window Stop and Hang Kill surfaces through the shared stop outcome path.

Purpose: Close audit gaps `INT-01` and `FLOW-01` by making Progress Stop reach the same confirmed two-stage termination outcomes as main Stop, while preserving immediate Hang Kill behavior.
Output: `ProgressViewModel` stop outcome handling, persistent result-summary warning binding, constructor wiring, and targeted Progress ViewModel tests.
- [x] **T03: 12-process-stop-verification-progress-flow-closure 03** `est:3 min`
  - Run targeted evidence checks and create the Phase 12 verification artifact that closes the audit gaps.

Purpose: Phase 12 is not complete until it proves `SAF-01`, `SAF-02`, `REF-04`, and `TEST-01` with current tests and documents closure of `INT-01` and `FLOW-01` without rewriting Phase 5 artifacts.
Output: `12-VERIFICATION.md` plus validation status updates based on actual command results.

## Files Likely Touched

- `AutoQAC/Models/StopTerminationDialogContent.cs`
- `AutoQAC/Services/UI/IMessageDialogService.cs`
- `AutoQAC/Services/UI/MessageDialogService.cs`
- `AutoQAC/ViewModels/MessageDialogViewModel.cs`
- `AutoQAC/Views/MessageDialog.axaml`
- `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`
- `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs`
- `AutoQAC/ViewModels/ProgressViewModel.cs`
- `AutoQAC/Views/ProgressWindow.axaml`
- `AutoQAC/Views/MainWindow.axaml.cs`
- `AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs`
- `.planning/phases/12-process-stop-verification-progress-flow-closure/12-VERIFICATION.md`
- `.planning/phases/12-process-stop-verification-progress-flow-closure/12-VALIDATION.md`
