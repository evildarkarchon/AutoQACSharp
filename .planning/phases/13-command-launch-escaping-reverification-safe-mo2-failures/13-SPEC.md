# Phase 13: Command Launch Escaping Reverification & Safe MO2 Failures - Specification

**Created:** 2026-05-01
**Ambiguity score:** 0.14 (gate: <= 0.20)
**Requirements:** 5 locked

## Goal

Produce current Phase 13 verification evidence that MO2 mode cannot fall back to direct xEdit when MO2 configuration is missing, and that direct xEdit/MO2 command escaping plus launch-failure diagnostics still satisfy `SAF-03` and `TEST-02`.

## Background

Phase 6 originally implemented data-first command construction with `ProcessStartInfo.ArgumentList` for direct xEdit and a four-argument MO2 wrapper contract: `run`, xEdit path, `-a`, and one nested xEdit payload. Its verification report later marked `SAF-03` blocked and `TEST-02` partial because missing MO2 configuration could fall through to a direct xEdit launch, and unexpected launch exceptions could expose path or command details.

The live codebase now contains the intended gap-closure behavior: `XEditCommandBuilder.BuildCommand` returns `null` when MO2 mode is enabled and `Mo2ExecutablePath` is null, empty, or whitespace; `CleaningService.CleanPluginAsync` maps null command construction to a failed result before process execution; unexpected cleaning exceptions return safe plugin-scoped user copy while logging technical details. Existing tests cover the missing-MO2 command builder branch, difficult direct and MO2 argv cases, `ProcessExecutionService` argument preservation through a helper process, and safe launch-failure messages.

The remaining gap is documentation and current evidence: `.planning/v1.0-MILESTONE-AUDIT.md` still treats `SAF-03` and `TEST-02` as unsatisfied based on stale Phase 6 verification evidence. Phase 13 produces fresh Phase 13 artifacts as the current source of truth rather than rewriting historical Phase 6 docs.

## Requirements

1. **Missing MO2 configuration fails before process start**: MO2-enabled cleaning must not construct or start a direct xEdit process when MO2 executable configuration is missing or blank.
   - Current: `XEditCommandBuilder.BuildCommand` returns `null` for null, empty, or whitespace `Mo2ExecutablePath` when MO2 mode is enabled, and `CleaningService` converts null command construction into a failed result before calling `IProcessExecutionService.ExecuteAsync`.
   - Target: Current Phase 13 evidence proves missing MO2 configuration cannot produce a direct xEdit launch or any process start, including null, empty, and whitespace MO2 path cases.
   - Acceptance: Verification cites passing tests or equivalent evidence that MO2 mode with null, empty, and whitespace MO2 path returns no command, reports a no-process-started failure, and does not call the process execution service.

2. **Direct xEdit escaping remains verified**: Direct xEdit command construction must preserve difficult plugin names and paths as literal argv data.
   - Current: Direct mode builds `ProcessStartInfo` with `UseShellExecute = false`, empty `Arguments`, and parsed `ArgumentList` entries for `-QAC`, `-autoexit`, `-autoload`, and exact `plugin.FileName`; tests include quotes, Unicode, spaces, shell-sensitive punctuation, and a combined worst-case plugin name.
   - Target: Current Phase 13 evidence proves direct xEdit launches preserve quotes, Unicode, spaces, shell-sensitive characters, and plugin file names without string-concatenated shell quoting.
   - Acceptance: Verification cites passing direct-mode command builder tests and real process-boundary argument preservation tests covering difficult-character argv values.

3. **Configured MO2 escaping remains verified**: Configured MO2 launch construction must preserve wrapper arguments and the nested xEdit payload without target-plugin corruption.
   - Current: MO2 mode builds `ProcessStartInfo.FileName` from the configured MO2 executable and `ArgumentList` entries `run`, xEdit path, `-a`, and one nested payload; tests parse the nested payload and assert the target plugin remains file-name-only.
   - Target: Current Phase 13 evidence proves configured MO2 launches preserve xEdit path, nested xEdit arguments, quotes, Unicode, spaces, shell-sensitive characters, and file-name-only `-autoload` targets.
   - Acceptance: Verification cites passing configured-MO2 command builder tests that assert the four wrapper arguments, parse the nested payload, and reject use of the plugin full path as the autoload target.

4. **Launch failure diagnostics stay safe**: Launch-build failures, process-start failures, and unexpected launch exceptions must avoid exposing configured executable paths, raw command fragments, or nested MO2 payloads in user-facing results.
   - Current: `CleaningService` returns concise failed results for null command construction, nonzero process-start results, and unexpected exceptions; Phase 11 diagnostics boundaries removed raw argv/path exposure from process logs and user-facing failure copy.
   - Target: Current Phase 13 evidence proves launch-failure user-facing messages remain concise and omit configured xEdit paths, MO2 paths, `run`, `-a`, raw `-autoload` payloads, and raw exception text.
   - Acceptance: Verification cites passing failure-flow tests for command-build failure, mocked launch-start failure, and unexpected launch exception disclosure boundaries.

