# Technology Stack

**Analysis Date:** 2026-04-29

## Languages

**Primary:**
- C# 13 / .NET 10 - application, services, models, and tests in `AutoQAC/`, `AutoQAC.Tests/`, `QueryPlugins/`, and `QueryPlugins.Tests/`; nullable reference types are enabled in every project file.
- Avalonia AXAML - desktop UI markup in `AutoQAC/App.axaml` and `AutoQAC/Views/*.axaml`.

**Secondary:**
- YAML - bundled/default and user configuration in `AutoQAC/AutoQAC Data/AutoQAC Main.yaml` and `AutoQAC/AutoQAC Data/AutoQAC Settings.yaml`.
- JSON - backup session metadata written by `AutoQAC/Services/Backup/BackupService.cs` and process PID tracking through `AutoQAC/Services/Process/JsonPidStore.cs`.
- XML - Windows application manifest in `AutoQAC/app.manifest` and XML-based solution file `AutoQACSharp.slnx`.

## Runtime

**Environment:**
- .NET 10 SDK/runtime.
- `AutoQAC`: `net10.0-windows10.0.19041.0` Windows desktop executable, configured in `AutoQAC/AutoQAC.csproj`.
- `AutoQAC.Tests`: `net10.0-windows10.0.19041.0`, configured in `AutoQAC.Tests/AutoQAC.Tests.csproj`.
- `QueryPlugins`: `net10.0` class library, configured in `QueryPlugins/QueryPlugins.csproj`.
- `QueryPlugins.Tests`: `net10.0`, configured in `QueryPlugins.Tests/QueryPlugins.Tests.csproj`.
- `AutoQAC.Tests/TestProcessHelper`: `net10.0` helper executable, configured in `AutoQAC.Tests/TestProcessHelper/AutoQAC.TestProcessHelper.csproj`.

**Package Manager:**
- NuGet via the `dotnet` CLI.
- Lockfile: missing (`packages.lock.json` not detected).
- SDK pinning: no root `global.json` detected.
- Central package management: not detected at repo root; package versions are declared directly in each `.csproj`.

## Frameworks

**Core:**
- Avalonia 12.0.1 - desktop UI framework for `AutoQAC`; configured in `AutoQAC/AutoQAC.csproj` and initialized in `AutoQAC/Program.cs`.
- Avalonia.Controls.DataGrid 12.0.0 - plugin grids and tabular UI; theme included in `AutoQAC/App.axaml`.
- Avalonia.Desktop 12.0.1 - classic desktop lifetime used by `AutoQAC/Program.cs`.
- Avalonia.Themes.Fluent 12.0.1 - Fluent theme loaded from `AutoQAC/App.axaml`.
- Avalonia.Fonts.Inter 12.0.1 - Inter font configured via `.WithInterFont()` in `AutoQAC/Program.cs`.
- CommunityToolkit.Mvvm 8.4.2 - source-generator MVVM for ViewModels in `AutoQAC/ViewModels/`.
- Microsoft.Extensions.DependencyInjection 10.0.3 - service container configured by `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` and built in `AutoQAC/App.axaml.cs`.

**Testing:**
- xUnit 2.9.3 - unit/integration test framework in `AutoQAC.Tests/` and `QueryPlugins.Tests/`.
- xunit.runner.visualstudio 3.1.5 - Visual Studio / `dotnet test` adapter.
- FluentAssertions 8.8.0 - assertion library used by test projects.
- NSubstitute 5.3.0 - mocking/substitution framework used by test projects.
- NSubstitute.Analyzers.CSharp 1.0.17 - analyzer package declared as private assets in test project files.
- coverlet.collector 8.0.0 and coverlet.msbuild 8.0.0 - Cobertura coverage collection configured in `AutoQAC.Tests/AutoQAC.Tests.csproj` and `QueryPlugins.Tests/QueryPlugins.Tests.csproj`.
- Microsoft.NET.Test.Sdk 18.0.1 - test host package in both test projects.

**Build/Dev:**
- .NET CLI - commands documented in `README.md` and `CLAUDE.md`: `dotnet restore AutoQACSharp.slnx`, `dotnet build AutoQACSharp.slnx`, `dotnet test AutoQACSharp.slnx`, and `dotnet run --project AutoQAC/AutoQAC.csproj`.
- `.slnx` solution format - root solution is `AutoQACSharp.slnx` with five project entries.
- AvaloniaUI.DiagnosticsSupport 2.2.1 - DEBUG-only developer tools attached by `AutoQAC/Program.cs`; Release output excludes the package through conditions in `AutoQAC/AutoQAC.csproj`.

## Key Dependencies

**Critical:**
- Mutagen.Bethesda 0.53.1 - Bethesda plugin/load-order APIs for `AutoQAC/Services/Plugin/PluginLoadingService.cs`, `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs`, and `QueryPlugins/PluginQueryService.cs`.
- Mutagen.Bethesda.Skyrim 0.53.1 - Skyrim/Skyrim SE/Skyrim VR record support used by `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs` and `QueryPlugins/Detectors/Games/SkyrimDetector.cs`.
- Mutagen.Bethesda.Fallout4 0.53.1 - Fallout 4/Fallout 4 VR record support used by `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs` and `QueryPlugins/Detectors/Games/Fallout4Detector.cs`.
- Mutagen.Bethesda.Starfield 0.53.1 - Starfield detector support in `QueryPlugins/Detectors/Games/StarfieldDetector.cs`; desktop app does not expose Starfield cleaning.
- Mutagen.Bethesda.Oblivion 0.53.1 - Oblivion detector support in `QueryPlugins/Detectors/Games/OblivionDetector.cs`.
- QueryPlugins project reference - `AutoQAC/AutoQAC.csproj` references `QueryPlugins/QueryPlugins.csproj` for Mutagen-based issue approximations.
- xEdit external executable - direct and MO2-wrapped cleaning commands are built by `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`; xEdit results are read from log files by `AutoQAC/Services/Cleaning/XEditLogFileService.cs`.

