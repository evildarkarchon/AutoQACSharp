---
phase: 07
reviewers: [gemini, claude, codex]
reviewed_at: 2026-04-28T23:31:35.1289535-07:00
plans_reviewed: [07-01-PLAN.md, 07-02-PLAN.md, 07-03-PLAN.md, 07-04-PLAN.md, 07-05-PLAN.md]
---

# Cross-AI Plan Review - Phase 7

## Gemini Review

# Phase 7: Backup Restore & Retention Safety - Plan Review

The implementation plans for Phase 7 provide a comprehensive and technically sound strategy for hardening the backup and restore subsystem of AutoQAC. By transitioning from synchronous, exception-driven logic to an asynchronous, structured outcome-based pipeline, the plans significantly improve the application's resilience and user transparency during critical file operations.

### Summary
Phase 7 correctly addresses the risks of "silent" backup failures and "all-or-nothing" restore logic identified in the codebase concerns. The introduction of `IBackupFileCopier` and a rich status model (`Complete`, `Partial`, `Failed`, `Canceled`) allows the application to handle real-world filesystem edge cases-such as missing directories, permission locks, and user cancellation-without compromising the integrity of the cleaning session. The plans rigorously maintain the "Sequential xEdit" constraint while providing modern UX features like byte-level progress and granular cancellation.

### Strengths
*   **Structured Outcome Modeling**: Moving away from boolean success/failure to a 5-state status model (Complete, Partial, Failed, Canceled, Warning) is a best-practice approach for batch operations.
*   **Atomic Cancellation Hygiene**: The commitment to deleting partial destination files on cancellation (D-08) prevents "corrupt" backups or partial restores from being treated as valid.
*   **Sequential Integrity**: The plans explicitly avoid parallel execution (`Task.WhenAll`), ensuring that file operations and xEdit launches remain strictly ordered as per project requirements.
*   **UI Transparency**: The "Restore All" logic (D-01/D-19) is correctly designed to continue past individual failures, providing users with an actionable report instead of a generic error dialog.
*   **Granular Cancellation**: Separating file-operation cancellation (Cancel Backup/Cleanup) from process termination (Stop xEdit) provides superior UX control without overloading the process safety state.

### Concerns
*   **Retention Deletion Retry Window (MEDIUM)**: Plan 07-02 suggests a `Task.Delay(50, ct)` before retrying a failed deletion. For some aggressive antivirus scanners or Windows Search indexers, 50ms may be too short.
*   **UI Layout Density (LOW)**: Plan 07-05 adds a new progress band to the `ProgressWindow`. Care must be taken in `ProgressWindow.axaml` to ensure this doesn't push the main plugin list or progress bar out of the fixed-size window's visible area.
*   **Restore Overwrite Confirmation (LOW)**: While the plan includes count-based confirmation, it does not explicitly mention if the confirmation dialog should warn about the *age* of the backup if it is significantly different from the current file's timestamp. (Note: D-18 includes session timestamp, which mostly mitigates this).

### Suggestions
*   **Retention Retry Tuning**: Consider increasing the retention retry delay to `250ms` or making it a small exponential backoff (e.g., 100ms, then 500ms) to better handle transient file locks.
*   **Logging Detail**: Ensure that while the UI shows concise labels (e.g., `Access denied`), the `ILoggingService` captures the full technical exception and stack trace for local troubleshooting.
*   **Progress Throttling**: In the `BackupFileCopier` loop, consider a simple threshold (e.g., update progress every 100KB or every 10ms) to prevent UI thread saturation if many small files are processed rapidly.

### Risk Assessment: LOW
The overall risk is low. The plans are surgical, respect existing MVVM boundaries, and utilize Wave-based deployment to ensure the foundation (Results/Copier) is verified before UI integration begins. The heavy emphasis on automated verification of edge cases (missing targets, permission errors) directly fulfills the `SAF-04` and `TEST-04` requirements.

---

## the agent Review

