---
phase: 07
slug: backup-restore-retention-safety
status: draft
nyquist_compliant: true
wave_0_complete: true
created: 2026-04-29
---

# Phase 07 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 + FluentAssertions 8.8.0 + NSubstitute 5.3.0 |
| **Config file** | `AutoQAC.Tests/AutoQAC.Tests.csproj` |
| **Quick run command** | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupServiceTests` |
| **Full suite command** | `dotnet test AutoQACSharp.slnx` |
| **Estimated runtime** | ~60 seconds |

---

## Sampling Rate

- **After every task commit:** Run the task-specific `<automated>` command.
- **After every plan wave:** Run `dotnet test AutoQACSharp.slnx`.
- **Before `/gsd-verify-work`:** Full suite must be green.
- **Max feedback latency:** 60 seconds for targeted tests, full suite before phase completion.

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 07-01-01 | 01 | 1 | SAF-04/PERF-04 | T-07-01-01 | Partial copy deleted on cancellation | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupFileCopierTests` | ✅ | ⬜ pending |
| 07-01-02 | 01 | 1 | SAF-04/TEST-04 | T-07-01-02 | Restore outcomes carry concise reasons | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupOperationResultTests` | ✅ | ⬜ pending |
| 07-02-01 | 02 | 2 | SAF-04/TEST-04 | T-07-02-01 | Restore continues after failures | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupServiceTests` | ✅ | ⬜ pending |
| 07-02-02 | 02 | 2 | SAF-04/PERF-04 | T-07-02-02 | Retention protects current/newest sessions | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupServiceTests` | ✅ | ⬜ pending |
| 07-03-01 | 03 | 3 | PERF-04 | T-07-03-01 | Backup remains per-plugin before xEdit | integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~CleaningOrchestratorTests` | ✅ | ⬜ pending |
| 07-03-02 | 03 | 3 | PERF-04 | T-07-03-02 | Backup/retention progress is visible and cancellable | vm/integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningOrchestratorTests|FullyQualifiedName~ProgressViewModelTests"` | ✅ | ⬜ pending |
| 07-04-01 | 04 | 3 | SAF-04 | T-07-04-01 | Restore confirmation before overwrite | vm | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~RestoreViewModelTests` | ✅ | ⬜ pending |
| 07-04-02 | 04 | 3 | SAF-04/TEST-04 | T-07-04-02 | Inline restore result rows show concise reasons | vm | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~RestoreViewModelTests` | ✅ | ⬜ pending |
| 07-05-01 | 05 | 4 | SAF-04/PERF-04/TEST-04 | T-07-05-01 | UI/result wiring preserves concise outcomes | integration | `dotnet test AutoQACSharp.slnx` | ✅ | ⬜ pending |

---

## Wave 0 Requirements

Existing infrastructure covers all phase requirements. Tests are added in the plans that introduce each contract.

---

## Manual-Only Verifications

All phase behaviors have automated verification through service, ViewModel, and source-level AXAML assertions. No Avalonia.Headless project exists.

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references
- [x] No watch-mode flags
- [x] Feedback latency < 60s for targeted tests
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** approved 2026-04-29
