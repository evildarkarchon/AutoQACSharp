# Technology Stack

**Analysis Date:** 2026-04-28

## Languages

**Primary:**
- C# 13 / .NET 10 - application, services, ViewModels, and test code in `AutoQAC/`, `AutoQAC.Tests/`, `QueryPlugins/`, and `QueryPlugins.Tests/`.
- XAML / AXAML - Avalonia UI layout and resources in `AutoQAC/App.axaml` and `AutoQAC/Views/*.axaml`.

**Secondary:**
- YAML - runtime configuration defaults and user settings in `AutoQAC/AutoQAC Data/AutoQAC Main.yaml`, `AutoQAC/AutoQAC Data/AutoQAC Settings.yaml`, and `openspec/config.yaml`.
- JSON - local tool/plugin configuration in `.opencode/package.json`, `.kilocode/package.json`, `.planning/config.json`, and generated backup/process metadata written by `AutoQAC/Services/Backup/BackupService.cs` and `AutoQAC/Services/Process/ProcessExecutionService.cs`.
- XML - solution and Windows manifest metadata in `AutoQACSharp.slnx` and `AutoQAC/app.manifest`.

## Runtime

**Environment:**
- .NET SDK/runtime 10.0.203 detected locally via `dotnet --version`.
- Desktop target for the main app: `net10.0-windows10.0.19041.0` in `AutoQAC/AutoQAC.csproj`.
- Library target: `net10.0` in `QueryPlugins/QueryPlugins.csproj`.
- Windows desktop process model: `[STAThread]` Avalonia startup in `AutoQAC/Program.cs`, Windows 10 compatibility manifest in `AutoQAC/app.manifest`, and Windows registry probing in `AutoQAC/Services/Plugin/PluginLoadingService.cs`.

**Package Manager:**
- NuGet via SDK-style `<PackageReference>` entries in `AutoQAC/AutoQAC.csproj`, `AutoQAC.Tests/AutoQAC.Tests.csproj`, `QueryPlugins/QueryPlugins.csproj`, and `QueryPlugins.Tests/QueryPlugins.Tests.csproj`.
- npm is used only for local agent/tooling plugin folders: `.opencode/package.json` and `.kilocode/package.json`.
- Lockfile: NuGet lock files are not present; npm lockfiles exist at `.opencode/package-lock.json` and `.kilocode/package-lock.json`.

## Frameworks

**Core:**
- Avalonia 12.0.1 - cross-platform UI framework used for the Windows desktop app; configured in `AutoQAC/Program.cs`, `AutoQAC/App.axaml`, and `AutoQAC/AutoQAC.csproj`.
- Avalonia.Controls.DataGrid 12.0.0 - table/grid UI support referenced from `AutoQAC/AutoQAC.csproj` and used by `AutoQAC/Views/*.axaml`.
- Avalonia.Themes.Fluent 12.0.1 and Avalonia.Fonts.Inter 12.0.1 - theme/font packages referenced in `AutoQAC/AutoQAC.csproj` and enabled by `.WithInterFont()` in `AutoQAC/Program.cs`.
- CommunityToolkit.Mvvm 8.4.2 - source-generator MVVM attributes used in ViewModels such as `AutoQAC/ViewModels/AboutViewModel.cs`, `AutoQAC/ViewModels/SettingsViewModel.cs`, and `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`.
- Microsoft.Extensions.DependencyInjection 10.0.3 - application composition root and DI registrations in `AutoQAC/App.axaml.cs` and `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`.
- Mutagen.Bethesda 0.53.1 family - Bethesda plugin load-order and record analysis support in `AutoQAC/Services/Plugin/PluginLoadingService.cs`, `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs`, and `QueryPlugins/`.
- YamlDotNet 16.3.0 - YAML serialization/deserialization for runtime configuration in `AutoQAC/Services/Configuration/ConfigurationService.cs` and validation in `AutoQAC/Services/Configuration/ConfigWatcherService.cs`.
- Serilog 4.3.1 with Serilog.Sinks.Console 6.1.1 and Serilog.Sinks.File 7.0.0 - structured logging in `AutoQAC/Infrastructure/Logging/LoggingService.cs`.
- System.Reactive APIs - observable/debounced configuration and UI pipelines in `AutoQAC/Services/Configuration/ConfigurationService.cs`, `AutoQAC/Services/Configuration/ConfigWatcherService.cs`, and `AutoQAC/Services/Monitoring/HangDetectionService.cs`.

**Testing:**
- xUnit 2.9.3 - test runner for `AutoQAC.Tests/` and `QueryPlugins.Tests/`.
- Microsoft.NET.Test.Sdk 18.0.1 - test SDK referenced in `AutoQAC.Tests/AutoQAC.Tests.csproj` and `QueryPlugins.Tests/QueryPlugins.Tests.csproj`.
- FluentAssertions 8.8.0 - assertion library referenced by both test projects.
- NSubstitute 5.3.0 and NSubstitute.Analyzers.CSharp 1.0.17 - mocking/analyzer packages referenced by both test projects.
- coverlet.collector 8.0.0 and coverlet.msbuild 8.0.0 - Cobertura coverage collection configured in both test `.csproj` files.

