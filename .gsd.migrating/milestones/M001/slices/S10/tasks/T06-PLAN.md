# T06: 09-plugin-refresh-approximation-performance 06

**Slice:** S10 — **Milestone:** M001

## Description

Close Phase 9 verification gaps for destructive selected approximation refresh.

Purpose: Selected refresh must be a targeted approximation update, not a plugin-list replacement. This preserves existing non-targeted row visibility and prior approximation values while still narrowing analysis work to the selected target snapshot per D-02, D-04, D-06, D-08, and D-27.
Output: A coordinator regression test and implementation/source guard ensuring selected refresh marks or merges only target rows and leaves non-targeted rows unchanged.

## Must-Haves

- [ ] "Targeted approximation updates preserve non-targeted row values."
- [ ] "Selected approximation refresh keeps the current visible plugin row set intact."
- [ ] "Selected approximation refresh updates only selected target rows to pending/available/unavailable states."

## Files

- `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs`
- `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs`
