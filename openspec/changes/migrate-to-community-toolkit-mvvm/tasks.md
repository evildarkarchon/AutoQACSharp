## 1. Package and project setup

- [ ] 1.1 Add `<PackageReference Include="CommunityToolkit.Mvvm" Version="8.*" />` to `AutoQAC/AutoQAC.csproj` (do not yet remove `ReactiveUI.Avalonia`).
- [ ] 1.2 Verify `dotnet build AutoQACSharp.slnx` succeeds with both packages present and no warnings introduced.
- [ ] 1.3 Confirm the source generator runs (e.g. add a throwaway `[ObservableProperty] private int _probe;` in a temporary scratch class, build, see `Probe` property exist, then delete).

## 2. Infrastructure: IUiDispatcher

- [ ] 2.1 Create `AutoQAC/Services/UI/IUiDispatcher.cs` exposing `void Post(Action action)` and `Task InvokeAsync(Func<Task> action)`.
- [ ] 2.2 Create `AutoQAC/Services/UI/AvaloniaUiDispatcher.cs` implementing `IUiDispatcher` via `Avalonia.Threading.Dispatcher.UIThread`.
- [ ] 2.3 Register `IUiDispatcher` -> `AvaloniaUiDispatcher` as a singleton in `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` (add to `AddUiServices` or the appropriate group).
- [ ] 2.4 Create `AutoQAC.Tests/TestInfrastructure/SynchronousUiDispatcher.cs` implementing `IUiDispatcher` by running callbacks inline on the calling thread.
- [ ] 2.5 Add a unit test verifying `SynchronousUiDispatcher.Post` runs its action before `Post` returns.

## 3. Infrastructure: in-house Interaction abstraction

- [ ] 3.1 Create `AutoQAC/Services/UI/Interactions/Interaction.cs` declaring `public sealed class Interaction<TInput, TOutput>` with `RegisterHandler` (returning `IDisposable`) and `Handle` (returning `Task<TOutput>`).
- [ ] 3.2 Implement a tiny `Disposable.Create(Action)` helper in the same namespace (or an `AnonymousDisposable` private class) so the `Interaction` file does not need `System.Reactive`.
- [ ] 3.3 Add unit tests in `AutoQAC.Tests/Services/UI/InteractionTests.cs` covering: register-then-handle round trip, double-register throws, handle-without-handler throws, dispose-then-register works.

## 4. ViewModelBase

- [ ] 4.1 Update `AutoQAC/ViewModels/ViewModelBase.cs` to inherit from `CommunityToolkit.Mvvm.ComponentModel.ObservableObject`. Remove `using ReactiveUI;`.
- [ ] 4.2 Build the solution; expect compile errors in concrete ViewModels — this is the migration backlog.

## 5. Migrate leaf ViewModels

- [ ] 5.1 Migrate `AutoQAC/ViewModels/AboutViewModel.cs` to `partial`, `[ObservableProperty]`, `[RelayCommand]`. Remove ReactiveUI imports. Build clean.
- [ ] 5.2 Migrate `AutoQAC/ViewModels/MessageDialogViewModel.cs`. Build clean.
- [ ] 5.3 Migrate `AutoQAC/ViewModels/PartialFormsWarningViewModel.cs`. Build clean.
- [ ] 5.4 Run the existing test suite for these VMs and fix any breakage.

## 6. Migrate dialog ViewModels

- [ ] 6.1 Migrate `AutoQAC/ViewModels/SettingsViewModel.cs` (61 ReactiveUI hits — methodically convert each property and command, preserve `LoadSettingsAsync` behavior).
- [ ] 6.2 Migrate `AutoQAC/ViewModels/SkipListViewModel.cs`.
- [ ] 6.3 Migrate `AutoQAC/ViewModels/RestoreViewModel.cs`.
- [ ] 6.4 Migrate `AutoQAC/ViewModels/CleaningResultsViewModel.cs`.
- [ ] 6.5 Run dialog-related tests; fix any breakage caused by source-generator naming or `CanExecute` recomputation differences.

## 7. Migrate sub-ViewModels

- [ ] 7.1 Migrate `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs` (48 hits — biggest file). Convert async-validating properties to manual `SetProperty` setters where partial methods don't fit, otherwise to `[ObservableProperty]`.
- [ ] 7.2 Migrate `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs`.
- [ ] 7.3 Migrate `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` — replace `WhenAnyValue(x => x.CanStartCleaning)` driven `ReactiveCommand` setup with `[RelayCommand(CanExecute = nameof(CanStartCleaning))]` plus `[NotifyCanExecuteChangedFor(...)]` on each input property. Replace the `RxSchedulers.MainThreadScheduler.Schedule(...)` block in `OnStateChanged` with `_uiDispatcher.Post(() => ApplyState(state))`. Inject `IUiDispatcher` via the constructor.
- [ ] 7.4 Migrate `AutoQAC/ViewModels/ProgressViewModel.cs` — convert all `RaiseAndSetIfChanged` properties to `[ObservableProperty]` (the file has the largest number of mechanical conversions).
- [ ] 7.5 Run all VM tests; fix breakage.

## 8. Migrate MainWindowViewModel and the dialog interaction surface

