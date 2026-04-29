---
phase: 07-backup-restore-retention-safety
verified: 2026-04-29T07:35:33Z
status: gaps_found
score: 7/12 must-haves verified
overrides_applied: 0
gaps:
  - truth: "Backup failure choices finalize and report the session instead of leaving cleaning state or progress incomplete."
    status: failed
    reason: "Code-review CR-01 and CR-02 are real in the actual source: AbortSession returns before FinishCleaningWithResults, and SkipPlugin updates SkippedPlugins then continues without adding a PluginCleaningResult or progress increment."
    artifacts:
      - path: "AutoQAC/Services/Cleaning/CleaningOrchestrator.cs"
        issue: "Lines 315-323 SkipPlugin branch continues without pluginResults.Add or AddDetailedCleaningResult; lines 324-338 AbortSession returns inside the try block before final session completion."
      - path: "AutoQAC.Tests/Services/CleaningOrchestratorTests.cs"
        issue: "No regression tests cover BackupFailureChoice.SkipPlugin or BackupFailureChoice.AbortSession; only ContinueWithoutBackup is tested."
    missing:
      - "For SkipPlugin, add a skipped PluginCleaningResult, publish it through AddDetailedCleaningResult, and preserve progress/session accounting."
      - "For AbortSession, route through FinishCleaningWithResults (or finalize before return) so IsCleaning is cleared and a final result is emitted."
      - "Add tests for BackupFailureChoice.SkipPlugin and BackupFailureChoice.AbortSession."
  - truth: "Restore safety validates backup-session metadata paths before copying."
    status: failed
    reason: "Code-review CR-03 is real: restore builds source and target paths directly from session metadata, allowing file-name path traversal/absolute backup paths and arbitrary OriginalPath overwrite targets."
    artifacts:
      - path: "AutoQAC/Services/Backup/BackupService.cs"
        issue: "Lines 378-403 use Path.Combine(sessionDir, entry.FileName) and entry.OriginalPath without validating that FileName is a simple file name or that the resolved backup path stays under the session directory."
    missing:
      - "Reject or structurally fail restore rows whose FileName is rooted or contains traversal."
      - "Ensure resolved backup paths remain contained within the selected backup session directory."
      - "Validate/limit restore targets according to the trusted game data root or an equivalent safe target policy before overwriting."
      - "Add malicious metadata regression tests for `..\\` and absolute FileName values."
  - truth: "Retention cleanup progress is data-flowing, visible progress rather than a static active band."
    status: failed
    reason: "CleanupOldSessionsAsync accepts a progress sink and the orchestrator/UI wire a cleanup progress band, but the service never calls progress.Report, so count progress never advances for retention cleanup."
    artifacts:
      - path: "AutoQAC/Services/Backup/BackupService.cs"
        issue: "Lines 297-357 accept IProgress<BackupCopyProgress>? progress but no progress.Report call exists anywhere in CleanupOldSessionsAsync or its retention helpers."
      - path: "AutoQAC/Services/Cleaning/CleaningOrchestrator.cs"
        issue: "Lines 713-743 create and pass retention progress to CleanupOldSessionsAsync, but no upstream data is produced."
      - path: "AutoQAC/ViewModels/ProgressViewModel.cs"
        issue: "Lines 238-252 can render count-only progress, but TotalFiles/FilesCompleted remain default because the retention service does not report them."
    missing:
      - "Report retention count progress after classification and after keep/delete/failure rows, or introduce a dedicated retention progress model."
      - "Add a test proving Cleaning up old backups progresses beyond the initial static state."
  - truth: "Maintainers can verify TEST-04 coverage for permission failures and cleanup deletion failures."
    status: failed
    reason: "The targeted Phase 7 tests pass, but coverage is incomplete for TEST-04: there is no restore permission/UnauthorizedAccess regression and no non-canceled cleanup-deletion-failure regression that proves Warning plus Cleanup deletion failed after retry."
    artifacts:
      - path: "AutoQAC.Tests/Services/BackupFileCopierTests.cs"
        issue: "Covers cancellation, target preservation, progress, and missing source, but not UnauthorizedAccessException/Access denied or target write failure mapping."
      - path: "AutoQAC.Tests/Services/BackupServiceTests.cs"
        issue: "Covers missing target directory, partial restore, target-folder creation failure, retention current/newest/malformed/canceled paths, but no cleanup deletion failure after retry remains failed."
    missing:
      - "Add restore permission/access-denied or copier UnauthorizedAccessException coverage."
      - "Add CleanupOldSessionsAsync deletion failure coverage where retry also fails and result.Status is Warning with a Cleanup deletion failed row."
---

# Phase 07: Backup Restore & Retention Safety Verification Report

