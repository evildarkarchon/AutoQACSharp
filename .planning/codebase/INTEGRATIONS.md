# External Integrations

**Analysis Date:** 2026-04-28

## APIs & External Services

**GitHub:**
- GitHub Releases API - update-check flow fetches the latest release from `https://api.github.com/repos/evildarkarchon/AutoQACSharp/releases/latest` in `AutoQAC/ViewModels/AboutViewModel.cs`.
  - SDK/Client: built-in `System.Net.Http.HttpClient` and `System.Text.Json`.
  - Auth: none; public unauthenticated request with `User-Agent: AutoQACSharp/1.0`.
- GitHub project and issue URLs - About window links to `https://github.com/evildarkarchon/AutoQACSharp` and `https://github.com/evildarkarchon/AutoQACSharp/issues` in `AutoQAC/ViewModels/AboutViewModel.cs`.
  - SDK/Client: `System.Diagnostics.Process.Start` with `UseShellExecute = true`.
  - Auth: none.

**xEdit / TES5Edit:**
- xEdit executable - primary external process integration for Quick Auto Clean; command arguments include `-QAC`, `-autoexit`, `-autoload`, game flags for universal xEdit, and optional partial-form flags in `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`.
  - SDK/Client: `System.Diagnostics.ProcessStartInfo` executed by `AutoQAC/Services/Process/ProcessExecutionService.cs`.
  - Auth: none; user supplies local executable path in `AutoQAC/AutoQAC Data/AutoQAC Settings.yaml`.
- xEdit logs - session statistics and exception details are read from xEdit log files by `AutoQAC/Services/Cleaning/XEditLogFileService.cs` and parsed by `AutoQAC/Services/Cleaning/XEditOutputParser.cs`.
  - SDK/Client: local filesystem.
  - Auth: none.

**Mod Organizer 2:**
- Mod Organizer 2 launch wrapper - MO2 mode runs `ModOrganizer.exe run "<xEdit>" -a "<args>"` from `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`.
  - SDK/Client: local `ModOrganizer.exe` process via `System.Diagnostics.ProcessStartInfo`.
  - Auth: none; user supplies local executable path in `AutoQAC/AutoQAC Data/AutoQAC Settings.yaml`.
- MO2 running-state validation - detects running `ModOrganizer` processes in `AutoQAC/Services/MO2/MO2ValidationService.cs`.
  - SDK/Client: `System.Diagnostics.Process.GetProcessesByName`.
  - Auth: none.

**Bethesda game installations:**
- Game data folder discovery - Mutagen `GameLocations.TryGetDataFolder` resolves supported games in `AutoQAC/Services/Plugin/PluginLoadingService.cs`.
  - SDK/Client: `Mutagen.Bethesda.Installs.GameLocations`.
  - Auth: none.
- Windows registry fallback - probes Bethesda and Steam uninstall keys for install paths in `AutoQAC/Services/Plugin/PluginLoadingService.cs`.
  - SDK/Client: `Microsoft.Win32.RegistryKey` over `RegistryHive.LocalMachine` and `RegistryHive.CurrentUser`, both 64-bit and 32-bit registry views.
  - Auth: local user/Windows permissions only.

## Data Storage

**Databases:**
- Not detected for the active application projects. No `DbContext`, connection string, or database client is used in `AutoQAC/`, `AutoQAC.Tests/`, `QueryPlugins/`, or `QueryPlugins.Tests/`.
- The read-only `Mutagen/` submodule contains SQLite-related code, but it is not part of the active `AutoQACSharp.slnx` projects and should not be treated as this app's database integration.

**File Storage:**
- Local YAML configuration - `AutoQAC/AutoQAC Data/AutoQAC Main.yaml` and `AutoQAC/AutoQAC Data/AutoQAC Settings.yaml`, loaded/saved through `AutoQAC/Services/Configuration/ConfigurationService.cs`.
- Local config file watching - `FileSystemWatcher` monitors `AutoQAC Settings.yaml` in `AutoQAC/Services/Configuration/ConfigWatcherService.cs`.
- Local logs - Serilog rolling file sink writes daily log files under the log directory resolved by `AutoQAC/Infrastructure/Logging/LogFilePaths.cs` and configured in `AutoQAC/Infrastructure/Logging/LoggingService.cs`.
- Local backups - plugin backup sessions and `session.json` metadata are stored under `AutoQAC Backups/` by `AutoQAC/Services/Backup/BackupService.cs`.
- Local PID tracking - `autoqac-pids.json` is used for process tracking/orphan cleanup in `AutoQAC/Services/Process/ProcessExecutionService.cs`.
- Local load order files - file-based games use user-provided `plugins.txt`/load-order paths loaded by `AutoQAC/Services/Plugin/PluginLoadingService.cs` and validated by `AutoQAC/Services/Plugin/PluginValidationService.cs`.

