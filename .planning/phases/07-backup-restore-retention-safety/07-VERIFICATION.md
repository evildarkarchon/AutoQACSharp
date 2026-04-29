---
phase: 07-backup-restore-retention-safety
verified: 2026-04-29T10:10:28Z
status: gaps_found
score: 18/20 must-haves verified
overrides_applied: 0
re_verification:
  previous_status: gaps_found
  previous_score: 16/18
  gaps_closed:
    - "Create-new backup copy failures now preserve existing destination backup files via createdOutput ownership tracking."
    - "Restore metadata now enforces trusted restore-root containment before target directory creation or copy."
  gaps_remaining: []
  regressions:
    - "Normal cleaning progress window still does not handle ProgressViewModel.CloseRequested or dispose the ViewModel on close."
    - "RestoreWindow Delete Session still recursively deletes SelectedSession.SessionDirectory without revalidating it under the loaded backup root."
gaps:
  - truth: "Normal cleaning progress window close/disposal path is wired for completed results."
    status: failed
    reason: "The primary ShowProgressAsync path creates ProgressViewModel and ProgressWindow but never subscribes CloseRequested and never disposes the ViewModel, so the result Close button does not close the normal progress window and subscriptions can remain alive."
    artifacts:
      - path: "AutoQAC/Views/MainWindow.axaml.cs"
        issue: "ShowProgressAsync lines 129-145 lacks the CloseRequested handler and Closed disposal used by the preview path."
      - path: "AutoQAC/ViewModels/ProgressViewModel.cs"
        issue: "CloseCommand only raises CloseRequested; it cannot close the window without a view-side handler."
    missing:
      - "Wire progressViewModel.CloseRequested to progressWindow.Close() in ShowProgressAsync."
      - "Dispose ProgressViewModel when the normal progress window closes."
      - "Add lifecycle regression coverage for the normal cleaning progress window path."
  - truth: "RestoreWindow backup-session deletion cannot recursively delete outside the loaded backup root."
    status: failed
    reason: "DeleteSessionAsync deletes SelectedSession.SessionDirectory directly from mutable ViewModel state without normalizing and checking containment under _backupRoot."
    artifacts:
      - path: "AutoQAC/ViewModels/RestoreViewModel.cs"
        issue: "Lines 301-322 call Directory.Delete(dirToDelete, recursive: true) and surface ex.Message without backup-root containment validation."
    missing:
      - "Validate selected session directory is under the loaded backup root before recursive delete."
      - "Fail closed with concise user-facing copy and log technical details when the session path is unsafe."
      - "Add traversal/sibling-prefix rejection and normal deletion tests."
---

# Phase 07: Backup Restore & Retention Safety Verification Report