**Infrastructure:**
- Serilog 4.3.1 - logging abstraction implementation in `AutoQAC/Infrastructure/Logging/LoggingService.cs`.
- Serilog.Sinks.Console 6.1.1 - warning-and-above console sink configured in `AutoQAC/Infrastructure/Logging/LoggingService.cs`.
- Serilog.Sinks.File 7.0.0 - rolling file logs configured in `AutoQAC/Infrastructure/Logging/LoggingService.cs`.
- YamlDotNet 16.3.0 - YAML serialization/deserialization in `AutoQAC/Services/Configuration/ConfigurationService.cs`, `AutoQAC/Services/Configuration/ConfigWatcherService.cs`, and `AutoQAC/Services/Configuration/LegacyMigrationService.cs`.
- System.Reactive APIs - used directly from the shared framework/package graph for service-layer observables in `AutoQAC/Services/Configuration/ConfigurationService.cs`, `AutoQAC/Services/Configuration/ConfigWatcherService.cs`, and `AutoQAC/Services/State/StateService.cs`.
- Microsoft.Win32.Registry APIs - Windows registry probing in `AutoQAC/Services/Plugin/PluginLoadingService.cs`.
- System.Diagnostics.Process APIs - xEdit/MO2 launch and termination in `AutoQAC/Services/Process/ProcessExecutionService.cs` and `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`.

## Configuration

**Environment:**
- Bundled configuration lives in `AutoQAC/AutoQAC Data/` and is copied to build output by `AutoQAC/AutoQAC.csproj`.
- Main defaults and skip lists are loaded from `AutoQAC/AutoQAC Data/AutoQAC Main.yaml` through `AutoQAC/Services/Configuration/ConfigurationService.cs`.
- User settings are loaded from `AutoQAC/AutoQAC Data/AutoQAC Settings.yaml` through `AutoQAC/Services/Configuration/ConfigurationService.cs`.
- Required runtime paths are configured by the user, not environment variables: xEdit executable path, optional MO2 executable path, optional load-order path, per-game data folder overrides, skip lists, backup settings, and log retention settings.
- `.env` files: not detected in the repo; do not introduce secret-bearing environment files for current configuration needs.

**Build:**
- `AutoQACSharp.slnx` includes `AutoQAC/AutoQAC.csproj`, `AutoQAC.Tests/AutoQAC.Tests.csproj`, `AutoQAC.Tests/TestProcessHelper/AutoQAC.TestProcessHelper.csproj`, `QueryPlugins/QueryPlugins.csproj`, and `QueryPlugins.Tests/QueryPlugins.Tests.csproj`.
- `AutoQAC/AutoQAC.csproj` sets `<OutputType>WinExe</OutputType>`, `<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>`, `<Nullable>enable</Nullable>`, `<BuiltInComInteropSupport>true</BuiltInComInteropSupport>`, `<ApplicationManifest>app.manifest</ApplicationManifest>`, and `<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>`.
- `QueryPlugins/QueryPlugins.csproj` sets `<TargetFramework>net10.0</TargetFramework>`, `<ImplicitUsings>enable</ImplicitUsings>`, and `<Nullable>enable</Nullable>`.
- Test project coverage outputs are configured to `./TestResults/coverage/` in `AutoQAC.Tests/AutoQAC.Tests.csproj` and `QueryPlugins.Tests/QueryPlugins.Tests.csproj`.
- `AutoQAC/app.manifest` declares Windows 10 compatibility.
- Root `.editorconfig`, `Directory.Build.props`, `Directory.Build.targets`, and `NuGet.config`: not detected outside the read-only `Mutagen/` submodule.

## Platform Requirements

**Development:**
- Windows 10 or Windows 11 for running the main app because `AutoQAC/AutoQAC.csproj` targets Windows and services use registry/process behavior.
- .NET 10 SDK for build/test/run commands.
- xEdit executable such as `SSEEdit.exe`, `FO4Edit.exe`, `xEdit.exe`, or `xEdit64.exe`; accepted executable names are listed in `AutoQAC/AutoQAC Data/AutoQAC Main.yaml`.
- Optional Mod Organizer 2 (`ModOrganizer.exe`) when MO2 mode is enabled; validated by `AutoQAC/Services/MO2/MO2ValidationService.cs`.
- Mutagen reference material is available in `docs/mutagen/`; `Mutagen/` is a read-only git submodule declared in `.gitmodules`.

**Production:**
- Desktop deployment target: Windows 10+ classic desktop app using Avalonia.
- Runtime output must include `AutoQAC Data/` because `AutoQAC/Services/Configuration/ConfigurationService.cs` resolves configuration under `AppContext.BaseDirectory` outside DEBUG source-tree fallback.
- Logs are written under the executable-adjacent log directory resolved by `AutoQAC/Infrastructure/Logging/LogFilePaths.cs`.
- Plugin backups are written to local filesystem backup roots managed by `AutoQAC/Services/Backup/BackupService.cs`; MO2 mode skips backups because MO2 uses a virtual filesystem.

---

*Stack analysis: 2026-04-29*
