---
phase: 8
reviewers: [gemini, claude, codex]
reviewed_at: 2026-04-29T20:06:39.2498992-07:00
plans_reviewed:
  - 08-01-PLAN.md
  - 08-02-PLAN.md
  - 08-03-PLAN.md
  - 08-04-PLAN.md
  - 08-05-PLAN.md
  - 08-06-PLAN.md
  - 08-07-PLAN.md
  - 08-08-PLAN.md
---

# Cross-AI Plan Review - Phase 8

## Gemini Review

# Phase 8 implementation plans review

The implementation plans for **Phase 8: Cleaning Orchestrator Decomposition** (including the Wave 0-5 refactor and the subsequent Gap Closure plans) are exceptionally high-quality and demonstrate a disciplined, risk-aware approach to refactoring a core state machine. The strategy of using characterization tests (08-01) to lock current outcomes before extraction, followed by incremental decomposition into stateless or focused stateful collaborators, directly addresses the `REF-01` requirement while minimizing regression risk.

### Strengths

*   **Behavioral Lock-in (08-01):** The use of a "Wave 0" to pin 41+ existing behaviors and 7 new characterization gaps (including a public-surface reflection snapshot) ensures that refactoring success is empirically verifiable rather than assumed.
*   **Drift Prevention (08-02):** Consolidating preflight and selection logic into a shared `ICleaningPreflight` used by both real cleaning and dry-runs (D-13) effectively eliminates a known source of logic drift.
*   **Surgical State Management (08-04):** The extraction of the termination coordinator (Wave 3) is handled with extreme care. The plan explicitly preserves the "5-action reset" (R-03) and the self-PID refusal safety check (INV-5.4), which are critical for system stability.
*   **Dependency Hygiene (08-05):** The use of delegate handoff (`attachProcess`/`detachProcess`) instead of a direct back-edge dependency from the Runner to the Termination Coordinator keeps the dependency graph acyclic and improves testability.
*   **Rigorous Gap Closure (08-07, 08-08):** Addressing the `CR-01` (startup cancellation) and `CR-02`/`WR-01` (result consistency) findings as dedicated TDD plans ensures that the final "slim" orchestrator is not only cleaner but actually more robust than the monolithic version.
*   **Structural Guards (08-06):** The inclusion of a source-level guard test to prevent the accidental introduction of parallel constructs (`Parallel.ForEach`, `Task.WhenAll`) is an excellent defensive engineering practice for this project's sequential requirement.

### Concerns

*   **Metadata Dependency Mismatch (08-07, 08-08):**
    *   **Severity: LOW**
    *   Plans 08-07 and 08-08 list `depends_on: []` in their metadata. However, the tasks and tests within these plans explicitly reference the post-refactor facade constructor (the 8-parameter version) and the new collaborator types (e.g., `ICleaningPreflight`, `TerminationFinalizeContext`). These plans effectively depend on the completion of the Wave 5 integration (08-06). Executing them against the legacy monolithic code would fail.
*   **Characterization Test Timing (08-07):**
    *   **Severity: LOW**
    *   Task 1 in 08-07 uses `WaitForSignalAsync` with a hardcoded 2-second timeout to synchronize between the orchestrator task and the test driver. While consistent with existing patterns, this could be brittle in high-contention CI environments or slow debuggers.

### Suggestions

*   **Update Plan Metadata:** For traceability, update the `depends_on` field in `08-07-PLAN.md` and `08-08-PLAN.md` to include `["08-06"]`.
*   **AlreadyClean Promotion Clarification (08-08):** In `PluginResultFinalizer.cs`, when tightening the `AlreadyClean` gate, ensure the `Message` returned for the reclassified row remains "Ready for cleaning" (or the legacy equivalent) to ensure the UI transition from "Cleaned" to "Already Clean" is seamless. The plan preserves `result.Message` in the ternary, which should be audited during implementation to ensure it matches legacy user expectations.

### Risk Assessment

**Verdict: LOW**

The overall risk for this phase is low, despite the complexity of the refactor. The strategy of **Characterize -> Extract -> Validate -> Close Gaps** is the gold standard for behavior-preserving changes. By the time the final gap closure plans (08-07 and 08-08) are executed, the codebase will have reached a higher state of both structural quality and behavioral correctness. The plan set successfully achieves the `REF-01` phase goal.

