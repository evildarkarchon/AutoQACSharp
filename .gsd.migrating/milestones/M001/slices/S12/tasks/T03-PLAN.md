# T03: 11-user-facing-diagnostics-boundaries 03

**Slice:** S12 — **Milestone:** M001

## Description

Harden configuration browse-path and restore session-loading diagnostics.

Purpose: Phase 11 must cover inline/status/dialog surfaces beyond the cleaning command path, especially selected load-order files, selected data folders, and restore session loading.
Output: Tested safe labels and latest-log guidance for configuration technical failures and restore session load failures, preserving already-safe backup labels per D-04.

## Must-Haves

- [ ] "D-06: Configuration browse failures use safe game-folder labels rather than selected full folder paths."
- [ ] "D-07: Configuration and persistence diagnostics use sanitized display names when basenames are shown."
- [ ] "D-08: Restore session load and technical configuration failures use concise latest-log guidance rather than raw exception messages, while simple missing-path validation omits extra log guidance."
- [ ] "D-04: Settings persistence write/read failures preserve ConfigPersistenceFailure.SafeSummary and never surface unsafe path-bearing exception text."
- [ ] "D-04: Existing typed safe labels from backup and persistence flows remain preserved."

## Files

- `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`
- `AutoQAC/ViewModels/RestoreViewModel.cs`
- `AutoQAC/ViewModels/SettingsViewModel.cs`
- `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs`
- `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs`
- `AutoQAC.Tests/ViewModels/SettingsViewModelTests.cs`
