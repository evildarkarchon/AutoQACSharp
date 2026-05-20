---
id: T13
parent: S08
milestone: M001
provides:
  - service-layer DeleteSessionAsync(BackupSession, string, CancellationToken) on IBackupService with structured BackupSessionDeleteResult/Status types
  - RestoreViewModel.DeleteSessionAsync that delegates filesystem work to the backup service and consumes the structured result; ViewModel no longer touches System.IO.Directory.Delete
  - DeleteSessionCommand predicate gated on _backupRoot non-null/non-whitespace AND HasTrustedRestoreRoot AND !IsRestoreActive (unifies safety with Restore Selected/All)
  - extended RestoreCommands_DisabledWhenTrustedRestoreRootMissing test that also asserts DeleteSessionCommand.CanExecute is false
  - one canonical user-facing sentence shared between StatusText and dialog details for the out-of-root rejection branch
requires: []
affects: []
key_files: []
key_decisions: []
patterns_established: []
observability_surfaces: []
drill_down_paths: []
duration: 7 min
verification_result: passed
completed_at: 2026-04-29
blocker_discovered: false
---
# T13: 07-backup-restore-retention-safety 13

**# Phase 07 Plan 13: Service-layer Delete Session Containment Summary**

## What Happened

# Phase 07 Plan 13: Service-layer Delete Session Containment Summary

**RestoreWindow Delete Session now validates session-directory containment in `IBackupService.DeleteSessionAsync` using the shared `BackupPathContainment.IsContained` helper from Plan 07-14, with recursive filesystem work routed through the existing `IBackupSessionDeleter` seam — closing Truth #20 from `07-VERIFICATION.md` and resolving the cross-AI reviewer consensus that ViewModels must not call `System.IO.Directory.Delete` when an injectable service seam exists.**

## Performance

- **Duration:** ~7 min
- **Started:** 2026-04-29T11:20:50Z
- **Completed:** 2026-04-29T11:28:39Z
- **Tasks:** 3 (RED + GREEN + verification)
- **Files modified:** 6 (1 SUMMARY created, 5 source/test files modified)

## Accomplishments

