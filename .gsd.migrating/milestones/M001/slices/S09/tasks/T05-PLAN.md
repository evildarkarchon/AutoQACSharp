# T05: 08-cleaning-orchestrator-decomposition 05

**Slice:** S09 — **Milestone:** M001

## Description

Extract per-plugin work into runner + finalizer (D-07). Runner owns retry/launch/offset capture; finalizer owns log read + result construction. Both depend on already-extracted preflight/termination via delegate handoff (Research A3) — no back-edge to termination coordinator.

Purpose: Fourth extraction. Bottom of dependency graph. After this, the orchestrator's foreach body shrinks to: state-update → backupCoordinator.RunPluginBackupAsync → dispatch → runner.RunAsync → terminationCoordinator.DetachProcess → snapshot → finalizer.FinalizeAsync → state.AddDetailedCleaningResult.

Note on lambda hoisting (per R-04): Wave 3 (plan 08-04) keeps the orchestrator's existing inline foreach lambda — it just rewrites the lambda body to call `terminationCoordinator.AttachProcess(proc)` instead of mutating private fields. THIS plan (Wave 4) is where the lambda→delegate parameter conversion happens, by hoisting it into the runner via `attachProcess`/`detachProcess` parameters. Same code is extracted ONCE, not twice.

Output: 5 new production files (+1 model file extension to add `XEditDirectory`), 2 new test files, modified facade, modified DI.

## Must-Haves

- [ ] "IPluginCleaningRunner owns the per-plugin retry loop, log-offset capture, and xEdit launch (D-07 runner half)."
- [ ] "IPluginResultFinalizer is a pure function over runner output + termination context — owns log read + parse + result construction (D-07 finalizer half)."
- [ ] "Runner takes attachProcess/detachProcess delegates, NOT direct ICleaningTerminationCoordinator dependency (Research recommendation A3). The lambda→delegate hoist into the runner happens ONCE in this Wave 4 plan; Wave 3 (08-04) keeps the inline foreach lambda calling terminationCoordinator.AttachProcess directly per R-04."
- [ ] "Log offset capture occurs INSIDE the retry loop, BEFORE each xEdit launch (D-20 protected ordering at CleaningOrchestrator.cs:386-440)."
- [ ] "maxRetryAttempts is the literal constant `3` (matches CleaningOrchestrator.cs:57: `const int maxRetryAttempts = 3;`). Per R-01: changing to 2 would silently change user-visible retry behavior — D-09/D-12 violation."
- [ ] "detachProcess is called exactly once per plugin in a `finally` block AFTER the retry loop, matching CleaningOrchestrator.cs:442-454. attachProcess is called per attempt by CleaningService.CleanPluginAsync via the onProcessStarted callback. Per R-02: per-attempt detach would create observable behavior differences and violate D-09."
- [ ] "CleaningPreflightPlan exposes XEditDirectory (single source of truth for the runner's offset capture and the finalizer's log read — eliminates re-derivation in the facade per R-08)."
- [ ] "Phase 6 launch argv intent preserved — runner calls cleaningService.CleanPluginAsync exactly as today; no command construction in runner."
- [ ] "Phase 5 no-log-after-unsafe-termination invariant preserved: finalizer's `if (!terminationContext.ProcessMayStillBeRunning && !terminationContext.StopWasRequested && result.Status != Skipped)` guard at CleaningOrchestrator.cs:461."
- [ ] "ICleaningOrchestrator public surface unchanged (Wave 0 snapshot test green)."
- [ ] "Sequential xEdit cleaning preserved — runner does not parallelize, ProcessExecutionService single-slot stays in path."
- [ ] "D-11 honored — if a non-REF-01 bug is found during extraction, executor MUST stop and ask, not silently fix."

## Files

- `AutoQAC/Services/Cleaning/IPluginCleaningRunner.cs`
- `AutoQAC/Services/Cleaning/PluginCleaningRunner.cs`
- `AutoQAC/Services/Cleaning/IPluginResultFinalizer.cs`
- `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs`
- `AutoQAC/Services/Cleaning/RunnerFinalizerModels.cs`
- `AutoQAC/Services/Cleaning/CleaningPreflightModels.cs`
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC.Tests/Services/Cleaning/PluginCleaningRunnerTests.cs`
- `AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs`
