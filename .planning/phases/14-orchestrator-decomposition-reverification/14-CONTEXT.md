# Phase 14: Orchestrator Decomposition Reverification - Context

**Gathered:** 2026-05-01
**Status:** Ready for planning

<domain>
## Phase Boundary

Phase 14 produces current, phase-local verification evidence that `REF-01` is satisfied after the Phase 8 gap closures for concurrent session guarding and post-detection file-load-order validation. This is a reverification phase, not a broad refactor: downstream agents should verify current source, current focused tests, structural collaborator boundaries, and the full solution test run, then write the Phase 14 verdict without editing Phase 8 artifacts or milestone marker files.

</domain>

<spec_lock>
## Requirements (locked via SPEC.md)

**5 requirements are locked.** See `14-SPEC.md` for full requirements, boundaries, and acceptance criteria.

Downstream agents MUST read `14-SPEC.md` before planning or implementing. Requirements are not duplicated here.

**In scope (from SPEC.md):**
- Phase-local `14-VERIFICATION.md` proving or rejecting `REF-01` with current evidence.
- Current evidence for concurrent `StartCleaningAsync` session guarding and first-session CTS ownership.
- Current evidence for post-detection file-load-order validation for Fallout3, FalloutNewVegas, and Oblivion.
- Current evidence that cleaning remains sequential and collaborator responsibilities stay focused.
- Focused `REF-01` test/source checks plus a full `dotnet test AutoQACSharp.slnx --nologo` run.
- Evidence-first remediation path: if a live gap is found, Phase 14 may require a minimal follow-up plan before `REF-01` can pass.

**Out of scope (from SPEC.md):**
- Refreshing `.planning/phases/08-cleaning-orchestrator-decomposition/08-VERIFICATION.md`.
- Updating roadmap, requirements, milestone audit, or completion markers.
- Broad orchestrator refactoring.
- Consolidating duplicated file-load-order policy across services.
- Changing user-facing cleaning behavior, adding new UI, or changing MO2 behavior.
- Parallel xEdit cleaning.
- Mutagen submodule changes.

</spec_lock>

<decisions>
## Implementation Decisions

### Artifact Shape
- **D-01:** Create `14-VERIFICATION.md` only. Do not create a separate `14-VALIDATION.md` unless a later failure forces a new approved workflow decision.
- **D-02:** Put the main evidence matrix in `14-VERIFICATION.md` and drive it by the acceptance criteria in `14-SPEC.md`, with roadmap success criteria cross-referenced inside those rows.
- **D-03:** Use concise command-result rows for evidence. Record command, result, and relevant pass/fail or pass-count status without dumping long command output.
- **D-04:** If verification passes without production or test changes, explicitly state that Phase 14 was evidence-only because current source and tests already satisfy the locked SPEC.

### Audit Narrative
- **D-05:** Use an explicit closure chain: `.planning/v1.0-MILESTONE-AUDIT.md` `REF-01` finding -> stale `.planning/phases/08-cleaning-orchestrator-decomposition/08-VERIFICATION.md` blockers -> `08-09` and `08-10` gap-closure evidence -> current Phase 14 verification evidence.
- **D-06:** State that `08-VERIFICATION.md` remains historical/stale and is not edited. `14-VERIFICATION.md` is the current source of truth for `REF-01` status after the gap closures.
- **D-07:** Mandatory historical citations are the milestone audit, stale `08-VERIFICATION.md`, `08-VALIDATION.md`, `08-09-SUMMARY.md`, and `08-10-SUMMARY.md`. Do not cite every Phase 8 summary unless implementation uncovers a specific need.
- **D-08:** Include an explicit boundary note that Phase 14 does not update Phase 8 files, `ROADMAP.md`, `REQUIREMENTS.md`, or milestone audit markers. Marker reconciliation is deferred to milestone completion or the relevant GSD state/roadmap workflow.

### Evidence Breadth
- **D-09:** Use a SPEC-focused evidence set before the full solution run: the named session-guard test, the named detected-load-order tests, sequential/source guard proof, collaborator/DI boundary proof, then `dotnet test AutoQACSharp.slnx --nologo`.
- **D-10:** Prove collaborator boundaries and DI wiring with direct source evidence and existing tests where available. If current tests do not directly prove the boundary, add the smallest boundary test needed; do not refactor production code just to create evidence.
- **D-11:** Report focused evidence separately by concern: session guard, detected load-order validation, sequential/source guard, collaborator/DI proof, and full solution suite.
- **D-12:** Do not require real xEdit, real MO2, or manual smoke evidence. Phase 14's evidence bar is automated source/test evidence for an internal orchestrator refactor requirement.

