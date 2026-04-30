---
phase: 09-plugin-refresh-approximation-performance
verified: 2026-04-30T09:34:53Z
status: gaps_found
score: "19/22 must-haves verified"
overrides_applied: 0
re_verification:
  previous_status: gaps_found
  previous_score: "19/21"
  gaps_closed:
    - "Disable Skip Lists is ignored by coordinator refresh row publication."
    - "Full-list approximation refresh does not publish a terminal status after successful analysis."
  gaps_remaining: []
  regressions: []
gaps:
  - truth: "User can refresh plugin issue approximations with responsive cancellation and narrower refresh scope when only selected or visible plugins need updates."
    status: failed
    reason: "The coordinator cancels and disposes cancellation token sources owned by still-running refresh tasks, so supersede/manual/cleaning-start cancellation can surface ObjectDisposedException instead of ordinary cooperative cancellation."
    artifacts:
      - path: "AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs"
        issue: "CreateAndActivateGeneration disposes the previous live CTS via CancelAndDispose at lines 254-259 and 390-408."
      - path: "AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs"
        issue: "CancelActiveRefresh exchanges, cancels, and disposes the active CTS at lines 218-237 while the owning async method may still be awaiting plugin loading or approximation analysis."
    missing:
      - "Change external cancellation paths to signal only; let the owning refresh method dispose its linked CTS after awaited work unwinds."
      - "Add regression coverage for cancel/supersede of delayed refresh work completing as cancellation without ObjectDisposedException."
  - truth: "Refresh failures publish a terminal non-running status so users can tell work is over and cancel controls clear."
    status: failed
    reason: "Successful full refresh now publishes FullRefreshCompleted, but the non-cancellation exception path still merges Unavailable rows and exits without Idle/error/terminal status, leaving PluginListViewModel in the last running state."
    artifacts:
      - path: "AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs"
        issue: "The catch block at lines 123-136 logs and merges Unavailable results but publishes no non-running status."
      - path: "AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs"
        issue: "OnPluginRefreshStatusChanged sets IsApproximationRefreshRunning true for LoadingPlugins/AnalyzingSelected and requires a later non-running status to clear it (lines 294-299)."
    missing:
      - "Publish Idle, a typed error status, or another non-running terminal status after recoverable refresh failure handling when the generation is still current."
      - "Add tests for approximation/load/skip-list failure clearing IsApproximationRefreshRunning and disabling cancel refresh."
  - truth: "User-visible plugin lists and approximation results remain consistent with the selected game, data folder, skip lists, and current cancellation generation."
    status: failed
    reason: "Disable Skip Lists now flows, but variant-specific skip lists do not: refresh always fetches GameVariant.None skip lists, unlike cleaning preflight, so TTW/Enderal rows can disagree with what cleaning will skip."
    artifacts:
      - path: "AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs"
        issue: "RefreshForGameAsync calls GetSkipListAsync(request.GameType, token) at line 84 and GetSkipListAsync calls IConfigurationService.GetSkipListAsync(gameType, ct: ct) at lines 309-317, implicitly using GameVariant.None."
      - path: "AutoQAC/Services/Cleaning/CleaningPreflight.cs"
        issue: "Cleaning preflight detects GameVariant from plugin names and passes it to GetSkipListAsync at lines 84 and 120, proving refresh and cleaning use different skip-list semantics."
    missing:
      - "Inject/use IGameDetectionService or equivalent in PluginRefreshCoordinator to detect variants from loaded plugin names before retrieving skip lists."
      - "Pass the detected GameVariant into IConfigurationService.GetSkipListAsync."
      - "Add TTW and Enderal refresh regression tests matching cleaning preflight skip-list decisions."
---

# Phase 9: Plugin Refresh & Approximation Performance Verification Report

**Phase Goal:** Users can refresh plugin issue approximations with better cancellation and less redundant work while plugin loading and approximation refresh behavior moves out of the configuration ViewModel.  
**Verified:** 2026-04-30T09:34:53Z  
**Status:** gaps_found  
**Re-verification:** Yes — after 09-07/09-08 gap closure

