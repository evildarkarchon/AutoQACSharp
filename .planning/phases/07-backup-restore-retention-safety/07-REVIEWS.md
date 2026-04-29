---
phase: 07
reviewers: [gemini, claude, codex]
reviewed_at: 2026-04-29T02:43:41.9534919-07:00
plans_reviewed: [07-01-PLAN.md, 07-02-PLAN.md, 07-03-PLAN.md, 07-04-PLAN.md, 07-05-PLAN.md, 07-06-PLAN.md, 07-07-PLAN.md, 07-08-PLAN.md, 07-09-PLAN.md, 07-10-PLAN.md, 07-11-PLAN.md]
---

# Cross-AI Plan Review - Phase 7

## Gemini Review

# Implementation Plan Review: Phase 7 (Backup Restore & Retention Safety)

## Summary
Phase 7 is a comprehensive hardening of the backup and recovery subsystem. It successfully transitions the application from basic file operations to a robust, failure-aware service layer with structured outcomes and cancellable async operations. The pending plans (07-10 and 07-11) are surgically targeted to close high-severity safety gaps identified during verification: the accidental deletion of existing backups and the potential for "out-of-bounds" file overwrites via tampered metadata. The use of a "trusted restore root" (the game Data folder) as a validation anchor is an excellent application of the Principle of Least Privilege.

## Strengths
*   **Verification-Driven Closure:** Plans 07-10 and 07-11 directly address the "failed truths" from the `07-VERIFICATION.md` report, ensuring that the phase doesn't consider itself complete while safety blockers remain.
*   **Ownership Tracking (07-10):** Introducing a `createdOutput` flag in the file copier is a low-overhead, effective way to prevent the "IOException cleanup" path from deleting files it didn't actually create.
*   **Trusted Root Validation (07-11):** Explicitly passing the configured Data folder as a `trustedRestoreRoot` provides a hard security boundary that prevents `session.json` metadata from ever redirecting a restore to sensitive system directories.
*   **TDD Discipline:** The continuation of RED/GREEN task cycles for these gap closures maintains high confidence in the fix and prevents regression of the prior 700+ passing tests.
*   **Atomic Move Semantics:** The use of `ReplaceAtomically` (temp file then move) across the phase is a critical design choice that prevents plugin corruption during partial failures or cancellations.

## Concerns
*   **Fixed Temp Suffix Collision (MEDIUM):** Plan 07-10 and earlier plans use a fixed `.autoqac-tmp` suffix. While safe for sequential operations, a stale temp file from a crashed session could technically block a future restore if `FileMode.Create` isn't used for the temp file itself.
    *   *Severity:* **Medium**. It could cause a "Target write failed" error for a user whose previous session was force-killed.
*   **Sync/Async Validation Duplication (LOW):** Plan 07-11 correctly identifies the need to update both sync and async restore paths. There is a small risk that logic drift could occur between `RestorePlugin` and `RestorePluginAsync` if the `IsRestoreTargetInsideTrustedRoot` helper isn't strictly shared.
    *   *Severity:* **Low**. The plan mentions sharing the helper, which mitigates this.
*   **Path Normalization Edge Cases (LOW):** While `Path.GetFullPath` is used, Windows-specific paths (UNC, long paths, case-insensitivity differences in environment variables) can occasionally produce false negatives in "starts with" containment checks.
    *   *Severity:* **Low**. For a desktop app cleaning local plugins, the current approach is standard and sufficient.

## Suggestions
*   **Unique Temp Names:** Consider using a GUID or timestamp in the temp restore file name (e.g., `filename.esp.autoqac-{guid}.tmp`) to avoid collisions with stale files from previous failed runs.
*   **Centralized Metadata Policy:** Ensure that the `IsSafeRestoreTarget` and `IsRestoreTargetInsideTrustedRoot` helpers are the *only* paths through which a file write is authorized.
*   **Explicit Exception Mapping:** For Plan 07-11, ensure that when a restore is rejected because it's outside the trusted root, the log explicitly states *why* (security policy) even if the UI only shows "Target folder creation failed."

