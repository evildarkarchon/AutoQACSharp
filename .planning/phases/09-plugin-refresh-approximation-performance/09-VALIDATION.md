---
phase: 09
slug: plugin-refresh-approximation-performance
status: draft
nyquist_compliant: true
wave_0_complete: false
created: 2026-04-30
---

# Phase 09 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 + Microsoft.NET.Test.Sdk 18.0.1 + FluentAssertions 8.8.0 + NSubstitute 5.3.0 |
| **Config file** | Existing `.csproj` test configuration; no new test framework required |
| **Quick run command** | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~PluginRefreshCoordinator|FullyQualifiedName~PluginListViewModel|FullyQualifiedName~StateService"` |
| **Full suite command** | `dotnet test AutoQACSharp.slnx` |
| **Estimated runtime** | ~60 seconds for targeted filters; full suite varies by coverage collection |

---

## Sampling Rate

- **After every task commit:** Run the task-specific targeted `dotnet test` command from the PLAN.md `<verify>` block.
- **After every plan wave:** Run `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj && dotnet test QueryPlugins.Tests/QueryPlugins.Tests.csproj`.
- **Before `/gsd-verify-work`:** `dotnet test AutoQACSharp.slnx` must be green.
- **Max feedback latency:** 60 seconds for targeted commands where possible.

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 09-01-01 | 01 | 1 | REF-02, PERF-01 | T-09-01 / T-09-02 | Coordinator rejects stale writes and publishes row-first state | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~PluginRefreshCoordinator"` | ❌ W0 | ⬜ pending |
| 09-01-02 | 01 | 1 | PERF-01 | T-09-02 | Targeted merge preserves non-targeted rows | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~StateService"` | ✅ | ⬜ pending |
| 09-02-01 | 02 | 2 | PERF-02 | T-09-03 | ITM hot loop honors cancellation without publishing partial counts | unit | `dotnet test QueryPlugins.Tests/QueryPlugins.Tests.csproj --filter "FullyQualifiedName~ItmDetector"` | ✅ | ⬜ pending |
| 09-03-01 | 03 | 2 | REF-02, PERF-01 | T-09-01 / T-09-02 | ViewModels delegate workflow to service and map typed status only | unit + integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~PluginRefreshCoordinator|FullyQualifiedName~DependencyInjection"` | ❌ W0 | ⬜ pending |
| 09-04-01 | 04 | 3 | PERF-01 | T-09-04 | Refresh selected command is disabled for unsupported/no-selection states | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~PluginListViewModel"` | ✅ | ⬜ pending |
| 09-05-01 | 05 | 4 | REF-02, PERF-01, PERF-02 | T-09-01 / T-09-04 | Cleaning cancels refresh before xEdit flow and phase suite passes | integration | `dotnet test AutoQACSharp.slnx` | ✅ | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs` — coordinator generation, cancellation, selected snapshots, skip-list-hidden behavior, unsupported approximation status.
- [ ] Extend `AutoQAC.Tests/ViewModels/PluginListViewModelTests.cs` — refresh selected command gating and selected-target snapshot.
- [ ] Extend `AutoQAC.Tests/Integration/DependencyInjectionTests.cs` — coordinator and capability policy registration.
- [ ] Extend `QueryPlugins.Tests/Detectors/ItmDetectorTests.cs` — cancellation in hot loop and immediate-lower-priority preservation after streaming rewrite.
- [ ] Extend `AutoQAC.Tests/Services/StateServiceTests.cs` — targeted refresh regression proving non-targeted rows keep existing values when single-row merges are used.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Visual placement of `Refresh selected approximations` near plugin-list controls | PERF-01 | Existing repo has no Avalonia.Headless test project | Run `dotnet run --project AutoQAC/AutoQAC.csproj`, inspect plugin-list header, and confirm the CTA appears near `All`, `None`, and `Manage Skip List` without moving global cleaning controls. |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 60s for targeted task checks
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