5. **Fresh Phase 13 evidence closes stale audit gaps**: Phase 13 must produce current verification/validation artifacts proving `SAF-03` and `TEST-02` are satisfied.
   - Current: Phase 6 gap-closure summaries say the implementation was fixed, but the milestone audit still records `SAF-03` and `TEST-02` as unsatisfied based on stale `06-VERIFICATION.md` status.
   - Target: Phase 13 artifacts become the current source of truth for command-launch escaping and safe MO2 failure evidence, without rewriting historical Phase 6 verification documents.
   - Acceptance: `13-VERIFICATION.md` or equivalent Phase 13 validation evidence marks `SAF-03` and `TEST-02` satisfied, links the current tests/code evidence, and records any remaining non-blocking human smoke-test assumptions separately from automated pass/fail status.

## Boundaries

**In scope:**
- Fresh Phase 13 verification/validation evidence for `SAF-03` and `TEST-02`.
- Confirmation that missing MO2 configuration in MO2 mode fails before any direct xEdit or other process launch.
- Reverification of direct xEdit difficult-character `ArgumentList` behavior.
- Reverification of configured MO2 wrapper and nested-payload escaping behavior.
- Reverification that launch-failure user-facing diagnostics omit raw paths, raw command fragments, and nested MO2 payloads.
- Minimal code or test changes only if current evidence reveals a real gap.

**Out of scope:**
- Rewriting Phase 6 verification or validation documents - Phase 13 produces fresh current evidence instead of changing historical records.
- Adding new command-launch features or changing xEdit/MO2 argv contracts - this phase re-verifies existing guarantees.
- Real xEdit or real MO2 smoke-test automation - automated verification uses command-builder, process-helper, and failure-flow tests available in the repo.
- Broader user-facing diagnostics hardening outside launch-failure paths - Phase 11 owns repository-wide diagnostics boundaries.
- Process stop, PID tracking, or termination behavior changes - Phase 12 already covered stop/PID evidence.
- Orchestrator decomposition reverification - Phase 14 owns current evidence for orchestrator structure and session guarding.
- SEC-03 executable-name warning behavior - that requirement is listed under Future Requirements.

## Constraints

- Sequential xEdit cleaning remains a hard runtime requirement; this phase must not parallelize cleaning or process launches.
- Launch diagnostics must preserve Phase 11 boundaries: no user-facing full local paths, raw argv payloads, nested MO2 payloads, or raw exception text.
- Automated evidence must not require installed xEdit or ModOrganizer; tests must use repo-controlled command-builder/process-helper seams unless a later human smoke test is explicitly recorded as optional evidence.
- Historical Phase 6 artifacts remain historical; Phase 13 evidence is additive and current.
- If code or tests change, changes must be minimal and targeted to the failing requirement evidence.

## Acceptance Criteria

- [ ] MO2 mode with null, empty, or whitespace MO2 executable path produces no command and cannot launch direct xEdit.
- [ ] The missing-MO2 failure path reports a failed no-process-started result and does not call `IProcessExecutionService.ExecuteAsync`.
- [ ] Direct xEdit command-builder tests pass for quotes, Unicode, spaces, shell-sensitive punctuation, and combined worst-case plugin names.
- [ ] Configured MO2 command-builder tests pass for the four-argument wrapper contract and parsed nested xEdit payload.
- [ ] Process-boundary integration tests pass for `ArgumentList` preservation and legacy `Arguments` fallback.
- [ ] Launch-failure diagnostics tests pass for command-build failure, mocked launch-start failure, and unexpected launch exception non-disclosure.
- [ ] Fresh Phase 13 verification/validation artifacts mark `SAF-03` and `TEST-02` satisfied using current evidence.
- [ ] Phase 13 does not rewrite historical Phase 6 verification artifacts as part of closing the stale audit gap.

## Ambiguity Report

| Dimension           | Score | Min   | Status | Notes |
|---------------------|-------|-------|--------|-------|
| Goal Clarity        | 0.93  | 0.75  | ✓      | Primary deliverable is current Phase 13 evidence for `SAF-03`/`TEST-02`. |
| Boundary Clarity    | 0.86  | 0.70  | ✓      | Fresh Phase 13 artifacts are in scope; Phase 6 rewrites and new features are out of scope. |
| Constraint Clarity  | 0.75  | 0.65  | ✓      | Preserves sequential cleaning, diagnostics boundaries, and no external xEdit/MO2 dependency for automation. |
| Acceptance Criteria | 0.84  | 0.70  | ✓      | Pass/fail checks map to missing-MO2, direct/MO2 escaping, process boundary, diagnostics, and evidence artifacts. |
| **Ambiguity**       | 0.14  | <=0.20| ✓      | Gate passed after one interview round. |

## Interview Log

| Round | Perspective | Question summary | Decision locked |
|-------|-------------|------------------|-----------------|
| 1 | Researcher | What is the primary deliverable: verification artifact, new tests first, or code hardening? | Verification artifact is primary; code/test changes are only for discovered gaps. |
| 1 | Researcher | Should evidence refresh Phase 6 docs, create Phase 13 docs, or both? | Create fresh Phase 13 artifacts as the current source of truth; do not rewrite Phase 6 history. |

---

*Phase: 13-command-launch-escaping-reverification-safe-mo2-failures*
*Spec created: 2026-05-01*
*Next step: /gsd-discuss-phase 13 - implementation decisions (evidence artifact shape, validation commands, and any minimal gap fixes)*