**Phase Goal:** Users can restore backups and run backup/retention work with clear failure reporting, cancellation, and progress while preserving sequential xEdit cleaning.  
**Verified:** 2026-04-29T07:35:33Z  
**Status:** gaps_found  
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | User can restore backups and receive clear row-level failure reporting when target directories are missing, backup files are missing, or target writes/access fail. | ✓ VERIFIED | `BackupService.cs:372-410` recreates target directories, maps directory creation failures to `Target folder creation failed`, maps missing backup files to `Missing backup file`, and maps copier `AccessDenied`/write failures through `MapRestoreFailure`; row labels are constrained by `BackupOperationResults.cs:57-74`. |
| 2 | Restore All continues after individual plugin failures and distinguishes Complete, Partial, Failed, and Canceled with counts. | ✓ VERIFIED | `BackupService.cs:231-250` iterates every plugin and returns `GetRestoreStatus`; `BackupOperationResults.cs:207-217` exposes restored/failed/canceled counts; `BackupServiceTests.cs:300-331` verifies partial restore continues after missing backup. |
| 3 | Restore Selected/All require overwrite confirmation and inline results remain visible for complete/partial/failed/canceled outcomes. | ✓ VERIFIED | `RestoreViewModel.cs:178-195` and `227-245` confirm selected/all restores; `ApplyRestoreResult` at `435-455` maps outcome titles and keeps inline rows; `RestoreWindow.axaml:94-154` renders progress/results inline with `Cancel Restore`. |
| 4 | Active backup/restore copy cancellation deletes only active partial output/temp files and preserves existing restore targets. | ✓ VERIFIED | `BackupFileCopier.cs:62-79` deletes `actualOutputPath` on cancel/failure; `GetOutputPath` uses `.autoqac-tmp` for atomic restore; tests `CopyAsync_CanceledCreateNewCopy_DeletesPartialDestination` and `CopyAsync_CanceledAtomicReplace_PreservesExistingTarget` verify behavior. |
| 5 | Retention cleanup protects the current session, keeps newest configured sessions, retries deletion once, and can return canceled. | ✓ VERIFIED | `BackupService.cs:316-357` excludes current before retention counting and returns Warning on failed rows; `478-512` retries once and treats cancellation distinctly; tests cover current/newest/malformed/canceled paths. |
| 6 | User can distinguish cleanup-deletion failure outcomes for a session. | ⚠️ PARTIAL | The model/service can represent `Warning` and `Cleanup deletion failed` rows (`BackupOperationResults.cs:241-250`, `BackupService.cs:354-357`, `502-505`), and `CleaningSessionResult.cs:121-125` shows a cleanup warning summary. However, no production UI renders retention row reasons, and no test proves non-canceled cleanup deletion failure after retry. |
| 7 | Cleaning-session backups remain per-plugin immediately before xEdit launch and xEdit cleaning remains sequential. | ✓ VERIFIED | In `CleaningOrchestrator.cs`, `await BackupPluginAsync` at line 284 occurs inside the plugin `foreach` before `CleanPluginAsync` at line 385. Grep found no `Task.WhenAll`, `Parallel.ForEachAsync`, or `Task.Run` in the orchestrator source. |
| 8 | Cancel Backup/Cancel Cleanup are separate from xEdit Stop and do not terminate xEdit. | ✓ VERIFIED | `ICleaningOrchestrator.cs:54-58` exposes `CancelBackupOperationAsync`; `CleaningOrchestrator.cs:627-653` no-ops when `_currentProcess` is non-null and cancels only `_backupOperationCts`; `ProgressWindow.axaml:133-148` has separate `Cancel Backup`/`Cancel Cleanup` buttons. |
| 9 | Backup failure choices finalize and report the session instead of leaving cleaning state or progress incomplete. | ✗ FAILED | Code-review CR-01/CR-02 confirmed: `CleaningOrchestrator.cs:315-323` SkipPlugin branch does not add a result/progress row; `324-338` AbortSession returns before `FinishCleaningWithResults`, leaving state completion dependent on finally cleanup only. |
| 10 | Backup and retention progress are visible and data-flowing with file counts/byte progress when available. | ✗ FAILED | Backup copy progress flows (`BackupFileCopier.cs:121-138` → orchestrator progress), but retention cleanup does not: `BackupService.cs:297-357` accepts `progress` but never calls `progress.Report`, so cleanup UI stays at default count text. |
| 11 | Restore safety validates untrusted backup-session metadata before copying. | ✗ FAILED | Code-review CR-03 confirmed: `BackupService.cs:378-403` uses `Path.Combine(sessionDir, entry.FileName)` and `entry.OriginalPath` directly without containment or target validation. |
| 12 | Maintainer can verify SAF-04/TEST-04/PERF-04 clusters including permission and cleanup deletion failures. | ✗ FAILED | Targeted tests pass (103 tests), but grep/read of test files found no permission/UnauthorizedAccess restore/copy test and no non-canceled cleanup deletion failure test; only model labels and cancellation paths cover those terms. |

