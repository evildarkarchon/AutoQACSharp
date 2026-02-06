# Architecture Research

**Domain:** C# Avalonia MVVM desktop app -- extending with deferred config saves, real-time progress, file system monitoring, CPU monitoring, dry-run mode, backup/rollback, and enhanced process management
**Researched:** 2026-02-06
**Confidence:** HIGH (based on direct codebase analysis; no external sources required for architecture mapping)

## Existing Architecture Overview

```
+-----------------------------------------------------------------------+
|                           Views Layer (XAML)                           |
|  MainWindow  ProgressWindow  SettingsWindow  SkipListWindow  Dialogs  |
+-------------------------------|---------------------------------------+
                                | Data Binding + Interactions
+-------------------------------|---------------------------------------+
|                        ViewModels Layer (ReactiveUI)                  |
|  MainWindowVM  ProgressVM  SettingsVM  SkipListVM  CleaningResultsVM  |
+-------------------------------|---------------------------------------+
                                | DI + IObservable<T>
+-------------------------------|---------------------------------------+
|                         Services Layer (Business Logic)               |
|  +-----------------+  +--------------------+  +-----------+           |
|  | Cleaning        |  | Configuration      |  | State     |           |
|  |  Orchestrator   |  |  Service           |  |  Service  |           |
|  |  Service        |  |  (YAML I/O)        |  | (Reactive)|           |
|  |  CmdBuilder     |  +--------------------+  +-----------+           |
|  |  OutputParser   |                                                  |
|  +-----------------+  +--------------------+  +-----------+           |
|                       | GameDetection      |  | Plugin    |           |
|  +-----------------+  |  Service           |  |  Loading  |           |
|  | Process         |  +--------------------+  |  Validatn |           |
|  |  Execution      |                          +-----------+           |
|  |  Service        |  +--------------------+                          |
|  +-----------------+  | MO2 Validation     |  +-----------+           |
|                       |  Service           |  | UI Srvcs  |           |
|                       +--------------------+  |  FileDlg  |           |
|                                               |  MsgDlg   |           |
|                                               +-----------+           |
+-----------------------------------------------------------------------+
                                |
+-------------------------------|---------------------------------------+
|                         Models Layer (Pure Data)                      |
|  AppState  PluginInfo  CleaningResult  CleaningSessionResult          |
|  PluginCleaningResult  CleaningStatistics  GameType                   |
|  UserConfiguration  MainConfiguration  AutoQacSettings                |
+-----------------------------------------------------------------------+
                                |
+-------------------------------|---------------------------------------+
|                     Infrastructure Layer                              |
|  ILoggingService (Serilog)  ServiceCollectionExtensions (DI)          |
+-----------------------------------------------------------------------+
```

### Current Component Responsibilities

| Component | Responsibility | Communicates With |
|-----------|----------------|-------------------|
| `IStateService` / `StateService` | Centralized reactive state container. Holds `AppState` record, emits `IObservable<AppState>`, `ProgressChanged`, `PluginProcessed`, `CleaningCompleted`. Thread-safe via `Lock`. | ViewModels (observe), CleaningOrchestrator (mutate), ProcessExecutionService (read) |
| `IConfigurationService` / `ConfigurationService` | YAML file I/O for `MainConfiguration` and `UserConfiguration`. Thread-safe via `SemaphoreSlim`. Caches main config. Manages skip lists, game selection, data folder overrides. Emits `UserConfigurationChanged` and `SkipListChanged`. | ViewModels (load/save), CleaningOrchestrator (read skip lists) |
| `ICleaningOrchestrator` / `CleaningOrchestrator` | Workflow coordinator. Validates config, detects game, filters skip lists, processes plugins SEQUENTIALLY, manages cancellation, creates session results. | CleaningService, PluginValidationService, GameDetectionService, StateService, ConfigurationService |
| `ICleaningService` / `CleaningService` | Single-plugin cleaning logic. Validates environment, builds xEdit command via `IXEditCommandBuilder`, executes via `IProcessExecutionService`, parses output via `IXEditOutputParser`. | XEditCommandBuilder, XEditOutputParser, ProcessExecutionService, StateService, ConfigurationService |
| `IProcessExecutionService` / `ProcessExecutionService` | Subprocess lifecycle management. Acquires process slots via `SemaphoreSlim`, handles async output reading, timeout, graceful+forced termination. | StateService (read max slots) |
| `IXEditCommandBuilder` / `XEditCommandBuilder` | Builds `ProcessStartInfo` for xEdit invocations. Handles game flags, MO2 wrapping, partial forms flags. | StateService (read config) |
| `IXEditOutputParser` / `XEditOutputParser` | Stateless regex-based parser for xEdit stdout. Counts ITMs removed, UDRs fixed, records skipped, partial forms created. | None (pure function) |
| `IGameDetectionService` / `GameDetectionService` | Detects `GameType` from xEdit executable name or load order master files. | None (pure function + file I/O) |
| `IPluginLoadingService` / `PluginLoadingService` | Loads plugins via Mutagen (Skyrim/Fallout 4) or file-based parsing. Detects game data folders. | Mutagen library, file system |
| `IPluginValidationService` / `PluginValidationService` | Parses load order files, filters by skip list, validates plugin file existence. | File system |
| `IMo2ValidationService` / `Mo2ValidationService` | Detects running MO2 process, validates MO2 executable path. | System.Diagnostics.Process |
| `IFileDialogService` / `FileDialogService` | Avalonia file/folder dialog abstraction for testability. | Avalonia `TopLevel` |
| `IMessageDialogService` / `MessageDialogService` | Custom modal dialog abstraction (error, warning, retry, info). | Avalonia windows |
| `MainWindowViewModel` | Primary UI logic. Manages path configuration, game selection, plugin display, cleaning start/stop, settings/skip list dialogs. 904 lines -- identified as tech debt. | All services via DI |
| `ProgressViewModel` | Real-time cleaning progress display. Subscribes to `StateChanged` and `PluginProcessed`. | StateService, CleaningOrchestrator |
| `SettingsViewModel` | Settings editing with validation and unsaved change tracking. | ConfigurationService |

