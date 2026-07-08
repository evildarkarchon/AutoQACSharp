# Phase 15: Stop Escalation Ownership Closure - Context

**Gathered:** 2026-05-01
**Status:** Ready for planning

<domain>
## Phase Boundary

Phase 15 closes the confirmed Progress-window Stop force-escalation ownership gap. After `GracePeriodExpired`, the later confirmed `Force Terminate` action must still have the original xEdit target available, force-kill it in the controlled success case, or report the shared safe `ForceKillFailed` outcome when the target cannot be killed or cannot be proven exited. This phase is a narrow stop/process ownership and evidence phase, not a broad termination redesign.

</domain>

<spec_lock>
## Requirements (locked via SPEC.md)

**5 requirements are locked.** See `15-SPEC.md` for full requirements, boundaries, and acceptance criteria.

Downstream agents MUST read `15-SPEC.md` before planning or implementing. Requirements are not duplicated here.

**In scope (from SPEC.md):**
- Progress-window Stop flow after `GracePeriodExpired`, affirmative `Force Terminate`, runner detach or process execution return, and final force-kill/failure outcome.
- Minimal shared stop/termination ownership changes required so confirmed force escalation still has a real target after detach.
- Persistent Progress-window warning and shared safe force-failure dialog behavior for genuine force-kill failures.
- Automated unit/integration evidence for the detached escalation sequence and relevant stop/process/PID behavior.
- Phase 15 verification evidence for `SAF-01`, `SAF-02`, `TEST-01`, `INT-STOP-01`, and `FLOW-STOP-ESCALATION-01`.

**Out of scope (from SPEC.md):**
- Broad termination architecture rewrite - this phase closes one ownership gap, not a new decomposition pass.
- Redesigning timeout-driven cleanup, orphan cleanup, backup cancellation, or Hang Kill behavior - they are adjacent stop/process flows and are not the audit blocker.
- Command launch escaping or MO2 missing-path behavior - Phase 13 owns that scope.
- Milestone marker reconciliation in `ROADMAP.md` or `REQUIREMENTS.md` - Phase 16 owns final audit/marker reconciliation.
- Adding Avalonia.Headless or new UI test infrastructure - this repository does not currently include a headless UI test project.
- Parallelizing plugin cleaning or external process execution - sequential xEdit cleaning and the single process slot remain hard runtime requirements.
- Mutagen or `QueryPlugins` changes - Phase 15 concerns AutoQAC stop escalation only.

</spec_lock>

<decisions>
## Implementation Decisions

### Target Ownership
- **D-01:** The primary fix is to retain the original `System.Diagnostics.Process` handle that was attached when xEdit started. Do not make PID/start-time recovery the required mechanism for Phase 15.
- **D-02:** The retained force-termination target must survive runner detach and normal session finalization until the user resolves the `GracePeriodExpired` state by choosing `Force Terminate`, choosing `Leave Running`, the process naturally exiting, or a next-session cleanup/reset boundary safely releasing it.
- **D-03:** Keep the pending force-escalation target separate from the normal active-process slot. `DetachProcess()` may stop hang monitoring and clear active-process semantics, but it must not discard the pending target needed for confirmed force escalation.
- **D-04:** `HasActiveProcess`, hang monitoring, backup-cancel gating, and current-plugin active state should continue to represent active cleaning. The pending escalation target represents a left-running process awaiting user resolution, not an active cleaning attempt.
- **D-05:** PID/start-time fallback is optional only if research or implementation proves the retained-handle approach cannot cover a required timing edge. Existing PID evidence remains relevant for orphan cleanup and process tests, but Phase 15 should not broaden into a PID recovery feature by default.

