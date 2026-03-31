# External Integrations

**Analysis Date:** 2026-03-30

## Process Integration

### xEdit (Primary External Process)

AutoQAC's core function is launching xEdit with Quick Auto Clean (`-QAC`) flags, one plugin at a time.

**Command Builder:** `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`
- Builds `ProcessStartInfo` with flags: `-QAC -autoexit -autoload "<plugin>"`
- Adds game-type flag for universal `xEdit.exe` (e.g., `-SSE`, `-FO4`, `-TES4`)
- Adds `-iknowwhatimdoing -allowmakepartial` when partial forms are enabled
- In MO2 mode, wraps the entire command: `ModOrganizer.exe run "<xEdit>" -a "<args>"`

**Process Execution:** `AutoQAC/Services/Process/ProcessExecutionService.cs`
- Single-slot semaphore (`SemaphoreSlim(1, 1)`) enforces sequential xEdit execution
- Captures stdout/stderr via `RedirectStandardOutput`/`RedirectStandardError`
- Timeout support with configurable duration (default 300 seconds from settings)
- Two-stage termination: `CloseMainWindow()` first, then `Kill(entireProcessTree: true)` after 2.5s grace
- PID tracking via `autoqac-pids.json` in the `AutoQAC Data` directory for orphan detection on startup

**Output Parsing:** `AutoQAC/Services/Cleaning/XEditOutputParser.cs`
- Regex-based parsing of xEdit stdout for: `Removing:`, `Undeleting:`, `Skipping:`, `Making Partial Form:`
- Uses source-generated regex (`[GeneratedRegex(...)]`)

**Log File Parsing:** `AutoQAC/Services/Cleaning/XEditLogFileService.cs`
- Reads `<XEDIT_NAME>_log.txt` from the xEdit directory after each cleaning run
- Staleness detection: rejects log files older than the process start time
- Single retry with 200ms delay on `IOException` (xEdit may hold file lock briefly)
- Log-file stats preferred over stdout stats when available

**Known xEdit process names** (for orphan detection):
`sseedit`, `fo4edit`, `fo3edit`, `fnvedit`, `tes5vredit`, `xedit`, `fo76edit`, `tes4edit`

### Mod Organizer 2 (Optional Process Wrapper)

**Validation:** `AutoQAC/Services/MO2/Mo2ValidationService.cs`
- Checks if MO2 is running via `Process.GetProcessesByName("ModOrganizer")`
- Validates the configured path points to `modorganizer.exe`

**Integration Pattern:**
- When MO2 mode is enabled, xEdit is launched through MO2's `run` command
- Backups are skipped in MO2 mode (MO2 uses a virtual filesystem)
- File-existence validation is skipped in MO2 mode (MO2 VFS resolves paths at runtime)

## File System Integration

### Configuration Files

**Location:** `AutoQAC Data/` directory (bundled with app, copied to output)

| File | Purpose | Service |
|------|---------|---------|
| `AutoQAC Main.yaml` | Bundled defaults: skip lists, xEdit exe names, version | `ConfigurationService` |
| `AutoQAC Settings.yaml` | User settings: paths, game selection, timeouts | `ConfigurationService` |
| `autoqac-pids.json` | Tracked xEdit process PIDs for orphan detection | `ProcessExecutionService` |

**Config Service:** `AutoQAC/Services/Configuration/ConfigurationService.cs`
- YAML serialization via YamlDotNet with `NullNamingConvention`
- Debounced save pipeline: 500ms throttle via `System.Reactive` before writing to disk
- Retry logic: up to 2 retries with 100ms delay on save failure
- Fallback to last-known-good config on persistent save failure
- SHA256 hashing to detect external vs internal changes
- Thread-safe with `SemaphoreSlim` for file I/O and `Lock` for in-memory state

**Config Watcher:** `AutoQAC/Services/Configuration/ConfigWatcherService.cs`
- `FileSystemWatcher` on `AutoQAC Settings.yaml` for external edits
- 500ms throttle to coalesce rapid filesystem events
- SHA256 content hashing to distinguish app-initiated saves from external edits
- Defers reloading during active cleaning sessions; applies changes when cleaning ends
- YAML validation before accepting external changes

### Plugin Discovery