### Current Data Flow: Cleaning Workflow

```
User clicks "Start Cleaning"
    |
MainWindowVM.StartCleaningAsync()
    |
    +---> Shows ProgressWindow (non-modal via Interaction)
    |
    +---> CleaningOrchestrator.StartCleaningAsync()
              |
              +---> Validates config (StateService.CurrentState)
              +---> Detects game (GameDetectionService)
              +---> Loads skip list (ConfigurationService)
              +---> Filters plugins
              +---> StateService.StartCleaning(plugins)
              |
              +---> FOR EACH plugin (SEQUENTIAL):
              |       |
              |       +---> StateService.UpdateState(CurrentPlugin = ...)
              |       +---> CleaningService.CleanPluginAsync()
              |       |       |
              |       |       +---> XEditCommandBuilder.BuildCommand()
              |       |       +---> ProcessExecutionService.ExecuteAsync()
              |       |       |       |
              |       |       |       +---> SemaphoreSlim.WaitAsync (slot)
              |       |       |       +---> Process.Start()
              |       |       |       +---> Async output capture
              |       |       |       +---> WaitForExit with timeout
              |       |       |
              |       |       +---> XEditOutputParser.ParseOutput()
              |       |
              |       +---> StateService.AddDetailedCleaningResult()
              |       +---> (PluginProcessed observable fires)
              |       +---> (ProgressChanged observable fires)
              |
              +---> StateService.FinishCleaningWithResults()
              +---> (CleaningCompleted observable fires)
    |
MainWindowVM receives state change, updates StatusText
ProgressVM receives state changes, updates progress display
CleaningResultsWindow shown via Interaction
```

### Current Data Flow: Configuration Save

```
User changes setting (e.g., MO2Mode toggle)
    |
MainWindowVM property setter fires
    |
WhenAnyValue subscription triggers
    |
SaveConfigurationAsync()
    |
ConfigurationService.LoadUserConfigAsync()  <-- reads file
    |
Mutate UserConfiguration object in memory
    |
ConfigurationService.SaveUserConfigAsync()  <-- writes file immediately
    |
Subject<UserConfiguration>.OnNext()  <-- notifies subscribers
```

**Key observation:** Every individual property change triggers a full load-mutate-save cycle. This is the source of the "deferred config saves" requirement.

---

## New Feature Integration Map

### Feature 1: Deferred Configuration Saves

**Layer:** Services (ConfigurationService)
**Problem:** Currently, each property change in `MainWindowViewModel` calls `SaveConfigurationAsync()`, which does a full file read-write cycle. Multiple rapid changes (e.g., toggling MO2 mode then changing game) create redundant I/O and risk race conditions on the file lock.
**Solution:** Dirty-flag pattern with debounced save.

**New/Modified Components:**

