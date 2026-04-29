---
phase: 07
slug: backup-restore-retention-safety
status: passed
nyquist_compliant: true
wave_0_complete: true
updated: 2026-04-29
---

# Phase 07 — Validation Strategy

> Per-phase validation contract for backup restore, backup copy, retention cleanup, progress, cancellation safety, and Wave 5-9 gap-closure regressions.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3, FluentAssertions 8.8.0, NSubstitute 5.3.0 |
| **Config file** | `AutoQAC.Tests/AutoQAC.Tests.csproj` |
| **Phase 7 targeted cluster** | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~BackupFileCopierTests\|FullyQualifiedName~BackupServiceTests\|FullyQualifiedName~BackupPathContainmentTests\|FullyQualifiedName~RestoreViewModelTests\|FullyQualifiedName~CleaningOrchestratorTests\|FullyQualifiedName~ProgressViewModelTests\|FullyQualifiedName~ViewSubscriptionLifecycleTests\|FullyQualifiedName~BackupOperationResultTests"` |
| **Full suite command** | `dotnet test AutoQACSharp.slnx` |
| **Latest targeted result** | 2026-04-29 — passed, 177/177 tests |

---

## Sampling Rate

- **After every task commit:** Run the task-specific targeted `dotnet test` command from the PLAN.md.
- **After every plan wave:** Run the Phase 7 targeted cluster; run `dotnet test AutoQACSharp.slnx` when touched files cross service/UI boundaries.
- **Before `/gsd-verify-work`:** Full solution test suite must be green.
- **Max feedback latency:** one targeted suite per task.

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | Evidence | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|----------|--------|
| 07-01-01 | 01 | 1 | SAF-04, PERF-04 | T-07-01/T-07-02 | Cancellable copy reports progress, deletes only owned partial output, and preserves atomic restore targets. | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupFileCopierTests` | `BackupFileCopierTests` | ✅ green |
| 07-01-02 | 01 | 1 | SAF-04, TEST-04, PERF-04 | T-07-01 | Result contracts encode complete/partial/failed/canceled/warning outcomes and concise labels. | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupOperationResultTests` | `BackupOperationResultTests` | ✅ green |
| 07-02-01 | 02 | 2 | SAF-04, TEST-04 | T-07-02 | Restore continues after per-plugin failures, recreates safe target directories, and maps row failures concisely. | unit/integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupServiceTests` | `RestoreSessionAsync_ContinuesAfterPluginFailure`, restore row tests | ✅ green |
| 07-02-02 | 02 | 2 | SAF-04, TEST-04, PERF-04 | T-07-03 | Retention protects current/newest sessions, retries deletion once, reports warnings/cancel counts. | unit/integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupServiceTests` | `CleanupOldSessionsAsync_*` tests | ✅ green |
| 07-03-01 | 03 | 3 | PERF-04 | T-07-04 | Backup progress/cancel prevents xEdit launch for canceled plugin and keeps cleaning sequential. | service integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~CleaningOrchestratorTests` | `BackupCancellation_DoesNotLaunchXEdit_ForCanceledPlugin`, sequential source guard | ✅ green |
| 07-03-02 | 03 | 3 | SAF-04, PERF-04 | T-07-05 | Retention warning/cancel is emitted before final session completion. | service integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~CleaningOrchestratorTests` | retention cleanup orchestration tests | ✅ green |
| 07-04-01 | 04 | 3 | SAF-04, TEST-04 | T-07-05/T-07-06 | Restore confirmations and inline rows omit technical exception details. | viewmodel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~RestoreViewModelTests` | confirmation/result row tests | ✅ green |
| 07-04-02 | 04 | 3 | SAF-04, PERF-04 | T-07-05 | Restore cancellation disables actions and preserves visible result/progress rows. | viewmodel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~RestoreViewModelTests` | `CancelRestore*`, `DisposeClearsRestoreCancellationSource` | ✅ green |
| 07-05-01 | 05 | 4 | PERF-04 | T-07-06/T-07-07 | Cleaning progress has separate backup/cleanup cancel commands and visible count/byte progress. | viewmodel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~ProgressViewModelTests` | `BackupOperation_ShowsCancelBackup`, `CleanupOperation_ShowsCancelCleanup`, command/dispose tests | ✅ green |
| 07-05-02 | 05 | 4 | SAF-04, TEST-04, PERF-04 | — | Full phase verification remains green and xEdit cleaning remains sequential. | full suite/source guard | `dotnet test AutoQACSharp.slnx` | 07-05 SUMMARY full suite + source guard | ✅ green |
| 07-06-01 | 06 | 5 | SAF-04, TEST-04, PERF-04 | T-07-06-01/T-07-06-02 | Backup failure SkipPlugin publishes skipped result; AbortSession finalizes canceled session. | service integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~CleaningOrchestratorTests` | `BackupFailureChoice_SkipPlugin_PublishesSkippedResultAndCompletesSessionAccounting`, `BackupFailureChoice_AbortSession_FinalizesCanceledSessionWithPreviousResults` | ✅ green |
| 07-07-01 | 07 | 5 | SAF-04, TEST-04 | T-07-07-01/T-07-07-02 | Unsafe restore metadata and unrooted targets are rejected before copy; deletion warning after retry is reported. | unit/integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupServiceTests` | traversal/absolute/unrooted restore tests; `CleanupOldSessionsAsync_DeletionFailureAfterRetry_ReturnsWarning` | ✅ green |
| 07-07-02 | 07 | 5 | TEST-04 | T-07-07-03 | Access denied/write failures map to approved labels without machine-specific ACL setup. | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~BackupServiceTests\|FullyQualifiedName~BackupFileCopierTests"` | `CopyAsync_DestinationDirectoryMissing_ReturnsTargetWriteFailed`, `RestorePluginAsync_AccessDeniedCopyFailure_ReturnsAccessDenied` | ✅ green |
| 07-08-01 | 08 | 6 | PERF-04, TEST-04 | T-07-08-04 | Restore copy progress from worker thread is marshaled through `IUiDispatcher`. | viewmodel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~RestoreViewModelTests` | `RestoreProgressReportedFromWorkerThread_ShouldPostBindableUpdatesToDispatcher` | ✅ green |
| 07-08-02 | 08 | 6 | SAF-04, TEST-04 | T-07-08-02 | Backup session directory creation failure returns structured backup create failure before copy. | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupServiceTests` | `BackupPluginAsync_SessionDirectoryCreationFailure_ReturnsStructuredFailure` | ✅ green |
| 07-08-03 | 08 | 6 | SAF-04, PERF-04 | T-07-08-03 | Retention cancellation gates return structured canceled rows/counts rather than escaping. | unit/integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupServiceTests` | `CleanupOldSessionsAsync_PreDeleteCancellation_ReturnsCanceledRowsAndCounts`, `CleanupOldSessionsAsync_RetryDelayCancellation_ReturnsCanceled` | ✅ green |
| 07-09-01 | 09 | 7 | SAF-04, TEST-04 | T-07-09-01 | Backup creation rejects rooted/traversing plugin names before constructing unsafe destination paths. | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupServiceTests` | `BackupPlugin_FileNameTraversal_ReturnsFailureAndDoesNotEscapeSession`, async traversal/rooted tests | ✅ green |
| 07-09-02 | 09 | 7 | SAF-04, TEST-04 | T-07-09-02/T-07-09-03 | Restore target metadata requires matching plugin file name, local-drive rooted target, and plugin extension policy. | unit/integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupServiceTests` | filename mismatch, extension, sync compatibility, positive restore tests | ✅ green |
| 07-09-03 | 09 | 7 | SAF-04, PERF-04 | T-07-09-04 | Mixed failed+canceled restore sessions preserve canceled aggregate visibility and UI copy. | unit/viewmodel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~BackupServiceTests\|FullyQualifiedName~RestoreViewModelTests"` | `RestoreSessionAsync_FailedThenCanceledRows_ReturnsCanceled`, `RestoreAllCommand_FailedThenCanceledResult_ShowsCanceledSummaryWithFailedCount` | ✅ green |
| 07-10-01 | 10 | 8 | SAF-04, TEST-04 | T-07-10-01/T-07-10-02 | Create-new copy failures never delete existing backup destination; canceled copies still clean owned partial/temp files. | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupFileCopierTests` | `CopyAsync_CreateNewDestinationAlreadyExists_PreservesExistingDestination`, atomic temp cleanup tests | ✅ green |
| 07-11-01 | 11 | 8 | SAF-04, TEST-04 | T-07-11-01/T-07-11-02 | Restore operations are constrained to configured game Data folder and fail closed without trusted root. | unit/viewmodel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~BackupServiceTests\|FullyQualifiedName~RestoreViewModelTests\|FullyQualifiedName~BackupOperationResultTests"` | out-of-root, Data2 sibling, missing-root, root propagation tests | ✅ green |
| 07-12-01 | 12 | 9 | PERF-04, TEST-04 | T-07-12-01/T-07-12-03 | Normal progress result Close button closes window and disposes ProgressViewModel with defense-in-depth lifecycle wiring. | source-level unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~ViewSubscriptionLifecycleTests` | `MainWindowShowProgressAsync_ShouldWireProgressWindowCloseAndDisposal` | ✅ green |
| 07-14-01 | 14 | 9 | SAF-04, TEST-04 | T-07-14-01/T-07-14-04 | Shared `BackupPathContainment.IsContained` owns canonical string-level containment. | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupPathContainmentTests` | 11 `IsContained_*` tests | ✅ green |
| 07-13-01 | 13 | 9 | SAF-04, TEST-04 | T-07-13-01/T-07-13-05 | RestoreWindow Delete Session uses service-layer containment and deleter seam; ViewModel has no recursive delete. | unit/viewmodel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~BackupServiceTests\|FullyQualifiedName~RestoreViewModelTests"` | 6 `DeleteSessionAsync_*` service tests + 5 `DeleteSessionCommand_*` ViewModel tests | ✅ green |
| 07-RF-01 | REVIEW-FIX | 9 | SAF-04, TEST-04 | WR-01 | Single-argument `BackupService(ILoggingService)` constructor still wires default directory deleter for DeleteSessionAsync. | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "DeleteSessionAsync_SingleArgConstructor_UsesDirectoryBackupSessionDeleter"` | `DeleteSessionAsync_SingleArgConstructor_UsesDirectoryBackupSessionDeleter` | ✅ green |
| 07-RF-02 | REVIEW-FIX | 9 | SAF-04, TEST-04 | WR-02 | Already-canceled delete token throws before validation/logging/deletion. | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "DeleteSessionAsync_AlreadyCanceledToken_ThrowsBeforeValidation"` | `DeleteSessionAsync_AlreadyCanceledToken_ThrowsBeforeValidation` | ✅ green |
| 07-RF-03 | REVIEW-FIX | 9 | TEST-04 | WR-03/IN-02 | Restore/delete timestamp copy reuses canonical `FormatSessionTimestamp`; stale helper doc fixed. | viewmodel/unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~RestoreViewModelTests"` | `RestoreViewModelTests` 27/27 per `07-REVIEW-FIX.md` | ✅ green |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 5-9 Gap-Closure Coverage

