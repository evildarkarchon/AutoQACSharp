---
phase: 8
reviewers: [gemini, claude, codex]
reviewed_at: 2026-04-29T17:50:00Z
plans_reviewed:
  - 08-01-PLAN.md
  - 08-02-PLAN.md
  - 08-03-PLAN.md
  - 08-04-PLAN.md
  - 08-05-PLAN.md
  - 08-06-PLAN.md
risk_summary:
  gemini: LOW
  claude: MEDIUM
  codex: MEDIUM
---

# Cross-AI Plan Review — Phase 8

> Three external AI CLIs (Gemini, Claude, Codex) independently reviewed the six Phase 8 plans, the CONTEXT.md decisions, the RESEARCH.md seam map, and the validation strategy. The reviews below are unedited from each CLI; the consensus summary at the bottom synthesizes shared findings.

---

## Gemini Review

# Phase 8 Plan Review: Cleaning Orchestrator Decomposition

## Summary
The implementation plan for Phase 8 is an exceptionally well-structured, behavior-preserving refactor that successfully addresses the monolithic concentration of responsibilities in `CleaningOrchestrator`. By following a "characterize-then-extract" strategy (D-05), the plan minimizes regression risk for high-stakes logic like Phase 5 termination locks and Phase 7 backup safety. The sequence of waves (0 through 5) logically moves from the least-coupled logic (Preflight) to the most-coupled state (Termination), culminating in a thin facade that satisfies the maintainability requirements of `REF-01`.

## Strengths
- **Characterization Net (Wave 0):** Plan 08-01 identifies and closes 6 specific coverage gaps (e.g., `LeftRunningByUser`, retention warnings) identified during research, ensuring the baseline is truly "locked" before the first line of production code changes.
- **Dependency Isolation:** The use of delegates (`attachProcess`/`detachProcess`) in `IPluginCleaningRunner` (Plan 08-05) is an excellent architectural choice to avoid circular dependencies with the `CleaningTerminationCoordinator` while keeping the runner stateless.
- **Single Source of Truth for Preflight:** Consolidating dry-run and real-run logic into `ICleaningPreflight.PrepareAsync` (Plan 08-02) effectively eliminates the "logic drift" risk between the preview UI and actual cleaning.
- **Invariant Guards:** The inclusion of a reflection-based public surface snapshot test (INV-8.1) and a source-level string-scan guard against parallelization (INV-8.2) provides automated enforcement of hard runtime requirements that are often lost during refactors.
- **Phase Lock Preservation:** The plans explicitly cite and preserve critical semantics from prior phases, such as `CancellationToken.None` for termination (Phase 5) and MO2 backup-skip policies (Phase 7).

## Concerns
- **Retry Attempt Discrepancy (MEDIUM):** In Plan 08-05 (Wave 4, Task 2), the facade call to `runner.RunAsync` is planned with `maxRetryAttempts: 2`. However, the current `CleaningOrchestrator.cs` (line 53) defines `const int maxRetryAttempts = 3;`. This discrepancy violates **D-09** (zero intentional behavior changes) and should be corrected to `3` to match existing behavior.
- **Reflection Test Robustness (LOW):** The `ICleaningOrchestrator_PublicSurface_MatchesLockedSnapshot` test (Plan 08-01, Task 2) relies on a custom `FormatMemberSignature` helper. It must be carefully implemented to handle nullable reference types and generic `Task<T>` returns consistently across different build environments to avoid flakiness.
- **Wave 4 Scope (LOW):** Plan 08-05 modifies 9 files across 2 tasks. While logically cohesive (Runner + Finalizer), this is the "heaviest" wave. Ensuring the executor performs a clean build and test run between Task 1 (RED) and Task 2 (GREEN) is critical.

## Suggestions
- **Correct Retry Constant:** Update Plan 08-05 to preserve the `maxRetryAttempts = 3` constant from the current orchestrator.
- **Lock-Type Audit:** Ensure the `CleaningTerminationCoordinator` uses the existing `object _processLock = new();` pattern rather than the newer `System.Threading.Lock` to maintain consistency with the rest of the 1.0 codebase (as noted in Pattern E).
- **Finalizer Snapshot Timing:** Explicitly verify in Wave 4 that `terminationCoordinator.DetachProcess()` is awaited or completed *before* the `TerminationFinalizeContext` is snapshotted, ensuring `ProcessMayStillBeRunning` reflects the post-cleanup state. (The plan currently places the snapshot after `detachProcess`, which is correct).

