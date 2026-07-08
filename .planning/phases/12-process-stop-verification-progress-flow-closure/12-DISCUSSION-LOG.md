# Phase 12: Process Stop Verification & Progress Flow Closure - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md - this log preserves the alternatives considered.

**Date:** 2026-05-01
**Phase:** 12-process-stop-verification-progress-flow-closure
**Areas discussed:** Stop dialog parity, Outcome visibility, Hang Kill boundary, Verification scope

---

## Stop Dialog Parity

### Shared Stop Copy

| Option | Description | Selected |
|--------|-------------|----------|
| Exact same copy | Use existing main Stop wording as the behavior contract; best parity and simplest regression tests. | Yes |
| Progress-specific copy | Same decisions and safety outcome, but wording may mention the Progress window context. | |
| You decide | Planner may choose exact or equivalent wording as long as requirements and Phase 11 safety boundaries hold. | |

**User's choice:** Exact same copy
**Notes:** Progress Stop and main Stop should not drift in confirmation, force-failure, or leave-running warning text.

### Copy Test Strictness

| Option | Description | Selected |
|--------|-------------|----------|
| Exact text contract | Assert the shared title/message text exactly so future drift between main and Progress Stop is caught. | Yes |
| Key meaning only | Assert important phrases/outcomes, allowing minor wording changes without breaking tests. | |
| You decide | Planner picks the smallest stable assertion strategy while preserving exact user-facing parity. | |

**User's choice:** Exact text contract
**Notes:** Tests should enforce the final shared copy exactly.

### Confirmation Button Labels

| Option | Description | Selected |
|--------|-------------|----------|
| Keep Yes/No | Minimal change and exact parity with the current main Stop path; no dialog-service expansion. | |
| Explicit labels | Better destructive-action UX, but requires expanding the dialog API and updating both Stop surfaces. | Yes |
| You decide | Planner may improve labels only if it stays small and preserves all existing dialog behavior. | |

**User's choice:** Explicit labels
**Notes:** The desired labels are shaped as `Force Terminate` and `Leave Running`.

### Safe Copy Refresh

| Option | Description | Selected |
|--------|-------------|----------|
| Only button labels | Keep existing title/message text exactly; add explicit button labels only. | |
| Refresh safe wording | Update both Stop surfaces together to use current Phase 11 safe-copy phrasing like latest AutoQAC log guidance. | Yes |
| You decide | Planner can make the smallest copy refresh needed for explicit labels and safety consistency. | |

**User's choice:** Refresh safe wording
**Notes:** The refreshed copy should still be shared by both Stop surfaces and asserted exactly after implementation.

---

## Outcome Visibility

### Declined Force Termination

| Option | Description | Selected |
|--------|-------------|----------|
| Cancelled results plus warning | Show the normal cancelled/results state and a visible warning/status that xEdit was left running by choice. | Yes |
| Modal only | Show the warning dialog only; do not add persistent Progress-window state beyond existing cancellation results. | |
| Keep cleaning view | Keep the progress view visible with Stopping/left-running status until the user closes it. | |

**User's choice:** Cancelled results plus warning
**Notes:** The outcome should remain visible after the modal is dismissed.

### Force-Kill Failure

| Option | Description | Selected |
|--------|-------------|----------|
| Failed stop warning | Keep the window open with visible warning/status that xEdit may still be running and the user must close it manually. | Yes |
| Cancelled results only | Show the error dialog, then rely on the normal cancelled/results state without extra persistent warning. | |
| You decide | Planner chooses the smallest visible state that satisfies accurate user-facing reporting. | |

**User's choice:** Failed stop warning
**Notes:** The persistent message should make the still-running risk clear.

### Message Placement

| Option | Description | Selected |
|--------|-------------|----------|
| Result summary area | Show it in/near the final cancelled-results summary so it remains visible after cleaning stops. | Yes |
| Stop area banner | Show it near the Stop button/progress controls while the termination flow resolves. | |
| You decide | Planner places it where existing ProgressViewModel state supports it cleanly without UI redesign. | |

**User's choice:** Result summary area
**Notes:** Do not rely only on transient Stop/spinner UI.

### Main Stop Alignment

| Option | Description | Selected |
|--------|-------------|----------|
| Align both surfaces | Keep modal copy shared and also use equivalent status/result wording on main and Progress surfaces. | Yes |
| Progress only | Main Stop can keep its existing status behavior; Phase 12 adds persistent result-summary feedback only to Progress. | |
| You decide | Planner aligns only where it avoids duplication and does not broaden the phase. | |

**User's choice:** Align both surfaces
**Notes:** Main status text should not contradict the refreshed Progress outcome wording.

---

## Hang Kill Boundary

### Confirmation Behavior

