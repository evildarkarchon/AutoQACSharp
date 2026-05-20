# T07: 08-cleaning-orchestrator-decomposition 07

**Slice:** S09 — **Milestone:** M001

## Description

Close gap CR-01 (Blocker): A user Stop click during the startup window (orphan cleanup or preflight) is silently dropped because the session CTS is created AFTER preflight returns. After the fix, calling `StopCleaningAsync` during preflight cancels the session before any plugin enters the cleaning loop, and no xEdit launch occurs.

Purpose: Restore behavior preservation truth #4 and #8 from `08-VERIFICATION.md` — "User-observable stopped behavior remains unchanged across startup/preflight and plugin execution paths."

Output: Updated `CleaningOrchestrator.cs` with session CTS created early + threaded into orphan cleanup and preflight, plus a new regression test in `CleaningOrchestratorTests.cs` proving Stop wins the race against preflight.

## Must-Haves

- [ ] "User-observable stopped behavior remains unchanged across startup/preflight and plugin execution paths."
- [ ] "A user Stop click during CleanOrphanedProcessesAsync or preflight.PrepareAsync cancels the session before any xEdit launch can occur. Addresses review concern: startup-window cancellation coverage must include both orphan cleanup and preflight."
- [ ] "ICleaningOrchestrator public surface remains identical (10 members) — ICleaningOrchestrator_PublicSurface_MatchesLockedSnapshot stays green."
- [ ] "Sequential cleaning is preserved — no Parallel/Task.WhenAll/Task.Run is introduced."
- [ ] "All pre-existing CleaningOrchestratorTests still pass after the fix."

## Files

- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`
