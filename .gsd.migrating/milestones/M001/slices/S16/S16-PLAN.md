# S16: Stop Escalation Ownership Closure

**Goal:** Create the coordinator-owned retained process handle required for confirmed detached force escalation.
**Demo:** Create the coordinator-owned retained process handle required for confirmed detached force escalation.

## Must-Haves


## Tasks

- [x] **T01: 15-stop-escalation-ownership-closure 01** `est:5 min`
  - Create the coordinator-owned retained process handle required for confirmed detached force escalation.

Purpose: Phase 15 must close the audit blocker where `DetachProcess()` clears `_currentProcess` and later confirmed `ForceStopAsync()` silently returns cached `GracePeriodExpired` instead of killing or reporting failure.
Output: TDD tests and implementation in the termination coordinator that keep active cleaning semantics separate from pending force-escalation ownership.
- [x] **T02: 15-stop-escalation-ownership-closure 02** `est:18 min`
  - Wire the retained coordinator target through session finalization and preserve the Progress-window confirmation/failure proof.

Purpose: Plan 01 fixes the coordinator mechanism; this plan ensures `CleaningOrchestrator` does not erase unresolved pending escalation during normal finalization and that Progress Stop still gates force stop behind user confirmation.
Output: TDD tests and minimal orchestration/ViewModel-adjacent verification for the detached escalation user flow.
- [x] **T03: 15-stop-escalation-ownership-closure 03** `est:4 min`
  - Produce the Phase 15 verification and validation evidence required to close SAF-01, SAF-02, TEST-01, INT-STOP-01, and FLOW-STOP-ESCALATION-01.

Purpose: The milestone audit cannot trust the stop-escalation flow until current evidence maps the retained ownership fix and Progress confirmation/failure behavior to the pending requirements and audit gaps.
Output: `15-VERIFICATION.md` and updated `15-VALIDATION.md` with command rows and requirement mapping.
- [x] **T04: 15-stop-escalation-ownership-closure 04** `est:5 min`
  - Close the Phase 15 verification gaps by proving the confirmed force-stop path no longer depends on a disposed `ProcessExecutionService.ExecuteAsync` wrapper and by replacing the stale `gaps_found` evidence with current passed verification.

Purpose: Phase 15 is not complete until the exact verifier gap is closed: after `GracePeriodExpired`, process execution may return and dispose its wrapper before the user confirms `Force Terminate`. The implementation must use stable target identity or safely fail, and the verification artifact must reflect that proof.
Output: Durable pending-target source/test proof plus updated `15-VERIFICATION.md` and `15-VALIDATION.md`.

## Files Likely Touched

- `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs`
- `AutoQAC/Services/Cleaning/ICleaningTerminationCoordinator.cs`
- `AutoQAC.Tests/Services/Cleaning/CleaningTerminationCoordinatorTests.cs`
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- `AutoQAC/Services/Cleaning/ICleaningTerminationCoordinator.cs`
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`
- `AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs`
- `.planning/phases/15-stop-escalation-ownership-closure/15-VERIFICATION.md`
- `.planning/phases/15-stop-escalation-ownership-closure/15-VALIDATION.md`
- `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs`
- `AutoQAC.Tests/Services/Cleaning/CleaningTerminationCoordinatorTests.cs`
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`
- `.planning/phases/15-stop-escalation-ownership-closure/15-VERIFICATION.md`
- `.planning/phases/15-stop-escalation-ownership-closure/15-VALIDATION.md`
