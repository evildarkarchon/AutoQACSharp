# T01: 08-cleaning-orchestrator-decomposition 01

**Slice:** S09 — **Milestone:** M001

## Description

Add Wave 0 characterization tests that lock current `CleaningOrchestrator` behavior BEFORE any extraction begins. This is pure test-only work: no production code is modified. The test IS the deliverable.

Purpose: D-05 mandates "characterize-then-extract." Six characterization gaps from RESEARCH.md must be filled, plus a public-surface snapshot must be locked, so subsequent waves can refactor with confidence.

Output: 7 new test methods in `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`, all green against current production code.

## Must-Haves

- [ ] "Six new characterization tests are added to CleaningOrchestratorTests.cs covering D-18 gaps (left-running, retention warning, retention canceled, dry-run/preflight equivalence, ContinueWithoutBackup, last-termination-result reset)."
- [ ] "D-05: characterize-then-extract — this plan locks current outcomes in tests BEFORE Wave 1+ extractions begin; no production code touched in Wave 0."
- [ ] "D-17: minimum proof is characterization plus seams — this plan delivers the characterization half; per-collaborator seam tests follow in Waves 1-4."
- [ ] "All existing CleaningOrchestratorTests.cs tests continue to pass unmodified — no production code changes in this plan."
- [ ] "ICleaningOrchestrator public-surface snapshot test exists and asserts the exact pre-refactor member list (D-19 hard-to-observe invariant INV-8.1). The reflection helper handles nullable annotations via NullabilityInfoContext, generic Task<T>/Task<List<T>> return types via recursive type formatting, method overloads via parameter-type concatenation, and produces deterministic output across Debug/Release builds (per R-06)."
- [ ] "Sequential xEdit cleaning preserved — `ProcessExecutionService` single-slot semaphore stays in the launch path (no production code touched)."
- [ ] "ICleaningOrchestrator public surface unchanged — every method/property/event present pre-refactor must remain at the same signature (this plan adds the guard test that future plans will use)."
- [ ] "D-11 honored — if a non-REF-01 bug is found during extraction, executor MUST stop and ask, not silently fix."

## Files

- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`
