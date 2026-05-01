# Phase 13: Command Launch Escaping Reverification & Safe MO2 Failures - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md - this log preserves the alternatives considered.

**Date:** 2026-05-01
**Phase:** 13-command-launch-escaping-reverification-safe-mo2-failures
**Areas discussed:** Evidence shape, Test sweep, Audit markers, Real-tool assumptions

---

## Evidence Shape

### Artifact Set

| Option | Description | Selected |
|--------|-------------|----------|
| Verification + validation | Create `13-VERIFICATION.md` for requirement conclusions plus `13-VALIDATION.md` for command/test evidence. Recommended for traceability. | ✓ |
| Verification only | Create one `13-VERIFICATION.md` containing all requirement mapping, commands, and evidence. | |
| Validation only | Use `13-VALIDATION.md` as the single evidence artifact and skip a separate verification report. | |
| You decide | Let downstream agents choose the minimal artifact set that satisfies SPEC.md. | |

**User's choice:** Verification + validation
**Notes:** Best-practices research emphasized traceability from requirements to concrete evidence and pass/fail status.

### Validation Granularity

| Option | Description | Selected |
|--------|-------------|----------|
| By acceptance criterion | Map each SPEC checkbox to evidence, command, result, and status. Recommended for verifier usability. | ✓ |
| By test command | List commands first, then summarize which requirements each command covers. | |
| By source area | Group evidence by command builder, process layer, cleaning service, and docs. | |
| You decide | Let the planner choose the validation matrix layout. | |

**User's choice:** By acceptance criterion
**Notes:** `13-VALIDATION.md` should be directly auditable from SPEC checkbox to evidence.

### Verification Conclusion

| Option | Description | Selected |
|--------|-------------|----------|
| SAF-03/TEST-02 satisfied | State both requirement IDs are satisfied, with traceability to validation rows and audit gaps. Recommended. | ✓ |
| Spec requirements only | Verify the five SPEC requirements but avoid requirement-ID conclusions. | |
| Audit closure only | Focus on closing the milestone audit entries, with less detail on SPEC requirement rows. | |
| You decide | Let the planner decide how explicit the final conclusion should be. | |

**User's choice:** SAF-03/TEST-02 satisfied
**Notes:** `13-VERIFICATION.md` must make the requirement-ID status explicit.

### Evidence-Only Phase

| Option | Description | Selected |
|--------|-------------|----------|
| Yes, state evidence-only | Record that no production changes are needed when current code/tests already satisfy the SPEC. Recommended. | ✓ |
| No, avoid emphasis | Just present the verification result; do not call attention to whether code changed. | |
| Only if no diffs | Mention evidence-only only when the final git diff has no source/test changes. | |
| You decide | Let downstream agents decide whether this needs calling out. | |

**User's choice:** Yes, state evidence-only
**Notes:** Do not manufacture code changes if current implementation and tests already satisfy the phase.

---

## Test Sweep

### Command Set

| Option | Description | Selected |
|--------|-------------|----------|
| Targeted plus full | Run command-builder, process-boundary, cleaning failure diagnostics, then full solution. Recommended for current evidence. | ✓ |
| Targeted only | Run only focused tests tied directly to Phase 13 acceptance criteria. | |
| Full suite only | Rely on the full solution test run to cover all relevant evidence. | |
| You decide | Let downstream agents choose exact command coverage. | |

**User's choice:** Targeted plus full
**Notes:** Targeted tests provide requirement-specific proof; full suite provides regression confidence.

### Mandatory Focused Groups

| Option | Description | Selected |
|--------|-------------|----------|
| Builder/process/cleaning | Require `XEditCommandBuilderTests`, `ProcessExecutionIntegrationTests`, and `CleaningServiceTests`. Recommended. | ✓ |
| Add Phase 11 sentinels | Also require Phase 11 log/diagnostic sentinel tests because launch diagnostics are in scope. | |
| All AutoQAC service tests | Run every AutoQAC service test as targeted evidence before the full suite. | |
| You decide | Let the planner choose targeted filters based on current test names. | |

**User's choice:** Builder/process/cleaning
**Notes:** The selected test groups map directly to command construction, process argument preservation, and launch-failure diagnostics.

### Unrelated Full-Suite Failures

| Option | Description | Selected |
|--------|-------------|----------|
| Document unrelated separately | Mark Phase 13 evidence satisfied only if failures are clearly unrelated and documented with command output. Recommended. | ✓ |
| Block Phase 13 | Any full-suite failure blocks Phase 13, even if unrelated. | |
| Skip full suite | Avoid the classification problem by not requiring full solution evidence. | |
| You decide | Let downstream agents decide based on failure details. | |

**User's choice:** Document unrelated separately
**Notes:** Unrelated failures must be explicit; they cannot be hidden under a passing targeted test claim.

### Targeted Test Failure Policy

| Option | Description | Selected |
|--------|-------------|----------|
| Fix minimal gap | Make the smallest code/test fix in Phase 13, rerun targeted and full evidence. Recommended. | ✓ |
| Stop for replan | Do not fix in execution; stop and require a new plan/context update. | |
| Document as gap | Write verification with gaps_found instead of fixing. | |
| You decide | Let downstream agents decide based on the failure. | |

**User's choice:** Fix minimal gap
**Notes:** Any fix must stay scoped to the SPEC and avoid broad redesign.

---

## Audit Markers

### Status File Updates

