## Context

AutoQAC currently builds its ViewModel layer on `ReactiveUI.Avalonia` (12.0.1 on this branch). Every ViewModel inherits `ReactiveObject`, every command is a `ReactiveCommand`, every derivation goes through `WhenAnyValue` / `ObservableAsPropertyHelper`, and every dialog flows through `ReactiveUI.Interaction<TIn,TOut>`. The View code-behind (`MainWindow.axaml.cs`) registers handlers against those interactions. UI-thread marshaling for `IStateService.StateChanged` uses `RxSchedulers.MainThreadScheduler`, and the test project provides `RxAppMainThreadSchedulerScope` to swap that scheduler in tests.

CommunityToolkit.Mvvm 8.x is the actively maintained, source-generator–based MVVM library that the Avalonia documentation now treats as the recommended default. It provides `ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`, `[NotifyPropertyChangedFor]`, `[NotifyCanExecuteChangedFor]`, `ObservableValidator`, and `WeakReferenceMessenger` — covering every pattern AutoQAC currently uses without pulling in `System.Reactive`. Source generators require ViewModels to be declared `partial`.

Constraints:
- AXAML bindings (e.g. `{Binding Commands.StartCleaningCommand}`) must keep working without changes — the migration should be transparent to XAML markup.
- Services in `AutoQAC/Services` (e.g. `ConfigurationService` debounce, `StateService` `BehaviorSubject`) keep their `System.Reactive` dependency. The Rx removal is scoped to the ViewModel layer and the dialog abstraction.
- Cleaning workflow must remain bit-identical: same `CanExecute` semantics, same UI-thread marshaling guarantees, no new races during sequential plugin cleaning.
- Existing tests must keep passing or be updated in lockstep; no test should be skipped to land the migration.

Stakeholders: solo maintainer (`evildarkarchon`). The migration lives on the `community-toolkit` branch and lands as one squash merge into `main`.

## Goals / Non-Goals

**Goals:**
- Replace ReactiveUI as the ViewModel framework with CommunityToolkit.Mvvm.
- Remove `ReactiveUI.Avalonia`, `ReactiveUI`, and `System.Reactive` from the `AutoQAC` project's direct dependencies (services that still need Rx pull it back in directly).
- Keep all current AXAML bindings working without rewriting markup.
- Keep all current dialog interactions working (Settings, Skip List, Progress, Preview, Restore, About, Cleaning Results) with equivalent input/output types and timing.
- Provide a testable UI-thread dispatch abstraction so ViewModels can be tested without standing up a real Avalonia dispatcher.
- Preserve the strict MVVM boundary: Views own dialog/window lifetime, ViewModels never reference Avalonia controls.
- Land the migration as a single coherent change with a green test suite at the end.

**Non-Goals:**
- Replacing the Rx-based debounce pipeline inside `ConfigurationService` or the `BehaviorSubject` inside `StateService`. Services keep using Rx where it earns its keep.
- Migrating the Avalonia SDK version (already done on this branch).
- Changing the xEdit cleaning workflow, log parsing, or any business logic.
- Adopting the WeakReferenceMessenger as a general application bus. We use it (or a similar abstraction) only where it directly replaces `Interaction<TIn,TOut>`, not as a pretext to rewrite cross-VM communication that already works through `IStateService`.
- Introducing `ObservableValidator` for existing validation. Today's validation is hand-rolled `List<ValidationError>` plumbing in `CleaningCommandsViewModel`; rewriting it on `ObservableValidator` is an attractive future task but is out of scope for this migration.
- Refactoring service interfaces or business-logic ownership.

## Decisions

### Decision 1: Adopt CommunityToolkit.Mvvm 8.x source generators by default

Use `ObservableObject` as the new `ViewModelBase` base class. Use `[ObservableProperty]` on private underscore-prefixed fields to generate the public properties (`_currentPlugin` → `CurrentPlugin`). Use `[RelayCommand]` to generate command properties from methods.

This means every concrete ViewModel becomes `public sealed partial class …ViewModel : ViewModelBase` (the `partial` keyword is required so the source generator can emit the other half of the class).

