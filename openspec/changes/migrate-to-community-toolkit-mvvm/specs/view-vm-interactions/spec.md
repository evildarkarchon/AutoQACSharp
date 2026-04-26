## ADDED Requirements

### Requirement: Dialog interactions use an in-house Interaction abstraction
ViewModel-to-View dialog requests (Settings, Skip List, Progress, Preview, Restore, About, Cleaning Results) SHALL flow through an in-house generic `Interaction<TInput, TOutput>` type owned by AutoQAC, not through `ReactiveUI.Interaction<TInput, TOutput>`. The abstraction SHALL expose:
- `IDisposable RegisterHandler(Func<TInput, Task<TOutput>> handler)` — invoked by the View code-behind to attach the single handler that creates and shows the dialog window.
- `Task<TOutput> Handle(TInput input)` — invoked by the ViewModel to raise the interaction and await the registered handler's response.

#### Scenario: Interaction type declaration
- **WHEN** a developer reads the `Interaction<,>` source file
- **THEN** the type SHALL live under an AutoQAC-owned namespace (e.g. `AutoQAC.Services.UI.Interactions`)
- **AND** the type SHALL NOT be `ReactiveUI.Interaction<,>`
- **AND** no `using ReactiveUI;` directive SHALL be present in its file

#### Scenario: Single handler registration
- **WHEN** `RegisterHandler` is called twice on the same `Interaction<TInput, TOutput>` instance without disposing the first registration
- **THEN** the second call SHALL throw `InvalidOperationException`

#### Scenario: Handle without registration
- **WHEN** `Handle` is called on an `Interaction<TInput, TOutput>` that has no registered handler
- **THEN** the call SHALL throw `InvalidOperationException`

#### Scenario: Round-trip request/response
- **WHEN** a ViewModel calls `await interaction.Handle(input)` and the registered handler returns `output`
- **THEN** the awaited result SHALL equal `output`

### Requirement: MainWindowViewModel owns the dialog interaction surface
`MainWindowViewModel` SHALL declare one public `Interaction<TInput, TOutput>` field per dialog flow listed in Decision 1 of the design (Progress, Preview, Cleaning Results, Settings, Skip List, Restore, About). Each interaction SHALL preserve the input and output types of the equivalent pre-migration ReactiveUI interaction.

#### Scenario: Interaction surface preserved
- **WHEN** a developer compares `MainWindowViewModel`'s public interaction properties before and after the migration
- **THEN** the names, input types, and output types SHALL be unchanged
- **AND** only the underlying `Interaction<,>` type origin SHALL differ (AutoQAC-owned vs. ReactiveUI)

### Requirement: View code-behind registers handlers via RegisterHandler
`MainWindow.axaml.cs` (and any other View that hosts dialog interactions) SHALL register a single handler per interaction by calling `RegisterHandler(...)` and SHALL hold the returned `IDisposable` for the lifetime of the View. The handler SHALL set the interaction's output exactly once before returning.

#### Scenario: Handler registration on construction
- **WHEN** `MainWindow` is constructed with a `MainWindowViewModel`
- **THEN** the constructor SHALL call `RegisterHandler` on each interaction the ViewModel exposes
- **AND** SHALL store the returned `IDisposable` for cleanup

#### Scenario: Handler returns the dialog result
- **WHEN** a registered handler completes its dialog flow
- **THEN** the handler SHALL set the interaction output to the dialog's result before returning
- **AND** the awaiting ViewModel SHALL receive that output as the result of `Handle`

### Requirement: WeakReferenceMessenger is not introduced for dialog flows
The migration SHALL NOT replace dialog interactions with `WeakReferenceMessenger` request/response messages. The toolkit's messenger MAY be adopted in future work for cross-ViewModel communication, but dialog flows in this milestone use the typed `Interaction<,>` abstraction only.

#### Scenario: No messenger usage for dialogs
- **WHEN** a developer searches `AutoQAC/ViewModels/**/*.cs` and `AutoQAC/Views/**/*.cs` for `WeakReferenceMessenger`
- **THEN** no usages tied to dialog flows (Settings, Skip List, Progress, Preview, Restore, About, Cleaning Results) SHALL be found