## Risk Assessment
**Overall Risk: LOW**

The phase is in its final "gap closure" stage. The architecture is already proven through Plans 01-09 with a high volume of passing tests. Plans 07-10 and 07-11 are safety-positive changes that reduce the risk profile of the application. By the end of these two plans, AutoQAC will have one of the most resilient and secure backup systems in the xEdit ecosystem. No xEdit parallelization risk is introduced, and sequential integrity is maintained.

---
*Phase 07 Review - 2026-04-29*

---

## the agent Review

# Phase 7 Plan Review: Backup Restore & Retention Safety

## Scope Note

Plans 07-01 through 07-09 have already shipped (commits visible in git log; SUMMARY.md files present; `gaps_found: 16/18 truths verified`). 07-REVIEWS.md captures detailed pre-execution review of those plans. **This review focuses on the two pending Wave 8 plans (07-10 and 07-11)** that close the remaining verified blockers, with brief notes on plan-set cohesion.

---

## Summary

Plans 07-10 and 07-11 are tightly scoped, TDD-gated gap closures targeting exactly the two FAILED truths in `07-VERIFICATION.md` (Truth #18 create-new ownership, Truth #11 trusted restore root). Both have correct technical direction, but **07-11 understates the blast radius** of adding `trustedRestoreRoot` to async and legacy sync `IBackupService` restore signatures — a wave of existing 07-09 tests and the legacy sync test row will silently regress unless explicitly enumerated and updated. **07-10's ownership-flag mechanism is underspecified** (how `CopyFileContentsAsync` reports stream-open success back to `CopyAsync` is left to the executor).

---

## Strengths

- **Surgical scope.** Each plan touches one verified gap; no scope creep beyond the failed truths.
- **TDD discipline preserved.** Both plans split RED tests into a separate task before GREEN production changes, matching the pattern that drove 07-06 -> 07-09 to clean convergence.
- **07-10 preserves prior cancellation invariants explicitly.** Acceptance criteria require `CopyAsync_CanceledCreateNewCopy_DeletesPartialDestination` and `CopyAsync_CanceledAtomicReplace_PreservesExistingTarget` to keep passing, which catches over-correction.
- **07-11 grounds trusted-root validation in existing config flow.** `MainWindow.axaml.cs` already passes `Configuration.GameDataFolder` into `LoadSessionsAsync`; plumbing it through to the service is a small, well-scoped change.
- **Both plans confirm the no-parallel-xEdit invariant** as a secondary source check rather than primary gate, matching 07-09 conventions.
- **07-11 fails closed on null/empty/invalid trusted root** rather than silently allowing legacy behavior — correct security posture for a metadata-trust boundary.

---

## Concerns

### HIGH

- **C1 — 07-11 has unenumerated breaking-change blast radius.** Adding `string? trustedRestoreRoot` "before progress/cancellation parameters" changes async signatures and the legacy sync `RestorePlugin`/`RestoreSession` signatures. Plan 07-09 added these tests that all call the old signatures:
  - `RestoreSessionAsync_ContinuesAfterPluginFailure`
  - `RestoreSessionAsync_FailedThenCanceledRows_ReturnsCanceled`
  - `RestorePluginAsync_MissingTargetDirectory_RecreatesDirectory`
  - `RestorePluginAsync_CanceledAtomicRestore_PreservesExistingTarget`
  - `RestorePluginAsync_TargetFolderCreationFailure_ReturnsFailedRow`
  - `RestorePluginAsync_FileNameTraversal_ReturnsMissingBackupFileAndDoesNotCopy`
  - `RestorePluginAsync_AbsoluteFileName_ReturnsMissingBackupFileAndDoesNotCopy`
  - `RestorePluginAsync_UnrootedOriginalPath_ReturnsTargetFolderCreationFailed`
  - `RestorePluginAsync_OriginalPathFileNameMismatch_ReturnsTargetFolderCreationFailedAndDoesNotCopy`
  - `RestorePluginAsync_NonPluginOriginalPathExtension_ReturnsTargetFolderCreationFailedAndDoesNotCopy`
  - `RestorePluginAsync_AccessDeniedCopyFailure_ReturnsAccessDenied`
  - `RestorePluginAsync_NormalPluginPath_StillRestoresSuccessfully` (already noted in plan)
  - `RestorePlugin_FileNameMismatch_ThrowsBeforeCopying`
  - `RestorePlugin_MissingBackupFile_WithSafeMetadata_StillThrowsFileNotFoundException`

  These either need a new trusted-root argument or will start returning `TargetFolderCreationFailed` when validation fails closed, masking what the test is actually exercising. The plan's RED task only mentions updating `RestorePluginAsync_NormalPluginPath_StillRestoresSuccessfully`. The execute step will hit a wall unless the GREEN task also updates every other test call site in the same commit. **Add an explicit "test call-site update" subtask listing every method that calls `RestorePluginAsync`/`RestoreSessionAsync`/`RestorePlugin`/`RestoreSession` in `BackupServiceTests` and what trusted root each passes.**

- **C2 — 07-11 doesn't define behavior when `Configuration.GameDataFolder` is null/empty.** `RestoreViewModel.LoadSessionsAsync(string? dataFolderPath)` already accepts null (it shows `"No game data folder configured -- cannot locate backups"`). But once the field becomes the trusted root, *every* restore in that state fails closed with `TargetFolderCreationFailed`. The user-facing experience is that they'd see the session list (sessions can still load if they exist on disk) but every restore fails with the same opaque label. Plan should either: (a) gate restore commands on `_trustedRestoreRoot` being non-null/valid via `[NotifyCanExecuteChangedFor]`, or (b) extend the empty-state copy to explain why restores are disabled.

### MEDIUM

- **C3 — 07-10's `createdOutput` plumbing is underspecified.** The plan says "have `CopyFileContentsAsync` report when the destination stream has opened successfully" without specifying the mechanism. Choices: (a) `out bool createdOutput` parameter, (b) `Action<bool>` callback like `updateCopiedBytes`, (c) refactor to return a tuple, (d) lift the `FileStream` open out of `CopyFileContentsAsync` and back into `CopyAsync`. Each has implications:
  - (a)/(b) require setting the flag inside `CopyFileContentsAsync` *immediately after* the destination `FileStream` constructor returns. If the flag is set on the wrong line (e.g., before `var destination = new FileStream(...)`), the bug returns inverted.
  - (d) is cleanest but the bigger refactor.
  
  **Recommend specifying option (b) `Action<bool>` callback or option (d) lifted-open** explicitly so RED -> GREEN doesn't spawn ambiguity at execution time.

- **C4 — 07-10 creates a subtle window where `FileMode.Create` (atomic restore) might mis-flag ownership.** `FileMode.Create` always creates/truncates, so `createdOutput` should always be true after open succeeds for `ReplaceAtomically` flow. The plan implicitly assumes the flag flips after open for both modes, but if the implementer reads the verification report literally ("delete only the partial file created by the active copy attempt") they might add a mode-specific branch and break atomic-restore temp-file cleanup. **Add an explicit invariant: "for `ReplaceAtomically`, the temp file is owned by this attempt as soon as the destination FileStream successfully opens; cleanup must still delete it."**

- **C5 — 07-11's containment check uses prefix-match, which doesn't follow NTFS reparse points or symlinks.** The threat model row T-07-11-03 acknowledges this is mitigated only at the string level, but the plan inherits T-07-09-05's "accept" disposition for reparse-point escape. Worth restating that `IsRestoreTargetInsideTrustedRoot` is a string-level check, not a filesystem-identity check, in the helper's XML doc so future readers don't assume otherwise.

- **C6 — 07-11 changes legacy sync method signatures without auditing UI/production callers.** 07-09 Task 2 audited remaining `RestorePlugin(` callers and concluded only `BackupService` and `BackupServiceTests` use them. That audit needs a refresh: Plan 07-11 inserts a new required parameter into `RestorePlugin`/`RestoreSession`. If any production code path still calls these (the audit was 24 hours ago), they'll break compilation. **Re-run the audit (`Select-String -Path AutoQAC/**/*.cs,AutoQAC.Tests/**/*.cs -Pattern 'RestorePlugin\(|RestoreSession\('`) before the GREEN task and document the result in the plan.**

- **C7 — `BackupCopyOptions.CreateNewBackup` may not exist on `BackupCopyOptions` exactly as plan-10 describes.** The plan references `BackupCopyOptions.CreateNewBackup` as if it were a static instance, but `BackupCopyOptions` (07-01) was described as a struct/record with a `BackupCopyExistingTargetPolicy ExistingTargetPolicy` property. Verify whether `CreateNewBackup` is a static factory/preset on the type or shorthand for `new BackupCopyOptions(BackupCopyExistingTargetPolicy.FailIfExists)`. Existing tests use `BackupCopyOptions.CreateNewBackup` (per `BackupFileCopierTests.cs:55`), so the convention exists; just confirm during implementation.

### LOW

- **C8 — 07-10's RED test asserts `FailureReason == BackupFailureReason.TargetWriteFailed`** for the existing-destination case. Today's behavior throws `IOException` from `FileMode.CreateNew`, which the catch maps to `TargetWriteFailed` (line 84-88 of `BackupFileCopier.cs`). The fix doesn't change that mapping — it just stops the cleanup. So the assertion is correct, but the failure-reason name is slightly misleading: the destination wasn't *unwritable*, it was *occupied*. Consider whether the verified-truth requires a new reason like `DestinationAlreadyExists`. Not blocking, but worth a follow-up note.

- **C9 — 07-11 doesn't address whether retention-cleanup also needs trusted-root scoping.** Backup retention deletes session directories under `backupRoot` (a sibling of Data, not Data itself). The trusted-root design here is restore-only. That's correct for this gap, but the plan should state it explicitly to avoid scope confusion later.

- **C10 — Plan 07-11's positive regression test name is reused from 07-09** (`RestorePluginAsync_NormalPluginPath_StillRestoresSuccessfully`). The plan implies "update" — clarify whether it's a signature update or a new test to avoid a stale duplicate.

- **C11 — `UI hint: yes` on the roadmap entry for Phase 7.** Plan 07-11 plumbs a trusted root through the ViewModel but doesn't surface the UX implication of C2 (disabled restore commands when no game configured). If the team wants a user-visible affordance, that's a follow-up task; if not, document the deliberate "fail-closed silently" choice in `07-11-SUMMARY.md`.

---

## Suggestions

1. **For 07-11 RED task:** Before writing tests, run `Grep -n "RestorePluginAsync\|RestoreSessionAsync\|RestorePlugin(\|RestoreSession(" AutoQAC.Tests/Services/BackupServiceTests.cs` and append the resulting list of call sites to the plan as the explicit "tests-to-update" inventory. The GREEN task can then mechanically add `trustedRestoreRoot` to each.

2. **For 07-11 ViewModel behavior:** Add a fourth task or extend Task 2 to update `CanRestorePlugin`/`CanRestoreAll` predicates to include `!string.IsNullOrEmpty(_trustedRestoreRoot)`, and add a ViewModel test `RestoreCommands_DisabledWhenTrustedRestoreRootMissing`. This prevents the silent-failure UX from C2 and makes the intent auditable.

3. **For 07-10 ownership plumbing:** Pick the mechanism explicitly. Recommended: add an `Action<bool>? onDestinationOpened` parameter to `CopyFileContentsAsync` (parallel to `updateCopiedBytes`), and have `CopyAsync` capture `var createdOutput = false; ...onDestinationOpened: opened => createdOutput = opened, ...`. Pass `createdOutput` to every `DeletePartialOutput(...)` call.

4. **For 07-10 atomic-restore symmetry:** Add a brief test `CopyAsync_CanceledAtomicReplace_DeletesTempFile_RegardlessOfOwnershipFlag` to lock in that the ownership refactor doesn't accidentally break `.autoqac-tmp` cleanup. Even though existing tests cover the canceled case, a refactor-aimed test makes the invariant explicit.

5. **For 07-11 trusted-root helper:** In `IsRestoreTargetInsideTrustedRoot`, normalize the trusted root with `Path.GetFullPath` *and* `EnsureTrailingDirectorySeparator` (the helper already exists in `BackupService.cs:653`). Reuse the existing helper to avoid divergent normalization logic.

6. **For 07-11 caller audit:** Add a one-line acceptance criterion: "Pre-execution audit confirms no production code path calls legacy `RestorePlugin(BackupPluginEntry, string)` or `RestoreSession(BackupSession)` outside `BackupService.cs` and `BackupServiceTests.cs`. Result documented in `07-11-SUMMARY.md`."

7. **Both plans:** After GREEN, run `dotnet test AutoQACSharp.slnx` *before* declaring victory. Both plans include this in the verification block, but neither lists it as an `<acceptance_criteria>` item for the final task. Make it a hard acceptance criterion.

---

## Risk Assessment

**Overall: MEDIUM** for 07-11; **LOW-MEDIUM** for 07-10.

Justification:
- **07-10** is small, well-targeted, and has clear failure modes. The ownership-plumbing ambiguity (C3) and atomic-restore invariant (C4) can be resolved during implementation without redesigning the plan. Risk is bounded by test-driven gates.
- **07-11** is small in production code but has wide test-call-site impact (C1). If the executor implements GREEN by changing the interface and only updating the one positive test the plan names, 13+ existing tests will start failing in confusing ways and the executor may regress safety to make tests pass. Adding the C2 fail-closed UX behavior is a separate UX decision the plan doesn't resolve. The blast-radius enumeration *should* happen during the RED task naturally, but it's not codified in acceptance criteria.
- The Phase 7 architecture (managed copy, atomic temp+move, structured outcomes, separate non-xEdit CTS, sequential xEdit guarantee) has been validated through nine prior plans and 742 + 59 passing tests. The remaining two gaps don't touch architectural decisions; they're localized hardening. Reverse-risk is low.
- Verification report's "Required Artifacts" table marks `BackupService.cs` PARTIAL and `BackupFileCopier.cs` PARTIAL — exactly what these two plans target. After execution, both should flip to VERIFIED, closing Phase 7.

**Bottom line:** Both plans are technically sound and proceed-ready. Strengthen 07-11's test-call-site enumeration (C1) and resolve the null-trusted-root UX question (C2) before execution; specify 07-10's ownership-flag mechanism (C3) explicitly. With those tightened, risk drops to LOW for both.

---

## Codex Review

## Summary

The Phase 7 plan set is strong overall: it is staged well, keeps xEdit cleaning sequential, moves risky filesystem operations toward structured async outcomes, and adds meaningful regression coverage for restore, backup copy, cancellation, retention, and UI reporting. The later gap-closure plans are especially valuable because they tighten unsafe path handling and address subtle cancellation/result aggregation bugs. Main risks are API churn across plans, restore-root contract timing, Windows filesystem edge cases, and some over-specific source/text assertions that could make implementation brittle.

## Strengths

- Clear dependency flow: contracts/copier first, service behavior next, orchestrator/UI integration after.
- Good preservation of core invariant: per-plugin backup before xEdit, no parallel xEdit launches.
- Strong safety posture around partial file cleanup, restore target preservation, retention warning/cancel outcomes, and concise UI reasons.
- Tests are behavior-focused in many critical places, especially backup cancellation, restore partial failure, retention current-session protection, and mixed canceled/failed restore outcomes.
- Gap-closure plans are justified by concrete verification findings instead of speculative refactors.
- Restore UI decisions align with phase goals: confirmation before overwrite, inline results, no success popups, visible cancellation.

## Concerns

- **HIGH:** 07-11 changes restore service signatures after 07-04/07-09 already migrated UI and tests. This is valid as a gap closure, but it creates broad churn and potential missed callers. The plan should require a compile-driven audit of all `RestorePluginAsync`, `RestoreSessionAsync`, `RestorePlugin`, and `RestoreSession` call sites.

- **HIGH:** Trusted restore-root validation in 07-11 uses string prefix containment. It must normalize trailing separators carefully so `C:\Game\Data2` does not pass for `C:\Game\Data`. The plan mentions trailing separator, but this should be an explicit tested case.

- **HIGH:** Restore target policy may reject legitimate edge cases such as ghosted plugins, case/normalization oddities, or managed paths under virtualized setups. The plan acknowledges `.esp.ghost` as deferred, but the user-visible impact should be called out in summary/release notes.

- **MEDIUM:** `destinationPath + ".autoqac-tmp"` can collide with an existing temp file or another interrupted attempt. This is deferred in 07-09, but restore overwrite safety would be stronger with a unique temp name in the same directory.

- **MEDIUM:** Some acceptance criteria depend on source-string checks like exact method text, color values, or absence of `Task.Run`. These are useful secondary guards, but brittle as primary acceptance criteria.

- **MEDIUM:** 07-01/07-02 keep legacy sync wrappers while introducing async APIs. The plans say not to block on async, which is good, but duplicated sync/async restore validation creates drift risk until 07-09/07-11 consolidate validation.

- **MEDIUM:** Retention cleanup classification based only on “valid backup metadata” is safer than deleting unknown directories, but malformed session directories accumulating forever may surprise users. Reporting skipped rows helps, but there may need to be a later explicit cleanup path.

- **LOW:** The project context says Avalonia 11.3/ReactiveUI, while repo instructions say Avalonia 12/CommunityToolkit MVVM. Plans should follow the actual repo, but this mismatch is worth correcting in planning docs.

- **LOW:** UI-specific requirements such as exact colors and 44px hit height are reasonable, but without a headless UI project they are mostly source assertions, not runtime UI verification.

## Suggestions

- Add explicit path-containment tests for sibling-prefix attacks, for example trusted root `C:\Game\Data` and target `C:\Game\Data2\Plugin.esp`.

- In 07-11, require `dotnet build` immediately after signature changes before targeted tests, because interface changes will surface missed call sites faster.

- Prefer a shared helper for containment checks, such as `NormalizeDirectoryRootForContainment`, used by both session backup source containment and trusted restore-root containment.

- Replace `destination + ".autoqac-tmp"` with a unique same-directory temp file, for example `.{filename}.{Guid}.autoqac-tmp`, while still deleting only owned temp files.

- Keep source scans as secondary verification only. Primary acceptance should be named behavioral tests and full solution test results.

- Add a small compatibility note in 07-09/07-11 summaries explaining rejected restore metadata cases: UNC paths, device paths, ghosted plugin extensions, non-plugin extensions, filename mismatches, and targets outside current Data folder.

- Make `BackupFailureReason.SourceMissing` explicitly non-displayable or mapped per caller so it cannot accidentally surface as UI text outside the approved labels.

- Add one test proving null/empty `trustedRestoreRoot` fails closed for restore operations.

## Risk Assessment

**Overall risk: MEDIUM.**

The plans address the phase goals well and cover many important edge cases, but they touch core backup/restore contracts, cleaning orchestration, app state, and UI surfaces. The highest risks are filesystem path validation correctness, legacy sync/async behavior drift, and late API changes in 07-11. With the suggested containment tests, compile-driven call-site audit, and unique restore temp names, the remaining risk becomes manageable.

---

## Consensus Summary

All reviewers agree that Phase 7 is fundamentally well structured and that plans `07-10` and `07-11` are the right remaining gap closures. The strongest shared feedback is not to change the strategic direction, but to tighten execution details before implementation: enumerate the `07-11` restore-signature blast radius, make trusted-root containment tests more explicit, and remove ambiguity from `07-10` ownership tracking.

### Agreed Strengths

- `07-10` and `07-11` are verification-driven and map directly to the remaining failed truths.
- The phase preserves the hard sequential xEdit invariant while making backup/restore/retention file work cancellable and visible.
- The structured result model, atomic restore semantics, and concise user-facing reason labels are strong architectural choices.
- The trusted restore root in `07-11` is the correct boundary for preventing tampered backup metadata from overwriting arbitrary files.
- The TDD flow and named regression tests make the remaining changes safer than ad-hoc patching.

### Agreed Concerns

- **HIGH: `07-11` restore-signature blast radius.** Claude and Codex both flagged broad churn across existing service, ViewModel, and test call sites. Before execution, inventory every `RestorePluginAsync`, `RestoreSessionAsync`, `RestorePlugin`, and `RestoreSession` caller and define the trusted root each one should pass.
- **HIGH/MEDIUM: trusted-root containment edge cases.** Codex explicitly called out sibling-prefix attacks like `C:\Game\Data2`; Claude and Gemini both noted Windows path normalization limits. Add behavior tests for trailing-separator normalization and document that reparse-point/symlink handling is string-level only unless expanded later.
- **MEDIUM: fixed `.autoqac-tmp` collision risk.** Gemini and Codex suggested unique same-directory temp names. This is not required to close the verified `07-10` ownership gap, but it is the main residual restore-copy robustness issue.
- **MEDIUM: `07-10` ownership plumbing ambiguity.** Claude asked for a concrete mechanism for setting `createdOutput` exactly after destination stream open, plus an explicit invariant that atomic-restore temp files remain owned and deleted on cancel/failure.
- **MEDIUM: sync/async validation drift.** Gemini, Claude, and Codex all called out the need for shared validation helpers and refreshed sync-caller audits so legacy restore paths do not diverge from async safety.
- **MEDIUM: null or empty trusted restore root UX.** Claude and Codex both flagged fail-closed behavior when `Configuration.GameDataFolder` is missing. Decide whether restore commands should be disabled or whether inline failure copy is sufficient, then test that path.

### Divergent Views

- Gemini rates overall risk as **LOW**, while Claude and Codex rate `07-11`/the full set as **MEDIUM** because of interface churn and path-validation edge cases.
- Unique temp file names are presented by Gemini and Codex as a concrete improvement; Claude treats ownership tracking plus atomic temp cleanup as the immediate requirement and does not require unique names to proceed.
- Codex treats ghosted plugin rejection as a higher user-impact concern; Claude frames it as a summary/release-note obligation; Gemini does not emphasize it.

### Recommended Planning Feedback

- Add a `07-11` call-site audit task or acceptance criterion before the GREEN implementation task.
- Add explicit tests for trusted-root sibling-prefix containment and null/empty trusted-root behavior.
- Specify `07-10` ownership tracking mechanics, including when `createdOutput` flips and how `ReplaceAtomically` temp cleanup remains intact.
- Keep source/text scans as secondary invariants; require `dotnet build` after signature changes and `dotnet test AutoQACSharp.slnx` before completion.
- Document residual limitations in `07-10-SUMMARY.md` / `07-11-SUMMARY.md`: fixed temp suffix or unique-temp decision, ghosted plugin exclusion, string-level containment vs reparse points, and malformed retention-session cleanup as future work.
