---
phase: 09-plugin-refresh-approximation-performance
verified: 2026-04-30T08:14:33Z
status: gaps_found
score: "16/19 must-haves verified"
overrides_applied: 0
gaps:
  - truth: "Targeted approximation updates preserve non-targeted row values."
    status: failed
    reason: "Selected refresh replaces the whole plugin list with only selected target rows before analysis, so non-targeted visible rows and their existing approximation values are removed instead of preserved."
    artifacts:
      - path: "AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs"
        issue: "RefreshSelectedApproximationsAsync builds pendingRows from the selected target snapshot and calls _stateService.SetPluginsToClean(pendingRows) at lines 167-177."
      - path: "AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs"
        issue: "Coordinator selected-refresh tests only assert selected and late-added targets; none preload a non-targeted row and assert it remains with its previous approximation."
    missing:
      - "Update selected-refresh pending/result publication to preserve the existing plugin list and merge only targeted row approximation changes."
      - "Add a coordinator-level regression test that preloads target and non-target rows, runs RefreshSelectedApproximationsAsync for only the target, and asserts the non-target row remains with its prior approximation."
  - truth: "User-visible plugin lists and approximation results remain consistent with the selected game, data folder, skip lists, and current cancellation generation."
    status: failed
    reason: "The selected refresh path can shrink the visible plugin list to the selected targets, which is inconsistent with the loaded game/load-order row set."
    artifacts:
      - path: "AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs"
        issue: "Line 176 uses SetPluginsToClean(pendingRows) during selected approximation refresh, replacing the current list rather than preserving non-targeted rows."
    missing:
      - "Keep the current row set intact during selected approximation refresh; only targeted rows should become pending/available/unavailable."
  - truth: "User can refresh plugin issue approximations with responsive cancellation and narrower refresh scope when only selected or visible plugins need updates."
    status: partial
    reason: "The narrower selected scope and cancellation plumbing exist, but the selected-scope implementation is destructive to non-targeted rows."
    artifacts:
      - path: "AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs"
        issue: "Selected-target analysis is narrowed through target filtering, but pending publication replaces list state with selected targets only."
    missing:
      - "Implement non-destructive selected-scope refresh semantics."
---

# Phase 9: Plugin Refresh & Approximation Performance Verification Report

**Phase Goal:** Users can refresh plugin issue approximations with better cancellation and less redundant work while plugin loading and approximation refresh behavior moves out of the configuration ViewModel.  
**Verified:** 2026-04-30T08:14:33Z  
**Status:** gaps_found  
**Re-verification:** No — initial verification

## Goal Achievement

