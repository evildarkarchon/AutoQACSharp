---
phase: 08
slug: cleaning-orchestrator-decomposition
status: verified
nyquist_compliant: true
wave_0_complete: true
created: 2026-04-29
updated: 2026-04-30
---

# Phase 08 - Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 + FluentAssertions 8.8.0 + NSubstitute 5.3.0 (.NET 10 / C# 13) |
| **Config file** | `AutoQAC.Tests/AutoQAC.Tests.csproj` and `QueryPlugins.Tests/QueryPlugins.Tests.csproj` (coverlet Cobertura collection enabled) |
| **Quick run command** | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Cleaning" --nologo` |
| **Full suite command** | `dotnet test AutoQACSharp.slnx --nologo` |
| **Estimated runtime** | ~10s quick (Cleaning subset) / ~20s full suite on the audit host |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Cleaning" --nologo` or the narrower command listed for that task.
- **After every plan wave:** Run `dotnet test AutoQACSharp.slnx --nologo`.
- **Before `/gsd-verify-work`:** Full suite must be green.
- **Max feedback latency:** 20s observed for the full suite during this audit.

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 08-01-01 | 01 | 0 | REF-01 | T-08-01 | Characterization tests pin current cleaning outcomes before refactor work. | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningOrchestratorTests" --nologo` | yes | green |
| 08-01-02 | 01 | 0 | REF-01 | T-08-12 | `ICleaningOrchestrator` public surface snapshot locks the service contract. | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ICleaningOrchestrator_PublicSurface_MatchesLockedSnapshot" --nologo` | yes | green |
| 08-02-01 | 02 | 1 | REF-01 | T-08-02, T-08-14 | `ICleaningPreflight` contract tests cover idempotence, row mapping, MO2 policy facts, and enum mapping. | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningPreflightTests" --nologo` | yes | green |
| 08-02-02 | 02 | 1 | REF-01 | T-08-02, T-08-14 | Facade and dry-run paths use shared preflight behavior without diverging row semantics. | integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Cleaning" --nologo` | yes | green |
| 08-03-01 | 03 | 2 | REF-01 | T-08-04, T-08-05, T-08-15 | Backup coordinator contract covers cancellation, failed-choice mapping, metadata, retention, and MO2 backup skip. | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~BackupSessionCoordinatorTests" --nologo` | yes | green |
| 08-03-02 | 03 | 2 | REF-01 | T-08-04, T-08-05, T-08-15 | Orchestrator delegates backup session flow through the coordinator while preserving session finalization. | integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Cleaning" --nologo` | yes | green |
| 08-04-01 | 04 | 3 | REF-01 | T-08-06, T-08-07, T-08-08, T-08-13 | Termination coordinator tests cover stop, force-stop, self-PID refusal, hang forwarding, and reset behavior. | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningTerminationCoordinatorTests" --nologo` | yes | green |
| 08-04-02 | 04 | 3 | REF-01 | T-08-06, T-08-07, T-08-08, T-08-13 | Facade termination delegation preserves Phase 5 two-stage stop and unsafe-log-read protection. | integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Cleaning" --nologo` | yes | green |
| 08-05-01 | 05 | 4 | REF-01 | T-08-09, T-08-10 | Runner/finalizer tests cover attempt/retry, log offset capture, termination-aware log reads, and result assembly. | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~PluginCleaningRunnerTests|FullyQualifiedName~PluginResultFinalizerTests" --nologo` | yes | green |
| 08-05-02 | 05 | 4 | REF-01 | T-08-09, T-08-10 | Orchestrator delegates plugin execution and finalization without changing launch or result semantics. | integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Cleaning" --nologo` | yes | green |
| 08-06-01 | 06 | 5 | REF-01 | T-08-12 | Final facade cleanup keeps the public surface stable and collaborator responsibilities isolated. | integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningOrchestratorTests" --nologo` | yes | green |
| 08-06-02 | 06 | 5 | REF-01 | T-08-11, T-08-12 | Source-level guards prevent parallel plugin cleaning and detect public surface drift. | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Cleaning_Source_NoFileParallelizesPluginLoop|FullyQualifiedName~ICleaningOrchestrator_PublicSurface_MatchesLockedSnapshot|FullyQualifiedName~CleaningOrchestrator_Source_DoesNotParallelizePluginCleaning" --nologo` | yes | green |
| 08-07-01 | 07 | 6 | REF-01 | T-08-13, T-08-14, T-08-15 | Startup-window Stop tests cover preflight and orphan-cleanup cancellation before xEdit launch. | unit | `dotnet test AutoQACSharp.slnx --nologo --filter "FullyQualifiedName~StopCleaningAsync_DuringPreflight|FullyQualifiedName~StopCleaningAsync_DuringOrphanCleanup"` | yes | green |
| 08-07-02 | 07 | 6 | REF-01 | T-08-13, T-08-15 | Session CTS is published before startup awaits and cancellation is honored before plugin-loop entry. | integration | `dotnet test AutoQACSharp.slnx --nologo --filter "FullyQualifiedName~CleaningOrchestratorTests"` | yes | green |
| 08-07-03 | 07 | 6 | REF-01 | T-08-11, T-08-12 | Regression sweep keeps source guard and public-surface snapshot green after startup cancellation fix. | regression | `dotnet test AutoQACSharp.slnx --nologo` | yes | green |
| 08-08-01 | 08 | 6 | REF-01 | T-08-16, T-08-17, T-08-18 | Finalizer tests cover failed-runner, exception-log, and skipped-path status/success cross-products. | unit | `dotnet test AutoQACSharp.slnx --nologo --filter "FullyQualifiedName~PluginResultFinalizerTests"` | yes | green |
| 08-08-02 | 08 | 6 | REF-01 | T-08-16, T-08-17 | AlreadyClean promotion requires a successful cleaned runner result; Success is derived from final status. | unit | `dotnet test AutoQACSharp.slnx --nologo --filter "FullyQualifiedName~PluginResultFinalizerTests"` | yes | green |
| 08-08-03 | 08 | 6 | REF-01 | T-08-12 | Public-surface and full-suite regression checks stay green after finalizer fix. | regression | `dotnet test AutoQACSharp.slnx --nologo` | yes | green |
| 08-09-01 | 09 | 7 | REF-01 | T-08-09-01, T-08-09-02 | Concurrent-start regression proves a second `StartCleaningAsync` call is rejected. | unit | `dotnet test AutoQACSharp.slnx --nologo --filter "FullyQualifiedName~StartCleaningAsync_WhenSessionAlreadyActive_ShouldRejectSecondStartAndKeepFirstSessionCancellable"` | yes | green |
| 08-09-02 | 09 | 7 | REF-01 | T-08-09-01, T-08-09-02 | Active session guard prevents `_cleaningCts` overwrite while keeping the first session cancellable. | integration | `dotnet test AutoQACSharp.slnx --nologo --filter "FullyQualifiedName~CleaningOrchestratorTests"` | yes | green |
| 08-09-03 | 09 | 7 | REF-01 | T-08-11 | Source guard and orchestrator regression sweep confirm no parallel constructs were introduced. | regression | `dotnet test AutoQACSharp.slnx --nologo --filter "FullyQualifiedName~CleaningOrchestratorTests"` | yes | green |
| 08-10-01 | 10 | 7 | REF-01 | T-08-10-01, T-08-10-02, T-08-10-03 | Detected file-load-order games with missing `LoadOrderPath` fail preflight before rows are built. | unit | `dotnet test AutoQACSharp.slnx --nologo --filter "FullyQualifiedName~PrepareAsync_UnknownGameDetectedAsFileLoadOrderGame_WithMissingLoadOrderPath_Throws|FullyQualifiedName~PrepareAsync_UnknownGameDetectedAsMutagenSupportedGame_WithMissingLoadOrderPath_Succeeds"` | yes | green |
| 08-10-02 | 10 | 7 | REF-01 | T-08-10-01, T-08-10-02, T-08-10-03 | Post-detection load-order validation preserves generic invalid-configuration messaging and Mutagen-supported behavior. | unit | `dotnet test AutoQACSharp.slnx --nologo --filter "FullyQualifiedName~CleaningPreflightTests"` | yes | green |
| 08-10-03 | 10 | 7 | REF-01 | T-08-10-01, T-08-10-02 | Focused preflight and orchestrator load-order regressions remain green. | regression | `dotnet test AutoQACSharp.slnx --nologo --filter "FullyQualifiedName~CleaningPreflightTests|FullyQualifiedName~StartCleaningAsync_ShouldNotRequireLoadOrderPath_WhenGameTypeIsMutagenSupported|FullyQualifiedName~StartCleaningAsync_ShouldThrow_WhenNonMutagenGameMissingLoadOrderPath"` | yes | green |