**Phase Goal:** Users can restore backups and run backup/retention work with clear failure reporting, cancellation, and progress while preserving sequential xEdit cleaning.  
**Verified:** 2026-04-29T10:10:28Z  
**Status:** gaps_found  
**Re-verification:** Yes — after Wave 8 gap closures (07-10 and 07-11)

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | User can restore backups and receive clear row-level failure reporting when target directories are missing, backup files are missing, access is denied, or target writes fail. | ✓ VERIFIED | `BackupService.RestorePluginRowAsync` returns structured `BackupRestoreRowResult` values with concise `BackupFailureReason` labels; Phase 7 targeted tests passed. |
| 2 | Restore All continues after individual plugin failures and distinguishes Complete, Partial, Failed, and Canceled with counts. | ✓ VERIFIED | `RestoreSessionAsync` loops entries and `GetRestoreStatus` distinguishes complete/partial/canceled/failed; `RestoreViewModel.BuildRestoreSummaryText` includes restored/failed/canceled counts. |
| 3 | Restore Selected/All require overwrite confirmation and inline results remain visible. | ✓ VERIFIED | `RestoreViewModel` confirmation calls precede restore service calls; `ApplyRestoreResult` keeps `RestoreResults` visible inline. |
| 4 | Active backup/restore copy cancellation deletes only active partial output/temp files and preserves existing restore targets. | ✓ VERIFIED | `BackupFileCopier` uses atomic temp restore and ownership-gated cleanup; targeted `BackupFileCopierTests` passed. |
| 5 | Create-new backup copy failures never delete an existing destination backup file. | ✓ VERIFIED | `createdOutput` is set only after destination stream open succeeds and `DeletePartialOutput` returns when false (`BackupFileCopier.cs:33-50`, `164-172`); regression test exists. |
| 6 | Restore metadata cannot redirect restores outside the trusted game Data folder. | ✓ VERIFIED | Restore contracts include `trustedRestoreRoot`; `ValidateRestoreEntry` calls `IsRestoreTargetInsideTrustedRoot` before `Directory.CreateDirectory`/copy; out-of-root, `Data2`, and missing-root tests exist. |
| 7 | Restore UI passes configured game Data folder as trusted restore root. | ✓ VERIFIED | `MainWindow.axaml.cs:177-183` passes `Configuration.GameDataFolder` to `LoadSessionsAsync`; `RestoreViewModel` stores `_trustedRestoreRoot` and passes it to selected/all restore service calls. |
| 8 | Restore commands are disabled when no trusted restore root is loaded. | ✓ VERIFIED | `CanRestorePlugin`/`CanRestoreAll` require `HasTrustedRestoreRoot`; `RestoreCommands_DisabledWhenTrustedRestoreRootMissing` exists. |
| 9 | Retention cleanup protects current session, keeps newest configured sessions, retries deletion once, and reports deletion failures as Warning rows. | ✓ VERIFIED | `CleanupOldSessionsAsync` protects current/newest sessions and `DeleteRetentionCandidateAsync` retries once, returning warning rows on persistent failure. |
| 10 | Retention cleanup progress is data-flowing and visible. | ✓ VERIFIED | `ReportRetentionProgress` emits count progress; `CleaningOrchestrator` maps it to `BackupOperationState`; `ProgressViewModel` renders count/byte text. |
| 11 | Cleaning-session backups remain per-plugin immediately before xEdit launch and xEdit cleaning remains sequential. | ✓ VERIFIED | `CleaningOrchestrator` awaits backup in the plugin `foreach` before `CleanPluginAsync`; source scan found no `Task.WhenAll`, `Parallel.ForEachAsync`, or `Task.Run` under `AutoQAC/Services/Cleaning`. |
| 12 | Cancel Backup/Cancel Cleanup are separate from xEdit Stop and do not terminate xEdit. | ✓ VERIFIED | `CancelBackupOperationAsync` no-ops while `_currentProcess` exists; ProgressWindow has separate `Cancel Backup`/`Cancel Cleanup` buttons. |
| 13 | Backup failure choices finalize and report session state. | ✓ VERIFIED | SkipPlugin publishes a skipped detailed result; AbortSession creates and emits `CleaningSessionResult` before return. |
| 14 | Restore progress callbacks marshal through `IUiDispatcher`. | ✓ VERIFIED | `RestoreViewModel.CreateRestoreProgressReporter` posts `UpdateRestoreProgress` through `_uiDispatcher.Post`. |
| 15 | BackupPluginAsync maps expected backup session directory creation failures to structured failures. | ✓ VERIFIED | `BackupService.BackupPluginAsync` catches expected `Directory.CreateDirectory(sessionDir)` failures and returns `BackupCreateResult`. |
| 16 | CleanupOldSessionsAsync converts cancellation gates into structured canceled rows/counts. | ✓ VERIFIED | `CleanupOldSessionsAsync` catches cancellation and returns `BackupRetentionCleanupResult(Canceled, rows)`. |
| 17 | Maintainer can verify SAF-04/TEST-04/PERF-04 with targeted tests. | ✓ VERIFIED | Targeted Phase 7 cluster passed: 140 tests. |
| 18 | Full automated solution remains green. | ✓ VERIFIED | Rerun `dotnet test "AutoQACSharp.slnx"` passed: 752 AutoQAC.Tests and 59 QueryPlugins.Tests. |
| 19 | Normal cleaning progress window close/disposal path is wired for completed results. | ✗ FAILED | `ShowProgressAsync` does not handle `CloseRequested` or dispose `ProgressViewModel`; the Close command only raises an event. |
| 20 | RestoreWindow backup-session deletion cannot recursively delete outside the loaded backup root. | ✗ FAILED | `DeleteSessionAsync` directly deletes `SelectedSession.SessionDirectory` without `_backupRoot` containment validation. |