# Cross-AI Plan Review: Phase 7 - Backup Restore & Retention Safety

## Overall Summary

The five plans collectively chart a coherent path from foundation contracts -> service implementation -> orchestrator integration -> restore UI -> progress UI. The wave ordering is reasonable, the structured-outcome model addresses the SAF-04 requirements, and the sequential xEdit preservation is enforced at multiple levels (orchestrator, source-level guard, tests). However, several integration seams between plans are under-specified - particularly **cancellation token ownership in the orchestrator**, **legacy sync-wrapper behavior during transition**, and **how a backup-canceled plugin appears in session results**. There is also some inconsistency in result-type usage (legacy `BackupResult` for backup vs new structured types for restore/retention).

Risk level: **MEDIUM** - the architecture is sound but several gaps could surface as integration bugs in Wave 3.

---

## Plan 07-01: Result Contracts & Copier Foundation

### Strengths
- Clean separation of pure data contracts (`BackupOperationResults.cs`) from the service implementation.
- Cancellation-deletes-partial behavior is explicit in both behavior and threat model (T-07-02).
- Singleton DI registration is appropriate (copier is stateless).
- TDD ordering is correct.

### Concerns
- **MEDIUM - Overwrite semantics unspecified.** `IBackupFileCopier.CopyAsync` has no overwrite parameter. Backup-direction copies use `overwrite: false` (existing code), restore-direction uses `overwrite: true`. Plan 07-02 punts on this with vague language ("by replacing the target through a temp file or by allowing copier to overwrite"). This belongs in the contract.
- **MEDIUM - Reason-label conflation.** `Missing backup file` is a *restore-specific* failure (source = backup file). For backup-direction copies, source-missing means the *plugin* is missing, which already has different handling in `BackupService.BackupPlugin`. A single `BackupFailureReason` enum forces both directions to share labels that don't fit cleanly. Either the copier should emit a generic `SourceMissing` and let callers map, or two reasons should exist.
- **LOW - File mode/share unspecified.** `FileStream` mode (`Create` vs `CreateNew`) and share flags are not in the action. `FileMode.Create` truncates, which is the right choice for restore but may surprise readers expecting `CreateNew`.
- **LOW - Acceptance criteria are text-grep, not behavioral.** Checking that the file "contains `FileStream` and `Delete` and `OperationCanceledException`" is brittle - comments containing these words would pass.
- **LOW - Plan 07-01 also extends `IBackupService` with async signatures returning `Task<BackupResult>` for backup but `Task<BackupRestoreResult>` for restore.** This asymmetry survives into all later plans.

### Suggestions
- Add a `bool overwrite` parameter (or `FileExistsBehavior` enum) to `CopyAsync`.
- Specify atomic temp-file + `File.Move(..., overwrite: true)` strategy in the contract docs to guarantee restore never leaves a half-written target on cancellation.
- Tighten acceptance criteria with a behavioral assertion (e.g., "test creates 100MB file, cancels mid-copy, asserts destination does not exist").
- Decide whether `BackupPluginAsync` should return a structured type for symmetry with `RestorePluginAsync`.

---

## Plan 07-02: Async Service Implementation

### Strengths
- Continue-past-failure aggregation (D-01) and missing-target recreation (D-02) are explicit.
- Retry-once-then-keep retention rule (D-15) and current-session protection (D-14) are specified.
- Test names map directly to context decisions.

