---
phase: 07-backup-restore-retention-safety
verified: 2026-04-29T08:45:00Z
status: gaps_found
score: 14/17 must-haves verified
overrides_applied: 0
re_verification:
  previous_status: gaps_found
  previous_score: 11/14
  gaps_closed:
    - "Restore progress callbacks are now marshaled through IUiDispatcher.Post before RestoreViewModel bindable state mutation."
    - "BackupPluginAsync maps expected backup session directory creation failures to structured BackupCreateResult failures."
    - "CleanupOldSessionsAsync converts classification/pre-delete cancellation into BackupRetentionCleanupResult(Canceled) with rows/counts."
  gaps_remaining: []
  regressions:
    - "Code-review CR-01 remains: backup creation still combines sessionDir with unvalidated PluginInfo.FileName."
    - "Code-review CR-02 remains for async restore targets: OriginalPath only has to be rooted; no data-root or file-name policy constrains the overwrite target."
gaps:
  - truth: "Backup creation cannot write outside the selected backup session directory."
    status: failed
    reason: "BackupPlugin and BackupPluginAsync both combine sessionDir with plugin.FileName without rejecting rooted or multi-segment names, so malformed PluginInfo.FileName can escape the backup session directory."
    artifacts:
      - path: "AutoQAC/Services/Backup/BackupService.cs"
        issue: "Lines 81 and 133 use Path.Combine(sessionDir, plugin.FileName) with no Path.GetFileName/rooted/containment validation."
      - path: "AutoQAC.Tests/Services/BackupServiceTests.cs"
        issue: "No regression test covers BackupPluginAsync or BackupPlugin rejecting traversal/rooted plugin file names."
    missing:
      - "Validate plugin.FileName is a simple non-rooted file name before backup destination construction."
      - "Verify the resolved backup destination remains under the normalized session directory."
      - "Add sync and async backup path traversal/rooted-name regression tests."
  - truth: "Restore metadata cannot redirect restores to arbitrary rooted filesystem targets."
    status: failed
    reason: "ValidateRestoreEntry treats any rooted OriginalPath as acceptable and normalizes it, so a corrupted session.json can restore a backup over an arbitrary user-writable rooted path. This does not satisfy the plan's approved restore root policy or code-review CR-02."
    artifacts:
      - path: "AutoQAC/Services/Backup/BackupService.cs"
        issue: "Lines 504-514 only reject null/whitespace/unrooted OriginalPath; they do not require filename match, plugin extension, configured data-folder containment, or another approved restore root. Legacy RestorePlugin at lines 208-224 also performs no metadata validation."
      - path: "AutoQAC.Tests/Services/BackupServiceTests.cs"
        issue: "Tests cover traversal/absolute FileName and unrooted OriginalPath, but not arbitrary rooted OriginalPath overwrite rejection or filename mismatch."
    missing:
      - "Define/enforce an approved restore root policy for async restore targets, or at minimum reject OriginalPath whose file name does not match entry.FileName and non-plugin extensions."
      - "Apply equivalent safety to the legacy RestorePlugin path or remove it from reachable service contracts."
      - "Add tests proving arbitrary rooted targets and filename mismatches are rejected before IBackupFileCopier.CopyAsync."
  - truth: "Restore cancellation outcomes remain distinguishable when failures and cancellations are mixed."
    status: partial
    reason: "GetRestoreStatus returns Failed when rows contain failures plus canceled rows and no restored rows, losing the cancellation outcome in the title even though rows include canceled counts."
    artifacts:
      - path: "AutoQAC/Services/Backup/BackupService.cs"
        issue: "Lines 544-558 return Canceled only when all rows are canceled; failed+canceled with no restored rows falls through to Failed."
      - path: "AutoQAC.Tests/Services/BackupServiceTests.cs"
        issue: "No test covers a restore session with one failed row and later canceled rows."
    missing:
      - "Define mixed failure+cancellation aggregate semantics and update GetRestoreStatus accordingly."
      - "Add a restore-session regression test for failed+canceled rows and expected user-visible aggregate status."
---

# Phase 07: Backup Restore & Retention Safety Verification Report

