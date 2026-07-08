# Phase 14: Orchestrator Decomposition Reverification - Specification

**Created:** 2026-05-01
**Ambiguity score:** 0.11 (gate: <= 0.20)
**Requirements:** 5 locked

## Goal

Phase 14 produces phase-local current verification evidence proving REF-01 is satisfied after the Phase 8 session-guard and detected-game preflight gap closures, or reports any live gap without marking REF-01 complete.

## Background

REF-01 requires maintainers to change cleaning preflight, backup, execution, result finalization, or termination logic without editing one monolithic cleaning orchestrator. Phase 8 decomposed `CleaningOrchestrator` into `ICleaningPreflight`, `IBackupSessionCoordinator`, `ICleaningTerminationCoordinator`, `IPluginCleaningRunner`, and `IPluginResultFinalizer`, all registered before `ICleaningOrchestrator` in `ServiceCollectionExtensions`. The stale `08-VERIFICATION.md` still reports two blockers: concurrent `StartCleaningAsync` calls could overlap and `CleaningPreflight` could bypass file-load-order validation after `Unknown` game detection. Current code and Phase 8 gap-closure artifacts show an `Interlocked` active-session guard in `CleaningOrchestrator`, post-detection `ValidateDetectedLoadOrderPath` in `CleaningPreflight`, targeted tests for both behaviors, and no parallel constructs in cleaning services. Phase 14 exists because the milestone audit needs current, phase-local verification evidence for REF-01 rather than another broad refactor.

## Requirements

1. **Phase-local REF-01 verdict**: Phase 14 must create a phase-local verification artifact that gives a clear pass/fail verdict for REF-01.
   - Current: `.planning/phases/08-cleaning-orchestrator-decomposition/08-VERIFICATION.md` is stale and still reports gaps, while `.planning/phases/14-orchestrator-decomposition-reverification/` has no verification artifact.
   - Target: A Phase 14 verification artifact records whether REF-01 is satisfied by current code and evidence, without requiring Phase 8 artifact edits.
   - Acceptance: `.planning/phases/14-orchestrator-decomposition-reverification/14-VERIFICATION.md` exists and contains an explicit REF-01 verdict plus pass/fail status for the four Phase 14 roadmap success criteria.

2. **Concurrent start guard evidence**: Verification must prove overlapping `StartCleaningAsync` calls cannot replace the active session or start a second cleaning workflow.
   - Current: `CleaningOrchestrator` has `_sessionActive`, `EnterSessionOrThrow`, `ExitSession`, and `CreateSessionCts`; `CleaningOrchestratorTests` contains `StartCleaningAsync_WhenSessionAlreadyActive_ShouldRejectSecondStartAndKeepFirstSessionCancellable`.
   - Target: Phase 14 evidence records that concurrent starts fail fast or otherwise safely no-op without overwriting the first session CTS, and that the first active session remains cancellable.
   - Acceptance: The verification artifact cites current code evidence and a passing focused test result for `StartCleaningAsync_WhenSessionAlreadyActive_ShouldRejectSecondStartAndKeepFirstSessionCancellable`.

3. **Detected-game load-order validation evidence**: Verification must prove file-load-order validation runs after `Unknown` game detection resolves to Fallout3, FalloutNewVegas, or Oblivion.
   - Current: `CleaningPreflight` calls `ValidateDetectedLoadOrderPath(gameType, config.LoadOrderPath)` after game detection; `CleaningPreflightTests` covers `Unknown` to Fallout3/FalloutNewVegas/Oblivion missing-load-order failures and the Fallout4 control case.
   - Target: Phase 14 evidence records that detected file-load-order games cannot proceed to skip-list loading or plugin-row construction when `LoadOrderPath` is missing or invalid, while Mutagen-supported detected games still do not require a load-order file.
   - Acceptance: The verification artifact cites current code evidence and a passing focused test result for `PrepareAsync_UnknownGameDetectedAsFileLoadOrderGame_WithMissingLoadOrderPath_Throws` and `PrepareAsync_UnknownGameDetectedAsMutagenSupportedGame_WithMissingLoadOrderPath_Succeeds`.

4. **Sequential collaborator boundary evidence**: Verification must prove cleaning remains sequential and responsibility boundaries remain focused across preflight, backup, runner, finalizer, and termination collaborators.
   - Current: `CleaningOrchestrator` retains a sequential `foreach` plugin loop and delegates preflight, backup session coordination, process termination, plugin execution, and result finalization through focused interfaces; source search found no `Task.WhenAll`, `Task.WhenAny`, `Task.Run`, `Parallel.ForEach`, or `Parallel.For` in `AutoQAC/Services/Cleaning/*.cs`.
   - Target: Phase 14 evidence records that the orchestrator remains a thin sequential facade and that the five Phase 8 collaborators remain wired through DI.
   - Acceptance: The verification artifact cites code references for the sequential loop, collaborator calls, and DI registrations, and records a source-check result showing no parallel plugin-cleaning constructs in cleaning services.

5. **Current regression evidence**: Phase 14 must finish with current automated evidence, not only historical Phase 8 summaries.
   - Current: Phase 8 summaries and validation show prior commands passed, but Phase 14 has not yet recorded current test execution.
   - Target: Phase 14 verification records focused REF-01 checks and a full solution test run from this phase.
   - Acceptance: The verification artifact records passing focused verification commands for the historical gaps and a passing `dotnet test AutoQACSharp.slnx --nologo` result, or records `gaps_found` and does not claim REF-01 satisfied if any required check fails.