### Concerns
- **HIGH - Legacy sync wrapper behavior change.** "Keep legacy `RestorePlugin`/`RestoreSession` wrappers by throwing only when the structured result is not successful" silently changes contract. Existing test `RestorePlugin_MissingBackupFile_ThrowsFileNotFoundException` (BackupServiceTests.cs:280) expects `FileNotFoundException` specifically - the wrapper would now throw something generic. The plan should either update that test or commit to removing the legacy method (since Plan 07-04 rewrites the only caller).
- **HIGH - `.GetAwaiter().GetResult()` deadlock risk.** The legacy sync wrappers calling new async methods is a classic UI-thread deadlock pattern. Plan does not specify how sync->async is bridged. If it's via `.Result` or `.GetAwaiter().GetResult()` this can hang in Avalonia UI contexts.
- **MEDIUM - Atomic restore strategy is hand-wavy.** "by replacing the target through a temp file *or* by allowing copier to overwrite" leaves implementation ambiguous. Without temp-file + atomic move, a canceled restore mid-overwrite leaves a corrupted target - *worse* than the current behavior, which throws before any partial overwrite. This directly conflicts with D-08.
- **MEDIUM - `Task.Delay(50, ct)` retry will throw on cancellation.** If retention is canceled during the retry delay, this surfaces as `OperationCanceledException`. Plan must specify catching this and emitting `Canceled` row, not `Cleanup deletion failed`.
- **MEDIUM - Cancellation testability for retention.** Asserting `CleanupOldSessionsAsync_ReportsCanceled` deterministically requires either a cancellable hook between deletes (a `Func<...>` on the service) or pre-canceling the token before the call. Plan does not specify.
- **LOW - `BackupService` constructor changes** to take `IBackupFileCopier` - this affects DI but isn't called out in `files_modified` for `ServiceCollectionExtensions.cs` (only Plan 07-01 modifies it).

### Suggestions
- Remove the legacy sync wrappers in this plan rather than maintaining them. Update RestoreViewModel's calls in Plan 07-04 (already required). The wrappers are dead-end debt.
- Mandate atomic restore: copy to `target.path + ".autoqac-restore-tmp"`, then `File.Move(tmp, target, overwrite: true)`. Cancellation deletes the tmp file; target stays intact.
- Inject an `IBackupSessionDeleter` (or expose a `Func<string, CancellationToken, Task>` test seam) so retry/cancellation paths are deterministically testable.
- Add Plan 07-02 to `files_modified` for `ServiceCollectionExtensions.cs` if `BackupService` constructor changes.

---

## Plan 07-03: Orchestrator Integration

### Strengths
- Correctly preserves per-plugin backup placement (D-06) and forbids `Task.WhenAll`.
- Awaits retention before final session emission (D-12).
- Adds dedicated `BackupOperationState` so xEdit Stop is not overloaded (D-11).

### Concerns
- **HIGH - Cancellation token design unspecified.** The orchestrator currently has one `_cleaningCts`. Cancelling backup must NOT cancel the entire session/xEdit. Plan says "expose `CancelBackupOperationAsync` or equivalent" but doesn't describe whether this is a separate CTS linked to the session token, or a flag, or token replacement. Without an explicit design, this is the most likely place for a regression: a "Cancel Backup" click that accidentally aborts cleaning.
- **HIGH - Plugin-result mapping for backup-canceled is undefined.** D-07 requires backup-canceled plugins to skip xEdit. The orchestrator currently has `BackupFailureCallback` returning `SkipPlugin | AbortSession | ContinueWithoutBackup`. The plan does not say whether backup cancellation:
  1. Maps to `Skipped` status in `pluginResults`
  2. Adds a new `CleaningStatus.BackupCanceled` value
  3. Reuses the `BackupFailureCallback` path
  This affects `CleaningSessionResult` aggregation and downstream UI.
- **MEDIUM - Existing `BackupFailureCallback` flow unaddressed.** `CleaningOrchestrator.cs:282-322` has callback-driven failure handling. With async structured results, does the callback still fire? Does it still receive `error.Message` text? Plan should explicitly say whether the callback API survives, mutates, or is replaced.
- **MEDIUM - Partial backup metadata flow.** The existing `OperationCanceledException` catch (CleaningOrchestrator.cs:506-545) writes partial metadata. With backup cancellation now distinct from xEdit cancellation, the metadata-on-cancel path needs review - does it run on backup-only cancel? Plan doesn't say.
- **MEDIUM - Retention runs before `FinishCleaningWithResults`.** Currently retention is "fire-and-forget after metadata write." Awaiting it changes the timing of `CleaningCompleted` emission. This is correct per D-12, but tests/UI subscribers depending on early completion may need updating.
- **LOW - Acceptance criterion `does not contain Task.WhenAll`** is good but doesn't catch other parallel constructs (`Parallel.ForEachAsync`, `Task.Run` loops).

