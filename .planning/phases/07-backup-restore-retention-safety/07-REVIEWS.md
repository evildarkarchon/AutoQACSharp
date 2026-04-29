---
phase: 07
reviewers: [gemini, claude, codex]
reviewed_at: 2026-04-29T01:44:10.0592363-07:00
plans_reviewed: [07-01-PLAN.md, 07-02-PLAN.md, 07-03-PLAN.md, 07-04-PLAN.md, 07-05-PLAN.md, 07-06-PLAN.md, 07-07-PLAN.md, 07-08-PLAN.md, 07-09-PLAN.md]
---

# Cross-AI Plan Review — Phase 7

## Gemini Review

# Implementation Plan Review: Phase 7 (Backup Restore & Retention Safety)

## Summary
The plan set for Phase 7 is exceptionally thorough, evolving from a foundational service-layer refactor (07-01, 07-02) to UI/UX integration (07-04, 07-05) and culminating in a rigorous series of "Gap Closure" plans (07-06 through 07-09). The strategy of using a managed `IBackupFileCopier` abstraction with atomic replacement semantics (`.autoqac-tmp`) is a high-water mark for safety in this project. By treating session metadata as untrusted input and enforcing path containment (Plan 09), the phase successfully transitions AutoQAC from a "happy path" backup tool to a resilient, production-grade recovery system.

## Strengths
*   **Atomic Restore Logic:** The decision to copy to a temporary file and `File.Move` only upon success (Plan 01/02) ensures that a failed or canceled restore never leaves a plugin in a corrupted/zero-byte state.
*   **Structured Outcome Modeling:** Moving away from thrown exceptions toward a rich `BackupOperationStatus` (Complete, Partial, Failed, Canceled, Warning) allows the UI to provide nuanced feedback (e.g., "Partial Restore") without crashing or losing context.
*   **Sequential Integrity:** The plans explicitly guard against parallelizing xEdit launches, maintaining the core constraint of the application while still allowing "background" file work.
*   **Honest Verification:** The inclusion of Plans 07-06 through 07-09 demonstrates a mature engineering lifecycle where verification gaps (like path traversal or UI-thread marshalling) are identified and systematically closed rather than ignored.
*   **UX Accessibility:** Adhering to a 44px hit height for Cancel buttons and using the multiples-of-4 spacing scale ensures a professional, touch-friendly Windows desktop experience.

## Concerns
*   **Legacy Sync/Async Coexistence (MEDIUM):** Plans 01 and 02 keep synchronous methods for "compile compatibility." While necessary for a staged migration, there is a risk that legacy code paths (like session deletion or sync restore) might bypass the new safety checks (traversal/containment) implemented in the async versions.
    *   *Severity:* **Medium**. Plan 09 partially addresses this by applying safety to legacy `RestorePlugin`.
*   **Retention Deletion Eligibility (LOW):** Plan 02 filters candidates by "readable session metadata." If a user manually places a subfolder in the backup root, it is reported as "skipped/kept." This is safe, but in a large backup root, it could create UI "noise" in the progress band.
    *   *Severity:* **Low**. This is a UX preference rather than a safety bug.
*   **Path Comparison Fragility (LOW):** Using `Path.GetFullPath` for containment checks is the standard approach, but case-sensitivity/trailing slashes on Windows can sometimes produce false negatives.
    *   *Severity:* **Low**. Plan 09 includes an `EnsureTrailingDirectorySeparator` helper which mitigates the "sibling directory" containment exploit.

## Suggestions
*   **Consolidate Safety Gated Helpers:** Ensure that the `ValidateRestoreEntry` and `ValidateBackupDestination` logic from Plan 09 is shared between the Sync and Async methods to prevent "divergent safety" where one path is hardened and the other remains exploitable.
*   **Session Metadata Redundancy:** Consider adding a checksum or basic file-hash to `BackupPluginEntry` in a future phase. While Phase 7 focuses on "Restore Safety," the system currently trusts that the file in the backup folder hasn't been tampered with or corrupted on disk.
*   **Cancellation Responsiveness:** In the `BackupFileCopier` (Plan 01), ensure the `81920-byte` buffer loop checks `ct.IsCancellationRequested` at every iteration. For very large plugins (1GB+), a single `FileStream.CopyToAsync` call without a manual loop might not be responsive enough if the underlying OS buffer is large.

## Risk Assessment
**Overall Risk: LOW**

