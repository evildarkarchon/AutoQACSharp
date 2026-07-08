# Phase 8: Cleaning Orchestrator Decomposition - Pattern Map

**Mapped:** 2026-04-29
**Files analyzed:** 11 new files (5 interface + 5 implementation pairs + 1 facade modification) + 5 new test files
**Analogs found:** 11 / 11 (100% coverage)

## File Classification

### Production Files (under `AutoQAC/Services/Cleaning/`)

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `ICleaningPreflight.cs` | service interface | request-response (pure async) | `AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs` (XML doc style) + `AutoQAC/Services/Backup/IBackupService.cs` (multi-method shape) | exact (collaborator interface in same folder) |
| `CleaningPreflight.cs` | service implementation | request-response (pure async) | `AutoQAC/Services/Cleaning/CleaningService.cs` (primary-constructor sealed class) | exact (sibling cleaning service, primary ctor pattern) |
| `IBackupSessionCoordinator.cs` | service interface | event-driven (CTS + progress + outcome enum) | `AutoQAC/Services/Backup/IBackupService.cs` (CTS + IProgress + structured-result records) | exact (wraps `IBackupService` directly; same API shape) |
| `BackupSessionCoordinator.cs` | service implementation | event-driven (CTS + progress + lock state) | `AutoQAC/Services/Backup/BackupService.cs` (traditional ctor with optional dep + ILogger) + `CleaningOrchestrator.cs` lines 679-808 (current `BackupPluginAsync`/`CleanupOldSessionsAsync` helpers) | exact (code lift-and-shift) |
| `IPluginCleaningRunner.cs` | service interface | request-response (retry FSM) | `AutoQAC/Services/Cleaning/CleaningService.cs:14-21` (single-method service interface, primary ctor) | exact (sibling cleaning service) |
| `PluginCleaningRunner.cs` | service implementation | request-response (retry FSM with delegates) | `AutoQAC/Services/Cleaning/CleaningService.cs` lines 22-143 (try/catch around `processService.ExecuteAsync` + `ConfigureAwait(false)` + structured result) | exact |
| `IPluginResultFinalizer.cs` | service interface | transform (pure function over inputs) | `AutoQAC/Services/Cleaning/IXEditOutputParser` (pure-function service in same folder) | role-match (transform/parse) |
| `PluginResultFinalizer.cs` | service implementation | transform | `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` lines 456-513 (current log-read + parse + result-build block) | exact (code lift-and-shift) |
| `ICleaningTerminationCoordinator.cs` | service interface | event-driven + state-owning | `AutoQAC/Services/Process/IProcessExecutionService.cs` (terminate methods + structured `TerminationResult`) + `AutoQAC/Services/Monitoring/IHangDetectionService.cs` (`IObservable<bool>` exposure) | exact (combines both) |
| `CleaningTerminationCoordinator.cs` | service implementation | event-driven + state-owning | `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` lines 813-964 + 1106-1116 (current `StopCleaningAsync`/`ForceStopCleaningAsync`/`MarkLeftRunningByUser`/`StartHangMonitoring`) | exact (code lift-and-shift) |
| `CleaningOrchestrator.cs` (modified) | service facade | request-response | itself (current implementation) | exact (in-place reduction) |
| `ServiceCollectionExtensions.cs` (modified) | DI registration | configuration | `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs:41-64` (existing `AddBusinessLogic` block) | exact |

### Test Files (under `AutoQAC.Tests/Services/Cleaning/` — new subfolder per Research Risk #8)

| New Test File | Role | Closest Analog | Match Quality |
|---------------|------|----------------|---------------|
| `AutoQAC.Tests/Services/Cleaning/CleaningPreflightTests.cs` | service-isolated unit | `AutoQAC.Tests/Services/CleaningServiceTests.cs` (NSubstitute-only, no temp files) | exact |
| `AutoQAC.Tests/Services/Cleaning/BackupSessionCoordinatorTests.cs` | service-isolated unit | `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs` (existing backup-failure-choice tests at lines 1446, 1539, 1579, 1629) — collaborator-level extraction; some setup may use temp paths like `BackupServiceTests.cs` | exact |
| `AutoQAC.Tests/Services/Cleaning/PluginCleaningRunnerTests.cs` | service-isolated unit | `AutoQAC.Tests/Services/CleaningServiceTests.cs` (mock-only, returns `ProcessResult`) | exact |
| `AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs` | service-isolated unit | `AutoQAC.Tests/Services/XEditOutputParserTests.cs` (pure-function tests) + `CleaningOrchestratorTests.cs` log-skip-after-cancel (line 1974) | exact |
| `AutoQAC.Tests/Services/Cleaning/CleaningTerminationCoordinatorTests.cs` | service-isolated unit | `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs` lines 617, 679, 743, 804, 870 (existing stop/force-stop/self-PID tests) + `HangDetectionServiceTests.cs` (real-process pattern) | exact |

## Pattern Assignments

### `ICleaningPreflight.cs` (service interface, request-response)

**Analog:** `AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs` (1-88) for XML doc shape; `AutoQAC/Services/Backup/IBackupService.cs` (12-108) for multi-method declarations with structured records