---

## Claude Review

# Phase 8 Plan Review

## Summary

Phase 8 decomposes a 1151-line `CleaningOrchestrator` into a thin facade plus five focused collaborators (preflight, backup-session, termination, runner, finalizer). Plans 01-06 have already been executed and merged; plans 07-08 are post-verification gap-closure plans for blockers found by `08-VERIFICATION.md`. Overall the plan set is unusually rigorous - research-backed, TDD-gated, with line-level extraction maps and explicit Phase 5/6/7 invariant tracing - but the original 6-plan track shipped two behavior regressions (CR-01 stop-during-preflight, CR-02/WR-01 finalizer status drift) that the gap-closure plans now fix. The fact that those bugs slipped through despite careful invariant tables points to a structural gap in characterization coverage, not to weak planning.

## Strengths

- **Wave 0 characterization gate (08-01) is correctly placed.** Locking outcomes before any production change is the right move for a behavior-preserving refactor; the public-surface snapshot test (with deterministic generic/nullable formatting per R-06) is a high-value low-cost guard.
- **Line-level extraction map** in research lets each plan name exact source ranges (e.g. "lines 386-440") rather than vague "the retry loop." This made the diffs auditable.
- **Explicit verbatim-preservation contracts** for high-risk code: R-09 enumerates the 5-step AbortSession sequence, R-03 enumerates the 5-action `ResetForNewSession`, R-01 pins `maxRetryAttempts = 3`, R-08 pins `XEditDirectory` single-source-of-truth. These literal-string acceptance criteria caught known regressions in earlier drafts.
- **Lambda-hoist-once rule (R-04)** prevents Wave 3 and Wave 4 from extracting the same `onProcessStarted` lambda twice - a real risk in staged refactors.
- **Threading ownership map** (facade keeps `_cleaningCts`; coordinator owns `_processLock`/`_isStopRequested`/hang Subject; backup coordinator owns `_backupOperationCts`) avoids nested cross-collaborator lock acquisition.
- **TDD gates are real**: each plan has a RED task that asserts the test build fails referencing the missing class (R-07), not just that "tests fail."
- **Gap-closure plans (07, 08) are tightly scoped** to the verifier's findings, with grep-based acceptance criteria that prove the fix shape, not just that tests pass.

## Concerns

### HIGH

- **Characterization gap that allowed CR-01 to ship.** Plan 08-01's six characterization tests cover the D-18 outcome list but contain no test for "Stop during startup window" - the exact failure CR-01 represents. The Wave 0 gate is meant to lock current behavior; current behavior here was a bug (stop ignored during preflight) that wasn't characterized either way, so the refactor preserved the bug without catching it. Future characterization waves should explicitly cover *startup-window cancellation* and *concurrent stop during each long-running step*.
- **Characterization gap that allowed CR-02/WR-01 to ship.** Plan 08-05's three finalizer tests covered the success path of AlreadyClean promotion and the termination-skip guard, but never exercised "failed runner + completion-line log" or "successful runner + exception log" - the two paths where Status/Success could disagree. The R-04/R-08-style literal-contract approach worked well for migrations; a similar approach (cross-product of `result.Success in {true,false}` x `logResult.ExceptionContent in {null, present}`) would have caught both bugs.
- **08-07 RED test (`StopCleaningAsync_DuringPreflight_...`) constructs a fresh orchestrator inline** with hand-wired collaborators, bypassing the test class's `_orchestrator` field. This is necessary for the substituted `ICleaningPreflight` but means the test won't catch regressions in the standard wiring path. Acceptable, but flag-worthy: if the production DI registration drifts, this test won't notice.

### MEDIUM