**Build/Dev:**
- .NET SDK CLI - primary build, restore, run, and test commands from `README.md` and `AGENTS.md`.
- SDK-style MSBuild projects - `AutoQAC/AutoQAC.csproj`, `AutoQAC.Tests/AutoQAC.Tests.csproj`, `QueryPlugins/QueryPlugins.csproj`, and `QueryPlugins.Tests/QueryPlugins.Tests.csproj`.
- `.slnx` solution format - root solution file `AutoQACSharp.slnx` includes all four active projects.
- AvaloniaUI.DiagnosticsSupport 2.2.1 - debug-only Avalonia developer tools support referenced in `AutoQAC/AutoQAC.csproj` and enabled in `AutoQAC/Program.cs` under `#if DEBUG`.
- OpenSpec tooling configuration - artifact workflow metadata in `openspec/config.yaml` and project-local skills under `.claude/skills/` and `.opencode/skills/`.
- Local agent plugin packages - `@opencode-ai/plugin`, `@kilocode/plugin` in `.opencode/package.json` and `@kilocode/plugin` in `.kilocode/package.json`.

## Key Dependencies

**Critical:**
- `Avalonia` 12.0.1 - required for the desktop UI shell (`AutoQAC/Program.cs`, `AutoQAC/App.axaml.cs`, `AutoQAC/Views/*.axaml`).
- `CommunityToolkit.Mvvm` 8.4.2 - required for `[ObservableProperty]`, `[RelayCommand]`, and generated ViewModel members in `AutoQAC/ViewModels/`.
- `Mutagen.Bethesda` 0.53.1 and game packages - required for supported-game plugin discovery and approximation/analysis in `AutoQAC/Services/Plugin/PluginLoadingService.cs`, `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs`, and `QueryPlugins/PluginQueryService.cs`.
- `YamlDotNet` 16.3.0 - required to load and save `AutoQAC Data/*.yaml` in `AutoQAC/Services/Configuration/ConfigurationService.cs`.
- `Serilog` 4.3.1 - required for operational logs and diagnostics through `AutoQAC/Infrastructure/Logging/ILoggingService.cs` and `AutoQAC/Infrastructure/Logging/LoggingService.cs`.
- `Microsoft.Extensions.DependencyInjection` 10.0.3 - required for service/view/viewmodel wiring in `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`.

**Infrastructure:**
- `System.Diagnostics.Process` - launches xEdit and Mod Organizer 2 commands in `AutoQAC/Services/Process/ProcessExecutionService.cs` and `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`.
- `Microsoft.Win32.Registry` - resolves Bethesda game install/data folders on Windows in `AutoQAC/Services/Plugin/PluginLoadingService.cs`.
- `FileSystemWatcher` - watches user YAML settings for external edits in `AutoQAC/Services/Configuration/ConfigWatcherService.cs`.
- `HttpClient` and `System.Text.Json` - checks latest GitHub release metadata in `AutoQAC/ViewModels/AboutViewModel.cs`.
- `System.Text.Json` - writes backup session metadata and process PID tracking JSON in `AutoQAC/Services/Backup/BackupService.cs` and `AutoQAC/Services/Process/ProcessExecutionService.cs`.

## Configuration

**Environment:**
- User-facing runtime settings are YAML files copied from `AutoQAC/AutoQAC Data/**` by `AutoQAC/AutoQAC.csproj`.
- Bundled defaults, xEdit executable names, and skip lists live in `AutoQAC/AutoQAC Data/AutoQAC Main.yaml`.
- User settings, selected game, paths, timeouts, CPU threshold, MO2 mode, and skip-list overrides live in `AutoQAC/AutoQAC Data/AutoQAC Settings.yaml`.
- Debug builds resolve configuration from the repository `AutoQAC Data` directory when found; production builds use `AutoQAC Data` next to the executable in `AutoQAC/Services/Configuration/ConfigurationService.cs` and `AutoQAC/Services/Configuration/ConfigWatcherService.cs`.
- No `.env` files are present in the repository scan; `.gitignore` excludes `*.env`.

**Build:**
- `AutoQACSharp.slnx` - root solution with four active projects.
- `AutoQAC/AutoQAC.csproj` - Windows desktop app target, Avalonia compiled bindings, assets, copied YAML data, NuGet packages, and `QueryPlugins` project reference.
- `QueryPlugins/QueryPlugins.csproj` - standalone Mutagen analysis library target and Mutagen package references.
- `AutoQAC.Tests/AutoQAC.Tests.csproj` and `QueryPlugins.Tests/QueryPlugins.Tests.csproj` - test and coverage configuration.
- `AutoQAC/app.manifest` - Windows 10 compatibility manifest for the desktop executable.
- `openspec/config.yaml` - OpenSpec schema configuration for planning artifacts.

## Platform Requirements

**Development:**
- Windows 10 or Windows 11 for registry probing, xEdit process behavior, and `net10.0-windows10.0.19041.0` desktop target.
- .NET 10 SDK; local environment reports SDK `10.0.203`.
- xEdit executable such as `SSEEdit.exe`, `FO4Edit.exe`, or `xEdit64.exe`; command construction is in `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`.
- Optional Mod Organizer 2 executable `ModOrganizer.exe`; MO2 validation is in `AutoQAC/Services/MO2/MO2ValidationService.cs`.

**Production:**
- Windows desktop deployment. The repository does not contain root `.github/workflows/*` CI/CD files or publish profiles.
- Runtime writes operational logs under `logs/` next to the executable through `AutoQAC/Infrastructure/Logging/LoggingService.cs` and `AutoQAC/Infrastructure/Logging/LogFilePaths.cs`.
- Runtime writes backup sessions under `AutoQAC Backups/` next to the game `Data` directory via `AutoQAC/Services/Backup/BackupService.cs`.

---

*Stack analysis: 2026-04-28*
