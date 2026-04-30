---
phase: 09-plugin-refresh-approximation-performance
verified: 2026-04-30T08:48:38Z
status: gaps_found
score: "19/21 must-haves verified"
overrides_applied: 0
re_verification:
  previous_status: gaps_found
  previous_score: "16/19"
  gaps_closed:
    - "Targeted approximation updates preserve non-targeted row values."
    - "Selected approximation refresh keeps the current visible plugin row set intact."
    - "Selected approximation refresh remains narrow without selected-only row replacement."
  gaps_remaining:
    - "Disable Skip Lists is ignored by coordinator refresh row publication."
    - "Full-list approximation refresh does not publish a terminal status to clear running/cancel UI state."
  regressions: []
gaps:
  - truth: "User-visible plugin lists and approximation results remain consistent with the selected game, data folder, skip lists, and current cancellation generation."
    status: failed
    reason: "The coordinator always applies skip-list status with disableSkipLists: false, and PluginRefreshRequest carries no DisableSkipLists value, so the user's Disable Skip Lists setting cannot affect refreshed rows."
    artifacts:
      - path: "AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs"
        issue: "RefreshForGameAsync calls ApplySkipListStatus(..., disableSkipLists: false, ...) at line 90."
      - path: "AutoQAC/Services/Plugin/PluginRefreshRequest.cs"
        issue: "The request record has GameType/DataFolderPath/LoadOrderPath only, so ConfigurationViewModel cannot pass DisableSkipListsEnabled into the coordinator."
      - path: "AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs"
        issue: "DisableSkipListsEnabled is saved and RefreshPluginsForGameAsync is called, but the refresh request constructed at lines 544-545 omits the setting."
    missing:
      - "Add a DisableSkipLists flag or equivalent config read to the coordinator refresh path."
      - "Pass ConfigurationViewModel.DisableSkipListsEnabled into PluginRefreshRequest, or have PluginRefreshCoordinator read the saved setting."
      - "Add a regression test proving skip-list plugins remain visible/selectable when Disable Skip Lists is enabled."
  - truth: "Full approximation refresh publishes a terminal status so users can tell refresh work is complete and cancel controls clear."
    status: failed
    reason: "RefreshForGameAsync emits LoadingPlugins and per-plugin AnalyzingSelected statuses, but after successful full-list analysis it returns without publishing Idle or another terminal status; PluginListViewModel keeps IsApproximationRefreshRunning true for LoadingPlugins/AnalyzingSelected until a non-running status arrives."
    artifacts:
      - path: "AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs"
        issue: "After AnalyzeTargetsAsync completes at line 115, there is no Publish(...) before the method exits."
      - path: "AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs"
        issue: "OnPluginRefreshStatusChanged sets IsApproximationRefreshRunning true for LoadingPlugins or AnalyzingSelected at lines 281-285 and relies on a later non-running status to clear it."
    missing:
      - "Publish Idle, FullRefreshCompleted, or another terminal non-running status after successful full-list approximation analysis."
      - "Add coordinator/ViewModel regression coverage that a full refresh clears IsApproximationRefreshRunning and hides/disables Cancel refresh after completion."
---

# Phase 9: Plugin Refresh & Approximation Performance Verification Report

**Phase Goal:** Users can refresh plugin issue approximations with better cancellation and less redundant work while plugin loading and approximation refresh behavior moves out of the configuration ViewModel.  
**Verified:** 2026-04-30T08:48:38Z  
**Status:** gaps_found  
**Re-verification:** Yes — after selected-refresh gap closure

## Goal Achievement

The previous selected-refresh blocker is closed: selected approximation refresh now updates matching rows in place through `IStateService.UpdateState`, keeps non-selected rows visible, and has a regression test preserving `PluginIssueApproximation.Available(9, 8, 7)` on `Unselected.esp`.