The implementation achieves the QueryPlugins hot-path performance/cancellation work and moves most refresh workflow ownership into services. However, the selected approximation refresh path has a blocker: it replaces the entire plugin list with the selected target snapshot before analysis. That violates the phase requirement that targeted approximation updates preserve non-targeted row values and undermines user-visible list consistency.

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | User can refresh plugin issue approximations with responsive cancellation and narrower refresh scope when only selected or visible plugins need updates. | ✗ FAILED | `PluginListViewModel` snapshots checked rows and coordinator filters callback results, but `PluginRefreshCoordinator.cs:167-177` replaces the whole state row list with selected `pendingRows`, so selected refresh is destructively narrow. |
| 2 | User can run ITM approximation on large plugins without the detector materializing every override context for each record. | ✓ VERIFIED | `ItmDetector.cs:73-91` streams `ResolveAllSimpleContexts`; no `ResolveAllSimpleContexts(...).ToArray()` match found. |
| 3 | Maintainer can change plugin loading or issue approximation refresh behavior outside `ConfigurationViewModel`. | ✓ VERIFIED | `PluginRefreshCoordinator.cs` owns loading, skip-list application, generation/CTS, and approximation publication. `ConfigurationViewModel.cs` delegates via `_pluginRefreshCoordinator.RefreshForGameAsync` and no longer contains `StartApproximationRefresh`, `RunApproximationRefreshAsync`, `_pluginApproximationCts`, or `_pluginRefreshGeneration`. |
| 4 | User-visible plugin lists and approximation results remain consistent with the selected game, data folder, skip lists, and current cancellation generation. | ✗ FAILED | Full refresh uses generation checks and skip-list application, but selected refresh calls `_stateService.SetPluginsToClean(pendingRows)` with selected targets only at `PluginRefreshCoordinator.cs:167-177`, dropping non-targeted visible rows. |
| 5 | User can cancel large-plugin approximation work while ITM record/context loops are running. | ✓ VERIFIED | `ItmDetector.cs:39` polls inside major-record loop and `ItmDetector.cs:75` polls inside context traversal; `PluginQueryService.cs:84` passes `ct`. |
| 6 | Analyzed plugin counts remain exact; incomplete canceled plugin counts are not published. | ✓ VERIFIED | `PluginIssueApproximationService.cs:81-99` only adds/publishes a result after `Analyse(...)` returns; `OperationCanceledException` is rethrown before callback publication. |
| 7 | ITM detection no longer materializes every override context into an array for each record. | ✓ VERIFIED | `ItmDetector.cs:73` uses `foreach` over `ResolveAllSimpleContexts`; grep found no `ResolveAllSimpleContexts.*ToArray`. |
| 8 | Maintainer has concrete refresh contracts to implement outside ConfigurationViewModel. | ✓ VERIFIED | `IPluginRefreshCoordinator.cs`, `PluginRefreshRequest.cs`, `PluginRefreshStatus.cs`, and `IPluginRefreshCapabilityPolicy.cs` define typed contracts with XML docs. |
| 9 | Selected refresh target sets are snapshots and do not mutate when row selection changes mid-refresh. | ✓ VERIFIED | `PluginListViewModel.cs:133-136` materializes `.ToList()` before awaiting; `PluginListViewModelTests.cs:82-120` covers post-invocation toggle not changing captured targets. |
| 10 | Targeted approximation updates preserve non-targeted row values. | ✗ FAILED | `StateService.MergePluginApproximation` preserves non-targeted values, but the coordinator selected-refresh path first replaces state with selected-only `pendingRows` at `PluginRefreshCoordinator.cs:167-177`. |
| 11 | Plugin rows load and display before background approximation starts. | ✓ VERIFIED | Full refresh calls `_stateService.SetPluginsToClean(rows)` at `PluginRefreshCoordinator.cs:91` before `AnalyzeTargetsAsync` at line 115. |
| 12 | Plugin loading, skip-list application, generation/CTS ownership, and approximation refresh no longer live in ConfigurationViewModel. | ✓ VERIFIED | Coordinator contains `LoadPluginsAsync`, `ApplySkipListStatus`, `Interlocked` generation/CTS handling, and `AnalyzeTargetsAsync`; Configuration VM delegates. |
| 13 | New refresh requests cancel/replace older refresh work and stale results do not alter current rows. | ✓ VERIFIED | `PluginRefreshCoordinator.cs:62-64`, `233-249`, and `320-327` use generation IDs, CTS exchange, and `IsCurrent` before state writes. |
| 14 | User can refresh checked visible plugin rows from a button near the plugin list controls. | ✓ VERIFIED | `MainWindow.axaml:278-284` defines `Refresh selected approximations` bound to `PluginList.RefreshSelectedApproximationsCommand`; `PluginListViewModel.cs:130-151` delegates checked rows to coordinator. |
| 15 | Refresh selected is disabled until a stable plugin list is loaded, approximation is supported, not cleaning, and at least one visible row is checked. | ✓ VERIFIED | `PluginListViewModel.cs:82-83` gates on `HasPlugins`, `!IsCleaning`, `HasSelectedVisiblePlugin`, and `CanRefreshApproximations`; tests cover no rows, unsupported game, and snapshot behavior. |
| 16 | User can cancel an active approximation refresh without a confirmation dialog. | ✓ VERIFIED | `MainWindow.axaml:285-292` binds `Cancel refresh`; `PluginListViewModel.cs:163-167` directly calls `CancelActiveRefresh(Manual)`. No dialog call in this path. |
| 17 | Starting cleaning cancels any active approximation refresh before xEdit cleaning begins. | ✓ VERIFIED | `CleaningCommandsViewModel.cs:137-141` cancels with `CleaningStarted` before progress interaction and `_orchestrator.StartCleaningAsync`; test asserts call order. |
| 18 | Settings reset, ViewModel disposal, or equivalent teardown cancels active refresh work and prevents stale state writes. | ✓ VERIFIED | `ConfigurationViewModel.cs:409` cancels on reset, `ConfigurationViewModel.cs:580-584` cancels on dispose, and coordinator generation checks guard writes. |
| 19 | Full solution tests pass after QueryPlugins, coordinator, ViewModel, and XAML integration. | ✓ VERIFIED | `dotnet test AutoQACSharp.slnx` passed: QueryPlugins.Tests 61/61 and AutoQAC.Tests 842/842. |

