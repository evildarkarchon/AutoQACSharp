# T02: 12-process-stop-verification-progress-flow-closure 02

**Slice:** S13 — **Milestone:** M001

## Description

Wire the Progress-window Stop and Hang Kill surfaces through the shared stop outcome path.

Purpose: Close audit gaps `INT-01` and `FLOW-01` by making Progress Stop reach the same confirmed two-stage termination outcomes as main Stop, while preserving immediate Hang Kill behavior.
Output: `ProgressViewModel` stop outcome handling, persistent result-summary warning binding, constructor wiring, and targeted Progress ViewModel tests.

## Must-Haves

- [ ] "D-05: Declining Progress Stop force termination marks xEdit left running and leaves a persistent result-summary warning."
- [ ] "D-06/D-07: Progress Stop force-kill failure shows the shared failure dialog and a persistent result-summary warning near the result summary."
- [ ] "D-09/D-12: Hang warning Kill remains immediate, skips confirmation, and reports ForceKillFailed through the shared failure path."
- [ ] "D-10: Hang warning Kill ForceKillFailed uses the same shared force-failure dialog and persistent warning as confirmed Progress Stop force failure."
- [ ] "D-11: Hang warning Kill success adds no new success copy; the normal cancelled/results flow remains sufficient."

## Files

- `AutoQAC/ViewModels/ProgressViewModel.cs`
- `AutoQAC/Views/ProgressWindow.axaml`
- `AutoQAC/Views/MainWindow.axaml.cs`
- `AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs`