| Component | Change Type | Description |
|-----------|-------------|-------------|
| `IConfigurationService` | Modify interface | Add `MarkDirty()`, `FlushAsync()`, `IsDirty` property. Optionally add `DeferredSaveUserConfigAsync()` that queues rather than writes immediately. |
| `ConfigurationService` | Modify implementation | Add `_isDirty` flag, `Timer` or `IObservable` debounce (e.g., 500ms). On `MarkDirty()`, start/reset debounce timer. On timer fire, call existing `SaveUserConfigAsync()`. On `FlushAsync()`, cancel timer and save immediately. On `Dispose()`, flush pending saves. |
| `MainWindowViewModel` | Modify | Replace direct `SaveConfigurationAsync()` calls with `_configService.MarkDirty()`. Call `FlushAsync()` before cleaning starts and on window closing. |
| `SettingsViewModel` | Modify | `SaveAsync()` already does explicit save -- keep as-is, but ensure it resets dirty flag. |

**Data Flow (new):**

```
User toggles MO2 Mode
    |
MainWindowVM.Mo2ModeEnabled setter fires
    |
WhenAnyValue subscription
    |
ConfigurationService.MarkDirty()
    |                                      500ms debounce timer starts
User changes game selection (within 500ms)  |
    |                                      timer resets
ConfigurationService.MarkDirty()
    |
    ... 500ms elapses with no more changes ...
    |
ConfigurationService (internal) auto-saves
    |
LoadUserConfigAsync() (cached) -> mutate -> SaveUserConfigAsync() -> notify
```

**Dependencies:** None -- self-contained within existing ConfigurationService.
**Build order position:** FIRST. No other feature depends on this, but it reduces disk I/O for all subsequent features.

---

### Feature 2: Real-Time Parsed Progress Callbacks

**Layer:** Services (CleaningService, XEditOutputParser) + ViewModels (ProgressViewModel)
**Problem:** The Python reference emits per-record progress with parsed stats (e.g., `{plugin: "foo.esp", action: "undeleted", stats: {undeleted: 3, removed: 12}}`). The C# version only emits raw log lines via `IProgress<string>` and per-plugin status after completion. Mid-plugin progress (how many ITMs found so far) is not surfaced to the UI.
**Solution:** Add a structured progress callback alongside the raw line callback.

**New/Modified Components:**

| Component | Change Type | Description |
|-----------|-------------|-------------|
| `CleaningProgressInfo` | NEW model | Record type: `string PluginName`, `string Action` (undeleted/removed/skipped/partial_forms/completed), `string RawLine`, `CleaningStatistics RunningStats`. |
| `IXEditOutputParser` | Modify interface | Add `ParseLine(string line)` returning `(string? action, CleaningStatistics runningStats)` for incremental parsing. Keep existing `ParseOutput()` for batch use. |
| `XEditOutputParser` | Modify | Add stateful incremental parsing. Track running counts across calls. Add `Reset()` to clear between plugins. |
| `ICleaningService` | Modify interface | Add `IProgress<CleaningProgressInfo>? structuredProgress` parameter to `CleanPluginAsync()`. |
| `CleaningService` | Modify | Wire output callback to call `XEditOutputParser.ParseLine()` on each line, then report structured progress. |
| `IStateService` | Modify interface | Add `IObservable<CleaningProgressInfo> CleaningProgressDetailed` observable. |
| `StateService` | Modify | Add `Subject<CleaningProgressInfo>` and expose as observable. |
| `CleaningOrchestrator` | Modify | Create structured progress reporter, pass to `CleanPluginAsync()`, forward to state service. |
| `ProgressViewModel` | Modify | Subscribe to `CleaningProgressDetailed`. Show running ITM/UDR counts mid-plugin. |

**Data Flow (new):**

```
xEdit writes "Removing: [CELL:00012345]" to stdout
    |
ProcessExecutionService captures line
    |
IProgress<string>.Report(line)  (existing raw callback)
    |
CleaningService output handler
    |
XEditOutputParser.ParseLine(line)
    |  returns ("removed", {ItemsRemoved: 13, ItemsUndeleted: 2, ...})
    |
IProgress<CleaningProgressInfo>.Report(new CleaningProgressInfo { ... })
    |
CleaningOrchestrator forwards to StateService
    |
StateService._cleaningProgressSubject.OnNext(info)
    |
ProgressViewModel subscription fires on MainThreadScheduler
    |
UI updates: "Cleaning MyMod.esp -- 13 ITMs, 2 UDRs so far..."
```

**Dependencies:** None strictly, but benefits from Feature 1 being done (less disk contention during cleaning).
**Build order position:** SECOND. Enhances the core cleaning UX.

---

### Feature 3: Enhanced Process Management

