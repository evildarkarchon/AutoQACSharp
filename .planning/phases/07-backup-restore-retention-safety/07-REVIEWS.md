---
phase: 07
reviewers: [gemini, claude, codex]
reviewed_at: 2026-04-29T03:26:00.6499608-07:00
plans_reviewed: [07-01-PLAN.md, 07-02-PLAN.md, 07-03-PLAN.md, 07-04-PLAN.md, 07-05-PLAN.md, 07-06-PLAN.md, 07-07-PLAN.md, 07-08-PLAN.md, 07-09-PLAN.md, 07-10-PLAN.md, 07-11-PLAN.md, 07-12-PLAN.md, 07-13-PLAN.md]
---

# Cross-AI Plan Review - Phase 7

## Gemini Review

# implementation Plan Review: Phase 7 (Backup Restore & Retention Safety)

## Summary
Phase 7 represents a critical hardening of the AutoQAC backup and recovery infrastructure. The plan set (07-01 through 07-13) successfully transitions the application from fragile, synchronous file operations to a robust, async-first service layer with structured outcomes, progress reporting, and meaningful cancellation. The final gap-closure plans (07-12 and 07-13) are well-targeted responses to verification findings, addressing UI lifecycle leaks and ensuring that the "Delete Session" feature respects the same security boundaries as the restore and backup creation features. The overall architecture remains strictly sequential, preserving the hard requirement of one xEdit process at a time.

## Strengths
*   **Sequential Integrity:** The orchestration logic in 07-03 and 07-06 ensures backups are performed per-plugin immediately before xEdit launch, maintaining the established cleaning order.
*   **Atomic Move Semantics:** The use of `ReplaceAtomically` (temporary file copy followed by an atomic move) is a high-confidence design choice that prevents plugin corruption during failed or canceled restores.
*   **Defense-in-Depth Path Validation:** Plans 07-09, 07-10, 07-11, and 07-13 provide comprehensive path validation (traversal rejection, sibling-prefix protection, and trusted root containment) across backup creation, restore, and session deletion.
*   **TDD Discipline:** Every plan utilizes RED/GREEN cycles with named behavioral tests, ensuring that 700+ existing tests are not regressed by the significant service-layer changes.
*   **Clear UX Boundaries:** The separation of "Cancel Backup/Cleanup" from the xEdit "Stop" button prevents confusing process-termination states while still providing responsive file-operation control.

## Concerns

### MEDIUM
*   **Source-Inspection Test Brittleness (07-12):** The verification of the normal progress window lifecycle relies on `ViewSubscriptionLifecycleTests.cs` performing string matches on `MainWindow.axaml.cs`. While necessary due to the lack of a headless UI testing project, these tests are brittle to future refactoring or formatting changes.
    *   *Severity:* **Medium**. It could lead to false negatives or missed coverage if the source code is reorganized without updating the regex patterns.

### LOW
*   **String-Level Containment Limitation (07-11/07-13):** As documented in the plans, the path containment checks are string-level (`Path.GetFullPath` + prefix matching) and do not resolve NTFS reparse points or symlinks. 
    *   *Severity:* **Low**. For a desktop app managing local game files, this is standard and the risk of a "symlink escape" attack via malicious backup metadata is minimal in this context.
*   **Fixed Temp Suffix (07-10):** The implementation retains the fixed `.autoqac-tmp` suffix rather than unique GUID-based temp names. 
    *   *Severity:* **Low**. Collision risk is low due to the sequential nature of the app, but a stale file from a crashed session could technically block a subsequent restore.

