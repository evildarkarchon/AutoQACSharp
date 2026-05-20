# T07: 09-plugin-refresh-approximation-performance 07

**Slice:** S10 — **Milestone:** M001

## Description

Close VERIFICATION.md gap: "Disable Skip Lists is ignored by coordinator refresh row publication."

`PluginRefreshCoordinator.RefreshForGameAsync` currently calls `ApplySkipListStatus(..., disableSkipLists: false, ...)` at line 90, hardcoding the flag to `false`. `PluginRefreshRequest` has no `DisableSkipLists` field, so `ConfigurationViewModel` cannot pass `DisableSkipListsEnabled` (already loaded from `UserConfiguration.Settings.DisableSkipLists`) into the coordinator. Result: refreshed plugin rows always treat skip-list entries as hidden, contradicting the user's Disable Skip Lists toggle.

Purpose: Restore the user-visible truth that toggling Disable Skip Lists changes which plugin rows are marked `IsInSkipList` and therefore which rows are eligible approximation targets.

Output:
- `PluginRefreshRequest` gains a `DisableSkipLists` boolean field (default `false`).
- `PluginRefreshCoordinator.RefreshForGameAsync` passes `request.DisableSkipLists` into `ApplySkipListStatus` instead of `false`.
- `ConfigurationViewModel.RefreshPluginsForGameAsync` populates the new field from `DisableSkipListsEnabled`.
- A new xUnit regression test asserts that when the request carries `DisableSkipLists: true`, no row in `state.PluginsToClean` has `IsInSkipList: true` even when the loaded plugin name matches a configured skip-list entry.

## Must-Haves

- [ ] "User-visible plugin lists remain consistent with the user's Disable Skip Lists setting after a coordinator refresh."
- [ ] "PluginRefreshRequest carries the user's Disable Skip Lists setting from ConfigurationViewModel into PluginRefreshCoordinator."
- [ ] "When Disable Skip Lists is enabled, refreshed plugin rows are NOT marked as IsInSkipList=true even if their file name matches a skip-list entry."
- [ ] "When Disable Skip Lists is enabled, formerly skip-listed plugins become eligible approximation targets again."

## Files

- `AutoQAC/Services/Plugin/PluginRefreshRequest.cs`
- `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs`
- `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`
- `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs`
