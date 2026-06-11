# AutoQAC: Avalonia → WinUI 3 Migration Plan

> Status: **Approved** — migration is go. Phase 0 pre-work on `main` is the starting point; no WinUI cutover branch work has started yet.
>
> Scope: `AutoQAC` desktop app only. `QueryPlugins`, `QueryPlugins.Tests`, and all non-UI services are unaffected. The app is already Windows-only (`net10.0-windows10.0.19041.0`), so there is no cross-platform capability being given up.
>
> Last Microsoft Learn refresh: June 2026, covering Windows App SDK 1.8 storage pickers, AppWindow modal windows, ContentDialog ownership, unpackaged deployment, and .NET 10 WinUI guidance.

## 1. Why this migration is tractable

The codebase was deliberately structured with strict MVVM boundaries, which keeps Avalonia coupling small and concentrated:

- **ViewModels are almost framework-free.** 12 of 14 ViewModel files have zero Avalonia imports. They use CommunityToolkit.Mvvm source generators, a custom `Interaction<T>` pattern, and service abstractions (`IUiDispatcher`, `IFileDialogService`, `IMessageDialogService`) — all of which carry over unchanged.
- **Business logic is untouched.** All of `Services/Backup`, `Cleaning`, `Configuration`, `GameDetection`, `MO2`, `Monitoring`, `Plugin`, `Process`, and `State` is framework-agnostic.
- **The UI seam already exists.** `IUiDispatcher`, `IFileDialogService`, `IMessageDialogService`, `CallbackObserver<T>`, and `Interaction<T>`/`Unit` were designed as abstractions; only their implementations are Avalonia-specific.

## 2. Complete inventory of Avalonia coupling

### 2.1 Bootstrap and lifetime

| File | Coupling |
|---|---|
| `AutoQAC/Program.cs` | `AppBuilder.Configure<App>()`, `UsePlatformDetect()`, `WithInterFont()`, DevTools, `StartWithClassicDesktopLifetime()` |
| `AutoQAC/App.axaml` + `App.axaml.cs` | `Application` base, `AvaloniaXamlLoader`, `IClassicDesktopStyleApplicationLifetime` (MainWindow assignment, `Shutdown`, `ShutdownRequested`), FluentTheme, DataGrid theme include, app-level converter resources, `hyperlink` button style |
| `AutoQAC/ViewLocator.cs` | `IDataTemplate` — **appears unused at runtime**; candidate for deletion rather than porting |
| `AutoQAC/AutoQAC.csproj` | Avalonia package references, `AvaloniaUseCompiledBindingsByDefault`, `<AvaloniaResource Include="Assets\**" />` |

### 2.2 UI services (`AutoQAC/Services/UI`)

| File | Verdict |
|---|---|
| `AvaloniaUiDispatcher.cs` | Replace with `WinUiDispatcher` over `DispatcherQueue` |
| `FileDialogService.cs` | Rewrite over Windows App SDK `Microsoft.Windows.Storage.Pickers` + active `WindowId`/`AppWindow` access |
| `MessageDialogService.cs` | Rewrite over `ContentDialog` (currently builds Avalonia windows programmatically) |
| `IUiDispatcher.cs`, `IFileDialogService.cs`, `IMessageDialogService.cs`, `CallbackObserver.cs`, `Interactions/Interaction.cs`, `Interactions/Unit.cs` | Keep as-is (framework-neutral) |

### 2.3 Views (10 windows, all rewritten)

| View | Notable features to port |
|---|---|
| `MainWindow` | Menu, `ComboBox` + item template + `GameTypeDisplayConverter`, path validation glyphs, `ListBox` with checkboxes, 7 `Interaction` handler registrations, child-window lifecycle management |
| `ProgressWindow` | Three overlay panels, determinate/indeterminate `ProgressBar`, hang/backup banners, close-guard while cleaning, VM dispose discipline |
| `SettingsWindow` | `NumericUpDown` → WinUI `NumberBox`, persistence banner, `IsDefault`/`IsCancel` buttons (no WinUI equivalent — handle via `KeyboardAccelerator`/explicit handlers) |
| `SkipListWindow` | Dual `ListBox`, manual entry |
| `RestoreWindow` | Session/plugin lists, inline restore progress, `OnOpened` → load command |
| `CleaningResultsWindow` | **Only `DataGrid` usage** — needs `CommunityToolkit.WinUI.Controls.DataGrid` or an `ItemsView`/`ListView` with grid columns |
| `AboutWindow` | Asset image, version grid (drop `AvaloniaVersion`, show Windows App SDK version), hyperlink buttons |
| `MessageDialog` | Becomes a `ContentDialog` (expandable details, dynamic buttons) |
| `PartialFormsWarningDialog` | Registered in DI but **not wired yet** (no call site) — not dead code; port as `ContentDialog` when Partial Forms support is connected |

