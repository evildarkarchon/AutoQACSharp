# T01: 09-plugin-refresh-approximation-performance 01

**Slice:** S10 — **Milestone:** M001

## Description

Optimize the QueryPlugins ITM hot path so Phase 9 approximation work can be canceled inside record/context loops while preserving exact count semantics.

Purpose: Satisfies PERF-02 and the count-accuracy decisions D-17 through D-24 before the app-level refresh coordinator depends on cancellation-aware analysis.
Output: Cancellation-aware QueryPlugins analysis contracts, streaming ITM traversal, and regression tests.

## Must-Haves

- [ ] "User can cancel large-plugin approximation work while ITM record/context loops are running."
- [ ] "Analyzed plugin counts remain exact; incomplete canceled plugin counts are not published."
- [ ] "ITM detection no longer materializes every override context into an array for each record."

## Files

- `QueryPlugins/IPluginQueryService.cs`
- `QueryPlugins/PluginQueryService.cs`
- `QueryPlugins/Detectors/IItmDetector.cs`
- `QueryPlugins/Detectors/ItmDetector.cs`
- `QueryPlugins.Tests/Detectors/ItmDetectorTests.cs`
- `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs`
- `AutoQAC.Tests/Services/PluginIssueApproximationServiceTests.cs`