### Failure Path
- **D-13:** If a focused Phase 14 check finds a real live gap, record the failed evidence, make only the smallest SPEC-scoped fix or test change, then rerun focused and full evidence before any pass verdict.
- **D-14:** A failing full `dotnet test AutoQACSharp.slnx --nologo` run blocks a `REF-01` pass verdict. The artifact may separate unrelated failure analysis from focused `REF-01` evidence, but it must still report `gaps_found` until the required full-suite check passes.
- **D-15:** If direct collaborator/DI proof is missing, the acceptable test change is the smallest boundary-focused test that proves registrations or collaborator boundaries without broader production refactoring.
- **D-16:** If a discovered gap requires broad refactoring or changes outside `14-SPEC.md` boundaries, stop and report `gaps_found`. Do not expand Phase 14 into a broad refactor.

### the agent's Discretion
- Exact section titles, row IDs, and table columns in `14-VERIFICATION.md` are planner discretion as long as the artifact follows D-01 through D-16 and every `14-SPEC.md` acceptance criterion has an explicit pass/fail row.
- Exact focused test command syntax is planner discretion. Separate commands are preferred for reporting, but equivalent filtered runs are acceptable if the evidence rows stay separated by concern.
- Exact placement of the stale-artifact boundary note is planner discretion, but it must be visible before the final `REF-01` verdict.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase Scope And Requirements
- `.planning/phases/14-orchestrator-decomposition-reverification/14-SPEC.md` - Locked Phase 14 goal, requirements, boundaries, constraints, acceptance criteria, and interview log. MUST read first.
- `.planning/ROADMAP.md` - Phase 14 goal, dependency, mapped requirement `REF-01`, gap-closure note, and four success criteria.
- `.planning/REQUIREMENTS.md` - `REF-01` definition and traceability.
- `.planning/PROJECT.md` - Cleanup milestone intent, hard sequential xEdit constraint, read-only `Mutagen/` constraint, and carried-forward project decisions.
- `.planning/STATE.md` - Current project session state and accumulated decisions, including Phase 13 completion and current focus drift.

### Historical REF-01 Gap Evidence
- `.planning/v1.0-MILESTONE-AUDIT.md` - Source audit finding that still marks `REF-01` unsatisfied because stale Phase 8 verification reported two blockers.
- `.planning/phases/08-cleaning-orchestrator-decomposition/08-VERIFICATION.md` - Historical stale verification report that reported the concurrent-start and post-detection load-order blockers. Do not edit.
- `.planning/phases/08-cleaning-orchestrator-decomposition/08-VALIDATION.md` - Historical validation matrix showing later `08-09` and `08-10` rows for the two blocker closures.
- `.planning/phases/08-cleaning-orchestrator-decomposition/08-09-SUMMARY.md` - Concurrent `StartCleaningAsync` active-session guard closure summary.
- `.planning/phases/08-cleaning-orchestrator-decomposition/08-10-SUMMARY.md` - Post-detection file-load-order validation closure summary.

### Carried-Forward Evidence Patterns
- `.planning/phases/13-command-launch-escaping-reverification-safe-mo2-failures/13-CONTEXT.md` - Recent reverification-phase pattern for current evidence, stale audit closure, and marker-boundary handling. Phase 14 intentionally differs by using only `14-VERIFICATION.md`.
- `.planning/phases/12-process-stop-verification-progress-flow-closure/12-CONTEXT.md` - Prior decision to make a later phase's verification the current source of truth without rewriting older phase artifacts or marker files.

