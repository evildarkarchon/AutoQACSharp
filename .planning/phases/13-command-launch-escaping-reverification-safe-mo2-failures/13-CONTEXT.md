# Phase 13: Command Launch Escaping Reverification & Safe MO2 Failures - Context

**Gathered:** 2026-05-01
**Status:** Ready for planning

<domain>
## Phase Boundary

Phase 13 produces current evidence that command launch escaping and safe MO2 failure behavior satisfy `SAF-03` and `TEST-02`. This is a gap-closure and re-verification phase: it should create fresh Phase 13 validation/verification artifacts, prove current code behavior with targeted and full test evidence, and avoid broad command-launch redesign unless a targeted test reveals a real gap.

</domain>

<spec_lock>
## Requirements (locked via SPEC.md)

**5 requirements are locked.** See `13-SPEC.md` for full requirements, boundaries, and acceptance criteria.

Downstream agents MUST read `13-SPEC.md` before planning or implementing. Requirements are not duplicated here.

**In scope (from SPEC.md):**
- Fresh Phase 13 verification/validation evidence for `SAF-03` and `TEST-02`.
- Confirmation that missing MO2 configuration in MO2 mode fails before any direct xEdit or other process launch.
- Reverification of direct xEdit difficult-character `ArgumentList` behavior.
- Reverification of configured MO2 wrapper and nested-payload escaping behavior.
- Reverification that launch-failure user-facing diagnostics omit raw paths, raw command fragments, and nested MO2 payloads.
- Minimal code or test changes only if current evidence reveals a real gap.

**Out of scope (from SPEC.md):**
- Rewriting Phase 6 verification or validation documents - Phase 13 produces fresh current evidence instead of changing historical records.
- Adding new command-launch features or changing xEdit/MO2 argv contracts - this phase re-verifies existing guarantees.
- Real xEdit or real MO2 smoke-test automation - automated verification uses command-builder, process-helper, and failure-flow tests available in the repo.
- Broader user-facing diagnostics hardening outside launch-failure paths - Phase 11 owns repository-wide diagnostics boundaries.
- Process stop, PID tracking, or termination behavior changes - Phase 12 already covered stop/PID evidence.
- Orchestrator decomposition reverification - Phase 14 owns current evidence for orchestrator structure and session guarding.
- SEC-03 executable-name warning behavior - that requirement is listed under Future Requirements.

</spec_lock>

<decisions>
## Implementation Decisions

### Evidence Shape
- **D-01:** Create both `13-VALIDATION.md` and `13-VERIFICATION.md`. `13-VALIDATION.md` is the acceptance-criterion evidence matrix; `13-VERIFICATION.md` is the final requirement conclusion report.
- **D-02:** Structure `13-VALIDATION.md` by SPEC acceptance criterion. Each row should record the criterion, requirement link, evidence source, command or inspection method, result, and pass/fail status.
- **D-03:** `13-VERIFICATION.md` must explicitly conclude whether `SAF-03` and `TEST-02` are satisfied, and trace those conclusions to `13-VALIDATION.md` rows and the stale audit gaps they close.
- **D-04:** If current source/tests already satisfy the SPEC and no production/test changes are needed, state that Phase 13 is evidence-only. Do not manufacture code changes just to make the phase feel active.

### Test Sweep
- **D-05:** Require targeted evidence plus a full solution test run before marking `SAF-03` and `TEST-02` satisfied.
- **D-06:** The mandatory targeted groups are command builder, process-boundary, and cleaning failure diagnostics: `XEditCommandBuilderTests`, `ProcessExecutionIntegrationTests`, and `CleaningServiceTests`.
- **D-07:** If the full solution suite fails while all Phase 13 targeted tests pass, classify the full-suite failure separately. Phase 13 may still be marked satisfied only when the failure is clearly unrelated and documented with command output and rationale.
- **D-08:** If a targeted Phase 13 test fails, make the smallest code or test fix needed for the SPEC gap, then rerun the targeted evidence and full solution evidence.