## Suggestions
*   **Idempotent Progress Window Close (07-12):** Ensure the `DisposeProgressViewModel()` helper in `MainWindow.axaml.cs` is truly idempotent (using the planned `progressDisposed` guard) to survive the race between `CloseRequested` from the VM and the Window's own `Closed` event.
*   **Consistent Normalization (07-13):** Ensure that `IsSessionDirectoryInsideBackupRoot` uses the same `EnsureTrailingDirectorySeparator` logic established in `BackupService.cs` to prevent sibling-prefix vulnerabilities (e.g., `Backups` matching `Backups 2`).
*   **Log Context for Rejected Deletes (07-13):** When a session deletion is rejected for being "out of root," the log entry should include the `BackupRoot` and the `dirToDelete` to assist in debugging misconfigured data folders.

## Risk Assessment
**Overall Risk: LOW**

The phase is in its final "gap closure" stage. The foundational architecture (managed copy, structured results, and linked CTS) has already been verified through over 700 passing tests. Plans 07-12 and 07-13 are surgical fixes for specific UI and security gaps. By completing these, AutoQAC will have a fail-safe backup system that is both user-friendly and resilient to filesystem edge cases. No risk of parallel xEdit cleaning is introduced.

---
*Phase 07 Plan Review - 2026-04-29*

---

## the agent Review

# Cross-AI Plan Review: Phase 7 Backup Restore & Retention Safety

## Scope Note

Plans 07-01 through 07-11 have shipped (commits in git log; SUMMARY.md files present; 752 AutoQAC tests + 59 QueryPlugins tests passing). The remaining work is **Wave 9**: plans 07-12 (normal progress window lifecycle) and 07-13 (RestoreWindow Delete Session containment). I'll focus this review on those two plans, with brief observations on plan-set cohesion.

---

## Summary

