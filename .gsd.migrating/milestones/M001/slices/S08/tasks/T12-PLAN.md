# T12: 07-backup-restore-retention-safety 12

**Slice:** S08 — **Milestone:** M001

## Description

Close the verification gap where the normal cleaning progress window can display completed results but its Close command does not have explicit normal-path close/disposal wiring.

Purpose: PERF-04 requires visible cancellable backup/retention progress without stale UI subscriptions after completion. This gap closure ensures the normal progress-window path has the same lifecycle guarantees as the preview path while explicitly documenting how the new wiring composes with the existing `ProgressWindow.axaml.cs` cleanup helper.

Output: Source-level regression coverage (using loosened regex assertions) plus normal `ShowProgressAsync` lifecycle wiring that closes the window from `ProgressViewModel.CloseRequested` and disposes the ViewModel when the window closes, with an explicit defense-in-depth code comment so future readers do not mistake the dual subscription for redundant work.

## Must-Haves

- [ ] "Normal cleaning progress window result Close button closes the window instead of leaving a completed progress surface open."
- [ ] "Normal cleaning progress window disposal releases ProgressViewModel subscriptions when the window closes."
- [ ] "Backup/retention progress visibility from D-09 and non-xEdit cancel affordances from D-11 do not leave a stale progress ViewModel alive after completion."
- [ ] "The new ShowProgressAsync wiring is documented as defense-in-depth alongside the pre-existing ProgressWindow.OnDataContextChanged + OnClosed → DisposeViewModelIfNeeded contract, with an explicit code comment in ShowProgressAsync explaining why both subscription paths exist."
- [ ] "Source-grep regression coverage uses regex patterns tolerant of method-group syntax and alternate guard variable names, not exact substring matches that break under valid refactors."

## Files

- `AutoQAC/Views/MainWindow.axaml.cs`
- `AutoQAC.Tests/Views/ViewSubscriptionLifecycleTests.cs`
