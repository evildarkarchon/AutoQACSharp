# T05: 09-plugin-refresh-approximation-performance 05

**Slice:** S10 — **Milestone:** M001

## Description

Complete cross-ViewModel refresh cancellation/lifecycle wiring and run final Phase 9 verification.

Purpose: Ensures background Mutagen/file I/O cannot compete with sequential xEdit cleaning and that all Phase 9 source decisions are connected in the application shell.
Output: Cleaning-start cancellation, lifecycle cancellation tests, DI assertions, and full solution verification.

## Must-Haves

- [ ] "Starting cleaning cancels any active approximation refresh before xEdit cleaning begins."
- [ ] "Settings reset, ViewModel disposal, or equivalent teardown cancels active refresh work and prevents stale state writes."
- [ ] "Full solution tests pass after QueryPlugins, coordinator, ViewModel, and XAML integration."

## Files

- `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`
- `AutoQAC/ViewModels/MainWindowViewModel.cs`
- `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`
- `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs`
- `AutoQAC.Tests/ViewModels/PluginListViewModelTests.cs`
- `AutoQAC.Tests/Integration/DependencyInjectionTests.cs`