## Risk Assessment: LOW
The overall risk is **LOW**. The plan's strict adherence to TDD, its comprehensive characterization wave, and its preservation of the `ProcessExecutionService` single-slot semaphore ensure that the core sequential cleaning guarantee remains intact. The decomposition is surgical, and the verification strategy is redundant across unit, integration, and source-level checks.

### Verification Gate Check
- **Sequential Invariant:** Guaranteed (Orchestrator loop + `ProcessExecutionService` semaphore).
- **Public Surface:** Guaranteed (Snapshot test in Wave 0).
- **Phase 5/6/7 Locks:** Guaranteed (Explicit verbatim code-lift tasks).
- **REF-01 Satisfaction:** High confidence (Seams are well-defined and isolated).

**Approved for execution.** Please ensure the `maxRetryAttempts` discrepancy is addressed during Wave 4.

---

## Claude Review

# Phase 8 Plan Review — Cross-AI Feedback

## 1. Summary

This is a well-structured, behavior-preserving refactor with clean wave ordering (least- to most-coupled state) and disciplined TDD per extraction. The plans correctly identify Phase 5 lock surfaces as the highest risk, encode the public-surface snapshot test as a structural guard, and lift code largely verbatim — minimizing semantic drift. However, several plan-level details have factual gaps (a wrong retry constant, a guessed validation enum, missing `xEditDir` plumbing), one cross-session Subject behavior is unspecified, and Wave 3→Wave 4 contains an avoidable two-step lambda migration. Overall the design is right; the execution-time correction surface is wider than ideal.

## 2. Strengths

- **Wave 0 separation is genuinely risk-reducing.** Locking 7 characterization tests + a `typeof(ICleaningOrchestrator).GetMembers()` snapshot before any production change is the right call; it directly exercises D-05.
- **Public-surface snapshot test (08-01 Task 2)** correctly asserts the 10-member D-03 contract using reflection, ordered comparison, and FluentAssertions `BeEquivalentTo`. This will catch member drift in Waves 1–5.
- **Phase 5 lock preservation is explicit.** Self-PID refusal (`Environment.ProcessId` check) and `CancellationToken.None` to `TerminateProcessAsync` are called out as literal-string acceptance criteria in 08-04 — exactly the right grain.
- **Source-level parallelization guard across all 6 cleaning files** (08-06 Task 2) is the correct way to enforce INV-8.2 long-term.
- **Delegate handoff (Research A3) for runner→termination** keeps the runner unaware of termination policy. This is a sound boundary; injecting `ICleaningTerminationCoordinator` directly would have created a back-edge.
- **Comment-preservation discipline** is repeated in every plan and tied to specific lines (e.g., `// Capture log offsets before each xEdit launch (per D-03...)` at 08-05). This aligns with the user's CLAUDE.md no-strip rule.
- **AbortSession ownership decision is correct** (Research Open Q1 / A2). Coordinator returns the outcome; facade finalizes the session. This matches the existing implementation's lifecycle ownership.

## 3. Concerns

### HIGH

- **[08-05] Wrong `maxRetryAttempts` constant.** The plan example uses `var maxRetryAttempts = 2;` and tells the executor to "verify the actual source — read existing CleaningOrchestrator.cs around 386." The actual constant is at `CleaningOrchestrator.cs:63`: `const int maxRetryAttempts = 3;`. If executed verbatim, this silently changes user-visible retry behavior — a direct D-09/D-12 violation. The plan must specify `3`.

- **[08-04] `ResetForNewSession` is under-specified vs. current finally block.** Lines 622–648 today reset 5 things: `_isStopRequested = false`, `stateService.SetTerminating(false)`, `_lastTerminationResult = null`, `_hangMonitorSubscription?.Dispose()`, `_hangDetected.OnNext(false)`, plus `_currentProcess = null` under lock. Plan 08-04 lists only stop flag + last result + hang subscription dispose for `ResetForNewSession`. **Two omissions matter:**
  - `stateService.SetTerminating(false)` — without this, a UI state ("terminating") leaks across sessions.
  - `_hangDetected.OnNext(false)` — without this, a stale `true` emitted in session N persists into session N+1 (the Subject is on a Singleton).
  Both are user-visible effects (D-09 says zero changes). The plan needs to enumerate the full reset list.

