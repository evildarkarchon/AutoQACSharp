# T02: 11-user-facing-diagnostics-boundaries 02

**Slice:** S12 — **Milestone:** M001

## Description

Harden the main cleaning command UI boundary.

Purpose: SEC-01 requires the most visible cleaning and preview failures to stop surfacing `ex.Message`, `ex.StackTrace`, full configured paths, and command fragments.
Output: Tested safe status/dialog text and safe pre-clean validation rows in `CleaningCommandsViewModel`.

## Must-Haves

- [ ] "D-01: Unexpected cleaning failures show operation-specific safe copy and latest AutoQAC log guidance, not raw exception details."
- [ ] "D-02: Unexpected cleaning and preview modal dialog details contain safe latest-log guidance only, not stack traces, exception messages, paths, or command fragments."
- [ ] "D-03: Unexpected preview failures set concise status copy and latest AutoQAC log guidance, not ex.Message."
- [ ] "D-05: Pre-clean inline validation identifies xEdit, MO2, and load-order settings by safe basename only."
- [ ] "D-08: Simple missing-path validation provides the safe identifier and fix action without extra latest-log guidance."

## Files

- `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`
- `AutoQAC.Tests/ViewModels/ErrorDialogTests.cs`