## Boundaries

**In scope:**
- Phase-local `14-VERIFICATION.md` proving or rejecting REF-01 with current evidence.
- Current evidence for concurrent `StartCleaningAsync` session guarding and first-session CTS ownership.
- Current evidence for post-detection file-load-order validation for Fallout3, FalloutNewVegas, and Oblivion.
- Current evidence that cleaning remains sequential and collaborator responsibilities stay focused.
- Focused REF-01 test/source checks plus a full `dotnet test AutoQACSharp.slnx --nologo` run.
- Evidence-first remediation path: if a live gap is found, Phase 14 may require a minimal follow-up plan before REF-01 can pass.

**Out of scope:**
- Refreshing `.planning/phases/08-cleaning-orchestrator-decomposition/08-VERIFICATION.md` - the selected deliverable is Phase 14 verification.
- Updating roadmap, requirements, milestone audit, or completion markers - status reconciliation belongs to milestone completion or a separate hygiene phase.
- Broad orchestrator refactoring - this phase verifies the existing decomposition and only permits minimal live-gap remediation if evidence fails.
- Consolidating duplicated file-load-order policy across services - this remains outside this reverification phase.
- Changing user-facing cleaning behavior, adding new UI, or changing MO2 behavior - Phase 14 is REF-01 verification only.
- Parallel xEdit cleaning - sequential cleaning remains a hard runtime requirement.
- Mutagen submodule changes - `Mutagen/` remains read-only.

## Constraints

- Sequential cleaning must remain mandatory: no phase output may introduce or approve parallel plugin cleaning or parallel xEdit launches.
- Verification must be phase-local: Phase 14 can prove REF-01 without editing stale Phase 8 verification or milestone audit files.
- Evidence must be current: historical summaries may be cited as context, but the final Phase 14 verdict must be based on current code/source checks and current test execution.
- Production/test code changes are evidence-first only: no code changes are required if the focused checks pass; if a live gap fails verification, the phase must not claim REF-01 satisfied until the gap is resolved and reverified.
- Standard project constraints still apply: Windows-specific runtime assumptions, strict MVVM boundaries, async process/I/O work, and read-only `Mutagen/`.

## Acceptance Criteria

- [ ] `.planning/phases/14-orchestrator-decomposition-reverification/14-VERIFICATION.md` exists.
- [ ] `14-VERIFICATION.md` gives an explicit REF-01 pass/fail verdict.
- [ ] `14-VERIFICATION.md` evaluates all four Phase 14 roadmap success criteria as pass/fail checks.
- [ ] Focused evidence proves concurrent `StartCleaningAsync` calls do not start overlapping sessions or overwrite the first session CTS.
- [ ] Focused evidence proves `Unknown` to Fallout3/FalloutNewVegas/Oblivion detection requires valid `LoadOrderPath` before skip-list or plugin-row work.
- [ ] Source evidence proves cleaning remains sequential and no parallel plugin-cleaning constructs exist in `AutoQAC/Services/Cleaning/*.cs`.
- [ ] Evidence cites DI registrations for `ICleaningPreflight`, `IBackupSessionCoordinator`, `ICleaningTerminationCoordinator`, `IPluginCleaningRunner`, `IPluginResultFinalizer`, and `ICleaningOrchestrator`.
- [ ] A current `dotnet test AutoQACSharp.slnx --nologo` run is recorded as passing before REF-01 is marked satisfied.
- [ ] If any focused or full-suite check fails, the Phase 14 verification artifact records `gaps_found` and does not claim REF-01 satisfied until a minimal remediation plan passes verification.
- [ ] Phase 14 does not update Phase 8 verification, roadmap, requirements, milestone audit, or completion markers.

## Ambiguity Report

| Dimension           | Score | Min   | Status | Notes |
|---------------------|-------|-------|--------|-------|
| Goal Clarity        | 0.92  | 0.75  | PASS   | Phase-local REF-01 reverification is the locked outcome. |
| Boundary Clarity    | 0.88  | 0.70  | PASS   | Phase-local only; status reconciliation and Phase 8 refresh are out of scope. |
| Constraint Clarity  | 0.85  | 0.65  | PASS   | Evidence-first, sequential-only, current-test-evidence constraints are explicit. |
| Acceptance Criteria | 0.90  | 0.70  | PASS   | Focused checks, source checks, full suite, and gap behavior are pass/fail. |
| **Ambiguity**       | 0.11  | <=0.20| PASS   | Gate passed after round 2. |

Status: PASS = met minimum, BELOW = below minimum (planner treats as assumption)

## Interview Log

| Round | Perspective | Question summary | Decision locked |
|-------|-------------|------------------|-----------------|
| 1 | Researcher | What should count as Phase 14's primary deliverable? | Create phase-local Phase 14 verification evidence. |
| 1 | Researcher | Should the phase be evidence-only or allow fixes if live gaps appear? | Use evidence-first execution: verify first, only plan minimal fixes if a live gap appears. |
| 2 | Researcher + Simplifier | Should Phase 14 reconcile roadmap/requirements/audit markers? | Keep Phase 14 phase-local only; leave marker reconciliation to later milestone hygiene. |
| 2 | Researcher + Simplifier | What is the minimum verification bar? | Focused REF-01 checks plus full solution test suite. |

---

*Phase: 14-orchestrator-decomposition-reverification*
*Spec created: 2026-05-01*
*Next step: /gsd-discuss-phase 14 - implementation decisions for how to produce the verification evidence*
