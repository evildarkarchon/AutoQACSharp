# T02: 08-cleaning-orchestrator-decomposition 02

**Slice:** S09 — **Milestone:** M001

## Description

Extract preflight/selection logic into `ICleaningPreflight`/`CleaningPreflight` (D-13–D-16) and wire it into the facade. Both `StartCleaningAsync` and `RunDryRunAsync` consume the same plan, eliminating dry-run/real-run drift.

Purpose: First extraction — preflight is the least-coupled seam (async function over inputs with no cleaning state mutation, no process launch, no backup, no CTS creation). Establishes the pattern for subsequent extractions.

Output: 3 new production files, 1 new test file, modified facade, modified DI registration. ~250 lines removed from `CleaningOrchestrator.cs`.

## Must-Haves

- [ ] "ICleaningPreflight.PrepareAsync is the single source of preflight/selection logic, consumed by both StartCleaningAsync and RunDryRunAsync (D-13)."
- [ ] "CleaningPreflightPlan carries full clean/skip reason rows (NotSelected, InSkipList, FileNotFound, Unreadable, ZeroByte, MalformedEntry, InvalidExtension, ReadyForCleaning) per D-15."
- [ ] "CleaningPreflightPlan carries explicit MO2 policy facts (IsMo2ModeActive, BackupSkippedByPolicy, FileValidationSkippedByPolicy, LaunchModeLabel) per D-16."
- [ ] "CleaningPreflightPlan carries XEditDirectory (single source of truth for the runner's offset capture and the finalizer's log read — eliminates re-derivation in the facade per R-08)."
- [ ] "ICleaningPreflight does NOT mutate cleaning state (no state.UpdateState calls during preflight) — caller (facade) handles state mutation per D-14. Preflight DOES perform persistence flushing (FlushPendingSavesAsync) and environment validation (ValidateEnvironmentAsync); the invariant is 'no cleaning state mutation, no process launch, no backup, no CTS creation' (per Codex MEDIUM clarification — preflight is not strictly pure)."
- [ ] "PreflightSkipReason mapping uses the actual PluginWarningKind enum (per R-05). Members verified at planning time from AutoQAC/Models/PluginInfo.cs:6-14: None, NotFound, Unreadable, ZeroByte, MalformedEntry, InvalidExtension. The plan example formerly referenced a non-existent `PluginValidationFailureKind` — that name has been corrected throughout."
- [ ] "D-04: ICleaningPreflight.cs, CleaningPreflight.cs, CleaningPreflightModels.cs are placed under AutoQAC/Services/Cleaning/ — no cross-folder churn."
- [ ] "ICleaningOrchestrator public surface unchanged — every method/property/event present pre-refactor must remain at the same signature (Wave 0 snapshot test still green)."
- [ ] "Sequential xEdit cleaning preserved — `ProcessExecutionService` single-slot semaphore stays in the launch path."
- [ ] "Phase 6 launch argv intent preserved — exact xEdit flags and MO2 wrapping unchanged (preflight does not touch launch)."
- [ ] "All 47+ existing characterization tests pass unchanged."
- [ ] "D-11 honored — if a non-REF-01 bug is found during extraction, executor MUST stop and ask, not silently fix."

## Files

- `AutoQAC/Services/Cleaning/ICleaningPreflight.cs`
- `AutoQAC/Services/Cleaning/CleaningPreflight.cs`
- `AutoQAC/Services/Cleaning/CleaningPreflightModels.cs`
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC.Tests/Services/Cleaning/CleaningPreflightTests.cs`