**Caching:**
- In-memory main config cache - `_mainConfigCache` in `AutoQAC/Services/Configuration/ConfigurationService.cs`.
- In-memory pending/last-known-good user config snapshots - `_pendingConfig` and `_lastKnownGoodConfig` in `AutoQAC/Services/Configuration/ConfigurationService.cs`.
- In-memory app state stream - `IStateService` / `StateService` in `AutoQAC/Services/State/`.
- No external cache service such as Redis or Memcached is detected.

## Authentication & Identity

**Auth Provider:**
- Not detected. The application has no login, OAuth, OpenID Connect, JWT, API key, or user identity provider integration in `AutoQAC/`, `QueryPlugins/`, or tests.
  - Implementation: local desktop app with user-selected filesystem paths and unauthenticated public GitHub release lookup.

## Monitoring & Observability

**Error Tracking:**
- No external error-tracking provider is detected. Errors are logged locally through `AutoQAC/Infrastructure/Logging/ILoggingService.cs` and `AutoQAC/Infrastructure/Logging/LoggingService.cs`.

**Logs:**
- Serilog local logging with minimum level `Debug`, warnings to console, and rolling daily file logs configured in `AutoQAC/Infrastructure/Logging/LoggingService.cs`.
- Startup diagnostics log version, .NET runtime, xEdit path, game type, MO2 mode, and plugin count in `AutoQAC/App.axaml.cs`.
- Log retention cleanup runs on startup through `AutoQAC/Services/Configuration/LogRetentionService.cs`, invoked from `AutoQAC/App.axaml.cs`.
- CPU-based hang detection emits state through `IHangDetectionService` in `AutoQAC/Services/Monitoring/HangDetectionService.cs`.

## CI/CD & Deployment

**Hosting:**
- Windows desktop application. Build/run/test commands are documented in `README.md` and `AGENTS.md`.
- No server hosting platform is detected for `AutoQAC/` or `QueryPlugins/`.

**CI Pipeline:**
- None detected at the repository root; `.github/workflows/*` is absent for the active repo.
- The `Mutagen/` submodule contains its own `.github/workflows/` files, but `Mutagen/` is read-only reference material and not the active app pipeline.

## Environment Configuration

**Required env vars:**
- Not detected. No required environment variables are used by the active application code.

**Secrets location:**
- Not applicable. No secret files or `.env` files were detected in the repo scan; `.gitignore` excludes `*.env`.
- Runtime configuration values are non-secret local paths/settings in `AutoQAC/AutoQAC Data/AutoQAC Settings.yaml`.

**Required user-provided paths/settings:**
- xEdit executable path - stored under the `xEdit` section in `AutoQAC/AutoQAC Data/AutoQAC Settings.yaml`, consumed by `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs` and validation flows in `AutoQAC/ViewModels/SettingsViewModel.cs`.
- Load order path - stored under `Load_Order` in `AutoQAC/AutoQAC Data/AutoQAC Settings.yaml`, required for Fallout 3, Fallout: New Vegas, and Oblivion flows in `AutoQAC/Services/Cleaning/CleaningService.cs` and `AutoQAC/Services/Plugin/PluginLoadingService.cs`.
- MO2 executable path - stored under `Mod_Organizer` in `AutoQAC/AutoQAC Data/AutoQAC Settings.yaml`, used when MO2 mode is enabled by `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`.
- Per-game defaults and skip lists - bundled in `AutoQAC/AutoQAC Data/AutoQAC Main.yaml` and merged by configuration/plugin flows.

## Webhooks & Callbacks

**Incoming:**
- None. No HTTP server, route handlers, webhook endpoints, sockets, or callback listeners are detected in the active application projects.

**Outgoing:**
- GitHub Releases API request from `AutoQAC/ViewModels/AboutViewModel.cs`.
- Browser/shell URL opens for GitHub project, GitHub issues, latest release, and xEdit project URLs from `AutoQAC/ViewModels/AboutViewModel.cs`.
- Local process launches for xEdit and optional MO2 wrapper from `AutoQAC/Services/Process/ProcessExecutionService.cs` and `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`.

---

*Integration audit: 2026-04-28*
