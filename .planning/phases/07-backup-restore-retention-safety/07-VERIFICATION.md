---
phase: 07-backup-restore-retention-safety
verified: 2026-04-29T09:31:55Z
status: gaps_found
score: 16/18 must-haves verified
overrides_applied: 0
re_verification:
  previous_status: gaps_found
  previous_score: 14/17
  gaps_closed:
    - "Backup creation now rejects rooted, traversing, or multi-segment PluginInfo.FileName values before copy/path use."
    - "Restore filename mismatch, non-plugin extension, UNC/device root, unrooted OriginalPath, and mixed failed+canceled aggregate cases are covered."
    - "Restore failed+canceled/no-restored sessions now aggregate as Canceled and RestoreViewModel shows failed/canceled counts."
  gaps_remaining:
    - "Restore metadata can still redirect overwrites to same-named .esm/.esp/.esl files outside the current game Data folder or another explicit trusted restore root."
  regressions: []
gaps:
  - truth: "Restore metadata cannot redirect restores to arbitrary rooted filesystem targets outside an approved restore root."
    status: failed
    reason: "ValidateRestoreEntry now checks simple FileName, matching target filename, plugin extension, and normal local-drive root, but no configured Data-folder or trusted restore-root containment exists. A crafted session.json can still restore Backup.esp over C:\\Users\\...\\Backup.esp before any human-visible failure."
    artifacts:
      - path: "AutoQAC/Services/Backup/BackupService.cs"
        issue: "Lines 592-608 accept any normal local-drive rooted OriginalPath with matching file name and .esm/.esp/.esl extension; no game Data root is passed or validated."
      - path: "AutoQAC.Tests/Services/BackupServiceTests.cs"
        issue: "Tests cover filename mismatch and extension rejection, but not same-file-name plugin-extension targets outside the expected game Data folder."
    missing:
      - "Persist/pass the expected restore root (normally current game Data folder) into restore validation."
      - "Reject OriginalPath values outside the normalized trusted restore root before Directory.CreateDirectory or IBackupFileCopier.CopyAsync."
      - "Add async and legacy restore tests proving same-name .esp/.esm/.esl targets outside the trusted root are rejected before directory creation/copy."
  - truth: "Create-new backup copy failures never delete an existing destination backup file."
    status: failed
    reason: "BackupFileCopier uses FileMode.CreateNew for backup creation, but all IOException paths call DeletePartialOutput(actualOutputPath). If the destination already exists, the open fails before this copy creates anything, then the existing backup file is deleted. This violates backup safety and the code-review CR-01 blocker."
    artifacts:
      - path: "AutoQAC/Services/Backup/BackupFileCopier.cs"
        issue: "Lines 106-116 open FailIfExists destinations with FileMode.CreateNew; lines 84-88 delete actualOutputPath on IOException without tracking whether this copy attempt created it."
      - path: "AutoQAC.Tests/Services/BackupFileCopierTests.cs"
        issue: "No regression test asserts CreateNewBackup preserves pre-existing destination contents when the destination already exists."
    missing:
      - "Track whether this copy attempt created the output before deleting partial output, or copy to a unique temp file for create-new backups."
      - "Add a BackupFileCopier test where destination already exists under CreateNewBackup and original contents remain unchanged."
---

# Phase 07: Backup Restore & Retention Safety Verification Report