- **[08-04 → 08-05] Avoidable two-step lambda migration.** Plan 08-04 says: replace the inline `lock (_processLock) { _currentProcess = proc; } StartHangMonitoring(proc);` lambda with an inline `terminationCoordinator.AttachProcess(proc);` call **inside the orchestrator's foreach lambda** — and then Plan 08-05 moves the *same lambda* again into the runner via the `attachProcess` delegate. The same 4 lines get rewritten twice. Either:
  - Keep the inline lambda calling `_currentProcess`/`StartHangMonitoring` in Wave 3, and only swap to coordinator in Wave 4 (when runner takes the delegate); OR
  - Introduce the delegate parameter to runner in Wave 3 (which means runner must already exist, breaking wave ordering).
  Either way the current plan creates one unnecessary extraction pass. Recommend option 1.

- **[08-02] Guessed `PluginValidationFailureKind` enum.** Plan example references `PluginValidationFailureKind.NotFound`, `Unreadable`, `ZeroByte`, `InvalidExtension`, `MalformedEntry`. The actual orchestrator at line 196 uses `PluginWarningKind.None` (different enum name). The plan does say "verify by reading `IPluginValidationService.cs`," but mapping reasons is the *core* of D-15 fidelity — guessing the type at planning time means the executor will hit a compile failure on first try and may pick the wrong mapping under pressure. Either resolve the enum at planning time or explicitly require the executor to read `PluginWarningKind` first and re-derive the switch.

### MEDIUM

- **[08-02 / 08-05] Missing `XEditDirectory` plumbing.** `xEditDir` is consumed by both runner (offset capture) and finalizer (log read). After preflight extraction, the facade no longer derives it during preflight; it must read `stateService.CurrentState.XEditExecutablePath` separately. Cleaner solution: add `string XEditDirectory` to `CleaningPreflightPlan`. The plans don't include this field — risk is silent re-implementation of `Path.GetDirectoryName(config.XEditExecutablePath ?? string.Empty) ?? string.Empty` in the facade.

- **[08-03] AbortSession facade dispatch is summarized, not specified.** Lines 333–362 of today's orchestrator do: write partial metadata → `wasCancelled = true` → build `CleaningSessionResult` → `stateService.FinishCleaningWithResults` → `LogSessionSummary` → `return`. Plan 08-03's facade pseudocode shows `WritePartialMetadataAsync(...) ... build CleaningSessionResult, FinishCleaningWithResults, return`. The middle steps (especially `LogSessionSummary`) are easy to drop. With detailed work happening in TDD GREEN, this is a real omission risk. Recommend full pseudocode for the AbortSession branch.

- **[08-05] Detach timing flagged but unresolved.** Plan correctly notes the original code disposes the hang subscription and clears `_currentProcess` *outside* the do-while (lines 442–454, once per plugin), not after each attempt. The example pseudocode places `detachProcess()` inside the loop after each attempt. The plan tells the executor to "re-read" and decide. The semantic difference is: with detach-per-attempt, the second retry reattaches a NEW hang monitor on the new `Process`. With detach-once-per-plugin, the first attempt's hang monitor stays attached *across the retry's gap* — which is impossible (the `Process` object is new per retry attempt). Reading current behavior carefully: `cleaningService.CleanPluginAsync` returns when the process exits; the `_currentProcess` reference holds the EXITED process between retry attempts. So it's safe either way, but to preserve current behavior bit-for-bit, detach must be once-per-plugin (after the loop). The plan should call this out as a correctness requirement, not "verify against current code."

- **[08-06] Singleton dispose order for `_hangDetected` Subject.** Coordinator's `Dispose` will dispose the Subject. ViewModels subscribed via `HangDetected` keep the subscription. The DI container disposes registered Singletons at app shutdown, but order is unspecified. If a ViewModel is disposed *after* the coordinator, its `Dispose` might call into a disposed subscription. Today's code lives in the orchestrator and has the same property — so behavior is preserved — but worth a one-line acknowledgement in 08-04 rather than discovering it later.

