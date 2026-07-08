# Phase 9: Plugin Refresh & Approximation Performance - Context

**Gathered:** 2026-04-29
**Status:** Ready for planning

<domain>
## Phase Boundary

Phase 9 improves plugin list refresh and issue approximation performance without expanding app-level game support or changing the core xEdit cleaning workflow. Users should get immediate plugin rows, targeted/cancellable issue approximation refresh, exact approximation counts for analyzed plugins, and consistent row state while plugin loading and approximation coordination move out of `ConfigurationViewModel`.

Out of scope for this phase: parallel xEdit cleaning, modifying `Mutagen/`, adding app-level approximation support for new games, and a full app-wide game capability registry beyond what plugin refresh needs.

</domain>

<decisions>
## Implementation Decisions

### Refresh Targeting
- **D-01:** After a game, data folder, or load-order selection, AutoQAC should load and display plugin rows first; approximation analysis runs afterward as background work.
- **D-02:** Approximation refresh should target selected plugin rows first, not all loaded plugins by default.
- **D-03:** Add a user-facing Refresh selected approximations action for selected plugin rows.
- **D-04:** Non-targeted rows keep their existing approximation value. Rows that have never been analyzed remain Unavailable or the existing equivalent not-analyzed state.
- **D-05:** Skip-list-hidden rows are not analyzed until they become visible after skip-list settings change.
- **D-06:** A selected-plugin refresh stops after its selected target set completes; do not continue into visible deselected rows as idle fill.
- **D-07:** If no plugins are selected, Refresh selected should do nothing clearly, preferably by disabling the command or showing a concise status such as "Select plugins to refresh."
- **D-08:** The selected target set is a snapshot taken when refresh starts. Selection changes during a refresh do not mutate the active target set; a new refresh cancels/replaces the current generation.

### Stale Result And Cancellation UX
- **D-09:** When a newer refresh supersedes older work, keep existing counts until replacement results arrive. Do not clear rows just because work was canceled.
- **D-10:** Automatic supersedes should be silent; users should see the newer refresh status, not a persistent canceled state for the old generation.
- **D-11:** Refresh progress should be truthful item-count progress, such as "Analyzing 2 of 12 selected plugins," with rows updating incrementally as each result arrives.
- **D-12:** A per-plugin analysis failure marks only that row Unavailable and logs details. Do not interrupt users with a dialog for ordinary per-plugin approximation failures.
- **D-13:** Expose a cancel action while approximation refresh is running.
- **D-14:** Manual cancellation keeps completed row results from the canceled run. Not-yet-analyzed rows keep their previous value or Unavailable state, and status should say the refresh was canceled.
- **D-15:** Starting cleaning cancels any active approximation refresh so background Mutagen/file I/O does not compete with the primary cleaning workflow.
- **D-16:** After selected-plugin refresh completes, leave a concise completion status such as "Updated 12 selected plugin approximations." Rows carry detailed results.

### Count Accuracy And Detector Semantics
- **D-17:** Preserve exact count semantics for analyzed plugins. Optimize allocation, memory, and cancellation, but do not show partial, capped, or approximate-subset counts as if they were complete.
- **D-18:** Do not add an automatic per-plugin analysis timeout in this phase. User cancellation is the way to stop long-running exact analysis.
- **D-19:** If cancellation interrupts one plugin midway through exact analysis, do not publish incomplete counts. Keep the previous row value or Unavailable state for that plugin.
- **D-20:** If optimized ITM analysis hits an ambiguous or unsupported Mutagen context case, fail that plugin to Unavailable and log details rather than showing a questionable count.
- **D-21:** If one analysis category fails for a plugin, mark the whole plugin approximation Unavailable rather than mixing complete and incomplete category counts in one row.
- **D-22:** Keep the UI wording as approximations. Counts may be exact for the analyzed load-order snapshot, but they are still pre-clean preview data that can become stale.
- **D-23:** Cancellation checks should be frequent inside hot ITM record/context loops, not only between plugins.
- **D-24:** When exact counts conflict with peak memory or raw speed, prefer lower peak memory over raw speed. Avoid materializing every context per record.

### Workflow Ownership
- **D-25:** Move the whole refresh pipeline out of `ConfigurationViewModel`: game/data-folder resolution, load-order/plugin loading, skip-list application, approximation targeting, cancellation/generation, and refresh status outcomes.
- **D-26:** A refresh coordinator/service owns refresh generation IDs and `CancellationTokenSource` lifecycle. The ViewModel requests refresh/cancel and observes state/status.
- **D-27:** The coordinator publishes plugin rows and approximation updates through `IStateService`, preserving the existing application state hub and merge patterns.
- **D-28:** Game capability cleanup is refresh-scoped for Phase 9. Create only the policy/capability surface needed by plugin refresh and approximation decisions.
- **D-29:** The manual load-order file path in `ConfigureLoadOrderAsync` should join the new coordinator path so file-based and game/data-folder refreshes share behavior and cancellation.
- **D-30:** After extraction, `ConfigurationViewModel` should be a UI shell: path properties, file dialogs, commands, and display/status binding. Plugin load/skip/approximation workflow decisions belong in services.
- **D-31:** The coordinator should communicate typed progress/status outcomes; the ViewModel maps those to user-facing `StatusText` strings.
- **D-32:** The Refresh selected approximations command belongs near the plugin list because it operates on selected plugin rows.
- **D-33:** The coordinator should allow one active refresh generation at a time. New refresh requests cancel/replace old work rather than queueing or running concurrently.
- **D-34:** Refresh selected should be disabled until a stable plugin list is loaded.
- **D-35:** For games where plugin loading is available but issue approximation is unsupported, disable/hide Refresh selected with a clear reason. Do not attempt to expand app-level support in this phase.
- **D-36:** Settings reset, ViewModel disposal, or equivalent lifecycle teardown cancels active refresh work and prevents stale callbacks/state writes.