### Suggestions
- **Explicit token design**: `_cleaningCts` (session) -> `_backupOperationCts = CTS.CreateLinkedTokenSource(_cleaningCts.Token)`. `CancelBackupOperationAsync` cancels only the linked source. Document this in the plan.
- Add a new `CleaningStatus.SkippedBackupCanceled` (or reuse `Skipped` with a reason field) and specify it in the plan.
- Address `BackupFailureCallback` explicitly: keep, modify, or deprecate.
- Broaden the parallel-guard to also forbid `Parallel.` and `Task.Run` inside the foreach loop.

---

## Plan 07-04: Restore Window UI

### Strengths
- UI-SPEC copy is quoted verbatim, ensuring downstream ckecker compliance.
- Cancel CTS lifecycle (own -> cancel -> dispose on completion or VM dispose) follows established RestoreViewModel patterns.
- Inline result rows match D-19/D-20 (no popup, no auto-close).

### Concerns
- **HIGH - Existing test break is not flagged.** `RestorePlugin_MissingBackupFile_ThrowsFileNotFoundException` will fail once `RestoreViewModel` no longer calls `RestorePlugin` (the throwing sync method). Plan should explicitly update or remove that test, or Plan 07-02 should remove the throwing wrapper.
- **MEDIUM - `MinHeight="44"` acceptance criterion is too prescriptive.** UI-SPEC says "44px effective hit height" which can be padding-based. The strict text match `MinHeight="44"` could fail a perfectly compliant implementation using `Padding="12,10"`. Loosen to "button hit height verified >= 44px" or remove the literal grep.
- **MEDIUM - Status text composition for byte-formatted progress.** UI-SPEC requires `"Restoring 2 / 8 plugins - 38.4 MB / 120.0 MB"`. The plan does not specify a formatter (`ByteSize` library? hand-rolled?). Inconsistent unit display (MB vs MiB, decimal places) is a likely source of UI-checker friction.
- **MEDIUM - `LoadSessionsCommand` disablement during active restore** - currently this command has no gating. If a user clicks Refresh mid-restore, what happens? Plan disables it but doesn't address the existing observable collection mutation race with active CTS.
- **LOW - `RestoreOutcomeTitle` as a single bound string.** The acceptance criterion says AXAML "contains `Restore Complete`, `Restore Partial`, `Restore Failed`, and `Restore Canceled` bindings or templates." If implemented as one TextBlock with `{Binding RestoreOutcomeTitle}`, none of those strings literally appear in AXAML - only in the VM. The acceptance criterion should be relaxed to "VM produces the four exact title strings" verifiable via test.

### Suggestions
- Specify `RestorePlugin_MissingBackupFile_ThrowsFileNotFoundException` removal or rewrite as part of this plan.
- Replace `MinHeight="44"` with an asserted property check or remove the grep entirely.
- Choose and document a byte-formatting helper. Three reasonable options: hand-roll a `FormatBytes(long)` static, use `ByteSize` NuGet, or extract from existing code if any.

---

## Plan 07-05: Progress UI & Final Verification

### Strengths
- Source-level guard against `Task.WhenAll` regression.
- Separate `Cancel Backup` / `Cancel Cleanup` buttons preserve xEdit Stop semantics.
- Final full-solution `dotnet test` gate.