- **[08-01] Test 6 capture mechanism is hand-wavy.** "use a state-changed observable subscription or a hook in the first plugin's `onProcessStarted`" — plan should specify the concrete capture: e.g., NSubstitute callback `_cleaningServiceMock.CleanPluginAsync(...).Returns(ci => { capturedLastTerminationResult = _orchestrator.LastTerminationResult; return new CleaningResult { ... }; })`. Without this, the executor will spend time inventing the technique.

### LOW

- **[08-02 Test 1]** `PrepareAsync_RealMode_AndDryRunMode_ProduceSamePluginRows_ForIdenticalInputs` calls the same single-mode method twice and asserts equality — this proves idempotence, not dual-mode equivalence. The actual dual-mode equivalence test belongs at the orchestrator layer (already in Wave 0). Slight test-name/intent mismatch; consider renaming to `PrepareAsync_IsIdempotent_ForIdenticalState` and dropping the dual-mode framing here.

- **[08-04]** Coordinator `Dispose` ordering: the plan removes `_hangDetected.Dispose()` from the facade. But both facade and coordinator are `IDisposable` Singletons. If a future developer adds a non-Singleton scope, dispose order assumptions break. Worth a comment.

- **[08-03]** `BeginSessionAsync(CleaningPreflightPlan plan, ...)` couples backup coordinator to preflight model. Acceptable per D-04, but if `CleaningPreflightPlan` ever needs backup-specific fields you create bidirectional coupling. Alternative: have the facade unpack the plan and pass primitives. Low priority.

- **[08-05]** `RunnerFinalizerModels.cs` and `BackupSessionModels.cs` and `CleaningPreflightModels.cs` are three separate model files for three small record/enum sets. Could be consolidated into `Cleaning/CollaboratorModels.cs` for browseability. Stylistic.

## 4. Suggestions

