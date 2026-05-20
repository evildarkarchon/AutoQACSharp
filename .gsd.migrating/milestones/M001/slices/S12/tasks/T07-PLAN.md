# T07: 11-user-facing-diagnostics-boundaries 07

**Slice:** S12 — **Milestone:** M001

## Description

Close Phase 11 verification gap #1: legacy migration user warnings must use safe latest-log guidance instead of raw exception messages.

Purpose: SEC-01 and decisions D-01 through D-03 require migration warning UI to avoid raw exception/path/command details while keeping actionable guidance.
Output: Safe migration result warning copy, a defensive startup display boundary, and regression tests proving path-bearing migration warnings are not shown.

## Must-Haves

- [ ] "SEC-01 / D-01 / D-02 / D-03: legacy migration warnings shown through MainWindowViewModel.ShowMigrationWarning use concise safe latest-log guidance, not raw exception messages."
- [ ] "SEC-01 / D-14: migration implementation may log local exception objects/details, but warning text that reaches UI does not include path, command, exception, or stack sentinels."

## Files

- `AutoQAC/App.axaml.cs`
- `AutoQAC/Services/Configuration/LegacyMigrationService.cs`
- `AutoQAC.Tests/Services/LegacyMigrationServiceTests.cs`
- `AutoQAC.Tests/Integration/DependencyInjectionTests.cs`
