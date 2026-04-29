---
phase: 07-backup-restore-retention-safety
verified: 2026-04-29T08:01:30Z
status: gaps_found
score: 11/14 must-haves verified
overrides_applied: 0
re_verification:
  previous_status: gaps_found
  previous_score: 7/12
  gaps_closed:
    - "BackupFailureChoice.SkipPlugin now publishes a skipped PluginCleaningResult and BackupFailureChoice.AbortSession finalizes through FinishCleaningWithResults."
    - "Restore metadata FileName traversal/absolute paths and unrooted OriginalPath are rejected before copying."
    - "Retention cleanup now reports count progress through BackupCopyProgress and the orchestrator maps it into BackupOperationState."
    - "TEST-04 now covers malicious metadata, access-denied restore mapping, target write failure, and cleanup deletion failure after retry."
  gaps_remaining:
    - "Restore copy progress still mutates RestoreViewModel UI-bound properties from the backup copy callback without IUiDispatcher marshalling."
    - "BackupPluginAsync still lets backup session directory creation exceptions escape before structured BackupCreateResult/backup-failure callback handling."
    - "CleanupOldSessionsAsync still lets cancellation during directory classification/pre-delete ThrowIfCancellationRequested escape instead of returning a structured canceled cleanup result."
  regressions: []
gaps:
  - truth: "Restore progress and cancellation remain usable during real async copy work."
    status: failed
    reason: "RestoreViewModel.CreateRestoreProgressReporter uses a custom synchronous IProgress implementation that directly mutates UI-bound properties; BackupFileCopier reports from async copy continuations using ConfigureAwait(false), so real progress can arrive off the Avalonia UI thread."
    artifacts:
      - path: "AutoQAC/ViewModels/RestoreViewModel.cs"
        issue: "Lines 366-428 create RestoreProgressReporter and call UpdateRestoreProgress directly; no IUiDispatcher or Progress<T> UI-context marshalling is injected or used."
      - path: "AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs"
        issue: "Restore progress tests only report synchronously from substitutes and do not prove thread marshalling for async copy callbacks."
    missing:
      - "Marshal restore progress updates through IUiDispatcher (or another UI-thread-safe mechanism) before mutating RestoreProgressText, RestoreBytesCopied, RestoreTotalBytes, or RestoreResults-related state."
      - "Add a regression test proving progress reported from an asynchronous/background callback is dispatched before UI-bound state changes."
  - truth: "Backup creation failures are reported through structured backup failure choices instead of escaping the cleaning workflow."
    status: failed
    reason: "BackupService.BackupPluginAsync creates the backup session directory outside any failure mapping; IOException/UnauthorizedAccessException/ArgumentException can bypass BackupCreateResult, so CleaningOrchestrator never reaches the backup failure callback for that failure."
    artifacts:
      - path: "AutoQAC/Services/Backup/BackupService.cs"
        issue: "Line 111 calls Directory.CreateDirectory(sessionDir) before CopyAsync and outside try/catch/result mapping."
      - path: "AutoQAC.Tests/Services/CleaningOrchestratorTests.cs"
        issue: "Tests cover structured BackupCreateResult failures returned by the mock, but not a real BackupPluginAsync session-directory creation failure path."
    missing:
      - "Catch expected session-directory creation exceptions in BackupPluginAsync and return BackupCreateResult with AccessDenied or TargetFolderCreationFailed."
      - "Add coverage proving backup session directory creation failure reaches BackupFailureCallback with a concise reason and does not bypass session reporting."
  - truth: "Retention cleanup cancellation always returns a structured canceled cleanup result with rows/counts."
    status: failed
    reason: "CleanupOldSessionsAsync handles pre-canceled tokens and cancellation during retry/deletion, but cancellation during ClassifyRetentionDirectoriesAsync or the explicit pre-delete ct.ThrowIfCancellationRequested propagates OperationCanceledException. The orchestrator then reports whole-session cancellation with BackupCleanup still null."
    artifacts:
      - path: "AutoQAC/Services/Backup/BackupService.cs"
        issue: "Lines 317 and 349 can throw OperationCanceledException outside a catch that converts it to BackupRetentionCleanupResult(Canceled, rows)."
      - path: "AutoQAC.Tests/Services/BackupServiceTests.cs"
        issue: "Cancellation tests cover pre-canceled tokens and retry-delay cancellation, not classification/pre-delete cancellation after some rows are known."
    missing:
      - "Catch OperationCanceledException inside CleanupOldSessionsAsync around classification and delete-loop cancellation gates, add remaining rows, report final progress, and return BackupOperationStatus.Canceled."
      - "Add tests for cancellation during classification or just before deleting a candidate that assert rows/counts and no exception escape."
