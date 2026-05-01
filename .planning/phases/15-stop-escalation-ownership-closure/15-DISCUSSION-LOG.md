# Phase 15: Stop Escalation Ownership Closure - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md - this log preserves the alternatives considered.

**Date:** 2026-05-01
**Phase:** 15-stop-escalation-ownership-closure
**Areas discussed:** Target ownership, No-target outcome, Test proof shape, Verification artifact

---

## Target Ownership

### Primary Guarantee After Detach

| Option | Description | Selected |
|--------|-------------|----------|
| Retain handle | Keep the original `Process` handle in the termination coordinator until the user chooses `Force Terminate` or `Leave Running`. Directly fixes `DetachProcess` clearing `_currentProcess`. | Yes |
| Hybrid fallback | Retain the handle first, but also keep PID/start-time evidence as a fallback if the handle is unavailable. More robust, slightly broader surface. |  |
| PID recovery only | Let detach release the `Process` handle and recover through PID/start-time lookup later. Avoids ownership lifetime changes but is riskier with PID expiry/reuse. |  |
| Planner decides | Lock only the user-visible guarantee and let research/planning choose the minimal safe ownership mechanism. |  |

**User's choice:** Retain handle.
**Notes:** The retained handle is the required fix; PID recovery is not the primary Phase 15 mechanism.

### Retained Target Lifetime

| Option | Description | Selected |
|--------|-------------|----------|
| Until user resolves | Keep it through runner detach and session finalization until `Force Terminate`, `Leave Running`, natural exit, or next-session reset cleanup resolves it. | Yes |
| Only through detach | Retain after `DetachProcess`, but allow normal session reset to clear it. Smaller change, but may still lose the target while the confirmation dialog is open. |  |
| Until process exits | Keep the handle until observed exit regardless of user choice. Strong ownership, but may keep process resources longer than needed. |  |
| Planner decides | Lock the success/failure guarantee, not the exact lifetime boundary. |  |

**User's choice:** Until user resolves.
**Notes:** The target must survive session-finalization timing if the confirmation dialog is still pending.

### Active Slot Separation

| Option | Description | Selected |
|--------|-------------|----------|
| Separate slot | Keep a pending-escalation target distinct from `_currentProcess`, so hang monitoring/active-process checks can detach while `ForceStop` still has a target. | Yes |
| Reuse current slot | Make `_currentProcess` stay set until user resolution. Smaller surface, but can blur active cleaning vs pending escalation state. |  |
| Planner decides | Planner may choose the smallest design as long as detach no longer loses confirmed force termination. |  |

**User's choice:** Separate slot.
**Notes:** Active cleaning semantics and pending escalation ownership should not be conflated.

### PID Fallback Requirement

| Option | Description | Selected |
|--------|-------------|----------|
| Handle only | Keep the phase minimal: retained `Process` handle is the required fix; PID evidence remains for orphan cleanup and tests unless implementation naturally needs it. | Yes |
| Fallback required | Require planner to add verified PID/start-time recovery if the retained handle is missing. More resilient but broader than the direct ownership gap. |  |
| Fallback optional | Planner can add a verified fallback only if research shows the retained-handle approach cannot cover session-finalization timing safely. |  |

**User's choice:** Handle only.
**Notes:** Fallback may be added only if research proves it is necessary for the retained-handle approach.

---

## No-Target Outcome

### Already Exited Before Force

| Option | Description | Selected |
|--------|-------------|----------|
| AlreadyExited success | Return/report `AlreadyExited` without showing force-failure copy because there is no longer a running xEdit target to kill. | Yes |
| ForceKilled equivalent | Treat already-exited as successful escalation for user purposes, even though no kill occurred. Simpler UX but less exact evidence. |  |
| ForceKillFailed | Show shared failure copy whenever no kill occurs after confirmation. Strongly conservative, but can over-warn when xEdit already closed. |  |
| Planner decides | Planner may choose the exact enum mapping as long as silent cached `GracePeriodExpired` is forbidden. |  |

**User's choice:** `AlreadyExited` success.
**Notes:** Do not warn if the target is provably gone.

### Invalid Or Unavailable Target