- Added 5 new `DeleteSessionCommand_*` RED tests in `RestoreViewModelTests` covering unsafe-outside-root, sibling-prefix, parent-traversal, null-`_backupRoot`, normal-contained-session, and service-failed-status branches.
- Extended `RestoreCommands_DisabledWhenTrustedRestoreRootMissing` (Plan 07-11 test) to also assert `DeleteSessionCommand.CanExecute(null)` is false, unifying the safety story across all three restore-window commands.
- Added 6 new `DeleteSessionAsync_*` RED tests in `BackupServiceTests` driving the full validation matrix (null/empty/whitespace `backupRoot`, outside session, sibling-prefix, traversal-normalized escape, contained deletion, IOException failure).
- Added `BackupSessionDeleteStatus` enum (`Deleted` / `RejectedOutsideBackupRoot` / `Failed`) and `BackupSessionDeleteResult` record to `AutoQAC/Models/BackupOperationResults.cs`.
- Added `IBackupService.DeleteSessionAsync(BackupSession, string, CancellationToken)` with structured-result return semantics — never throws on expected validation/IO failures.
- Implemented `BackupService.DeleteSessionAsync`: short-circuits null/empty/whitespace inputs; delegates containment to `BackupPathContainment.IsContained` (Plan 07-14); routes the actual recursive delete through `IBackupSessionDeleter`; maps `IOException`/`UnauthorizedAccessException`/`DirectoryNotFoundException` to `Failed`; propagates `OperationCanceledException`.
- Refactored `RestoreViewModel.DeleteSessionAsync` to consume the structured result. The ViewModel no longer touches `System.IO` — `grep -nE "System\.IO\.Directory\.Delete" AutoQAC/ViewModels/RestoreViewModel.cs` returns zero matches.
- Added `CanDeleteSession` predicate gated on `_backupRoot` non-null/non-whitespace AND `HasTrustedRestoreRoot` AND `!IsRestoreActive`; switched `DeleteSessionCommand` from `CanRestoreAll` to `CanDeleteSession`.
- Wired `DeleteSessionCommand.NotifyCanExecuteChanged()` in both branches of `LoadSessionsAsync` because `_backupRoot` is a private field rather than an `[ObservableProperty]` (the source generator does not re-evaluate predicates on private-field mutations).
- Locked one canonical user-facing sentence — `"The selected backup session is outside the configured backup folder."` — used verbatim in both `StatusText` and dialog details for the out-of-root branch.
- Used the generic `"Technical details were written to the log."` sentence for IO failures so raw `IOException`/`UnauthorizedAccessException` text never appears in the dialog (D-04 concise reason pattern).
- Added `ThrowingBackupSessionDeleter` test helper (next to existing `RecordingBackupSessionDeleter` from Plan 07-02) so the `Failed` status path is exercised without filesystem stub coupling.
- Verified 175 Phase 7 cluster tests pass (BackupFileCopier + BackupService + BackupPathContainment + RestoreViewModel + CleaningOrchestrator + ProgressViewModel + ViewSubscriptionLifecycle + BackupOperationResult).
- Verified the full solution test suite passes (779 AutoQAC.Tests + 59 QueryPlugins.Tests = 838 total tests, 0 failures).
- Verified the sequential xEdit invariant remains intact — `grep -rnE "Task\.WhenAll|Parallel\.ForEachAsync|Task\.Run" AutoQAC/Services/Cleaning` returns zero matches.
- Verified zero containment-logic duplication — `grep -nE "Path\.GetFullPath.*StartsWith" AutoQAC/Services/Backup/BackupService.cs AutoQAC/ViewModels/RestoreViewModel.cs` returns zero matches.

## Reviewer-Consensus Resolutions Adopted

| Finding | Severity | Source | Resolution |
|---------|----------|--------|------------|
| Helper duplication risk between BackupService and RestoreViewModel | HIGH (the agent + Codex) / consistent-normalization (Gemini) | All three reviewers | Plan 07-14 extracted `BackupPathContainment`. This plan consumes it via the new service method instead of reintroducing inline containment logic in the ViewModel. |
| ViewModel filesystem I/O violates CLAUDE.md MVVM contract | MEDIUM (the agent + Codex preferred 2/3 vote; Gemini accepted "ViewModel patch is fine") | the agent + Codex | Service-layer move: `IBackupService.DeleteSessionAsync` owns containment validation and routes recursive delete through `IBackupSessionDeleter`. ViewModel consumes structured `BackupSessionDeleteResult`. |
| Null `_backupRoot` test coverage + predicate gating | MEDIUM (Codex) | Codex | `CanDeleteSession` returns false when `_backupRoot` is null/empty/whitespace; explicit `DeleteSessionCommand_NullBackupRoot_FailsClosed` `[Theory]` test covers null/empty inputs. |
| Status/dialog text inconsistency | LOW (the agent) | the agent | One canonical sentence (`The selected backup session is outside the configured backup folder.`) shared between `StatusText` and dialog details. Generic IO-failure sentence (`Technical details were written to the log.`) for the `Failed` branch. |
| Verification cluster ordering | LOW (the agent) | the agent | Wave 9 sequenced as 07-12 → 07-14 → 07-13 (07-13 `depends_on: [07-11, 07-14]`). All three plans landed in this order; the cluster filter exercises every newly added test. |
| `RestoreCommands_DisabledWhenTrustedRestoreRootMissing` should also gate `DeleteSessionCommand` | LOW (the agent) | the agent | Test extended with `vm.DeleteSessionCommand.CanExecute(null).Should().BeFalse()` assertion. |

## Verification Gap Closure

This plan, together with Plan 07-12 and Plan 07-14, closes the remaining Phase 7 verification gaps documented in `07-VERIFICATION.md`:

