# External Integrations

**Analysis Date:** 2026-04-29

## APIs & External Services

**Desktop process integrations:**
- xEdit / TES5Edit / SSEEdit / FO4Edit / FNVEdit / FO3Edit / TES4Edit - authoritative Quick Auto Clean execution and cleaning statistics.
  - SDK/Client: external executable launched through `System.Diagnostics.ProcessStartInfo` in `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs` and `AutoQAC/Services/Process/ProcessExecutionService.cs`.
  - Auth: none.
  - Configuration: user-selected xEdit path stored in `AutoQAC/AutoQAC Data/AutoQAC Settings.yaml` and modeled by `AutoQAC/Models/Configuration/UserConfiguration.cs`.
  - Command mode: direct launch uses `-QAC`, `-autoexit`, `-autoload`, optional universal xEdit game flags, and optional partial-forms flags in `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`.
  - Result channel: appended xEdit log files in the xEdit install directory are located/read by `AutoQAC/Services/Cleaning/XEditLogFileService.cs`; stdout/stderr are not the authoritative result source.
- Mod Organizer 2 - optional wrapper for launching xEdit inside MO2's virtual filesystem.
  - SDK/Client: `ModOrganizer.exe run` process invocation built by `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`.
  - Auth: none.
  - Configuration: optional MO2 path and mode flag in `AutoQAC/AutoQAC Data/AutoQAC Settings.yaml`, modeled by `AutoQAC/Models/Configuration/UserConfiguration.cs`.
  - Validation: process-name and filename checks in `AutoQAC/Services/MO2/MO2ValidationService.cs`.
- Windows process table - single-instance, orphan tracking, hang detection, graceful close, and force-kill behavior.
  - SDK/Client: `System.Diagnostics.Process` in `AutoQAC/Services/Process/ProcessExecutionService.cs`, `AutoQAC/Services/Process/SingleInstanceGuard.cs`, `AutoQAC/Services/MO2/MO2ValidationService.cs`, and `AutoQAC/Services/Monitoring/HangDetectionService.cs`.
  - Auth: current Windows user permissions.

**Bethesda plugin analysis:**
- Mutagen NuGet packages - load-order discovery and approximate ITM/deleted-record analysis.
  - SDK/Client: `Mutagen.Bethesda*` package references in `AutoQAC/AutoQAC.csproj`, `QueryPlugins/QueryPlugins.csproj`, and `QueryPlugins.Tests/QueryPlugins.Tests.csproj`.
  - Auth: none.
  - Implementation: `AutoQAC/Services/Plugin/PluginLoadingService.cs` uses `GameLocations` and `LoadOrder.GetLoadOrderListings`; `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs` imports typed load orders; `QueryPlugins/PluginQueryService.cs` delegates to detectors in `QueryPlugins/Detectors/`.
  - Reference source: `Mutagen/` git submodule declared in `.gitmodules`; treat as read-only.

**External websites and update metadata:**
- Nexus Mods URLs appear in bundled warning text in `AutoQAC/AutoQAC Data/AutoQAC Main.yaml`.
  - SDK/Client: no HTTP client implementation detected.
  - Auth: none.
  - Current behavior: `Update Check` is present in legacy/default YAML text, but no `HttpClient`, webhook, or live update-check service was detected in `AutoQAC/` or `QueryPlugins/`.

## Data Storage

**Databases:**
- Not detected.
  - Connection: not applicable.
  - Client: not applicable.

**File Storage:**
- YAML configuration files in `AutoQAC/AutoQAC Data/AutoQAC Main.yaml` and `AutoQAC/AutoQAC Data/AutoQAC Settings.yaml`.
  - Client: YamlDotNet serializers/deserializers in `AutoQAC/Services/Configuration/ConfigurationService.cs`, `AutoQAC/Services/Configuration/ConfigWatcherService.cs`, and `AutoQAC/Services/Configuration/LegacyMigrationService.cs`.
  - Watch/reload: `FileSystemWatcher` pipeline in `AutoQAC/Services/Configuration/ConfigWatcherService.cs`.
- xEdit log files in the selected xEdit install directory.
  - Client: `AutoQAC/Services/Cleaning/XEditLogFileService.cs` resolves `{wbAppName}Edit_log.txt` and `{wbAppName}EditException.log`.
  - Read model: offset capture before each launch and offset-based reads after process exit.
- Application logs in local filesystem.
  - Client: Serilog file sink configured by `AutoQAC/Infrastructure/Logging/LoggingService.cs`.
  - Retention: `AutoQAC/Services/Configuration/LogRetentionService.cs` deletes old logs by age or count.