**Mutagen-backed (preferred):** `AutoQAC/Services/Plugin/PluginLoadingService.cs`
- Uses `Mutagen.Bethesda.Plugins.Order.LoadOrder.GetLoadOrderListings()` for supported games
- Uses `Mutagen.Bethesda.Installs.GameLocations.TryGetDataFolder()` for auto-detection
- Supported games: SkyrimLE, SkyrimSE, SkyrimVR, Fallout4, Fallout4VR

**File-based (fallback):** `AutoQAC/Services/Plugin/PluginLoadingService.cs`
- Reads `plugins.txt` from `Documents/My Games/<game>/` for non-Mutagen games
- Used for: Fallout 3, Fallout New Vegas, Oblivion
- Maps game types to folder names: `Fallout3`, `FalloutNV`, `Oblivion`

### Backup System

**Service:** `AutoQAC/Services/Backup/BackupService.cs`
- Pre-cleaning file backup: copies plugin files to timestamped session directories
- Backup root: `<game-install>/AutoQAC Backups/<yyyy-MM-dd_HH-mm-ss>/`
- Session metadata: `session.json` in each session directory (System.Text.Json serialized)
- Retention: configurable `MaxSessions` count; oldest sessions pruned after each cleaning run
- Restore: copies backup files back to original paths
- Skipped in MO2 mode (MO2 manages files through its virtual filesystem)

### Log Files

**Service:** `AutoQAC/Infrastructure/Logging/LoggingService.cs`
- Serilog with rolling daily log files
- File size limit: 5 MB per file, 5 retained files
- Console output restricted to Warning+ level
- Additional log retention cleanup via `ILogRetentionService` on startup

## Game Detection

### Registry Probing

**Service:** `AutoQAC/Services/Plugin/PluginLoadingService.cs` (method `ResolveDataFolderFromRegistry`)
- Probes Windows Registry for game install paths
- Registry hives: `HKLM` and `HKCU`, both 32-bit and 64-bit views
- Key patterns per game:
  - `SOFTWARE\WOW6432Node\Bethesda Softworks\<game>`
  - `SOFTWARE\Bethesda Softworks\<game>`
  - `SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App <appid>`
- Value names checked: `Installed Path`, `Install Path`, `InstallLocation`, `Path`
- Normalizes paths: appends `\Data` if the install root is found

**Steam App IDs mapped:**

| Game | Steam App ID |
|------|-------------|
| Oblivion | 22330 |
| Fallout 3 | 22300 |
| Fallout New Vegas | 22380 |
| Skyrim LE | 72850 |
| Skyrim SE | 489830 |
| Skyrim VR | 611670 |
| Fallout 4 | 377160 |
| Fallout 4 VR | 611660 |

### Executable-Based Detection

**Service:** `AutoQAC/Services/GameDetection/GameDetectionService.cs`
- Maps xEdit executable filenames to game types (e.g., `sseedit` -> SkyrimSE, `fo4edit` -> Fallout4)
- Supports 64-bit variants (e.g., `sseedit64`, `fo3edit64`)
- Partial matching for versioned filenames (e.g., `SSEEdit 4.0.4`)

### Load Order-Based Detection

**Service:** `AutoQAC/Services/GameDetection/GameDetectionService.cs`
- Scans load order file for known master ESMs (e.g., `Skyrim.esm`, `Fallout4.esm`)
- Handles `plugins.txt` format (strips leading `*` enabled flags, ignores `#` comments)

### Game Variant Detection

**Service:** `AutoQAC/Services/GameDetection/GameDetectionService.cs`
- Detects TTW (Tale of Two Wastelands) via `TaleOfTwoWastelands.esm` in FalloutNV load order
- Detects Enderal via `Enderal - Forgotten Stories.esm` or `Enderal.esm` in SkyrimSE load order
- Variants affect skip list merging (TTW includes FO3 skip list; Enderal uses its own key)

## Mutagen Integration

### In AutoQAC (Load Order Discovery)

**Service:** `AutoQAC/Services/Plugin/PluginLoadingService.cs`
- `LoadOrder.GetLoadOrderListings()` to enumerate the full load order for a game
- `GameLocations.TryGetDataFolder()` to auto-detect game data folder from registry/standard paths
- Game type mapping: `GameType` enum -> `Mutagen.Bethesda.GameRelease` enum

### In AutoQAC (Plugin Issue Approximations)