### Concerns
- **HIGH - Acceptance criterion is weak.** "At least one test file contains `SAF-04`, `TEST-04`, or behavior names for restore missing target, partial failure, cleanup deletion failure, and cancellation" is a text grep that doesn't verify coverage. A comment in any test file would pass. Replace with explicit `[Trait]` attributes or named test fixtures asserted via `dotnet test --list-tests`.
- **MEDIUM - Subscription lifecycle.** `ProgressViewModel` adds subscriptions to a new state stream. Plan must ensure the new subscription is added to `_subscriptions` list and disposed in `Dispose()`. Not called out explicitly.
- **MEDIUM - Color-token enforcement.** UI-SPEC mandates `#DAA520` for retention-warning indicators and `#FFF3E0`/`#E65100` for warning banners (matching hang-warning pattern). Plan acceptance criteria don't enforce these. UI-checker may flag this in retroactive review.
- **MEDIUM - Backup operation visibility precedence.** When IsBackupOperationActive is true AND IsHangWarningVisible could be true (xEdit running concurrently). The plan doesn't define z-order or which banner shows. Practically, backup happens before xEdit launch so this overlap shouldn't occur - but defensive UI behavior is unspecified.
- **LOW - `Cancel Backup` and `Cancel Cleanup`** as separate buttons assumes only one is relevant at a time. Confirm via state - backup operation kind drives which button binds.

### Suggestions
- Replace text-grep coverage criterion with: list of required test method names that must exist and pass.
- Add an explicit acceptance criterion that `_subscriptions.Count` increased by N and Dispose path covers them.
- Lock down warning color hex values in acceptance criteria.

---

## Cross-Cutting Concerns

- **Result-type asymmetry.** `BackupPluginAsync : Task<BackupResult>` (legacy single-plugin model) vs `RestorePluginAsync : Task<BackupRestoreResult>` (new structured row+aggregate). Either harmonize in 07-01 or document why the asymmetry is intentional.
- **Retention row status.** `BackupOperationStatus` has 5 values; `BackupRestoreRowStatus` has 3. There is no `BackupRetentionRowStatus` defined - `BackupRetentionRowResult` apparently uses ad-hoc fields. Adding `BackupRetentionRowStatus { Deleted, Kept, Failed }` would parallel the restore design.
- **Logging vs UI separation** is correctly enforced (technical exception text -> log, concise label -> UI). Good across all plans.
- **MO2 mode preservation.** All plans state MO2 backup-skip stays intact; orchestrator integration in 07-03 should add a test asserting `IBackupService.BackupPluginAsync` is *not* called when MO2 mode is active.
- **Performance (PERF-04).** Plans correctly emphasize cancellation and visible progress without parallelization. The 81920-byte buffer in the copier is the .NET default - fine, but progress reports per chunk on a large plugin could spam state updates. Consider throttling progress emission (every 100ms or every N MB) to avoid UI thread saturation.
- **DI ordering.** `IBackupFileCopier` registered in 07-01, `BackupService` constructor changes in 07-02. The DI graph remains acyclic - fine.

---

## Risk Assessment: **MEDIUM**

**Justification:**
- Architecture is sound and faithful to all 20 context decisions.
- Sequential xEdit preservation is multiply-enforced.
- Threat model and security posture are well-considered.

**However:**
- **Three HIGH-severity gaps** (orchestrator CTS design, backup-canceled plugin status mapping, legacy-wrapper exception contract) are integration-seam issues that typically surface only during execution, when fixing them is more expensive.
- The atomic-restore strategy (temp file + move vs direct overwrite) materially affects the SAF-04 invariant. Leaving this to the executor's discretion creates risk that a canceled restore mid-overwrite produces *worse* user-visible behavior than today.
- Several acceptance criteria are text-grep based and won't catch behavioral regressions.

**Recommended pre-execution updates:**
1. Plan 07-01: add `overwrite` parameter to `CopyAsync` contract; specify atomic-temp-move strategy.
2. Plan 07-02: drop legacy sync wrappers (or commit to `.GetAwaiter().GetResult()` with documented UI-thread-safe context).
3. Plan 07-03: explicit CTS design (linked token sources) and explicit `CleaningStatus` mapping for backup-canceled plugins.
4. Plan 07-04: remove `MinHeight="44"` literal grep; specify removal of `RestorePlugin_MissingBackupFile_ThrowsFileNotFoundException`.
5. Plan 07-05: replace text-grep coverage criterion with named-test-method assertions.

