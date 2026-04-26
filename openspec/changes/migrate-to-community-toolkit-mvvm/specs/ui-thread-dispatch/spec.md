## ADDED Requirements

### Requirement: ViewModels marshal state callbacks via IUiDispatcher
ViewModels that observe `IStateService.StateChanged` (or any other background-thread observable) SHALL marshal their callbacks onto the UI thread by invoking an injected `IUiDispatcher` abstraction. ViewModels SHALL NOT call `RxSchedulers.MainThreadScheduler`, `RxApp.MainThreadScheduler`, or any other ReactiveUI-provided scheduler.

#### Scenario: State subscription marshals through IUiDispatcher
- **WHEN** `MainWindowViewModel` (or any sub-ViewModel) subscribes to `IStateService.StateChanged`
- **THEN** the subscription handler SHALL invoke `IUiDispatcher.Post(...)` (or `InvokeAsync`) before mutating ViewModel properties
- **AND** the mutation SHALL run on the UI thread in production
- **AND** no reference to `RxSchedulers.MainThreadScheduler` SHALL remain in any ViewModel file

#### Scenario: ViewModels accept IUiDispatcher via constructor
- **WHEN** a ViewModel needs UI-thread marshaling
- **THEN** the dependency SHALL be supplied via constructor injection
- **AND** the parameter type SHALL be `IUiDispatcher`
- **AND** the dependency SHALL be registered in `ServiceCollectionExtensions`

### Requirement: IUiDispatcher production implementation uses Avalonia Dispatcher.UIThread
The production registration of `IUiDispatcher` SHALL implement `Post(Action)` by calling `Avalonia.Threading.Dispatcher.UIThread.Post(action)` and SHALL implement `InvokeAsync(Func<Task>)` by calling `Dispatcher.UIThread.InvokeAsync(...)`.

#### Scenario: Post forwards to Dispatcher.UIThread.Post
- **WHEN** the production `IUiDispatcher.Post(action)` is called from a non-UI thread
- **THEN** the action SHALL be enqueued via `Dispatcher.UIThread.Post(action)`
- **AND** SHALL execute on the Avalonia UI thread

### Requirement: IUiDispatcher test double runs callbacks synchronously
A test-only `IUiDispatcher` implementation SHALL be provided in `AutoQAC.Tests/TestInfrastructure/` that runs `Post` and `InvokeAsync` callbacks synchronously on the calling thread. Tests requiring UI-thread marshaling SHALL inject this test double, NOT spin up a real Avalonia dispatcher.

#### Scenario: Synchronous test dispatch
- **WHEN** a test substitutes the test `IUiDispatcher` and triggers a state change
- **THEN** the resulting callback SHALL run before `Post` returns
- **AND** the test SHALL be able to assert on the post-callback ViewModel state without yielding

### Requirement: Removal of RxApp scheduler test infrastructure
The `RxAppSchedulerTestCollection`, `RxAppMainThreadSchedulerScope`, `RxAppEventLoopMainThreadSchedulerScope`, and `ImmediateMainThreadSchedulerTestBase` types in `AutoQAC.Tests/TestInfrastructure/RxAppSchedulerTestCollection.cs` SHALL be removed once all tests have been ported to the new `IUiDispatcher` test infrastructure. No test SHALL retain a `[Collection("RxApp scheduler")]` attribute after the migration.

#### Scenario: No RxApp scheduler references remain in tests
- **WHEN** a developer searches `AutoQAC.Tests/` for `RxSchedulers.MainThreadScheduler` or `RxAppMainThreadScheduler`
- **THEN** no occurrences SHALL be found in production or test code