However, Phase 9 still does not fully achieve the phase goal. The advisory code review findings CR-01 and CR-02 are real behavior gaps in the current codebase: coordinator refresh ignores the existing Disable Skip Lists setting, and full-list approximation refresh does not publish a terminal status that clears the running/cancel UI state.

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | User can refresh plugin issue approximations with responsive cancellation and narrower refresh scope when only selected or visible plugins need updates. | ✓ VERIFIED | Selected refresh snapshots checked visible rows in `PluginListViewModel.cs:137-140`, uses coordinator target filtering in `PluginRefreshCoordinator.cs:325-342`, and the prior destructive selected-list replacement is gone. |
| 2 | User can run ITM approximation on large plugins without the detector materializing every override context for each record. | ✓ VERIFIED | `ItmDetector.cs:73-90` streams `ResolveAllSimpleContexts`; no `.ToArray()` materialization is present. |
| 3 | Maintainer can change plugin loading or issue approximation refresh behavior outside `ConfigurationViewModel`. | ✓ VERIFIED | `PluginRefreshCoordinator.cs` owns loading, skip-list application, generation/CTS, and approximation publication; `ConfigurationViewModel.cs:544-545` delegates to `RefreshForGameAsync`. |
| 4 | User-visible plugin lists and approximation results remain consistent with the selected game, data folder, skip lists, and current cancellation generation. | ✗ FAILED | Generation/data-folder wiring exists, but `PluginRefreshCoordinator.cs:90` hardcodes `disableSkipLists: false`, so refreshed rows do not respect the user's Disable Skip Lists setting. |
| 5 | User can cancel large-plugin approximation work while ITM record/context loops are running. | ✓ VERIFIED | `ItmDetector.cs:39` and `ItmDetector.cs:75` poll the cancellation token; `PluginQueryService.cs:84` passes `ct` into the ITM detector. |
| 6 | Analyzed plugin counts remain exact; incomplete canceled plugin counts are not published. | ✓ VERIFIED | `PluginIssueApproximationService.cs:81-99` publishes only after `Analyse(...)` returns and rethrows `OperationCanceledException`. |
| 7 | ITM detection no longer materializes every override context into an array for each record. | ✓ VERIFIED | Streaming `foreach` over `ResolveAllSimpleContexts` at `ItmDetector.cs:73`; no context-array materialization found. |
| 8 | Maintainer has concrete refresh contracts to implement outside ConfigurationViewModel. | ✓ VERIFIED | `IPluginRefreshCoordinator`, `PluginRefreshRequest`, `PluginRefreshStatus`, and `IPluginRefreshCapabilityPolicy` exist with documented contracts. |
| 9 | Selected refresh target sets are snapshots and do not mutate when row selection changes mid-refresh. | ✓ VERIFIED | `PluginListViewModel.cs:137-140` materializes targets with `.ToList()`; tests cover snapshot behavior. |
| 10 | Targeted approximation updates preserve non-targeted row values. | ✓ VERIFIED | `PluginRefreshCoordinator.cs:178-190` maps current `PluginsToClean` in place and leaves non-targeted rows unchanged; test `RefreshSelectedApproximationsAsync_ShouldPreserveNonSelectedPluginRows` asserts `Unselected.esp` keeps `Available(9, 8, 7)`. |
| 11 | Plugin rows load and display before background approximation starts. | ✓ VERIFIED | `RefreshForGameAsync` calls `_stateService.SetPluginsToClean(rows)` at `PluginRefreshCoordinator.cs:91` before `AnalyzeTargetsAsync` at line 115. |
| 12 | Plugin loading, skip-list application, generation/CTS ownership, and approximation refresh no longer live in ConfigurationViewModel. | ✓ VERIFIED | Coordinator contains `LoadPluginsAsync`, `ApplySkipListStatus`, `Interlocked` generation/CTS handling, and `AnalyzeTargetsAsync`; Configuration VM delegates. |
| 13 | New refresh requests cancel/replace older refresh work and stale results do not alter current rows. | ✓ VERIFIED | `Interlocked.Increment/Exchange/CompareExchange` and `IsCurrent` guards appear in `PluginRefreshCoordinator.cs:62-64`, `248-264`, and `325-342`. |
| 14 | User can refresh checked visible plugin rows from a button near the plugin list controls. | ✓ VERIFIED | `MainWindow.axaml:278-284` binds `Refresh selected approximations` to `PluginList.RefreshSelectedApproximationsCommand`. |
| 15 | Refresh selected is disabled until a stable plugin list is loaded, approximation is supported, not cleaning, and at least one visible row is checked. | ✓ VERIFIED | `PluginListViewModel.cs:86-87` gates on `HasPlugins`, `!IsCleaning`, `HasSelectedVisiblePlugin`, and `CanRefreshApproximations`. |
| 16 | User can cancel an active approximation refresh without a confirmation dialog. | ✓ VERIFIED | `PluginListViewModel.cs:167-170` calls `CancelActiveRefresh(Manual)` directly; XAML exposes the command at `MainWindow.axaml:285-292`. |
| 17 | Starting cleaning cancels any active approximation refresh before xEdit cleaning begins. | ✓ VERIFIED | `CleaningCommandsViewModel.cs:137-141` cancels before progress interaction and `_orchestrator.StartCleaningAsync`. |
| 18 | Settings reset, ViewModel disposal, or equivalent teardown cancels active refresh work and prevents stale state writes. | ✓ VERIFIED | `ConfigurationViewModel.cs:413` cancels on reset, `ConfigurationViewModel.cs:586` cancels on dispose, and coordinator generation checks guard writes. |
| 19 | Full solution tests pass after QueryPlugins, coordinator, ViewModel, and XAML integration. | ✓ VERIFIED | `dotnet test "AutoQACSharp.slnx"` passed: QueryPlugins.Tests 61/61 and AutoQAC.Tests 844/844. |
| 20 | Selected approximation refresh keeps the current visible plugin row set intact. | ✓ VERIFIED | `PluginRefreshCoordinator.cs:185-190` derives replacement rows from current `s.PluginsToClean`; no `_stateService.SetPluginsToClean(pendingRows)` match remains. |
| 21 | Full approximation refresh publishes a terminal status so users can tell refresh work is complete and cancel controls clear. | ✗ FAILED | After full refresh `AnalyzeTargetsAsync` completes at `PluginRefreshCoordinator.cs:115`, no terminal status is published. `PluginListViewModel.cs:281-285` therefore can leave `IsApproximationRefreshRunning` true. |