The phase is well-structured and places safety (atomic writes, path validation) at the forefront. The initial complexity of the async refactor is managed through localized task waves. The most significant risks (path traversal and arbitrary overwrite) are handled in the final gap-closure plan (07-09), which effectively "locks the door" on the new implementation. As long as the full solution verification in Plan 09 is treated as a hard gate, the phase achieves its goals with high confidence.

---

## the agent Review

# Plan Review: Phase 7 — Backup Restore & Retention Safety

## Summary

Plans 07-01 through 07-08 form a cohesive, TDD-driven progression that converted a synchronous, throw-on-failure backup/restore/retention surface into a structured, cancellable, dispatcher-safe pipeline. Plans 06–08 already shipped, and the verification report shows 14/17 truths verified with three remaining filesystem-safety / cancellation-classification gaps. **Plan 07-09 is the only pending plan** and is the right scope: it closes exactly the gaps verification flagged. The plan series is overall well-organized and each plan has clear acceptance criteria, but 07-09 has tightenable spots around path-safety primitives and the breaking-change risk introduced by the aggregate-status change and the legacy `RestorePlugin` exception swap.

## Strengths

- **Correct scope on 07-09.** Three tasks map 1:1 to the three failed truths in `07-VERIFICATION.md` (T17 backup containment, T10 restore target policy, T2 mixed cancellation aggregate). No scope creep.
- **TDD discipline is consistent across all nine plans.** Each task has a RED-test list and a GREEN action list, with exact test method names that the verification PowerShell `Select-String` checks can grep. This explains why prior gap-closure plans (06/07/08) cleanly converged.
- **`must_haves.truths` blocks are falsifiable.** Each plan declares exactly what becomes true, enabling automated post-execution verification.
- **Threat model is real, not boilerplate.** The STRIDE entries (especially T-07-01 information-disclosure of exception text and T-07-04 cancellation as DoS) are tied back to the row-label allow-list and the partial-file delete invariant, both of which are implemented.
- **Atomic restore semantics are well thought out.** The split between `BackupCopyOptions.CreateNewBackup` (`FileMode.CreateNew`) and `AtomicReplace` (`temp + Move`) is the right call and is preserved correctly through 07-02 → 07-04.
- **Sequential xEdit invariant is explicitly defended** in 07-03/07-05/07-09 with grep guards and a separate `_backupOperationCts` so non-xEdit cancellation cannot accidentally route through `Stop`/`ForceStop`.
- **Decisions log is faithfully populated.** STATE.md captures every locked Phase 7 decision (D-01..D-20) plus per-plan refinements, which keeps gap-closure plans aligned to the original contract.

## Concerns

### HIGH

- **C1 — Plan 07-09 Task 2 introduces a behavior break in the legacy synchronous `RestorePlugin` (throws `InvalidOperationException` on unsafe metadata) but doesn't account for `RestoreViewModel.DeleteSessionAsync` and any other still-sync callers.** The existing test `RestorePlugin_MissingBackupFile_ThrowsFileNotFoundException` is preserved, but the new pre-`File.Exists` validation path will throw `InvalidOperationException` before `FileNotFoundException` for *valid* metadata pointing at a missing backup if both checks happen to overlap. Worse, any path that reaches `RestorePlugin` with rooted-but-mismatched `OriginalPath` will now throw a *different* exception type than callers expect. The plan should either (a) explicitly enumerate every remaining sync-restore caller and document expected exception type, or (b) leave legacy `RestorePlugin` synchronous-only on its current code path and only validate in the async path that UI actually uses, since the verification report itself only marks the async path as the safety-critical one.

- **C2 — Plan 07-09 Task 3 changes `GetRestoreStatus` aggregate semantics in a way that may regress prior verified truths.** The verification report Truth #2 currently says: "RestoreSessionAsync iterates all plugins and counts exist in `BackupRestoreResult`, but `GetRestoreStatus` only returns `Canceled` when every row is canceled; failed+canceled/no-restored rows become `Failed`, losing cancellation as the aggregate outcome." The proposed fix is "no restored AND any canceled => `Canceled`". But there are existing passing tests (`RestoreSessionAsync_ContinuesAfterPluginFailure` returns `Partial`) and the `BackupRestoreResult.SessionSummary`/UI title mapping in `RestoreViewModel.ApplyRestoreResult`/`BuildRestoreSummaryText` is structured against the four current outcomes. The plan should explicitly enumerate which existing tests check `Status == Failed` for sessions that include any canceled rows, and update them as part of Task 3 rather than discovering them at GREEN time. (Also: a session with 1 failed + 1 canceled + 0 restored is arguably *more* a partial failure than a cancellation; the call is defensible but should be reflected in `RestoreSummaryText` copy too, otherwise the UI will say "Restore Canceled" while showing a failed row with a non-cancellation reason.)

