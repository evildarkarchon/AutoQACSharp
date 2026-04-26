## REMOVED Requirements

### Requirement: View subscription disposal via CompositeDisposable
**Reason**: This requirement was framed around ReactiveUI's `CompositeDisposable` and Rx subscriptions in View code-behind. After the CommunityToolkit.Mvvm migration, View code-behind no longer holds Rx subscriptions; it holds `IDisposable` registrations from the in-house `Interaction<TInput, TOutput>` abstraction and ad-hoc event subscriptions to `Window`/`UserControl` lifecycle events. The disposal intent is preserved by the new requirement "View handler and disposable registration cleanup" added in this change.
**Migration**: Replace any `CompositeDisposable` field in View code-behind with explicit `IDisposable?` fields (or a small list) holding the registrations returned from `interaction.RegisterHandler(...)` and any other `IDisposable` resources, and dispose them in the `Window`/`UserControl` close path.

## ADDED Requirements

### Requirement: View handler and disposable registration cleanup
All View code-behind registrations (interaction handlers obtained from `Interaction<TInput, TOutput>.RegisterHandler`, event handler subscriptions, and any `IDisposable` resources) SHALL be disposed when the View is deactivated, closed, or has its `DataContext` replaced. The implementation MAY use individual `IDisposable?` fields, a small list, or another lightweight mechanism — but SHALL NOT depend on `System.Reactive.Disposables.CompositeDisposable`.

#### Scenario: SkipListWindow handler cleanup
- **WHEN** `SkipListWindow` is closed
- **THEN** every `IDisposable` returned from interaction `RegisterHandler` calls in its code-behind SHALL be disposed
- **AND** any event handlers attached to the `SkipListViewModel` SHALL be detached

#### Scenario: ProgressWindow DataContext change
- **WHEN** `ProgressWindow.DataContext` changes from one `ProgressViewModel` instance to another
- **THEN** any registrations and event handlers tied to the previous `DataContext` SHALL be disposed
- **AND** new registrations and event handlers SHALL be created for the new `DataContext`

#### Scenario: MainWindow registration cleanup on close
- **WHEN** `MainWindow` is closed
- **THEN** every `IDisposable` returned from `RegisterHandler` for each of its interactions (Settings, SkipList, Progress, Preview, Restore, About, CleaningResults) SHALL be disposed
- **AND** no further `Handle` calls SHALL find a registered handler
