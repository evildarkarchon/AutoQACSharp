# T08: 08-cleaning-orchestrator-decomposition 08

**Slice:** S09 — **Milestone:** M001

## Description

Close gaps CR-02 (Blocker) and WR-01 (Warning) in `PluginResultFinalizer.cs`:

- CR-02: Stop converting failed xEdit runs into AlreadyClean. The "completion line + zero stats" promotion must require a SUCCESSFUL cleaned runner result.
- WR-01: Stop returning `Status=Failed, Success=true` for exception-log failures. Derive the returned `Success` from the FINAL status after log-parse overrides.

These two fixes share the same file and would collide on the same return statement, so they are landed together in one plan.

Purpose: Restore behavior preservation truth #4 and #8 from `08-VERIFICATION.md` — "Result finalization preserves failed and exception-log outcomes without reclassifying failures as successful/AlreadyClean."

Output: Updated `PluginResultFinalizer.cs` with the AlreadyClean gate tightened and `Success` derived from `finalStatus`, plus three unit tests in `PluginResultFinalizerTests.cs` (two RED reproducers and one skipped-path characterization).

## Must-Haves

- [ ] "Result finalization preserves failed and exception-log outcomes without reclassifying failures as successful/AlreadyClean."
- [ ] "AlreadyClean reclassification is gated on a successful, cleaned runner result — failed runner results MUST stay Failed even when a completion line + zero stats are present in the log."
- [ ] "PluginCleaningResult.Success is derived from the FINAL status after log-parse overrides — exception-log failures cannot return Status=Failed with Success=true. Addresses review concern: finalizer tests must cover status/success cross-products."
- [ ] "Skipped results remain a non-success short-circuit after finalSuccess derivation; no log read occurs for skipped rows. Addresses review concern: characterize Skipped path under the new Success derivation."
- [ ] "Existing finalizer tests (termination skip, already-clean success path, stop-was-requested) remain green."
- [ ] "Sequential cleaning preserved; ICleaningOrchestrator public surface unchanged."

## Files

- `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs`
- `AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs`