Plans 07-12 and 07-13 are tightly scoped TDD-gated gap closures targeting the exact two FAILED truths in `07-VERIFICATION.md` (Truth #19 normal progress lifecycle; Truth #20 Delete Session containment). Both have correct technical direction, sound TDD discipline, and reuse established Phase 7 patterns (string-level containment, concise UI copy, logged technicals). Two material concerns warrant attention before execution: (1) **07-12's RED test is solely a source-string assertion** and may be fragile against equally valid implementations that don't match the exact substring; (2) **07-13's helper duplicates path-containment logic that already exists three times in `BackupService.cs`** (`IsRestoreTargetInsideTrustedRoot`, `EnsureTrailingDirectorySeparator`, `IsSafeSessionRelativeName`), so DRY/centralization is the bigger architectural risk than correctness.

---

## Plan 07-12: Normal Progress Window Lifecycle

### Strengths

- **Exactly mirrors the existing preview path** (lines 161-166 of `MainWindow.axaml.cs`) which already does `progressViewModel.CloseRequested += (_, _) => progressWindow.Close();` — the asymmetry is the verified bug.
- **Local closure-based double-dispose guard** (`progressDisposed` + local function) is idiomatic and matches the same defensive pattern in `ProgressWindow.axaml.cs:9` (`_disposeHandled`).
- **Preserves `ProgressWindow.axaml.cs` lifecycle helper unchanged** — Plan correctly recognizes the centralized cleanup must keep working.
- **TDD gate enforces named lifecycle test** which integrates with the existing `ViewSubscriptionLifecycleTests.cs:46` pattern.
- **Honors the no-Avalonia-headless constraint** documented in CLAUDE.md by using source-string assertions.

### Concerns

- **MEDIUM — Source-string RED is brittle.** The test asserts five exact substrings (`progressViewModel.CloseRequested +=`, `progressWindow.Close()`, `progressWindow.Closed +=`, `progressViewModel.Dispose()`, `progressDisposed`). An equally valid implementation using a method group (`progressWindow.Closed += OnProgressClosed;`) or a different guard variable name (`disposed`, `cleaned`, `done`) would fail the test even though the behavior is correct. Either:
  - Loosen the assertion to regex like `progressWindow\.Closed\s*\+=` and `Dispose\s*\(`, or
  - Document the exact-string contract in the test rationale so future readers know they must match the convention literally.

- **MEDIUM — Double-subscription risk with `ProgressWindow.axaml.cs`.** The existing `ProgressWindow.OnDataContextChanged` (line 28) *already* subscribes to `viewModel.CloseRequested` and disposes the VM in `OnClosed`. Adding a *second* `CloseRequested` subscription in `ShowProgressAsync` means `progressWindow.Close()` may run twice on the same Close click (once from the new handler, once via the existing window subscription that calls `Close()` from `OnCloseRequested`). Avalonia's `Window.Close()` is idempotent so this likely works, but it's worth either:
  - Confirming the double-close is harmless and adding a comment, or
  - Recognizing that `ProgressWindow.axaml.cs` already provides the contract — and the actual gap is just that the **disposal** path in `ProgressWindow.axaml.cs` only runs when `DataContext` was set via `DataContextChanged` while `_disposeHandled` is still false. Re-reading: yes, the existing `ProgressWindow` *does* dispose the VM on `OnClosed`. So **this gap may already be partially closed**, and the verification report's Truth #19 may be over-flagging given that `ProgressWindow.OnClosed → DisposeViewModelIfNeeded → _subscribedViewModel.Dispose()` already handles disposal.
  - **Recommend the executor verify the actual runtime behavior** before adding redundant wiring. The Phase 7 verifier's source inspection caught a missing inline pattern, but the lifecycle helper may already cover the runtime contract.

- **LOW — Plan doesn't acknowledge the existing `ProgressWindow` cleanup helper.** Plan text says "primary ShowProgressAsync path lacks the CloseRequested handler" — but the window itself subscribes via `DataContextChanged`. The plan should explicitly state whether the new wiring is *additive* (defense in depth) or *redundant-but-required-for-test-pass*, so reviewers don't merge code that does the same work twice.

### Suggestions

1. **Add one runtime-style assertion** alongside the source-grep test: instantiate a `ProgressWindow` with a real `ProgressViewModel` (or a stub), raise `CloseRequested`, and assert the window closed — even without Avalonia.Headless, this can be done by wrapping the test in a `Dispatcher.UIThread.InvokeAsync` block if the app target supports it. If that's infeasible per CLAUDE.md, then loosen the source assertion to regex.

2. **Consult `ProgressWindow.axaml.cs` lifecycle helper** before adding a parallel handler in `ShowProgressAsync`. If the runtime contract is already correct via DataContextChanged, the plan may only need to:
   - Update the verifier's understanding (Truth #19 is satisfied by the View, not the Main).
   - Or leave `ShowProgressAsync` alone and document the contract.

3. **If keeping the additive wiring**, add a one-line comment in `ShowProgressAsync` like `// Defense in depth: ProgressWindow.OnDataContextChanged also subscribes; this guarantees close+dispose even if the View contract changes.`

---

## Plan 07-13: RestoreWindow Delete Session Containment

### Strengths

- **Direct closure of verified Truth #20** with concrete file/line references in the verification report (lines 301-322 of `RestoreViewModel.cs`).
- **Reuses existing concise-error pattern** from Phase 7 (`Technical details were written to the log.`) and matches D-04.
- **Sibling-prefix and traversal coverage explicit** — addresses the same class of Windows path bug fixed in Plan 07-11 for trusted restore root.
- **Replaces `ex.Message` exposure** in the catch block, closing a small information-disclosure leak that wasn't itself flagged but is consistent with SEC-01/SEC-02 direction (deferred to Phase 11 but freebie here).
- **TDD discipline preserved** with three separate RED tests (out-of-root, sibling-prefix, traversal-normalization).
- **Keeps NSubstitute optional-parameter matching explicit** — matches the project convention.

### Concerns

- **HIGH — Helper duplication.** The plan creates `IsSessionDirectoryInsideBackupRoot` in `RestoreViewModel.cs`, which is **functionally identical** to `BackupService.IsRestoreTargetInsideTrustedRoot` (lines 654-671 of `BackupService.cs`). Both:
  - Normalize a root with `EnsureTrailingDirectorySeparator(Path.GetFullPath(root))`
  - Normalize a candidate path with `Path.GetFullPath`
  - Check `StartsWith(root, StringComparison.OrdinalIgnoreCase)`
  - Wrap in try/catch for `ArgumentException, IOException, NotSupportedException, UnauthorizedAccessException`

  The difference is only the field being validated (`trustedRestoreRoot` vs `_backupRoot`). Codex's review of Plan 07-11 explicitly recommended a *shared helper* (`NormalizeDirectoryRootForContainment`). This plan repeats the smell instead of resolving it.

  **Recommendation:** Either (a) extract a public/internal static helper into a new file like `AutoQAC/Services/Backup/PathContainment.cs` and call it from both `BackupService` and `RestoreViewModel`, or (b) put the deletion-safety check on `BackupService` itself by adding `Task<BackupRetentionRowResult> DeleteSessionAsync(string sessionDirectory, string backupRoot, ...)` and have `RestoreViewModel` delegate. Option (b) also keeps filesystem operations in the service layer per CLAUDE.md ("Service layer reads logs; ViewModels receive parsed results via state").

- **MEDIUM — ViewModel performs filesystem I/O directly.** Currently `DeleteSessionAsync` calls `System.IO.Directory.Delete` from the ViewModel. The CLAUDE.md MVVM guidance is "All business logic lives in services, not ViewModels" and "Services in `AutoQAC/Services` ... keep their `System.Reactive` use; the Rx ban applies only to the ViewModel layer." Adding filesystem-validation logic to the VM compounds the existing layering violation. A `IBackupService.DeleteSessionAsync(BackupSession session)` method that owns containment + delete + retry would be cleaner architecturally and match the service patterns in `IBackupSessionDeleter` (which already exists from Plan 07-02, line 60 of `ServiceCollectionExtensions.cs`).

  **`IBackupSessionDeleter` is already injectable** — the plan could route Delete Session through that instead of direct `Directory.Delete`. This would also let retention-cleanup-style tests reuse `RecordingBackupSessionDeleter`.

- **MEDIUM — Plan doesn't enumerate behavior for legacy/null `_backupRoot`.** What happens if Delete Session is somehow invoked when `LoadSessionsAsync` was called with `null` or `""`? The `CanRestoreAll` predicate (which gates `DeleteSessionCommand`) requires `SelectedSession != null`, but does NOT require `HasTrustedRestoreRoot` (Plan 07-11 only added that to Restore Selected/All). So a session can theoretically be selected with `_backupRoot == null` (if `LoadSessionsAsync` was called with a valid root, then later changed). The new helper handles this (returns false on null backup root) but the *test plan* should explicitly cover it. The current `RED` task doesn't list a null-backup-root case.

- **LOW — Status text inconsistency.** Plan specifies `StatusText = "Delete failed -- selected backup session is outside the configured backup folder";` but the dialog shows `"The selected backup session is outside the configured backup folder."` Two different copies for the same condition. Prefer one canonical sentence.

- **LOW — `using System.IO;` directive.** Plan says "add `using System.IO;` if needed and replace fully qualified `System.IO.Directory` calls". `RestoreViewModel.cs` doesn't currently `using System.IO` (it uses fully qualified `System.IO.Directory.Exists` / `System.IO.Directory.Delete` per line 304-306). This is a minor style change that's fine but worth noting it slightly expands the diff.

- **LOW — Verification command in Task 3.** The cluster filter list in the acceptance criterion is long and includes `ViewSubscriptionLifecycleTests` (which 07-12 owns). If 07-12 hasn't shipped first, this command will reference a non-existent test. Ensure 07-12 ships before or alongside 07-13, or remove `ViewSubscriptionLifecycleTests` from 07-13's filter.

### Suggestions

1. **Extract path-containment helper.** Add `internal static class BackupPathContainment` (or similar) with `IsContained(string? candidatePath, string? rootPath)` that handles normalization + try/catch. Refactor `BackupService.IsRestoreTargetInsideTrustedRoot` to call it. Then `RestoreViewModel` calls the same helper. This closes Codex's deferred suggestion from the 07-09/07-11 review.

2. **Move Delete Session to the service.** Add `Task<BackupSessionDeleteResult> DeleteSessionAsync(BackupSession session, string backupRoot, CancellationToken ct)` to `IBackupService`. The ViewModel calls it, receives a structured result, and updates `Sessions` / `StatusText` from the outcome. This matches the Plan 07-02 retention pattern and lets you reuse `IBackupSessionDeleter` for the actual `Directory.Delete`.

3. **Add null-backup-root coverage.** Either gate `DeleteSessionCommand` predicate on `!string.IsNullOrWhiteSpace(_backupRoot)` (mirroring the trusted-root gate from Plan 07-11), or add an explicit test `DeleteSessionCommand_NullBackupRoot_FailsClosed`.

4. **Reconcile status text and dialog text** to one sentence used in both places.

5. **Consider tightening `RestoreCommands_DisabledWhenTrustedRestoreRootMissing` to also disable `DeleteSessionCommand`.** Plan 07-11 explicitly didn't disable Delete Session. Now that Delete Session is part of restore safety, gating it on the same condition keeps the safety story consistent.

---

## Plan-Set Cohesion Notes

- **Helper centralization is becoming a real debt.** Plans 07-09, 07-11, and 07-13 each add path-validation helpers (`IsSafeSessionRelativeName`, `IsRestoreTargetInsideTrustedRoot`, `IsSessionDirectoryInsideBackupRoot`). Codex flagged this in the consolidated review. Wave 9 is the last opportunity to consolidate before Phase 8 starts. Strongly recommend a 5-line refactor task in 07-13 (or a new 07-14) to extract a shared helper.

- **Delete Session was outside Phase 7's original scope.** The phase boundary in `07-CONTEXT.md` lists "restore safety, backup/restore/retention progress and cancellation, retention cleanup failure reporting, and regression coverage." Backup-session deletion isn't mentioned. The verifier added it as a blocker because it's in the same code surface, which is reasonable, but the scope creep is real. Consider documenting in `07-13-SUMMARY.md` that this gap was discovered during verification and is closed defensively to maintain the phase's safety story.

- **Sequential xEdit invariant remains protected.** Both plans' source scan for `Task.WhenAll | Parallel.ForEachAsync | Task.Run` under `AutoQAC/Services/Cleaning` correctly stays as a secondary verification gate.

- **Verification cluster grows.** With these two plans, the Phase 7 targeted cluster reaches `BackupFileCopierTests | BackupServiceTests | RestoreViewModelTests | CleaningOrchestratorTests | ProgressViewModelTests | ViewSubscriptionLifecycleTests | BackupOperationResultTests`. That's ~150-160 tests. Worth confirming runtime budget; if it exceeds ~30 seconds locally, splitting the cluster into "fast" and "slow" filters in `07-VALIDATION.md` may help future iteration.

---

## Risk Assessment

| Plan | Risk | Justification |
|------|------|---------------|
| **07-12** | **LOW-MEDIUM** | Source-string brittleness and possible redundant wiring with `ProgressWindow.axaml.cs`. Behavior outcome is sound; mechanism question is the open issue. |
| **07-13** | **MEDIUM** | Helper duplication compounds existing debt; ViewModel filesystem I/O reinforces a layering smell. Functional correctness is fine; architectural cleanliness is the risk. |
| **Plan set overall** | **LOW-MEDIUM** | Phase 7 has 11 shipped plans, 752+59 passing tests, validated TDD discipline, and the two remaining gaps are localized. The architecture is proven. The remaining risk is incremental debt accumulation, not user-visible regressions. |

---

## Recommended Execution Order

1. **Investigate first**: Verify whether `ProgressWindow.OnDataContextChanged + OnClosed` already satisfies Truth #19 at runtime. If yes, downgrade 07-12 to a documentation/verification-clarification task.
2. **07-12 next** (regardless of #1 outcome): if defensive wiring is added, document it as such; if not, update the verifier's expectation.
3. **Refactor pass before 07-13**: extract `BackupPathContainment` helper and refactor `BackupService.IsRestoreTargetInsideTrustedRoot` to call it (10-line change).
4. **07-13**: implement Delete Session containment using the shared helper, ideally by routing through `IBackupSessionDeleter` from the service layer rather than direct ViewModel `Directory.Delete`.
5. Re-run full Phase 7 verification cluster, archive the phase.

---

*Review completed: 2026-04-29*

---

## Codex Review

**Overall Assessment**

The Phase 7 plan set is strong: it is traceable to SAF-04, TEST-04, and PERF-04, keeps xEdit cleaning sequential, and uses iterative gap-closure well. The main weakness is plan sprawl: later plans repeatedly patch earlier safety assumptions, which is normal for verification-driven work but increases signature churn and regression risk. The biggest risks are path-containment edge cases, sync/async compatibility drift, cancellation semantics, and source-inspection tests becoming brittle.

**Plan Reviews**

### 07-01
**Summary:** Good foundation for structured outcomes and cancellable copy.
**Strengths:** Clear result contracts; explicit overwrite policy; partial-file cleanup tested.
**Concerns:** MEDIUM: fixed temp suffix may collide. MEDIUM: result model may grow too broad before callers prove needs.
**Suggestions:** Define temp-name ownership now, or explicitly defer unique temp names with a test guard.
**Risk:** MEDIUM, because copier mistakes can corrupt backup/restore targets.

### 07-02
**Summary:** Correctly moves restore/retention from exceptions/logging into structured outcomes.
**Strengths:** Continues restore after failures; retention protects current/newest sessions; deletion seam is testable.
**Concerns:** HIGH: legacy sync wrappers can drift from async safety. MEDIUM: metadata validity and containment are still incomplete here.
**Suggestions:** Require all restore validation helpers to be shared by sync and async paths from the start.
**Risk:** MEDIUM-HIGH due to filesystem delete/overwrite behavior.

### 07-03
**Summary:** Good orchestration integration while preserving per-plugin sequential backup-before-cleaning.
**Strengths:** Separate non-xEdit cancellation path; explicit no-parallel guard; retention result included before finalization.
**Concerns:** MEDIUM: “backup canceled then continue next plugin” may surprise users if they expected session cancel. MEDIUM: CTS ownership is subtle.
**Suggestions:** Make UI copy distinguish “cancel current backup” from “cancel cleaning session.”
**Risk:** MEDIUM.

### 07-04
**Summary:** Restore UI plan is well aligned with decisions D-17 through D-20.
**Strengths:** Inline results; no success popup; progress/cancel state; command disabling.
**Concerns:** MEDIUM: progress callback threading was missed until 07-08. LOW: exact string tests may be brittle.
**Suggestions:** Add dispatcher marshalling in this plan rather than later.
**Risk:** MEDIUM.

### 07-05
**Summary:** Completes user-visible backup/retention progress in the cleaning progress surface.
**Strengths:** Separate Cancel Backup/Cleanup from xEdit Stop; final full-suite validation.
**Concerns:** LOW-MEDIUM: hard-coded color acceptance is design-brittle. MEDIUM: source-grep no-parallel checks should stay secondary.
**Suggestions:** Prefer behavior tests for command routing and ordering; keep source scans as smoke checks.
**Risk:** MEDIUM.

### 07-06
**Summary:** Valuable gap closure for backup-failure accounting.
**Strengths:** Fixes skipped-result publication and abort finalization; tightly scoped.
**Concerns:** LOW: finalization inside branch can duplicate normal-finalization logic over time.
**Suggestions:** Extract a small finalization helper if similar branches already exist.
**Risk:** LOW-MEDIUM.

### 07-07
**Summary:** Important security hardening for metadata and retention progress.
**Strengths:** Blocks traversal/absolute backup filenames; adds deletion-warning coverage.
**Concerns:** HIGH: “unrooted OriginalPath” rejection is not enough; arbitrary rooted targets remain until 07-11. MEDIUM: mapping unsafe metadata to “Missing backup file” may obscure tampering diagnostics in logs if not logged clearly.
**Suggestions:** Add trusted-root containment earlier or make 07-07 explicitly incomplete.
**Risk:** MEDIUM-HIGH.

### 07-08
**Summary:** Good targeted closure for dispatcher, backup setup failures, and retention cancellation.
**Strengths:** Addresses real async UI-thread risk; converts cancellation into structured outcomes.
**Concerns:** MEDIUM: changing `RestoreViewModel` constructor can break manual/test construction. MEDIUM: cancellation rows/counts may be inconsistent if classification is only partially complete.
**Suggestions:** Add a constructor/call-site audit and tests for partial classification counts.
**Risk:** MEDIUM.

### 07-09
**Summary:** Strong filesystem-safety refinement for filenames and mixed cancellation.
**Strengths:** Centralized validation; positive safe-restore regression; legacy sync audit.
**Concerns:** HIGH: still allows same-name rooted target outside the game Data folder until 07-11. LOW: ghosted plugin exclusion is acceptable but should be documented as compatibility impact.
**Suggestions:** Merge trusted-root policy into this plan if execution order allows.
**Risk:** MEDIUM-HIGH.

### 07-10
**Summary:** Excellent narrow TDD fix for copy-attempt ownership.
**Strengths:** Addresses a concrete destructive bug; tests both non-owned destination and owned temp.
**Concerns:** MEDIUM: fixed `.autoqac-tmp` remains a collision/race risk.
**Suggestions:** Use same-directory unique temp names if implementation cost is small; otherwise document as residual.
**Risk:** MEDIUM.

### 07-11
**Summary:** Essential plan; this closes the biggest restore overwrite boundary.
**Strengths:** Trusted restore root; sibling-prefix test; UI disables restore with missing root; call-site audit.
**Concerns:** HIGH: signature churn across many tests/callers is risky. MEDIUM: string containment does not handle reparse points or TOCTOU. MEDIUM: sync restore signature changes may not be worth preserving.
**Suggestions:** Consider deprecating/removing sync restore APIs after migration, or make them internal test-only.
**Risk:** HIGH, but justified.

### 07-12
**Summary:** Useful lifecycle cleanup, but the test strategy is weaker than the behavior being protected.
**Strengths:** Closes stale subscription risk; adds explicit normal-path disposal.
**Concerns:** MEDIUM: source-inspection lifecycle tests can pass while runtime wiring is still wrong. MEDIUM: duplicate disposal with `ProgressWindow` lifecycle helper may create redundant ownership.
**Suggestions:** Prefer a small testable factory/lifecycle helper over asserting strings in `MainWindow.axaml.cs`.
**Risk:** MEDIUM.

### 07-13
**Summary:** Strong final safety patch for recursive Delete Session containment.
**Strengths:** Covers traversal, sibling-prefix, normal delete, and concise error copy.
**Concerns:** HIGH: direct `Directory.Delete` remains in ViewModel, which is harder to test safely than an injected deleter. MEDIUM: string-level containment still ignores reparse points.
**Suggestions:** Add an `IBackupSessionDeleteService` or move deletion into `IBackupService` using the same root-containment rules.
**Risk:** MEDIUM-HIGH because recursive delete bugs have high blast radius.

**Top Recommendations**

- Collapse repeated path-validation logic into one shared, documented helper set for backup destination, restore source, restore target, and delete-session containment.
- Treat source-inspection tests as secondary only. The plans already mostly do this, but 07-12 is too string-test-heavy.
- After 07-11, remove or quarantine legacy sync restore APIs if no production caller remains.
- Add one final verification checklist that explicitly covers: normal restore, partial restore, canceled restore, trusted-root rejection, delete-session rejection, retention warning, retention cancellation, backup cancellation, and full `dotnet test`.
- Document residual risks in the final Phase 7 summary: reparse points/symlinks, fixed temp suffix if unchanged, ghosted plugin extensions, and any compatibility impact from rejecting UNC/device/out-of-root restore targets.

**Overall Risk Assessment: MEDIUM-HIGH**

The phase goals are achievable with these plans, and coverage is unusually strong. The risk level stays medium-high because this phase touches destructive filesystem operations, cancellation, UI lifecycle, and broad service signatures. The later gap-closure plans reduce most serious risks, but they also prove that early plans under-specified path containment and lifecycle behavior. The set should work if executed in order with full-suite validation after each signature-heavy or filesystem-delete change.

---

## Consensus Summary

All three reviewers consider Phase 7 coherent and substantially validated, with the remaining risk concentrated in the final gap-closure plans for progress-window lifecycle handling and RestoreWindow delete containment.

### Agreed Strengths

- The plans preserve AutoQAC's sequential xEdit invariant; no reviewer found evidence that backup, restore, retention, or UI work would parallelize cleaning.
- The phase uses strong verification-driven planning with explicit RED/GREEN gates, targeted regression tests, and clear traceability to SAF-04, TEST-04, and PERF-04.
- The backup/restore design is safety-oriented: structured outcomes, cancellable async file work, visible progress, atomic copy/move semantics, and fail-closed validation are consistently called out as good choices.
- The later gap-closure plans address real verification findings rather than speculative scope, especially around restore target containment, cleanup ownership, progress-window lifecycle, and delete-session safety.

### Agreed Concerns

- 07-12's planned source-inspection tests are brittle. Gemini, the agent, and Codex all flagged that string-based assertions should be secondary to runtime behavior or at least loosened/documented so formatting or equivalent handler shapes do not cause false failures.
- 07-12 may duplicate existing `ProgressWindow` lifecycle ownership. The agent and Codex specifically warned that `ProgressWindow.axaml.cs` may already subscribe/dispose through `DataContextChanged`/`OnClosed`, so the executor should verify actual behavior before adding redundant close/dispose wiring.
- 07-13 risks duplicating path-containment logic. The agent and Codex strongly recommend extracting or reusing a shared containment helper, and Gemini also calls for consistent normalization using the same trailing-separator/sibling-prefix protection already established elsewhere.
- Delete Session still sits close to destructive filesystem behavior. The agent and Codex recommend moving containment and recursive delete behind a service/injected deleter rather than adding more direct `Directory.Delete` logic to the ViewModel.
- Residual filesystem edge cases remain. Reviewers called out string-level containment not resolving NTFS reparse points/symlinks, fixed `.autoqac-tmp` collision risk after crashes, and the need to document these as accepted residual risks if not fixed in Phase 7.

### Divergent Views

- Overall risk rating differs: Gemini rates the remaining plan set LOW, the agent rates it LOW-MEDIUM overall with 07-13 at MEDIUM, and Codex rates the full phase MEDIUM-HIGH because destructive filesystem operations have high blast radius even with strong coverage.
- Gemini accepts 07-13 as a surgical ViewModel containment patch if it reuses established normalization. The agent and Codex prefer a service-layer delete path or shared delete service to reduce MVVM and testability debt.
- The agent suggests 07-12 may be partly or fully satisfied by the existing `ProgressWindow` lifecycle helper, while Gemini treats the planned idempotent close/dispose wiring as appropriate defense in depth. This should be resolved by checking the actual current lifecycle before implementation.

