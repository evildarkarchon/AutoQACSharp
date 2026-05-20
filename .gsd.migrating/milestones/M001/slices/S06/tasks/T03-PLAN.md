# T03: 05-process-stop-pid-safety 03

**Slice:** S06 — **Milestone:** M001

## Description

Wire the corrected process termination semantics into user-visible stop behavior.

Purpose: Phase 5 is only complete if users see the explicit confirmation, decline, second-click escalation, and force-kill failure outcomes described in the context and UI contract.
Output: Orchestrator/ViewModel state-machine updates and tests proving prompt ordering, decline, second stop, failure reporting, and unsafe log-read blocking.

## Must-Haves

- [ ] "First Stop shows the confirmation path before force-kill is requested (D-01, D-02, SAF-01)."
- [ ] "Declining the force-terminate prompt leaves xEdit running and reports that choice clearly (D-03)."
- [ ] "Second Stop during the grace/confirmation path force-kills immediately without another prompt (D-04)."
- [ ] "Force-kill failure is user-visible, concise, logged in detail, and blocks post-exit log parsing (D-08, D-09, SAF-02)."
- [ ] "Stop/force-stop APIs return structured outcomes so the ViewModel does not rely only on a fragile mutable side channel (review consensus)."

## Files

- `AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs`
- `AutoQAC/Services/Cleaning/StopCleaningResult.cs`
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`
- `AutoQAC.Tests/ViewModels/CleaningCommandsViewModelTests.cs`