**Why:** This is the toolkit's canonical pattern, the Avalonia docs recommend it, and the generators eliminate the manual `_field` / `RaiseAndSetIfChanged` boilerplate that dominates the current ViewModels. The compile-time generation cost is minimal and there is no runtime cost beyond standard `INotifyPropertyChanged`.

**Alternative considered:** Keep manual property setters using the C# 13 `field` keyword pattern (`get; set => SetProperty(ref field, value);`). This works and is what some current ViewModels already use (`CleaningCommandsViewModel`). It avoids the `partial` requirement and keeps property sites readable as plain C#. We **allow** this style as a secondary option for properties that need custom logic in the setter (logging, side effects beyond `On…Changed` partial methods) but make `[ObservableProperty]` the default.

### Decision 2: Replace `ReactiveCommand` with `[RelayCommand]`

Convert every `ReactiveCommand.CreateFromTask(…)` and `ReactiveCommand.Create(…)` to a method annotated with `[RelayCommand]`. Async methods generate an `IAsyncRelayCommand`; sync methods generate an `IRelayCommand`. The generator names the property by appending `Command` to the method name (e.g. `private async Task StartCleaningAsync()` → `StartCleaningAsyncCommand`), so we either rename the methods to drop the `Async` suffix on the command itself (e.g. `StartCleaning`) or use `[RelayCommand(Name = "StartCleaningCommand")]` to keep the public surface stable for XAML.

**Decision:** Rename the private command methods so the generated command name matches the existing XAML. For example, the existing `StartCleaningCommand` is generated from `private Task StartCleaning()` rather than from `StartCleaningAsync`. The internal helper that does the actual work (formerly `StartCleaningAsync`) becomes a private method called from inside `StartCleaning`. This keeps XAML bindings unchanged at the cost of small renames in the C# files.

**Why over alternatives:** `[RelayCommand(Name = …)]` works but every command would need it, doubling the noise. Renaming methods is a one-time edit and matches the toolkit's idiomatic shape.

### Decision 3: Replace `WhenAnyValue` / `CombineLatest` derivations with attribute-driven notifications

Every chained-property derivation becomes a computed getter plus `[NotifyPropertyChangedFor(nameof(DerivedProperty))]` annotations on each input property:

```csharp
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(CanStartCleaning))]
private bool _isCleaning;

public bool CanStartCleaning =>
    !IsCleaning && PluginsToClean.Count > 0 && !string.IsNullOrEmpty(XEditExecutablePath);
```

For commands gated on derived state, also add `[NotifyCanExecuteChangedFor(nameof(StartCleaningCommand))]` on each input. Where the current code uses `WhenAnyValue` on an external observable (e.g. `IStateService.StateChanged`), the existing `OnStateChanged(AppState)` callback that dispatches into sub-ViewModels stays as-is — the sub-ViewModels just write directly to their own `[ObservableProperty]`-backed fields, which raises change notifications and triggers `[NotifyCanExecuteChangedFor]` automatically.

`ObservableAsPropertyHelper<T>` derivations (e.g. `IsMutagenSupported`, `RequiresLoadOrderFile`) become plain readonly computed getters with `[NotifyPropertyChangedFor]` on the inputs they depend on.

**Why:** This trades a runtime Rx graph for compile-time wiring. It is cheaper at startup, easier to read, and eliminates the dependency on Rx in ViewModels. The trade-off is that change-propagation chains become local to each property; for AutoQAC's small ViewModel surface that is desirable, not a regression.

### Decision 4: Replace `ReactiveUI.Interaction<TIn,TOut>` with a small typed `Interaction<TIn,TOut>` of our own

Define a minimal abstraction in `AutoQAC.Infrastructure.Interactions` (or `AutoQAC.Services.UI`) that mirrors the API ViewModels and Views currently use:

```csharp
public sealed class Interaction<TInput, TOutput>
{
    private Func<TInput, Task<TOutput>>? _handler;

    public IDisposable RegisterHandler(Func<TInput, Task<TOutput>> handler)
    {
        if (_handler is not null)
            throw new InvalidOperationException("Handler already registered.");
        _handler = handler;
        return Disposable.Create(() => _handler = null);
    }

    public Task<TOutput> Handle(TInput input)
    {
        if (_handler is null)
            throw new InvalidOperationException("No handler registered.");
        return _handler(input);
    }
}
```