## Goal Achievement

The two previously recorded verification gaps are closed in the current codebase:

- `PluginRefreshRequest.DisableSkipLists` exists and `ConfigurationViewModel` passes `DisableSkipLists: DisableSkipListsEnabled` into `RefreshForGameAsync` (`ConfigurationViewModel.cs:543-549`); the coordinator applies `request.DisableSkipLists` at `PluginRefreshCoordinator.cs:90`.
- Successful full-list approximation refresh now publishes `PluginRefreshStatus.FullRefreshCompleted(updated)` after `AnalyzeTargetsAsync` (`PluginRefreshCoordinator.cs:113-121`), and `PluginListViewModel` treats terminal statuses as non-running (`PluginListViewModel.cs:294-299`).

However, the phase goal is still not achieved. The advisory review findings CR-01, CR-02, and CR-03 are real codebase gaps against the roadmap truths for responsive cancellation and list/skip-list consistency.

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | User can refresh plugin issue approximations with responsive cancellation and narrower refresh scope when only selected or visible plugins need updates. | ✗ FAILED | Narrow selected refresh exists, but cancellation is not robust: active CTS instances are disposed by other callers while refresh awaits are still in flight (`PluginRefreshCoordinator.cs:218-237`, `254-259`, `390-408`). |
| 2 | User can run ITM approximation on large plugins without the detector materializing every override context for each record. | ✓ VERIFIED | `ItmDetector.cs:73-90` streams `ResolveAllSimpleContexts`; no per-record `.ToArray()` materialization is present. |
| 3 | Maintainer can change plugin loading or issue approximation refresh behavior outside `ConfigurationViewModel`. | ✓ VERIFIED | `PluginRefreshCoordinator` owns loading, skip-list application, generation/CTS handling, and approximation publication; `ConfigurationViewModel.cs:543-549` delegates refresh. |
| 4 | User-visible plugin lists and approximation results remain consistent with the selected game, data folder, skip lists, and current cancellation generation. | ✗ FAILED | Disable Skip Lists is wired, but variant-specific skip lists are not: refresh fetches `GetSkipListAsync(gameType, ct: ct)` with no `GameVariant`, unlike `CleaningPreflight.cs:84,120`. |
| 5 | User can cancel large-plugin approximation work while ITM record/context loops are running. | ✓ VERIFIED | `ItmDetector.cs:39` and `:75` poll cancellation; `PluginQueryService.cs:84` passes `ct` into ITM detection. |
| 6 | Analyzed plugin counts remain exact; incomplete canceled plugin counts are not published. | ✓ VERIFIED | `PluginIssueApproximationService` passes caller token into QueryPlugins and does not publish partial callback results after `OperationCanceledException` (per existing tests and source trace). |
| 7 | ITM detection no longer materializes every override context into an array for each record. | ✓ VERIFIED | Streaming `foreach` over `ResolveAllSimpleContexts` at `ItmDetector.cs:73`; no context-array materialization found. |
| 8 | Maintainer has concrete refresh contracts to implement outside ConfigurationViewModel. | ✓ VERIFIED | `IPluginRefreshCoordinator`, `PluginRefreshRequest`, `PluginRefreshStatus`, and `IPluginRefreshCapabilityPolicy` exist. |
| 9 | Selected refresh target sets are snapshots and do not mutate when row selection changes mid-refresh. | ✓ VERIFIED | `PluginListViewModel.cs:137-140` materializes selected rows to a `List<PluginRefreshTarget>`. |
| 10 | Targeted approximation updates preserve non-targeted row values. | ✓ VERIFIED | `PluginRefreshCoordinator.cs:184-197` maps existing rows in place; tests assert non-selected `Available(9,8,7)` remains. |
| 11 | Plugin rows load and display before background approximation starts. | ✓ VERIFIED | `RefreshForGameAsync` calls `_stateService.SetPluginsToClean(rows)` at `PluginRefreshCoordinator.cs:91` before `AnalyzeTargetsAsync` at line 115. |
| 12 | Plugin loading, skip-list application, generation/CTS ownership, and approximation refresh no longer live in ConfigurationViewModel. | ✓ VERIFIED | Old approximation workflow members are absent from `ConfigurationViewModel`; loading path delegates to coordinator. |
| 13 | New refresh requests cancel/replace older refresh work and stale results do not alter current rows. | ⚠️ PARTIAL | Generation guards exist (`Interlocked.Increment`, `IsCurrent`), but CTS disposal during supersede is unsafe and blocks full verification. |
| 14 | User can refresh checked visible plugin rows from a button near the plugin list controls. | ✓ VERIFIED | `MainWindow.axaml:278-292` binds refresh and cancel buttons to `PluginList` commands. |
| 15 | Refresh selected is disabled until a stable plugin list is loaded, approximation is supported, not cleaning, and at least one visible row is checked. | ✓ VERIFIED | `PluginListViewModel.cs:86-87` gates selected refresh on `HasPlugins`, `!IsCleaning`, selected visible row, and capability. |
| 16 | User can cancel an active approximation refresh without a confirmation dialog. | ✓ VERIFIED | `PluginListViewModel.cs:167-170` directly calls `CancelActiveRefresh(Manual)`. |
| 17 | Starting cleaning cancels any active approximation refresh before xEdit cleaning begins. | ✓ VERIFIED | `CleaningCommandsViewModel.cs:137` cancels before progress interaction and `_orchestrator.StartCleaningAsync`. |
| 18 | Settings reset, ViewModel disposal, or equivalent teardown cancels active refresh work and prevents stale state writes. | ✓ VERIFIED | `ConfigurationViewModel.Dispose` cancels active refresh at lines 588-591; reset path cancellation is covered in phase tests. |
| 19 | Full solution tests pass after QueryPlugins, coordinator, ViewModel, and XAML integration. | ✓ VERIFIED | Provided context says `dotnet test "AutoQACSharp.slnx"` passed: QueryPlugins.Tests 61/61 and AutoQAC.Tests 848/848. |
| 20 | Selected approximation refresh keeps the current visible plugin row set intact. | ✓ VERIFIED | `RefreshSelectedApproximationsAsync` uses `_stateService.UpdateState` over current rows and no selected-only `SetPluginsToClean(pendingRows)` call is present. |
| 21 | Successful full approximation refresh publishes a terminal status so users can tell refresh work is complete and cancel controls clear. | ✓ VERIFIED | `PluginRefreshStatusKind.FullRefreshCompleted` and factory exist; coordinator publishes it on successful analysis (`PluginRefreshCoordinator.cs:115-121`). |
| 22 | Refresh failures publish a terminal non-running status so users can tell work is over and cancel controls clear. | ✗ FAILED | Non-cancellation failure catch at `PluginRefreshCoordinator.cs:123-136` publishes no terminal status after merging unavailable rows. |