**Imports + namespace pattern** (`ICleaningOrchestrator.cs:1-9`):
```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.Cleaning;
```

**XML doc on async method** (`ICleaningOrchestrator.cs:80-87`):
```csharp
/// <summary>
/// Runs the validation pipeline without invoking xEdit. Returns a preview of which
/// plugins would be cleaned or skipped, with human-readable reasons for each.
/// Does not mutate application state, start processes, or create cancellation tokens.
/// </summary>
/// <param name="ct">Cancellation token.</param>
/// <returns>A list of DryRunResult entries, one per plugin.</returns>
Task<List<DryRunResult>> RunDryRunAsync(CancellationToken ct = default);
```

**Why this analog:** `ICleaningOrchestrator.RunDryRunAsync` is itself the closest sibling — same purpose (read-only preview), same `CancellationToken ct = default` last-param convention, same XML doc structure with `<summary>`/`<param>`/`<returns>`. The new `PrepareAsync` should clone this docstring shape verbatim with content adapted from CONTEXT D-13 through D-16.

**Sealed-record output type pattern** (from `AutoQAC/Services/Process/IProcessExecutionService.cs:46-53`):
```csharp
public sealed record ProcessResult
{
    public int ExitCode { get; init; }
    public bool TimedOut { get; init; }

    /// <summary>Gets the termination outcome when execution ended through timeout or cancellation handling.</summary>
    public TerminationResult? TerminationResult { get; init; }
}
```