**Layer:** Services (ProcessExecutionService)
**Problem:** Three gaps vs Python reference: (a) No `QWaitCondition`-style timeout on slot acquisition (C# just awaits `SemaphoreSlim` indefinitely). (b) Graceful termination is attempted but there is a race condition (process may hold file handles after `Kill()`). (c) No process tree cleanup (child processes of MO2-wrapped xEdit may survive).
**Solution:** Add acquisition timeout, improve termination sequence, add process tree kill.

**New/Modified Components:**

| Component | Change Type | Description |
|-----------|-------------|-------------|
| `IProcessExecutionService` | Modify interface | Add `TimeSpan? slotTimeout` parameter to `AcquireProcessSlotAsync()`. |
| `ProcessExecutionService` | Modify | (a) Add configurable timeout to `_processSlots.WaitAsync(timeout, ct)`. Throw `TimeoutException` if slot not acquired. (b) Improve `TerminateProcessGracefullyAsync()`: attempt `CloseMainWindow()`, wait 2s, then `process.Kill(entireProcessTree: true)` (.NET 9+ API), wait 1s, log if still alive. (c) Add post-kill delay (500ms) to let file handles release before next plugin starts. |
| `ProcessResult` | Modify | Add `bool WasCancelled` to distinguish user cancellation from timeout. |

**Data Flow:** Unchanged -- this is an internal improvement to process lifecycle handling.

**Dependencies:** None.
**Build order position:** THIRD. Makes the cleaning pipeline more robust before adding monitoring features.

---

### Feature 4: CPU Usage Monitoring

**Layer:** NEW service (ICpuMonitorService) + Services (CleaningOrchestrator)
**Problem:** The Python reference has `check_process(pid, threshold)` using `psutil` to detect hung xEdit processes (CPU < threshold for extended period = likely hung). The C# version has no equivalent.
**Solution:** Lightweight process CPU monitor using `System.Diagnostics.Process.TotalProcessorTime` polling.

**New/Modified Components:**

| Component | Change Type | Description |
|-----------|-------------|-------------|
| `ICpuMonitorService` | NEW interface | `Task<bool> IsProcessActiveAsync(int pid, int thresholdPercent, TimeSpan sampleInterval, CancellationToken ct)`. Returns true if CPU usage exceeds threshold. |
| `CpuMonitorService` | NEW implementation | Poll `Process.GetProcessById(pid).TotalProcessorTime` at two sample points, compute delta / elapsed time / processor count. Compare against threshold. |
| `ProcessExecutionService` | Modify | After starting xEdit process, optionally start CPU monitoring loop. If CPU drops below threshold for configurable duration (e.g., 30s), treat as potential hang and log warning. |
| `CleaningOrchestrator` | Modify | Pass CPU threshold from `AutoQacSettings.CpuThreshold` (already in config model) to process execution. |
| `AppState` | Already has `CleaningTimeout` -- no change needed. `CpuThreshold` is already in `AutoQacSettings`. |
| `IStateService` | Optionally | Add `IObservable<(string plugin, bool isActive)> ProcessActivityChanged` for UI indication. |

**Data Flow (new):**

```
ProcessExecutionService.ExecuteAsync() starts xEdit
    |
CpuMonitorService starts polling loop (every 5s)
    |
    +---> Sample 1: TotalProcessorTime at T1
    +---> Sample 2: TotalProcessorTime at T2
    +---> CPU% = (T2 - T1) / (wallTime * processorCount) * 100
    |
    +---> If CPU% < threshold for 30s consecutive:
    |       Log warning: "xEdit appears inactive"
    |       Optionally: StateService emits activity warning
    |
Process exits normally -> monitoring loop exits
```

**Dependencies:** Feature 3 (enhanced process management) should be done first so the process handle lifecycle is robust.
**Build order position:** FOURTH.

---

### Feature 5: File System Monitoring (Log File Tailing)

**Layer:** NEW service (ILogFileMonitorService) + Services (CleaningService)
**Problem:** The Python reference mentions log file monitoring for xEdit diagnostics. xEdit writes to log files in its working directory; monitoring these provides additional diagnostic data beyond stdout.
**Solution:** Use `FileSystemWatcher` to detect xEdit log file changes, tail new content.

**New/Modified Components:**

| Component | Change Type | Description |
|-----------|-------------|-------------|
| `ILogFileMonitorService` | NEW interface | `IObservable<string> LogLines { get; }`, `void StartMonitoring(string logFilePath)`, `void StopMonitoring()`. |
| `LogFileMonitorService` | NEW implementation | Use `FileSystemWatcher` on the xEdit log directory. On `Changed` event, seek to last-read position and read new lines. Emit via `Subject<string>`. Handle file rotation. |
| `CleaningService` | Modify | Start log monitoring before xEdit process launch (xEdit creates/updates logs in its directory). Stop monitoring after process exits. Forward log lines to structured progress. |
| `ProgressViewModel` | Modify | Optionally display xEdit log file content in a separate tab/section. |

**Data Flow (new):**

```
CleaningService starts cleaning plugin
    |
LogFileMonitorService.StartMonitoring(xEditDir + "\\*.log")
    |
FileSystemWatcher detects change
    |
Read new lines from log file
    |
Subject<string>.OnNext(line)
    |
Subscribers (ProgressVM, logging) receive diagnostic lines
    |
Plugin cleaning completes
    |
LogFileMonitorService.StopMonitoring()
```

**Dependencies:** Feature 2 (structured progress) should exist first so log lines have somewhere to route.
**Build order position:** FIFTH. Nice-to-have diagnostic enhancement.

---

### Feature 6: Dry-Run Mode

**Layer:** Services (CleaningOrchestrator, CleaningService) + ViewModels (MainWindowViewModel)
**Problem:** No way to test configuration without actually cleaning plugins. Users want to verify xEdit path works, skip list is correct, and load order is valid before committing to a cleaning run.
**Solution:** Add a dry-run flag that runs the entire pipeline except the actual xEdit subprocess.

**New/Modified Components:**

| Component | Change Type | Description |
|-----------|-------------|-------------|
| `AppState` | Modify | Add `bool IsDryRun { get; init; }`. |
| `ICleaningOrchestrator` | Modify interface | Add `Task StartDryRunAsync(CancellationToken ct)` or add `bool dryRun` parameter to `StartCleaningAsync()`. |
| `CleaningOrchestrator` | Modify | In dry-run mode: validate config, load plugins, filter skip list, iterate plugins, but skip `CleaningService.CleanPluginAsync()`. Instead, create synthetic `PluginCleaningResult` with `Status = Skipped` and `Message = "Dry run"`. Still update state and emit events so progress window works. |
| `CleaningService` | Modify | Add `ValidateCommandAsync()` that builds the command and validates the executable exists, but does not execute. Return validation result. |
| `MainWindowViewModel` | Modify | Add `DryRunCommand` (ReactiveCommand). Wire to `CleaningOrchestrator.StartDryRunAsync()`. |
| `MainWindow.axaml` | Modify | Add "Dry Run" button near "Start Cleaning". |
| `CleaningSessionResult` | Modify | Add `bool IsDryRun { get; init; }`. Affects `SessionSummary` text. |

**Data Flow (new):**

```
User clicks "Dry Run"
    |
MainWindowVM.DryRunCommand executes
    |
CleaningOrchestrator.StartCleaningAsync(dryRun: true)
    |
    +---> Validates config (REAL validation)
    +---> Detects game (REAL detection)
    +---> Loads skip list (REAL skip list)
    +---> Filters plugins (REAL filtering)
    +---> StateService.StartCleaning(plugins)
    |
    +---> FOR EACH plugin:
    |       CleaningService.ValidateCommandAsync() -- builds command, checks exe exists
    |       StateService.AddDetailedCleaningResult(DryRun result)
    |
    +---> StateService.FinishCleaningWithResults(isDryRun: true)
    |
ProgressWindow shows: "Dry Run: 47 plugins would be cleaned, 3 would be skipped"
CleaningResults shows dry-run banner
```

**Dependencies:** Feature 1 (deferred saves) should be done so config is flushed before dry run validates. Feature 2 (structured progress) is nice-to-have.
**Build order position:** SIXTH. Useful safety feature but not blocking.

---

### Feature 7: Backup/Rollback

**Layer:** NEW service (IBackupService) + Services (CleaningOrchestrator)
**Problem:** No way to restore plugins if cleaning produces undesirable results. Users must manually back up plugins before cleaning.
**Solution:** Before cleaning each plugin, copy the original file to a timestamped backup directory. Provide rollback UI.

**New/Modified Components:**

| Component | Change Type | Description |
|-----------|-------------|-------------|
| `IBackupService` | NEW interface | `Task<string> BackupPluginAsync(PluginInfo plugin, CancellationToken ct)` returns backup path. `Task RollbackPluginAsync(string backupPath, string originalPath, CancellationToken ct)`. `Task<List<BackupEntry>> GetBackupsAsync(CancellationToken ct)`. `Task CleanOldBackupsAsync(int retentionDays, CancellationToken ct)`. |
| `BackupService` | NEW implementation | Copies plugin to `AutoQAC Data/Backups/{timestamp}/{plugin.FileName}`. Uses file copy with overwrite protection. Manages backup manifest (YAML or JSON). |
| `BackupEntry` | NEW model | Record: `string PluginName`, `string BackupPath`, `string OriginalPath`, `DateTime BackupTime`, `GameType Game`. |
| `CleaningOrchestrator` | Modify | Before calling `CleaningService.CleanPluginAsync()`, call `BackupService.BackupPluginAsync()`. On failure, log warning but continue (backup is best-effort). Store backup path in `PluginCleaningResult`. |
| `PluginCleaningResult` | Modify | Add `string? BackupPath { get; init; }`. |
| `CleaningResultsViewModel` | Modify | Add "Rollback" button per-plugin for failed/undesired cleans. |
| `AutoQacSettings` | Modify | Add `bool EnableBackups { get; set; } = true` and `int BackupRetentionDays { get; set; } = 30`. |
| `UserConfiguration` | Already uses `AutoQacSettings` -- no structural change. |
| `ServiceCollectionExtensions` | Modify | Register `IBackupService` / `BackupService`. |

**Data Flow (new):**

```
CleaningOrchestrator processes plugin "MyMod.esp"
    |
    +---> BackupService.BackupPluginAsync(plugin)
    |       |
    |       +---> File.CopyAsync(originalPath, backupDir/timestamp/MyMod.esp)
    |       +---> Returns backup path
    |
    +---> CleaningService.CleanPluginAsync(plugin)
    |
    +---> PluginCleaningResult { BackupPath = "...\\Backups\\2026-02-06_1430\\MyMod.esp" }

--- Later, user views results ---

User clicks "Rollback" on MyMod.esp
    |
CleaningResultsVM.RollbackCommand(result)
    |
BackupService.RollbackPluginAsync(backupPath, originalPath)
    |
File.CopyAsync(backupPath, originalPath, overwrite: true)
```

**Dependencies:** Needs Feature 3 (enhanced process management) so file handles are properly released before backup copy. Needs the plugin `FullPath` resolution (currently identified as tech debt -- `FileName` used as placeholder).
**Build order position:** SEVENTH. Requires the most new infrastructure and has the most prerequisites.

---

## New Service Interfaces Summary

| Interface | Namespace | Registration | Lifetime |
|-----------|-----------|-------------|----------|
| `ICpuMonitorService` | `AutoQAC.Services.Process` | `AddBusinessLogic()` | Singleton |
| `ILogFileMonitorService` | `AutoQAC.Services.Monitoring` | `AddBusinessLogic()` | Singleton |
| `IBackupService` | `AutoQAC.Services.Backup` | `AddBusinessLogic()` | Singleton |

All new services follow the existing pattern: interface + implementation, constructor-injected dependencies, registered in `ServiceCollectionExtensions`.

## New Model Types Summary

| Type | Namespace | Description |
|------|-----------|-------------|
| `CleaningProgressInfo` | `AutoQAC.Models` | Structured per-line progress data during cleaning |
| `BackupEntry` | `AutoQAC.Models` | Metadata for a backed-up plugin file |

---

## Recommended Project Structure (Additions)

```
AutoQAC/
├── Models/
|   ├── CleaningProgressInfo.cs         # NEW: structured progress data
|   └── BackupEntry.cs                  # NEW: backup metadata
├── Services/
|   ├── Backup/
|   |   ├── IBackupService.cs           # NEW
|   |   └── BackupService.cs            # NEW
|   ├── Cleaning/
|   |   ├── CleaningOrchestrator.cs     # MODIFY: backup, dry-run, structured progress
|   |   ├── CleaningService.cs          # MODIFY: structured progress, validate-only
|   |   ├── XEditOutputParser.cs        # MODIFY: incremental parsing
|   |   └── ...
|   ├── Configuration/
|   |   └── ConfigurationService.cs     # MODIFY: deferred saves
|   ├── Monitoring/
|   |   ├── ILogFileMonitorService.cs   # NEW
|   |   └── LogFileMonitorService.cs    # NEW
|   ├── Process/
|   |   ├── ICpuMonitorService.cs       # NEW
|   |   ├── CpuMonitorService.cs        # NEW
|   |   └── ProcessExecutionService.cs  # MODIFY: slot timeout, process tree kill
|   └── State/
|       └── StateService.cs             # MODIFY: new observables
└── ViewModels/
    ├── MainWindowViewModel.cs          # MODIFY: dry-run command, deferred saves
    ├── ProgressViewModel.cs            # MODIFY: structured progress, log tailing
    └── CleaningResultsViewModel.cs     # MODIFY: rollback command
```

---

## Architectural Patterns

### Pattern 1: Dirty-Flag Debounce (Deferred Saves)

**What:** Instead of saving config on every property change, mark config as dirty and save after a debounce interval.
**When to use:** Any service that writes to disk on frequent in-memory state changes.
**Trade-offs:** Reduces disk I/O and race conditions (+), but introduces risk of data loss on crash (-). Mitigated by `FlushAsync()` at critical points (before cleaning, on app close).

**Example:**

```csharp
private bool _isDirty;
private IDisposable? _debounceSubscription;

public void MarkDirty()
{
    _isDirty = true;
    _debounceSubscription?.Dispose();
    _debounceSubscription = Observable.Timer(TimeSpan.FromMilliseconds(500))
        .Subscribe(async _ => await FlushAsync());
}

public async Task FlushAsync(CancellationToken ct = default)
{
    if (!_isDirty) return;
    _isDirty = false;
    _debounceSubscription?.Dispose();
    // Perform actual save
    await SaveUserConfigAsync(_pendingConfig, ct);
}
```

### Pattern 2: Incremental Stateful Parser

**What:** Parser that processes one line at a time, maintaining running totals, instead of batch-processing all lines at the end.
**When to use:** When real-time feedback during a long-running operation is needed.
**Trade-offs:** More complex state management (+), enables live UI updates (+), parser must be `Reset()` between uses (-).

**Example:**

```csharp
public sealed class IncrementalOutputParser
{
    private int _removed, _undeleted, _skipped, _partialForms;

    public (string? action, CleaningStatistics stats) ParseLine(string line)
    {
        if (RemovedPattern().IsMatch(line))
        {
            _removed++;
            return ("removed", CurrentStats);
        }
        // ... other patterns
        return (null, CurrentStats);
    }

    public CleaningStatistics CurrentStats => new()
    {
        ItemsRemoved = _removed,
        ItemsUndeleted = _undeleted,
        ItemsSkipped = _skipped,
        PartialFormsCreated = _partialForms
    };

    public void Reset() { _removed = _undeleted = _skipped = _partialForms = 0; }
}
```

### Pattern 3: Observable Service Monitor

**What:** A service that runs a periodic monitoring loop and exposes results as `IObservable<T>`.
**When to use:** CPU monitoring, file system watching, process health checks.
**Trade-offs:** Fits ReactiveUI architecture perfectly (+), clean cancellation via `CancellationToken` (+), must handle disposal carefully to avoid leaks (-).

**Example:**

```csharp
public IObservable<bool> MonitorProcessActivity(int pid, int thresholdPercent)
{
    return Observable.Create<bool>(async (observer, ct) =>
    {
        while (!ct.IsCancellationRequested)
        {
            var isActive = await CheckCpuUsageAsync(pid, thresholdPercent);
            observer.OnNext(isActive);
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    });
}
```

---

## Anti-Patterns to Avoid

### Anti-Pattern 1: Synchronous File I/O in Property Setters

**What people do:** Call `SaveConfigAsync().Wait()` or `SaveConfigAsync().Result` in a ReactiveUI property subscription.
**Why it's wrong:** Deadlocks the UI thread. The `RxApp.MainThreadScheduler` is the Avalonia dispatcher; blocking it prevents the async continuation from completing.
**Do this instead:** Use the dirty-flag debounce pattern. Property changes mark dirty; saving happens asynchronously on a background scheduler.

### Anti-Pattern 2: Passing Stateful Parsers Across Threads Without Synchronization

**What people do:** Share an `IncrementalOutputParser` between the `OutputDataReceived` callback thread and the UI thread without synchronization.
**Why it's wrong:** `OutputDataReceived` fires on a threadpool thread. If the UI reads `CurrentStats` simultaneously, race condition.
**Do this instead:** The parser should only be accessed from the output callback. Publish immutable `CleaningStatistics` snapshots via `IProgress<T>` which marshals to the correct thread.

### Anti-Pattern 3: FileSystemWatcher Without Debounce

**What people do:** React to every `FileSystemWatcher.Changed` event immediately.
**Why it's wrong:** File writes trigger multiple `Changed` events (write, flush, close). Reading immediately may find a partially-written file.
**Do this instead:** Debounce `Changed` events by 100-200ms before reading. Use `Observable.Throttle()` on the event stream.

### Anti-Pattern 4: Backup Without FullPath Resolution

**What people do:** Attempt to back up a plugin using `PluginInfo.FileName` without resolving the actual file path.
**Why it's wrong:** `PluginInfo.FullPath` is currently a placeholder in some code paths (identified tech debt). Backing up a non-existent path silently fails.
**Do this instead:** Resolve the `FullPath` tech debt BEFORE implementing backup. The backup service should validate `File.Exists(plugin.FullPath)` before copying.

---

## Integration Points

### Internal Boundaries

| Boundary | Communication | Notes |
|----------|---------------|-------|
| ConfigurationService deferred saves | Timer-based auto-save with `FlushAsync()` for explicit save points | Must flush before cleaning starts, on app close, and on settings dialog save |
| Structured progress pipeline | `IProgress<CleaningProgressInfo>` from CleaningService to CleaningOrchestrator to StateService to ProgressViewModel | Progress objects are immutable records -- thread-safe by construction |
| CPU monitoring to cleaning pipeline | CpuMonitorService observes process PID, CleaningOrchestrator subscribes to activity observable | Activity warnings are informational only -- do not auto-kill processes |
| Backup to cleaning pipeline | BackupService called by CleaningOrchestrator before each plugin clean | Backup failure should log warning but NOT block cleaning |
| Dry-run to cleaning pipeline | Boolean flag gates subprocess execution in CleaningOrchestrator | Shares all validation and plugin filtering logic with real cleaning |
| Log file monitoring to progress UI | LogFileMonitorService emits lines, ProgressViewModel displays in diagnostic section | FileSystemWatcher must be disposed when monitoring stops |

### Prerequisite: PluginInfo.FullPath Resolution

**BLOCKING for Feature 7 (Backup/Rollback).** Currently `PluginInfo.FullPath` is populated correctly when loaded via Mutagen (absolute path from game data folder) but may be a placeholder when loaded from a file-based load order. Before implementing backup:

1. Ensure `PluginLoadingService.GetPluginsFromFileAsync()` resolves full paths using the game data folder.
2. Ensure `PluginValidationService.GetPluginsFromLoadOrderAsync()` resolves paths similarly.
3. Add `ValidatePluginExists()` check in BackupService before attempting copy.

---

## Build Order (Dependency-Driven)

```
Phase A: Foundation Hardening (no inter-dependencies)
    |
    +---> [1] Deferred Configuration Saves
    |         Standalone. Reduces disk I/O. Enables safer config operations.
    |
    +---> [3] Enhanced Process Management
    |         Standalone. Fixes process termination race conditions.
    |         Prerequisite for CPU monitoring and backup.
    |
Phase B: Core Enhancements (depends on Phase A)
    |
    +---> [2] Real-Time Parsed Progress Callbacks
    |         Depends on: nothing strictly, but benefits from Phase A stability.
    |         Prerequisite for: log file monitoring display.
    |
    +---> [4] CPU Usage Monitoring
    |         Depends on: [3] Enhanced Process Management (needs robust process handle).
    |
Phase C: Diagnostic Features (depends on Phase B)
    |
    +---> [5] File System Monitoring (Log File Tailing)
    |         Depends on: [2] Structured progress (needs progress pipeline for display).
    |
Phase D: Safety Features (depends on Phase A + tech debt resolution)
    |
    +---> [6] Dry-Run Mode
    |         Depends on: [1] Deferred saves (flush before validation).
    |         Lightweight -- no new services, just a boolean flag.
    |
    +---> [7] Backup/Rollback
              Depends on: [3] Process management (file handles released).
              Depends on: PluginInfo.FullPath tech debt resolution (BLOCKING).
              Most new infrastructure. Build last.
```

### Recommended Implementation Sequence

| Order | Feature | Estimated Effort | Risk |
|-------|---------|-----------------|------|
| 1 | Deferred Configuration Saves | Low (modify 2 files) | Low -- well-understood pattern |
| 2 | Enhanced Process Management | Medium (modify 1 file + tests) | Medium -- process lifecycle edge cases |
| 3 | Real-Time Parsed Progress | Medium (modify 3 files, new model) | Low -- parser logic is straightforward |
| 4 | CPU Usage Monitoring | Medium (new service + integration) | Medium -- CPU measurement accuracy varies |
| 5 | Dry-Run Mode | Low (modify 2 files, new button) | Low -- subset of existing pipeline |
| 6 | File System Monitoring | Medium (new service + FileSystemWatcher) | Medium -- FSW reliability on Windows |
| 7 | PluginInfo.FullPath Resolution | Low-Medium (modify 2 services) | Low -- straightforward path resolution |
| 8 | Backup/Rollback | High (new service + model + UI) | Medium -- file operations + manifest management |

**Critical path:** Items 1-3 can proceed in parallel. Item 4 depends on 2. Item 8 depends on 2 and 7.

---

## Sources

- Direct analysis of all source files in `J:\AutoQACSharp\AutoQAC\` (HIGH confidence -- primary source)
- Python reference implementation in `J:\AutoQACSharp\Code_To_Port\AutoQACLib\` (HIGH confidence -- authoritative for feature parity requirements)
- `J:\AutoQACSharp\.planning\PROJECT.md` active requirements list (HIGH confidence -- project owner maintained)
- Existing `ROADMAP.md` for historical context (MEDIUM confidence -- partially outdated, original parity roadmap)

---
*Architecture research for: AutoQACSharp milestone -- extending existing MVVM app with robustness and safety features*
*Researched: 2026-02-06*
