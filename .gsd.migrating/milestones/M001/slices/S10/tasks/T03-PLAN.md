# T03: 09-plugin-refresh-approximation-performance 03

**Slice:** S10 — **Milestone:** M001

## Description

Implement the plugin refresh coordinator and move the game/data-folder/load-order refresh workflow out of `ConfigurationViewModel`.

Purpose: Satisfies REF-02 and the workflow ownership decisions D-25 through D-31 while preserving row-first/stale-result behavior from existing code.
Output: DI-registered refresh coordinator, refresh-scoped capability policy, and a thinner ConfigurationViewModel.

## Must-Haves

- [ ] "Plugin rows load and display before background approximation starts."
- [ ] "Plugin loading, skip-list application, generation/CTS ownership, and approximation refresh no longer live in ConfigurationViewModel."
- [ ] "New refresh requests cancel/replace older refresh work and stale results do not alter current rows."

## Files

- `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs`
- `AutoQAC/Services/Plugin/PluginRefreshCapabilityPolicy.cs`
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`
- `AutoQAC/ViewModels/MainWindowViewModel.cs`
- `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs`
- `AutoQAC.Tests/Integration/DependencyInjectionTests.cs`