| Gap Closure | Plans | Automated Evidence | Status |
|-------------|-------|--------------------|--------|
| Backup failure choice finalization | 07-06 | `BackupFailureChoice_SkipPlugin_PublishesSkippedResultAndCompletesSessionAccounting`; `BackupFailureChoice_AbortSession_FinalizesCanceledSessionWithPreviousResults` | COVERED |
| Unsafe backup/restore metadata | 07-07, 07-09, 07-11 | traversal/rooted/mismatch/extension/out-of-root/Data2/missing-root restore and backup tests | COVERED |
| Dispatcher/progress/cancellation gaps | 07-08, 07-12 | worker-thread restore progress dispatcher test; pre-delete retention cancellation test; progress lifecycle source test | COVERED |
| Backup/restore filesystem safety | 07-09, 07-10, 07-11 | create-new existing destination preservation; atomic restore target preservation; trusted restore root containment | COVERED |
| Create-new copy ownership | 07-10 | `CopyAsync_CreateNewDestinationAlreadyExists_PreservesExistingDestination`; atomic temp cleanup test | COVERED |
| Trusted restore root | 07-11 | service out-of-root/Data2/missing-root tests; ViewModel root propagation tests | COVERED |
| Progress lifecycle | 07-12 | `MainWindowShowProgressAsync_ShouldWireProgressWindowCloseAndDisposal` | COVERED |
| Shared containment helper | 07-14 | `BackupPathContainmentTests` 11 behavioral cases | COVERED |
| Service-layer Delete Session containment | 07-13 | `DeleteSessionAsync_*` and `DeleteSessionCommand_*` regressions | COVERED |
| Code-review-fix regressions | REVIEW-FIX | single-arg constructor, already-canceled token, RestoreViewModel formatter test cluster | COVERED |