### Audit And Marker Boundaries
- **D-09:** Update `STATE.md` session information through the workflow only. Do not update `REQUIREMENTS.md`, `ROADMAP.md`, or milestone audit status markers during Phase 13.
- **D-10:** Treat `.planning/v1.0-MILESTONE-AUDIT.md` as the source of the stale `SAF-03`/`TEST-02` findings and cite it, but do not edit the audit file.
- **D-11:** Treat Phase 6 artifacts such as `06-VERIFICATION.md` and `06-04-SUMMARY.md` as historical inputs. Cite them to explain the old gap and later gap closure, but do not edit or annotate those historical files.
- **D-12:** Phase 13 artifacts should explicitly state that roadmap/requirements/audit marker reconciliation is deferred to milestone completion or the appropriate GSD state/roadmap workflow.

### Real-Tool Assumptions
- **D-13:** Do not require real xEdit or real ModOrganizer execution for Phase 13 to pass. Automated evidence should use the repo-controlled command-builder, process-helper, and cleaning-service seams.
- **D-14:** Frame missing real xEdit/MO2 smoke coverage as a non-blocking external integration risk: AutoQAC argv/seam behavior is verified, while real external tool parser behavior remains outside automated coverage.
- **D-15:** Include only a short optional human smoke-test note. Do not add a detailed manual procedure or new real-tool harness in this phase.

### the agent's Discretion
- Exact `13-VALIDATION.md` columns, row IDs, and section names are planner discretion as long as each SPEC acceptance criterion maps to evidence and status.
- Exact targeted test command syntax is planner discretion. Separate commands or an equivalent combined filter are acceptable if the evidence clearly covers `XEditCommandBuilderTests`, `ProcessExecutionIntegrationTests`, and `CleaningServiceTests`.
- Placement of the real-tool limitation note is planner discretion. It may appear in `13-VALIDATION.md`, `13-VERIFICATION.md`, or both, but it must be visible to downstream verifiers.
- Exact wording of the evidence-only statement, unrelated-failure rationale, and optional smoke-test note is planner discretion, subject to Phase 11-safe diagnostics boundaries.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase Scope And Requirements
- `.planning/phases/13-command-launch-escaping-reverification-safe-mo2-failures/13-SPEC.md` - Locked Phase 13 requirements, boundaries, constraints, acceptance criteria, and interview decisions. MUST read first.
- `.planning/ROADMAP.md` - Phase 13 goal, mapped requirements, gap-closure note, and success criteria.
- `.planning/REQUIREMENTS.md` - `SAF-03` and `TEST-02` definitions and traceability.
- `.planning/PROJECT.md` - Cleanup milestone intent, project constraints, sequential xEdit requirement, and key decisions.
- `.planning/v1.0-MILESTONE-AUDIT.md` - Source audit finding that still records `SAF-03` and `TEST-02` as unsatisfied based on stale Phase 6 verification.

### Historical Command-Launch Evidence
- `.planning/phases/06-command-launch-escaping/06-VERIFICATION.md` - Historical gaps_found report identifying missing-MO2 fallback and unexpected launch exception disclosure gaps.
- `.planning/phases/06-command-launch-escaping/06-04-SUMMARY.md` - Historical gap-closure summary stating missing-MO2 fallback and safe exception messaging were fixed.
- `.planning/phases/06-command-launch-escaping/06-CONTEXT.md` - Prior locked command-launch decisions, including `ArgumentList`, MO2 nested payload sensitivity, and command/payload redaction expectations.

### Carried-Forward Decisions
- `.planning/phases/11-user-facing-diagnostics-boundaries/11-CONTEXT.md` - Safe diagnostics and process/log boundary decisions that apply to launch-failure evidence and wording.
- `.planning/phases/12-process-stop-verification-progress-flow-closure/12-CONTEXT.md` - Fresh current-evidence pattern and decision to avoid rewriting older phase artifacts.