---

# Phase 07: Backup Restore & Retention Safety Verification Report

**Phase Goal:** Users can restore backups and run backup/retention work with clear failure reporting, cancellation, and progress while preserving sequential xEdit cleaning.  
**Verified:** 2026-04-29T08:01:30Z  
**Status:** gaps_found  
**Re-verification:** Yes — after gap-closure plans 07-06 and 07-07

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | User can restore backups and receive clear row-level failure reporting when target directories are missing, backup files are missing, access is denied, or target writes fail. | ✓ VERIFIED | `BackupService.cs:385-420` validates rows, maps missing backup files, target folder failures, access denied, and target writes to `BackupFailureReason`; `BackupServiceTests.cs:400-542` covers target-folder failure, traversal/absolute metadata rejection, unrooted target rejection, and access-denied restore mapping. |
| 2 | Restore All continues after individual plugin failures and distinguishes Complete, Partial, Failed, and Canceled with counts. | ✓ VERIFIED | `BackupService.cs:231-249` iterates all session plugins; `BackupOperationResults.cs:214-223` exposes restored/failed/canceled counts; existing restore-session tests cover partial continuation. |
| 3 | Restore Selected/All require overwrite confirmation and inline results remain visible for complete/partial/failed/canceled outcomes. | ✓ VERIFIED | `RestoreViewModel.cs:178-195` and `227-245` require confirmations and call structured async restore APIs; `ApplyRestoreResult` at `435-455` keeps inline rows visible; `RestoreWindow.axaml:94-154` renders progress/results inline. |
| 4 | Active backup/restore copy cancellation deletes only active partial output/temp files and preserves existing restore targets. | ✓ VERIFIED | `BackupFileCopier.cs:56-73` deletes `actualOutputPath`; atomic restore uses `.autoqac-tmp` at `76-79`; targeted copier/service tests pass. |
| 5 | Retention cleanup protects the current session, keeps newest configured sessions, retries deletion once, and reports deletion failures as Warning rows. | ✓ VERIFIED | `BackupService.cs:327-364` keeps newest/current and returns Warning on failed rows; `570-599` retries deletion once and maps retry failure to `CleanupDeletionFailed`; `BackupServiceTests.cs:716-734` verifies warning after retry failure. |
| 6 | Cleaning-session backups remain per-plugin immediately before xEdit launch and xEdit cleaning remains sequential. | ✓ VERIFIED | `CleaningOrchestrator.cs:265-284` processes plugins in a `foreach` and awaits backup before `CleanPluginAsync` at `409-420`; grep found no `Task.WhenAll`, `Parallel.ForEachAsync`, or `Task.Run` in `CleaningOrchestrator.cs`. |
| 7 | Cancel Backup/Cancel Cleanup are separate from xEdit Stop and do not terminate xEdit. | ✓ VERIFIED | `CleaningOrchestrator.cs:651-677` no-ops cancellation when `_currentProcess` exists and only cancels `_backupOperationCts`; `ProgressWindow.axaml:133-148` has separate `Cancel Backup` and `Cancel Cleanup` buttons. |
| 8 | Backup failure choices finalize and report the session instead of leaving cleaning state or progress incomplete. | ✓ VERIFIED | Gap closed by 07-06: `CleaningOrchestrator.cs:315-332` publishes skipped result for SkipPlugin; `333-362` writes partial metadata when present and calls `FinishCleaningWithResults` before return; tests at `CleaningOrchestratorTests.cs:1579-1688`. |
| 9 | Restore safety validates untrusted backup-session metadata before copying. | ✓ VERIFIED | Gap closed by 07-07: `ValidateRestoreEntry` at `BackupService.cs:433-491` rejects rooted/traversing `FileName`, checks session containment with `Path.GetFullPath(sessionDir)`, and rejects unrooted targets; tests at `BackupServiceTests.cs:426-510`. |
| 10 | Retention cleanup progress is data-flowing and visible rather than a static active band. | ✓ VERIFIED | Gap closed by 07-07: `BackupService.cs:319,337,344,352,356` reports retention progress; `CleaningOrchestrator.cs:737-750` maps count fields into `BackupOperationState`; `ProgressViewModel.cs:238-253` renders count-only progress. |
| 11 | Maintainer can verify SAF-04/TEST-04/PERF-04 clusters including permission and cleanup deletion failures. | ✓ VERIFIED | Targeted tests passed (116 tests). Named tests now cover metadata traversal/absolute paths, unrooted targets, access-denied mapping, destination write failure, deletion failure after retry, progress, skip/abort gap closure, and sequential guard. |
| 12 | Full automated solution remains green after Phase 7 changes. | ✓ VERIFIED | `dotnet test AutoQACSharp.slnx` passed: 728 AutoQAC.Tests and 59 QueryPlugins.Tests. |
| 13 | Restore progress and cancellation remain usable during real async copy work. | ✗ FAILED | `RestoreViewModel.cs:366-428` directly mutates UI-bound properties from the progress reporter; no UI dispatcher marshalling exists despite copy callbacks occurring from async `ConfigureAwait(false)` paths in `BackupFileCopier`. |
| 14 | Backup creation and retention cancellation failures remain structured in all expected filesystem/cancel paths. | ✗ FAILED | `BackupService.cs:111` can throw before returning `BackupCreateResult`; `BackupService.cs:317/349` can throw cancellation before returning `BackupRetentionCleanupResult(Canceled, rows)`. |