| Option | Description | Selected |
|--------|-------------|----------|
| ForceKillFailed | Use the shared force-failure dialog and persistent warning unless code can prove the process already exited. Avoids silent cached `GracePeriodExpired`. | Yes |
| AlreadyExited fallback | Assume no target means already gone. Less noisy, but risks hiding the ownership bug the phase is meant to close. |  |
| Planner decides | Planner may define the invalid-target mapping, but it must be explicit and user-visible when xEdit may still be running. |  |

**User's choice:** `ForceKillFailed`.
**Notes:** Invalid or unavailable target after confirmation must not silently pass.

### Cached GracePeriodExpired

| Option | Description | Selected |
|--------|-------------|----------|
| Never | After confirmation, `ForceStop` must produce a terminal result: `ForceKilled`, `AlreadyExited`, or `ForceKillFailed`. Cached `GracePeriodExpired` is the bug. | Yes |
| Only with explicit failure | Allow `GracePeriodExpired` internally only if the ViewModel still maps it to shared force-failure UI. Keeps enum unchanged but weakens clarity. |  |
| Planner decides | Planner can choose the internal result shape, but silent cached `GracePeriodExpired` must be impossible. |  |

**User's choice:** Never.
**Notes:** Confirmed force escalation must return a terminal result.

### User-Facing Wording

| Option | Description | Selected |
|--------|-------------|----------|
| Shared copy only | Use existing `StopTerminationDialogContent.ForceFailureTitle` / `ForceFailureMessage` and Progress warning; no new detached-target wording. | Yes |
| Add debug-only log reason | Keep user copy shared, but require logs/tests to distinguish invalid target vs OS kill refusal for maintainers. |  |
| New user wording | Show a different message for lost ownership/invalid target. More specific, but risks drifting from Phase 12's shared copy contract. |  |

**User's choice:** Shared copy only.
**Notes:** Do not add new user-facing detached-target wording.

---

## Test Proof Shape

### Mandatory Evidence Set

| Option | Description | Selected |
|--------|-------------|----------|
| Layered proof | Require coordinator/service proof, controlled helper-process proof, and Progress ViewModel confirmation-order proof. Matches SPEC and existing test families. | Yes |
| Service only | Require only `CleaningTerminationCoordinator` / orchestrator tests. Faster, but weaker proof that a real detached process exits. |  |
| Helper process only | Require a real helper-process integration test as the main proof. Strong process evidence, but may miss ViewModel confirmation ordering. |  |
| Planner decides | Planner can pick exact tests as long as acceptance criteria map explicitly to automated evidence. |  |

**User's choice:** Layered proof.
**Notes:** Evidence must cover ownership, process behavior, and UI confirmation/failure visibility.

### Helper-Process Test Location

| Option | Description | Selected |
|--------|-------------|----------|
| Coordinator-focused | Use `CleaningTerminationCoordinator` with a real helper process: Attach, `GracePeriodExpired`, `DetachProcess`, `ForceStopAsync`, assert process exits/`ForceKilled`. | Yes |
| Full orchestrator path | Drive through `CleaningOrchestrator` / `PluginCleaningRunner` so detach happens naturally. Stronger integration, but more setup and likely more brittle. |  |
| Both if needed | Start with coordinator-focused helper process and add orchestrator coverage only if planner finds runner/finalizer timing unproven. |  |

**User's choice:** Coordinator-focused.
**Notes:** Full orchestrator coverage is optional only if research finds it necessary.

### Pre-Confirmation Guard

| Option | Description | Selected |
|--------|-------------|----------|
| Extend VM test | Keep/extend the existing `ProgressViewModel` test that holds the confirmation dialog open and asserts `ForceStopCleaningAsync` is not called. | Yes |
| Add process-level guard | Also assert the real helper process remains running while confirmation is pending. Stronger, but needs more async/test plumbing. |  |
| Rely on Phase 12 | Cite Phase 12's existing confirmation-order tests without changing them. Minimal, but weaker after ownership code changes. |  |

**User's choice:** Extend VM test.
**Notes:** The Phase 12 confirmation boundary must remain actively guarded after ownership changes.

### Failure-Path Proof