### No-Target Outcome
- **D-06:** After the user confirms `Force Terminate`, `ForceStopAsync` / `ForceStopCleaningAsync` must never silently return the prior cached `GracePeriodExpired` result. Confirmed force escalation must produce a terminal outcome: `ForceKilled`, `AlreadyExited`, or `ForceKillFailed`.
- **D-07:** If the retained target can be proven already exited before the confirmed force kill runs, return/report `AlreadyExited` without showing force-failure copy. There is no longer a running xEdit process to kill.
- **D-08:** If the target is unavailable, disposed, invalid, inaccessible, or cannot prove it already exited after the user confirms `Force Terminate`, map that path to `ForceKillFailed` so the shared safe failure dialog and persistent Progress warning are used.
- **D-09:** Do not add new user-facing copy for detached-target or invalid-target cases. Use `StopTerminationDialogContent.ForceFailureTitle` and `StopTerminationDialogContent.ForceFailureMessage` for all confirmed force-failure outcomes.

### Test Proof Shape
- **D-10:** Require layered automated proof for this phase: coordinator/service ownership behavior, controlled helper-process force-kill behavior, and Progress ViewModel confirmation-order/failure-visibility behavior.
- **D-11:** The real process detached-escalation proof should be coordinator-focused: attach a controlled helper process, drive `GracePeriodExpired`, call `DetachProcess()`, call `ForceStopAsync()`, then assert the helper exits and the result is `ForceKilled` in the controlled success case.
- **D-12:** Extend or preserve the Progress ViewModel confirmation-order test so it holds the confirmation dialog open and verifies `ForceStopCleaningAsync()` is not called before the dialog returns affirmative confirmation.
- **D-13:** Use deterministic mocked failure plus UI proof for the confirmed detached force-failure path: the coordinator/orchestrator returns `ForceKillFailed` for invalid or unavailable target conditions, and `ProgressViewModel` shows the shared force-failure dialog plus persistent warning.
- **D-14:** Do not add Avalonia.Headless or real xEdit/MO2 setup. Use the existing helper-process and ViewModel/service test patterns.

### Verification Artifact
- **D-15:** `15-VERIFICATION.md` is mandatory. The planner may decide whether a separate `15-VALIDATION.md` is useful, but `15-VERIFICATION.md` must contain the final requirement and audit-gap closure evidence.
- **D-16:** The verification evidence must explicitly map `SAF-01`, `SAF-02`, `TEST-01`, `INT-STOP-01`, `FLOW-STOP-ESCALATION-01`, and every `15-SPEC.md` acceptance criterion to source/test evidence and status.
- **D-17:** Record test evidence as concise command rows: command, scope, result, relevant test names, and relevant source files. Do not paste long command output unless documenting a failure.
- **D-18:** Marker edits are planner discretion only inside locked SPEC boundaries. Because `15-SPEC.md` puts milestone marker reconciliation for `ROADMAP.md` and `REQUIREMENTS.md` out of scope and assigns it to Phase 16, Phase 15 should default to writing current evidence only unless a registered GSD state/roadmap workflow explicitly requires a scoped update.

### the agent's Discretion
- Exact field, helper, DTO, and method names for the pending escalation target are planner discretion.
- Exact production file split is planner discretion, but the smallest likely surface is `ICleaningTerminationCoordinator` / `CleaningTerminationCoordinator` plus tests and any necessary orchestrator contract adjustments.
- Exact test names and filtered test command syntax are planner discretion as long as the evidence rows remain traceable by concern.
- Exact `15-VALIDATION.md` decision is planner discretion; `15-VERIFICATION.md` cannot be skipped.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase Scope And Requirements
- `.planning/phases/15-stop-escalation-ownership-closure/15-SPEC.md` - Locked Phase 15 goal, requirements, boundaries, constraints, acceptance criteria, and interview log. MUST read first.
- `.planning/ROADMAP.md` - Phase 15 goal, dependency on Phase 12, mapped requirements, gap-closure IDs, and success criteria.
- `.planning/REQUIREMENTS.md` - Current definitions and pending traceability for `SAF-01`, `SAF-02`, and `TEST-01`.
- `.planning/PROJECT.md` - Cleanup milestone intent, hard sequential xEdit constraint, MVVM boundary, read-only `Mutagen/` constraint, and carried-forward stop/process decisions.
- `.planning/STATE.md` - Current project state and accumulated decisions from Phases 10-14.