**Score:** 11/14 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `AutoQAC/Models/BackupOperationResults.cs` | Shared complete/partial/failed/canceled/warning result contracts and concise labels. | ✓ VERIFIED | Contains aggregate statuses, row statuses, approved labels, count helpers, and count-capable `BackupCopyProgress`. |
| `AutoQAC/Services/Backup/BackupFileCopier.cs` | Managed async copy with cancellation, byte progress, overwrite policy, and safe partial cleanup. | ⚠️ PARTIAL | Substantive and tested for main paths, but `new FileInfo(sourcePath).Length` remains outside the try block (`line 35`), so source metadata races can still escape structured copy results. |
| `AutoQAC/Services/Backup/BackupService.cs` | Structured async backup, restore, and retention behavior. | ⚠️ PARTIAL | Restore metadata validation and retention progress gaps are closed; backup session directory creation and some retention cancellation paths remain unstructured. |
| `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` | Backup/retention progress/cancel integration while preserving sequential cleaning. | ✓ VERIFIED | Sequential order preserved; SkipPlugin/AbortSession gap closure present; separate cancel API does not terminate xEdit. |
| `AutoQAC/Models/AppState.cs` / `IStateService.cs` / `StateService.cs` | Separate non-xEdit operation state. | ✓ VERIFIED | `BackupOperationState`, `SetBackupOperation`, and `ClearBackupOperation` exist and are used by orchestrator/progress VM. |
| `AutoQAC/ViewModels/RestoreViewModel.cs` / `RestoreWindow.axaml` | Restore confirmation, progress, cancellation, and inline result rows. | ⚠️ PARTIAL | Confirmation/result UI exists; restore progress callback is not UI-thread safe. |
| `AutoQAC/ViewModels/ProgressViewModel.cs` / `ProgressWindow.axaml` | Cleaning progress binding for backup/retention operation state and cancel commands. | ✓ VERIFIED | Count/byte progress rendering and separate cancel buttons are wired; note byte unit consistency remains a non-blocking quality warning. |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `BackupService.RestorePluginRowAsync` | `ValidateRestoreEntry` | validation before path existence/copy | ✓ VERIFIED | `BackupService.cs:385-388` rejects invalid metadata before `File.Exists`, `Directory.CreateDirectory`, or `CopyAsync`. |
| `BackupService.RestorePluginRowAsync` | `IBackupFileCopier.CopyAsync` | validated source/target paths | ✓ VERIFIED | `BackupService.cs:409-414` passes `backupPath` and normalized `targetPath` after validation. |
| `BackupService.CleanupOldSessionsAsync` | progress UI | `IProgress<BackupCopyProgress>.Report` | ✓ VERIFIED | `ReportRetentionProgress` calls `progress?.Report`; orchestrator maps count fields to `BackupOperationState`; VM renders count text. |
| `CleaningOrchestrator` | `IBackupService.BackupPluginAsync` | awaited inside plugin loop before xEdit cleaning | ✓ VERIFIED | Backup awaited at `CleaningOrchestrator.cs:284`; xEdit launch call begins at `409`. |
| `CleaningOrchestrator` | `IBackupService.CleanupOldSessionsAsync` | awaited before final session result emission | ✓ VERIFIED | `CleaningOrchestrator.cs:539-544` awaits cleanup before `FinishCleaningWithResults` at `559`. |
| `ProgressViewModel` | `ICleaningOrchestrator.CancelBackupOperationAsync` | cancel backup/cleanup command | ✓ VERIFIED | `ProgressViewModel.cs:206-210`; AXAML buttons bind same command. |
| `RestoreViewModel` | UI-thread dispatcher | progress callback marshalling | ✗ NOT WIRED | `RestoreViewModel` has no injected `IUiDispatcher`; `RestoreProgressReporter.Report` calls the VM updater directly. |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|--------------------|--------|
| `RestoreWindow.axaml` | `RestoreResults`, `RestoreOutcomeTitle`, `RestoreProgressText` | `RestoreViewModel` applies `BackupRestoreResult` returned by `IBackupService`. | Yes, but progress threading unsafe | ⚠️ PARTIAL |
| `ProgressWindow.axaml` | `BackupOperationProgressText` | `AppState.BackupOperation` set by `CleaningOrchestrator` progress callbacks. | Yes | ✓ FLOWING |
| `CleaningSessionResult` | `BackupCleanup` | `CleaningOrchestrator` awaits `CleanupOldSessionsAsync` before finalization. | Yes for normal/warning paths; cancel during classification can bypass | ⚠️ PARTIAL |
| `BackupService.RestorePluginRowAsync` | `backupPath`, `targetPath` | `ValidateRestoreEntry` from session metadata. | Yes, validated before copy | ✓ FLOWING |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Phase 7 targeted test clusters pass. | `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~BackupServiceTests\|FullyQualifiedName~BackupFileCopierTests\|FullyQualifiedName~RestoreViewModelTests\|FullyQualifiedName~CleaningOrchestratorTests\|FullyQualifiedName~ProgressViewModelTests"` | Passed: 116, Failed: 0. | ✓ PASS |
| Full solution tests pass. | `dotnet test "AutoQACSharp.slnx"` | Passed: 728 AutoQAC.Tests + 59 QueryPlugins.Tests. | ✓ PASS |
| Sequential xEdit launch invariant has no parallel constructs. | Source grep/read for `Task.WhenAll`, `Parallel.ForEachAsync`, `Task.Run`. | No matches in `CleaningOrchestrator.cs`; backup precedes xEdit call. | ✓ PASS |
| Restore progress uses UI dispatcher. | Source inspection of `RestoreViewModel.cs`. | No `IUiDispatcher`/dispatcher marshalling; direct progress callback mutation remains. | ✗ FAIL |
| Backup session directory creation is structured. | Source inspection of `BackupService.cs:94-126`. | `Directory.CreateDirectory(sessionDir)` outside catch/result mapping. | ✗ FAIL |
| Retention cancellation is structured for classification/pre-delete. | Source inspection of `BackupService.cs:317,349`. | `OperationCanceledException` can escape `CleanupOldSessionsAsync` before result creation. | ✗ FAIL |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| SAF-04 | 07-01 through 07-07 | User can restore backups with clear failure reporting when directories are missing, permissions fail, or a session partially restores. | ⚠️ PARTIAL | Restore row-level safety is now implemented, including malicious metadata and access-denied mapping. Remaining backup creation exception escape means not all backup/restore filesystem failures enter clear user choice/reporting paths. |
| TEST-04 | 07-01 through 07-07 | Maintainer can verify backup restore safety across missing target directories, permission failures, partial failures, and cleanup deletion failures. | ✓ SATISFIED | Targeted tests include missing target, metadata traversal/absolute path rejection, unrooted target rejection, partial restore, access denied, target write failure, and cleanup deletion failure after retry. |
| PERF-04 | 07-01 through 07-07 | User backup and retention operations remain cancellable and visible without parallelizing xEdit cleaning. | ⚠️ PARTIAL | Sequential xEdit, visible backup/retention progress, and separate cancel buttons are verified. Retention cancellation is not structured for classification/pre-delete cancellation, and restore progress has UI-thread risk. |

