# AGENTS.md

Guidance for coding agents working in this repository.

## Project Overview

- `AutoQAC` is a Windows-only WinUI 3 desktop app for running xEdit Quick Auto Clean (`-QAC`) safely, one plugin at a time.
- `QueryPlugins` is a separate Mutagen-based analysis library for detecting ITMs, deleted references, and deleted navmeshes.
- The solution includes `AutoQAC`, `AutoQAC.Tests`, `AutoQAC.TestProcessHelper` (a test-only child process used by process/termination tests), `QueryPlugins`, and `QueryPlugins.Tests`.

## Essential Commands

```bash
dotnet build AutoQACSharp.slnx
dotnet test AutoQACSharp.slnx
dotnet run --project AutoQAC/AutoQAC.csproj
dotnet build AutoQAC/AutoQAC.csproj -c Release
```

## Current Stack

- .NET 10, nullable reference types enabled, `LangVersion` set to `preview`.
- `AutoQAC` targets `net10.0-windows10.0.19041.0` and is x64-only, self-contained, unpackaged (`WindowsPackageType=None`).
- `QueryPlugins` and its tests target plain `net10.0`.
- WinUI 3 / Microsoft Windows App SDK, CommunityToolkit.Mvvm (source-generator MVVM), Microsoft.Extensions.DependencyInjection, Serilog (console + file sinks), YamlDotNet, Mutagen.
- Tests use xUnit, FluentAssertions, NSubstitute, and coverlet.
- Exact package versions live in the `.csproj` files; check there rather than trusting this list. Versions are not uniform across projects.

## Current Architecture

- `AutoQAC/Infrastructure` contains DI wiring and logging.
- `AutoQAC/Services` contains the main business logic, grouped into `Backup`, `Cleaning`, `Configuration`, `GameCapability`, `GameDetection`, `MO2`, `Monitoring`, `Plugin`, `Process`, `State`, and `UI`.
- `AutoQAC/ViewModels/MainWindow` splits the main window into `ConfigurationViewModel`, `PluginListViewModel`, and `CleaningCommandsViewModel`, coordinated by `MainWindowViewModel`.
- `MainWindow.xaml.cs` owns dialog/window interactions; ViewModels should not directly manipulate controls.
- `IStateService` and `AppState` are the shared runtime state hub for cleaning progress, plugin lists, and session results.
- `App.xaml.cs` builds the service provider, starts config watching, runs legacy config migration, and triggers log retention cleanup on startup.
- `IWindowContextProvider` supplies active WinUI window context for file pickers and `ContentDialog` ownership; `IAppLifetime` abstracts shutdown.

## Runtime Behavior To Preserve

- Sequential cleaning is a hard requirement. Do not parallelize plugin cleaning or xEdit launches.
- `ProcessExecutionService` intentionally uses a single process slot.
- `ICleaningSession` / `CleaningSession` owns the end-to-end session flow: preflight (flush pending config, validate environment, detect game and variant, apply skip lists), optional plugin backup, xEdit launch, result parsing, and final state publication. Its seam is `StartAsync`, `PreviewAsync`, `ControlAsync`, and `HangDetected`; preflight, backup, process/termination, runner, and finalizer sit behind it as adapters. See `docs/adr/0001-replace-cleaning-orchestrator-seam.md` — the older `ICleaningOrchestrator` seam no longer exists.
- Stop behavior is two-stage: graceful cancellation first, then force termination if needed.
- Hang detection is CPU-based and flows through `IHangDetectionService` into the progress UI.
- MO2 mode wraps xEdit with `ModOrganizer.exe run`.
- Backups are skipped in MO2 mode because MO2 uses a virtual filesystem.
- Mutagen-backed plugin discovery is used for `SkyrimLe`, `SkyrimSe`, `SkyrimVr`, `Fallout4`, and `Fallout4Vr`.
- `Fallout3`, `FalloutNewVegas`, and `Oblivion` currently rely on file-based load-order loading.
- Skip list merging includes bundled defaults, user overrides, and variant-specific handling for TTW and Enderal.