- **Truth #19 — normal progress lifecycle wiring:** Closed by Plan 07-12 (defense-in-depth `CloseRequested` + `Closed` wiring in `MainWindow.ShowProgressAsync`).
- **Truth #20 — Delete Session out-of-root containment:** Closed by this plan (Plan 07-13). RestoreWindow's recursive deletion now fails closed for out-of-root, sibling-prefix, traversal-normalized, and null/empty `_backupRoot` cases at both the service and ViewModel layers.
- **Containment-logic duplication risk:** Closed by Plan 07-14 (extracted `BackupPathContainment.IsContained`). This plan consumed the helper without reintroducing duplication.

## Grep Evidence

- `grep -nE "Task<BackupSessionDeleteResult>\s+DeleteSessionAsync" AutoQAC/Services/Backup/IBackupService.cs` → 1 match (line 98).
- `grep -nE "public async Task<BackupSessionDeleteResult>\s+DeleteSessionAsync" AutoQAC/Services/Backup/BackupService.cs` → 1 match (line 420).
- `grep -n "BackupPathContainment.IsContained(session.SessionDirectory, backupRoot)" AutoQAC/Services/Backup/BackupService.cs` → 1 match (line 437).
- `grep -n "_sessionDeleter.DeleteAsync(session.SessionDirectory, ct)" AutoQAC/Services/Backup/BackupService.cs` → 1 match (line 450).
- `grep -nE "Path\.GetFullPath.*StartsWith" AutoQAC/Services/Backup/BackupService.cs AutoQAC/ViewModels/RestoreViewModel.cs` → 0 matches (no duplication).
- `grep -n "_backupService.DeleteSessionAsync" AutoQAC/ViewModels/RestoreViewModel.cs` → 1 match (line 326).
- `grep -n "private bool CanDeleteSession" AutoQAC/ViewModels/RestoreViewModel.cs` → 1 match.
- `grep -nE "string\.IsNullOrWhiteSpace\(_backupRoot\)" AutoQAC/ViewModels/RestoreViewModel.cs` → 2 matches (predicate + defense-in-depth top-of-method guard).
- `grep -n "DeleteSessionCommand.NotifyCanExecuteChanged" AutoQAC/ViewModels/RestoreViewModel.cs` → 2 matches (both LoadSessionsAsync branches).
- `grep -nE "System\.IO\.Directory\.Delete" AutoQAC/ViewModels/RestoreViewModel.cs` → 0 matches (filesystem work moved out of ViewModel).
- `grep -c "The selected backup session is outside the configured backup folder\." AutoQAC/ViewModels/RestoreViewModel.cs` → 2 matches (StatusText + ShowErrorAsync details, same canonical sentence).
- `grep -rnE "Task\.WhenAll|Parallel\.ForEachAsync|Task\.Run" AutoQAC/Services/Cleaning` → 0 matches (sequential xEdit invariant preserved).

## Task Commits

Each task was committed atomically using TDD RED/GREEN gates:

1. **Task 1 RED — failing service + ViewModel tests:** `54b09bb` (test)
2. **Task 2 GREEN — service-layer DeleteSessionAsync + ViewModel refactor:** `65e943a` (feat)
3. **Task 3 verification:** no code commit; verification confirmed Phase 7 closure.

## Test Results

- **Targeted tests (Task 2 + Task 3 cluster):** 175 / 175 pass
  - `RestoreViewModelTests`: 26 / 26 (5 new + 1 extended + 20 existing)
  - `BackupServiceTests`: 57 / 57 (6 new + 51 existing)
  - `BackupPathContainmentTests`: 11 / 11 (Plan 07-14)
  - `BackupFileCopierTests`, `CleaningOrchestratorTests`, `ProgressViewModelTests`, `ViewSubscriptionLifecycleTests`, `BackupOperationResultTests`: all pass
