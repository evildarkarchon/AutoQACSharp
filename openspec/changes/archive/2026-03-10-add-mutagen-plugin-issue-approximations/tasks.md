## 1. QueryPlugins integration

- [x] 1.1 Add the `QueryPlugins` project reference to `AutoQAC` and introduce immutable plugin approximation metadata on `PluginInfo` that can represent pending, available, and unavailable results.
- [x] 1.2 Add a new AutoQAC-side approximation service contract and register it through `ServiceCollectionExtensions`.

## 2. Approximation pipeline

- [x] 2.1 Implement the Mutagen-backed approximation service so it builds one load-order analysis context per refresh and maps `QueryPlugins` results to per-plugin approximate ITM, deleted reference, and deleted navmesh counts.
- [x] 2.2 Update `ConfigurationViewModel.RefreshPluginsForGameAsync` to publish the loaded plugin list first, then start and cancel approximation work only for Mutagen-supported games.
- [x] 2.3 Merge approximation results back into state without resetting `IsSelected` or skip-list state, and keep approximation failures non-fatal to plugin loading.

## 3. Plugin list UI

- [x] 3.1 Update the plugin list view model/bindings to surface per-plugin approximation metadata.
- [x] 3.2 Update `MainWindow.axaml` so each plugin row shows approximate ITM, UDR, and deleted navmesh counts when available and does not present unavailable analysis as zero issues.

## 4. Verification and documentation

- [x] 4.1 Add tests for the approximation service covering supported games, unsupported games, cancellation, and analysis failure handling.
- [x] 4.2 Add view model or state-flow tests covering two-phase refresh behavior, selection preservation, and result merging after background analysis.
- [x] 4.3 Update user-facing documentation to describe Mutagen-only approximate issue previews and clarify that xEdit session output remains authoritative.