### 2.4 Converters (`AutoQAC/Converters`)

- `GameTypeDisplayConverter`, `IsTrueConverter`/`IsFalseConverter`/`IsNotNullConverter` → reimplement against `Microsoft.UI.Xaml.Data.IValueConverter`, or better, replace most with `x:Bind` function bindings (WinUI's `x:Bind` can call VM methods/static functions directly, eliminating converter boilerplate).
- `IntEqualsConverter` → unused; delete.
- Avalonia built-ins used in XAML (`StringConverters.IsNotNullOrEmpty`, `ObjectConverters.IsNotNull`, `!` negation bindings) have **no WinUI equivalents** — replace with `x:Bind` functions or small static helper class.

### 2.5 ViewModels with direct coupling (2 files)

- `AboutViewModel.cs` — reads `Avalonia.Application` assembly version. Replace with Windows App SDK / app version.
- `CleaningCommandsViewModel.cs` — uses `IClassicDesktopStyleApplicationLifetime.Shutdown()` for Exit. Introduce an `IAppLifetime` abstraction (`Shutdown()`), implement over `Application.Current.Exit()`, and inject it. This also removes the last framework reference from the VM layer.

### 2.6 Tests affected (~14 tests across 5 files)

- `Views/ViewSubscriptionLifecycleTests.cs` (4) — source-reads `.axaml`/code-behind files; update file paths and contract strings.
- `Services/UI/FileDialogServiceTests.cs` (7) — reflection over `ParseFilter` using `FilePickerFileType`; rehost against the new picker implementation's filter parsing.
- `ViewModels/SettingsViewModelTests.cs` (1), `Integration/DependencyInjectionTests.cs` (1), `Phase11LogBoundaryTests.cs` (1) — source/string scans referencing `App.axaml.cs` and `SettingsWindow.axaml`; update paths.
- `TestInfrastructure/SynchronousUiDispatcher.cs` — already framework-neutral; unchanged.

## 3. Key WinUI 3 differences and risks

These are the things that will actually hurt; plan around them up front.

1. **No simple `ShowDialog()` equivalent, but modal windows exist.** WinUI 3 `Window` still has no Avalonia/WPF-style `ShowDialog()`. Current architecture opens Settings, SkipList, Restore, CleaningResults, About, and Progress as separate (sometimes modal) windows. Windows App SDK windowing now supports modal top-level windows via `OverlappedPresenter.IsModal`, but the modal window must have an owner, and setting that owner still requires Win32 interop. Options:
   - **Recommended:** Convert MessageDialog and PartialFormsWarning to `ContentDialog`; convert Settings/SkipList/About to `ContentDialog` or in-window navigation; keep Progress, Restore, and CleaningResults as secondary `Window`s. Use `OverlappedPresenter.IsModal` only where true owner-blocking behavior is worth the interop.
   - `ContentDialog` constraint: Microsoft Learn still warns that attempting to open multiple dialogs throws (wording varies between one per window and one per thread depending on article/API page). Keep `MessageDialogService` serialized and audit dialog flows such as backup-failure prompts while progress UI is active.
2. **Use Windows App SDK pickers, not legacy HWND-initialized UWP pickers.** In Windows App SDK 1.8+, `Microsoft.Windows.Storage.Pickers.FileOpenPicker`, `FileSavePicker`, and `FolderPicker` are the WinUI 3 path. They take a `WindowId` in the constructor and return lightweight `PickFileResult`/`PickFolderResult` path results, so they do **not** need `WinRT.Interop.InitializeWithWindow`. HWND interop is only needed if falling back to legacy `Windows.Storage.Pickers` or other WinRT UI objects that depend on `CoreWindow`. `FileDialogService` should therefore depend on an active `WindowId`/`AppWindow` provider, not an HWND-only provider.
3. **No `SizeToContent`.** `MessageDialog` relies on it; `ContentDialog` solves this naturally.
4. **Binding model change.** Avalonia compiled bindings (`x:DataType`) map to WinUI `{x:Bind}` (default `OneTime` — must specify `Mode=OneWay`/`TwoWay` explicitly; this is the most common porting bug). `Binding`-with-`DataContext` still works as a fallback.
5. **DataGrid is not in the box.** Use `CommunityToolkit.WinUI.Controls.DataGrid` (maintenance-mode but functional) or rework `CleaningResultsWindow` to a `ListView` with column headers. Given it's a single read-only results grid, `ListView` is the lower-risk choice.
6. **Packaging decision.** Recommend **unpackaged, self-contained Windows App SDK** deployment (`<WindowsPackageType>None</WindowsPackageType>`, `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>`) to preserve current xcopy-style distribution and relative-path behavior (`AutoQAC Data`, logs). Microsoft Learn now documents this as the right shape for xcopy/zip distribution, with the trade-off of a larger output and no package identity. `PublishSingleFile` is supported for unpackaged self-contained WinUI 3 apps on Windows App SDK 1.5+, but it extracts dependencies to a temp directory on first launch; prefer folder-based publish unless a single-file wrapper is explicitly desired.
7. **TFM / tooling.** Project can remain on `net10.0-windows10.0.19041.0` + `Microsoft.WindowsAppSDK` + `<UseWinUI>true</UseWinUI>`; current Microsoft Learn migration guidance includes .NET 10 examples. Still verify the exact Windows App SDK stable package and Visual Studio/tooling requirements at migration time.
8. **Threading.** `DispatcherQueue` replaces `Dispatcher.UIThread`. `DispatcherQueue.GetForCurrentThread()` is only valid on threads with a queue — capture the UI thread's queue once during app startup and inject it into `WinUiDispatcher`.
9. **Single-instance guard + startup dialogs.** Current code shows a warning dialog then `Shutdown()` during startup. In WinUI 3, showing a dialog before a window/XamlRoot exists is awkward — show the main window first or use `AppInstance` redirection from Windows App SDK (cleaner).

## 4. Phased migration plan

The recommended approach is a **branch-based big-bang for the view layer** with **incremental pre-work on `main`** to shrink the cutover. A side-by-side dual-UI approach is possible but not worth it for ~10 windows.

### Phase 0 — Pre-work on `main` (Avalonia still in place, all tests green)

Goal: eliminate all Avalonia references outside `Program.cs`, `App.axaml*`, `Views/`, `Converters/`, and the three UI service implementations.

1. Add `IAppLifetime` (`void Shutdown()`); implement `AvaloniaAppLifetime`; inject into `CleaningCommandsViewModel`; remove its Avalonia imports.
2. Change `AboutViewModel` to take a UI-framework version string via an injected provider (or drop the row); remove the Avalonia assembly probe.
3. Delete dead code: `ViewLocator.cs` (+ `Application.DataTemplates` entry) and `IntEqualsConverter`. Do **not** delete `PartialFormsWarningDialog` — it is registered in DI and awaits a call site as part of the experimental Partial Forms feature.
4. Extract `FileDialogService.ParseFilter` into a framework-neutral helper (returns plain name/patterns pairs) so its 7 tests survive the swap.
5. Audit each code-behind and confirm every behavior is expressed through `Interaction<T>`/`CloseRequested` events (it nearly is already) — document the per-window contract that the WinUI views must satisfy.

Exit criteria: `dotnet test` green; `rg "Avalonia" AutoQAC/ViewModels AutoQAC/Services` returns only `Services/UI` implementation files.

### Phase 1 — Project and bootstrap (migration branch)

1. Retarget `AutoQAC.csproj`: `UseWinUI`, `Microsoft.WindowsAppSDK`, unpackaged self-contained settings, and any build/MSIX tooling package required by the chosen Windows App SDK version; remove all Avalonia packages; convert `AvaloniaResource` assets to `Content`/`ms-appx` assets (replace `avalonia-logo.ico` with an AutoQAC icon).
2. Rewrite `App.xaml`/`App.xaml.cs`: WinUI `Application`, `OnLaunched` builds the existing DI container (unchanged service registrations except UI swaps below), creates `MainWindow`, wires `Closed` → existing shutdown/dispose path (`config watcher dispose`, provider dispose, `Log.CloseAndFlush()`).
3. Single-instance: switch to `AppInstance.FindOrRegisterForKey` + redirection.
4. Delete `Program.cs` Avalonia bootstrap (WinUI generates `Main`, or keep a custom `[STAThread]` Main for unpackaged bootstrap).

### Phase 2 — UI service implementations

1. `WinUiDispatcher : IUiDispatcher` over a captured `DispatcherQueue`.
2. `FileDialogService` over Windows App SDK `Microsoft.Windows.Storage.Pickers`; reuse the extracted filter parser. Add an `IWindowContextProvider` (or similar) abstraction that exposes the active `WindowId`/`AppWindow` and root `XamlRoot` for picker/dialog ownership.
3. `MessageDialogService` over `ContentDialog`, including the backup-failure dialog. Map `MessageDialogResult` enum 1:1. Serialize dialog display to respect the one-ContentDialog-at-a-time rule.
4. Update `ServiceCollectionExtensions` registrations.

### Phase 3 — Views, in dependency order

Per view: port XAML (`x:Bind` with explicit modes), port code-behind contracts (interaction handlers, `CloseRequested`, dispose-on-close), verify against the Phase 0 contract document.

1. `MessageDialog` → `ContentDialog` (validates the dialog service early). Port `PartialFormsWarningDialog` the same way (unwired today; keep for the upcoming Partial Forms call site).
2. `MainWindow` (largest; carries the interaction-handler hub and child-window lifecycle).
3. `ProgressWindow` (close-guard while cleaning, hang banner — exercise with a real cleaning run).
4. `SettingsWindow` (`NumberBox`, persistence banner), `SkipListWindow`.
5. `RestoreWindow`, `CleaningResultsWindow` (`ListView`-based grid), `AboutWindow`.
6. Recreate the `hyperlink` button style and theme resources in `App.xaml` (WinUI Fluent is the default theme; `RequestedTheme` left unset = follow system).
7. Port converters that survive (`GameTypeDisplayConverter`) and replace boolean/null converters with `x:Bind` functions.

### Phase 4 — Tests and verification

1. Update the 5 affected test files (paths, contract strings, filter-parser test rehosting).
2. Confirm no Avalonia.Headless-style claims creep into docs/tests (none today; keep it that way unless WinUI UI tests are deliberately added).
3. Full manual regression pass, prioritized by runtime-behavior invariants from `AGENTS.md`:
   - Sequential cleaning, single xEdit process slot, stop (graceful → force), hang detection banner, MO2 wrapped launch, backup skip in MO2 mode, dry-run preview, restore flow, skip-list editing, settings persistence + `FlushPendingSavesAsync` before launch, log retention on startup, single-instance guard.
4. Update `AGENTS.md`, `.cursor/rules/project-overview.mdc`, and `README.md` (stack section, commands unchanged).

### Phase 5 — Release

1. Verify Release build, self-contained publish, and that `AutoQAC Data` copy-to-output and relative paths behave identically unpackaged.
2. Smoke-test on a clean Windows 10 19041+ machine. Self-contained publish should remove the separate Windows App SDK runtime-install requirement; if `PublishSingleFile` is enabled, also verify first-launch extraction and runtime lookup behavior.

## 5. Effort estimate (rough)

| Phase | Estimate |
|---|---|
| 0 — Pre-work | 1–2 days |
| 1 — Bootstrap | 1–2 days |
| 2 — UI services | 2–3 days |
| 3 — Views | 5–8 days (MainWindow and ProgressWindow dominate) |
| 4 — Tests + regression | 2–3 days |
| 5 — Release validation | 1 day |
| **Total** | **~2.5–4 weeks** of focused work |

## 6. Resolved decisions

1. **Go/no-go** — **Go.** Proceed with the phased migration (~2.5–4 weeks, accepting the WinUI 3 papercuts in §3).
2. **Dialog strategy** — Follow §3.1 recommendation:
   - `ContentDialog`: `MessageDialog`, `PartialFormsWarningDialog`, and Settings / SkipList / About (or in-window navigation where that fits better).
   - Secondary `Window`: Progress, Restore, CleaningResults.
   - `OverlappedPresenter.IsModal` only where true owner-blocking behavior is worth the Win32 interop.
3. **DataGrid** — **`ListView` with column headers** for `CleaningResultsWindow` (not CommunityToolkit DataGrid).
4. **Windows App SDK version** — **1.8+** (required for `Microsoft.Windows.Storage.Pickers`). Confirm the exact stable package and Visual Studio/tooling requirements at Phase 1 kickoff.
5. **PartialFormsWarningDialog** — **Port as `ContentDialog` in Phase 3** (not dead code; DI-registered but unwired). The call site ships in a subsequent Partial Forms plan after migration.
