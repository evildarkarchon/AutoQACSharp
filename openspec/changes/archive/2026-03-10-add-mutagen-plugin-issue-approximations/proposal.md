## Why

AutoQAC currently shows whether plugins are selectable and skipped, but it does not give users any early signal about which plugins are likely to contain ITMs, UDRs, or deleted navmeshes before a cleaning run. The repository already includes the `QueryPlugins` library for approximate Mutagen-based analysis, so exposing those results in the plugin list can make the app more informative without changing the sequential xEdit cleaning flow.

## What Changes

- Add a Mutagen-only plugin issue approximation workflow that analyzes loaded plugins with `QueryPlugins` and produces approximate ITM, deleted reference, and deleted navmesh counts.
- Surface the approximation results on each plugin row in the main plugin list so users can see likely issues before starting cleaning.
- Keep approximation analysis separate from xEdit execution and clearly treat the counts as previews rather than authoritative cleaning results.
- Skip approximation analysis for games that do not use Mutagen-based plugin loading, and leave the plugin list behavior unchanged for those games.
- Add logging, cancellation, and failure handling so plugin list refreshes remain responsive if approximation analysis is slow or unavailable.

## Capabilities

### New Capabilities
- `plugin-issue-approximations`: Analyze Mutagen-loaded plugins with `QueryPlugins` and show approximate ITM, UDR, and deleted navmesh counts in the plugin list.

### Modified Capabilities

## Impact

- Affected code: `AutoQAC/Models/PluginInfo.cs`, `AutoQAC/Services/Plugin/`, `AutoQAC/Services/State/`, `AutoQAC/ViewModels/MainWindow/`, `AutoQAC/Views/MainWindow.axaml`, and related tests.
- Dependencies: `AutoQAC` will consume the existing `QueryPlugins` project and its Mutagen-based analysis APIs.
- Runtime behavior: plugin refresh gains an asynchronous approximation pass for Mutagen-supported games only; cleaning orchestration and xEdit process sequencing stay unchanged.