| Option | Description | Selected |
|--------|-------------|----------|
| Immediate force action | Keep Kill as direct force termination because the label is already explicit; add outcome handling as needed. | Yes |
| Confirm before kill | Treat Kill like Stop escalation and require the same confirmation before force termination. | |
| You decide | Planner chooses based on smallest safe change and current UI wording. | |

**User's choice:** Immediate force action
**Notes:** `Kill` is already an explicit force action and should not add another confirmation.

### Failure Handling

| Option | Description | Selected |
|--------|-------------|----------|
| Same failure handling | Reuse the shared ForceKillFailed dialog/outcome path so all Progress force failures report consistently. | Yes |
| Dialog only | Show the same failure dialog, but do not add the persistent Progress result-summary warning for Hang Kill. | |
| Leave unchanged | Do not change Hang Kill failure reporting in Phase 12 unless required by Stop parity implementation. | |

**User's choice:** Same failure handling
**Notes:** A failed Hang Kill should leave the same persistent still-running warning as failed confirmed Progress Stop.

### Success Messaging

| Option | Description | Selected |
|--------|-------------|----------|
| No new success copy | Let normal cancelled/results state communicate session outcome; avoid adding extra messages for successful force kill. | Yes |
| Brief killed status | Show a concise result-summary/status line that xEdit was force terminated. | |
| You decide | Planner can add success copy only if needed for consistent result-summary behavior. | |

**User's choice:** No new success copy
**Notes:** Avoid expanding success messaging for the direct Kill path.

### Test Coverage

| Option | Description | Selected |
|--------|-------------|----------|
| Direct Hang Kill tests | Add targeted tests that Kill calls force-stop directly and reports ForceKillFailed through the shared outcome path. | Yes |
| Indirect coverage only | Test Stop parity directly; Hang Kill can be covered only through shared helper tests if implementation extracts one. | |
| You decide | Planner chooses the minimal test set that proves no regression in this boundary. | |

**User's choice:** Direct Hang Kill tests
**Notes:** Tests should prove no confirmation is added to Kill and failure reporting is shared.

---

## Verification Scope

### Artifact Scope

| Option | Description | Selected |
|--------|-------------|----------|
| Phase 12 only | Create current Phase 12 verification as the source of truth; reference Phase 5 history without rewriting old artifacts. | Yes |
| Also add Phase 5 | Create or update `05-VERIFICATION.md` so the original Phase 5 directory also has direct closure evidence. | |
| You decide | Planner chooses the minimal artifact set needed to satisfy the audit and avoid stale truth sources. | |

**User's choice:** Phase 12 only
**Notes:** Do not create or update old Phase 5 verification artifacts.

### Requirement Mapping Detail

| Option | Description | Selected |
|--------|-------------|----------|
| Requirement table plus evidence | For each ID, list status, source files, tests, commands, and audit gap closed. Best for audit readability. | Yes |
| Concise summary | Summarize all four IDs with links to tests/source, but avoid a full matrix. | |
| You decide | Planner picks a clear format as long as all four IDs are visibly satisfied. | |

**User's choice:** Requirement table plus evidence
**Notes:** The four IDs are `SAF-01`, `SAF-02`, `REF-04`, and `TEST-01`.

### Test Evidence

| Option | Description | Selected |
|--------|-------------|----------|
| Targeted plus full suite | Run targeted Progress/process/PID tests and the full solution test command, documenting any unrelated failures. | Yes |
| Targeted only | Run only the focused tests needed for Stop flow, ProcessExecution, PID storage, and integration evidence. | |
| You decide | Planner uses acceptance criteria to choose commands and document evidence clearly. | |

**User's choice:** Targeted plus full suite
**Notes:** Full-suite failures may be documented if unrelated and pre-existing.

### Milestone Marker Updates

| Option | Description | Selected |
|--------|-------------|----------|
| Leave to milestone completion | Phase 12 writes verification evidence only; broader roadmap/requirements reconciliation happens later. | Yes |
| Update Phase 12 markers | After verification passes, update Phase 12-related completion markers for the four requirements. | |
| You decide | Planner follows the repo's GSD state handlers and updates only if workflow conventions require it. | |

**User's choice:** Leave to milestone completion
**Notes:** Phase 12 should not update `REQUIREMENTS.md` or `ROADMAP.md` completion markers.

---

## the agent's Discretion

- Exact helper, service, method, DTO, and test class names are planner discretion.
- Exact final shared copy may be chosen during implementation, but it must be shared and asserted exactly afterward.
- Exact Progress result-summary layout is planner discretion as long as the visible outcome decisions are met.

## Deferred Ideas

- None. Broader milestone marker reconciliation, Phase 5 artifact rewriting, command launch/MO2 work, orchestrator reverification, and new UI test infrastructure remained outside Phase 12.