### MEDIUM

- **C3 — Path-safety primitives in 07-09 Task 1/Task 2 don't account for Win32 alternate path forms.** The plan checks `Path.IsPathRooted` and `Path.GetFileName(name) != name` and normalizes via `Path.GetFullPath`. That is correct for normal paths and `..\` traversal, but does not block:
  - DOS device paths: `\\?\C:\Windows\System32\foo.esp` (technically rooted, will normalize, but `Path.GetFullPath` strips the prefix inconsistently across runtimes)
  - UNC paths: `\\server\share\foo.esp` (rooted; nothing in the plan rejects writes outside the local drive)
  - NTFS reparse points / symlinks inside the otherwise-contained target directory (containment check passes, but the resolved physical write target can still be elsewhere)
  - `Path.GetFileName` accepts trailing colons / ADS streams on Windows (`foo.esp:hidden`); `Path.GetFileName(x) == x` is true but it's a stream write
  
  None of these are exploitable in normal AutoQAC use, but if the threat model is "untrusted `session.json` metadata", the plan should either explicitly note these are out of scope or add a `Path.GetPathRoot` drive-letter check + simple-character whitelist.

- **C4 — The `.esm/.esp/.esl` extension whitelist in 07-09 Task 2 will reject `.esp.ghost` files** if the user ever backed up a ghosted plugin (Mutagen-loaded plugins are typically not ghosted, but xEdit users do toggle ghost state). It will also reject `.esh`, `.esm.bak`, etc. This is probably fine for Phase 7 but should be documented as an intentional restriction in the SUMMARY since it would make a previously-restorable backup un-restorable after the upgrade.

- **C5 — 07-09 doesn't add a regression test for the existing safe restore path it modifies.** Tasks 1–3 are all "reject + don't copy" tests. There is no positive-case test that `RestorePluginAsync` with normal `OriginalPath = "C:\Games\Skyrim\Data\Foo.esp"` and `FileName = "Foo.esp"` still succeeds end-to-end after the new policy is applied. The existing `RestorePluginAsync_MissingTargetDirectory_RecreatesDirectory` is close but uses a fixed temp path. One additional sanity test would catch policy over-tightening.

- **C6 — 07-09's grep-style automated verification at the bottom (`Select-String ... 'Task\.WhenAll|Parallel\.ForEachAsync|Task\.Run'`) will false-positive on legitimate `Task.Run` usage** in `Services/Cleaning` — for example any test-only `Task.Run` indirectly referenced. The earlier 07-05 plan made this a "secondary invariant" check; 07-09 should clarify the same (it currently lists it as a hard verification step).

- **C7 — Plans don't address what happens when retention cleanup itself triggers backup metadata mutation.** If `IBackupSessionDeleter.DeleteAsync` partially deletes a session (e.g., AV scanner releases mid-delete), the session is left half-existent. The retry handles transient locks but does not handle "directory still exists but `session.json` was already deleted" — `ClassifyRetentionDirectoriesAsync` would then treat the half-dead session as "malformed" and keep it forever. Not a Phase 7 blocker but worth a SUMMARY note for future cleanup.

### LOW

- **C8 — `BackupCopyProgress` was extended in 07-07 with optional file-count fields, but the original plan in 07-01 didn't mention this field set.** This is mentioned in 07-07's deviation log only obliquely. Consider adding a Phase 7 model-history note since the record now serves both byte-progress and count-progress modes.
- **C9 — 07-09 Task 2 acceptance criteria include a literal exception message "Backup metadata is not safe to restore."** Tying tests to exception message text is brittle; an exception-type assertion plus a custom enum/code on the exception (or a structured `BackupValidationException`) would survive copy edits better.
- **C10 — `_backupOperationLock` and `_processLock` (07-03) are both `object`-based locks with no documentation about ordering.** Two locks acquired in different orders is a deadlock risk; the orchestrator never holds both simultaneously today, but a comment on the invariant would prevent regression.
- **C11 — 07-04 sets `MinHeight="44"` on Cancel Restore but tests can't easily verify "effective hit height" through ViewModel assertions.** The acceptance criterion is satisfied by the literal AXAML attribute, which is what was actually tested.
- **C12 — Several plans (07-03, 07-05) reference `partial` workarounds in CleaningSessionResult/AppState but don't surface the ToString/diagnostic shape of the new fields.** Logs that include `BackupOperationState` may now leak file names that were intentionally kept out of UI dialogs.

