---
phase: 05
slug: process-stop-pid-safety
status: partial
nyquist_compliant: false
wave_0_complete: true
created: 2026-04-28
updated: 2026-04-29
---

# Phase 05 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 with FluentAssertions 8.8.0 and NSubstitute 5.3.0 |
| **Config file** | `AutoQAC.Tests/AutoQAC.Tests.csproj` |
| **Quick run command** | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Process|FullyQualifiedName~CleaningOrchestrator|FullyQualifiedName~CleaningCommands"` |
| **Full suite command** | `dotnet test AutoQACSharp.slnx` |
| **Estimated runtime** | ~60 seconds for targeted tests; full suite project-dependent |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Process|FullyQualifiedName~CleaningOrchestrator|FullyQualifiedName~CleaningCommands"`
- **After every plan wave:** Run `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj`
- **Before `/gsd-verify-work`:** `dotnet test AutoQACSharp.slnx` must be green
- **Max feedback latency:** 60 seconds for targeted feedback

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 05-01-01 | 01 | 1 | REF-04 | T-05-01-01 | PID store updates are file-lock protected, corrupt JSON is preserved, and session IDs are injectable | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~JsonPidStore"` | ✅ `AutoQAC.Tests/Services/JsonPidStoreTests.cs` | ✅ green |
| 05-02-01 | 02 | 2 | SAF-01, SAF-02, REF-04 | T-05-02-01 | User cancellation returns `GracePeriodExpired`, preserves PID evidence, and timeout force-kill success is integration-tested; post-kill wait cancellation remains manual-only | unit + integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ProcessExecutionIntegration|FullyQualifiedName~ProcessExecutionService"` | ✅ `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`, `AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs` | ⚠️ partial |
| 05-03-01 | 03 | 3 | SAF-01, SAF-02 | T-05-03-01 | Confirmation/decline/failure outcomes surface through ViewModel and state; unsafe log parsing is blocked | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningOrchestrator|FullyQualifiedName~MainWindowViewModel"` | ✅ `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`, `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs` | ✅ green |
| 05-04-01 | 04 | 4 | TEST-01, REF-04 | T-05-04-01 | Real helper process tests prove timeout, graceful signal, force kill, PID cleanup, and duplicate-instance guard behavior without xEdit | integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~SingleInstanceGuard|FullyQualifiedName~ProcessExecutionIntegration"` | ✅ `AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs`, `AutoQAC.Tests/Services/SingleInstanceGuardTests.cs`, `AutoQAC.Tests/TestProcessHelper/` | ✅ green |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [x] `AutoQAC.Tests/Services/JsonPidStoreTests.cs` — temp-path tests for REF-04.
- [x] `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs` — ViewModel confirmation/decline/failure tests for SAF-01/SAF-02.
- [x] `AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs` — real helper-process tests for TEST-01, plus user-cancellation PID evidence coverage added during this audit.
- [x] `AutoQAC.Tests/TestProcessHelper/AutoQAC.TestProcessHelper.csproj` and `AutoQAC.Tests/TestProcessHelper/Program.cs` — controlled helper executable for process lifecycle tests.

---

## Manual-Only Verifications

| Requirement | Reason | Manual Check |
|-------------|--------|--------------|
| SAF-02: `TerminateProcessAsync(forceKill: true)` returns `ForceKillFailed` when post-kill wait is canceled | Auditor attempts with `sleep 30000` and `spawn-child 30000` helpers both returned `ForceKilled`; the helper exits quickly enough after `Kill(true)` that `WaitForExitAsync` completes before cancellation can surface. Testing the exact failure path requires a production seam or a reliably unkillable/inaccessible process fixture. | Review logs around `[Termination] Force kill wait was canceled` and treat a real-world occurrence as `ForceKillFailed`; add a test if a deterministic process fixture or seam is introduced. |

Optional smoke check: run `dotnet run --project AutoQAC/AutoQAC.csproj`, start a controlled cleaning session, click Stop once, and confirm the copy matches `05-UI-SPEC.md`.

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all missing references
- [x] No watch-mode flags
- [x] Feedback latency < 60s for targeted checks
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** partial — one SAF-02 edge remains manual-only pending a deterministic failure seam.

## Validation Audit 2026-04-29

| Metric | Count |
|--------|-------|
| Gaps found | 2 |
| Resolved | 1 |
| Escalated | 1 |

### Audit Notes

- Added `ExecuteAsync_UserCancellation_ShouldReturnGracePeriodExpiredKeepHelperRunningAndPreservePidEvidence` to cover user Stop semantics with a real helper process.
- Retained the force-kill post-wait cancellation scenario as manual-only because the current public API and helper process behavior do not make the failure deterministic without implementation changes.
- Refreshed stale Wave 0 and per-task statuses from pending to current coverage state.
