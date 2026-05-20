# T09: 09-plugin-refresh-approximation-performance 09

**Slice:** S10 — **Milestone:** M001

## Description

Close the remaining Phase 9 verification blockers for refresh cancellation lifetime safety, failure-terminal status cleanup, and variant-specific skip-list consistency.

Purpose: PERF-01 remains blocked until cancellation is cooperative under active async work, refresh failures leave the UI in a non-running state, and refresh row skip-list decisions match cleaning preflight for TTW/Enderal variants.
Output: Hardened `PluginRefreshCoordinator` behavior plus focused regression tests proving the three verification gaps are closed.

## Must-Haves

- [ ] "Supersede, manual cancel, and cleaning-start cancel signal active plugin refresh work without disposing a CTS still owned by an awaiting refresh task."
- [ ] "Recoverable full-refresh failures publish a terminal non-running status so PluginListViewModel clears the cancel affordance."
- [ ] "Refresh skip-list application detects TTW/Enderal variants from loaded plugin rows and requests variant-specific skip lists like cleaning preflight."

## Files

- `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs`
- `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs`
- `AutoQAC.Tests/ViewModels/PluginListViewModelTests.cs`
