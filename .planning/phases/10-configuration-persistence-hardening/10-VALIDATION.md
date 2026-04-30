---
phase: 10
slug: configuration-persistence-hardening
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-04-30
---

# Phase 10 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3, FluentAssertions 8.8.0, NSubstitute 5.3.0, Microsoft.NET.Test.Sdk 18.0.1 (verified `dotnet list package`) |
| **Config file** | `AutoQAC.Tests/AutoQAC.Tests.csproj` (coverage and package settings embedded) |
| **Quick run command** | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~Configuration --nologo` |
| **Full suite command** | `dotnet test AutoQACSharp.slnx --nologo` |
| **Estimated runtime** | Quick: ~10s · Full: ~60s (existing project averages) |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~Configuration --nologo`
- **After every plan wave:** Run `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --nologo`
- **Before `/gsd-verify-work`:** `dotnet test AutoQACSharp.slnx --nologo` must be green
- **Max feedback latency:** 60 seconds

---

## Per-Task Verification Map

> Filled in once plans are written. Each entry maps a task to the test file + automated command that proves it. Plan agent MUST update this table during planning.

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 10-01-01 | 01 | 1 | PERF-03 | — | N/A (in-memory clone, no untrusted input) | unit/model | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~UserConfigurationCopyTests --nologo` | ❌ W0 | ⬜ pending |
| 10-02-01 | 02 | 1 | REF-03, TEST-03 | T-10-01 | Service-owned coordinator rejects raw exceptions to ViewModels | unit/coordinator | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~ConfigPersistenceCoordinatorTests --nologo` | ❌ W0 | ⬜ pending |
| 10-04-01 | 04 | 3 | TEST-03 | T-10-02 | Pre-cleaning flush failure blocks xEdit launch path | unit/integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~CleaningPreflight --nologo` | ✅ existing | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

> **Planner instruction:** Replace this stub table with one row per task in the plans you produce. Do not leave the stub rows; they are illustrative only.

---

## Wave 0 Requirements

- [ ] `AutoQAC.Tests/Models/UserConfigurationCopyTests.cs` — RED tests for PERF-03 deep copy / null normalization / YAML-free clone guard.
- [ ] `AutoQAC.Tests/Services/ConfigPersistenceCoordinatorTests.cs` — RED tests for REF-03 / TEST-03 serialized flow and the SPEC race matrix (save/flush/reload overlap, app-save vs external edit, cleaning deferral, invalid/missing YAML, recovery).
- [ ] `AutoQAC.Tests/Services/Fakes/FakeUserConfigFileStore.cs` (or test-private fake) — deterministic write/replace/read/hash/missing-file/invalid-content failure injection.
- [ ] Existing infrastructure (`AutoQAC.Tests/Services/ConfigurationServiceTests.cs`, `ConfigWatcherServiceTests.cs`) — preserved for facade behavior and watcher smoke coverage.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| ViewModel banner/status text presentation for typed persistence failures | REF-03 (UX surface only) | UI text presentation is ViewModel-mapped from typed status; covered indirectly by ViewModel tests but final wording is design-level | Run app, edit settings to invalid path that simulates write failure (or use existing dev hook), verify status text appears and clears after a successful save. |

> All other Phase 10 behaviors have automated verification.

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies listed above
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references in Per-Task Verification Map
- [ ] No watch-mode flags (e.g., `--watch`, `dotnet watch test`)
- [ ] Feedback latency < 60s (quick filter scoped to `Configuration`)
- [ ] `nyquist_compliant: true` set in frontmatter once planner has populated the task map

**Approval:** pending
