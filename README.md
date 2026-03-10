# AutoQACSharp

AutoQACSharp is a Windows desktop tool for sequential cleaning of Bethesda plugins with xEdit Quick Auto Clean (`-QAC`). The main app is built with Avalonia and ReactiveUI, uses Serilog for logging, YamlDotNet for configuration, and Mutagen for plugin discovery where supported.

## Current Status

- The desktop app is past the old "foundation stage". Core configuration, plugin loading, cleaning orchestration, backup/restore, result reporting, and test coverage are all in place.
- The solution currently contains four projects: `AutoQAC`, `AutoQAC.Tests`, `QueryPlugins`, and `QueryPlugins.Tests`.
- The UI uses Avalonia, but the shipping app currently targets Windows (`net10.0-windows10.0.19041.0`) because it relies on Windows registry and process behavior for game discovery and xEdit integration.

## Implemented Features

- Sequential xEdit orchestration with timeout handling, retry prompts, hang detection, graceful stop, and force-kill fallback.
- Plugin discovery via Mutagen for Skyrim LE/SE/VR and Fallout 4/VR, with file-based load-order support for Fallout 3, Fallout: New Vegas, and Oblivion.
- Per-game data folder overrides and per-game load order overrides.
- Skip list management backed by `AutoQAC Main.yaml` defaults plus user overrides, including TTW and Enderal variant handling.
- Plugin backup sessions before cleaning, retention cleanup, and a restore browser.
- Dry-run preview showing which plugins will be cleaned or skipped before xEdit is launched.
- Cleaning results windows with per-plugin stats and exportable session reports.
- Debounced YAML config saves, external config file watching, startup log retention cleanup, and one-time legacy config migration.
- A separate `QueryPlugins` library for Mutagen-based issue detection (ITMs, deleted references, deleted navmeshes) with its own tests.

Note: the dry-run preview is a readiness check only. ITM/UDR/navmesh statistics still require an actual xEdit run.

## Supported Games

| Game | AutoQAC app | Plugin discovery |
|------|-------------|------------------|
| Skyrim (Legendary Edition) | Yes | Mutagen |
| Skyrim Special Edition | Yes | Mutagen |
| Skyrim VR | Yes | Mutagen |
| Fallout 4 | Yes | Mutagen |
| Fallout 4 VR | Yes | Mutagen |
| Fallout 3 | Yes | File-based load order |
| Fallout: New Vegas | Yes | File-based load order |
| Oblivion | Yes | File-based load order |

`QueryPlugins` also contains standalone detector support for Starfield analysis, but the desktop cleaning app does not currently expose Starfield cleaning.

## Solution Layout

```text
AutoQAC/             Avalonia desktop application
AutoQAC.Tests/       Tests for the desktop app
QueryPlugins/        Standalone Mutagen-based plugin analysis library
QueryPlugins.Tests/  Tests for the analysis library
AutoQAC Data/        Bundled YAML config and assets
docs/mutagen/        Curated Mutagen reference docs
Mutagen/             Read-only Mutagen git submodule
```

## Requirements

- Windows 10 or 11
- .NET 10 SDK
- xEdit (`SSEEdit.exe`, `FO4Edit.exe`, `xEdit64.exe`, etc.)
- Optional: Mod Organizer 2 for MO2 launch mode

## Build, Run, and Test

```bash
dotnet restore AutoQACSharp.slnx
dotnet build AutoQACSharp.slnx
dotnet run --project AutoQAC/AutoQAC.csproj
dotnet test AutoQACSharp.slnx
dotnet build AutoQAC/AutoQAC.csproj -c Release
```

## Configuration Files

- `AutoQAC Data/AutoQAC Main.yaml` - bundled defaults such as xEdit executable name lists and built-in skip lists.
- `AutoQAC Data/AutoQAC Settings.yaml` - user settings, selected game, per-game overrides, log retention, and backup settings.
- Legacy `AutoQAC Config.yaml` files are migrated on startup when present.
- External edits to `AutoQAC Settings.yaml` are watched and reloaded when it is safe to do so.

## Logs and Backups

- Logs are written to `logs/` next to the running executable.
- Backup sessions are written to `AutoQAC Backups/` next to the selected game's `Data` directory.
- Session reports can be exported from the cleaning results window.

## Important Constraint

Only one plugin can be cleaned at a time. `ProcessExecutionService` and `CleaningOrchestrator` intentionally serialize xEdit launches because xEdit relies on single-instance file locking.

## Mutagen Reference Material

- Use `docs/mutagen/` first for quick API lookups.
- Use the read-only `Mutagen/` submodule when the curated docs are not detailed enough.

## License

GPL-3.0. See `LICENSE`.

## Credits

- Original AutoQAC / XEdit-PACT concept by Poet (GuidanceOfGrace)
- xEdit by the TES5Edit/xEdit team
- C# desktop implementation in this repository
