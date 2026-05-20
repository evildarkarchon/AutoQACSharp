# T04: 15-stop-escalation-ownership-closure 04

**Slice:** S16 — **Milestone:** M001

## Description

Close the Phase 15 verification gaps by proving the confirmed force-stop path no longer depends on a disposed `ProcessExecutionService.ExecuteAsync` wrapper and by replacing the stale `gaps_found` evidence with current passed verification.

Purpose: Phase 15 is not complete until the exact verifier gap is closed: after `GracePeriodExpired`, process execution may return and dispose its wrapper before the user confirms `Force Terminate`. The implementation must use stable target identity or safely fail, and the verification artifact must reflect that proof.
Output: Durable pending-target source/test proof plus updated `15-VERIFICATION.md` and `15-VALIDATION.md`.

## Must-Haves

- [ ] "SAF-01 / D-01-D-06: Progress-window Stop retains stable force-termination identity after GracePeriodExpired, runner detach, finalization, and ProcessExecutionService-owned handle disposal."
- [ ] "SAF-02 / D-08-D-09: confirmed force escalation returns ForceKillFailed when the pending target cannot be safely reopened or verified, so ProgressViewModel can show the shared safe warning."
- [ ] "TEST-01 / D-10-D-13: automated regression coverage exercises the production ownership boundary by disposing the original process wrapper before confirmed ForceStopCleaningAsync."
- [ ] "D-15-D-18: 15-VERIFICATION.md supersedes the stale gaps_found report with passed evidence rows for SAF-01, SAF-02, TEST-01, INT-STOP-01, and FLOW-STOP-ESCALATION-01 without performing Phase 16 marker reconciliation."

## Files

- `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs`
- `AutoQAC.Tests/Services/Cleaning/CleaningTerminationCoordinatorTests.cs`
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`
- `.planning/phases/15-stop-escalation-ownership-closure/15-VERIFICATION.md`
- `.planning/phases/15-stop-escalation-ownership-closure/15-VALIDATION.md`
