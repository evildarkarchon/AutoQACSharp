---
phase: 05
slug: process-stop-pid-safety
status: draft
nyquist_compliant: true
wave_0_complete: false
created: 2026-04-28
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
| 05-01-01 | 01 | 1 | REF-04 | T-05-01-01 | PID store updates are file-lock protected and corrupt JSON is preserved | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~JsonPidStore"` | ❌ W0 | ⬜ pending |
| 05-02-01 | 02 | 2 | SAF-01, SAF-02 | T-05-02-01 | User cancellation returns `GracePeriodExpired`; failed force kill returns `ForceKillFailed` | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ProcessExecutionService"` | ✅ existing + W0 new cases | ⬜ pending |
| 05-03-01 | 03 | 3 | SAF-01, SAF-02 | T-05-03-01 | Confirmation/decline/failure outcomes surface through ViewModel and state | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningOrchestrator|FullyQualifiedName~CleaningCommands"` | ✅ existing + W0 new file | ⬜ pending |
| 05-04-01 | 04 | 3 | TEST-01, REF-04 | T-05-04-01 | Real helper process tests prove timeout, force kill, and PID cleanup without xEdit | integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ProcessExecutionIntegration|FullyQualifiedName~SingleInstanceGuard"` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `AutoQAC.Tests/Services/JsonPidStoreTests.cs` — temp-path tests for REF-04.
- [ ] `AutoQAC.Tests/ViewModels/CleaningCommandsViewModelTests.cs` — ViewModel confirmation/decline/failure tests for SAF-01/SAF-02.
- [ ] `AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs` — real helper-process tests for TEST-01.
- [ ] `AutoQAC.Tests/TestProcessHelper/AutoQAC.TestProcessHelper.csproj` and `AutoQAC.Tests/TestProcessHelper/Program.cs` — controlled helper executable for process lifecycle tests.

---

## Manual-Only Verifications

All phase behaviors have automated verification. Optional manual smoke check: run `dotnet run --project AutoQAC/AutoQAC.csproj`, start a controlled cleaning session, click Stop once, and confirm the copy matches `05-UI-SPEC.md`.

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all missing references
- [x] No watch-mode flags
- [x] Feedback latency < 60s for targeted checks
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