**Phase Goal:** Users can restore backups and run backup/retention work with clear failure reporting, cancellation, and progress while preserving sequential xEdit cleaning.  
**Verified:** 2026-04-29T09:31:55Z  
**Status:** gaps_found  
**Re-verification:** Yes — after gap-closure plan 07-09

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | User can restore backups and receive clear row-level failure reporting when target directories are missing, backup files are missing, access is denied, or target writes fail. | ✓ VERIFIED | `BackupService.cs:429-471` maps restore rows to `MissingBackupFile`, `AccessDenied`, `TargetFolderCreationFailed`, `TargetWriteFailed`, or `Canceled`; `BackupServiceTests.cs` covers missing backup, target-folder creation failure, access-denied mapping, and destination write failure. |
| 2 | Restore All continues after individual plugin failures and distinguishes Complete, Partial, Failed, and Canceled with counts. | ✓ VERIFIED | `RestoreSessionAsync` loops all entries (`BackupService.cs:279-288`) and `GetRestoreStatus` returns Complete/Partial/Canceled/Failed (`671-685`); mixed failed+canceled regression `RestoreSessionAsync_FailedThenCanceledRows_ReturnsCanceled` exists. |
| 3 | Restore Selected/All require overwrite confirmation and inline results remain visible for complete/partial/failed/canceled outcomes. | ✓ VERIFIED | `RestoreViewModel.cs:178-199` and `230-248` confirm before restore; `ApplyRestoreResult` (`431-450`) keeps inline rows visible; tests assert confirmation copy and exact outcome titles. |
| 4 | Active backup/restore copy cancellation deletes only active partial output/temp files and preserves existing restore targets. | ✓ VERIFIED | `BackupFileCopier.cs:51-54` only moves atomic restore temp after complete copy; cancel path deletes active output/temp (`72-76`); cancellation tests cover create-new partial cleanup and atomic target preservation. |
| 5 | Retention cleanup protects current session, keeps newest configured sessions, retries deletion once, and reports deletion failures as Warning rows. | ✓ VERIFIED | `CleanupOldSessionsAsync` protects current/newest (`370-388`), retries once (`735-755`), and returns Warning for failed rows (`404-407`); tests include current protection, newest retention, malformed skips, deletion failure after retry. |
| 6 | Cleaning-session backups remain per-plugin immediately before xEdit launch and xEdit cleaning remains sequential. | ✓ VERIFIED | `CleaningOrchestrator.cs:265-284` iterates a `foreach`, awaits backup, then launches xEdit at `409-420`; source scan found no `Task.WhenAll`, `Parallel.ForEachAsync`, or `Task.Run` under `AutoQAC/Services/Cleaning`. |
| 7 | Cancel Backup/Cancel Cleanup are separate from xEdit Stop and do not terminate xEdit. | ✓ VERIFIED | `CancelBackupOperationAsync` no-ops when `_currentProcess` exists (`CleaningOrchestrator.cs:651-677`); `ProgressWindow.axaml:133-148` has separate `Cancel Backup` and `Cancel Cleanup` buttons. |
| 8 | Backup failure choices finalize and report the session instead of leaving cleaning state or progress incomplete. | ✓ VERIFIED | `SkipPlugin` publishes `skippedResult` (`CleaningOrchestrator.cs:315-332`); `AbortSession` writes partial metadata and calls `FinishCleaningWithResults` before return (`333-362`). |
| 9 | Restore rejects unsafe backup metadata FileName values before copying. | ✓ VERIFIED | `ValidateRestoreEntry` calls `IsSafeSessionRelativeName(entry.FileName, requirePluginExtension: true)` and session containment checks before copy (`BackupService.cs:566-584`); traversal/absolute tests exist. |
| 10 | Restore rejects mismatched, unrooted, non-plugin-extension, UNC/device-rooted targets before copying. | ✓ VERIFIED | `IsSafeRestoreTarget` rejects unrooted, UNC/device, filename mismatch, and non-plugin extension paths (`BackupService.cs:622-638`); Plan 07-09 tests cover mismatch/non-plugin/normal safe restore. |
| 11 | Restore metadata cannot redirect restores to arbitrary rooted filesystem targets outside an approved restore root. | ✗ FAILED | No trusted restore root is available or enforced. `ValidateRestoreEntry` accepts same-name `.esp/.esm/.esl` targets on any normal local drive (`BackupService.cs:592-608`). |
| 12 | Retention cleanup progress is data-flowing and visible rather than a static active band. | ✓ VERIFIED | `ReportRetentionProgress` emits count progress (`BackupService.cs:659-669`); orchestrator maps counts to `BackupOperationState` (`CleaningOrchestrator.cs:737-750`); `ProgressViewModel.cs:238-253` renders count/byte text. |
| 13 | Maintainer can verify SAF-04/TEST-04/PERF-04 clusters including permission and cleanup deletion failures. | ✓ VERIFIED | Targeted Phase 7 cluster passed: 130 tests. Added tests cover traversal/rooted backup names, restore mismatch/extension/access-denied, deletion warning, mixed cancellation, and UI progress/cancel behavior. |
| 14 | Full automated solution remains green after Phase 7 changes. | ✓ VERIFIED | `dotnet test "AutoQACSharp.slnx"` passed on rerun: 742 AutoQAC.Tests and 59 QueryPlugins.Tests. Initial parallel run hit a transient build file lock only. |
| 15 | Restore progress callbacks from real async copy work are marshaled through IUiDispatcher before bindable property mutation. | ✓ VERIFIED | `RestoreViewModel.cs:369-371` posts progress updates through `_uiDispatcher`; worker-thread regression test exists. |
| 16 | BackupPluginAsync maps expected backup session directory creation failures to BackupCreateResult. | ✓ VERIFIED | `BackupService.cs:123-136` catches expected `Directory.CreateDirectory(sessionDir)` failures and returns structured `BackupCreateResult`; regression test exists. |
| 17 | CleanupOldSessionsAsync converts cancellation during classification or immediately before deletion into structured canceled rows/counts. | ✓ VERIFIED | `CleanupOldSessionsAsync` returns canceled rows in pre-canceled, post-classification, delete-loop, and caught cancellation paths (`BackupService.cs:351-367`, `390-413`); pre-delete cancellation test exists. |
| 18 | Create-new backup copy failures never delete an existing destination backup file. | ✗ FAILED | `BackupFileCopier.cs:84-88` deletes `actualOutputPath` on all IOException paths. For `CreateNewBackup`, `FileMode.CreateNew` can throw because the destination already exists before this copy created anything (`106-116`), deleting the existing file. |