### Audit Gap Source
- `.planning/v1.0-MILESTONE-AUDIT.md` - Source of `SAF-01`, `SAF-02`, `TEST-01`, `INT-STOP-01`, and `FLOW-STOP-ESCALATION-01` gaps. Lines 28-64 explain the requirement failures; lines 180-190 describe the integration and flow blockers.
- `.planning/phases/12-process-stop-verification-progress-flow-closure/12-CONTEXT.md` - Prior locked decisions for shared stop copy, confirmation ordering, Progress warning visibility, Hang Kill boundary, and verification source-of-truth policy.
- `.planning/phases/12-process-stop-verification-progress-flow-closure/12-VERIFICATION.md` - Historical current-evidence artifact that Phase 15 supersedes for the detached escalation ownership gap.
- `.planning/phases/11-user-facing-diagnostics-boundaries/11-CONTEXT.md` - Safe user-facing diagnostics and log-boundary decisions that still constrain force-failure copy and logs.

### Codebase Maps
- `.planning/codebase/ARCHITECTURE.md` - Stop/termination flow, MVVM/dialog ownership, process execution constraints, state hub, single process slot, and no direct process launch from UI.
- `.planning/codebase/TESTING.md` - xUnit, FluentAssertions, NSubstitute, helper-process, finite-timeout, `IDisposable` cleanup, and no Avalonia.Headless test project patterns.
- `.planning/codebase/CONCERNS.md` - Stop/force-stop race handling as a fragile area, sequential cleaning invariant, and current testing gaps around real external tools.

### Recent Artifact Patterns
- `.planning/phases/14-orchestrator-decomposition-reverification/14-CONTEXT.md` - Recent verification-only closure pattern with concise command rows and no marker edits.
- `.planning/phases/13-command-launch-escaping-reverification-safe-mo2-failures/13-CONTEXT.md` - Recent validation-plus-verification pattern if planner chooses a separate validation artifact.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs` - Owns `_currentProcess`, `_lastTerminationResult`, stop state, hang monitoring, `StopAsync`, `ForceStopAsync`, and `DetachProcess`. Current bug: `DetachProcess()` clears `_currentProcess`; `ForceStopAsync()` returns cached `_lastTerminationResult` when no process is current.
- `AutoQAC/Services/Cleaning/ICleaningTerminationCoordinator.cs` - Public termination contract. Likely needs minimal documentation/API adjustment if a separate pending escalation target changes visible behavior.
- `AutoQAC/Services/Cleaning/PluginCleaningRunner.cs` - Calls `attachProcess` per xEdit start and `detachProcess` once in a trailing `finally` after the retry loop. This is the code path that currently drops the force target before confirmed escalation.
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` - `StopCleaningAsync()` and `ForceStopCleaningAsync()` cancel the session CTS before delegating to the termination coordinator. `StartCleaningAsync()` resets termination state in `finally`, so pending escalation lifetime must be considered across session finalization.
- `AutoQAC/Services/Process/ProcessExecutionService.cs` - User cancellation maps to graceful termination and preserves PID evidence when `GracePeriodExpired`; force kill uses `Process.Kill(entireProcessTree: true)` and maps real kill/wait failures to `ForceKillFailed`.
- `AutoQAC/ViewModels/ProgressViewModel.cs` - Prompts on `GracePeriodExpired`, calls `ForceStopCleaningAsync()` only after affirmative `Force Terminate`, and reports `ForceKillFailed` through the shared dialog and persistent `StopOutcomeWarningText`.
- `AutoQAC/Models/StopTerminationDialogContent.cs` - Shared exact copy and button labels for Stop confirmation, force failure, and left-running warning.
- `AutoQAC.Tests/Services/Cleaning/CleaningTerminationCoordinatorTests.cs` - Natural home for coordinator-level pending-target and detached force-stop tests; already starts sleeper processes and performs best-effort cleanup.
- `AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs` - Existing controlled helper-process evidence for user cancellation preserving a running process and PID evidence, plus force-kill failure mapping.
- `AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs` - Existing confirmation-order and force-failure UI tests to extend for Phase 15.