With those fixes, risk drops to **LOW**.

---

## Codex Review

## Summary

The five-plan sequence is broadly sound: it decomposes Phase 7 in the right dependency order, starts with contracts and copy primitives, moves into service outcomes, then wires cleaning and restore UI separately before final progress-window integration. The biggest risk is not scope coverage; it is safety precision. The plans need tighter rules around overwrite/atomic restore semantics, retention deletion eligibility, cancellation ownership, and the current repo's actual MVVM/tooling conventions. As written, the phase likely achieves SAF-04/TEST-04/PERF-04, but only if those safety details are clarified before implementation.

## Plan 07-01 Review

### Strengths

- Good first wave: shared result contracts and a copier abstraction unblock later service/UI work.
- Managed async copy is a pragmatic starting point and preserves future `CopyFileExW` swapability.
- Explicit concise failure reasons align with the security/error-boundary direction.
- Tests for cancellation and partial destination deletion are the right early proof.

### Concerns

- **HIGH:** `CopyAsync(source, destination, ...)` does not define overwrite behavior. If restore copies directly over an existing plugin and cancellation/failure deletes the destination, it can delete or corrupt the user's current plugin.
- **HIGH:** Partial-file cleanup must distinguish "new destination file" from "existing target being restored over." Deleting `destinationPath` is safe for backup creation but unsafe for restore overwrite unless using a temp file.
- **MEDIUM:** A single reason `Missing backup file` is wrong for backup creation when the plugin source is missing. The copier may need neutral internal failure codes plus caller-specific display mapping.
- **MEDIUM:** The plan says log technical details but does not explicitly add `ILoggingService` to `BackupFileCopier`.
- **LOW:** Result enum/value naming is reasonable, but `Warning` on the same aggregate enum as restore/copy may invite invalid combinations unless factory methods or helpers constrain use.

### Suggestions

- Add overwrite policy to the contract, or require callers to copy to a temp file and atomically replace only after a successful copy.
- For restore, copy `backup -> target.tmp` in the target directory, then replace/move over the original only after success; delete only the temp file on cancel/failure.
- Use typed failure reasons internally and map to display labels per operation.
- Add tests for "canceled restore does not remove or truncate the existing target file," not just "canceled copy deletes partial destination."

### Risk Assessment

**MEDIUM-HIGH.** The abstraction is the right shape, but overwrite/cancel semantics are a core safety issue.

## Plan 07-02 Review

### Strengths

- Correctly changes restore-all from fail-fast to aggregate recovery.
- Covers missing target directory recreation and concise per-plugin failures.
- Retention rules cover current-session protection, newest-session retention, retry, and cancellation.
- Keeps legacy sync wrappers temporarily, which lowers migration risk.

### Concerns

- **HIGH:** Restore overwrite semantics remain ambiguous. "Replace through a temp file or allowing copier to overwrite" must be resolved in favor of atomic/temp replacement.
- **HIGH:** Retention deletion appears to operate on directories sorted by name. The plan should say whether only valid backup sessions with metadata are eligible; otherwise unrelated directories under the backup root could be deleted.
- **MEDIUM:** "private virtual/testable helper" conflicts with the current `BackupService` being `sealed`; use an injected deletion abstraction or a non-virtual private helper plus filesystem tests.
- **MEDIUM:** Sync wrappers that block on async can deadlock if accidentally called from UI paths. Better to migrate known callers quickly and mark wrappers as compatibility-only.
- **MEDIUM:** Retention "keep newest `MaxSessions` among non-current sessions" should be clarified. If current is excluded before counting, total retained may become `MaxSessions + 1`.

### Suggestions