**Score:** 16/19 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `QueryPlugins/Detectors/ItmDetector.cs` | Streaming immediate-lower-priority ITM context traversal with cancellation polling | ✓ VERIFIED | Exists, substantive, polls cancellation at lines 18/39/75, streams contexts at line 73, no `.ToArray()` on contexts. |
| `QueryPlugins/PluginQueryService.cs` | Cancellation-aware analysis orchestration | ✓ VERIFIED | `Analyse(..., CancellationToken ct = default)` at line 73; passes `ct` to `_itmDetector.FindItmRecords` at line 84. |
| `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs` | Cancellation token propagation into QueryPlugins analysis | ✓ VERIFIED | Calls `_pluginQueryService.Analyse(target.Plugin, context.LinkCache, context.GameRelease, ct)` at line 83 and rethrows cancellation. |
| `AutoQAC/Services/Plugin/IPluginRefreshCoordinator.cs` | Refresh/cancel coordinator contract | ✓ VERIFIED | Exposes `RefreshForGameAsync`, `RefreshSelectedApproximationsAsync`, `CancelActiveRefresh`, and `StatusChanged`. |
| `AutoQAC/Services/Plugin/PluginRefreshRequest.cs` | Typed refresh request and target DTOs | ✓ VERIFIED | Defines `PluginRefreshRequest` and `PluginRefreshTarget` immutable records. |
| `AutoQAC/Services/Plugin/PluginRefreshStatus.cs` | Typed status/progress outcomes | ✓ VERIFIED | Defines status kinds and canonical display text. |
| `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs` | Service-owned plugin refresh workflow | ⚠️ HOLLOW for selected refresh | Full refresh is substantive and wired, but selected refresh uses `SetPluginsToClean(pendingRows)` for selected targets only, dropping non-targeted rows. |
| `AutoQAC/Services/Plugin/PluginRefreshCapabilityPolicy.cs` | Refresh-scoped game capability policy | ✓ VERIFIED | Supports issue approximation only for Skyrim and Fallout 4 families. |
| `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs` | UI shell delegating refresh workflow | ✓ VERIFIED | Delegates refresh to coordinator; still handles configuration/path UI concerns. |
| `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs` | Refresh selected and cancel commands | ✓ VERIFIED | Commands exist and delegate to coordinator; no direct `PluginIssueApproximationService` reference. |
| `AutoQAC/Views/MainWindow.axaml` | Plugin-list toolbar UI for refresh/cancel actions | ✓ VERIFIED | Toolbar buttons and bindings present at lines 278-292. |
| `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` | Cleaning-start refresh cancellation integration | ✓ VERIFIED | Calls `CancelActiveRefresh(CleaningStarted)` before orchestrator start. |
| `AutoQAC/ViewModels/MainWindowViewModel.cs` | Shared coordinator injection into child ViewModels | ✓ VERIFIED | Passes the optional DI coordinator to Configuration, PluginList, and Commands child VMs. |
| `AutoQAC.Tests/Integration/DependencyInjectionTests.cs` | End-to-end DI registration coverage | ✓ VERIFIED | Asserts coordinator/capability resolve and shared coordinator field wiring. |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `PluginIssueApproximationService` | `PluginQueryService` | Passes caller token into `Analyse` | ✓ WIRED | Exact call at `PluginIssueApproximationService.cs:83`. |
| `ItmDetector` | Mutagen link cache contexts | Streaming `ResolveAllSimpleContexts` enumeration | ✓ WIRED | `foreach` at `ItmDetector.cs:73`; no context array materialization. |
| `ConfigurationViewModel` | `IPluginRefreshCoordinator` | Game/data-folder/load-order refresh requests | ✓ WIRED | Calls `RefreshForGameAsync` at `ConfigurationViewModel.cs:274-275` and `540-541`. |
| `PluginRefreshCoordinator` | `IStateService` | `SetPluginsToClean` and `MergePluginApproximation` | ⚠️ PARTIAL | Full refresh row-first and merges are wired; selected refresh incorrectly uses `SetPluginsToClean(pendingRows)` for selected-only rows. |
| `PluginListViewModel` | `IPluginRefreshCoordinator` | Selected target snapshot request | ✓ WIRED | Calls `RefreshSelectedApproximationsAsync` with a materialized target list at `PluginListViewModel.cs:133-151`. |
| `MainWindow.axaml` | `PluginListViewModel` | Button command bindings | ✓ WIRED | `PluginList.RefreshSelectedApproximationsCommand` and `PluginList.CancelApproximationRefreshCommand` bound in XAML. |
| `CleaningCommandsViewModel.StartCleaningAsync` | `IPluginRefreshCoordinator.CancelActiveRefresh` | Cleaning-start cancel reason before orchestrator start | ✓ WIRED | Call order in `CleaningCommandsViewModel.cs:137-141`; test asserts cancel/progress/orchestrator order. |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|--------------------|--------|
| `ItmDetector.cs` | `PluginIssue` stream | Mutagen plugin records + link cache contexts | Yes | ✓ FLOWING |
| `PluginIssueApproximationService.cs` | `PluginIssueApproximationResult` | QueryPlugins `Analyse` result | Yes | ✓ FLOWING |
| `PluginRefreshCoordinator.RefreshForGameAsync` | `rows` / approximation callbacks | `IPluginLoadingService`, skip list, `IPluginIssueApproximationService` | Yes | ✓ FLOWING |
| `PluginRefreshCoordinator.RefreshSelectedApproximationsAsync` | `pendingRows` / current state rows | Selected target snapshot only | No, non-targeted current-state rows are discarded | ✗ HOLLOW_PROP |
| `PluginListViewModel` | `targets` | Checked visible `PluginListItem` rows | Yes | ✓ FLOWING |
| `MainWindow.axaml` | Button commands | MainWindowViewModel child VM bindings | Yes | ✓ FLOWING |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Phase 9 targeted AutoQAC tests pass | `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~PluginRefreshCoordinator|FullyQualifiedName~PluginListViewModel|FullyQualifiedName~CleaningCommandsViewModel|FullyQualifiedName~DependencyInjection"` | Passed 21/21 | ✓ PASS |
| ITM detector tests pass | `dotnet test "QueryPlugins.Tests/QueryPlugins.Tests.csproj" --filter "FullyQualifiedName~ItmDetector"` | Passed 14/14 | ✓ PASS |
| Full solution tests pass | `dotnet test "AutoQACSharp.slnx"` | QueryPlugins.Tests 61/61 and AutoQAC.Tests 842/842 passed | ✓ PASS |
| Selected refresh preserves non-target rows | Source-level trace of `RefreshSelectedApproximationsAsync` | `SetPluginsToClean(pendingRows)` replaces full list with selected-only rows | ✗ FAIL |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| REF-02 | 09-02, 09-03, 09-04, 09-05 | Maintainer can change plugin loading or issue approximation refresh behavior outside `ConfigurationViewModel`. | ✓ SATISFIED | Coordinator/capability policy own refresh workflow; Configuration VM delegates to coordinator and DI wires shared service. |
| PERF-01 | 09-01, 09-02, 09-03, 09-04, 09-05 | User can refresh plugin issue approximations with better cancellation, reduced redundant load-order work, or narrower target scope. | ✗ BLOCKED | Cancellation and target filtering exist, but selected refresh corrupts visible row scope by replacing state with selected targets only. |
| PERF-02 | 09-01, 09-05 | User can run ITM approximation on large plugins without materializing every override context for each record. | ✓ SATISFIED | Streaming detector implementation with cancellation polling; targeted and full solution tests pass. |