## Suggestions

1. **For 07-09 Task 2:** Add a short "Caller audit" subsection enumerating every reachable caller of legacy `RestorePlugin` (search for non-async restore consumers) before committing the `InvalidOperationException` change, and capture the result in the plan's deviation log. If no UI-reachable callers remain, deprecate `RestorePlugin` outright instead of changing its exception contract.

2. **For 07-09 Task 3:** Before writing `RestoreSessionAsync_FailedThenCanceledRows_ReturnsCanceled`, run `Grep -n "BackupOperationStatus.Failed" AutoQAC.Tests/` and update the `BuildRestoreSummaryText` switch in `RestoreViewModel` so the cancellation title still produces an accurate summary line for failed-plus-canceled rows ("Restore canceled with N failed before cancellation"). Otherwise UI copy and aggregate title diverge.

3. **For 07-09 Task 1/Task 2:** Add a single helper `IsSafeSessionRelativeName(string name)` that combines `!string.IsNullOrWhiteSpace` + `!Path.IsPathRooted` + `Path.GetFileName(name) == name` + extension allow-list, and reuse it from both backup and restore. Right now Task 1 and Task 2 each re-state the rule, which invites drift.

4. **Add a positive regression test in 07-09:** `RestorePluginAsync_NormalPluginPath_StillRestoresSuccessfully` that writes a real source/target under temp, calls the real `BackupFileCopier`, and asserts `BackupOperationStatus.Complete`. Cheap insurance against policy over-tightening.

5. **Strengthen the path-safety threat note in 07-09's threat model** to either explicitly defer UNC/DOS-device/symlink hardening to a future phase or add a one-line drive-letter check (e.g., reject `OriginalPath` whose `Path.GetPathRoot` does not start with a letter+colon). Documenting the boundary is more important than implementing it; right now the verification report calls these "blockers" without specifying the exploit class.

6. **Convert the `Task.Run`/`Parallel.*` grep guard in 07-09's verification step into a `<acceptance_criteria>` source-content check** rather than a top-level verification command, matching how 07-05 framed it as a "secondary invariant." This avoids brittleness if a non-cleaning test ever needs `Task.Run`.

7. **Future-phase note:** consider adding a Phase 7.x ticket to harden `IBackupSessionDeleter` against half-deleted sessions (C7), and a note in `07-09-SUMMARY.md` (when written) about the `.esp.ghost` extension exclusion (C4) so users who report "can't restore my ghosted plugin" have a paper trail.

## Risk Assessment

**Overall: MEDIUM** (down from HIGH if C1 and C2 are addressed during 07-09 RED phase).

Justification:
- The Phase 7 architecture (managed copy + atomic temp+move + structured results + separate non-xEdit CTS) is sound and already shipped through 07-08 with 732 + 59 tests passing. Reverse-risk is low.
- Plan 07-09 is small (three tasks, one file in production, one in tests) and TDD-gated. The technical path is correct.
- The two HIGH concerns (C1: legacy `RestorePlugin` exception type swap, C2: aggregate-status semantics change) are *behavior* changes whose blast radius isn't fully enumerated in the plan. Both are detectable at RED time if the test names listed are actually run before any production edit, but neither plan task explicitly mandates running the *full* `BackupServiceTests` and `RestoreViewModelTests` suites at RED to catch collateral failures. Adding that step would drop overall risk to LOW.
- Path-safety primitives (C3) are good enough for the locally-trusted-metadata threat model the project actually has, but the verification report's wording ("can overwrite arbitrary rooted files") is broader than what the plan defends against. If that wording is taken at face value, additional hardening will be needed; if it's interpreted as "any non-plugin rooted target," 07-09 is sufficient.

---

## Codex Review

## Summary

The Phase 7 plan set is strong and unusually complete: it traces from requirements and decisions into service contracts, UI behavior, cancellation semantics, security validation, and regression tests. The wave ordering is mostly sound, and the later gap-closure plans address real weaknesses around metadata trust, dispatcher marshalling, structured failures, and mixed cancellation. The main risks are implementation complexity, brittle source-grep acceptance criteria, duplicated compatibility paths, and a few policy choices that should be made explicit before execution.

## Strengths