### the agent's Discretion
- Exact class/interface names, method signatures, DTO/status enum names, XAML placement details, and test file organization are planner discretion as long as the decisions above are preserved.
- The planner may choose the smallest safe refresh-scoped capability abstraction needed to remove duplicated plugin-refresh rules without broadening Phase 9 into a full registry rewrite.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase Scope And Requirements
- `.planning/ROADMAP.md` - Phase 9 goal, dependencies, requirements, and success criteria.
- `.planning/REQUIREMENTS.md` - REF-02, PERF-01, and PERF-02 requirement definitions and traceability.
- `.planning/PROJECT.md` - Project constraints, milestone context, and key decisions including sequential xEdit cleaning and read-only `Mutagen/`.

### Codebase Constraints And Existing Patterns
- `.planning/codebase/CONCERNS.md` - Phase-driving concerns: `ConfigurationViewModel` responsibility concentration, duplicated game/load-order rules, full load-order import, per-record ITM allocation, and fragile approximation state merging.
- `.planning/codebase/ARCHITECTURE.md` - Existing plugin discovery/configuration flow, state hub, MVVM boundaries, plugin loading strategy, and QueryPlugins detector registry.
- `.planning/codebase/CONVENTIONS.md` - Service/ViewModel conventions, async/cancellation patterns, DI registration expectations, comments, and XML doc guidance.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `AutoQAC/Services/Plugin/IPluginLoadingService.cs` and `PluginLoadingService.cs`: existing Mutagen-supported plugin loading, file-based loading, data-folder resolution, and default load-order path behavior.
- `AutoQAC/Services/Plugin/IPluginIssueApproximationService.cs` and `PluginIssueApproximationService.cs`: existing background approximation API with callback-per-plugin reporting and cancellation token support.
- `AutoQAC/Services/State/IStateService.cs` and `StateService.cs`: existing `SetPluginsToClean`, `MergePluginApproximation`, and `MergePluginApproximations` state merge points.
- `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs`: existing selected-row state via `ExcludedPluginPaths`; natural home for a selected-row refresh command.
- `QueryPlugins/PluginQueryService.cs` and `QueryPlugins/Detectors/ItmDetector.cs`: current detector path for ITM/deleted-reference/deleted-navmesh counts.

### Established Patterns
- `ConfigurationViewModel.RefreshPluginsForGameAsync` currently owns refresh generation checks, CTS replacement, plugin loading, skip-list application, approximation launch, row merge callbacks, and stale-result prevention. Phase 9 should preserve the generation-safety behavior while moving ownership to a service coordinator.
- `StateService.MergePluginApproximation` updates a single matching row and leaves other rows unchanged, which supports selected-target incremental refresh.
- `StateService.MergePluginApproximations` currently marks missing plugins Unavailable in batch mode, so targeted refresh planning must avoid using broad batch merge in a way that clears non-targeted rows.
- `PluginListViewModel.OnStateChanged` displays non-skip-list rows and tracks selection through `ExcludedPluginPaths`; selected refresh should snapshot selected visible rows at command start.
- `ItmDetector` currently calls `ResolveAllSimpleContexts(...).ToArray()` for each major record. PERF-02 work should replace that with lower-memory context traversal while preserving immediate-lower-priority comparison semantics.

### Integration Points
- `ConfigurationViewModel` triggers refresh on selected game changes, data-folder override changes, skip-list setting changes, load-order file configuration, startup initialization, and reset. These triggers should call the coordinator instead of owning workflow logic.
- `PluginListViewModel` should expose or host the Refresh selected command and pass selected-row intent to the coordinator without doing analysis itself.
- `CleaningCommandsViewModel` or the cleaning start path must cancel any active approximation refresh before cleaning begins.
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` is the DI registration point for any new refresh coordinator, capability policy, or service interface.
- `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs`, `AutoQAC.Tests/Services/PluginIssueApproximationServiceTests.cs`, `AutoQAC.Tests/Services/StateServiceTests.cs`, and `QueryPlugins.Tests/Detectors/ItmDetectorTests.cs` already cover key behavior that should be preserved or extended.

</code_context>

<specifics>
## Specific Ideas

- Put the user action near the plugin list as "Refresh selected approximations" or equivalent wording.
- Use status text shaped like "Analyzing 2 of 12 selected plugins," "Updated 12 selected plugin approximations," and "Select plugins to refresh."
- Unsupported approximation games should communicate why refresh is unavailable without implying new app-level detector support.
- Keep the existing row wording that identifies values as approximation/pre-clean preview data.

</specifics>

<deferred>
## Deferred Ideas

- Full app-wide game capability registry cleanup across UI validation, cleaning, dry-run, plugin loading, approximation support, and default paths belongs in a later phase unless a refresh-scoped slice is required here.
- App-level issue approximation support for games beyond the current Skyrim/Fallout 4 families is out of scope for Phase 9.
- Partial/capped count display modes and automatic per-plugin analysis timeouts are intentionally not part of Phase 9.

</deferred>

---

*Phase: 09-plugin-refresh-approximation-performance*
*Context gathered: 2026-04-29*