**Score:** 7/12 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `AutoQAC/Models/BackupOperationResults.cs` | Shared complete/partial/failed/canceled/warning result contracts and concise labels. | ✓ VERIFIED | Contains `BackupOperationStatus`, restore/retention row statuses, all six approved labels, aggregate count helpers. |
| `AutoQAC/Services/Backup/BackupFileCopier.cs` | Managed async copy with cancellation, byte progress, overwrite policy, and safe partial cleanup. | ✓ VERIFIED | Substantive and wired through `IBackupFileCopier`; progress throttled; cancellation/failure cleanup implemented. |
| `AutoQAC/Services/Backup/BackupService.cs` | Structured async backup, restore, and retention behavior. | ⚠️ PARTIAL | Restore/retention contracts are substantive, but metadata path validation is missing and retention progress sink is unused. |
| `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` | Backup/retention progress/cancel integration while preserving sequential cleaning. | ⚠️ PARTIAL | Sequential happy path and backup-cancel path are wired; backup failure Skip/Abort choices have finalization/accounting gaps. |
| `AutoQAC/Models/AppState.cs` / `IStateService.cs` / `StateService.cs` | Separate non-xEdit operation state. | ✓ VERIFIED | `BackupOperationState`, `SetBackupOperation`, and `ClearBackupOperation` exist and are wired through state changes. |
| `AutoQAC/ViewModels/RestoreViewModel.cs` / `RestoreWindow.axaml` | Restore confirmation, progress, cancellation, and inline result rows. | ✓ VERIFIED | Confirmation/cancel/progress/result properties and AXAML bindings exist; restore result rows bind display reasons. |
| `AutoQAC/ViewModels/ProgressViewModel.cs` / `ProgressWindow.axaml` | Cleaning progress binding for backup/retention operation state and cancel commands. | ⚠️ PARTIAL | UI and command wiring exist; retention cleanup source does not produce count progress. |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `IBackupService` | `BackupOperationResults` | Async method contracts use `BackupCreateResult`, `BackupRestoreResult`, `BackupRetentionCleanupResult`. | ✓ VERIFIED | `IBackupService.cs:28-84`. |
| `BackupService` | `IBackupFileCopier` | Backup/restore delegates to `CopyAsync`. | ✓ VERIFIED | `BackupService.cs:113-118` and `398-403`. |
| `CleaningOrchestrator` | `IBackupService.BackupPluginAsync` | Awaited inside plugin loop before xEdit cleaning. | ✓ VERIFIED | `CleaningOrchestrator.cs:284` before `CleanPluginAsync` at `385`. |
| `CleaningOrchestrator` | `IBackupService.CleanupOldSessionsAsync` | Awaited before final session result emission. | ✓ VERIFIED | `CleaningOrchestrator.cs:515-535`. |
| `ProgressViewModel` | `ICleaningOrchestrator.CancelBackupOperationAsync` | `CancelBackupOperationCommand`. | ✓ VERIFIED | `ProgressViewModel.cs:206-210`; AXAML buttons bind the command. |
| `RestoreViewModel` | `IBackupService.RestoreSessionAsync` | Restore All awaits structured result. | ✓ VERIFIED | `RestoreViewModel.cs:241-245`. |
| `BackupService.CleanupOldSessionsAsync` | Progress UI | `IProgress<BackupCopyProgress>` passed from orchestrator. | ✗ NOT WIRED | Parameter exists, but no `progress.Report` call exists in the retention cleanup implementation. |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|--------------------|--------|
| `RestoreWindow.axaml` | `RestoreResults`, `RestoreOutcomeTitle`, `RestoreProgressText` | `RestoreViewModel` applies `BackupRestoreResult` returned by `IBackupService`. | Yes | ✓ FLOWING |
| `ProgressWindow.axaml` | `BackupOperationProgressText` | `AppState.BackupOperation` set by `CleaningOrchestrator` progress callbacks. | Partial | ⚠️ PARTIAL — backup copy bytes flow; retention cleanup progress is static because service never reports count updates. |
| `CleaningSessionResult` | `BackupCleanup` | `CleaningOrchestrator` awaits `CleanupOldSessionsAsync` before `FinishCleaningWithResults`. | Yes | ✓ FLOWING |
| `BackupService.RestorePluginRowAsync` | `backupPath`, `entry.OriginalPath` | Backup session metadata entries. | Unsafe | ✗ HOLLOW SAFETY — data flows, but untrusted metadata is not validated before copy/overwrite. |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Phase 7 targeted test clusters pass. | `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~BackupServiceTests|FullyQualifiedName~RestoreViewModelTests|FullyQualifiedName~CleaningOrchestratorTests|FullyQualifiedName~ProgressViewModelTests"` | Passed: 103, Failed: 0. | ✓ PASS |
| Sequential xEdit launch invariant has no parallel constructs. | Grep/read `CleaningOrchestrator.cs` for `Task.WhenAll`, `Parallel.ForEachAsync`, `Task.Run`, and backup-before-clean order. | No parallel constructs found; backup call precedes `CleanPluginAsync`. | ✓ PASS |
| Code-review blocker CR-01/CR-02 exists. | Source inspection of `CleaningOrchestrator.cs:315-338`. | SkipPlugin lacks result/progress; AbortSession returns before finalization. | ✗ FAIL |
| Retention progress is reported by service. | Grep `BackupService.cs` for `progress.Report`. | No matches in retention cleanup implementation. | ✗ FAIL |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| SAF-04 | 07-01 through 07-05 | User can restore backups with clear failure reporting when directories are missing, permissions fail, or a session partially restores. | ✗ BLOCKED | Normal restore failure/result handling exists, but restore metadata path traversal/arbitrary target overwrite remains unvalidated. Backup failure choices also leave progress/session reporting inconsistent. |
| TEST-04 | 07-01 through 07-05 | Maintainer can verify backup restore safety across missing target directories, permission failures, partial failures, and cleanup deletion failures. | ✗ BLOCKED | Targeted tests pass, but permission failure and non-canceled cleanup deletion failure coverage are absent. |
| PERF-04 | 07-01 through 07-05 | User backup and retention operations remain cancellable and visible without parallelizing xEdit cleaning. | ⚠️ PARTIAL | Sequential xEdit and separate cancellation are verified, but retention progress does not data-flow beyond a static active band. |

