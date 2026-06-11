# WinUI 3 View Contracts

This document captures the WinUI 3 view behavior that must remain intact after the migration. The ViewModels and services are the behavioral source of truth; views own only window/dialog lifecycle, ownership, and UI-specific event wiring.

## MainWindow Interaction Hub

`AutoQAC/Views/MainWindow.xaml.cs` registers seven `Interaction` handlers from `MainWindowViewModel` and disposes all registrations in `OnClosed`.

| Interaction | Input / output | Current presentation | Required lifecycle behavior |
| --- | --- | --- | --- |
| `ShowSettingsInteraction` | `Unit` / `bool` saved flag | `ContentDialog` via `SettingsWindow` presenter | Create `SettingsViewModel`, call `LoadSettingsAsync` before showing, return `false` for canceled/null results, dispose the VM after close. |
| `ShowSkipListInteraction` | `Unit` / `bool` saved flag | `ContentDialog` via `SkipListWindow` presenter | Create `SkipListViewModel`, call `LoadSkipListAsync` before showing, return `false` for canceled/null results, dispose the VM after close. |
| `ShowProgressInteraction` | `Unit` / `Unit` | Non-modal secondary window | Create `ProgressViewModel`, set it as the window data context, wire `CloseRequested` to close the window, and dispose the VM from the window `Closed` event with an idempotent guard. This composes with `ProgressWindow`'s own disposal guard. |
| `ShowPreviewInteraction` | `List<DryRunResult>` / `Unit` | Non-modal secondary window | Create `ProgressViewModel`, call `LoadDryRunResults`, set title to `Dry-Run Preview`, wire `CloseRequested` to close the window, and rely on `ProgressWindow.OnClosed` for VM disposal. |
| `ShowRestoreInteraction` | `Unit` / `Unit` | Secondary `Window` awaited by `ShowWindowAndWaitAsync` | Create `RestoreViewModel`, pass the current game data folder to `LoadSessionsAsync` before showing, dispose the VM after close. |
| `ShowAboutInteraction` | `Unit` / `Unit` | `ContentDialog` via `AboutWindow` presenter | Create `AboutViewModel` with the injected `IUiFrameworkVersionProvider`, show, and return when closed. |
| `ShowCleaningResultsInteraction` | `CleaningSessionResult` / `Unit` | Secondary `Window` awaited by `ShowWindowAndWaitAsync` | Create `CleaningResultsViewModel` with the session result plus logger/file dialog services, show, and return when closed. |

## Child Window Contracts

| View | Close contract | Required behavior |
| --- | --- | --- |
| `ProgressWindow` | `ProgressViewModel.CloseRequested` as `EventHandler` closes the window. | On data-context changes, unsubscribe from the old VM and subscribe to the new VM. `AppWindow.Closing` must cancel while `IsCleaning` is true. `Closed` must call the shared disposal path. The disposal path must remove `Root.DataContextChanged`, `AppWindow.Closing`, and `Closed`; unsubscribe from `CloseRequested`; dispose the VM; and short-circuit repeated disposal. |
| `SettingsWindow` | `SettingsViewModel.CloseRequested` as `Action<bool>` closes with that result. | Use `ContentDialogPresenter.ShowBooleanAsync`, passing subscribe and unsubscribe delegates for `CloseRequested`; dispose the VM after the dialog closes. |
| `SkipListWindow` | `SkipListViewModel.CloseRequested` as `Action<bool>` closes with that result. | Match the Settings presenter subscription and cleanup pattern through `ContentDialogPresenter.ShowBooleanAsync`. |
| `RestoreWindow` | `RestoreViewModel.CloseRequested` as `EventHandler` closes the window. | `MainWindow.ShowRestoreAsync` calls `LoadSessionsAsync` before showing; the window unsubscribes from `CloseRequested` on close. |
| `CleaningResultsWindow` | `CleaningResultsViewModel.CloseRequested` as `EventHandler` closes the window. | Unsubscribe from `CloseRequested` on close. |
| `AboutWindow` | Content dialog close button. | Display read-only VM state and keep the dialog free of business logic. |
| `MessageDialogService` | Programmatic `ContentDialog` result mapped to `MessageDialogResult`. | Preserve expandable details behavior, dynamic button mapping, and serialized dialog display. |
| `PartialFormsWarningDialog` | `PartialFormsWarningViewModel.CloseRequested` as `Action<bool>` closes with that result. | Use the same `ContentDialogPresenter.ShowBooleanAsync` cleanup path. The dialog is registered in DI but currently awaits a Partial Forms call site. |

## WinUI Mapping Notes

The completed WinUI mapping is:

- `MessageDialogService`, `PartialFormsWarningDialog`, Settings, SkipList, and About use serialized `ContentDialog` flows with `XamlRoot` from `IWindowContextProvider`.
- Progress, Restore, and CleaningResults remain secondary `Window` surfaces.
- Modal result flows previously expressed as `ShowDialog<bool?>` now return through `ContentDialogPresenter.ShowBooleanAsync` or through the awaited secondary-window helper where a separate window is retained.
- Source-contract tests in `AutoQAC.Tests/Views/ViewSubscriptionLifecycleTests.cs` cover the SkipList presenter wiring, `ContentDialogPresenter` cleanup, the ProgressWindow disposal guard, and the MainWindow progress path.
