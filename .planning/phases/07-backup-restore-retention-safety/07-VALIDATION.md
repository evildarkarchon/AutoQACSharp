---
phase: 07
slug: backup-restore-retention-safety
status: draft
nyquist_compliant: true
wave_0_complete: true
created: 2026-04-29
---

# Phase 07 — Validation Strategy

> Per-phase validation contract for backup restore, backup copy, retention cleanup, progress, and cancellation safety.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3, FluentAssertions 8.8.0, NSubstitute 5.3.0 |
| **Config file** | `AutoQAC.Tests/AutoQAC.Tests.csproj` |
| **Quick run command** | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupServiceTests` |
| **Full suite command** | `dotnet test AutoQACSharp.slnx` |
| **Estimated runtime** | existing full-suite runtime |

---

## Sampling Rate

- **After every task commit:** Run the task-specific targeted `dotnet test` command from the PLAN.md.
- **After every plan wave:** Run `dotnet test AutoQACSharp.slnx` when the wave changes or when touched files cross service/UI boundaries.
- **Before `/gsd-verify-work`:** Full solution test suite must be green.
- **Max feedback latency:** one targeted suite per task.

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 07-01-01 | 01 | 1 | SAF-04, PERF-04 | T-07-01 | partial copy deletion and concise failure reasons | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupFileCopierTests` | ✅ | ⬜ pending |
| 07-01-02 | 01 | 1 | SAF-04, TEST-04, PERF-04 | T-07-01 | result contracts encode complete/partial/failed/canceled without exception text | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupOperationResultTests` | ✅ | ⬜ pending |
| 07-02-01 | 02 | 2 | SAF-04, TEST-04 | T-07-02 | restore continues after per-plugin failures | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupServiceTests` | ✅ | ⬜ pending |
| 07-02-02 | 02 | 2 | SAF-04, TEST-04, PERF-04 | T-07-03 | retention protects current/newest sessions and reports retry/cancel failures | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupServiceTests` | ✅ | ⬜ pending |
| 07-03-01 | 03 | 3 | PERF-04 | T-07-04 | backup progress/cancel prevents xEdit launch for canceled plugin | service integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~CleaningOrchestratorTests` | ✅ | ⬜ pending |
| 07-03-02 | 03 | 3 | SAF-04, PERF-04 | T-07-04 | retention warning/cancel is emitted before final session completion | service integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~CleaningOrchestratorTests` | ✅ | ⬜ pending |
| 07-04-01 | 04 | 3 | SAF-04, TEST-04 | T-07-05 | restore confirmation and inline rows omit technical exception details | viewmodel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~RestoreViewModelTests` | ✅ | ⬜ pending |
| 07-04-02 | 04 | 3 | SAF-04, PERF-04 | T-07-05 | restore cancel disables actions and preserves visible result rows | viewmodel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~RestoreViewModelTests` | ✅ | ⬜ pending |
| 07-05-01 | 05 | 4 | PERF-04 | T-07-06 | cleaning progress has separate backup/cleanup cancel commands | viewmodel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~ProgressViewModelTests` | ✅ | ⬜ pending |
| 07-05-02 | 05 | 4 | SAF-04, TEST-04, PERF-04 | — | full phase verification remains green | full suite | `dotnet test AutoQACSharp.slnx` | ✅ | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

Existing xUnit infrastructure covers all Phase 7 requirements. New test classes may be added inside `AutoQAC.Tests` as part of the same task that creates the corresponding production contract.

---

## Manual-Only Verifications

All phase behaviors have automated service, ViewModel, or source-level AXAML verification. No Avalonia.Headless/manual-only gate is planned because the repository does not currently include a UI automation project.

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify commands.
- [x] Sampling continuity: every task has targeted automated verification.
- [x] Wave 0 covers all MISSING references.
- [x] No watch-mode flags.
- [x] Feedback latency bounded by targeted test filters.
- [x] `nyquist_compliant: true` set in frontmatter.

**Approval:** approved 2026-04-29