- **Full solution suite:** 779 / 779 AutoQAC.Tests + 59 / 59 QueryPlugins.Tests = 838 / 838 (0 failures, 0 skipped).
- **Sequential xEdit source scan:** zero `Task.WhenAll`/`Parallel.ForEachAsync`/`Task.Run` matches under `AutoQAC/Services/Cleaning`.
- **Containment duplication scan:** zero `Path.GetFullPath.*StartsWith` matches in `BackupService.cs` or `RestoreViewModel.cs`.
- **Build:** `dotnet build AutoQACSharp.slnx` succeeds with 0 warnings.

## Deviations from Plan

None — the plan was executed exactly as written. The only minor adjustments were:

1. **CS8620 nullability warning fix in RED test:** The initial `Arg.Any<object?[]>()` matcher in `DeleteSessionAsync_DeleterThrowsIOException_ReturnsFailedAndLogsTechnicalDetails` triggered CS8620 because `ILoggingService.Error`'s `params` parameter is `object[]` (non-nullable). Tightened to `Arg.Any<object[]>()` so the matcher type matches the contract exactly. This was an in-task adjustment to the RED test before the GREEN commit; it does not change test behavior.

2. **`grep -c "BackupSessionDeleteStatus"` in `BackupOperationResults.cs` returns 2 matches**, not the "at least 4" listed in the plan's Task 2 acceptance criteria. The plan's wording counted "enum declaration + 3 enum members + uses". The literal-string grep only catches the type name, not the enum members `Deleted`/`RejectedOutsideBackupRoot`/`Failed` (which appear without a `BackupSessionDeleteStatus.` prefix in their declarations). The intent — that the enum exists with three members and is referenced by the result record — is fully satisfied (line 265 declares the enum, lines 267/270/273 declare the three members, line 283 uses the type as a property).

## Residual Risks (NOT fixed in Phase 7, per cross-AI consensus)

The following are documented for the eventual Phase 7 SUMMARY, not fixed in this plan:

- **NTFS reparse points and symlinks are NOT resolved.** `BackupPathContainment.IsContained` is string-level only; a symlinked path that physically points outside the trusted root would still be considered contained.
- **Fixed `.autoqac-tmp` collision risk after a crashed session** (Plan 07-10 residual). Unique same-directory temp naming is documented as future hardening.
- **Ghosted plugin extensions are not handled.** `IsApprovedPluginExtension` accepts only `.esm`/`.esp`/`.esl`; ghosted variants would be rejected at the validation gate.

These risks are out of scope for Phase 7 and should be reconsidered in a future hardening phase if the threat model changes.

## Self-Check: PASSED

Verification of claimed artifacts:

- `AutoQAC/Models/BackupOperationResults.cs` (modified, contains `BackupSessionDeleteStatus` enum at line 265 and `BackupSessionDeleteResult` record at line 282): FOUND
- `AutoQAC/Services/Backup/IBackupService.cs` (modified, contains `Task<BackupSessionDeleteResult> DeleteSessionAsync` at line 98): FOUND
- `AutoQAC/Services/Backup/BackupService.cs` (modified, contains `public async Task<BackupSessionDeleteResult> DeleteSessionAsync` at line 420 + `BackupPathContainment.IsContained` call at line 437): FOUND
- `AutoQAC/ViewModels/RestoreViewModel.cs` (modified, contains `private bool CanDeleteSession` predicate, `_backupService.DeleteSessionAsync` call, and zero `System.IO.Directory.Delete` matches): FOUND
- `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs` (modified, contains 5 new `DeleteSessionCommand_*` tests + extended `RestoreCommands_DisabledWhenTrustedRestoreRootMissing`): FOUND
- `AutoQAC.Tests/Services/BackupServiceTests.cs` (modified, contains 6 new `DeleteSessionAsync_*` tests + `ThrowingBackupSessionDeleter` helper): FOUND
- Commit `54b09bb` (test RED): FOUND
- Commit `65e943a` (feat GREEN): FOUND
