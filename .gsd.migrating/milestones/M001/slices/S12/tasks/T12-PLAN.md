# T12: 11-user-facing-diagnostics-boundaries 12

**Slice:** S12 — **Milestone:** M001

## Description

Close the Phase 11 SEC-01 cleaning-session dialog gaps where timeout retry and backup failure callbacks can expose raw plugin paths, command fragments, exception text, stack markers, or unsafe backup error strings.

Purpose: callback inputs cross a user-facing trust boundary and must be sanitized even when upstream services usually provide safe values.
Output: executable regression tests plus safe callback/dialog formatting in the existing ViewModel and dialog service surfaces.

## Must-Haves

- [ ] "User-facing backup failure dialogs sanitize callback pluginName and errorMessage values before display per D-07, D-09, and D-10."
- [ ] "Timeout retry dialogs display sanitized plugin names only per D-07."
- [ ] "Backup and timeout callback regressions fail if path, command, exception, or stack sentinels reach user-facing dialog strings."

## Files

- `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`
- `AutoQAC/Services/UI/MessageDialogService.cs`
- `AutoQAC.Tests/ViewModels/ErrorDialogTests.cs`
- `AutoQAC.Tests/ViewModels/Phase11DiagnosticsBoundaryTests.cs`