No additional Phase 7 requirement IDs were found in `.planning/REQUIREMENTS.md` beyond SAF-04, TEST-04, and PERF-04.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `AutoQAC/ViewModels/RestoreViewModel.cs` | 366-428 | Custom progress reporter directly mutates UI-bound properties. | 🛑 Blocker | Real async restore progress can update Avalonia-bound state off the UI thread. |
| `AutoQAC/Services/Backup/BackupService.cs` | 111 | Filesystem operation outside structured backup failure mapping. | 🛑 Blocker | Backup session directory creation failures bypass BackupCreateResult and backup failure choices. |
| `AutoQAC/Services/Backup/BackupService.cs` | 317, 349 | Cancellation gates outside structured retention result conversion. | 🛑 Blocker | Cleanup cancellation can be reported as whole-session cancellation without backup cleanup rows/counts. |
| `AutoQAC/Services/Backup/BackupFileCopier.cs` | 35 | Source length read outside copier try/catch. | ⚠️ Warning | Source metadata race/access failures can escape structured copy results. |
| `AutoQAC/Services/Backup/BackupService.cs` | 48-55 | Timestamp-only backup session directory name. | ⚠️ Warning | Two sessions in the same second can collide/mix backup files. |
| `AutoQAC/ViewModels/ProgressViewModel.cs` / `RestoreViewModel.cs` | 260-263 / 404-416 | Inconsistent binary-vs-decimal byte formatting. | ⚠️ Warning | Same copy progress model can display different byte units in cleaning vs restore UI. |

### Human Verification Required

No human verification is requested until blockers are fixed. After gap closure, a manual smoke test should verify RestoreWindow and ProgressWindow visual affordances for partial/canceled rows and Cancel Backup/Cancel Cleanup because visual quality cannot be fully checked by source inspection.

### Gaps Summary

Plans 07-06 and 07-07 closed the previous verification gaps: backup failure Skip/Abort accounting now finalizes correctly, restore metadata is validated before copying, retention progress now data-flows into the UI path, and TEST-04 has the missing safety coverage. Automated Phase 7 and full solution tests pass, and sequential xEdit cleaning remains preserved.

However, Phase 7 still cannot pass goal-backward verification. Independent inspection of the Phase 07 code review findings found one still-blocking restore progress threading defect and two structured-reporting/cancellation gaps in production paths not covered by the passing tests. These affect the phase goal directly because restore progress can fail during real async copy work, backup filesystem setup failures can bypass clear backup failure choices, and retention cancellation can lose cleanup rows/counts.

---

_Verified: 2026-04-29T08:01:30Z_  
_Verifier: the agent (gsd-verifier)_
