# External Integrations

**Analysis Date:** 2026-02-06

## External Applications & Tools

**xEdit Integration (Critical):**
- Bethesda plugin cleaner tools (TES4Edit, TES5Edit, SSEEdit, FO4Edit, etc.)
- Integration method: `System.Diagnostics.Process` with subprocess invocation
- Supported xEdit flavors: Universal xEdit, game-specific variants (FO3Edit, FNVEdit, FO4Edit, SSEEdit, TES5Edit, SkyrimVREdit, FO4VREdit)
- Command flags: `-QAC -autoexit -autoload "{plugin}"` with optional `-iknowwhatimdoing -allowmakepartial` for partial forms
- Output parsing: Regex patterns in `XEditOutputParser` match "Undeleting:", "Removing:", "Skipping:", "Making Partial Form:" lines
- Timeout: Configurable via `AutoQacSettings.CleaningTimeout` (default 300 seconds)
- Location: `IXEditCommandBuilder` builds ProcessStartInfo; execution via `ProcessExecutionService`
- Binary path: User-configured in `UserConfiguration.XEdit.Binary`

**Mod Organizer 2 (Optional):**
- Integration type: Wrapper execution via MO2's `run` command
- Usage: When `AutoQacSettings.Mo2Mode` is enabled and `UserConfiguration.ModOrganizer.Binary` is configured
- Invocation: `ModOrganizer.exe run "{xEditPath}" -a "{xEditArgs}"`
- Validation: `Mo2ValidationService.ValidateMo2ExecutableAsync()` checks binary name is "ModOrganizer.exe"
- Process detection: `Mo2ValidationService.IsMo2Running()` checks if ModOrganizer process is active
- Location: `XEditCommandBuilder` wraps xEdit commands; `Mo2ValidationService` provides validation
- Binary path: User-configured in `UserConfiguration.ModOrganizer.Binary`
- Warning: Runtime detection alerts if MO2 is running (user should close before cleaning)

## Data Sources & Storage

**Local Configuration Files (YAML):**
- Location: `AutoQAC Data/` directory (configurable via `ConfigurationService`)
- Storage mechanism: YamlDotNet deserialization with NullNamingConvention
- Files:
  - `AutoQAC Main.yaml` - Read-only application defaults (distributed)
    - `XEdit_Lists`: Game-specific xEdit executable names
    - `Skip_Lists`: Default skip lists for games (DLC/base game protection)
    - Version tracking
  - `AutoQAC Settings.yaml` - User-writable configuration
    - Selected game type
    - Custom skip lists per game
    - Game data folder overrides
    - xEdit and MO2 binary paths
    - Cleaning timeout, CPU threshold, journal expiration

**Local File Storage:**
- Plugin load order: User-specified file path (typically `plugins.txt`)
- Game data folders: Detected via Mutagen or registry, with user override support
- Logs: Rolling file logs in `logs/` directory (daily rotation, 5MB size limit, 5 files retained)
- Default load order paths: `My Documents\My Games\{GameFolder}\plugins.txt` for unsupported Mutagen games

## Game Detection & Plugin Loading

**Mutagen.Bethesda Integration:**
- Supported games: Skyrim LE, Skyrim SE, Skyrim VR, Fallout 4, Fallout 4 VR
- Functionality: Automatic game installation detection via `GameEnvironment.Typical` (registry-based)
- Load order detection: `env.LoadOrder.ListedOrder` lists plugins in load order
- Data folder detection: `env.DataFolderPath.Path` returns game data directory
- Game releases mapped: `GameRelease.SkyrimLE`, `GameRelease.SkyrimSE`, `GameRelease.SkyrimVR`, `GameRelease.Fallout4`, `GameRelease.Fallout4VR`
- Location: `PluginLoadingService` uses Mutagen for supported games
- Fallback: File-based load order for unsupported games (FO3, FNV, Oblivion)

**Game Detection Methods:**
- Executable-based: Pattern matching on xEdit executable names (tes5edit, sseedit, fo4edit, etc.)
- Load order file-based: Master file detection (Skyrim.esm, Fallout4.esm, etc.)
- Location: `GameDetectionService`

## Registry Access

**Windows Registry (Indirect via Mutagen):**
- Purpose: Game installation directory detection
- Method: Mutagen's `GameEnvironment.Typical.Builder()` reads registry for game locations
- Used by: `PluginLoadingService.GetGameDataFolder()` and load order detection
- Games: Skyrim LE/SE/VR, Fallout 4/4VR
- No direct registry access in application code; delegated to Mutagen

## Process Management

**Subprocess Execution:**
- Framework: `System.Diagnostics.Process`
- Sequential processing: Semaphore-based limiting to single xEdit process (configurable via `AutoQacSettings.MaxConcurrentSubprocesses`)
- Output capture: Async streaming via `OutputDataReceived` and `ErrorDataReceived` events
- Timeout handling: Configurable timeout with graceful termination (CloseMainWindow -> Kill)
- Cancellation: CancellationToken support with linked timeout tokens
- Location: `ProcessExecutionService`

## Logging Infrastructure

**Serilog Integration:**
- Configuration: Initialized in `LoggingService` constructor
- Output sinks:
  - Console: Warning level and above
  - File: Rolling daily logs in `logs/autoqac-.log` (5MB limit per file, 5 files retained)
- Log format: `{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}`
- Levels: Debug, Information, Warning, Error, Fatal
- Thread-safe: Via `Serilog.Log.CloseAndFlush()` on disposal
- Location: `LoggingService` (singleton)

## Authentication & Authorization

**No External Auth:**
- Application is desktop standalone
- No remote authentication required
- Configuration stored locally (user-writable files)

## Environment Configuration

**Required Application Settings:**
- `XEditBinary`: Path to xEdit executable (user must configure)
- `LoadOrderFile`: Path to plugins.txt or equivalent (user must configure)
- `SelectedGame`: Game type for plugin cleaning (user selection)

**Optional Configuration:**
- `Mo2ExecutablePath`: Path to ModOrganizer.exe (for MO2 mode)
- `CustomGameDataFolder`: Override automatic game data detection
- `CleaningTimeout`: Max seconds per plugin (default 300)
- `PartialFormsEnabled`: Allow partial form creation (default false)
- `DisableSkipLists`: Bypass skip list protection (default false)

**Configuration Storage:**
- YAML files in `AutoQAC Data/` (local filesystem)
- No environment variables used for secrets
- No cloud configuration

## No Remote Integrations

**Not Integrated:**
- No REST APIs called
- No cloud services (AWS, Azure, Google Cloud)
- No webhooks
- No CDN or file hosting
- No error tracking (Sentry, etc.)
- No analytics or telemetry
- No database connections (all local files)
- No container registries
- No message queues
- No email services

## File I/O & Async Patterns

**File Access:**
- All file operations use `System.IO` with async/await (`File.ReadAllTextAsync`, `File.WriteAllTextAsync`)
- Configuration persistence: Serialization/deserialization via YamlDotNet
- Thread-safe file access: `SemaphoreSlim` locking in `ConfigurationService`
- Cancellation support: `CancellationToken` passed to all async file operations

---

*Integration audit: 2026-02-06*
