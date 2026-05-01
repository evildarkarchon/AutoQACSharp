---
phase: 12
slug: process-stop-verification-progress-flow-closure
status: passed
nyquist_compliant: true
wave_0_complete: true
created: 2026-05-01
---

# Phase 12 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 + FluentAssertions + NSubstitute |
| **Config file** | `AutoQAC.Tests/AutoQAC.Tests.csproj` |
| **Quick run command** | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~ProgressViewModelTests` |
| **Full suite command** | `dotnet test AutoQACSharp.slnx` |
| **Estimated runtime** | Targeted: < 60 seconds; full suite: project-dependent |

---

## Sampling Rate

- **After every task commit:** Run the task's targeted `dotnet test ... --filter ...` command.
- **After every plan wave:** Run `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ProgressViewModelTests|FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~ProcessExecutionIntegrationTests|FullyQualifiedName~ProcessExecutionServiceTests|FullyQualifiedName~JsonPidStoreTests"`.
- **Before `/gsd-verify-work`:** Run `dotnet test AutoQACSharp.slnx` and record any unrelated pre-existing failures explicitly in `12-VERIFICATION.md`.
- **Max feedback latency:** Targeted checks should complete within one executor feedback loop.

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 12-01-01 | 01 | 1 | SAF-01, SAF-02 | T-12-01 / T-12-02 | Shared safe stop copy and explicit labels | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~MainWindowViewModelTests` | ✅ | ✅ green |
| 12-01-02 | 01 | 1 | SAF-01, SAF-02 | T-12-01 / T-12-02 | Main Stop uses shared labels/copy | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~MainWindowViewModelTests` | ✅ | ✅ green |
| 12-02-01 | 02 | 2 | SAF-01, SAF-02, TEST-01 | T-12-01 / T-12-02 | Progress Stop confirmation and failed force warning | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~ProgressViewModelTests` | ✅ | ✅ green |
| 12-02-02 | 02 | 2 | SAF-02, TEST-01 | T-12-02 | Hang Kill skips confirmation but reports force failure | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~ProgressViewModelTests` | ✅ | ✅ green |
| 12-03-01 | 03 | 3 | REF-04, TEST-01 | T-12-03 | Existing process/PID evidence remains green | integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ProcessExecutionIntegrationTests|FullyQualifiedName~ProcessExecutionServiceTests|FullyQualifiedName~JsonPidStoreTests"` | ✅ | ✅ green |
| 12-03-02 | 03 | 3 | SAF-01, SAF-02, REF-04, TEST-01 | T-12-04 | Verification maps evidence without rewriting old artifacts | docs/test | `dotnet test AutoQACSharp.slnx` | ✅ | ✅ green |

---

## Wave 0 Requirements

- [x] `.planning/phases/12-process-stop-verification-progress-flow-closure/12-VERIFICATION.md` — created by Plan 03 after targeted and full-suite commands were run.

---

## Manual-Only Verifications

All phase behaviors have automated verification. No Avalonia.Headless/manual UI infrastructure is required by this phase.

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references
- [x] No watch-mode flags
- [x] Feedback latency remains bounded by targeted `dotnet test` filters
- [x] `nyquist_compliant: true` set in frontmatter after execution evidence is complete

**Approval:** passed. Plan 12-03 recorded targeted ViewModel, process/PID, combined targeted, and full solution evidence in `12-VERIFICATION.md`; no unrelated full-suite failures were observed.