Use this exact shape for `CleaningPreflightPlan`, `PreflightPluginRow` etc. — `public sealed record` with `{ get; init; }` properties; mark required fields with `required` (C# 13).

---

### `CleaningPreflight.cs` (service implementation, request-response)

**Analog:** `AutoQAC/Services/Cleaning/CleaningService.cs:14-21` (primary-constructor sealed class)

**Primary-constructor sealed-class pattern** (`CleaningService.cs:14-21`):
```csharp
public sealed class CleaningService(
    IGameDetectionService gameDetection,
    IStateService stateService,
    ILoggingService logger,
    IProcessExecutionService processService,
    IXEditCommandBuilder commandBuilder)
    : ICleaningService
{
    public async Task<CleaningResult> CleanPluginAsync(
        PluginInfo plugin,
        CancellationToken ct = default,
        Action<System.Diagnostics.Process>? onProcessStarted = null)
    {
```

**ConfigureAwait + try/catch + structured result pattern** (`CleaningService.cs:84-130`):
```csharp
var result = await processService.ExecuteAsync(command, timeout, ct, onProcessStarted).ConfigureAwait(false);

// ...

}
catch (OperationCanceledException)
{
    return new CleaningResult
    {
        Success = false,
        Status = CleaningStatus.Skipped,
        Message = "Operation cancelled.",
        Duration = sw.Elapsed
    };
}
catch (Exception ex)
{
    logger.Error(ex, "Error cleaning {Plugin}", plugin.FileName);
    return new CleaningResult
    {
        Success = false,
        Status = CleaningStatus.Failed,
        Message = $"Cleaning failed for {plugin.FileName}. See logs for technical details.",
        Duration = sw.Elapsed
    };
}
```

**Source code to migrate** (per RESEARCH section-by-section map):
- `CleaningOrchestrator.cs:76-93` (orphan cleanup, config flush, validation)
- `CleaningOrchestrator.cs:95-135` (game/variant detection)
- `CleaningOrchestrator.cs:137-159` (load user config, MO2 path validation)
- `CleaningOrchestrator.cs:161-188` (skip-list filter)
- `CleaningOrchestrator.cs:190-222` (file validation)
- `CleaningOrchestrator.cs:1010-1064` (dry-run reason mapping switch — D-15 vocabulary)
- `CleaningOrchestrator.cs:1077-1104` (`ValidateConfigurationAsync`, `RequiresFileLoadOrder` — keep `RequiresFileLoadOrder` as private helper inside `CleaningPreflight`; do NOT consolidate per Out-of-Scope Trap #1)

---

### `IBackupSessionCoordinator.cs` + `BackupSessionCoordinator.cs` (service, event-driven)

**Analog (interface):** `AutoQAC/Services/Backup/IBackupService.cs:12-108`

**Multi-method interface with `IProgress<T>` + `CancellationToken` pattern** (`IBackupService.cs:28-32`):
```csharp
/// <summary>
/// Copies the plugin file to the session directory with structured status, byte progress, and cancellation support.
/// </summary>
Task<BackupCreateResult> BackupPluginAsync(
    PluginInfo plugin,
    string sessionDir,
    IProgress<BackupCopyProgress>? progress = null,
    CancellationToken ct = default);
```

**Analog (implementation):** `AutoQAC/Services/Backup/BackupService.cs:32-46` (traditional ctor with optional dep) AND `CleaningOrchestrator.cs:679-808` (the actual code being lifted)

**Traditional-ctor + ILogger field pattern** (`BackupService.cs:23-46`):
```csharp
public sealed class BackupService : IBackupService
{
    private readonly IBackupFileCopier _fileCopier;
    private readonly IBackupSessionDeleter _sessionDeleter;
    private readonly ILoggingService _logger;

    /// <summary>
    /// Creates the backup service with injectable file-copy behavior for async backup and restore operations.
    /// </summary>
    /// <param name="fileCopier">Copy service used for cancellable backup and atomic restore work.</param>
    /// <param name="logger">Logger for technical diagnostics that should not be exposed in user-facing result rows.</param>
    public BackupService(IBackupFileCopier fileCopier, ILoggingService logger, IBackupSessionDeleter? sessionDeleter = null)
    {
        _fileCopier = fileCopier;
        _sessionDeleter = sessionDeleter ?? new DirectoryBackupSessionDeleter();
        _logger = logger;
    }
```

**Why traditional ctor here:** The coordinator owns mutable state (`_backupOperationCts`, `_backupOperationLock`) that must be initialized in a constructor body, not a primary-ctor parameter list. Use traditional ctor (NOT primary) when the class declares `private readonly object _xLock = new();` or holds disposable state.

**State-owning lock pattern with CTS lifecycle** (`CleaningOrchestrator.cs:778-808`):
```csharp
/// <summary>
/// Creates the current non-xEdit operation CTS linked to the overall cleaning session.
/// </summary>
private CancellationTokenSource CreateBackupOperationCts(CancellationToken sessionToken)
{
    var cts = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
    lock (_backupOperationLock)
    {
        _backupOperationCts = cts;
    }

    return cts;
}

/// <summary>
/// Clears and disposes the active non-xEdit operation CTS when the owning operation exits.
/// </summary>
private void ClearBackupOperationCts(CancellationTokenSource? expected = null)
{
    CancellationTokenSource? toDispose = null;
    lock (_backupOperationLock)
    {
        if (expected == null || ReferenceEquals(_backupOperationCts, expected))
        {
            toDispose = _backupOperationCts;
            _backupOperationCts = null;
        }
    }

    toDispose?.Dispose();
}
```

This entire helper PAIR moves into `BackupSessionCoordinator` verbatim. Keep XML docs intact (no comment stripping per CLAUDE.md).

**Progress + try/finally + state-publication pattern** (`CleaningOrchestrator.cs:704-724`):
```csharp
try
{
    stateService.SetBackupOperation(new BackupOperationState
    {
        Kind = BackupOperationKind.Backup,
        Label = $"Backing up: {plugin.FileName}",
        FileName = plugin.FileName,
        FilesCompleted = 0,
        TotalFiles = 1,
        IsActive = true,
        CanCancel = true
    });

    return await backupService.BackupPluginAsync(plugin, sessionDir, progress, operationCts.Token)
        .ConfigureAwait(false);
}
finally
{
    stateService.ClearBackupOperation();
    ClearBackupOperationCts(operationCts);
}
```

This pattern (set → await with `ConfigureAwait(false)` → finally clear) preserves Phase 7 lock D-Phase07 and must move verbatim.

**ObjectDisposedException-tolerant cancellation pattern** (`CleaningOrchestrator.cs:667-674`):
```csharp
try
{
    cts?.Cancel();
}
catch (ObjectDisposedException)
{
    // The operation completed between reading the CTS and attempting cancellation.
}
```

The empty-comment-explains-why pattern is mandatory per CLAUDE.md. Preserve verbatim.

---

### `IPluginCleaningRunner.cs` + `PluginCleaningRunner.cs` (service, request-response retry FSM)

**Analog:** `AutoQAC/Services/Cleaning/CleaningService.cs` (entire file)

**Source code to migrate** (per RESEARCH section-by-section map):
- `CleaningOrchestrator.cs:386-440` — retry loop, log-offset capture, `cleaningService.CleanPluginAsync` call, `onTimeout` callback, attach/detach delegates

**Retry-loop FSM pattern** (`CleaningOrchestrator.cs:393-440`):
```csharp
do
{
    attemptNumber++;

    if (attemptNumber > 1)
    {
        logger.Information("Retry attempt {Attempt} for plugin: {Plugin}",
            attemptNumber, plugin.FileName);
    }

    // Capture log offsets before each xEdit launch (per D-03: per-plugin, inside retry loop)
    var mainLogPath = logFileService.GetLogFilePath(xEditDir, gameType);
    var exceptionLogPath = logFileService.GetExceptionLogFilePath(xEditDir, gameType);
    mainLogOffset = logFileService.CaptureOffset(mainLogPath);
    exceptionLogOffset = logFileService.CaptureOffset(exceptionLogPath);

    result = await cleaningService.CleanPluginAsync(
        plugin,
        cts.Token,
        onProcessStarted: proc =>
        {
            lock (_processLock)
            {
                _currentProcess = proc;
            }

            StartHangMonitoring(proc);
        }).ConfigureAwait(false);

    // If timed out and callback provided, ask user if they want to retry
    if (result.TimedOut && onTimeout != null && attemptNumber < maxRetryAttempts)
    {
        var shouldRetry = await onTimeout(plugin.FileName, timeoutSeconds, attemptNumber)
            .ConfigureAwait(false);

        if (!shouldRetry)
        {
            logger.Information("User chose not to retry plugin: {Plugin}", plugin.FileName);
            break;
        }

        logger.Information("User chose to retry plugin: {Plugin}", plugin.FileName);
    }
    else
    {
        break; // No timeout or no callback or max attempts reached
    }
} while (true);
```

In the runner, the lambda body that captures `_currentProcess` becomes a call to the `attachProcess` delegate parameter; the `_hangMonitorSubscription?.Dispose()` block (lines 442-454) becomes the `detachProcess` delegate. Keep all comments (e.g. `// Capture log offsets before each xEdit launch (per D-03: per-plugin, inside retry loop)`).

**Per-attempt-comment pattern is critical:** The `// per D-03` comment documents the protected ordering (log-offset BEFORE launch). Move verbatim.

---

### `IPluginResultFinalizer.cs` + `PluginResultFinalizer.cs` (service, transform)

**Analog:** `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:456-513` (the actual code being lifted)

**Source code to migrate** verbatim — log read with offset, completion-line detection, exception-log surfacing, result construction.

**Critical guard-pattern to preserve** (`CleaningOrchestrator.cs:461`):
```csharp
// Guard: only read logs if process was not killed/cancelled (per D-04)
if (!MayProcessStillBeRunning(_lastTerminationResult) && !_isStopRequested && result.Status != CleaningStatus.Skipped)
{
    var logResult = await logFileService.ReadLogContentAsync(
        xEditDir, gameType, mainLogOffset, exceptionLogOffset, cts.Token).ConfigureAwait(false);
    // ...
}
else if (_isStopRequested || MayProcessStillBeRunning(_lastTerminationResult))
{
    logParseWarning = "xEdit was terminated -- no log available";
}
```

In the finalizer this becomes:
```csharp
if (!terminationContext.ProcessMayStillBeRunning && !terminationContext.StopWasRequested && result.Status != CleaningStatus.Skipped)
```
The `// per D-04` comment is a Phase 5 lock reference and MUST be preserved.

**Result-record construction pattern** (`CleaningOrchestrator.cs:502-513`):
```csharp
var pluginCleaningResult = new PluginCleaningResult
{
    PluginName = plugin.FileName,
    Status = finalStatus,
    Success = result.Success,
    Message = result.TimedOut && attemptNumber >= maxRetryAttempts
        ? $"Cleaning timed out after {attemptNumber} attempts."
        : result.Message,
    Duration = pluginStopwatch.Elapsed,
    Statistics = logStats,
    LogParseWarning = logParseWarning
};
```

`Statistics = logStats` (nullable) and `LogParseWarning = logParseWarning` (nullable) preserve the no-log-after-unsafe-termination outcome.

---

### `ICleaningTerminationCoordinator.cs` + `CleaningTerminationCoordinator.cs` (service, event-driven + state-owning) — HIGHEST RISK

**Analog (interface shape):** `AutoQAC/Services/Process/IProcessExecutionService.cs` (TerminateProcessAsync + structured results) + `AutoQAC/Services/Monitoring/IHangDetectionService.cs` (`IObservable<bool>` exposure)

**`IObservable<bool>` exposure pattern** (`ICleaningOrchestrator.cs:72-78`):
```csharp
/// <summary>
/// Observable that emits hang detection state changes during cleaning.
/// Emits true when xEdit appears hung (near-zero CPU for 60+ seconds),
/// false when xEdit resumes activity. Emits false on plugin change and
/// cleaning end to auto-dismiss any visible warning.
/// </summary>
IObservable<bool> HangDetected { get; }
```

The coordinator's `HangDetected` interface property uses this exact docstring; the facade then exposes `HangDetected => _terminationCoordinator.HangDetected`.

**Source code to migrate** (verbatim, no behavior changes per D-09/D-10):
- `CleaningOrchestrator.cs:36-46` — fields: `_hangDetected` Subject, `_processLock`, `_isStopRequested`, `_currentProcess`, `_lastTerminationResult`, `_hangMonitorSubscription`
- `CleaningOrchestrator.cs:813-889` — `StopCleaningAsync` (graceful path, self-PID check at 858-862)
- `CleaningOrchestrator.cs:891-951` — `ForceStopCleaningAsync` (force-kill path, self-PID check at 925-929)
- `CleaningOrchestrator.cs:953-958` — `MarkLeftRunningByUser`
- `CleaningOrchestrator.cs:960-964` — `ToStopCleaningResult`, `MayProcessStillBeRunning` (private statics)
- `CleaningOrchestrator.cs:1106-1116` — `StartHangMonitoring`

**Field-declaration + lock-init pattern** (`CleaningOrchestrator.cs:36-49`):
```csharp
private readonly Subject<bool> _hangDetected = new();
private readonly object _ctsLock = new();
private readonly object _processLock = new();
private readonly object _backupOperationLock = new();

private CancellationTokenSource? _cleaningCts;
private CancellationTokenSource? _backupOperationCts;
private volatile bool _isStopRequested;
private System.Diagnostics.Process? _currentProcess;
private TerminationResult? _lastTerminationResult;
private IDisposable? _hangMonitorSubscription;

public TerminationResult? LastTerminationResult => _lastTerminationResult;
public IObservable<bool> HangDetected => _hangDetected.AsObservable();
```

`volatile` on `_isStopRequested` is intentional (ANALYSIS: "Read-only access; no lock needed (volatile)"). Move verbatim. **Note:** Coordinator should use `System.Threading.Lock` (the C# 13 type) only if the existing code does — current code uses `object _processLock = new();`, so preserve that until a follow-up phase migrates it.

**Self-PID refusal pattern** (`CleaningOrchestrator.cs:858-862` and 925-929) — Phase 5 INV-5.4 / INV-5.5 lock:
```csharp
if (proc.Id == Environment.ProcessId)
{
    logger.Error(null, "[Termination] Refusing to terminate the AutoQAC process during stop request");
    return new StopCleaningResult(null, MayStillBeRunning: false);
}
```

**`CancellationToken.None` for termination** (`CleaningOrchestrator.cs:866`, 933) — Phase 5 lock per RESEARCH "Threading / Cancellation":
```csharp
var result = await processService.TerminateProcessAsync(proc, forceKill: false, ct: CancellationToken.None)
    .ConfigureAwait(false);
```
Termination cleanup must NOT be abandoned by caller cancellation. Preserve verbatim.

**Hang monitor subscribe + dispose pattern** (`CleaningOrchestrator.cs:1106-1116`):
```csharp
private void StartHangMonitoring(System.Diagnostics.Process process)
{
    // Ensure only one active monitor subscription per xEdit process lifecycle.
    _hangMonitorSubscription?.Dispose();
    _hangMonitorSubscription = hangDetection.MonitorProcess(process)
        .Subscribe(
            isHung => _hangDetected.OnNext(isHung),
            _ => { }, // Error: monitor completed unexpectedly
            () => { } // Completed: process exited
        );
}
```

The two empty-lambda-with-comment cases satisfy the CLAUDE.md "empty catch/finally needs WHY" rule (extended here to empty-handlers). Preserve.

**Disposable + Subject lifecycle** (`CleaningOrchestrator.cs:1140-1150`):
```csharp
public void Dispose()
{
    _hangMonitorSubscription?.Dispose();
    _hangDetected.Dispose();

    lock (_ctsLock)
    {
        _cleaningCts?.Dispose();
        _cleaningCts = null;
    }
}
```

The coordinator's `Dispose` owns `_hangDetected` and `_hangMonitorSubscription`; the facade keeps `_cleaningCts` (session CTS) per RESEARCH Threading section.

**Subject<bool>-as-IObservable pattern** uses `System.Reactive.Subjects.Subject<bool>`. Add `using System.Reactive.Linq;` and `using System.Reactive.Subjects;` to the coordinator file (already present in `CleaningOrchestrator.cs:6-7`). Per CLAUDE.md, services may keep `System.Reactive`; only ViewModels are banned from it.

---

### `CleaningOrchestrator.cs` (modified facade)

**Analog:** itself + `AutoQAC/Services/Backup/BackupService.cs` (constructor with composed services)

**Reduced facade ctor pattern (target shape, derived from existing primary-ctor):**
```csharp
public sealed class CleaningOrchestrator(
    ICleaningPreflight preflight,
    IBackupSessionCoordinator backupCoordinator,
    IPluginCleaningRunner runner,
    IPluginResultFinalizer finalizer,
    ICleaningTerminationCoordinator terminationCoordinator,
    IStateService stateService,
    IConfigurationService configService,
    ILoggingService logger)
    : ICleaningOrchestrator, IDisposable
```

**Property-forwarding pattern** for `LastTerminationResult` and `HangDetected`:
```csharp
public TerminationResult? LastTerminationResult => terminationCoordinator.LastTerminationResult;
public IObservable<bool> HangDetected => terminationCoordinator.HangDetected;
```

**Session-CTS retention pattern** — facade keeps `_ctsLock`, `_cleaningCts` (per RESEARCH Threading table). Lift the existing `CleaningOrchestrator.cs:227-237` block intact.

**`StopCleaningAsync` thin-wrapper pattern** (target):
```csharp
public async Task<StopCleaningResult> StopCleaningAsync()
{
    // Cancel session CTS first so the foreach loop exits
    CancellationTokenSource? cts;
    lock (_ctsLock) { cts = _cleaningCts; }
    try { if (cts is not null) _ = cts.CancelAsync(); }
    catch (ObjectDisposedException) { /* Already disposed -- fine */ }

    return await terminationCoordinator.StopAsync().ConfigureAwait(false);
}
```

The empty-catch comment (`// Already disposed -- fine`) is the existing Phase-5 pattern at `CleaningOrchestrator.cs:909-912`. Preserve.

---

### `ServiceCollectionExtensions.cs` (modified)

**Analog:** `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs:41-64` — `AddBusinessLogic` block

**DI registration pattern** (`ServiceCollectionExtensions.cs:54-62`):
```csharp
services.AddSingleton<IXEditCommandBuilder, XEditCommandBuilder>();
services.AddSingleton<IXEditOutputParser, XEditOutputParser>();
services.AddSingleton<IXEditLogFileService, XEditLogFileService>();
services.AddSingleton<ICleaningService, CleaningService>();
services.AddSingleton<IBackupFileCopier, BackupFileCopier>();
services.AddSingleton<IBackupSessionDeleter, DirectoryBackupSessionDeleter>();
services.AddSingleton<IBackupService, BackupService>();
services.AddSingleton<IHangDetectionService, HangDetectionService>();
services.AddSingleton<ICleaningOrchestrator, CleaningOrchestrator>();
```

**Add (insert before `ICleaningOrchestrator` line):**
```csharp
services.AddSingleton<ICleaningPreflight, CleaningPreflight>();
services.AddSingleton<IBackupSessionCoordinator, BackupSessionCoordinator>();
services.AddSingleton<IPluginCleaningRunner, PluginCleaningRunner>();
services.AddSingleton<IPluginResultFinalizer, PluginResultFinalizer>();
services.AddSingleton<ICleaningTerminationCoordinator, CleaningTerminationCoordinator>();
```

All `Singleton` per RESEARCH Assumption A4 (matches existing pattern; collaborator state is session-scoped within a singleton, reset between sessions).

---

### Test Files

#### `CleaningPreflightTests.cs` (analog: `CleaningServiceTests.cs:13-28`)

**Pure-mock test class pattern** (`CleaningServiceTests.cs:13-28`):
```csharp
public sealed class CleaningServiceTests
{
    private readonly IGameDetectionService _mockGameDetection;
    private readonly IStateService _mockState;
    private readonly ILoggingService _mockLogger;
    private readonly IProcessExecutionService _mockProcess;
    private readonly IXEditCommandBuilder _mockCommandBuilder;

    public CleaningServiceTests()
    {
        _mockGameDetection = Substitute.For<IGameDetectionService>();
        _mockState = Substitute.For<IStateService>();
        _mockLogger = Substitute.For<ILoggingService>();
        _mockProcess = Substitute.For<IProcessExecutionService>();
        _mockCommandBuilder = Substitute.For<IXEditCommandBuilder>();
    }
```

**No `IDisposable`, no temp directories** — pure NSubstitute. Apply this exact shape to `CleaningPreflightTests`.

**AAA + FluentAssertions pattern** (`CleaningServiceTests.cs:30-76`):
```csharp
[Fact]
public async Task CleanPluginAsync_ShouldCallProcessAndReturnSuccess()
{
    // Arrange
    var service = new CleaningService(...);
    var plugin = new PluginInfo { FileName = "Mod.esp", ... };
    _mockState.CurrentState.Returns(appState);
    _mockCommandBuilder.BuildCommand(plugin, GameType.SkyrimSe).Returns(startInfo);
    _mockProcess.ExecuteAsync(startInfo, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
        .Returns(processResult);

    // Act
    var result = await service.CleanPluginAsync(plugin);

    // Assert
    result.Success.Should().BeTrue();
    result.Status.Should().Be(CleaningStatus.Cleaned);
    await _mockProcess.Received(1).ExecuteAsync(startInfo, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
}
```

#### `BackupSessionCoordinatorTests.cs` (analogs: `CleaningOrchestratorTests.cs` lines 1446–1629 + `BackupServiceTests.cs:18-39` for temp directory)

**Existing backup-failure test shape** (`CleaningOrchestratorTests.cs` setup at lines 22-85): all 11 dependencies as NSubstitute mocks, default no-op setups for `GetSkipListAsync`, `LoadUserConfigAsync`, log-file offset APIs.

**Coordinator test shape:** Reduced to `IBackupService`, `IStateService`, `ILoggingService` (3 deps). May need a temp directory if testing `BeginSessionAsync` → `CreateSessionDirectory` interaction; in that case use:
```csharp
public sealed class BackupSessionCoordinatorTests : IDisposable
{
    private readonly string _testRoot;
    public BackupSessionCoordinatorTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"autoqac_bsc_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
    }
    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            try { Directory.Delete(_testRoot, recursive: true); }
            catch { /* Best-effort cleanup */ }
        }
    }
```
This is `BackupServiceTests.cs:18-39` verbatim. Preserve the empty-catch-comment.

**Optional-parameter assertion pattern** (per CLAUDE.md global rule: "match optional parameters explicitly"):
```csharp
await _mockBackupService.Received(1).BackupPluginAsync(
    plugin,
    sessionDir,
    Arg.Any<IProgress<BackupCopyProgress>>(),
    Arg.Any<CancellationToken>());
```
Always pass ALL optional arguments to `Received(...)` calls or NSubstitute analyzers will warn.

#### `PluginCleaningRunnerTests.cs` (analog: `CleaningServiceTests.cs`)

Same pure-mock pattern. Mock `ICleaningService`, `IXEditLogFileService`, `ILoggingService`. Use `Action<Process>` and `Action` test doubles for the `attachProcess` / `detachProcess` delegates — record invocation count on plain local variables (no NSubstitute needed for delegates):
```csharp
var attachCount = 0;
var detachCount = 0;
Action<System.Diagnostics.Process> attach = _ => attachCount++;
Action detach = () => detachCount++;
```

#### `PluginResultFinalizerTests.cs` (analog: `XEditOutputParserTests.cs` for pure-function tests; `CleaningOrchestratorTests.cs:1974` for log-skip characterization)

Pure-function test class. No `IDisposable`. Mock `IXEditLogFileService`, `IXEditOutputParser`, `ILoggingService`. Test `TerminationFinalizeContext` permutations: `(false, false)` reads logs; `(true, false)` skips logs; `(false, true)` skips logs.

#### `CleaningTerminationCoordinatorTests.cs` (analogs: `CleaningOrchestratorTests.cs` stop tests at lines 617, 679, 743, 804, 870 + `HangDetectionServiceTests.cs` for real-process pattern)

**Real-process pattern** (`HangDetectionServiceTests.cs:39-55`):
```csharp
[Fact]
public async Task MonitorProcess_ShouldCompleteForAlreadyExitedProcess()
{
    // Arrange - start and immediately wait for a short-lived process to exit
    var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c echo done",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        }
    };
    process.Start();
    await process.WaitForExitAsync();
```

Existing stop-cleaning tests in `CleaningOrchestratorTests.cs` already use this pattern with `AutoQAC.TestProcessHelper` for long-running helpers — REUSE that helper for coordinator tests.

**Self-PID test pattern** (`CleaningOrchestratorTests.cs:870` and surrounding): wire the coordinator with a fake `Process.GetCurrentProcess()` capture and assert `StopAsync` returns `(null, false)` without calling `processService.TerminateProcessAsync`.

## Shared Patterns

### Pattern A: Async Service Method Signature

**Source:** `AutoQAC/Services/Cleaning/CleaningService.cs:22-25`, `AutoQAC/Services/Backup/IBackupService.cs:28-32`

**Apply to:** Every new async method on every collaborator interface.

```csharp
Task<TResult> XxxAsync(
    PluginInfo plugin,            // required positional args first
    string sessionDir,
    IProgress<TProgress>? progress = null,   // optional with `?` and default
    CancellationToken ct = default);          // ALWAYS last; ALWAYS `= default`
```

The implementation must `await ... .ConfigureAwait(false)` on every await, propagate `ct` to inner calls, and check `ct.ThrowIfCancellationRequested()` at iteration boundaries (per CLAUDE.md project conventions).

### Pattern B: Sealed-Record Result Types with `required init`

**Source:** `AutoQAC/Services/Process/IProcessExecutionService.cs:46-53`, `AutoQAC/Models/PluginCleaningResult.cs` (existing models in `AutoQAC/Models/`)

**Apply to:** All output records returned by collaborators (`CleaningPreflightPlan`, `PluginBackupOutcome`, `PluginRunnerOutput`, `TerminationFinalizeContext`).

```csharp
public sealed record CleaningPreflightPlan
{
    public required GameType DetectedGameType { get; init; }
    public required IReadOnlyList<PreflightPluginRow> PluginRows { get; init; }
    public required bool IsMo2ModeActive { get; init; }
    // ...
}
```

### Pattern C: Empty-Catch / Empty-Handler WHY-Comment

**Source:** `CleaningOrchestrator.cs:909-912`, `1106-1116`, `673`

**Apply to:** Every empty `catch` / `finally` / no-op lambda in the new collaborators.

```csharp
catch (ObjectDisposedException)
{
    // Already disposed -- fine
}
```

```csharp
.Subscribe(
    isHung => _hangDetected.OnNext(isHung),
    _ => { }, // Error: monitor completed unexpectedly
    () => { } // Completed: process exited
);
```

This is the CLAUDE.md global rule ("Empty `catch`/`finally` blocks should always have a one-line comment explaining why they're empty").

### Pattern D: XML Doc Comments on Every Public/Internal Method

**Source:** `AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs:43-87`, `AutoQAC/Services/Backup/IBackupService.cs:88-101`

**Apply to:** Every member on every new interface AND every method on every new implementation that is public/internal or substantially rewritten (per CLAUDE.md).

```csharp
/// <summary>
/// One-sentence purpose. Include threading/lifetime contract if non-obvious.
/// </summary>
/// <param name="plugin">Plugin to back up.</param>
/// <param name="sessionDir">Absolute path to the session directory created by BeginSessionAsync.</param>
/// <param name="onBackupFailure">Optional callback invoked when backup fails; null means no prompt.</param>
/// <param name="sessionToken">Session-scoped cancellation token.</param>
/// <returns>Structured outcome describing whether the plugin was backed up, skipped, or aborted.</returns>
/// <exception cref="InvalidOperationException">Thrown when [domain condition] — verbatim from current orchestrator messages per D-12.</exception>
Task<PluginBackupOutcome> RunPluginBackupAsync(...);
```

### Pattern E: Lock-Protected State Field Pattern

**Source:** `CleaningOrchestrator.cs:36-46`, `778-808`

**Apply to:** `BackupSessionCoordinator` (`_backupOperationLock` + `_backupOperationCts`) and `CleaningTerminationCoordinator` (`_processLock` + `_currentProcess`, `_lastTerminationResult`, `_hangMonitorSubscription`).

```csharp
private readonly object _xLock = new();   // existing convention; do NOT migrate to System.Threading.Lock in this phase
private TFoo? _foo;

// Reader
TFoo? snapshot;
lock (_xLock) { snapshot = _foo; }

// Writer
lock (_xLock) { _foo = newValue; }
```

Read-modify-write inside lock; subsequent observable emissions (`Subject.OnNext`) OUTSIDE the lock to avoid subscriber deadlocks (mirrors `StateService` pattern).

### Pattern F: NSubstitute Test Setup with Default Mocks

**Source:** `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs:35-85`

**Apply to:** All new collaborator test ctors.

```csharp
public CleaningPreflightTests()
{
    _configServiceMock = Substitute.For<IConfigurationService>();
    // ...

    // Default mock setup for GetSkipListAsync to return empty list instead of null
    _configServiceMock.GetSkipListAsync(
            Arg.Any<GameType>(),
            Arg.Any<GameVariant>(),
            Arg.Any<CancellationToken>())
        .Returns(new List<string>());

    _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
        .Returns(new UserConfiguration());

    _sut = new CleaningPreflight(...);
}
```

**Match optional parameters explicitly in `Received(...)` assertions** (CLAUDE.md global rule).

## Files With No Analog

None — all 11 production files and 5 test files have direct analogs in the existing codebase. The decomposition is a redistribution of code that already follows established conventions; no novel patterns are required.

## Metadata

**Analog search scope:**
- `AutoQAC/Services/Cleaning/` (11 files: orchestrator, service, command builder, output parser, log file service, interface)
- `AutoQAC/Services/Backup/` (file copier, session deleter, backup service, interface)
- `AutoQAC/Services/Process/` (execution service, interface, PID store)
- `AutoQAC/Services/Monitoring/` (hang detection service, interface)
- `AutoQAC/Services/State/` (state service)
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC.Tests/Services/` (full directory listing of 26 test files)

**Files scanned (full or partial read):** 14
**Pattern extraction date:** 2026-04-29

## PATTERN MAPPING COMPLETE

**Phase:** 08 - Cleaning Orchestrator Decomposition
**Files classified:** 16 (11 production + 5 test)
**Analogs found:** 16 / 16

### Coverage
- Files with exact analog: 16
- Files with role-match analog: 0
- Files with no analog: 0

### Key Patterns Identified
1. **Primary-constructor sealed class pattern** for stateless services (`CleaningService.cs:14-21`); **traditional ctor with optional dep + ILogger field** for state-owning services (`BackupService.cs:23-46`). The phase 8 collaborators split: preflight/runner/finalizer use primary; backup-coordinator/termination-coordinator use traditional.
2. **`CancellationToken ct = default` last-parameter convention with `ConfigureAwait(false)` on every service-layer await**, identical across `CleaningService`, `BackupService`, `ProcessExecutionService`. Termination methods deliberately use `CancellationToken.None` for the inner `TerminateProcessAsync` call (Phase 5 lock).
3. **Sealed-record output types with `required` `init` properties** for structured outcomes (`ProcessResult`, `BackupCreateResult`, model records in `AutoQAC/Models/`). Phase 8's new records (`CleaningPreflightPlan`, `PluginBackupOutcome`, `PluginRunnerOutput`, `TerminationFinalizeContext`) follow this exactly.
4. **Lock-protected mutable state with reader/writer extraction outside the lock**, plus `Subject<bool>.OnNext` outside any lock to avoid subscriber deadlocks. Both `BackupSessionCoordinator` (operation CTS) and `CleaningTerminationCoordinator` (process handle, hang subscription, last-termination-result) inherit this pattern verbatim from `CleaningOrchestrator.cs:36-46, 778-808`.
5. **`IObservable<bool>` exposure via `Subject<bool>.AsObservable()`** for hang state, mirroring `IHangDetectionService.MonitorProcess` and `ICleaningOrchestrator.HangDetected`. Termination coordinator owns the Subject; facade forwards via expression-bodied property.
6. **Empty-catch / empty-handler WHY-comments** are non-negotiable per CLAUDE.md and already present in current orchestrator (`// Already disposed -- fine` at line 911, `// Error: monitor completed unexpectedly` at line 1113). All extractions preserve verbatim.
7. **NSubstitute pure-mock test class with default-Returns setup** in ctor (`CleaningOrchestratorTests.cs:35-85`); `IDisposable` + temp-directory pattern (`BackupServiceTests.cs:18-39`) only when filesystem is involved; real-process pattern via `cmd.exe /c echo done` (`HangDetectionServiceTests.cs:42-55`) for short-lived termination tests; reuse `AutoQAC.TestProcessHelper` from Phase 5 for long-running termination tests.
8. **DI registration as `Singleton`** in `ServiceCollectionExtensions.AddBusinessLogic` block (line 41-64), inserted before `ICleaningOrchestrator` registration so the orchestrator's transitive deps resolve.

### File Created
`J:/AutoQACSharp/.planning/phases/08-cleaning-orchestrator-decomposition/08-PATTERNS.md`

### Ready for Planning
Pattern mapping complete. Planner can now reference analog file paths + line ranges in each Phase 8 task's `<read_first>` block. Every new file has a concrete code excerpt the planner can quote for ctor shape, async method signature, sealed-record output, lock-state ownership, XML doc style, and test-substitute setup. The five collaborators sit cleanly inside existing conventions — no new patterns required.