*Status: green = command passed during phase execution and remains covered by the 2026-04-30 validation audit.*

---

## Wave 0 Requirements

- [x] `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs` - characterization gaps, dry-run/preflight equivalence, public-surface snapshot, sequential/source guards, startup Stop, and concurrent Start coverage are present.
- [x] `AutoQAC.Tests/Services/Cleaning/CleaningPreflightTests.cs` - shared preflight contract, MO2 policy facts, idempotence, warning mapping, state mutation guard, and detected load-order validation coverage are present.
- [x] `AutoQAC.Tests/Services/Cleaning/BackupSessionCoordinatorTests.cs` - backup session creation/run/finalization, cancellation, retention, failed-choice mapping, and MO2 skip coverage are present.
- [x] `AutoQAC.Tests/Services/Cleaning/CleaningTerminationCoordinatorTests.cs` - stop/force-stop escalation, self-PID refusal, `LastTerminationResult`, hang-monitor forwarding, and reset coverage are present.
- [x] `AutoQAC.Tests/Services/Cleaning/PluginCleaningRunnerTests.cs` and `PluginResultFinalizerTests.cs` - attempt/retry, log offset capture, termination-aware log reads, and final status/success coverage are present.

xUnit, FluentAssertions, NSubstitute, and coverlet were already installed; no framework installation or new test files were required by this audit.

---

## Manual-Only Verifications

All Phase 8 requirements have automated verification. Optional real-xEdit smoke testing can still be performed before release, but it is not a Phase 8 Nyquist gap because Phase 8 was an internal refactor with behavior pinned by automated characterization and regression tests.

---

## Validation Audit 2026-04-30

| Metric | Count |
|--------|-------|
| Gaps found | 0 |
| Resolved by new tests | 0 |
| Escalated | 0 |
| Validation rows refreshed | 24 |
| Test files generated | 0 |

### Commands Run

| Command | Result |
|---------|--------|
| `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Cleaning" --nologo` | passed: 198 tests |
| `dotnet test AutoQACSharp.slnx --nologo` | passed: 888 tests |

---

## Validation Sign-Off

- [x] All tasks have automated verify commands or completed Wave 0 dependencies.
- [x] Sampling continuity: no 3 consecutive tasks without automated verification.
- [x] Wave 0 covers all previously pending references.
- [x] No watch-mode flags; `dotnet test` commands run once and exit.
- [x] Feedback latency < 90s full suite target; latest full suite completed in ~20s.
- [x] `nyquist_compliant: true` set in frontmatter.
- [x] Public `ICleaningOrchestrator` surface guard is mapped and green.
- [x] Sequential cleaning invariant guard is mapped and green.

**Approval:** approved 2026-04-30