### Codebase Constraints And Existing Patterns
- `.planning/codebase/TESTING.md` - xUnit, FluentAssertions, NSubstitute, helper-process, targeted filter, and full-suite evidence patterns.
- `.planning/codebase/CONVENTIONS.md` - C# style, service/test naming, logging, XML docs, and comments conventions.
- `.planning/codebase/CONCERNS.md` - User-configured process launch boundary, MO2 nested parser risk, sequential cleaning invariant, and real xEdit/MO2 integration test gap.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs` - Builds direct xEdit and MO2 `ProcessStartInfo` contracts. Current MO2 mode returns `null` when `Mo2ExecutablePath` is null, empty, or whitespace, preventing direct xEdit fallback.
- `AutoQAC/Services/Cleaning/CleaningService.cs` - Converts null command construction into a safe failed `CleaningResult` before process execution, logs launch context with safe fields, and returns safe plugin-scoped messages for unexpected exceptions.
- `AutoQAC/Services/Process/ProcessExecutionService.cs` - Preserves caller-supplied `ArgumentList` through `CloneStartInfoForLaunch`, falls back to legacy `Arguments` only when no `ArgumentList` entries exist, and logs argument counts rather than raw payloads.
- `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` - Existing pre-clean validation for missing MO2 configuration shows safe `MO2 Path` identifiers and directs the user to configure MO2 or disable MO2 Mode.
- `AutoQAC.Tests/Services/XEditCommandBuilderTests.cs` - Existing tests cover direct difficult-character argv, configured MO2 wrapper/nested payload behavior, and null/empty/whitespace missing-MO2 command-build failure.
- `AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs` - Existing helper-process tests verify exact `ArgumentList` preservation through real process start and legacy `Arguments` fallback.
- `AutoQAC.Tests/Services/CleaningServiceTests.cs` - Existing tests cover safe command-build failures, MO2 no-process-started behavior, mocked launch-start failure copy, and unexpected launch exception non-disclosure.

### Established Patterns
- Command construction uses `ProcessStartInfo.ArgumentList` for direct argv preservation and a single nested string only at MO2's required `-a` parser boundary.
- `UseShellExecute = false` is part of the command-launch safety contract and should stay asserted in tests.
- User-facing launch diagnostics follow Phase 11 boundaries: concise copy, safe plugin names, no configured executable paths, no raw argv payloads, no nested MO2 payloads, and latest-log guidance when technical details are needed.
- Evidence artifacts should map requirements to tests/results and explicitly list assumptions, limitations, and unrelated failures.
- Sequential xEdit cleaning remains fixed. Verification must not introduce parallel cleaning, parallel process slots, or new process-launch paths.

### Integration Points
- `13-VALIDATION.md` should draw evidence from the existing command-builder, process integration, and cleaning-service tests, plus source inspection where helpful.
- `13-VERIFICATION.md` should consume `13-VALIDATION.md` and summarize requirement status for `SAF-03` and `TEST-02`.
- `STATE.md` should be updated only through the workflow/state command after context capture; milestone marker reconciliation stays out of Phase 13.
- If targeted evidence finds a gap, the planner should touch only the minimal source/test files needed to satisfy `13-SPEC.md` and then rerun the evidence commands.

</code_context>

<specifics>
## Specific Ideas

- Suggested targeted evidence commands may be separate commands for `FullyQualifiedName~XEditCommandBuilderTests`, `FullyQualifiedName~ProcessExecutionIntegrationTests`, and `FullyQualifiedName~CleaningServiceTests`, followed by `dotnet test AutoQACSharp.slnx`.
- `13-VALIDATION.md` should be easy to audit from SPEC checkbox to command output: acceptance criterion -> evidence -> status.
- `13-VERIFICATION.md` should explicitly say whether Phase 13 was evidence-only if no source or test changes were required.
- The real-tool note should be short and direct: automated tests verify AutoQAC command construction and process seams; real xEdit/MO2 parser behavior is not executed and remains a non-blocking external integration risk.

</specifics>

<deferred>
## Deferred Ideas

None from discussion. The following remain explicitly outside Phase 13: real xEdit/MO2 automated harnesses, detailed manual smoke-test procedures, Phase 6 artifact rewrites, milestone audit edits, roadmap/requirements marker reconciliation, SEC-03 executable-name warnings, process stop/PID changes, and orchestrator decomposition reverification.

</deferred>

---

*Phase: 13-command-launch-escaping-reverification-safe-mo2-failures*
*Context gathered: 2026-05-01*
