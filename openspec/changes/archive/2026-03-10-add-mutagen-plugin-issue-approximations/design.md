## Context

`AutoQAC` already uses Mutagen to discover plugins for Skyrim LE/SE/VR and Fallout 4/VR, then stores `PluginInfo` records in `AppState` for the main plugin list. The repository also contains a separate `QueryPlugins` library that can analyze a loaded plugin and a full load-order `ILinkCache` to produce approximate ITM, deleted reference, and deleted navmesh counts, but `AutoQAC` does not currently reference or surface those results.

The plugin list is refreshed from `ConfigurationViewModel.RefreshPluginsForGameAsync`, and row state is represented by immutable `PluginInfo` records except for the UI-thread-only `IsSelected` checkbox state. That means any background approximation pass must avoid blocking plugin discovery, must preserve user selections when refreshed results are merged back into state, and must not change the sequential xEdit cleaning pipeline.

## Goals / Non-Goals

**Goals:**
- Show approximate ITM, UDR, and deleted navmesh counts for plugins loaded through the existing Mutagen path.
- Keep plugin discovery responsive by loading the list first and completing approximation work asynchronously.
- Preserve skip-list filtering and user checkbox selections when approximation results are applied.
- Make approximation support explicit for Mutagen-backed games only and avoid changing file-based game behavior.
- Keep preview counts clearly separate from authoritative xEdit cleaning results.

**Non-Goals:**
- Add approximation support for file-based games such as Fallout 3, Fallout: New Vegas, or Oblivion.
- Change `CleaningOrchestrator`, xEdit launch sequencing, or session result parsing.
- Add per-record drill-down UI for individual ITMs, deleted references, or navmeshes.
- Expand `QueryPlugins` detector coverage beyond the current Skyrim and Fallout 4 families.

## Decisions

### 1. Add a dedicated AutoQAC approximation service
Create a new AutoQAC-side service, e.g. `IPluginIssueApproximationService`, that owns QueryPlugins integration and returns immutable per-plugin approximation snapshots.

- Why: `PluginLoadingService` should keep owning discovery, while QueryPlugins analysis needs richer Mutagen objects, a full link cache, and different failure semantics.
- Alternative considered: extend `PluginLoadingService` to also analyze plugins. Rejected because it mixes discovery and analysis concerns and makes unsupported/fallback behavior harder to reason about.

### 2. Limit analysis to the existing Mutagen-supported AutoQAC games
Approximation analysis will run only for the games that already load through Mutagen in AutoQAC: Skyrim LE/SE/VR and Fallout 4/VR.

- Why: the user explicitly wants this limited to Mutagen-supported games, and those games overlap with the detector coverage already present in `QueryPlugins`.
- Alternative considered: add partial support for file-based games or Oblivion now. Rejected because QueryPlugins requires a full Mutagen load-order context, and this change is about surfacing approximations, not broadening game support.

### 3. Use a two-phase plugin refresh
`RefreshPluginsForGameAsync` will continue to publish the loaded plugin list immediately, then start a cancellable approximation pass that merges results back into state after analysis completes.

- Why: this keeps the UI responsive and avoids making plugin loading wait on potentially expensive ITM analysis.
- Alternative considered: block the refresh until all approximations are computed. Rejected because it would make every game/data-folder refresh slower and more fragile.

### 4. Represent approximations as explicit plugin metadata, not implicit zeros
Add immutable approximation metadata to `PluginInfo` (either a dedicated value object or a small set of status-plus-count fields) so the UI can distinguish `pending`, `available`, and `unavailable/failed` states.

- Why: a zero count means "analyzed and found none," which is materially different from "analysis did not run" for unsupported or failed cases.
- Alternative considered: store three nullable integers only. Rejected because it leaves pending/failure semantics ambiguous and pushes too much interpretation into the view.

### 5. Build one load-order analysis context per refresh
The approximation service will build one Mutagen load-order context for the selected game/data folder, then analyze each discovered plugin against the same shared cache.

- Why: `QueryPlugins` requires the analyzed plugin to be present in a full `ILinkCache`, and reusing one context avoids inconsistent comparisons and repeated setup cost.
- Alternative considered: create a separate context per plugin. Rejected because it is slower and risks inconsistent results if the load order is resolved differently per call.

### 6. Merge results onto the current plugin list without resetting selections
When approximation results arrive, `ConfigurationViewModel` or the state layer will rebuild `PluginInfo` records by matching on plugin filename/full path and copying the current `IsSelected` value forward.

- Why: users can start selecting or deselecting plugins before the approximation pass finishes, and those edits must survive the background update.
- Alternative considered: simply replace the plugin list with fresh records from the approximation service. Rejected because it would silently reset checkbox state.

### 7. Treat approximation errors as non-fatal
If QueryPlugins analysis fails or is canceled, AutoQAC will keep the already-loaded plugin list, log the failure, and leave approximation metadata unavailable for that refresh.

- Why: plugin discovery is the primary workflow; approximations are additive guidance.
- Alternative considered: fail the whole plugin refresh when approximation analysis fails. Rejected because it would make the feature brittle and would regress current plugin loading behavior.

## Risks / Trade-offs

- [Large load orders can make approximation passes slow] -> Run analysis off the UI thread, make it cancellable, and publish the plugin list before counts are ready.
- [Approximate counts can differ from xEdit results] -> Label them as approximate previews and keep xEdit session results as the authoritative post-cleaning source.
- [Background result application can overwrite user checkbox changes] -> Merge results onto the latest state snapshot and preserve `IsSelected` values.
- [Mutagen and QueryPlugins setup adds cross-project complexity] -> Isolate the integration behind one service and add focused unit/integration coverage around supported-game refresh flows.

## Migration Plan

1. Add a project reference from `AutoQAC` to `QueryPlugins` and register the new approximation service in DI.
2. Extend plugin refresh flow to trigger the approximation pass only after successful Mutagen plugin discovery.
3. Add plugin-row metadata and UI rendering for approximate counts, including unavailable/pending states.
4. Update tests and documentation to describe the new preview behavior for Mutagen-backed games.

Rollback is straightforward: remove the service and UI surface, then fall back to the existing plugin discovery-only flow. No persisted configuration or data migration is required.

## Open Questions

- None blocking implementation. The remaining choice is presentational: whether the plugin list shows the counts as inline badges, secondary text, or a compact right-aligned summary, which can be finalized while implementing the XAML.
