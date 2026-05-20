# T04: 09-plugin-refresh-approximation-performance 04

**Slice:** S10 — **Milestone:** M001

## Description

Add the user-facing selected approximation refresh and cancel controls in the plugin list surface.

Purpose: Satisfies D-02, D-03, D-06, D-07, D-08, D-13, D-16, D-32, D-34, D-35, and the approved UI-SPEC without moving business workflow into the ViewModel.
Output: PluginList commands, XAML controls, and ViewModel tests.

## Must-Haves

- [ ] "User can refresh checked visible plugin rows from a button near the plugin list controls."
- [ ] "Refresh selected is disabled until a stable plugin list is loaded, approximation is supported, not cleaning, and at least one visible row is checked."
- [ ] "User can cancel an active approximation refresh without a confirmation dialog."

## Files

- `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs`
- `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`
- `AutoQAC/ViewModels/MainWindowViewModel.cs`
- `AutoQAC/Views/MainWindow.axaml`
- `AutoQAC.Tests/ViewModels/PluginListViewModelTests.cs`