**Score:** 18/20 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `AutoQAC/Services/Backup/BackupFileCopier.cs` | Cancellable copy with ownership-safe cleanup. | ✓ VERIFIED | `createdOutput`/`onDestinationOpened` gate partial-output deletion; existing create-new destination regression passes. |
| `AutoQAC/Services/Backup/BackupService.cs` | Structured backup/restore/retention with trusted-root restore validation. | ✓ VERIFIED | `trustedRestoreRoot` flows into restore validation and blocks sibling/out-of-root targets before writes. |
| `AutoQAC/ViewModels/RestoreViewModel.cs` | Restore confirmation/progress/cancel/results and trusted-root propagation. | ⚠️ PARTIAL | Restore path is wired; Delete Session path lacks backup-root containment before recursive delete. |
| `AutoQAC/Views/MainWindow.axaml.cs` | Opens progress/restore windows with complete lifecycle wiring. | ✗ FAILED | Restore root propagation is wired; normal progress-window close/dispose lifecycle is not. |
| `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` | Sequential backup/xEdit/retention orchestration. | ✓ VERIFIED | Backup awaited before xEdit; retention awaited before final session result; no parallel cleaning constructs found. |
| `AutoQAC/ViewModels/ProgressViewModel.cs` / `ProgressWindow.axaml` | Backup/retention progress, cancel controls, result close command. | ⚠️ PARTIAL | Progress/cancel data flows; `CloseCommand` depends on view handler missing in the normal progress path. |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `BackupFileCopier.CopyAsync` | `DeletePartialOutput` | `createdOutput` ownership flag | ✓ WIRED | All cleanup calls pass `createdOutput`; deletion returns when false. |
| `BackupService.ValidateRestoreEntry` | target writes | `IsRestoreTargetInsideTrustedRoot` before `Directory.CreateDirectory` and copy | ✓ WIRED | Trusted-root containment is checked before `targetPath` is used for writes. |
| `MainWindow.axaml.cs` | `RestoreViewModel.LoadSessionsAsync` | current `Configuration.GameDataFolder` | ✓ WIRED | Runtime restore UI supplies the trusted root. |
| `RestoreViewModel` | `IBackupService.RestorePluginAsync` / `RestoreSessionAsync` | `_trustedRestoreRoot` argument | ✓ WIRED | Selected and all restore calls include `_trustedRestoreRoot`. |
| `CleaningOrchestrator` | `IBackupService.BackupPluginAsync` | awaited inside plugin loop before xEdit cleaning | ✓ WIRED | Sequential invariant preserved. |
| `ProgressViewModel.CloseCommand` | normal progress window close | `CloseRequested` event handler in `ShowProgressAsync` | ✗ NOT_WIRED | Preview path subscribes; normal cleaning path does not. |
| `RestoreViewModel.DeleteSessionAsync` | filesystem delete | `_backupRoot` containment validation | ✗ NOT_WIRED | Direct recursive delete uses mutable selected session path. |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|--------------------|--------|
| `RestoreWindow.axaml` | `RestoreResults`, `RestoreOutcomeTitle`, `RestoreProgressText` | `IBackupService` structured restore results and progress callbacks | Yes | ✓ FLOWING |
| `ProgressWindow.axaml` | `BackupOperationProgressText` | `AppState.BackupOperation` set by `CleaningOrchestrator` progress callbacks | Yes | ✓ FLOWING |
| `CleaningSessionResult` | `BackupCleanup` | `CleanupOldSessionsAsync` awaited before finalization | Yes | ✓ FLOWING |
| `BackupService.RestorePluginRowAsync` | `targetPath` | session metadata plus trusted restore root | Yes | ✓ FLOWING + CONTAINED |
| `RestoreViewModel.DeleteSessionAsync` | `dirToDelete` | mutable `SelectedSession.SessionDirectory` | No root guard | ✗ HOLLOW SAFETY CHECK |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Phase 7 targeted test clusters pass. | `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~BackupFileCopierTests\|FullyQualifiedName~BackupServiceTests\|FullyQualifiedName~RestoreViewModelTests\|FullyQualifiedName~CleaningOrchestratorTests\|FullyQualifiedName~ProgressViewModelTests"` | Passed: 140, Failed: 0. | ✓ PASS |
| Full solution tests pass. | `dotnet test "AutoQACSharp.slnx"` | Passed: 752 AutoQAC.Tests + 59 QueryPlugins.Tests. | ✓ PASS |
| Sequential xEdit invariant has no parallel constructs. | Source scan for `Task.WhenAll`, `Parallel.ForEachAsync`, `Task.Run` under `AutoQAC/Services/Cleaning`. | No matches. | ✓ PASS |
| Create-new existing destination preservation. | Source/test inspection of `BackupFileCopier` and `CopyAsync_CreateNewDestinationAlreadyExists_PreservesExistingDestination`. | Ownership guard exists and test is present. | ✓ PASS |
| Restore trusted-root containment. | Source/test inspection of `IsRestoreTargetInsideTrustedRoot`, Data/Data2 and missing-root tests. | Containment guard exists and tests are present. | ✓ PASS |
| Normal progress Close button closes/disposes window. | Source inspection of `ShowProgressAsync`. | Handler/disposal missing. | ✗ FAIL |
| Delete Session path is contained under backup root. | Source inspection of `DeleteSessionAsync`. | Containment check missing. | ✗ FAIL |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| SAF-04 | 07-01 through 07-11 | User can restore backups with clear failure reporting when directories are missing, permissions fail, or a session partially restores. | ⚠️ PARTIAL | Restore service/reporting is implemented, including trusted-root containment. However RestoreWindow still exposes unsafe backup-session deletion from mutable state, which is a Phase 7 safety regression in the restore/backup management surface. |
| TEST-04 | 07-01 through 07-11 | Maintainer can verify backup restore safety across missing target directories, permission failures, partial failures, and cleanup deletion failures. | ⚠️ PARTIAL | Targeted restore/retention tests pass, but there is no regression coverage for unsafe `DeleteSessionAsync` path rejection or normal progress-window close/dispose lifecycle. |
| PERF-04 | 07-01 through 07-11 | User backup and retention operations remain cancellable and visible without parallelizing xEdit cleaning. | ⚠️ PARTIAL | Cancellable visible backup/retention work and sequential cleaning are verified; the normal progress results Close path remains unwired and can leave subscriptions alive after the progress window closes. |