1. **Fix the retry constant in 08-05** to `3` (matches `CleaningOrchestrator.cs:63`).
2. **Expand `ResetForNewSession` in 08-04** to enumerate all 5 actions: stop flag reset, `stateService.SetTerminating(false)`, last-result reset, hang subscription dispose, AND `_hangDetected.OnNext(false)`. Add a test asserting Subject emits `false` on reset.
3. **Add `string XEditDirectory` to `CleaningPreflightPlan`** in 08-02. Both runner and finalizer consume it; one source of truth eliminates re-derivation in the facade.
4. **Resolve `PluginWarningKind` at planning time, not execution time.** Update 08-02's mapper example to use the actual enum (`PluginWarningKind.NotFound`, etc.) once verified, or add a Task 0 sub-step "read `AutoQAC/Services/Plugin/IPluginValidationService.cs` and `PluginWarningKind` enum and update the plan in-place" before Task 1 RED.
5. **Collapse the two-step lambda migration.** In 08-04 keep the existing inline `lock + StartHangMonitoring` lambda untouched (it now calls the coordinator's internal state, which is fine because the orchestrator already injects the coordinator and the existing lambda becomes coordinator-state mutation). Do the lambda→delegate swap once, in 08-05 Wave 4. This removes one extraction pass over the same code.
6. **Detail the AbortSession branch in 08-03** with full pseudocode including `wasCancelled = true`, `LogSessionSummary(sessionResult)`, and the `return` placement — match today's lines 333–362 exactly.
7. **Lock the detach-once-per-plugin contract in 08-05** as an explicit must-have truth, not "verify against current code." Consider adding a unit test that asserts `detachCount == 1` after a 2-attempt retry session.
8. **Tighten Test 6 in 08-01** with a concrete `Returns(ci => { captured = _sut.LastTerminationResult; ...})` capture pattern.
9. **Add a Wave 5 acceptance check** that the facade primary-constructor parameter list dropped `IPluginValidationService`, `IGameDetectionService`, `IXEditLogFileService`, `IXEditOutputParser`, `IBackupService`, `IProcessExecutionService`, `IHangDetectionService` (all of which are now collaborator deps). Confirms the facade truly thinned.
10. **Add a small section to 08-06 about DI dispose order.** Document that `_hangDetected` Subject is now owned by `CleaningTerminationCoordinator.Dispose`, so any future subscriber must treat the observable as having app-lifetime semantics.

## 5. Risk Assessment

**MEDIUM**

Justification:

- **Architecture is sound.** The 5-collaborator decomposition cleanly maps to D-06/D-07/D-08 + D-13–D-16, and the wave ordering correctly stages risk (preflight → backup → termination → runner/finalizer → integration).
- **Public-surface and parallelization invariants are protected by guard tests** that will catch silent drift in the implementation phase.
- **Phase 5 lock fidelity is the dominant risk** and is concentrated in Wave 3. The plan calls out self-PID refusal and `CancellationToken.None` as literal acceptance criteria — strong. But `ResetForNewSession` semantic gap (no `OnNext(false)`, no `SetTerminating(false)`) is a real hole that could surface as a UI regression.
- **Plan-level detail correctness is variable.** The `maxRetryAttempts = 2` example, guessed validation enum, missing `xEditDir` field, and abbreviated AbortSession dispatch each represent execution-time correction loops. Each is recoverable, but together they will likely cause Wave 3 or Wave 4 to need plan amendments mid-execution.
- **Wave 4 (08-05) modifies 9 files in 2 tasks** — large but defensible because it's two paired extractions (runner + finalizer) with parallel structures. Acceptable.
- **D-11 stop-and-ask discipline** is mentioned in every `must_haves.truths` block but not operationalized; an executor under autonomous mode might silently fix the `OnNext(false)` gap instead of asking. Consider adding a Wave 0 test that fails specifically when `ResetForNewSession` doesn't emit `false` so the gap surfaces as a test failure (forcing escalation) rather than a missed behavior.

The phase will likely complete successfully but should expect 1–2 mid-flight plan corrections (notably the retry constant, `ResetForNewSession` enumeration, and validation enum). Recommend addressing the four HIGH concerns before kicking off Wave 1.

---

## Codex Review

**Summary**
The wave chain is mostly sound: Wave 0 characterization, then least-coupled preflight, backup, high-risk termination, runner/finalizer, and final guard work is the right risk order for D-05 and REF-01. The collaborator boundaries broadly match D-06 through D-08 and D-13 through D-16. The main risks are not the decomposition concept, but plan over-specification against possibly inaccurate current APIs, several tests that may be brittle or hard to make deterministic, and a few places where the proposed "RED" steps will fail production builds because interfaces reference implementation models before those models exist.

**Strengths**
- The dependency chain is well ordered. Preflight before backup before termination before runner/finalizer reduces shared-state churn and keeps the Phase 5 lock surface isolated in 08-04.
- Public surface stability is explicitly protected in 08-01 via `ICleaningOrchestrator_PublicSurface_MatchesLockedSnapshot`, which is a good fit for D-03 and D-19.
- The collaborator scopes are mostly correct:
  - `ICleaningPreflight` handles D-13 through D-16.
  - `IBackupSessionCoordinator` owns Phase 7 backup operation CTS/progress state.
  - `ICleaningTerminationCoordinator` owns process/hang/termination state.
  - Runner/finalizer split keeps launch/retry separate from log/result interpretation.
- The plans repeatedly preserve the important locked semantics: sequential cleaning, no command-builder bypass, no log reads after unsafe termination, MO2 backup skip, and `CancellationToken.None` for termination cleanup.
- D-11 is called out in every plan, which gives executors a clear stop-and-ask rule if characterization exposes a real behavior bug.

**Concerns**
- **HIGH: 08-02/03/04/05 "RED" gates can break production build.** Each plan creates public interfaces referencing new model types, but delays the implementation class only. That is fine. However the acceptance criteria also require `dotnet build AutoQAC/AutoQAC.csproj` to pass before all referenced types and required imports are proven correct. This is fragile if model files miss dependencies or interface signatures mismatch actual current models.
- **HIGH: 08-05 detach semantics are internally contradictory.** The plan first shows `detachProcess()` inside the retry loop, then warns it must be once after the loop. This is the kind of ambiguity that can change hang-monitor/process tracking behavior. The final instruction should be unambiguous: detach exactly where the current orchestrator detaches relative to all retry attempts.
- **MEDIUM: 08-01 characterization tests are ambitious and may be nondeterministic.** `LastTerminationResult_IsResetToNull_AtSessionStartAndEnd` and `MarkLeftRunningByUser_AfterStopCleaning...` depend on timing around active process attachment and stop state. These need clear synchronization hooks or they risk becoming flaky.
- **MEDIUM: Preflight may be too broad if it calls `FlushPendingSavesAsync` and `ValidateEnvironmentAsync`.** That matches D-13, but it means a "pure async function" still performs persistence flushing and environment validation. The plans should avoid calling it "pure"; the real invariant is "no state mutation/process/backups/CTS."
- **MEDIUM: 08-03 backup coordinator may be slightly too broad.** It owns begin/run/finalize/partial metadata/cancel. That is acceptable for D-06, but make sure it does not start owning `CleaningSessionResult` construction. The plan says facade owns abort-session finalization, which is the right guardrail.
- **MEDIUM: Source-level parallelization guard is useful but incomplete.** 08-06 scans for `Parallel`/`Task.WhenAll`, but sequential behavior mainly depends on the facade `foreach` awaiting each runner call and `ProcessExecutionService` remaining in the launch path. A source guard should supplement, not replace, behavior tests.
- **LOW: Public-surface snapshot formatting may be brittle.** Reflection formatting for `Task<List<DryRunResult>>`, nullable annotations, overloads, and generic types can produce false failures unless the helper is carefully written.
- **LOW: File path references are inconsistent.** Some sections refer to `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`; the validation scaffold mentions `AutoQAC.Tests/Services/Cleaning/CleaningOrchestratorTests.cs`. Keep the existing orchestrator test file in its current location unless intentionally moving it.
- **LOW: 08-06 target line count is arbitrary.** `< 400` lines is a useful smell check, but not a correctness criterion. Avoid forcing unnatural compression to satisfy it.

**Suggestions**
- In 08-05, replace the contradictory runner pseudo-code with a single explicit rule: `attachProcess` is called by `CleanPluginAsync` per attempt; `detachProcess` is called once in a `finally` after the retry loop, matching current behavior.
- For 08-01 timing-sensitive tests, require `TaskCompletionSource` synchronization around `onProcessStarted`, stop completion, and session exit. Avoid sleeps.
- Strengthen the sequential invariant with a behavior test that records ordered calls: backup for plugin A, runner A, finalizer A, then backup B, runner B, finalizer B. Keep the source scan as a regression guard.
- In 08-02, rename "RealMode_AndDryRunMode" if `ICleaningPreflight` has no mode parameter. Better: `PrepareAsync_ReturnsStablePluginRows_ForRepeatedCalls` plus an orchestrator-level test proving dry-run and start-cleaning consume the same clean set.
- In 08-03, explicitly require tests for `CancelActiveOperationAsync` no-op when no active backup CTS and cancellation of active retention, not just backup success.
- In 08-04, add an acceptance criterion that `CleaningTerminationCoordinator.Dispose()` is registered/disposed through DI or intentionally owned by the app provider. Since it owns a `Subject`, lifetime should be explicit.
- In 08-06, do not require final facade constructor parameter order unless there is a real convention. Require presence and wiring instead.

**Risk Assessment**
Overall risk: **MEDIUM**.

The architecture and wave order are good, and the plans are unusually explicit about prior phase locks. The risk comes from high-churn extraction across cancellation, live process state, retry behavior, and backup operation CTS ownership. 08-04 and 08-05 are the highest-risk waves. With the runner detach ambiguity fixed, timing-sensitive tests made deterministic, and behavior-level sequencing assertions added, this becomes a solid behavior-preserving refactor plan.

---

## Consensus Summary

### Agreed Strengths (cited by 2+ reviewers)

- **Wave 0 characterization-first ordering** genuinely reduces risk by locking outcomes before any production change (Gemini, Claude, Codex).
- **Public-surface snapshot test (08-01 Task 2)** is the right structural guard for D-03 / D-19 (Gemini, Claude, Codex).
- **Source-level parallelization guard (08-06 Task 2)** correctly enforces the sequential xEdit invariant at the source layer (Gemini, Claude, Codex).
- **Phase 5 lock preservation is explicit** — `CancellationToken.None` for termination cleanup and self-PID refusal are literal acceptance criteria, not narrative claims (Gemini, Claude, Codex).
- **Delegate handoff (`attachProcess`/`detachProcess`)** for runner→termination boundary avoids back-edge coupling (Gemini, Claude).
- **D-11 stop-and-ask** appears in every plan's `must_haves.truths` (Claude, Codex).

### Agreed Concerns (cited by 2+ reviewers)

| Severity | Concern | Reviewers | Plans Affected |
|----------|---------|-----------|----------------|
| **HIGH** | `maxRetryAttempts` constant is wrong in plan example (uses `2`, actual is `3` at `CleaningOrchestrator.cs:63`) — silent D-09/D-12 violation if executed verbatim | Gemini (MEDIUM), Claude (HIGH) | 08-05 |
| **HIGH** | 08-05 detach timing is internally contradictory (pseudocode shows per-attempt detach; correct behavior is once-per-plugin after the retry loop) — plan tells executor to "verify against current code" instead of locking the contract | Codex (HIGH), Claude (MEDIUM) | 08-05 |
| **MEDIUM** | Public-surface reflection helper `FormatMemberSignature` may be brittle for nullable annotations, `Task<List<T>>`, and overloads | Gemini (LOW), Codex (LOW) | 08-01 |
| **MEDIUM** | Plan 08-05 modifies 9 files across 2 tasks — heaviest wave; needs clean build between RED and GREEN | Gemini (LOW), Codex (implied) | 08-05 |

### Solo Concerns (HIGH severity, single reviewer but worth surfacing)

- **[Claude HIGH]** `ResetForNewSession` in 08-04 is missing `stateService.SetTerminating(false)` and `_hangDetected.OnNext(false)` from current `finally` block (lines 622–648). Both are user-visible: stale "terminating" UI state leaks across sessions; stale hang flag persists. **Concrete fix:** enumerate the full 5-item reset list in 08-04 and add a test asserting `OnNext(false)` is emitted on reset.
- **[Claude HIGH]** Two-step lambda migration: 08-04 rewrites the `lock + StartHangMonitoring` lambda to call `terminationCoordinator.AttachProcess`, then 08-05 rewrites the same lambda again to use a `attachProcess` delegate. Recommend keeping the lambda inline through Wave 3 and doing the delegate swap once in Wave 4.
- **[Claude HIGH]** 08-02 references a guessed enum `PluginValidationFailureKind` that doesn't exist; actual enum is `PluginWarningKind` per `CleaningOrchestrator.cs:196`. Resolve at planning time, not at RED-test compile time.
- **[Codex HIGH]** RED gates on plans 08-02/03/04/05 require `dotnet build AutoQAC/AutoQAC.csproj` to pass with public interfaces referencing new model types — fragile if model files miss dependencies or signatures mismatch.

### Divergent Views

- **Risk level:** Gemini says **LOW** ("Approved for execution"). Claude and Codex both say **MEDIUM**. The split reflects how much weight to put on plan-level detail accuracy vs. architectural soundness — Gemini scores the architecture; Claude/Codex score the executable plan text.
- **Detach timing semantic:** Codex says "the final instruction should be unambiguous"; Claude says "preserve current behavior bit-for-bit (detach once per plugin)" with a unit test. They agree on the symptom, disagree on the framing — Claude pins the contract; Codex pins the language.
- **Preflight scope:** Codex calls out that `PrepareAsync` calling `FlushPendingSavesAsync` + `ValidateEnvironmentAsync` is *not* a "pure async function" — recommends rewording the invariant to "no state mutation/process/backups/CTS." Other reviewers did not raise this.
- **Test seam strength:** Codex recommends adding a behavior test that records ordered calls (`backup_A → runner_A → finalizer_A → backup_B`) to supplement the source-level parallelization guard. Gemini and Claude rely on the source scan alone.

### Recommended Next Step

The architectural design has independent endorsement from all three reviewers. The HIGH concerns are concentrated in plan 08-05 (retry constant, detach timing) and plan 08-04 (`ResetForNewSession` semantic gap, lambda migration). Re-run planning with the review feedback to address these targeted issues:

```
/gsd-plan-phase 8 --reviews
```

The replan will preserve the wave structure and TDD framing — only the in-plan details flagged HIGH/MEDIUM need to change.