| Option | Description | Selected |
|--------|-------------|----------|
| Mocked failure plus UI | Coordinator/orchestrator returns `ForceKillFailed` for invalid/unavailable target; Progress VM test verifies shared dialog and persistent warning. | Yes |
| Real OS refusal only | Require a real process failure scenario such as canceled post-kill wait/access failure. Strong but harder to make deterministic. |  |
| Existing failure tests suffice | Cite current `ProcessExecution` and Progress failure tests, only adding detached success coverage. |  |
| Planner decides | Planner can choose deterministic failure evidence as long as `SAF-02` maps to explicit tests. |  |

**User's choice:** Mocked failure plus UI.
**Notes:** Prefer deterministic failure proof over hard-to-reproduce OS refusal.

---

## Verification Artifact

### Artifact Shape

| Option | Description | Selected |
|--------|-------------|----------|
| Verification only | Create `15-VERIFICATION.md` with an evidence matrix inside it. Matches `15-SPEC.md` and Phase 14's focused closure pattern. |  |
| Validation plus verification | Create `15-VALIDATION.md` for acceptance-criterion rows and `15-VERIFICATION.md` for final conclusions. More audit detail, more artifact overhead. |  |
| Planner decides | Planner may choose artifact count, but `15-VERIFICATION.md` must exist and contain requirement/gap closure evidence. | Yes |

**User's choice:** Planner decides.
**Notes:** `15-VERIFICATION.md` remains mandatory.

### Evidence Matrix Mapping

| Option | Description | Selected |
|--------|-------------|----------|
| Reqs + gaps + ACs | Rows must cover `SAF-01`, `SAF-02`, `TEST-01`, `INT-STOP-01`, `FLOW-STOP-ESCALATION-01`, and each `15-SPEC.md` acceptance criterion. | Yes |
| Requirements only | Map only `SAF-01`, `SAF-02`, `TEST-01` with test/source evidence. Shorter, but audit gap closure is less direct. |  |
| Audit gaps only | Focus on `INT-STOP-01` and `FLOW-STOP-ESCALATION-01` plus requirement conclusion text. Compact, but may miss SPEC checkbox traceability. |  |
| Planner decides | Planner may choose row structure as long as all locked SPEC requirements are objectively traceable. |  |

**User's choice:** Reqs + gaps + ACs.
**Notes:** Traceability should be explicit enough for audit closure.

### Command Evidence Format

| Option | Description | Selected |
|--------|-------------|----------|
| Concise command rows | Record command, scope, pass/fail result, and relevant test names/source refs. Do not paste long command output unless diagnosing failure. | Yes |
| Full output excerpts | Include meaningful output excerpts for every targeted/full run. More audit detail, but noisier. |  |
| Summary only | State tests passed and cite names without command rows. Shorter, but weaker for future audit reruns. |  |
| Planner decides | Planner can format evidence as long as commands and outcomes are reproducible. |  |

**User's choice:** Concise command rows.
**Notes:** Favor concise, reproducible evidence.

### Marker Updates

| Option | Description | Selected |
|--------|-------------|----------|
| No marker edits | Phase 15 writes current verification evidence only; Phase 16 handles roadmap/requirements/audit marker reconciliation. |  |
| Update requirements only | If verification passes, mark `SAF-01` / `SAF-02` / `TEST-01` complete in `REQUIREMENTS.md`. Useful but conflicts with Phase 16 boundary. |  |
| Update all markers | Synchronize `ROADMAP.md`, `REQUIREMENTS.md`, and milestone audit in Phase 15. Broader than SPEC boundaries. |  |
| Planner decides | Planner may decide based on verification outcome and GSD state, but must respect explicit SPEC boundaries. | Yes |

**User's choice:** Planner decides.
**Notes:** Because `15-SPEC.md` declares marker reconciliation out of scope and Phase 16 owns it, planner discretion is constrained by that locked boundary.

---

## the agent's Discretion

- Exact production names, helper shapes, DTOs, and internal field names for the pending escalation target.
- Exact test names and filtered command syntax.
- Whether to create a separate `15-VALIDATION.md`; `15-VERIFICATION.md` is mandatory.
- Marker handling only within `15-SPEC.md` boundaries and registered GSD workflow behavior.

## Deferred Ideas

None. Discussion stayed within Phase 15 scope.
