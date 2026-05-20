# T09: 08-cleaning-orchestrator-decomposition 09

**Slice:** S09 — **Milestone:** M001

## Description

Close Phase 8 verification gap 1: concurrent public `StartCleaningAsync` calls must not overlap session orchestration or overwrite the active session CTS.

Purpose: preserve sequential cleaning/session state invariants from D-02, D-09, D-10, and REF-01 after the facade decomposition.
Output: one failing-then-passing regression test and a non-blocking active-session guard in `CleaningOrchestrator`.

## Must-Haves

- [ ] "A second public StartCleaningAsync call cannot overlap an active cleaning session."
- [ ] "The first active session CTS remains the token canceled by StopCleaningAsync after a rejected concurrent start."
- [ ] "No second cleaning session publishes StartCleaning, reaches preflight completion, or launches xEdit."

## Files

- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`