### Established Patterns
- Cleaning remains sequential. Do not parallelize plugin loops, process slots, backup-before-xEdit work, or external process launches.
- Process launch and termination stay in services; ViewModels request orchestration and dialogs but do not manipulate processes directly.
- User-facing stop failure copy must stay Phase 11-safe and shared through `StopTerminationDialogContent`.
- Tests should use finite async waits, helper processes, `IDisposable` cleanup, FluentAssertions, and NSubstitute optional-parameter matching.
- Real xEdit/MO2 execution and Avalonia.Headless infrastructure are not required for this phase.
- Microsoft .NET process docs relevant to planner research: `Kill` is asynchronous and should be followed by exit wait/check; `GetProcessById` only rebinds while the PID is running and throws for expired IDs; `HasExited`/process APIs can throw when no process is associated.

### Integration Points
- `DetachProcess()` should detach active cleaning/hang monitoring without discarding a pending escalation target when `GracePeriodExpired` means xEdit may still be running.
- `ForceStopAsync()` should prefer the pending escalation target when no active process is current and the previous result indicates `MayProcessStillBeRunning`.
- `MarkLeftRunningByUser()` should resolve/release the pending escalation target consistently with the user's `Leave Running` choice.
- `ResetForNewSession()` must not leak stale stop UI/hang state across sessions, but it must also not erase the pending target before the user can resolve the already-shown confirmation.
- `ProgressViewModel` should not need new user-facing copy if coordinator/orchestrator returns terminal `ForceKilled`, `AlreadyExited`, or `ForceKillFailed` results.

</code_context>

<specifics>
## Specific Ideas

- Suggested coordinator success test shape: `StopAsync` returns `GracePeriodExpired`, `DetachProcess()` runs, `ForceStopAsync()` is confirmed later, and a controlled helper process exits with `TerminationResult.ForceKilled`.
- Suggested invalid-target test shape: after confirmed force escalation, if the retained target is invalid/unavailable and not provably exited, the coordinator returns `ForceKillFailed` and the Progress ViewModel displays `StopTerminationDialogContent.ForceFailureTitle` / `ForceFailureMessage`.
- Suggested Progress confirmation test shape: reuse or extend `StopCommand_WhenGracePeriodExpires_ShouldPromptBeforeForceStop` so the dialog's `TaskCompletionSource` remains pending while `ForceStopCleaningAsync()` is asserted not received.
- Suggested focused command groups: `CleaningTerminationCoordinatorTests`, `ProgressViewModelTests`, `ProcessExecutionIntegrationTests`, and any touched `CleaningOrchestratorTests`, followed by `dotnet test AutoQACSharp.slnx --nologo`.
- `15-VERIFICATION.md` should state explicitly that Phase 15 supersedes the stale Phase 12 stop-escalation evidence only for the detached ownership gap; it should not rewrite Phase 12 artifacts.

</specifics>

<deferred>
## Deferred Ideas

None - discussion stayed within Phase 15 scope. The following remain explicitly outside Phase 15: broad termination redesign, timeout/orphan cleanup redesign, Hang Kill redesign, command launch/MO2 work, Avalonia.Headless or real UI automation infrastructure, real xEdit/MO2 harnesses, Mutagen/QueryPlugins changes, and roadmap/requirements/audit marker reconciliation owned by Phase 16.

</deferred>

---

*Phase: 15-stop-escalation-ownership-closure*
*Context gathered: 2026-05-01*
