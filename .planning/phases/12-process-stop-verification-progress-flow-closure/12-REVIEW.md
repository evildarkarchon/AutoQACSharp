---
phase: 12-process-stop-verification-progress-flow-closure
reviewed: 2026-05-01T09:43:28Z
depth: deep
files_reviewed: 11
files_reviewed_list:
  - AutoQAC/Models/StopTerminationDialogContent.cs
  - AutoQAC/Services/UI/IMessageDialogService.cs
  - AutoQAC/Services/UI/MessageDialogService.cs
  - AutoQAC/ViewModels/MessageDialogViewModel.cs
  - AutoQAC/Views/MessageDialog.axaml
  - AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs
  - AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs
  - AutoQAC/ViewModels/ProgressViewModel.cs
  - AutoQAC/Views/ProgressWindow.axaml
  - AutoQAC/Views/MainWindow.axaml.cs
  - AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs
findings:
  critical: 0
  warning: 3
  info: 0
  total: 3
status: issues_found
---

# Phase 12: Code Review Report

**Reviewed:** 2026-05-01T09:43:28Z
**Depth:** deep
**Files Reviewed:** 11
**Status:** issues_found

## Summary

Reviewed the stop-confirmation dialog flow, progress-window stop/hang handling, message-dialog plumbing, and related tests. No critical security or data-loss findings were found, but there are robustness and UI-state defects that can leave users without the expected progress/stop surface or produce incorrect active-progress layout behavior.

## Warnings

### WR-01: Progress and preview window interactions are fire-and-forget, hiding failures

**File:** `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs:139,189`
**Issue:** `StartCleaningAsync` and `PreviewAsync` discard the task returned by the UI interaction handlers. If the progress or preview window handler throws (for example, window construction, DataContext setup, or Avalonia show failure), the exception is unobserved and cleaning/preview continues as though the UI opened successfully. For cleaning, that can leave the user without the intended progress window and stop controls during a live xEdit run.
**Fix:** Await the interactions inside the existing `try` blocks so failures are logged and surfaced by the existing error handling.

```csharp
await _showProgressInteraction.Handle(Unit.Default);

// ...

await _showPreviewInteraction.Handle(results);
```

### WR-02: Stop and kill command failures are not handled

**File:** `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs:220-253`; `AutoQAC/ViewModels/ProgressViewModel.cs:198-222,235-240`
**Issue:** The stop paths await orchestrator termination and dialog calls without any error boundary. If `StopCleaningAsync`, `ForceStopCleaningAsync`, or a dialog call throws, the async command propagates the exception instead of showing safe failure copy or updating the persistent stop warning. This is especially risky in the force-termination path because the UI may fail before telling the user that xEdit might still be running.
**Fix:** Wrap stop/force-stop command bodies in `try/catch`. In `CleaningCommandsViewModel`, log via `_logger` and show the shared safe failure dialog. In `ProgressViewModel`, set `StopOutcomeWarningText` and show the shared safe failure dialog.

```csharp
try
{
    var stopResult = await _orchestrator.StopCleaningAsync();
    // existing stop escalation flow
}
catch (Exception ex)
{
    _logger.Error(ex, "StopCleaningAsync failed");
    await _messageDialog.ShowErrorAsync(
        StopTerminationDialogContent.ForceFailureTitle,
        StopTerminationDialogContent.ForceFailureMessage);
}
```

### WR-03: Results summary panel is visible during active cleaning

**File:** `AutoQAC/Views/ProgressWindow.axaml:236-237`
**Issue:** The results-summary grid is visible whenever `IsPreviewMode` is false, even when `IsShowingResults` is false and the active-cleaning panel is visible. Because the summary grid is declared after the active grid in the same `Panel`, it is layered above the active progress UI during cleaning. Even with most child controls hidden, the extra visible overlay can interfere with hit-testing/focus behavior and makes the window state model incorrect.
**Fix:** Gate the summary panel on both “showing results” and “not preview mode”, preferably through an explicit ViewModel property so the XAML does not need a multi-binding.

```csharp
public bool IsResultsSummaryVisible => IsShowingResults && !IsPreviewMode;
```

```xml
<Grid Margin="20" RowDefinitions="Auto,Auto,Auto,Auto,*,Auto"
      IsVisible="{Binding IsResultsSummaryVisible}">
```

Ensure `IsResultsSummaryVisible` raises change notifications when either `IsShowingResults` or `IsPreviewMode` changes.

---

_Reviewed: 2026-05-01T09:43:28Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: deep_
