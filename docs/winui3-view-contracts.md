# WinUI 3 View Contracts

This document captures the current Avalonia view behavior that the WinUI 3 views must preserve during Phase 3 of the migration. The ViewModels and services should remain the behavioral source of truth; views own only window/dialog lifecycle, ownership, and UI-specific event wiring.

## MainWindow Interaction Hub

`AutoQAC/Views/MainWindow.axaml.cs` registers seven `Interaction` handlers from `MainWindowViewModel` and disposes all registrations in `OnClosed`.

| Interaction | Input / output | Current presentation | Required lifecycle behavior |
| --- | --- | --- | --- |
| `ShowSettingsInteraction` | `Unit` / `bool` saved flag | Modal dialog | Create `SettingsViewModel`, call `LoadSettingsAsync` before showing, return `false` for canceled/null results, dispose the VM after close. |
| `ShowSkipListInteraction` | `Unit` / `bool` saved flag | Modal dialog | Create `SkipListViewModel`, call `LoadSkipListAsync` before showing, return `false` for canceled/null results, dispose the VM after close. |
| `ShowProgressInteraction` | `Unit` / `Unit` | Non-modal secondary window | Create `ProgressViewModel`, set it as the window data context, wire `CloseRequested` to close the window, and dispose the VM from the window `Closed` event with an idempotent guard. This composes with `ProgressWindow`'s own disposal guard. |
| `ShowPreviewInteraction` | `List<DryRunResult>` / `Unit` | Non-modal secondary window | Create `ProgressViewModel`, call `LoadDryRunResults`, set title to `Dry-Run Preview`, wire `CloseRequested` to close the window, and rely on `ProgressWindow.OnClosed` for VM disposal. |
| `ShowRestoreInteraction` | `Unit` / `Unit` | Modal dialog | Create `RestoreViewModel`, pass the current game data folder to `LoadSessionsAsync` before showing, dispose the VM after close. |
| `ShowAboutInteraction` | `Unit` / `Unit` | Modal dialog | Create `AboutViewModel` with the injected `IUiFrameworkVersionProvider`, show, and return when closed. |
| `ShowCleaningResultsInteraction` | `CleaningSessionResult` / `Unit` | Modal dialog | Create `CleaningResultsViewModel` with the session result plus logger/file dialog services, show, and return when closed. |

## Child Window Contracts

| View | Close contract | Required behavior |
| --- | --- | --- |
| `ProgressWindow` | `ProgressViewModel.CloseRequested` as `EventHandler` closes the window. | On data-context changes, unsubscribe from the old VM and subscribe to the new VM. `OnClosing` must cancel while `IsCleaning` is true. `OnClosed` must call the shared disposal path. The disposal path must remove `DataContextChanged`, unsubscribe from `CloseRequested`, dispose the VM, and short-circuit repeated disposal. |
| `SettingsWindow` | `SettingsViewModel.CloseRequested` as `Action<bool>` closes with that result. | Subscribe/unsubscribe on data-context changes and unsubscribe again on close. |
| `SkipListWindow` | `SkipListViewModel.CloseRequested` as `Action<bool>` closes with that result. | Match the Settings window subscription and cleanup pattern. |
| `RestoreWindow` | `RestoreViewModel.CloseRequested` as `EventHandler` closes the window. | Execute `LoadSessionsCommand` from `OnOpened` and unsubscribe from `CloseRequested` on close. |
| `CleaningResultsWindow` | `CleaningResultsViewModel.CloseRequested` as `EventHandler` closes the window. | Unsubscribe from `CloseRequested` on close. |
| `AboutWindow` | Close button click closes the window. | Display read-only VM state and keep the window free of business logic. |
| `MessageDialog` | `MessageDialogViewModel.CloseRequested` as `Action<MessageDialogResult>` closes with that result. | Preserve expandable details behavior and the details button label toggle. In WinUI this should become a `ContentDialog` or equivalent serialized dialog flow. |
| `PartialFormsWarningDialog` | `PartialFormsWarningViewModel.CloseRequested` as `Action<bool>` closes with that result. | Keep the existing subscription/cleanup behavior. The dialog is registered in DI but currently awaits a Partial Forms call site. |

## WinUI Mapping Notes

Per `docs/winui3-migration-plan.md`, Phase 3 should map these surfaces as follows:

- `MessageDialog`, `PartialFormsWarningDialog`, Settings, SkipList, and About should become `ContentDialog` flows unless a later design explicitly chooses in-window navigation.
- Progress, Restore, and CleaningResults should remain secondary `Window` surfaces.
- Modal result flows currently expressed as `ShowDialog<bool?>` should map to `ContentDialog` results or to modal secondary windows only where owner-blocking behavior is required.
- Existing source-contract tests in `AutoQAC.Tests/Views/ViewSubscriptionLifecycleTests.cs` cover the SkipList subscription lifecycle, the ProgressWindow disposal guard, and the MainWindow progress path. Update those tests in Phase 4 when `.axaml` files are replaced by WinUI `.xaml` files.