No additional Phase 7 requirement IDs were found in `.planning/REQUIREMENTS.md` beyond SAF-04, TEST-04, and PERF-04.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `AutoQAC/Views/MainWindow.axaml.cs` | 129-145 | Normal progress window lacks `CloseRequested` handler and ViewModel disposal. | 🛑 Blocker | User result Close button does not close the normal progress window; subscriptions remain alive. |
| `AutoQAC/ViewModels/RestoreViewModel.cs` | 301-322 | Direct recursive delete from mutable UI state without backup-root containment. | 🛑 Blocker | Unsafe/stale selected session path can delete outside the backup root. |
| `AutoQAC/Services/Backup/BackupService.cs` | 703-717 | Restored+canceled sessions aggregate as Partial. | ℹ️ Info | Code-review CR-01 is real relative to the review text, but Plan 07-09 explicitly locked restored+non-restored as Partial while still showing canceled counts. Not counted as a blocker. |

### Human Verification Required

No human verification is requested while blockers remain. After gaps are fixed, manually smoke-test RestoreWindow and ProgressWindow visual affordances because hit-testing/visual quality cannot be fully proven by source inspection.

### Gaps Summary

Wave 8 closed the two previously recorded filesystem blockers: create-new backup copy failures now preserve existing destination files, and restore metadata is constrained to the configured trusted Data folder. Targeted and full test suites pass, and sequential xEdit cleaning is preserved.

Phase 7 still should not proceed because two independently verified code-review findings remain real and affect Phase 7 user-facing safety/progress surfaces: the normal cleaning progress window Close/disposal path is unwired, and RestoreWindow Delete Session can recursively delete outside the loaded backup root from mutable selected-session state.

---

_Verified: 2026-04-29T10:10:28Z_  
_Verifier: the agent (gsd-verifier)_
