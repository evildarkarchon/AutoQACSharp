---
phase: 08
slug: cleaning-orchestrator-decomposition
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-04-29
---

# Phase 08 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 + FluentAssertions 8.8.0 + NSubstitute 5.3.0 (.NET 10 / C# 13) |
| **Config file** | `AutoQAC.Tests/AutoQAC.Tests.csproj` (auto-collects Cobertura coverage) |
| **Quick run command** | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Cleaning" --nologo` |
| **Full suite command** | `dotnet test AutoQACSharp.slnx --nologo` |
| **Estimated runtime** | ~30s quick (Cleaning subset) / ~90s full suite |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Cleaning" --nologo`
- **After every plan wave:** Run `dotnet test AutoQACSharp.slnx --nologo`
- **Before `/gsd-verify-work`:** Full suite must be green
- **Max feedback latency:** 90s

---

## Per-Task Verification Map

> Populated by planner from PLAN.md task IDs. Initial scaffold below — planner expands one row per task.

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 08-01-XX | 01 | 0 | REF-01 | — | Characterization tests pin current outcomes (success/skipped/failed/stopped/left-running/already-clean/backup-canceled/backup-failed-choice/retention-warning/retention-canceled/dry-run) | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningOrchestratorTests" --nologo` | ❌ W0 | ⬜ pending |
| 08-02-XX | 02 | 1 | REF-01 | — | `ICleaningPreflight` returns identical clean/skip rows for both `StartCleaningAsync` and `RunDryRunAsync` paths (D-13–D-16) | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningPreflightTests" --nologo` | ❌ W0 | ⬜ pending |
| 08-03-XX | 03 | 2 | REF-01 | — | `IBackupSessionCoordinator` preserves backup cancellation, retention warning/canceled, MO2 backup-skip semantics from Phase 7 | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~BackupSessionCoordinatorTests" --nologo` | ❌ W0 | ⬜ pending |
| 08-04-XX | 04 | 3 | REF-01 | — | `ICleaningTerminationCoordinator` preserves Phase 5 two-stage stop/force-stop, `LastTerminationResult`, no log parse after unsafe termination | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningTerminationCoordinatorTests" --nologo` | ❌ W0 | ⬜ pending |
| 08-05-XX | 05 | 4 | REF-01 | — | `IPluginCleaningRunner` + `IPluginResultFinalizer` preserve Phase 6 launch argv intent and result construction from process+log+parser+termination state | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~PluginCleaningRunnerTests|FullyQualifiedName~PluginResultFinalizerTests" --nologo` | ❌ W0 | ⬜ pending |
| 08-06-XX | 06 | 5 | REF-01 | — | Facade `CleaningOrchestrator` keeps `ICleaningOrchestrator` surface stable; existing characterization tests still green; sequential xEdit invariant via `ProcessExecutionService` single slot intact | integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningOrchestratorTests" --nologo` | ✅ | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `AutoQAC.Tests/Services/Cleaning/CleaningOrchestratorTests.cs` — fill characterization gaps for left-running, retention warning, retention canceled, dry-run/preflight equivalence, ContinueWithoutBackup branch verification, last-termination-result reset (per RESEARCH.md gap list)
- [ ] `AutoQAC.Tests/Services/Cleaning/CleaningPreflightTests.cs` — RED stubs for shared preflight contract (D-13–D-16), dry-run vs real-run state mutation divergence (D-14), MO2 policy facts (D-16)
- [ ] `AutoQAC.Tests/Services/Cleaning/BackupSessionCoordinatorTests.cs` — RED stubs for create/run/finalize backup session, backup-failed user choice mapping, MO2 skip
- [ ] `AutoQAC.Tests/Services/Cleaning/CleaningTerminationCoordinatorTests.cs` — RED stubs for stop/force-stop escalation, LastTerminationResult, hang-monitor lifecycle, MayProcessStillBeRunning
- [ ] `AutoQAC.Tests/Services/Cleaning/PluginCleaningRunnerTests.cs` and `PluginResultFinalizerTests.cs` — RED stubs for attempt/retry, log offset capture, PluginCleaningResult assembly

*xUnit + FluentAssertions + NSubstitute already installed; no framework install needed.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| User-facing message text stability (D-12) | REF-01 | Test infrastructure asserts result domain, not exact UI strings; concise message wording is verified by reading | Compare `MessageDialogService` calls and `IStateService.UpdateState` payloads in code review against pre-refactor state — no message text rewording allowed unless explicitly approved |
| End-to-end real-xEdit cleaning run | REF-01 | Tests use NSubstitute; no live xEdit execution in CI | After Wave 5 lands, run `dotnet run --project AutoQAC/AutoQAC.csproj`, configure Skyrim SE, perform one full clean and one dry-run on a small mod list, verify identical user-visible behavior to pre-refactor build |

---

## Validation Sign-Off

- [ ] All tasks have automated verify command or Wave 0 dependency
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references (characterization gap fills + collaborator RED stubs)
- [ ] No watch-mode flags (`dotnet test` runs once and exits)
- [ ] Feedback latency < 90s (full suite); < 30s (Cleaning subset)
- [ ] `nyquist_compliant: true` set in frontmatter once planner populates per-task rows and Wave 0 stubs land green
- [ ] Public `ICleaningOrchestrator` surface diff is empty before merge (guard test recommended)
- [ ] Sequential cleaning invariant guard: `ProcessExecutionService` single-slot still in path post-refactor

**Approval:** pending