**Score:** 19/21 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `QueryPlugins/Detectors/ItmDetector.cs` | Streaming immediate-lower-priority ITM context traversal with cancellation polling | ✓ VERIFIED | Exists, substantive, cancellation polls in record/context loops, no context `.ToArray()`. |
| `QueryPlugins/PluginQueryService.cs` | Cancellation-aware analysis orchestration | ⚠️ PARTIAL | ITM cancellation is wired; deleted-reference/navmesh detector calls still lack token propagation (`PluginQueryService.cs:86`, `:88`). Advisory warning, not the blocking gap here. |
| `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs` | Cancellation token propagation into QueryPlugins analysis | ✓ VERIFIED | Calls `_pluginQueryService.Analyse(..., ct)` at line 83 and rethrows cancellation. |
| `AutoQAC/Services/Plugin/IPluginRefreshCoordinator.cs` | Refresh/cancel coordinator contract | ✓ VERIFIED | Contract exposes full refresh, selected refresh, cancel, and status observable. |
| `AutoQAC/Services/Plugin/PluginRefreshRequest.cs` | Typed refresh request and target DTOs | ⚠️ INCOMPLETE | Request exists, but it has no DisableSkipLists field, preventing the UI setting from reaching coordinator refresh. |
| `AutoQAC/Services/Plugin/PluginRefreshStatus.cs` | Typed status/progress outcomes | ⚠️ INCOMPLETE | Selected terminal status exists; no full-refresh terminal status is emitted by the coordinator. |
| `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs` | Service-owned plugin refresh workflow | ✗ FAILED | Substantive and wired, but hardcodes skip-list disabling to false and omits terminal status after full-list analysis. |
| `AutoQAC/Services/Plugin/PluginRefreshCapabilityPolicy.cs` | Refresh-scoped game capability policy | ✓ VERIFIED | Supports issue approximation for Skyrim/Fallout 4 families. |
| `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs` | UI shell delegating refresh workflow | ⚠️ PARTIAL | Delegates refresh workflow, but does not pass `DisableSkipListsEnabled` into the coordinator request. |
| `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs` | Refresh selected and cancel commands | ⚠️ PARTIAL | Commands exist, but running state relies on a terminal status that full refresh never emits. |
| `AutoQAC/Views/MainWindow.axaml` | Plugin-list toolbar UI for refresh/cancel actions | ✓ VERIFIED | Buttons and bindings present. |
| `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` | Cleaning-start refresh cancellation integration | ✓ VERIFIED | Cancels active refresh before cleaning starts. |
| `AutoQAC/ViewModels/MainWindowViewModel.cs` | Shared coordinator injection into child ViewModels | ✓ VERIFIED | Shared coordinator is wired through child VMs. |
| `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs` | Regression coverage for non-target row preservation | ✓ VERIFIED | Contains `RefreshSelectedApproximationsAsync_ShouldPreserveNonSelectedPluginRows` and asserts two rows remain. |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `PluginIssueApproximationService` | `PluginQueryService` | Passes caller token into `Analyse` | ✓ WIRED | Exact call at `PluginIssueApproximationService.cs:83`. |
| `ItmDetector` | Mutagen link cache contexts | Streaming `ResolveAllSimpleContexts` enumeration | ✓ WIRED | `foreach` at `ItmDetector.cs:73`; no context array materialization. |
| `ConfigurationViewModel` | `IPluginRefreshCoordinator` | Game/data-folder/load-order refresh requests | ⚠️ PARTIAL | Calls coordinator, but request does not include Disable Skip Lists setting. |
| `PluginRefreshCoordinator` | `IStateService` | `SetPluginsToClean`, `UpdateState`, `MergePluginApproximation` | ✓ WIRED | Full refresh row-first and selected refresh in-place update/merge are wired. |
| `PluginRefreshCoordinator` | `PluginListViewModel` | StatusChanged controls running/cancel UI state | ✗ NOT_WIRED | Full refresh does not publish a terminal status, so `IsApproximationRefreshRunning` may stay true. |
| `PluginListViewModel` | `IPluginRefreshCoordinator` | Selected target snapshot request | ✓ WIRED | Calls `RefreshSelectedApproximationsAsync` with materialized targets. |
| `MainWindow.axaml` | `PluginListViewModel` | Button command bindings | ✓ WIRED | Refresh selected and cancel bindings present. |
| `CleaningCommandsViewModel.StartCleaningAsync` | `IPluginRefreshCoordinator.CancelActiveRefresh` | Cleaning-start cancel reason before orchestrator start | ✓ WIRED | Call at `CleaningCommandsViewModel.cs:137` before `_orchestrator.StartCleaningAsync`. |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|--------------------|--------|
| `ItmDetector.cs` | `PluginIssue` stream | Mutagen plugin records + link cache contexts | Yes | ✓ FLOWING |
| `PluginIssueApproximationService.cs` | `PluginIssueApproximationResult` | QueryPlugins `Analyse` result | Yes | ✓ FLOWING |
| `PluginRefreshCoordinator.RefreshForGameAsync` | `rows` / approximation callbacks | `IPluginLoadingService`, config skip list, `IPluginIssueApproximationService` | Partially | ⚠️ HOLLOW for Disable Skip Lists setting and terminal status. |
| `PluginRefreshCoordinator.RefreshSelectedApproximationsAsync` | selected target row updates | Current `IStateService.CurrentState.PluginsToClean` plus target snapshot | Yes | ✓ FLOWING |
| `PluginListViewModel` | `targets` | Checked visible `PluginListItem` rows | Yes | ✓ FLOWING |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Phase 9 targeted AutoQAC tests pass | `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~PluginRefreshCoordinator|FullyQualifiedName~PluginListViewModel|FullyQualifiedName~CleaningCommandsViewModel|FullyQualifiedName~DependencyInjection"` | Passed 22/22 | ✓ PASS |
| ITM detector tests pass | `dotnet test "QueryPlugins.Tests/QueryPlugins.Tests.csproj" --filter "FullyQualifiedName~ItmDetector"` | Passed 14/14 | ✓ PASS |
| Full solution tests pass | `dotnet test "AutoQACSharp.slnx"` | QueryPlugins.Tests 61/61 and AutoQAC.Tests 844/844 passed | ✓ PASS |
| Selected refresh preserves non-target rows | Source/test trace | `UpdateState` maps current rows; regression test asserts two rows and preserved `Available(9, 8, 7)` | ✓ PASS |
| Disable Skip Lists flows into refresh rows | Source trace | `disableSkipLists: false` hardcoded, no request field | ✗ FAIL |
| Full refresh clears running/cancel UI | Source trace | No terminal status after `AnalyzeTargetsAsync`; ViewModel needs a non-running status | ✗ FAIL |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| REF-02 | 09-02, 09-03, 09-04, 09-05, 09-06 | Maintainer can change plugin loading or issue approximation refresh behavior outside `ConfigurationViewModel`. | ✓ SATISFIED | Coordinator/capability policy own refresh workflow; Configuration VM delegates. |
| PERF-01 | 09-01, 09-02, 09-03, 09-04, 09-05, 09-06 | User can refresh plugin issue approximations with better cancellation, reduced redundant load-order work, or narrower target scope. | ✗ BLOCKED | Narrow selected refresh now preserves rows, but full refresh can leave cancel/running UI stuck and skip-list consistency is broken. |
| PERF-02 | 09-01, 09-05 | User can run ITM approximation on large plugins without materializing every override context for each record. | ✓ SATISFIED | Streaming detector implementation with cancellation polling; tests pass. |

