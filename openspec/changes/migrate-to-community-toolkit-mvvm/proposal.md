## Why

ReactiveUI is a heavy MVVM framework whose value to AutoQAC is shrinking: most ViewModels just need `INotifyPropertyChanged`, `ICommand`, and a way to invoke dialogs. The current code carries a large surface area (`Interaction<TInput,TOutput>`, `ReactiveCommand`, `WhenAnyValue`, `ObservableAsPropertyHelper`, `RxSchedulers.MainThreadScheduler`) plus the `System.Reactive` dependency chain. The CommunityToolkit.Mvvm package is the actively maintained, source-generator–based MVVM library that the wider .NET / Avalonia ecosystem now treats as the default — it produces less code, no Rx dependency, faster startup, and integrates cleanly with `Microsoft.Extensions.DependencyInjection`. Switching now (while the project is still small enough to migrate in one branch) keeps AutoQAC aligned with mainstream Avalonia 12 guidance and lets us delete a class of subtle scheduling/lifetime bugs that come with Rx-based view models.

## What Changes

- **BREAKING**: Replace `ReactiveUI.Avalonia` and the `ReactiveUI` package with `CommunityToolkit.Mvvm` (latest 8.x).
- **BREAKING**: Drop the `ReactiveUI` Avalonia integration in `Program.cs` (`UseReactiveUI(...)`).
- **BREAKING**: Convert `ViewModelBase` from `ReactiveObject` to `ObservableObject` (CommunityToolkit base class).
- **BREAKING**: Replace every `RaiseAndSetIfChanged`-style property with the `[ObservableProperty]` source generator (or hand-written `SetProperty` where the C# 13 `field` keyword pattern is preserved).
- **BREAKING**: Replace every `ReactiveCommand` with `[RelayCommand]`-generated commands (sync and async variants); preserve `CanExecute` semantics via partial `Can…` methods or observable-derived booleans.
- **BREAKING**: Replace `WhenAnyValue` / `CombineLatest` / `ObservableAsPropertyHelper` derivations with direct property change handling (`OnPropertyChanged` partial methods, computed getters, or explicit recompute calls). No Rx in the ViewModel layer.
- **BREAKING**: Replace ReactiveUI `Interaction<TInput,TOutput>` with a small in-house dialog interaction abstraction (e.g. `IDialogInteraction<TIn,TOut>` exposing an async `HandleAsync` with a registered handler) so views still own dialog lifetime. The public surface that View code-behind registers handlers against changes shape.
- **BREAKING**: Remove `RxSchedulers.MainThreadScheduler` usage from ViewModels; UI-thread marshaling for `IStateService.StateChanged` moves to `Avalonia.Threading.Dispatcher.UIThread.Post` (or an injected `IUiDispatcher` wrapper for testability).
- **BREAKING**: Remove the `System.Reactive` package reference from ViewModels (services that legitimately use Rx — `ConfigurationService` debounce pipeline, `IStateService` BehaviorSubject — keep their `System.Reactive` dependency, which stays scoped to `AutoQAC/Services`).
- Update `AutoQAC.Tests` test infrastructure: replace `RxAppSchedulerTestCollection` / `RxAppMainThreadSchedulerScope` with a test-friendly `IUiDispatcher` (or equivalent) so ViewModels stay testable without ReactiveUI's scheduler injection.
- Update DI registration in `ServiceCollectionExtensions` so DI wiring no longer depends on ReactiveUI types.
- Update XAML where ReactiveUI-specific markup is in use (e.g. `vm:` namespaces are unaffected, but any `<Interaction.Triggers>`-style markup or `ReactiveWindow<T>` / `ReactiveUserControl<T>` base classes get replaced with plain Avalonia `Window` / `UserControl`).
- Update `CLAUDE.md` and any in-repo docs that describe the reactive patterns to reflect the new conventions.

Out of scope (explicitly NOT changing in this milestone):
- Behavior of services in `AutoQAC/Services` (cleaning orchestration, process execution, configuration debounce pipeline). Internal Rx use inside services stays.
- Public service interfaces (`IStateService`, `ICleaningOrchestrator`, etc.).
- The `QueryPlugins` library (no UI dependencies).
- xEdit log parsing logic (separate ongoing milestone).
- Avalonia 11 → 12 SDK migration (already in flight on this branch via the modified `AutoQAC.csproj`; this change lives on top of that bump but does not own it).

## Capabilities

### New Capabilities
- `mvvm-framework`: Defines which MVVM library backs the View/ViewModel layer (CommunityToolkit.Mvvm), the required base class for ViewModels, the property-change pattern, and the command pattern. Establishes that the ViewModel layer SHALL NOT depend on `System.Reactive` or `ReactiveUI`.
- `view-vm-interactions`: Defines the dialog/window interaction contract that replaces ReactiveUI's `Interaction<TInput,TOutput>` — a request/response abstraction the ViewModel raises and the View code-behind registers a single handler for, with await semantics and registration lifetime tied to the View.
- `ui-thread-dispatch`: Defines how state-service notifications are marshaled to the UI thread without depending on `RxApp.MainThreadScheduler` — an `IUiDispatcher` (or equivalent injected abstraction) backed by `Dispatcher.UIThread` in production and a synchronous test double in tests.

### Modified Capabilities
- `resource-lifecycle`: The "View subscription disposal via CompositeDisposable" requirement currently presupposes Rx subscriptions in View code-behind. After this change, View code-behind no longer holds Rx subscriptions; disposal requirements move to "registered interaction handlers" and "ViewModel `IDisposable` propagation". The intent (no leaked subscriptions across window close / DataContext change / double-dispose) stays the same; the mechanism changes.

## Impact

- **Affected projects**: `AutoQAC` (every ViewModel, every code-behind that uses `Interaction<>`, `Program.cs`, `ServiceCollectionExtensions`, `ViewModelBase`), `AutoQAC.Tests` (`TestInfrastructure/RxAppSchedulerTestCollection.cs` and any tests that touch `RxSchedulers.MainThreadScheduler`).
- **Unaffected projects**: `QueryPlugins`, `QueryPlugins.Tests`, the Mutagen submodule, `AutoQAC Data/*.yaml`.
- **Package additions**: `CommunityToolkit.Mvvm` (and its `Microsoft.CodeAnalysis`-based source generator).
- **Package removals**: `ReactiveUI.Avalonia`, transitive `ReactiveUI`, `System.Reactive` from the `AutoQAC` project (services that still use Rx pull it back in directly).
- **Public API impact**: ViewModel public surfaces remain the same shape (same property names, same command names) so XAML bindings continue to work. The shape of the dialog interaction surface changes (no longer `ReactiveUI.Interaction<TIn,TOut>`), so `MainWindow.axaml.cs` and any other code-behind that registers handlers must be updated.
- **Risk**: regressions in cleaning workflow if UI-thread marshaling or `CanExecute` recomputation differ subtly from the ReactiveUI behavior. Mitigated by porting test infrastructure first and running existing ViewModel/integration tests against the new framework before merging.
- **Migration timing**: single branch (`community-toolkit`), squash-merge into `main`. No staged rollout; the app is desktop-only and there are no in-flight users to migrate.