(The `Disposable.Create` helper is a tiny in-house equivalent — three lines — so we don't pull `System.Reactive` back in just for this.)

The shape (`Interaction<TIn,TOut>` with `RegisterHandler` + `Handle`) is intentionally identical to the ReactiveUI surface the code-behind uses today. Migration of `MainWindow.axaml.cs` becomes "change the import; the call sites stay."

**Alternative considered:** `WeakReferenceMessenger` request/response. The toolkit's canonical pattern uses a static messenger for cross-component communication and supports a request/response shape (`Send(new ConfirmDeleteRequest(…))` returns the reply). Two reasons we pass on it for AutoQAC's dialogs:
1. Each interaction in AutoQAC has exactly one well-known handler (the `MainWindow` code-behind). A point-to-point typed interaction is a better fit than a global pub/sub bus for a known 1:1 channel.
2. The static-singleton lifetime of `WeakReferenceMessenger.Default` complicates testing and tear-down. A typed `Interaction<>` field on `MainWindowViewModel` matches the existing ownership model exactly.

We can revisit `WeakReferenceMessenger` later if cross-VM messaging needs grow beyond `IStateService`.

### Decision 5: Replace `RxSchedulers.MainThreadScheduler` with an injected `IUiDispatcher`

Define a small interface in `AutoQAC.Services.UI`:

```csharp
public interface IUiDispatcher
{
    void Post(Action action);
    Task InvokeAsync(Func<Task> action);
}
```

Production implementation calls `Avalonia.Threading.Dispatcher.UIThread.Post` / `InvokeAsync`. Test implementation runs the action synchronously on the calling thread (or routes through the test's main-thread shim, mirroring what `ImmediateMainThreadSchedulerTestBase` did).

Inject `IUiDispatcher` into ViewModels that currently observe `IStateService.StateChanged` on `RxSchedulers.MainThreadScheduler` (i.e. `MainWindowViewModel`, `CleaningCommandsViewModel`, and any other VM that today does `.ObserveOn(RxSchedulers.MainThreadScheduler)`). The state subscription becomes:

```csharp
_stateSubscription = stateService.StateChanged.Subscribe(state =>
    _uiDispatcher.Post(() => OnStateChanged(state)));
```

`stateService.StateChanged` is still an `IObservable<AppState>` returned by the service layer (which legitimately uses Rx); the ViewModel's only Rx surface is calling `Subscribe` and storing the returned `IDisposable`. We can keep `System.Reactive` out of `using` directives in ViewModels by exposing the disposable as `IDisposable` directly.

**Why:** This isolates UI-thread marshaling behind one tiny interface that production wires to `Dispatcher.UIThread` and tests wire to a synchronous double. It removes the indirect ReactiveUI dependency without forcing tests to spin up a real Avalonia dispatcher.

### Decision 6: ViewModel disposal stays explicit (no Rx `CompositeDisposable`)

Current ViewModels use `System.Reactive.Disposables.CompositeDisposable` to track subscriptions. Post-migration, the only subscription a ViewModel typically holds is the `IDisposable` from `stateService.StateChanged.Subscribe(…)`. We replace the composite with a simple field plus null-safe disposal in `Dispose()`:

```csharp
private IDisposable? _stateSubscription;
public void Dispose() => _stateSubscription?.Dispose();
```

For ViewModels that hold multiple `IDisposable`s (rare — `MainWindowViewModel` will have at most a state subscription plus its sub-VMs to dispose), use a small local `List<IDisposable>` if needed, or just multiple fields. We do not introduce a new `CompositeDisposable`-equivalent abstraction; the existing call sites are few enough that explicit disposal stays readable.

### Decision 7: Keep `ViewModelBase` as a thin shell

```csharp
public abstract class ViewModelBase : ObservableObject { }
```

It exists only so existing `: ViewModelBase` declarations keep working and so we have a single insertion point if we ever want shared base behavior (e.g. an injected logger). It does **not** add `IDisposable` — VMs that need disposal continue to declare `: ViewModelBase, IDisposable` as they do today.

### Decision 8: `partial` everywhere, file scoped namespaces preserved

Every concrete ViewModel becomes `public sealed partial class`. The `partial` is non-negotiable for `[ObservableProperty]`/`[RelayCommand]` source generation. We update the project's coding conventions accordingly. File-scoped namespaces and one-public-type-per-file rules are unchanged.

### Decision 9: NSubstitute mocks for the new abstractions

Tests substitute `IUiDispatcher` so callbacks run synchronously. Tests substitute `Interaction<TIn,TOut>` indirectly by setting `RegisterHandler(input => …)` on the real instance — the abstraction is small enough that mocking it isn't worth a separate interface. Where tests previously used `RxAppMainThreadSchedulerScope` or `ImmediateMainThreadSchedulerTestBase`, they now construct ViewModels with a synchronous `IUiDispatcher` substitute.

### Decision 10: Migration order is bottom-up

Migrate in this order so each step compiles and tests pass:
1. Add `CommunityToolkit.Mvvm` package, leave `ReactiveUI` in place.
2. Add `IUiDispatcher` + production implementation; wire into DI; do not yet remove `RxSchedulers`.
3. Add the in-house `Interaction<TIn,TOut>` in a new namespace (`AutoQAC.Services.UI.Interactions` to avoid clashing with `ReactiveUI.Interaction`).
4. Migrate leaf ViewModels first (`AboutViewModel`, `MessageDialogViewModel`, `PartialFormsWarningViewModel`) — small, no commands, no interactions. Land them and run tests.
5. Migrate dialog ViewModels (`SettingsViewModel`, `SkipListViewModel`, `RestoreViewModel`, `CleaningResultsViewModel`).
6. Migrate sub-ViewModels (`ConfigurationViewModel`, `PluginListViewModel`, `CleaningCommandsViewModel`, `ProgressViewModel`).
7. Migrate `MainWindowViewModel` and switch its `Interaction<>` fields to the new type. Update `MainWindow.axaml.cs` to register handlers against the new abstraction (the call shape is identical).
8. Update `Program.cs` to drop `UseReactiveUI(...)`.
9. Update `AutoQAC.Tests` test infrastructure (`RxAppSchedulerTestCollection.cs`) — replace with an `IUiDispatcher`-based test fixture. Update individual tests that referenced the old fixture.
10. Remove `ReactiveUI.Avalonia` and `ReactiveUI` package references; remove `System.Reactive` from `AutoQAC.csproj` (services that still need it pull it back in directly via their own package references — likely `System.Reactive` stays in the project file via `ConfigurationService`/`StateService` needs).
11. Sweep for stragglers: any file still importing `ReactiveUI` or `System.Reactive.Linq` from a ViewModel.
12. Update `CLAUDE.md` to reflect the new ViewModel patterns.

After each step the project compiles and `dotnet test AutoQACSharp.slnx` is green.

## Risks / Trade-offs

- **Risk:** `[ObservableProperty]` requires `partial` — every concrete ViewModel touched. → **Mitigation:** mechanical change; do it as part of the leaf-up migration order so each VM is fully ported before moving on.

- **Risk:** Subtle behavior difference when replacing `WhenAnyValue` with `[NotifyPropertyChangedFor]`. ReactiveUI's `WhenAnyValue` debounces and skips initial duplicates; the toolkit fires on every set even if the new value equals the old. → **Mitigation:** `ObservableObject.SetProperty` already short-circuits when the value is equal (using the default `EqualityComparer`), so single-property notifications are equivalent. Multi-property derivations that previously coalesced via `CombineLatest` now fire once per input change, which is fine for `CanExecute` recomputation but means `OnPropertyChanged("Computed")` may raise more often. No correctness issue, slight binding-cost increase. AutoQAC's binding count is small enough that this is invisible.

- **Risk:** `RxSchedulers.MainThreadScheduler.Schedule(state, (_, currentState) => …)` in `CleaningCommandsViewModel.OnStateChanged` provides a specific marshaling shape (closure-passing). Replacing with `IUiDispatcher.Post(() => ApplyState(state))` captures the same `state` and runs on the UI thread; semantically equivalent. → **Mitigation:** keep the test that asserts UI-thread marshaling for state changes; if it doesn't exist, add one as part of the migration.

- **Risk:** Dropping `System.Reactive` from `AutoQAC.csproj` may transitively break services that rely on it. → **Mitigation:** services that use `System.Reactive` (`StateService`, `ConfigurationService`) get an explicit `<PackageReference Include="System.Reactive" />` if the transitive removal causes a missing-reference error. The dependency stays in the build graph; it just stops being a top-level concern of the ViewModel layer.

- **Risk:** Existing tests that swap `RxSchedulers.MainThreadScheduler` (`RxAppMainThreadSchedulerScope`) won't compile after the migration. → **Mitigation:** porting test infrastructure is step 9 in the migration order, not last. Tests that need UI-thread marshaling get an `IUiDispatcher` substitute that runs callbacks inline. The `RxAppSchedulerTestCollection` xUnit collection definition is replaced by a `UiDispatcherTestCollection` (or just removed if no test needs `[Collection(...)]` after the change).

- **Risk:** `Interaction<TIn,TOut>` in our own namespace can collide with `ReactiveUI.Interaction<,>` while both packages are in the project during migration. → **Mitigation:** put our type in `AutoQAC.Services.UI.Interactions`, and use fully qualified or `using` aliases in the transition window. After step 10 (ReactiveUI removed) we can rename if desired.

- **Trade-off:** Lose ReactiveUI's `IViewFor<T>` view-model location convention. AutoQAC doesn't use this pattern (DataContext is set explicitly in code-behind), so we lose nothing.

- **Trade-off:** Lose `ReactiveCommand`'s built-in error handling via `ThrownExceptions`. Today the ViewModels catch exceptions inside the command body anyway (every `StartCleaningAsync`, `RunPreviewAsync`, etc. has `try/catch`). `[RelayCommand]` does not surface a `ThrownExceptions` observable; uncaught exceptions become unhandled task exceptions. Since AutoQAC catches in-method, we don't regress.

- **Trade-off:** Source generators add a small compile-time cost and require a recent enough C# language version (we are on C# 13, well above the 8.0+ requirement). Tooling support in JetBrains Rider and Visual Studio is mature.

## Migration Plan

The migration plan is the ordered list in **Decision 10**. There is no production rollout — the app ships as a single executable to a single user (the maintainer), so "deploy" means "merge the branch."

**Rollback strategy:** If the migration introduces a regression we can't quickly diagnose, revert the squash-merge commit on `main`. The `community-toolkit` branch stays as a reference. There is no schema change, no data file change, no inter-process protocol change; the rollback surface is purely the `AutoQAC` codebase.

**Verification gates before merging to `main`:**
- `dotnet build AutoQACSharp.slnx` clean (no warnings introduced).
- `dotnet test AutoQACSharp.slnx` fully green (no skips, no quarantines).
- Manual smoke test: launch the app, configure xEdit, run a dry-run preview against a small load order, run a real clean of one plugin (Skyrim SE), verify Settings/SkipList/About dialogs work, verify the cleaning results window populates, verify Stop both gracefully and force-terminates.
- Re-read `CLAUDE.md` and the ViewModel section of the architecture notes to confirm the documented patterns match the migrated code.

## Open Questions

- Do any VMs need to keep a fully-manual property (no `[ObservableProperty]`) for non-trivial setter logic? Likely candidates: `ConfigurationViewModel.XEditExecutablePath` (which today triggers async validation). **Plan:** keep manual setters where the existing setter has side effects beyond raising notifications; convert to `[ObservableProperty]` + `partial void OnXEditExecutablePathChanged(string value)` where the side effect maps cleanly onto the partial method hook.
- Does `System.Reactive` need to remain a direct package reference on `AutoQAC.csproj` after migration? **Resolution path:** remove it; if `dotnet build` fails because `StateService` / `ConfigurationService` lose their transitive reference, add it back as an explicit dependency for those service files. Either outcome is fine.
- Should we also adopt `ObservableValidator` for the `ValidationErrors`/`HasValidationErrors` pattern in `CleaningCommandsViewModel`? **Resolution:** No — out of scope for this migration (see Non-Goals). Track as a follow-up if it becomes interesting.