No orphaned Phase 9 requirement IDs were found in `.planning/REQUIREMENTS.md`; REF-02, PERF-01, and PERF-02 are all claimed by plan frontmatter and mapped to Phase 9.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs` | 90 | `disableSkipLists: false` | 🛑 Blocker | Ignores user Disable Skip Lists setting during refreshed row publication. |
| `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs` | 115 | Full analysis completes without terminal `Publish(...)` | 🛑 Blocker | Cancel/running UI can remain active after work completes. |
| `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs` | 382 | raw `Subject<T>.OnNext` | ⚠️ Warning | Concurrent cancel/progress publication can race observer notification. |
| `QueryPlugins/PluginQueryService.cs` | 86-88 | detector calls without `CancellationToken` | ⚠️ Warning | Cancellation is not propagated into deleted-reference/navmesh detector traversals. |
| `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs` | 598-608 | no-op fallback approximation service | ℹ️ Info | Test/compatibility fallback only; production DI supplies real services. |

### Human Verification Required

None at this stage. Blocker gaps are observable in source and should be fixed before manual UI smoke testing. After fixes, manually verify: load plugins with a skip-list entry, toggle Disable Skip Lists, refresh, and confirm rows and cancel button state behave correctly.

### Gaps Summary

The original 09-06 selected-refresh gap is closed. The phase still cannot pass because two code-review blockers are real unmet phase-goal behaviors:

1. **Disable Skip Lists is ignored during coordinator refresh.** The coordinator owns row refresh now, but the setting does not cross the ViewModel-to-service boundary, so refreshed plugin lists can contradict the user's skip-list preference.
2. **Full approximation refresh has no terminal status.** The UI can show an active cancel-refresh affordance after successful full-list analysis has already completed.

Both gaps need code and regression tests before Phase 9 should proceed.

---

_Verified: 2026-04-30T08:48:38Z_  
_Verifier: the agent (gsd-verifier)_