No orphaned Phase 9 requirement IDs were found in `.planning/REQUIREMENTS.md`; REF-02, PERF-01, and PERF-02 are all claimed by plan frontmatter and mapped to Phase 9.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs` | 176 | `SetPluginsToClean(pendingRows)` during selected refresh | 🛑 Blocker | Drops non-targeted rows and their prior approximation values. |
| `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs` | 587-597 | No-op fallback approximation service | ℹ️ Info | Test/compatibility fallback only; production DI supplies real coordinator/services. Not a Phase 9 blocker. |
| Various existing service files | n/a | `return null` optional lookup results | ℹ️ Info | Expected nullable lookup semantics; not stubbed user-visible output. |

### Human Verification Required

Not requested while blocker gaps remain. After the selected-refresh preservation bug is fixed, a human should smoke-test the UI: load a multi-plugin list, uncheck one plugin, click **Refresh selected approximations**, and confirm the unchecked/non-targeted row remains visible with its previous approximation text while checked rows update.

### Gaps Summary

Phase 9 is close but not complete. The main service extraction, cancellation, QueryPlugins streaming, DI wiring, and command UI exist and pass tests. The blocker is specific: selected approximation refresh treats the selected target snapshot as a replacement plugin list instead of a subset of the current list. This defeats the “narrower refresh scope” contract because non-targeted visible rows are removed rather than preserved.

The required fix is to make selected refresh non-destructive: preserve `IStateService.CurrentState.PluginsToClean`, mark/merge only targeted rows as pending or completed, and add a coordinator test that catches non-target row disappearance/regression.

---

_Verified: 2026-04-30T08:14:33Z_  
_Verifier: the agent (gsd-verifier)_