- Clear dependency layering: contracts/copier first, service behavior second, orchestrator/UI integration third, then verification and gap closure.
- Strong preservation of sequential xEdit behavior; multiple plans explicitly guard against `Task.WhenAll`, `Parallel.ForEachAsync`, and backup/xEdit overlap.
- Good treatment of cancellation: backup, restore, and retention are separated from xEdit stop/kill behavior.
- Good security maturation over time: later plans correctly identify backup metadata and plugin filenames as untrusted path inputs.
- Tests are behavior-oriented in many places, especially backup failure choice handling, restore partial results, retention warnings, and UI cancellation.
- 07-08 and 07-09 are valuable gap closures. They catch issues that commonly slip through: UI-thread progress mutation, structured setup failures, path traversal, and aggregate cancellation visibility.

## Concerns

- **HIGH:** The plans preserve legacy sync APIs while adding async equivalents. That creates two behavior surfaces that can drift, especially for validation and error mapping. 07-09 partly fixes this for backup/restore safety, but the sync wrappers remain a maintenance risk.
- **HIGH:** Restore target policy in 07-09 is stricter than earlier context. Requiring `OriginalPath` filename to equal `entry.FileName` and extension to be `.esm/.esp/.esl` is probably right, but it is a new policy decision. It should be explicitly accepted because it can reject unusual but technically possible metadata.
- **HIGH:** Some acceptance criteria rely on source text scans rather than behavior. These are useful as secondary guards, but brittle as primary proof. Refactors can fail them without regressions.
- **MEDIUM:** `BackupFailureReason.SourceMissing` is used for unsafe async backup filename validation in 07-09. That is semantically weak: unsafe metadata is not the same as missing source. It may be fine if never user-visible, but it blurs diagnostics.
- **MEDIUM:** The temp restore path `destinationPath + ".autoqac-tmp"` can collide with a stale temp file or another process. The plan should require unique temp names or safe cleanup before use.
- **MEDIUM:** Retention progress semantics in 07-07 are a bit vague: “total files is the number of classified valid sessions plus malformed rows known at that point” can produce unstable totals. Prefer computing total rows once before reporting progress.
- **MEDIUM:** Plans add many model types and status enums. The scope is justified, but there is risk of over-modeling if UI only needs a smaller subset.
- **LOW:** The provided project context says Avalonia 11.3/ReactiveUI, while repo instructions say Avalonia 12/CommunityToolkit MVVM and no ReactiveUI in ViewModels. Plans should follow the repo’s actual current stack.
- **LOW:** UI tests assert exact text and some AXAML colors. That is acceptable for locked UI copy, but could be noisy if visual design changes.

## Suggestions

- Make a short compatibility policy: either remove legacy sync restore/cleanup callers by the end of Phase 7, or add tests proving sync and async validation rules stay equivalent.
- Rename or add a failure reason for invalid backup metadata, such as `InvalidBackupMetadata`, if that will not widen UI copy. If UI labels must remain fixed, map it internally to a concise existing label at the boundary.
- Require unique temp files for atomic restore, for example a GUID-suffixed temp file in the destination directory, and delete only that exact file on cancel/failure.
- Replace most source-grep acceptance checks with named unit tests. Keep source scans only for strict invariants like “no parallel xEdit constructs.”
- Add one explicit test for existing target preservation when `File.Move(temp, destination, overwrite: true)` fails after the temp copy succeeds.
- In 07-03, define precisely how backup cancellation differs from whole-session cancellation. The plan says continue to the next plugin unless session token is canceled; tests should cover both paths.
- In 07-05, avoid hard-coding color values as behavioral acceptance unless the UI spec truly treats them as contract. Prefer checking semantic state in ViewModel tests.
- Before executing 07-09, confirm the restore target restriction is desired: filename match plus `.esm/.esp/.esl` extension only.

## Per-Plan Risk Assessment

| Plan | Risk | Assessment |
|---|---:|---|
| 07-01 | MEDIUM | Good foundation, but temp-file semantics and result taxonomy need care. |
| 07-02 | MEDIUM-HIGH | Core filesystem behavior changes; strong tests reduce risk, but sync/async drift is likely. |
| 07-03 | HIGH | Orchestrator integration is the riskiest area because it touches session control flow and xEdit sequencing. |
| 07-04 | MEDIUM | Good UI/VM direction; dispatcher marshalling gap is only fixed later in 07-08. |
| 07-05 | MEDIUM | Mostly presentation/integration risk; brittle visual/text assertions are the main issue. |
| 07-06 | LOW-MEDIUM | Focused and valuable gap closure around skipped/abort accounting. |
| 07-07 | MEDIUM | Important security/progress gap closure; retention progress semantics need tightening. |
| 07-08 | MEDIUM | Correctly addresses real async/UI and cancellation failure paths. |
| 07-09 | MEDIUM-HIGH | Security fixes are necessary, but restore target policy is stricter and should be explicitly approved. |

