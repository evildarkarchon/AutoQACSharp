# T02: 09-plugin-refresh-approximation-performance 02

**Slice:** S10 — **Milestone:** M001

## Description

Define and test the Phase 9 plugin refresh coordinator contract before implementing the service extraction.

Purpose: Prevents downstream executors from inventing incompatible refresh APIs and pins D-01 through D-16 and D-25 through D-36 in executable tests.
Output: Service contracts, typed request/status DTOs, refresh-scoped capability policy contract, and Wave 0 tests.

## Must-Haves

- [ ] "Maintainer has concrete refresh contracts to implement outside ConfigurationViewModel."
- [ ] "Selected refresh target sets are snapshots and do not mutate when row selection changes mid-refresh."
- [ ] "Targeted approximation updates preserve non-targeted row values."

## Files

- `AutoQAC/Services/Plugin/IPluginRefreshCoordinator.cs`
- `AutoQAC/Services/Plugin/PluginRefreshRequest.cs`
- `AutoQAC/Services/Plugin/PluginRefreshStatus.cs`
- `AutoQAC/Services/Plugin/IPluginRefreshCapabilityPolicy.cs`
- `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs`
- `AutoQAC.Tests/Services/StateServiceTests.cs`