**Phase Goal:** Users can restore backups and run backup/retention work with clear failure reporting, cancellation, and progress while preserving sequential xEdit cleaning.  
**Verified:** 2026-04-29T08:45:00Z  
**Status:** gaps_found  
**Re-verification:** Yes — after gap-closure plan 07-08

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | User can restore backups and receive clear row-level failure reporting when target directories are missing, backup files are missing, access is denied, or target writes fail. | ✓ VERIFIED | `BackupService.cs:415-450` returns row-level `BackupFailureReason` values; tests cover missing backup, target-folder failure, access-denied mapping, and destination write failure. |
| 2 | Restore All continues after individual plugin failures and distinguishes Complete, Partial, Failed, and Canceled with counts. | ✗ PARTIAL | `RestoreSessionAsync` iterates all plugins and counts exist in `BackupRestoreResult`, but `GetRestoreStatus` only returns `Canceled` when every row is canceled; failed+canceled/no-restored rows become `Failed`, losing cancellation as the aggregate outcome. |
| 3 | Restore Selected/All require overwrite confirmation and inline results remain visible for complete/partial/failed/canceled outcomes. | ✓ VERIFIED | `RestoreViewModel.cs:178-199` and `230-248` confirm before restore; `ApplyRestoreResult` at `431-450` keeps inline result rows visible. |
| 4 | Active backup/restore copy cancellation deletes only active partial output/temp files and preserves existing restore targets. | ✓ VERIFIED | `BackupFileCopier.cs:72-76` returns canceled after deleting active output; atomic restore uses `destinationPath + ".autoqac-tmp"` at `92-95` and only moves at `51-54` after complete copy. |
| 5 | Retention cleanup protects the current session, keeps newest configured sessions, retries deletion once, and reports deletion failures as Warning rows. | ✓ VERIFIED | `CleanupOldSessionsAsync` filters current/newest at `350-370`; `DeleteRetentionCandidateAsync` retries once at `608-628`; warning status is selected at `384-387`. |
| 6 | Cleaning-session backups remain per-plugin immediately before xEdit launch and xEdit cleaning remains sequential. | ✓ VERIFIED | `CleaningOrchestrator.cs:265-284` processes plugins in a `foreach` and awaits backup before `CleanPluginAsync` at `409-420`; grep found no `Task.WhenAll`, `Parallel.ForEachAsync`, or `Task.Run` in `AutoQAC/Services/Cleaning`. |
| 7 | Cancel Backup/Cancel Cleanup are separate from xEdit Stop and do not terminate xEdit. | ✓ VERIFIED | `CancelBackupOperationAsync` no-ops when `_currentProcess` exists (`CleaningOrchestrator.cs:651-677`); `ProgressWindow.axaml:133-148` has separate `Cancel Backup` and `Cancel Cleanup` buttons. |
| 8 | Backup failure choices finalize and report the session instead of leaving cleaning state or progress incomplete. | ✓ VERIFIED | `CleaningOrchestrator.cs:315-332` publishes skipped result for `SkipPlugin`; `333-362` writes partial metadata and calls `FinishCleaningWithResults` before returning on `AbortSession`. |
| 9 | Restore rejects unsafe backup metadata FileName values before copying. | ✓ VERIFIED | `ValidateRestoreEntry` rejects rooted/multi-segment `FileName` and checks session containment (`BackupService.cs:478-497`); tests at `BackupServiceTests.cs:462` and `493`. |
| 10 | Restore rejects unsafe OriginalPath targets under an approved restore root policy. | ✗ FAILED | `ValidateRestoreEntry` only checks `OriginalPath` is rooted (`BackupService.cs:504-514`); no data-root, filename-match, extension, or approved-root policy exists. Legacy `RestorePlugin` also copies to metadata `OriginalPath` directly. |
| 11 | Retention cleanup progress is data-flowing and visible rather than a static active band. | ✓ VERIFIED | `ReportRetentionProgress` emits count progress (`BackupService.cs:532-542`); orchestrator maps counts into `BackupOperationState` (`CleaningOrchestrator.cs:737-750`); `ProgressViewModel.cs:238-253` renders counts/bytes. |
| 12 | Maintainer can verify SAF-04/TEST-04/PERF-04 clusters including permission and cleanup deletion failures. | ✗ PARTIAL | Phase 7 targeted tests pass (120 tests), but missing tests for backup filename traversal, arbitrary rooted restore targets, filename mismatch, and mixed failed+canceled aggregate status leave safety requirements unverified. |
| 13 | Full automated solution remains green after Phase 7 changes. | ✓ VERIFIED | `dotnet test "AutoQACSharp.slnx"` passed: 732 AutoQAC.Tests and 59 QueryPlugins.Tests. |
| 14 | Restore progress callbacks from real async copy work are marshaled through IUiDispatcher before bindable property mutation. | ✓ VERIFIED | 07-08 closed prior gap: `RestoreViewModel.cs:369-371` calls `_uiDispatcher.Post(() => UpdateRestoreProgress(...))`; test `RestoreProgressReportedFromWorkerThread_ShouldPostBindableUpdatesToDispatcher` exists. |
| 15 | BackupPluginAsync maps expected backup session directory creation failures to BackupCreateResult. | ✓ VERIFIED | 07-08 closed prior gap: `BackupService.cs:118-131` catches expected `Directory.CreateDirectory(sessionDir)` failures and returns `BackupCreateResult`; test `BackupPluginAsync_SessionDirectoryCreationFailure_ReturnsStructuredFailure` exists. |
| 16 | CleanupOldSessionsAsync converts cancellation during classification or immediately before deletion into structured canceled rows/counts. | ✓ VERIFIED | 07-08 closed prior gap: `BackupService.cs:337-394` catches/returns canceled results with rows; test `CleanupOldSessionsAsync_PreDeleteCancellation_ReturnsCanceledRowsAndCounts` exists. |
| 17 | Backup creation cannot write outside the selected backup session directory. | ✗ FAILED | `BackupService.cs:81` and `133` use `Path.Combine(sessionDir, plugin.FileName)` without simple-name or containment validation; no traversal/rooted-name backup tests exist. |