**Score:** 19/22 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `QueryPlugins/Detectors/ItmDetector.cs` | Streaming ITM traversal with cancellation polling | ✓ VERIFIED | Cancellation polls at lines 18, 39, and 75; streaming context traversal at line 73. |
| `QueryPlugins/PluginQueryService.cs` | Cancellation-aware analysis orchestration | ⚠️ WARNING | ITM receives `ct`; deleted-reference/navmesh detector traversal still receives no token (`PluginQueryService.cs:86-88`). |
| `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs` | Cancellation token propagation into QueryPlugins analysis | ✓ VERIFIED | Source/plan evidence confirms caller token flows into `Analyse(..., ct)`. |
| `AutoQAC/Services/Plugin/IPluginRefreshCoordinator.cs` | Refresh/cancel coordinator contract | ✓ VERIFIED | Contract exists and is DI-registered. |
| `AutoQAC/Services/Plugin/PluginRefreshRequest.cs` | Typed refresh request and target DTOs | ✓ VERIFIED | `DisableSkipLists` is documented and defaulted at lines 12-17. |
| `AutoQAC/Services/Plugin/PluginRefreshStatus.cs` | Typed status/progress outcomes | ✓ VERIFIED | `FullRefreshCompleted` kind, factory, and display text exist at lines 12, 63-69, and 80. |
| `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs` | Service-owned plugin refresh workflow | ✗ FAILED | Substantive and wired, but cancellation source disposal, failure terminal status, and variant skip-list behavior are incomplete. |
| `AutoQAC/Services/Plugin/PluginRefreshCapabilityPolicy.cs` | Refresh-scoped game capability policy | ✓ VERIFIED | DI registration present; policy supports expected families per tests/source. |
| `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs` | UI shell delegating refresh workflow | ✓ VERIFIED | Delegates refresh request at lines 543-549 and no old approximation CTS/generation workflow remains. |
| `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs` | Refresh selected and cancel commands | ✓ VERIFIED | Commands and running-state mapping exist; failure-terminal gap is in coordinator publication. |
| `AutoQAC/Views/MainWindow.axaml` | Plugin-list toolbar UI for refresh/cancel actions | ✓ VERIFIED | Buttons and bindings present at lines 278-292. |
| `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` | Cleaning-start refresh cancellation integration | ✓ VERIFIED | Cancels active refresh before starting cleaning at line 137. |
| `AutoQAC/ViewModels/MainWindowViewModel.cs` | Shared coordinator injection into child ViewModels | ✓ VERIFIED | Covered by DI tests and plan source evidence. |
| `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs` | Regression coverage for selected preservation, DisableSkipLists, and success terminal status | ⚠️ PARTIAL | Covers closed gaps, but lacks tests for ObjectDisposed cancellation, failure terminal status, and variant skip lists. |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `PluginIssueApproximationService` | `PluginQueryService` | Passes caller token into `Analyse` | ✓ WIRED | Existing phase source/tests verify token propagation. |
| `ItmDetector` | Mutagen link cache contexts | Streaming `ResolveAllSimpleContexts` enumeration | ✓ WIRED | `foreach` at `ItmDetector.cs:73`; cancellation inside loop. |
| `ConfigurationViewModel` | `IPluginRefreshCoordinator` | Game/data-folder/load-order/DisableSkipLists request | ✓ WIRED | Request construction includes named `DisableSkipLists` at `ConfigurationViewModel.cs:543-549`. |
| `PluginRefreshCoordinator` | `IStateService` | `SetPluginsToClean`, `UpdateState`, `MergePluginApproximation` | ✓ WIRED | Row-first full refresh and non-destructive selected refresh are wired. |
| `PluginRefreshCoordinator` | `PluginListViewModel` | StatusChanged controls running/cancel UI state | ⚠️ PARTIAL | Successful full refresh clears via `FullRefreshCompleted`; non-cancellation failure paths do not publish terminal status. |
| `PluginRefreshCoordinator` | `IConfigurationService` | Skip-list retrieval | ✗ NOT_WIRED | Does not pass detected `GameVariant`; refresh and cleaning preflight can disagree. |
| `CleaningCommandsViewModel.StartCleaningAsync` | `IPluginRefreshCoordinator.CancelActiveRefresh` | Cleaning-start cancellation before orchestrator start | ✓ WIRED | Call precedes `_orchestrator.StartCleaningAsync`. |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|--------------------|--------|
| `ItmDetector.cs` | `PluginIssue` stream | Mutagen plugin records + link cache contexts | Yes | ✓ FLOWING |
| `PluginRefreshCoordinator.RefreshForGameAsync` | `rows` / `targets` / approximations | `IPluginLoadingService`, `IConfigurationService`, `IPluginIssueApproximationService` | Partially | ⚠️ HOLLOW for variant skip-list source and failure terminal state. |
| `PluginRefreshCoordinator.RefreshSelectedApproximationsAsync` | selected target row updates | Current `IStateService.CurrentState.PluginsToClean` plus selected snapshot | Yes | ✓ FLOWING |
| `PluginListViewModel` | selected targets and running flag | `PluginsToClean` plus coordinator status stream | Partially | ⚠️ Depends on missing failure terminal status from coordinator. |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Full solution tests | Provided context: `dotnet test "AutoQACSharp.slnx"` | QueryPlugins.Tests 61/61 and AutoQAC.Tests 848/848 passed | ✓ PASS |
| Disable Skip Lists flows into refresh rows | Source trace | Request field, ViewModel named argument, coordinator apply call, and positive/negative tests exist | ✓ PASS |
| Successful full refresh clears running/cancel UI | Source trace | `FullRefreshCompleted` published and VM treats it as non-running | ✓ PASS |
| Cancel/supersede avoids disposing live CTS | Source trace | Active/previous CTS is disposed from non-owning paths | ✗ FAIL |
| Refresh failure clears running/cancel UI | Source trace | Failure catch has no terminal `Publish(...)` | ✗ FAIL |
| Variant-specific skip lists match cleaning preflight | Source trace | Refresh does not detect/pass `GameVariant`; cleaning preflight does | ✗ FAIL |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| REF-02 | 09-02, 09-03, 09-04, 09-05, 09-06, 09-07, 09-08 | Maintainer can change plugin loading or issue approximation refresh behavior outside `ConfigurationViewModel`. | ✓ SATISFIED | Coordinator/capability policy own workflow; Configuration VM delegates request/status concerns. |
| PERF-01 | 09-01 through 09-08 | User can refresh plugin issue approximations with better cancellation, reduced redundant load-order work, or narrower target scope. | ✗ BLOCKED | Selected scope and row preservation are implemented, but cancellation lifetime, failure terminal status, and variant skip-list consistency still break user-visible refresh behavior. |
| PERF-02 | 09-01, 09-05 | User can run ITM approximation on large plugins without materializing every override context for each record. | ✓ SATISFIED | Streaming ITM traversal with cancellation polling and no `.ToArray()` materialization. |