**Service:** `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs`
- Loads entire load order into memory using `LoadOrder.Import<ISkyrimModGetter>()` or `LoadOrder.Import<IFallout4ModGetter>()`
- Creates `ImmutableLinkCache` for cross-reference resolution
- Delegates analysis to `QueryPlugins.PluginQueryService`
- Supported games: SkyrimLE, SkyrimSE, SkyrimVR, Fallout4, Fallout4VR
- Streams results via callback (`onApproximationReady`) for progressive UI updates

### In QueryPlugins (Plugin Analysis Library)

**Service:** `QueryPlugins/PluginQueryService.cs`
- Orchestrates detectors: ITM detector + game-specific detectors
- Accepts `IModGetter`, `ILinkCache`, and `GameRelease` as inputs

**ITM Detection:** `QueryPlugins/Detectors/ItmDetector.cs`
- Game-agnostic: works on any `IModGetter`
- Uses `ILinkCache.ResolveAllSimpleContexts()` to find all versions of a record
- Compares plugin's override to the next lower-priority context via Loqui-generated `Equals()`
- Excludes new records (FormKey matches plugin's ModKey) and deleted records

**Game-Specific Detectors:**

| Detector | File | Supported Releases |
|----------|------|--------------------|
| SkyrimDetector | `QueryPlugins/Detectors/Games/SkyrimDetector.cs` | SkyrimLE, SkyrimSE, SkyrimSEGog, SkyrimVR, EnderalLE, EnderalSE |
| Fallout4Detector | `QueryPlugins/Detectors/Games/Fallout4Detector.cs` | Fallout4, Fallout4VR |
| StarfieldDetector | `QueryPlugins/Detectors/Games/StarfieldDetector.cs` | Starfield |
| OblivionDetector | `QueryPlugins/Detectors/Games/OblivionDetector.cs` | Oblivion |

**Detection Capabilities:**
- Deleted placed references (REFR/ACHR): checks `IsDeleted` flag on `IPlacedGetter` records
- Deleted navigation meshes (NAVM): checks `IsDeleted` flag on `INavigationMeshGetter` records
- Oblivion detector returns empty for navmeshes (game does not have them)

## External Services

### GitHub API (Update Check)

**ViewModel:** `AutoQAC/ViewModels/AboutViewModel.cs`
- Single outbound HTTP call to `https://api.github.com/repos/evildarkarchon/AutoQACSharp/releases/latest`
- Uses `System.Net.Http.HttpClient` (static singleton, 10-second timeout)
- User-Agent: `AutoQACSharp/1.0`
- Parses `tag_name` and `html_url` from JSON response
- Compares version against `Assembly.GetEntryAssembly().GetName().Version`
- User-initiated only (no auto-update, no background polling)

### No Other Network Dependencies

- No telemetry or analytics
- No cloud storage or remote databases
- No authentication providers
- No webhook endpoints
- All game detection is local (registry + filesystem)
- All configuration is local (YAML files)
- All plugin analysis is local (Mutagen reads files from disk)

## Process Monitoring

### Hang Detection

**Service:** `AutoQAC/Services/Monitoring/HangDetectionService.cs`
- CPU-based monitoring using `Process.TotalProcessorTime`
- Polls every 5 seconds (`PollIntervalMs`)
- Flags as hung after 60 seconds of near-zero CPU (`HangThresholdMs`)
- Near-zero threshold: < 0.5% CPU usage (`CpuThreshold`)
- Uses `System.Reactive.Linq.Observable.Interval` for polling
- Emits `true`/`false` via `IObservable<bool>` to drive UI warning
- Automatically completes when process exits

## Environment Configuration

**Required for operation:**
- xEdit executable path (configured in `AutoQAC Settings.yaml` or via Settings UI)
- Game data folder (auto-detected via Mutagen/registry or user-configured)

**Required for non-Mutagen games (FO3, FNV, Oblivion):**
- Load order file path (`plugins.txt`)

**Optional:**
- MO2 executable path (only if MO2 mode is enabled)
- Game data folder overrides (per-game in `AutoQAC Settings.yaml`)
- Load order file overrides (per-game in `AutoQAC Settings.yaml`)

**No secrets or API keys required.**

## CI/CD & Deployment

**Hosting:** Local desktop application (no server deployment)
**CI Pipeline:** No CI/CD detected (no `.github/workflows/`, no pipeline configs)
**Release Build:** `dotnet build AutoQAC/AutoQAC.csproj -c Release`
**Pre-built release:** `Release/` directory contains compiled output with native dependencies

---

*Integration audit: 2026-03-30*