- [ ] 8.1 Migrate `AutoQAC/ViewModels/MainWindowViewModel.cs`. Replace each `Interaction<TInput, TOutput>` (ReactiveUI) field with the AutoQAC-owned `Interaction<TInput, TOutput>`. Keep names and types.
- [ ] 8.2 Replace the `stateService.StateChanged.ObserveOn(RxSchedulers.MainThreadScheduler).Subscribe(OnStateChanged)` block with `stateService.StateChanged.Subscribe(state => _uiDispatcher.Post(() => OnStateChanged(state)))`. Inject `IUiDispatcher` into the constructor.
- [ ] 8.3 Update `Dispose()` to dispose the single `IDisposable?` field (previously inside `CompositeDisposable`) and the sub-VMs.
- [ ] 8.4 Update `AutoQAC/Views/MainWindow.axaml.cs`: change the `using ReactiveUI;` import to the AutoQAC interactions namespace; change `IInteractionContext<TIn,TOut>` handler signatures to the new abstraction's `Func<TInput, Task<TOutput>>` shape; store the `IDisposable` returned from each `RegisterHandler` call in a field; dispose them on window close.
- [ ] 8.5 Update any other code-behind that references ReactiveUI's `Interaction` (search for `IInteractionContext` and `RegisterHandler` across `AutoQAC/Views/**/*.cs`).

## 9. Wire up DI changes

- [ ] 9.1 Update `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` so all ViewModel registrations that now depend on `IUiDispatcher` resolve correctly through DI.
- [ ] 9.2 Verify `AutoQAC/App.axaml.cs` still resolves `MainWindowViewModel` and `MainWindow` after the constructor signatures change.
- [ ] 9.3 Run the full test suite; fix any DI resolution breakage.

## 10. Drop ReactiveUI from Program.cs

- [ ] 10.1 Update `AutoQAC/Program.cs` to remove `.UseReactiveUI(builder => builder.WithAvalonia())` from the `AppBuilder` chain.
- [ ] 10.2 Remove the `using ReactiveUI.Avalonia;` import.
- [ ] 10.3 Build and run the app manually; confirm the main window opens and basic navigation works.

## 11. Test infrastructure migration

- [ ] 11.1 Replace `AutoQAC.Tests/TestInfrastructure/RxAppSchedulerTestCollection.cs` with a `UiDispatcherTestCollection` (or remove the collection entirely if no test needs collection-scoped serialization).
- [ ] 11.2 Update every test that previously used `[Collection("RxApp scheduler")]` or `RxAppMainThreadSchedulerScope` / `ImmediateMainThreadSchedulerTestBase` — port them to a `SynchronousUiDispatcher` constructor argument (or its NSubstitute substitute) instead.
- [ ] 11.3 Update `AutoQAC.Tests/TestInfrastructure/TestAssemblyInitializer.cs` (newly added on this branch) if it touches Rx scheduler state.
- [ ] 11.4 Search `AutoQAC.Tests/` for `RxSchedulers`, `WhenAnyValue`, `ReactiveCommand`, `CompositeDisposable`, `RaiseAndSetIfChanged`; replace each occurrence or assert that none remain.
- [ ] 11.5 Run `dotnet test AutoQACSharp.slnx`; iterate until fully green.

## 12. Remove ReactiveUI and prune System.Reactive

- [ ] 12.1 Remove `<PackageReference Include="ReactiveUI.Avalonia" ... />` from `AutoQAC/AutoQAC.csproj`.
- [ ] 12.2 Remove any direct `<PackageReference Include="ReactiveUI" />` from `AutoQAC.csproj` and `AutoQAC.Tests.csproj`.
- [ ] 12.3 Remove `<PackageReference Include="System.Reactive" />` from `AutoQAC.csproj` if it was top-level. Build; if `Services/State/StateService.cs` or `Services/Configuration/ConfigurationService.cs` lose their transitive reference, add `System.Reactive` back as an explicit reference scoped to those services' needs.
- [ ] 12.4 Delete `RxSchedulers.cs` (or its equivalent file holding `MainThreadScheduler`) once no production or test code references it.
- [ ] 12.5 Final grep: `AutoQAC/**/*.cs` for `ReactiveUI`, `System.Reactive`, `WhenAnyValue`, `RaiseAndSetIfChanged`, `ReactiveCommand`, `ObservableAsPropertyHelper`, `RxApp` — every hit is a leftover to clean.

## 13. Documentation and final verification

- [ ] 13.1 Update `CLAUDE.md`: replace the "ReactiveUI 11.3.8" line with the CommunityToolkit.Mvvm version; replace the "Reactive Patterns" section with the CommunityToolkit equivalents (`[ObservableProperty]`, `[RelayCommand]`, `[NotifyPropertyChangedFor]`, `[NotifyCanExecuteChangedFor]`, `IUiDispatcher`, in-house `Interaction<,>`).
- [ ] 13.2 Update any other docs or comments that reference ReactiveUI patterns (search `AutoQAC/**/*.md`, `AutoQAC/**/*.cs` XML docs).
- [ ] 13.3 Run `dotnet build AutoQACSharp.slnx -c Release` and confirm a clean Release build.
- [ ] 13.4 Run `dotnet test AutoQACSharp.slnx` one last time; suite is fully green with no skips.
- [ ] 13.5 Manual smoke test: launch the app, configure xEdit, run a dry-run preview, run a one-plugin clean (Skyrim SE), open Settings/SkipList/About dialogs, verify cleaning results window populates, verify both graceful Stop and force-terminate paths work.
- [ ] 13.6 Run `openspec verify-change migrate-to-community-toolkit-mvvm` (or the `/opsx:verify` skill) and resolve any reported gaps.