## Coding Guidelines

- Comments are welcome and encouraged; this project overrides the default "no comments" agent rule. Prefer WHY-comments over WHAT-comments — explain non-obvious decisions, invariants, and the reasoning behind intentional patterns (e.g. sync-over-async in disposal, sequential-only cleaning, the single process slot). Do not strip accurate existing comments as cleanup. Add XML doc comments (`///`) on new or substantially rewritten public members unless trivial.
- Maintain strict MVVM boundaries.
- Use CommunityToolkit.Mvvm source generators (`[ObservableProperty]`, `[RelayCommand]`, `[NotifyPropertyChangedFor]`, `[NotifyCanExecuteChangedFor]`) for ViewModel state. ViewModels MUST be `partial` for the source generators. Service `IObservable<T>` streams are subscribed via `CallbackObserver<T>` and marshaled to the UI thread via the injected `IUiDispatcher`.
- Keep I/O and process work async; never block the UI thread with `.Result` or `.Wait()`.
- Use constructor injection through `ServiceCollectionExtensions`; avoid static mutable state and service locators.
- Respect Windows-specific assumptions when touching registry probing, executable paths, or process handling.
- If you touch Partial Forms support, verify end-to-end state flow first. The command-line flags exist, but the feature remains experimental.

## Testing Notes

- `AutoQAC.Tests` covers models, services, view models, integration flows, and view subscription lifecycle behavior.
- `QueryPlugins.Tests` covers the standalone detector library.
- `dotnet test` auto-collects Cobertura coverage into each test project's `TestResults/coverage/` directory.
- Use NSubstitute for mocks, and match optional parameters explicitly in substitute setups and assertions.
- There is no separate WinUI UI automation/headless test project in the current solution. Do not document or depend on one unless you add it intentionally.

## Important Files

- `AutoQAC/App.xaml.cs`
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC/Services/Cleaning/CleaningSession.cs`
- `AutoQAC/Services/Cleaning/CleaningPreflight.cs`
- `AutoQAC/Services/Process/ProcessExecutionService.cs`
- `AutoQAC/Services/Configuration/ConfigurationService.cs`
- `AutoQAC/Services/Plugin/PluginLoadingService.cs`
- `AutoQAC/Services/GameCapability/GameCapabilityCatalog.cs`
- `AutoQAC/AutoQAC Data/AutoQAC Main.yaml` and `AutoQAC/AutoQAC Data/AutoQAC Settings.yaml` — the copies the app ships and reads. A second `AutoQAC Data/` at the repo root holds branding assets plus its own `AutoQAC Main.yaml`; the project only copies the `AutoQAC/` one to output.

## Mutagen Reference

- Check `docs/mutagen/` first for fast lookups.
- The curated docs may lag the pinned package version. If something looks stale or mismatched, verify against the read-only `Mutagen/` submodule.
- Do not build, modify, or add files under `Mutagen/`.

## Common Pitfalls

- Do not assume every game uses a file-based load order; check `GameCapabilityCatalog` and `PluginLoadingService` first.
- Do not bypass `FlushPendingSavesAsync` before launching xEdit; `CleaningPreflight` is where the session does it.
- Do not revert unrelated working-tree changes.

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

When the user types `/graphify`, use the installed graphify skill or instructions before doing anything else.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- Dirty graphify-out/ files are expected after hooks or incremental updates; dirty graph files are not a reason to skip graphify. Only skip graphify if the task is about stale or incorrect graph output, or the user explicitly says not to use it.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).

## Agent skills

### Issue tracker

Track issues locally under `.scratch/<feature>/`. Before creating, fetching,
or updating tickets, read `docs/agents/issue-tracker.md`.

### Triage labels

Use the five default triage roles. Before triaging tickets, read
`docs/agents/triage-labels.md`.

### Domain docs

Use the single-context layout: root `CONTEXT.md` and `docs/adr/`.
Before exploring domain concepts, read `docs/agents/domain.md`.