**Score:** 14/17 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `AutoQAC/Models/BackupOperationResults.cs` | Complete/partial/failed/canceled/warning result contracts and concise labels. | ✓ VERIFIED | Status enums, row records, display labels, and count helpers exist. |
| `AutoQAC/Services/Backup/BackupFileCopier.cs` | Managed async copy with cancellation, byte progress, overwrite policy, and safe partial cleanup. | ✓ VERIFIED | Substantive and tested; `FileInfo(sourcePath).Length` is now inside the main try/catch at line 40. |
| `AutoQAC/Services/Backup/BackupService.cs` | Structured async backup, restore, and retention behavior. | ⚠️ PARTIAL | 07-08 gaps closed, but backup destination and restore target metadata validation remain unsafe. |
| `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` | Backup/retention progress/cancel integration while preserving sequential cleaning. | ✓ VERIFIED | Backup precedes xEdit launch, retention precedes final result, separate non-xEdit cancellation exists. |
| `AutoQAC/Models/AppState.cs` / `IStateService.cs` / `StateService.cs` | Separate non-xEdit operation state. | ✓ VERIFIED | `BackupOperationState`, `SetBackupOperation`, and `ClearBackupOperation` exist and are consumed. |
| `AutoQAC/ViewModels/RestoreViewModel.cs` / `RestoreWindow.axaml` | Restore confirmation, progress, cancellation, and inline result rows. | ✓ VERIFIED | Confirmation/result UI exists; 07-08 dispatcher marshalling is present. |
| `AutoQAC/ViewModels/ProgressViewModel.cs` / `ProgressWindow.axaml` | Cleaning progress binding for backup/retention operation state and cancel commands. | ✓ VERIFIED | Count/byte progress rendering and separate cancel buttons are wired. |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `BackupService.RestorePluginRowAsync` | `ValidateRestoreEntry` | validation before path existence/copy | ⚠️ PARTIAL | Source `FileName` containment is validated before copy, but `OriginalPath` target validation accepts any rooted target. |
| `BackupService.RestorePluginRowAsync` | `IBackupFileCopier.CopyAsync` | validated source/target paths | ⚠️ PARTIAL | Source is session-contained; target can be arbitrary rooted metadata path. |
| `BackupService.BackupPluginAsync` | backup session directory | `Path.Combine(sessionDir, plugin.FileName)` | ✗ NOT SAFE | No validation prevents rooted/traversing plugin names from escaping sessionDir. |
| `BackupService.CleanupOldSessionsAsync` | progress UI | `IProgress<BackupCopyProgress>.Report` | ✓ VERIFIED | Reports count progress; orchestrator maps to `BackupOperationState`; VM renders. |
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
| `BackupService.RestorePluginRowAsync` | `backupPath`, `targetPath` | `ValidateRestoreEntry` from session metadata. | Partially | ⚠️ HOLLOW TARGET POLICY — target is normalized but not constrained to an approved restore root. |
| `BackupService.BackupPluginAsync` | `destinationPath` | `plugin.FileName` from `PluginInfo`. | Partially | ⚠️ HOLLOW VALIDATION — destination is not constrained to session root after combine. |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Phase 7 targeted test clusters pass. | `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~BackupServiceTests\|FullyQualifiedName~BackupFileCopierTests\|FullyQualifiedName~RestoreViewModelTests\|FullyQualifiedName~CleaningOrchestratorTests\|FullyQualifiedName~ProgressViewModelTests"` | Passed: 120, Failed: 0. | ✓ PASS |
| Full solution tests pass. | `dotnet test "AutoQACSharp.slnx"` | Passed: 732 AutoQAC.Tests + 59 QueryPlugins.Tests. | ✓ PASS |
| Sequential xEdit launch invariant has no parallel constructs. | Source grep for `Task.WhenAll`, `Parallel.ForEachAsync`, `Task.Run` under `AutoQAC/Services/Cleaning`. | No matches. | ✓ PASS |
| Restore progress uses UI dispatcher. | Source inspection of `RestoreViewModel.cs:369-371`. | Progress reporter posts to `_uiDispatcher`. | ✓ PASS |
| Backup destination is session-contained. | Source inspection of `BackupService.cs:81,133`. | No validation before `Path.Combine(sessionDir, plugin.FileName)`. | ✗ FAIL |
| Restore target is constrained by approved root policy. | Source inspection of `BackupService.cs:504-514`. | Any rooted `OriginalPath` is accepted. | ✗ FAIL |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| SAF-04 | 07-01 through 07-08 | User can restore backups with clear failure reporting when directories are missing, permissions fail, or a session partially restores. | ✗ BLOCKED | Row-level reporting exists, but untrusted restore metadata can redirect overwrites to arbitrary rooted targets and mixed failed+canceled restores lose cancellation as the aggregate outcome. |
| TEST-04 | 07-01 through 07-08 | Maintainer can verify backup restore safety across missing target directories, permission failures, partial failures, and cleanup deletion failures. | ⚠️ PARTIAL | Many targeted tests pass, but no tests cover backup destination traversal, arbitrary rooted restore target rejection, filename mismatch, or mixed failure+cancellation aggregation. |
| PERF-04 | 07-01 through 07-08 | User backup and retention operations remain cancellable and visible without parallelizing xEdit cleaning. | ✓ SATISFIED | Backup/retention progress and separate cancel buttons are wired, retention cancellation now returns rows/counts, and no xEdit parallelization constructs were found. |