No additional Phase 7 requirement IDs were found in `.planning/REQUIREMENTS.md` beyond SAF-04, TEST-04, and PERF-04.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` | 315-323 | Skip path updates state set directly then `continue`s without result publication. | 🛑 Blocker | Progress/session accounting can omit a plugin selected for cleaning. |
| `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` | 324-338 | Early `return` from inside cleaning workflow before final state emission. | 🛑 Blocker | User can be left without final cleaning result/state completion after aborting on backup failure. |
| `AutoQAC/Services/Backup/BackupService.cs` | 378-403 | Untrusted metadata paths used directly for restore source and destination. | 🛑 Blocker | Malformed backup metadata can escape session source containment and overwrite arbitrary targets. |
| `AutoQAC/Services/Backup/BackupService.cs` | 297-357 | Progress parameter accepted but unused. | 🛑 Blocker | Retention progress requirement is wired but hollow. |
| `AutoQAC/Services/Backup/DirectoryBackupSessionDeleter.cs` | 13-17 | Synchronous recursive delete checks cancellation only before delete starts. | ⚠️ Warning | Cancel Cleanup is best-effort once a large directory deletion has begun. |
| `AutoQAC/ViewModels/RestoreViewModel.cs` | 366-428 | Custom progress reporter updates VM synchronously from copy callbacks. | ⚠️ Warning | Possible UI-thread affinity hazard if service callbacks arrive off the UI thread. |
| `AutoQAC/Models/CleaningSessionResult.cs` | 109 | `IsSuccess` ignores backup cleanup Warning/Canceled/Failed. | ⚠️ Warning | Callers using `IsSuccess` can treat cleanup warning/cancel as full success even though summary text calls it out. |

### Human Verification Required

No human verification is requested until blockers are fixed. After gap closure, a manual smoke test should verify RestoreWindow and ProgressWindow copy/spacing/visual affordances for restore partial/canceled rows and Cancel Backup/Cancel Cleanup because visual quality cannot be fully checked by source inspection.

### Gaps Summary

Phase 7 delivers a substantial amount of the planned foundation: structured result models, cancellable copy behavior, atomic restore target preservation, restore UI confirmations/inline rows, separate non-xEdit cancellation controls, and sequential xEdit ordering are present and targeted tests pass. However, the phase goal is not fully achieved because several must-have safety/reporting paths are hollow or broken in production code. The advisory code-review blockers were checked against source and are real: backup failure Skip/Abort paths break final reporting/accounting, and restore trusts unvalidated metadata paths. Retention progress is also wired in UI/orchestrator but not produced by the service, and TEST-04 lacks required permission and cleanup-deletion-failure coverage.

---

_Verified: 2026-04-29T07:35:33Z_  
_Verifier: the agent (gsd-verifier)_