| Option | Description | Selected |
|--------|-------------|----------|
| State only | Update `STATE.md` session info via workflow, but leave roadmap/requirements/audit markers for milestone completion. Recommended. | ✓ |
| Requirements too | Also mark `SAF-03` and `TEST-02` complete in `REQUIREMENTS.md`. | |
| All markers | Update `REQUIREMENTS.md`, `ROADMAP.md`, and milestone audit entries immediately. | |
| You decide | Let downstream agents choose marker updates. | |

**User's choice:** State only
**Notes:** Phase 13 evidence closes the gap; marker reconciliation remains a later workflow responsibility.

### Milestone Audit Handling

| Option | Description | Selected |
|--------|-------------|----------|
| Reference, don't edit | Cite its stale SAF-03/TEST-02 findings as the source gap, but do not change the audit file. Recommended. | ✓ |
| Append note | Add a Phase 13 closure note to the audit without rewriting old findings. | |
| Rewrite entries | Change the unsatisfied entries to satisfied after Phase 13 verification. | |
| You decide | Let downstream agents choose the audit update boundary. | |

**User's choice:** Reference, don't edit
**Notes:** `.planning/v1.0-MILESTONE-AUDIT.md` remains historical/source evidence.

### Phase 6 Artifact Handling

| Option | Description | Selected |
|--------|-------------|----------|
| Historical inputs | Cite `06-VERIFICATION.md` and `06-04-SUMMARY.md` as history; do not edit them. Recommended. | ✓ |
| Current refs only | Avoid referencing Phase 6 artifacts except where absolutely needed. | |
| Annotate Phase 6 | Add notes to Phase 6 files pointing to Phase 13 evidence. | |
| You decide | Let downstream agents decide how much Phase 6 history to cite. | |

**User's choice:** Historical inputs
**Notes:** Phase 13 should explain the old gap and later gap closure without modifying Phase 6 history.

### Deferred Marker Reconciliation

| Option | Description | Selected |
|--------|-------------|----------|
| Yes, explicit defer | State roadmap/requirements/audit marker reconciliation is deferred to milestone completion or the appropriate state workflow. Recommended. | ✓ |
| No, implicit only | Do not mention marker reconciliation unless someone asks later. | |
| Only in discussion log | Record this decision in discussion log, but keep verification focused on evidence. | |
| You decide | Let downstream agents decide where to mention the deferment. | |

**User's choice:** Yes, explicit defer
**Notes:** Phase 13 artifacts should make this boundary visible.

---

## Real-Tool Assumptions

### Real xEdit/MO2 Requirement

| Option | Description | Selected |
|--------|-------------|----------|
| No, document gap | Do not require real tools; rely on automated seam tests and document real-tool smoke testing as a non-blocking gap. Recommended. | ✓ |
| Optional if available | Run real-tool smoke tests only when the developer has xEdit/MO2 configured locally. | |
| Required smoke test | Block Phase 13 until a real xEdit/MO2 smoke test is completed. | |
| You decide | Let downstream agents choose based on local environment. | |

**User's choice:** No, document gap
**Notes:** Phase 13 should pass on repo-controlled automated evidence without external tool installation.

### Limitation Placement

| Option | Description | Selected |
|--------|-------------|----------|
| Both artifacts | Record it in `13-VALIDATION.md` as coverage limitation and `13-VERIFICATION.md` as accepted non-blocking assumption. Recommended. | |
| Verification only | Mention it only in the final verification report. | |
| Validation only | Mention it only next to the automated test evidence. | |
| You decide | Let downstream agents decide where the note belongs. | ✓ |

**User's choice:** You decide
**Notes:** Placement is agent discretion, but the limitation must be visible.

### Limitation Framing

| Option | Description | Selected |
|--------|-------------|----------|
| Non-blocking risk | State automation proves AutoQAC argv/seam behavior, while real tool parser behavior remains a non-blocking external integration risk. Recommended. | ✓ |
| Deferred requirement | Treat real xEdit/MO2 smoke coverage as a deferred future requirement. | |
| Human checklist | Provide a manual smoke-test checklist but do not classify risk. | |
| You decide | Let downstream agents decide the wording. | |

**User's choice:** Non-blocking risk
**Notes:** This avoids blocking Phase 13 while preserving transparency.

### Manual Smoke Guidance

| Option | Description | Selected |
|--------|-------------|----------|
| Short optional note | Include a concise optional smoke note only; do not add a new manual procedure. Recommended. | ✓ |
| Full checklist | Add a detailed real xEdit/MO2 manual checklist for later human validation. | |
| No instructions | Only record the limitation, with no smoke-test guidance. | |
| You decide | Let downstream agents decide whether instructions help. | |

**User's choice:** Short optional note
**Notes:** No new harness or detailed manual procedure should be added in Phase 13.

---

## the agent's Discretion

- Exact `13-VALIDATION.md` columns, row IDs, and section names.
- Exact targeted test command syntax, as long as command-builder, process-boundary, and cleaning failure diagnostics are covered.
- Placement of the real-tool limitation note between `13-VALIDATION.md`, `13-VERIFICATION.md`, or both.
- Exact wording of evidence-only, unrelated-failure, and optional smoke-test notes.

## Deferred Ideas

- Real xEdit/MO2 automated harnesses remain outside Phase 13.
- Detailed manual smoke-test procedures remain outside Phase 13.
- Phase 6 artifact rewrites and milestone audit edits remain outside Phase 13.
- Roadmap/requirements marker reconciliation is deferred to milestone completion or the appropriate GSD workflow.