No additional Phase 7 requirement IDs were found in `.planning/REQUIREMENTS.md` beyond SAF-04, TEST-04, and PERF-04.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `AutoQAC/Services/Backup/BackupService.cs` | 81, 133 | `Path.Combine(sessionDir, plugin.FileName)` with untrusted file name. | 🛑 Blocker | Backup creation can escape the backup session directory. |
| `AutoQAC/Services/Backup/BackupService.cs` | 504-514 | Rooted-only restore target validation. | 🛑 Blocker | Corrupted session metadata can overwrite arbitrary rooted files. |
| `AutoQAC/Services/Backup/BackupService.cs` | 544-558 | Canceled aggregate only when all rows are canceled. | ⚠️ Warning | Mixed failed+canceled restore can be titled `Restore Failed`, obscuring cancellation. |
| `AutoQAC/ViewModels/RestoreViewModel.cs` | 292-312 | Delete session uses direct `Directory.Delete` and surfaces `ex.Message`. | ⚠️ Warning | Advisory review WR-04 remains; not counted as a Phase 7 blocker because restore/backup/retention must-haves do not require session-delete hardening. |
| `AutoQAC/Services/Backup/BackupService.cs` | 168-205 | Session listing sorts by directory name before metadata timestamp. | ⚠️ Warning | Advisory review WR-02 remains; restore UI ordering can disagree with retention ordering after renamed/copied sessions. |

### Human Verification Required

No human verification is requested until blockers are fixed. After gap closure, manually smoke test RestoreWindow and ProgressWindow visual affordances for partial/canceled rows and Cancel Backup/Cancel Cleanup because visual quality cannot be fully checked by source inspection.

### Gaps Summary

Plan 07-08 closed all three previously recorded implementation gaps: restore progress is dispatcher-posted, backup session directory creation failures return structured backup results, and retention cancellation during classification/pre-delete returns canceled rows/counts. Targeted tests and the full solution test suite pass, and sequential xEdit cleaning remains preserved.

However, Phase 7 still does not achieve its goal. The code review's two critical filesystem-safety concerns remain materially unresolved in production code: backup creation trusts `PluginInfo.FileName` when constructing the destination path, and async restore trusts any rooted `OriginalPath` from backup metadata as the overwrite target. These are not just test omissions; the current source code permits backup-session escape and arbitrary rooted restore overwrite if metadata/input is malformed. A secondary cancellation-classification gap remains for mixed failed+canceled restore sessions.

---

_Verified: 2026-04-29T08:45:00Z_  
_Verifier: the agent (gsd-verifier)_