### Codebase Constraints And Existing Patterns
- `.planning/codebase/TESTING.md` - xUnit, FluentAssertions, NSubstitute, filtered test command, full-suite, and source-guard evidence patterns.
- `.planning/codebase/ARCHITECTURE.md` - Cleaning orchestrator flow, collaborator responsibilities, DI registration location, sequential cleaning invariant, and process safety constraints.
- `.planning/codebase/CONCERNS.md` - Original cleaning orchestration concentration concern, sequential cleaning fragile area, duplicated load-order policy concern, and known testing gaps.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` - Current facade has `_sessionActive`, `EnterSessionOrThrow`, `ExitSession`, and `CreateSessionCts`; the sequential plugin loop remains a `foreach` and delegates backup, runner, finalizer, and termination work through focused collaborators.
- `AutoQAC/Services/Cleaning/CleaningPreflight.cs` - Current preflight calls `ValidateDetectedLoadOrderPath(gameType, config.LoadOrderPath)` immediately after final game detection and before variant detection, skip-list loading, or plugin-row construction.
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` - Registers `ICleaningPreflight`, `IBackupSessionCoordinator`, `ICleaningTerminationCoordinator`, `IPluginCleaningRunner`, `IPluginResultFinalizer`, then `ICleaningOrchestrator` in `AddBusinessLogic`.
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs` - Contains `StartCleaningAsync_WhenSessionAlreadyActive_ShouldRejectSecondStartAndKeepFirstSessionCancellable` plus source guards for no parallel cleaning constructs.
- `AutoQAC.Tests/Services/Cleaning/CleaningPreflightTests.cs` - Contains `PrepareAsync_UnknownGameDetectedAsFileLoadOrderGame_WithMissingLoadOrderPath_Throws` and `PrepareAsync_UnknownGameDetectedAsMutagenSupportedGame_WithMissingLoadOrderPath_Succeeds`.

### Established Patterns
- Verification artifacts should map acceptance criteria to command/source evidence and final status.
- Focused tests are run with `dotnet test AutoQACSharp.slnx --nologo --filter "FullyQualifiedName~..."`; the full release confidence check is `dotnet test AutoQACSharp.slnx --nologo`.
- Source-level guards are already used to protect structural invariants such as no `Task.WhenAll`, `Task.WhenAny`, `Task.Run`, `Parallel.ForEach`, or `Parallel.ForEachAsync` in cleaning services.
- Sequential xEdit cleaning remains a hard runtime requirement. No verification or remediation path may approve parallel plugin loops, parallel backups before xEdit, or parallel xEdit launches.
- Production/test code changes are evidence-first only. If current checks pass, do not manufacture code changes.

### Integration Points
- `14-VERIFICATION.md` should be written under `.planning/phases/14-orchestrator-decomposition-reverification/` and should be the only canonical Phase 14 evidence artifact.
- If a missing collaborator/DI boundary test is discovered, place the smallest appropriate test in existing test families such as `AutoQAC.Tests/Integration/DependencyInjectionTests.cs` or `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`, depending on what boundary is missing.
- State/session updates after context capture should use the GSD workflow/state commands only; do not directly edit `.planning/STATE.md` outside the registered state handler.
- If any required evidence fails, `14-VERIFICATION.md` must preserve the failure and avoid marking `REF-01` satisfied until remediation and rerun evidence pass.

</code_context>

<specifics>
## Specific Ideas

- Suggested focused session-guard command: `dotnet test AutoQACSharp.slnx --nologo --filter "FullyQualifiedName~StartCleaningAsync_WhenSessionAlreadyActive_ShouldRejectSecondStartAndKeepFirstSessionCancellable"`.
- Suggested focused detected-load-order command: `dotnet test AutoQACSharp.slnx --nologo --filter "FullyQualifiedName~PrepareAsync_UnknownGameDetectedAsFileLoadOrderGame_WithMissingLoadOrderPath_Throws|FullyQualifiedName~PrepareAsync_UnknownGameDetectedAsMutagenSupportedGame_WithMissingLoadOrderPath_Succeeds"`.
- Suggested sequential/source guard command: `dotnet test AutoQACSharp.slnx --nologo --filter "FullyQualifiedName~Cleaning_Source_NoFileParallelizesPluginLoop|FullyQualifiedName~CleaningOrchestrator_Source_DoesNotParallelizePluginCleaning"`.
- Suggested full-suite command: `dotnet test AutoQACSharp.slnx --nologo`.
- `14-VERIFICATION.md` should include rows for session guard, detected file-load-order validation, sequential behavior, collaborator/DI boundaries, full-suite status, and final `REF-01` verdict.
- If the planner adds a missing boundary test, the verification artifact should say Phase 14 was not evidence-only and should explain the minimal test-only change.

</specifics>

<deferred>
## Deferred Ideas

None from discussion. The following remain explicitly outside Phase 14: Phase 8 artifact rewrites, roadmap/requirements/audit marker reconciliation, broad orchestrator refactoring, duplicated file-load-order policy consolidation, user-facing cleaning behavior changes, MO2 behavior changes, real xEdit/MO2 smoke testing, parallel xEdit cleaning, and `Mutagen/` changes.

</deferred>

---

*Phase: 14-orchestrator-decomposition-reverification*
*Context gathered: 2026-05-01*