- Add `IBackupSessionDeleter` or `IFileSystem` only if needed for deterministic retry/failure tests; otherwise use real temp dirs plus read-only/locked files.
- Explicitly filter retention candidates to directories with valid `session.json`, or state that every child directory in the backup root is owned by AutoQAC.
- Add tests for malformed session directory, current session older than retained set, `maxSessionCount <= 0`, and retry preserving failed-delete directories.

### Risk Assessment

**MEDIUM-HIGH.** Behavior coverage is strong, but deletion eligibility and overwrite semantics are too important to leave implicit.

## Plan 07-03 Review

### Strengths

- Correctly keeps backups per-plugin immediately before each xEdit launch.
- Uses state service rather than UI-specific coupling for backup/retention progress.
- Separates non-xEdit cancel behavior from xEdit stop behavior.
- Requires retention outcome before final session completion, which matches D-12/D-13/D-16.

### Concerns

- **HIGH:** Cancellation semantics are underspecified. If a backup is canceled, does the whole cleaning session cancel, or is only that plugin skipped? The plan says "marks that plugin skipped/canceled," but user intent from a visible Cancel Backup button may reasonably mean stop the operation/session.
- **HIGH:** Orchestrator-owned CTS lifecycle needs explicit locking/idempotence so `CancelBackupOperationAsync` cannot cancel a stale operation or race with xEdit execution.
- **MEDIUM:** Extending `CleaningSessionResult` with raw retention result objects may leak UI/service concepts into reporting. A concise summary model may be cleaner.
- **MEDIUM:** Source-level tests for "no `Task.WhenAll`" are brittle. Behavioral ordering tests should be primary.
- **LOW:** State naming `BackupOperationState` includes retention too; consider `FileOperationState` or `MaintenanceOperationState`.

### Suggestions

- Define exact outcomes: backup canceled by user should probably cancel remaining cleaning unless requirements explicitly say "skip only current plugin and continue."
- Store operation CTS under a narrow lock, clear it in `finally`, and make cancel commands no-op after completion.
- Add tests proving `CancelBackupOperationAsync` during xEdit does not stop/kill xEdit.
- Prefer behavior tests that record call order: `BackupPluginAsync(pluginA)` before `CleanPluginAsync(pluginA)`, never overlapping plugin B.

### Risk Assessment

**MEDIUM.** Good integration plan, with manageable risk if cancellation lifecycle is nailed down.

## Plan 07-04 Review

### Strengths

- Directly addresses the restore UX safety decisions.
- Keeps success/partial/failure results inline instead of modal-only.
- Disables destructive/session-changing commands while restore is active.
- Adds owned CTS disposal, which is necessary for a long-running restore UI.

### Concerns

- **MEDIUM:** Confirmation copy in the plan may be too exact-string oriented. Tests should verify required content, not fragile punctuation/formatting unless UI-SPEC truly requires exact text.
- **MEDIUM:** Generated command `CanExecute` updates must include `IsRestoreActive` notifications for every affected command, or controls may stay enabled.
- **MEDIUM:** `DeleteSessionAsync` remains synchronous and potentially long-running. It may be out of Phase 7 retention scope, but the restore window will still contain a blocking deletion path.
- **LOW:** `BackupRestoreRowResult` as a model bound directly in AXAML is fine, but UI may need display-specific properties for accessible labels and concise reason text.

### Suggestions

- Add tests for "successful restore does not call `ShowErrorAsync` or success popup," and "partial restore keeps window open."
- Add a test that cancel disposes/clears `_restoreCts` after completion and allows a later restore.
- Consider making delete-session async/cancellable later, or explicitly record it as out of Phase 7 if not touched.

### Risk Assessment

**MEDIUM.** The UX path is well scoped; main risk is command-state correctness and avoiding fragile text tests.

## Plan 07-05 Review

### Strengths

- Finishes the missing user-facing cleaning progress surface.
- Keeps Cancel Backup/Cancel Cleanup separate from xEdit Stop, which is a key phase requirement.
- Includes full solution verification after targeted tests.
- Avoids adding Avalonia.Headless dependencies, matching the current project note.