---

## Manual-Only Verifications

All Phase 7 behaviors have automated service, ViewModel, or source-level AXAML/C# verification. No Avalonia.Headless/manual-only gate is required because the repository does not currently include a UI automation project.

---

## Validation Audit — 2026-04-29

### Audit Inputs

- Plans and summaries `07-01` through `07-14` were reviewed.
- Existing automated tests in `BackupFileCopierTests`, `BackupOperationResultTests`, `BackupServiceTests`, `BackupPathContainmentTests`, `CleaningOrchestratorTests`, `RestoreViewModelTests`, `ProgressViewModelTests`, and `ViewSubscriptionLifecycleTests` were cross-referenced.
- `07-VERIFICATION.md` and `07-REVIEW-FIX.md` were reviewed for latest verification and code-review-fix evidence.

### Action Taken

- Replaced the stale 07-01 through 07-05 pending-only map with a full 07-01 through 07-14 map plus code-review-fix rows.
- Added explicit Wave 5-9 gap-closure rows/evidence for backup failure choice finalization, unsafe metadata, dispatcher/progress/cancellation gaps, filesystem safety, create-new copy ownership, trusted restore root, progress lifecycle, shared containment helper, and service-layer Delete Session containment.
- No new test files were created because the cross-reference found real behavioral coverage for every submitted gap.

### Commands Run During Audit

| Command | Result |
|---------|--------|
| `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~BackupFileCopierTests\|FullyQualifiedName~BackupServiceTests\|FullyQualifiedName~BackupPathContainmentTests\|FullyQualifiedName~RestoreViewModelTests\|FullyQualifiedName~CleaningOrchestratorTests\|FullyQualifiedName~ProgressViewModelTests\|FullyQualifiedName~ViewSubscriptionLifecycleTests\|FullyQualifiedName~BackupOperationResultTests"` | Passed — 177/177 tests |

### Compliance Decision

- **nyquist_compliant:** true
- **Gaps resolved by audit/update:** 4/4
- **New tests added:** none — existing tests are behavioral and were executed.
- **Remaining manual-only items:** none.

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify commands.
- [x] Sampling continuity: every task has targeted automated verification.
- [x] Wave 0 covers all original MISSING references.
- [x] Wave 5-9 gap closures are mapped to automated evidence.
- [x] Code-review-fix regressions are mapped to automated evidence.
- [x] No watch-mode flags.
- [x] Feedback latency bounded by targeted test filters.
- [x] `nyquist_compliant: true` set in frontmatter.

**Approval:** approved 2026-04-29; audit refreshed 2026-04-29.