**Score:** 16/18 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `AutoQAC/Models/BackupOperationResults.cs` | Complete/partial/failed/canceled/warning result contracts and concise labels. | ✓ VERIFIED | Status enums, row records, display labels, and count helpers exist. |
| `AutoQAC/Services/Backup/BackupFileCopier.cs` | Managed async copy with cancellation, byte progress, overwrite policy, and safe partial cleanup. | ✗ PARTIAL | Cancellation and atomic restore preservation exist, but create-new IOException cleanup can delete pre-existing destination files. |
| `AutoQAC/Services/Backup/BackupService.cs` | Structured async backup, restore, and retention behavior. | ⚠️ PARTIAL | Backup filename containment, restore source metadata, retention, and mixed cancellation are implemented; restore target lacks trusted-root containment. |
| `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` | Backup/retention progress/cancel integration while preserving sequential cleaning. | ✓ VERIFIED | Backup precedes xEdit launch, retention precedes final result, and separate non-xEdit cancellation exists. |
| `AutoQAC/Models/AppState.cs` / `IStateService.cs` / `StateService.cs` | Separate non-xEdit operation state. | ✓ VERIFIED | `BackupOperationState`, `SetBackupOperation`, and `ClearBackupOperation` exist and are consumed. |
| `AutoQAC/ViewModels/RestoreViewModel.cs` / `RestoreWindow.axaml` | Restore confirmation, progress, cancellation, and inline result rows. | ✓ VERIFIED | Confirmation/result UI exists; progress updates are dispatcher-posted. |
| `AutoQAC/ViewModels/ProgressViewModel.cs` / `ProgressWindow.axaml` | Cleaning progress binding for backup/retention operation state and cancel commands. | ⚠️ WARNING | Count/byte progress and cancel buttons are wired; code-review WR-01 remains because the results panel top-level grid is visible whenever not preview mode. |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `BackupService.BackupPlugin*` | backup session directory | `ValidateBackupDestination` before copy | ✓ VERIFIED | `IsSafeSessionRelativeName` plus full-path session-root containment before destination use. |
| `BackupFileCopier.CopyAsync` | create-new backup destination | `FileMode.CreateNew` and IOException cleanup | ✗ NOT SAFE | Existing destination failure can delete the existing file because created-output ownership is not tracked. |
| `BackupService.RestorePluginRowAsync` | `ValidateRestoreEntry` | validation before path existence/copy | ⚠️ PARTIAL | Source/file-name validation and target shape checks exist; trusted restore-root containment does not. |
| `BackupService.RestorePluginRowAsync` | `IBackupFileCopier.CopyAsync` | validated source/target paths | ⚠️ PARTIAL | Copy is gated by validation, but target validation still permits any same-name plugin file on a normal local drive. |
| `BackupService.CleanupOldSessionsAsync` | progress UI | `IProgress<BackupCopyProgress>.Report` | ✓ VERIFIED | Count progress flows to orchestrator state and ViewModel rendering. |
| `CleaningOrchestrator` | `IBackupService.BackupPluginAsync` | awaited inside plugin loop before xEdit cleaning | ✓ VERIFIED | Backup awaited at `CleaningOrchestrator.cs:284`; xEdit launch begins at `409`. |
| `CleaningOrchestrator` | `IBackupService.CleanupOldSessionsAsync` | awaited before final session result emission | ✓ VERIFIED | Cleanup awaited at `CleaningOrchestrator.cs:539-544`; result emitted at `559`. |
| `ProgressViewModel` | `ICleaningOrchestrator.CancelBackupOperationAsync` | cancel backup/cleanup command | ✓ VERIFIED | `ProgressViewModel.cs:206-210`; AXAML buttons bind command. |
| `RestoreViewModel` | `IUiDispatcher` | progress callback marshalling | ✓ VERIFIED | `RestoreViewModel.cs:369-371` posts progress update through dispatcher. |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|--------------------|--------|
| `RestoreWindow.axaml` | `RestoreResults`, `RestoreOutcomeTitle`, `RestoreProgressText` | `RestoreViewModel` applies `BackupRestoreResult` returned by `IBackupService`. | Yes | ✓ FLOWING |
| `ProgressWindow.axaml` | `BackupOperationProgressText` | `AppState.BackupOperation` set by `CleaningOrchestrator` progress callbacks. | Yes | ✓ FLOWING |
| `CleaningSessionResult` | `BackupCleanup` | `CleaningOrchestrator` awaits `CleanupOldSessionsAsync` before finalization. | Yes | ✓ FLOWING |
| `BackupService.RestorePluginRowAsync` | `targetPath` | `ValidateRestoreEntry` from session metadata. | Partially | ⚠️ HOLLOW TARGET ROOT POLICY — normalized but not constrained to game Data/trusted root. |
| `BackupFileCopier.CopyAsync` | `actualOutputPath` | `destinationPath` + copy policy. | Partially | ✗ HOLLOW OWNERSHIP — cleanup deletes path without proving this copy attempt created it. |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Phase 7 targeted test clusters pass. | `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~BackupFileCopierTests\|FullyQualifiedName~BackupServiceTests\|FullyQualifiedName~RestoreViewModelTests\|FullyQualifiedName~CleaningOrchestratorTests\|FullyQualifiedName~ProgressViewModelTests"` | Passed: 130, Failed: 0. | ✓ PASS |
| Full solution tests pass. | `dotnet test "AutoQACSharp.slnx"` | Passed: 742 AutoQAC.Tests + 59 QueryPlugins.Tests. | ✓ PASS |
| Sequential xEdit launch invariant has no parallel constructs. | Source scan for `Task.WhenAll`, `Parallel.ForEachAsync`, `Task.Run` under `AutoQAC/Services/Cleaning`. | No matches. | ✓ PASS |
| Backup destination is session-contained for PluginInfo.FileName. | Source inspection of `BackupService.cs:490-508`. | Simple-name and full-path containment validation exists. | ✓ PASS |
| Restore target is constrained by approved root policy. | Source inspection of `BackupService.cs:592-608`. | No trusted restore-root containment. | ✗ FAIL |
| Create-new copy preserves existing destination on fail-if-exists failure. | Source inspection of `BackupFileCopier.cs:84-88`, `106-116`, `154-160`. | Existing destination can be deleted on IOException. | ✗ FAIL |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| SAF-04 | 07-01 through 07-09 | User can restore backups with clear failure reporting when directories are missing, permissions fail, or a session partially restores. | ✗ BLOCKED | Clear row reporting exists, but restore metadata can still overwrite same-named plugin files outside a trusted restore root. |
| TEST-04 | 07-01 through 07-09 | Maintainer can verify backup restore safety across missing target directories, permission failures, partial failures, and cleanup deletion failures. | ⚠️ PARTIAL | Many targeted tests pass, but no tests cover same-name plugin-extension restore target outside trusted root or create-new copy existing-destination preservation. |
| PERF-04 | 07-01 through 07-09 | User backup and retention operations remain cancellable and visible without parallelizing xEdit cleaning. | ✓ SATISFIED | Backup/retention progress and cancel buttons are wired, retention cancellation returns rows/counts, and no xEdit parallelization constructs were found. |