### Concerns

- **MEDIUM:** The plan depends on Plan 07-03 choosing a clean orchestrator cancellation API. If that API is vague, this UI plan inherits the ambiguity.
- **MEDIUM:** Progress-window tests should verify command routing and state transitions, not only bound text properties.
- **LOW:** Warning/canceled visual treatment is mentioned but not testable without either view-level assertions or a very clear binding contract.
- **LOW:** Final validation should account for Windows/Avalonia file-lock flakiness by running tests sequentially, not parallelizing filtered test commands.

### Suggestions

- Add tests for Cancel Backup and Cancel Cleanup invoking the orchestrator method exactly once and becoming unavailable after state clears.
- Add a final checklist mapping each D-01 through D-20 decision to a test or implementation point.
- Include `dotnet build AutoQACSharp.slnx` before full `dotnet test` if CI/build warnings matter for the phase gate.

### Risk Assessment

**MEDIUM.** Mostly integration risk; the plan is viable once Plan 07-03's API is concrete.

## Overall Risk

**MEDIUM-HIGH.** The roadmap decomposition is good and should achieve the phase goals, but two safety details must be fixed before execution: restore must never copy directly over the live plugin in a way that cancellation/failure can delete or truncate it, and retention cleanup must precisely define which directories are eligible for deletion. Clarifying those points would reduce the overall phase risk to **MEDIUM**.

---

## Consensus Summary

The reviewers broadly agree that Phase 7 is decomposed in the right order and targets the correct safety, testability, and UX outcomes. The main actionable review feedback is to tighten the plans before execution around restore overwrite safety, cancellation ownership, retention deletion eligibility, and behavior-based acceptance criteria.

### Agreed Strengths

- The wave order is strong: contracts and copy primitives first, service behavior second, then orchestrator/UI integration.
- Structured restore/retention outcomes are the right architecture for `SAF-04`, `TEST-04`, and `PERF-04`.
- The plans preserve sequential xEdit execution and avoid parallel cleaning.
- Inline restore results and concise user-facing failure reasons align with the project’s UX and diagnostics boundaries.
- The planned test coverage targets the right backup, restore, retention, and cancellation risk clusters.

### Agreed Concerns

- Restore overwrite semantics need to be explicit before implementation. Claude and Codex both flag that direct overwrite plus partial-file deletion can corrupt or delete the current plugin; the plan should mandate temp-file copy plus atomic replace and test canceled restore preserving the original target.
- Backup/restore/copy failure reasons need clearer caller-specific mapping. Multiple reviewers noted that `Missing backup file` is not a good generic source-missing reason for backup creation.
- Orchestrator cancellation needs a concrete CTS design and status mapping. Canceling backup/cleanup must not accidentally trigger xEdit stop behavior or race with stale operation tokens.
- Retention cleanup needs sharper rules for retry behavior and deletion eligibility. Reviewers called out retry delay/cancellation handling, valid session filtering, `MaxSessions` semantics, and current-session protection edge cases.
- Several acceptance criteria are too text-grep oriented. Reviewers recommend named behavioral tests and call-order assertions instead of checking that files contain keywords.
- Progress and UI integration should account for progress throttling, command state updates, subscription disposal, and layout density.

### Divergent Views

- Gemini assessed overall risk as `LOW`, while Claude assessed `MEDIUM` and Codex assessed `MEDIUM-HIGH`. The difference is mostly because Gemini treated the partial-file cleanup design as sufficient, while Claude and Codex identified restore-overwrite cancellation as a core safety gap.
- Claude recommends either removing legacy synchronous restore wrappers or explicitly handling their changed exception contract and UI-thread deadlock risk. Codex also flags sync-wrapper blocking, while Gemini did not mention it.
- Gemini focused on retention retry timing and UI density. Claude and Codex raised deeper contract/testability issues around retention deletion eligibility, retry cancellation, and valid session filtering.