- **08-04 ResetForNewSession test (Test 8) uses `CallbackObserver<bool>`** which is `AutoQAC`-internal. The plan doesn't confirm it's accessible from the test project; if it's `internal`, the test needs `InternalsVisibleTo` (which 07-14 already established for `BackupPathContainment`, but not necessarily for `CallbackObserver`). Worth a one-line check during execution.
- **08-02 `XEditDirectory` is added to `CleaningPreflightPlan` in plan 08-02**, but plan 08-05 also lists `CleaningPreflightModels.cs` in its `files_modified` and re-describes adding the same field. If 08-02 already added it (per 08-02 SUMMARY's mention of `XEditDirectory`), 08-05's "extension" step is dead. Minor doc drift, not a bug, but creates confusion about which plan owns the field.
- **08-03 AbortSession dispatch in the facade** uses `var abortedSessionResult = new CleaningSessionResult { ..., AbortReason = outcome.FailureReasonText }`, but 08-03's SUMMARY notes `CleaningSessionResult` has no `AbortReason` property - it was an illustrative-only field that the executor correctly omitted. The plan body doesn't flag this as illustrative, so future readers comparing plan-vs-implementation will see a "deviation." Worth annotating in retrospect.
- **08-06 source-level guard** prohibits `Task.WhenAll(`, `Task.WhenAny(`, `Parallel.ForEach`, `Parallel.ForEachAsync`, but explicitly allows `Task.Run` with a comment that "Task.Run is allowed for fire-and-forget infrastructure work." That carveout is reasonable but unbounded - a future regression could legitimately use `Task.Run(() => CleanPlugins())` and pass the guard. Either tighten the guard to flag `Task.Run` inside `*Cleaning*.cs` files, or add a per-file comment annotation requirement.
- **Plan 08-04 lambda-stays-inline rule (R-04)** is correct, but the "lambda body just calls `terminationCoordinator.AttachProcess(proc)`" leaves `detachProcess` semantics ambiguous in Wave 3. The plan says detach also moves to coordinator-call inline, but doesn't specify whether the inline detach happens once-per-plugin (matching current) or once-per-attempt. The 08-04 SUMMARY confirms the executor got it right, but the plan text could have been tighter.

### LOW

- **08-01 Test 6 (LastTerminationResult reset)** uses an NSubstitute callback to sample `_orchestrator.LastTerminationResult` mid-call. This depends on `Returns(ci => ...)` evaluation order, which is reliable in NSubstitute but worth a one-line comment noting the ordering assumption.
- **08-02 `CleaningPreflight` does not call `IStateService.UpdateState`** but does call `IConfigurationService.FlushPendingSavesAsync`, which can write to disk. The plan documents this distinction (preflight is "not strictly pure"), but the boundary "no cleaning state mutation" vs "environment plumbing allowed" is subtle and could trip future reviewers. Consider naming the boundary explicitly (e.g. "no AppState mutation").
- **08-05 `PluginRunnerOutput.ReachedMaxRetryAttempts`** is computed as `result.TimedOut && attemptNumber >= maxRetryAttempts`, but in current code the timeout-message-shaping at orchestrator line 504-506 used `result.TimedOut && attemptNumber >= maxRetryAttempts` for the message text only. The new field surfaces the same predicate - fine - but it's now part of a record contract, so changing the timeout-retry FSM in the runner would require coordinated updates to the finalizer's message shaping.
- **08-06 `LocateCleaningSourceFolder()`** walks up directories looking for `AutoQACSharp.slnx`. This works in the standard test runner but breaks if the test is run from a deployed location (e.g. CI artifact-only run). Probably fine given the project's local-Windows test posture, but worth flagging.
- **No plan exercises MO2 mode + backup-disabled simultaneously.** The preflight plan exposes both `IsMo2ModeActive` and `BackupEnabled` independently, but tests cover MO2-active (backup auto-skip) and non-MO2 separately. A combined test would lock the invariant that `BackupSkippedByPolicy = isMo2Mode || !userConfig.Backup.Enabled`.

## Suggestions

1. **For 08-07 specifically:** Add a second regression test for `CleanOrphanedProcessesAsync` (not just preflight). The plan threads `cts.Token` into both, but only tests the preflight cancellation path. A symmetric test against orphan cleanup would lock both halves of the fix.
2. **For 08-08 specifically:** Add a third test for the `Skipped` short-circuit path - confirm that `result.Status == CleaningStatus.Skipped` continues to return `Success = false` (or whatever current behavior is) under the new `finalSuccess` derivation. The new gate `finalStatus is Cleaned or AlreadyClean` makes Skipped -> false, which is likely correct but isn't characterized.
3. **Add a "concurrent stop" characterization test family** to the Wave 0 pattern for future similar refactors. The matrix should be: stop during {orphan-cleanup, preflight, backup-begin, per-plugin-backup, per-plugin-runner, per-plugin-finalizer, retention}. CR-01 is the first row; the others are still uncharacterized post-08-07.
4. **Annotate 08-03 AbortSession plan retroactively** with a note that `AbortReason` was an illustrative-only model field; the actual implementation reuses existing `CleaningSessionResult` fields. This will save future readers from chasing a phantom property.
5. **Tighten 08-06's parallelization guard** to flag `Task.Run(` inside any `*.cs` file under `AutoQAC/Services/Cleaning/` unless preceded by an opt-out comment marker. The current carveout is too permissive given the importance of INV-8.2.
6. **For the gap-closure pattern in general:** When verification finds blockers post-execution, consider also adding a *characterization gap retrospective* to each gap-closure plan: "We missed CR-01 because Wave 0 had no startup-window cancellation test; future similar refactors should include this matrix." This turns each gap closure into a learning artifact for the next phase.

## Risk Assessment

**Overall risk: LOW for plans 07-08; MEDIUM for the original 1-6 track in retrospect.**

- **Plans 07 and 08 are LOW risk.** They are surgical, scoped to single methods, TDD-gated with a real RED test, and have strong literal-string acceptance criteria. The fixes are exactly what `08-REVIEW.md` recommended. The only residual risk is incomplete characterization of adjacent paths (orphan-cleanup-window stop, Skipped-path Success derivation), which suggestions 1 and 2 address.
- **Plans 01-06 carried MEDIUM risk in retrospect.** Each individual plan was high-quality, but the characterization gate (08-01) was not exhaustive enough to prevent two behavior regressions. The decomposition itself is sound - REF-01 is genuinely satisfied - but "behavior preservation" was over-claimed by 7/9 verification truths, not 9/9. Once 07 and 08 land, the milestone reaches the originally promised state.
- **No security, performance, or scope-creep concerns.** Phase 8 is internal refactor; no new attack surface, no parallelization, no public API drift, no Mutagen edits. The threat models in each plan are appropriately minimal.
- **D-11 (stop-and-ask on non-REF-01 bugs)** was correctly invoked in 08-02 and 08-03 (auto-fix logs in summaries cite Rule 1 deviations with explanations). This is working as designed.

The plan set is one of the cleaner GSD refactor decompositions I've seen - the failure mode wasn't planning quality, it was characterization breadth. Apply suggestion 3 in future refactor phases to close that gap.

---

## Codex Review

**Overall Assessment**

The phase design is directionally strong: the collaborator boundaries map well to `REF-01`, and the sequence mostly follows characterize-then-extract. The main issues are execution risk, not architecture. Several plans are over-prescriptive in ways that can break on current APIs, some RED-step verification commands contradict their own acceptance criteria, and the gap-closure plans need explicit dependencies on the extracted collaborator state. With 08-07 and 08-08 included before declaring Phase 8 complete, the plan can satisfy the phase goal.

## 08-01 - Wave 0 Characterization

**Summary:** Good foundational test plan, but the public-surface reflection test as written is likely brittle and may include property accessor methods unless filtered carefully.

**Strengths**
- Correctly prioritizes characterization before extraction.
- Covers the right behavior gaps: left-running, retention, dry-run equivalence, backup continue, termination reset.
- Public API snapshot is useful for `D-03`.

**Concerns**
- **HIGH:** `GetMembers()` plus the shown formatter can include special-name property getters unless explicitly filtered, causing false failures.
- **MEDIUM:** Several characterization tests are complex async/process-flow tests and need robust cleanup to avoid hangs.
- **MEDIUM:** "Do not modify existing tests" may be too rigid if helper extraction is needed to keep the file maintainable.

**Suggestions**
- Build the snapshot from `GetMethods().Where(!IsSpecialName)` plus `GetProperties()` separately.
- Add `finally` cleanup around any background `StartCleaningAsync` task.
- Keep the API snapshot deterministic, but avoid over-testing nullable annotation rendering.

**Risk Assessment:** **MEDIUM**. Valuable tests, but brittle reflection and async orchestration can create noisy failures.

## 08-02 - Preflight Extraction

**Summary:** The preflight seam is well chosen and directly supports dry-run/real-run consistency, but the plan has wording and RED-gate inconsistencies.

**Strengths**
- Good separation of preflight selection from cleaning state mutation.
- Carries MO2 policy facts and `XEditDirectory`, reducing downstream re-derivation.
- Corrects the validation enum to `PluginWarningKind`.

**Concerns**
- **HIGH:** RED verification commands in this and later plans invert expectations: production build should pass while test build fails, but the command fails when production succeeds.
- **MEDIUM:** `must_haves` mentions `ReadyForCleaning`, but the enum intentionally omits it.
- **MEDIUM:** `LaunchModeLabel` wording may drift from existing user-facing text if it is ever surfaced.
- **LOW:** "Does not mutate state" needs to remain narrowly defined, since preflight still flushes config and validates environment.

**Suggestions**
- Change RED checks to build/test the test project and expect missing implementation only there.
- Remove `ReadyForCleaning` from must-haves or make it explicitly derived from `Decision == Clean`.
- Treat launch labels as internal unless existing UI text requires exact preservation.

**Risk Assessment:** **MEDIUM**. The seam is right; the main risk is execution friction and wording drift.

## 08-03 - Backup Session Coordinator

**Summary:** The backup coordinator boundary is sensible, but the plan includes some stale model assumptions and a fragile abort-session recipe.

**Strengths**
- Good outcome enum design for `Succeeded`, `Canceled`, `UserSkipped`, `AbortSession`, and `ContinueWithoutBackup`.
- Keeps session-result publication in the facade, which is the right ownership.
- Preserves Phase 7 progress/cancellation boundaries.

**Concerns**
- **HIGH:** The abort-session snippet references fields such as `BackupEntries`/`AbortReason` that may not exist in the current `CleaningSessionResult` model.
- **HIGH:** Same RED-gate command contradiction as 08-02.
- **MEDIUM:** `BeginSessionAsync` and backup service method signatures need exact repo verification, not plan-level guesses.
- **MEDIUM:** Literal-string acceptance checks for abort flow can be brittle after harmless refactors.

**Suggestions**
- Rewrite abort acceptance around behavior: metadata attempted, canceled session published, summary logged, no xEdit launch.
- Validate the actual `CleaningSessionResult` shape before embedding sample object initializers.
- Add one test for unexpected backup-service exceptions so "expected failures return outcomes" remains bounded.

**Risk Assessment:** **MEDIUM-HIGH**. Good architecture, but stale model assumptions can cause compile failures or behavior drift.

## 08-04 - Termination Coordinator

**Summary:** This is the riskiest extraction and the plan treats it with appropriate care, especially around self-PID refusal, `CancellationToken.None`, and reset behavior.

**Strengths**
- Correctly keeps session CTS in the facade.
- Pins self-PID refusal and force-kill/graceful-stop semantics.
- Explicitly resets terminating state and hang state.

**Concerns**
- **HIGH:** The plan claims Phase 5 behavior is fully preserved, but the later CR-01 gap shows stop-during-preflight was not covered.
- **MEDIUM:** Clearing `LastTerminationResult` in finally can race with consumers that read the property after stop; tests should favor returned `StopCleaningResult`.
- **MEDIUM:** Real-process tests need strict cleanup to avoid orphaned helper processes.
- **LOW:** `HangDetected => AsObservable()` is acceptable, but lifetime expectations should be documented once, not repeated everywhere.

**Suggestions**
- Add a preflight/startup stop test before marking termination semantics preserved.
- Make tests assert return values first and property state only where the property is explicitly part of the contract.
- Keep coordinator tests mostly mock-based; use real processes only for self-PID/termination invariants.

**Risk Assessment:** **HIGH**. This touches user stop/kill safety and app-lifetime observable state.

## 08-05 - Runner + Finalizer

**Summary:** The runner/finalizer split is the right shape, but the plan misses two result-consistency edge cases later covered by 08-08.

**Strengths**
- Keeps command construction in `CleaningService`, preserving Phase 6.
- Correctly protects offset capture before each xEdit launch.
- Delegate-based process attach/detach avoids coupling runner to termination policy.

**Concerns**
- **HIGH:** Finalizer tests do not initially cover failed runner plus zero-stat completion, or exception log causing `Success=false`.
- **MEDIUM:** The pseudo-code should initialize `CleaningResult` before the retry loop or explicitly rethrow on launch exceptions.
- **MEDIUM:** `XEditDirectory` is introduced in both 08-02 and 08-05, creating plan ownership confusion.
- **LOW:** Calling the finalizer "pure" is inaccurate because it reads logs.

**Suggestions**
- Move the two 08-08 tests into 08-05 if possible, or make 08-08 a hard dependency before verification.
- State explicitly whether runner exceptions propagate or become failed `PluginRunnerOutput`.
- Let 08-02 own `XEditDirectory`; 08-05 should only consume it.

**Risk Assessment:** **HIGH** until 08-08 lands; **MEDIUM** afterward.

## 08-06 - Final Integration

**Summary:** Good regression sweep, but it declared success too early given the later CR/WR gaps.

**Strengths**
- Confirms all collaborators are wired through DI.
- Keeps public surface and sequential cleaning guarded.
- Adds cross-file parallelization checks.

**Concerns**
- **HIGH:** 08-07 and 08-08 reveal 08-06's success criteria were insufficient for behavior preservation.
- **MEDIUM:** Line-count targets can incentivize dense local functions rather than clarity.
- **MEDIUM:** Token scanning for parallelization is useful but incomplete and brittle.
- **MEDIUM:** The facade may still hold enough local branching that future backup/session-result changes require touching it.

**Suggestions**
- Make 08-06 depend on 08-07 and 08-08, or add a second final verification after both.
- Replace strict line-count goals with responsibility checks.
- Keep the source guard, but rely on behavior tests for the real invariant.

**Risk Assessment:** **MEDIUM-HIGH** as a phase gate; it needs the gap closures first.

## 08-07 - CR-01 Stop During Preflight

**Summary:** Correctly targets a real blocker: publishing the session CTS too late makes stop during startup ineffective. The production fix is small and well scoped, but the sample test needs cleanup.

**Strengths**
- Fix is precise: create CTS before orphan cleanup/preflight and pass `cts.Token`.
- Adds a needed regression for stop before xEdit launch.
- Preserves public API and sequential behavior.

**Concerns**
- **HIGH:** The sample `DidNotReceive().CleanPluginAsync(...)` uses an outdated/wrong signature; it should match `CleanPluginAsync(PluginInfo, CancellationToken, Action<Process>?)`.
- **HIGH:** The RED test can leave `StartCleaningAsync` blocked if cancellation does not happen; it needs a `finally` to release/cancel the preflight TCS.
- **MEDIUM:** Stop during `BeginSessionAsync` is only partially covered by the added pre-loop cancellation check.
- **LOW:** Grep-based acceptance should use PowerShell/`rg` in this Windows repo.

**Suggestions**
- Fix the test signature and always unwind the blocked preflight task in `finally`.
- Add a second small test for stop during orphan cleanup or backup-session begin if practical.
- Assert no xEdit launch and canceled session publication, but avoid expecting `StartCleaningAsync` to throw if the orchestrator catches cancellation.

**Risk Assessment:** **MEDIUM-HIGH**. The code change is low risk, but the regression test as written is fragile.

## 08-08 - CR-02 + WR-01 Finalizer Fixes

**Summary:** Strong, focused gap-closure plan. It addresses two real result-consistency bugs in the right file with good tests.

**Strengths**
- Adds the exact missing negative tests.
- Keeps fix surgical: gate `AlreadyClean`, derive `Success` from final status.
- Preserves existing parser/log comments and behavior.

**Concerns**
- **MEDIUM:** `finalSuccess` hard-codes success-equivalent statuses; acceptable now, but future enum additions must update it.
- **MEDIUM:** Terminated/no-log paths can still return `Status=Cleaned, Success=true` with a termination warning if the runner reported success. That may be existing behavior, but it is semantically awkward.
- **LOW:** Grep exact-count checks are brittle against comments or formatting.

**Suggestions**
- Consider a helper like `IsSuccessfulFinalStatus(finalStatus)` if this logic appears elsewhere.
- Add an assertion that exception content is preserved in `LogParseWarning`.
- Keep structural grep checks secondary to behavior tests.

**Risk Assessment:** **LOW-MEDIUM**. The fix is narrow and well tested.

## Cross-Plan Issues

- **HIGH:** 08-07 and 08-08 are marked `depends_on: []`, but they depend on post-extraction files from 08-05/08-06. They should explicitly depend on at least `08-05`, preferably `08-06`.
- **HIGH:** RED-stage verification commands are inconsistent across multiple plans. Standardize: production build should pass; targeted test build/test should fail for the intended missing implementation or failing assertion.
- **MEDIUM:** Several plans rely on literal-string grep acceptance. Useful as a backstop, but behavior tests should be authoritative.
- **MEDIUM:** The plans are highly detailed, sometimes to the point of embedding stale code snippets. That increases executor risk when current models differ.
- **LOW:** Project metadata has some stack drift in the supplied context versus repo guidance; Phase 8 is service-layer work, so this is not blocking.

**Final Risk Assessment:** **MEDIUM-HIGH overall before gap closures; MEDIUM after 08-07 and 08-08 are landed and re-verified.** The architecture achieves the phase goal, but the implementation plans need dependency cleanup, RED-gate correction, and a few test harness fixes to avoid false failures and hidden behavior drift.

---

## Consensus Summary

All three reviewers agree that Phase 8 is architecturally sound and that the collaborator split is an appropriate way to satisfy `REF-01`. The strongest consensus is that the plan quality is high, but the original 08-01 through 08-06 sequence overestimated behavior-preservation coverage. The gap-closure plans 08-07 and 08-08 are viewed as necessary and well-scoped, but reviewers identified dependency metadata and test-harness brittleness that should be cleaned up before feeding these reviews back into planning.

### Agreed Strengths

- The phase follows the right overall shape: characterize first, extract one seam at a time, keep `ICleaningOrchestrator` as the public facade, and preserve sequential xEdit cleaning.
- The collaborator boundaries map well to `REF-01`: preflight, backup session coordination, termination, runner, and finalizer are focused seams.
- High-risk invariants are explicitly called out, especially two-stage stop/force-stop behavior, self-PID refusal, no log parse after unsafe termination, exact launch path preservation, `maxRetryAttempts = 3`, and `XEditDirectory` single-source-of-truth.
- The gap-closure plans are surgical and TDD-oriented. Reviewers specifically praised 08-07 for fixing stop-during-preflight cancellation and 08-08 for fixing result finalization consistency.
- Structural guards against public API drift and accidental parallelization are valuable defensive tests for this project.

### Agreed Concerns

- `08-07-PLAN.md` and `08-08-PLAN.md` have incorrect `depends_on: []` metadata. All reviewers noted that these plans depend on the post-extraction collaborator state, preferably at least `08-06`.
- Characterization coverage was not broad enough before declaring the original 08-01 through 08-06 track behavior-preserving. The missing startup-window stop test allowed CR-01, and missing finalizer negative tests allowed CR-02/WR-01.
- The 08-07 test harness needs tightening: Codex flagged a likely stale `CleanPluginAsync` signature and both Claude/Codex flagged that blocked async tests need robust cleanup/finally release to avoid hangs.
- Several plans embed pseudo-code or literal acceptance checks that can drift from the real model/API shape. The clearest example is the 08-03 `AbortReason`/session-result snippet and the repeated `XEditDirectory` ownership language in 08-02/08-05.
- RED-gate verification commands and grep-style acceptance criteria should be secondary to behavior tests and should be standardized to avoid false failures.

### Divergent Views

- Overall risk differs by reviewer. Gemini rated the phase low risk due to the robust characterize/extract/gap-close process. Claude rated gap closures low risk but the original 1-6 track medium in retrospect. Codex rated the overall plan medium-high before gap closures and medium afterward because it weighted execution hazards more heavily.
- Gemini treated the 08-07/08-08 dependency mismatch as low severity metadata cleanup, while Codex treated it as high severity because executing those plans out of order would fail against the legacy code.
- Codex was more skeptical of reflection and literal-string acceptance checks than the other reviewers, especially in 08-01 public-surface snapshot and 08-03/08-06 structural checks.
- Claude recommended expanding future characterization into a full concurrent-stop matrix across orphan cleanup, preflight, backup begin, per-plugin backup, runner, finalizer, and retention. Gemini focused on the existing plans being sufficient once the known gaps are closed.