No orphaned Phase 9 requirement IDs were found in `.planning/REQUIREMENTS.md`; REF-02, PERF-01, and PERF-02 are mapped to Phase 9 and claimed by plan frontmatter.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs` | 218-237, 254-259, 390-408 | External cancel path disposes live CTS | 🛑 Blocker | Can turn cancellation/supersede into `ObjectDisposedException` while async work still owns the token. |
| `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs` | 123-136 | Recoverable refresh failure without terminal status | 🛑 Blocker | Running/cancel UI can remain active after work is over. |
| `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs` | 84, 309-317 | Skip-list lookup ignores detected `GameVariant` | 🛑 Blocker | TTW/Enderal refresh rows can disagree with cleaning preflight skip decisions. |
| `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs` | 191-193, 331-359 | Full-path OR file-name matching | ⚠️ Warning | Duplicate file names across paths can update non-target rows. |
| `QueryPlugins/PluginQueryService.cs` | 86-88 | Game-specific detector calls lack cancellation token | ⚠️ Warning | Cancellation cannot interrupt deleted-reference/navmesh traversals until each detector returns. |
| `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs` | 388 | Raw `Subject<T>.OnNext` from coordinator | ⚠️ Warning | Concurrent status publication can race observers. |

### Human Verification Required

None at this stage. The blocking gaps are directly observable in source and should be fixed before manual UI smoke testing. After fixes, manually smoke-test cancel/supersede, failure recovery, and TTW/Enderal skip-list refresh behavior.

### Gaps Summary

Phase 9 is close but must not proceed as passed. The previous gap-closure plans fixed Disable Skip Lists propagation and successful full-refresh terminal status. Remaining blockers are cancellation lifetime safety, terminal status on refresh failure, and variant-specific skip-list consistency. These block PERF-01 and the roadmap success criteria for responsive cancellation and user-visible list consistency.

---

_Verified: 2026-04-30T09:34:53Z_  
_Verifier: the agent (gsd-verifier)_