- Plugin backup sessions in local filesystem.
  - Client: `AutoQAC/Services/Backup/BackupService.cs` creates timestamped session directories, copies plugin files, writes `session.json`, restores files, and prunes retained sessions.
  - Settings: `AutoQAC/Models/Configuration/BackupSettings.cs` controls enablement and retained session count.
- PID/orphan tracking store in local JSON.
  - Client: `AutoQAC/Services/Process/JsonPidStore.cs` and path provider `AutoQAC/Services/Process/DefaultPidStorePathProvider.cs`.
- User-provided load-order files.
  - Client: `AutoQAC/Services/GameDetection/GameDetectionService.cs` detects game type from `plugins.txt` / `loadorder.txt`; `AutoQAC/Services/Plugin/PluginValidationService.cs` creates plugin lists from file-based load orders.

**Caching:**
- No external cache service detected.
- In-process cache: main configuration is cached in `AutoQAC/Services/Configuration/ConfigurationService.cs` as `_mainConfigCache`.
- In-process state: `AutoQAC/Services/State/StateService.cs` exposes `AppState` through observables.

## Authentication & Identity

**Auth Provider:**
- None.
  - Implementation: local desktop app with no login, OAuth, API keys, cloud identity provider, or network authentication detected.
  - Windows identity: file, registry, and process access run under the current Windows user.

## Monitoring & Observability

**Error Tracking:**
- None external.
- Local error logging uses Serilog through `AutoQAC/Infrastructure/Logging/ILoggingService.cs` and `AutoQAC/Infrastructure/Logging/LoggingService.cs`.

**Logs:**
- Serilog logs Debug+ to rolling files and Warning+ to console in `AutoQAC/Infrastructure/Logging/LoggingService.cs`.
- Startup diagnostics are emitted by `AutoQAC/App.axaml.cs` and include app version, .NET runtime, configured xEdit path, game type, MO2 mode, and plugin count.
- xEdit cleaning diagnostics are parsed from xEdit-owned log files by `AutoQAC/Services/Cleaning/XEditLogFileService.cs` and interpreted by `AutoQAC/Services/Cleaning/XEditOutputParser.cs`.
- Hang detection is local CPU/process monitoring through `AutoQAC/Services/Monitoring/HangDetectionService.cs` and surfaced by the orchestrator/UI state.

## CI/CD & Deployment

**Hosting:**
- Local Windows desktop application.
- No cloud hosting, server process, container, or database deployment target detected.

**CI Pipeline:**
- None detected in `.github/workflows/`.
- Local build/test commands are documented in `README.md` and `CLAUDE.md`.

## Environment Configuration

**Required env vars:**
- None detected.

**Runtime configuration values:**
- `Selected_Game` - selected game enum/string in `AutoQAC/AutoQAC Data/AutoQAC Settings.yaml`.
- `Load_Order.File` - optional file-based load order path in `AutoQAC/AutoQAC Data/AutoQAC Settings.yaml`.
- `Mod_Organizer.Binary` - optional MO2 executable path in `AutoQAC/AutoQAC Data/AutoQAC Settings.yaml`.
- `xEdit.Binary` - required xEdit executable path for cleaning in `AutoQAC/AutoQAC Data/AutoQAC Settings.yaml`.
- `AutoQAC_Settings.Cleaning_Timeout` - xEdit timeout in seconds consumed by `AutoQAC/Services/Cleaning/CleaningService.cs`.
- `AutoQAC_Settings.CPU_Threshold` - CPU threshold used for hang detection state/configuration.
- `AutoQAC_Settings.MO2Mode` - direct-vs-MO2 launch mode consumed by `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`.
- `AutoQAC_Settings.Disable_Skip_Lists` - skip-list behavior setting modeled in `AutoQAC/Models/Configuration/UserConfiguration.cs`.
- `Skip_Lists` - user skip-list overrides in `AutoQAC/AutoQAC Data/AutoQAC Settings.yaml`.
- `Game_Data_Folders` - per-game data folder overrides modeled in `AutoQAC/Models/Configuration/UserConfiguration.cs`.
- `Log_Retention` - log retention mode/count/age modeled in `AutoQAC/Models/Configuration/RetentionSettings.cs`.
- `Backup` - backup enablement and max sessions modeled in `AutoQAC/Models/Configuration/BackupSettings.cs`.

**Secrets location:**
- Not applicable; no secrets, `.env` files, package auth files, credentials, or cloud service keys detected.

## Webhooks & Callbacks

**Incoming:**
- None. No HTTP server, route handlers, webhook endpoints, or listener services detected.

**Outgoing:**
- None. No outbound HTTP API clients, webhook dispatchers, telemetry exporters, or cloud SDK calls detected.
- Process-level outgoing integration is local executable launch only: xEdit direct mode and MO2-wrapped mode in `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`.

---

*Integration audit: 2026-04-29*