## Overall Risk

**MEDIUM-HIGH**, mainly due to breadth and the number of cross-cutting paths touched: backup service contracts, restore UI, cleaning orchestration, state service, progress UI, retention deletion, and legacy compatibility APIs. The plan quality is high enough to proceed, but execution should be strict: implement wave-by-wave, run targeted tests after each wave, and avoid expanding beyond the listed gap closures.

---

## Consensus Summary

The reviewers agree that Phase 7 is well-structured, with strong wave ordering, robust structured outcomes, atomic restore intent, and explicit protection of the sequential xEdit invariant. The main actionable feedback is not to broaden the phase; it is to tighten Plan 07-09 before execution so metadata validation, legacy sync behavior, aggregate cancellation semantics, and verification criteria are explicit and test-backed.

### Agreed Strengths

- All reviewers praised the layered plan sequence: copy/contracts first, backup/restore service behavior second, orchestrator/UI integration third, then targeted verification gap closures.
- Structured result models for backup, restore, retention, warning, partial, failed, and canceled outcomes are the right approach for `SAF-04`, `TEST-04`, and `PERF-04`.
- Atomic restore using temp-file-plus-move semantics is recognized as a critical safety improvement over direct overwrite.
- The plans preserve sequential xEdit cleaning and keep backup/restore/retention cancellation separate from xEdit stop/force-kill behavior.
- Plans 07-06 through 07-09 demonstrate useful verification-driven closure of real edge cases: skipped backup accounting, unsafe metadata, UI-thread progress, structured setup failures, and mixed cancellation.

### Agreed Concerns

- Legacy synchronous APIs remain a shared risk. Gemini, Claude, and Codex all warned that sync and async backup/restore paths can drift, especially around metadata validation, exception behavior, and caller expectations.
- Plan 07-09's restore target policy needs explicit confirmation. Claude and Codex both flagged filename equality plus `.esm/.esp/.esl` extension validation as a new behavior that may reject unusual but possible backups, such as ghosted plugins.
- Verification should rely on named behavioral tests first. Claude and Codex both called out source-grep checks as brittle unless they are secondary invariants for strict no-parallel-cleaning rules.
- Path and metadata validation should be centralized. Gemini and Claude both recommended shared helpers for backup destination and restore entry validation to avoid divergent safety rules.
- Atomic restore temp handling needs precision. Codex specifically raised stale temp-file collision risk, while the other reviews emphasized preserving the existing target on cancellation/failure.
- Plan 07-09 should include positive regression coverage for normal restore behavior, not only rejection tests, to catch over-tightened metadata policy.

### Divergent Views

- Gemini rated overall risk `LOW`, while Claude rated `MEDIUM` and Codex rated `MEDIUM-HIGH`. The split is mostly about how much risk remains in Plan 07-09's legacy sync behavior and stricter metadata policy.
- Gemini treated the path-containment strategy as sufficient, while Claude asked for explicit treatment or deferral of Win32 alternate paths, UNC paths, reparse points, and alternate data streams.
- Claude focused heavily on `RestorePlugin` exception compatibility and mixed failed/canceled aggregate semantics. Codex focused more on policy approval, temp-file uniqueness, and source-grep brittleness.
- Codex questioned whether `BackupFailureReason.SourceMissing` is semantically appropriate for unsafe backup filename validation; Gemini did not flag the taxonomy.

### Recommended Plan Updates Before `/gsd-plan-phase 7 --reviews`

- Audit remaining legacy sync callers and decide whether `RestorePlugin`/sync backup paths must share the new validation helpers or be deprecated after async migration.
- Confirm the Plan 07-09 restore policy: `OriginalPath` file name must match backup `FileName`, and only `.esm`, `.esp`, `.esl` restore targets are accepted.
- Add or require a positive safe-path restore regression test, such as `RestorePluginAsync_NormalPluginPath_StillRestoresSuccessfully`.
- Make source-grep checks secondary to behavior tests except for strict no-parallel xEdit invariants.
- Consider unique restore temp file names and a test for `File.Move` failure preserving the existing target.
- Document unsupported edge cases or future work for ghosted plugin extensions, Win32 alternate path forms, symlink/reparse-point escape, and half-deleted retention sessions.
