# AGENTS.md

Guidance for coding agents working in this repository.

## Project Overview

- `AutoQAC` is a Windows-only Avalonia desktop app for running xEdit Quick Auto Clean (`-QAC`) safely, one plugin at a time.
- `QueryPlugins` is a separate Mutagen-based analysis library for detecting ITMs, deleted references, and deleted navmeshes.
- The solution currently includes `AutoQAC`, `AutoQAC.Tests`, `QueryPlugins`, and `QueryPlugins.Tests`.

## Essential Commands

```bash
dotnet build AutoQACSharp.slnx
dotnet test AutoQACSharp.slnx
dotnet run --project AutoQAC/AutoQAC.csproj
dotnet build AutoQAC/AutoQAC.csproj -c Release
dotnet clean AutoQACSharp.slnx
```

## Current Stack

- .NET 10
- C# 13 with nullable reference types enabled
- `AutoQAC`: `net10.0-windows10.0.19041.0`
- Avalonia 11.3.12
- ReactiveUI.Avalonia 11.3.8
- Microsoft.Extensions.DependencyInjection 10.0.3
- Serilog 4.3.1 with console and file sinks
- YamlDotNet 16.3.0
- Mutagen 0.53.1
- xUnit 2.9.3, FluentAssertions 8.8.0, NSubstitute 5.3.0, coverlet 8.0.0

## Current Architecture

- `AutoQAC/Infrastructure` contains DI wiring and logging.
- `AutoQAC/Services` contains the main business logic, grouped into `Backup`, `Cleaning`, `Configuration`, `GameDetection`, `MO2`, `Monitoring`, `Plugin`, `Process`, `State`, and `UI`.
- `AutoQAC/ViewModels/MainWindow` splits the main window into `ConfigurationViewModel`, `PluginListViewModel`, and `CleaningCommandsViewModel`, coordinated by `MainWindowViewModel`.
- `MainWindow.axaml.cs` owns dialog/window interactions; ViewModels should not directly manipulate controls.
- `IStateService` and `AppState` are the shared runtime state hub for cleaning progress, plugin lists, and session results.
- `App.axaml.cs` builds the service provider, starts config watching, runs legacy config migration, and triggers log retention cleanup on startup.

## Runtime Behavior To Preserve

- Sequential cleaning is a hard requirement. Do not parallelize plugin cleaning or xEdit launches.
- `ProcessExecutionService` intentionally uses a single process slot.
- `CleaningOrchestrator` owns the end-to-end session flow: flush pending config, validate environment, detect game and variant, apply skip lists, optionally back up plugins, launch xEdit, parse results, and finalize the session.
- Stop behavior is two-stage: graceful cancellation first, then force termination if needed.
- Hang detection is CPU-based and flows through `IHangDetectionService` into the progress UI.
- MO2 mode wraps xEdit with `ModOrganizer.exe run`.
- Backups are skipped in MO2 mode because MO2 uses a virtual filesystem.
- Mutagen-backed plugin discovery is used for `SkyrimLe`, `SkyrimSe`, `SkyrimVr`, `Fallout4`, and `Fallout4Vr`.
- `Fallout3`, `FalloutNewVegas`, and `Oblivion` currently rely on file-based load-order loading.
- Skip list merging includes bundled defaults, user overrides, and variant-specific handling for TTW and Enderal.

## Coding Guidelines

- Maintain strict MVVM boundaries.
- Use `ReactiveCommand`, `RaiseAndSetIfChanged`, `WhenAnyValue`, and `ObservableAsPropertyHelper` for reactive state.
- Keep I/O and process work async; never block the UI thread with `.Result` or `.Wait()`.
- Use constructor injection through `ServiceCollectionExtensions`; avoid static mutable state and service locators.
- Respect Windows-specific assumptions when touching registry probing, executable paths, or process handling.
- If you touch Partial Forms support, verify end-to-end state flow first. The command-line flags exist, but the feature remains experimental.
- Do not modify `Mutagen/`; treat it as read-only.

## Testing Notes

- `AutoQAC.Tests` covers models, services, view models, integration flows, and view subscription lifecycle behavior.
- `QueryPlugins.Tests` covers the standalone detector library.
- `dotnet test` auto-collects Cobertura coverage into each test project's `TestResults/coverage/` directory.
- Use NSubstitute for mocks, and match optional parameters explicitly in substitute setups and assertions.
- There is no separate Avalonia.Headless test project in the current solution. Do not document or depend on one unless you add it intentionally.

## Important Files

- `AutoQAC/App.axaml.cs`
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- `AutoQAC/Services/Process/ProcessExecutionService.cs`
- `AutoQAC/Services/Configuration/ConfigurationService.cs`
- `AutoQAC/Services/Plugin/PluginLoadingService.cs`
- `AutoQAC Data/AutoQAC Main.yaml`
- `AutoQAC Data/AutoQAC Settings.yaml`

## Mutagen Reference

- Check `docs/mutagen/` first for fast lookups.
- The curated docs are useful, but package references in this repo are on Mutagen 0.53.1. If something looks stale or mismatched, verify against the read-only `Mutagen/` submodule.
- Do not build, modify, or add files under `Mutagen/`.

## Common Pitfalls

- Do not parallelize cleaning work.
- Do not assume every game uses a file-based load order; check `PluginLoadingService` first.
- Do not bypass `FlushPendingSavesAsync` before launching xEdit.
- Do not claim UI test infrastructure that is not present.
- Do not revert unrelated working-tree changes.