No additional Phase 7 requirement IDs were found in `.planning/REQUIREMENTS.md` beyond SAF-04, TEST-04, and PERF-04.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `AutoQAC/Services/Backup/BackupService.cs` | 592-608 | Restore target validation lacks trusted restore-root containment. | 🛑 Blocker | Corrupted metadata can overwrite same-named plugin-extension files outside the intended game Data folder. |
| `AutoQAC/Services/Backup/BackupFileCopier.cs` | 84-88, 106-116, 154-160 | Deletes `actualOutputPath` after create-new open failure without ownership tracking. | 🛑 Blocker | Existing backup file can be deleted when `FileMode.CreateNew` fails because the file already exists. |
| `AutoQAC/Views/ProgressWindow.axaml` | 236-237 | Results panel top-level grid visible whenever not preview mode. | ⚠️ Warning | Code-review WR-01 remains; may overlay active progress UI/hit testing while `IsShowingResults` is false. |
| `AutoQAC/ViewModels/RestoreViewModel.cs` | 292-312 | Delete session uses direct `Directory.Delete` and surfaces `ex.Message`. | ⚠️ Warning | Separate restore-session-delete hardening follow-up; not counted as a Phase 7 backup/restore/retention blocker. |
| `AutoQAC/Services/Backup/BackupService.cs` | 174-185 | Session listing sorts by directory name before metadata timestamp. | ⚠️ Warning | Restore UI ordering can disagree with retention ordering after renamed/copied sessions. |

### Human Verification Required

No human verification is requested until blockers are fixed. After gap closure, manually smoke test RestoreWindow and ProgressWindow visual affordances for partial/canceled rows and Cancel Backup/Cancel Cleanup because visual quality and hit-testing cannot be fully checked by source inspection.

### Gaps Summary

Plan 07-09 closed two previously recorded implementation gaps: backup destinations are now session-contained for malformed `PluginInfo.FileName`, and mixed failed+canceled restore sessions preserve cancellation in aggregate service status and inline UI copy. Targeted Phase 7 tests and the full solution test suite pass, and sequential xEdit cleaning remains preserved.

Phase 7 still does not fully achieve the goal. Two filesystem-safety blockers remain in actual code: restore metadata can still redirect overwrites to same-named plugin-extension files outside a trusted restore root, and `BackupFileCopier` can delete an existing create-new backup destination when `FileMode.CreateNew` fails before this copy created any output. These are code behavior gaps, not merely missing summary claims.

---

_Verified: 2026-04-29T09:31:55Z_  
_Verifier: the agent (gsd-verifier)_
