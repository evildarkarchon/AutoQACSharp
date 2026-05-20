# Phase 8: Cleaning Orchestrator Decomposition - Research

**Researched:** 2026-04-29
**Domain:** Behavior-preserving service refactor (C# 13 / .NET 10 / Avalonia desktop)
**Confidence:** HIGH

## Summary

Phase 8 is a behavior-preserving refactor of `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` (1151 physical lines). The locked decisions in `08-CONTEXT.md` (D-01 through D-20) prescribe four collaborators behind a stable `ICleaningOrchestrator` facade: a shared preflight/selection plan (D-13–D-16), a backup-session lifecycle collaborator (D-06), a per-plugin runner + finalizer split (D-07), and a termination coordinator (D-08). The phase is constrained by upstream phase locks: Phase 5 stop/force-stop semantics, Phase 6 command-build/launch contract, and Phase 7 backup/retention/cancellation semantics.

The current monolithic orchestrator concentrates 14+ distinct responsibilities into one class with mutable shared fields (`_isStopRequested`, `_currentProcess`, `_lastTerminationResult`, `_hangMonitorSubscription`, `_cleaningCts`, `_backupOperationCts`) protected by three locks (`_ctsLock`, `_processLock`, `_backupOperationLock`). The decomposition must preserve every invariant from these locks, but can move ownership of related state into the new collaborators.

Existing tests in `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs` (2083 lines, 41 tests) already cover most of the D-18 outcome list — characterization gaps are narrower than CONCERNS.md suggested. The primary missing characterization coverage is around retention warning vs canceled session classification and dry-run/StartCleaning preflight equivalence. TDD applies cleanly to all four collaborators because each has a clear async I/O contract; the orchestrator facade integration is execute-only glue.

**Primary recommendation:** Use a five-wave plan that follows D-05 strictly: lock outcomes before each extraction. Order extractions from least-coupled to most-coupled state — preflight first (pure function over inputs), backup-session next (already isolated to `BackupPluginAsync`/`CleanupOldSessionsAsync` helpers), termination third (highest risk: touches Phase 5 locks), per-plugin runner+finalizer fourth, facade integration last.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Extraction Boundaries**
- **D-01:** Use focused collaborators around the Phase 8 success criteria, not a minimal helper-only cleanup and not a pipeline/stage rewrite.
- **D-02:** `CleaningOrchestrator` remains the public sequential shell/facade. It owns high-level session ordering and delegates detailed policies to collaborators.
- **D-03:** Keep the public `ICleaningOrchestrator` API stable for ViewModels: start-cleaning overloads, `RunDryRunAsync`, stop/force-stop, backup-operation cancellation, `LastTerminationResult`, and `HangDetected` remain available through the same interface.
- **D-04:** Place new extraction interfaces and implementations under `AutoQAC/Services/Cleaning` unless research finds a compelling narrower reason to use an existing owner folder. Avoid broad cross-folder churn.
- **D-05:** Migration order is characterize-then-extract: lock current outcomes with tests before moving each seam, then extract one collaborator at a time.

**Collaborator Responsibilities**
- **D-06:** Add a backup-session lifecycle collaborator for cleaning-session backup policy: create session, run per-plugin backup, shape backup-failure choices into cleaning outcomes, write metadata, and coordinate retention cleanup/progress/cancellation.
- **D-07:** Split backup-independent per-plugin work into a runner plus finalizer. Runner handles attempt/retry, xEdit launch, log-offset capture. Finalizer builds `PluginCleaningResult` from process results, log content, parser output, and termination state.
- **D-08:** Add a dedicated termination coordinator behind the orchestrator facade. Owns current-process tracking, stop/force-stop escalation state, `LastTerminationResult`, hang-monitor lifecycle, and `MayProcessStillBeRunning` decisions while preserving Phase 5 semantics.

**Behavior Preservation**
- **D-09:** Zero intentional user-visible behavior changes. Preserve successful, skipped, failed, stopped, force-stopped, left-running, already-clean, backup-canceled, backup-failed-choice, retention-warning, retention-canceled, and dry-run behavior.
- **D-10:** Prior phase locks are non-negotiable: sequential xEdit cleaning, two-stage stop/force-stop behavior, no log parsing after unsafe termination, exact launch argv intent, concise user-facing launch/error messages, backup cancellation semantics, restore/retention safety, and MO2 backup skip behavior.
- **D-11:** If decomposition reveals an edge-case bug not required to satisfy `REF-01`, downstream agents must stop and ask before fixing it. Do not silently expand into behavior repair.
- **D-12:** User-facing messages and result meanings stay stable. Internal log line placement, collaborator names in logs, and implementation-detail diagnostics may change if tests do not assert exact log text.

**Dry-Run and Preflight Sharing**
- **D-13:** One preview-safe preflight/selection plan consumed by both `StartCleaningAsync` and `RunDryRunAsync`. Handles config flush, game detection, variant detection, skip lists, exclusions, MO2 validation, file validation without starting cleaning, creating cleaning CTS/processes, or running backups.
- **D-14:** State mutation is mode-specific. The shared plan returns detected game/variant and selected-plugin facts; real cleaning may apply detected game state as current behavior does, while dry-run remains non-mutating.
- **D-15:** Plan includes full clean/skip reason rows: not selected, in skip list, file not found, unreadable, zero-byte, malformed entry, invalid extension, ready for cleaning.
- **D-16:** Plan carries explicit MO2 policy facts: MO2 mode active, backup skipped, file validation skipped, launch mode. Later collaborators should not rediscover these rules independently.

**Test Proof Expectations**
- **D-17:** Minimum proof is characterization plus seams.
- **D-18:** Prioritize characterization for: successful, skipped, failed, stopped/left-running, already-clean, backup-canceled/failed choices, retention warning/canceled.
- **D-19:** Source-level/structural guards used sparingly for hard-to-observe invariants (no parallel xEdit, stable public surface).
- **D-20:** Test contracts by inputs/outputs/state effects/preserved behavior. Assert call order only where order is the protected behavior (backup before xEdit, log offset before xEdit launch, sequential plugin processing).

### Claude's Discretion

- Exact collaborator type names, interface names, internal preflight model shape, test method names, and plan grouping are left to research and planning.
- Downstream agents may choose the smallest internal API that satisfies the locked boundaries above while preserving comments that explain safety, threading, cancellation, and termination constraints.

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| REF-01 | Maintainer can change cleaning preflight, backup, execution, result finalization, or termination logic without editing one monolithic cleaning orchestrator. | Each of the four locked collaborators (D-06, D-07, D-08, D-13–D-16) maps to one editable seam. The Section-by-Section Map below isolates the line ranges that move into each collaborator. The Wave Structure separates extractions so a maintainer changing termination logic touches only the termination coordinator and its tests. |
</phase_requirements>

## Project Constraints (from CLAUDE.md)

These directives override default agent behavior and the planner must check compliance:

- **No comment stripping.** Existing comments that explain safety/threading/cancellation/termination contracts must move into the new collaborators verbatim. Do NOT remove a comment because the code "looks self-explanatory."
- **XML doc comments required** on every new public/internal method and on substantially rewritten private methods. Use `///` form. Cover purpose, non-obvious parameters, return semantics, exceptions, and threading/lifetime contracts.
- **Sequential cleaning is non-negotiable.** No `Task.WhenAll`, `Parallel.ForEach`, `Parallel.ForEachAsync` over plugins or process launches.
- **MVVM boundaries.** New collaborators do NOT take Avalonia or ViewModel dependencies. ViewModels keep talking to `ICleaningOrchestrator` only.
- **No Bash usage on Windows; PowerShell only.** Test/build commands flow through `dotnet` CLI which works in either shell, but the agent should use the PowerShell tool for any shell work per user's CLAUDE.md.
- **Empty `catch`/`finally` blocks need a one-line WHY comment.** Already present in current code (e.g. `// Already disposed -- fine` at line 911); preserve and add equivalents in extracted code.
- **`ConfigureAwait(false)` on every service-layer await.** Current orchestrator follows this; new collaborators must too.
- **Match existing conventions:** `public sealed class` with primary constructors when possible, `_camelCase` private fields, `Async` suffix on async methods, `CancellationToken ct = default` as last parameter.
- **Mutagen/ is read-only.** Phase 8 should not need to touch it; flag if any planner instinct says otherwise.
- **No Avalonia.Headless test project.** Place collaborator tests under `AutoQAC.Tests/Services/Cleaning/` (new subfolder is acceptable per STRUCTURE.md guidance).
- **Tests use xUnit + FluentAssertions + NSubstitute.** Match optional parameters explicitly in substitute setups (`Arg.Any<CancellationToken>()` etc).

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Cleaning workflow ordering | Service (Cleaning Orchestrator facade) | — | Stays a service; ViewModels never sequence plugins. |
| Preflight (config flush, game detect, skip-list, MO2/file validation) | Service (new `ICleaningPreflight`) | Service | Pure orchestration of existing services; no UI. |
| Backup session lifecycle | Service (new `IBackupSessionCoordinator`) | Service | Wraps `IBackupService` boundary; produces structured outcomes. |
| Per-plugin xEdit execution | Service (new `IPluginCleaningRunner`) | — | Owns retry loop and log offset capture; calls `ICleaningService` + `IXEditLogFileService`. |
| Per-plugin result finalization | Service (new `IPluginResultFinalizer`) | — | Pure function over runner output, log content, termination state. |
| Termination coordination (stop/force-stop/hang) | Service (new `ICleaningTerminationCoordinator`) | — | Owns process handle, hang subscription, `_lastTerminationResult`. |
| Public ViewModel-facing facade | Service (`ICleaningOrchestrator` unchanged) | — | D-03 lock: VM contract is stable. |
| Progress / state publication | Service (existing `IStateService`) | — | Continues to be the single state hub. |
| Process launch / single-slot guarantee | Service (existing `IProcessExecutionService`) | — | Sequentiality is enforced here too; do not bypass. |

**Why this matters:** The decomposition stays entirely within the service tier. No capability migrates into ViewModels or DI infrastructure. The risk vector for tier misassignment is low; the risk vector is *intra-tier coupling* — specifically, accidentally giving the per-plugin runner termination state ownership, which would smear Phase 5 lock semantics across two collaborators.

## Section-by-Section Map of `CleaningOrchestrator.cs`

This is the seam map planners need. Line numbers below reference `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` as of the research date.

| Lines | Block | Concern | Migrates Into |
|-------|-------|---------|---------------|
| 22-49 | Class declaration, constructor (11 deps), fields, public properties (`LastTerminationResult`, `HangDetected`) | Facade composition | Stays in `CleaningOrchestrator` (D-02). `_lastTerminationResult` and `_hangDetected` move into termination coordinator; facade exposes them via property/event forwarding. |
| 51-75 | `StartCleaningAsync` overloads + locals + stop-flag reset | Public surface | Stays. Facade method orchestrates collaborator calls. |
| 76-93 | Orphan cleanup, config flush, configuration validation | Preflight | `ICleaningPreflight.PrepareAsync` |
| 95-135 | Game detection (executable + load order fallback), variant detection | Preflight | `ICleaningPreflight.PrepareAsync` |
| 137-159 | Load user config, MO2 path validation | Preflight | `ICleaningPreflight.PrepareAsync` |
| 161-188 | Skip-list filtering and excluded plugins | Preflight | `ICleaningPreflight.PrepareAsync` |
| 190-222 | File-existence validation (non-MO2) + reason mapping | Preflight | `ICleaningPreflight.PrepareAsync` (returns reasons matching D-15 dry-run vocabulary) |
| 224-225 | `stateService.StartCleaning(pluginsToClean)` | State init | Stays in facade (mode-specific per D-14) |
| 227-237 | Cleaning CTS creation + timeout fetch | Session state | Stays in facade |
| 239-263 | Backup session directory creation (with MO2 skip + missing-rooted-path handling) | Backup-session | `IBackupSessionCoordinator.BeginSessionAsync` |
| 265-273 | Foreach plugin loop + cancellation check | Sequencing | Stays in facade (D-20: order is the protected behavior) |
| 275-279 | `CurrentPlugin` state update | State | Stays in facade |
| 281-383 | Per-plugin backup + `BackupFailureChoice` switch (Skip / Abort / Continue) | Backup-session | `IBackupSessionCoordinator.RunPluginBackupAsync` returns `BackupOutcome`; facade handles abort by breaking loop and finalizing, OR coordinator returns an enum the facade dispatches on. **Recommendation:** keep abort decision in facade because it short-circuits session finalization. |
| 386-440 | Per-plugin retry loop, log-offset capture, `cleaningService.CleanPluginAsync`, timeout retry callback | Runner | `IPluginCleaningRunner.RunAsync` |
| 412-420 | `onProcessStarted` lambda: `_currentProcess` capture + `StartHangMonitoring` | Termination | Termination coordinator owns `_currentProcess` and hang monitor; runner gets a delegate from termination coordinator (e.g. `Action<Process> attachProcess`). |
| 442-454 | `pluginStopwatch.Stop()`, hang subscription disposal, `_currentProcess = null` | Termination | Termination coordinator's `DetachProcess()` |
| 456-499 | Log content read (offset-based), exception log, completion-line detection (already-clean), exception-content surfacing | Finalizer | `IPluginResultFinalizer.FinalizeAsync` (takes runner output + termination state + log offsets) |
| 461 | `MayProcessStillBeRunning(_lastTerminationResult)` guard | Termination | Termination coordinator exposes `bool MayProcessStillBeRunning { get; }`; finalizer reads it. |
| 501-524 | `PluginCleaningResult` construction + `AddDetailedCleaningResult` + log line | Finalizer | `IPluginResultFinalizer.FinalizeAsync` returns `PluginCleaningResult`; facade pushes to state service. |
| 526-546 | End-of-session: write metadata + retention cleanup | Backup-session | `IBackupSessionCoordinator.FinalizeSessionAsync` returns `BackupRetentionCleanupResult?`. |
| 548-560 | Build `CleaningSessionResult`, finish cleaning with results, log summary | Facade | Stays. |
| 562-602 | `OperationCanceledException` catch with partial metadata write | Facade + backup-session | Facade calls `IBackupSessionCoordinator.WritePartialMetadataOnCancelAsync` |
| 603-621 | General exception catch | Facade | Stays. |
| 622-648 | `finally`: reset stop flags, hang subscription, process ref, dispose CTS, clear backup-op CTS | Termination + facade | Termination coordinator's `ResetSessionState()`; facade keeps `_cleaningCts` lifecycle (the SESSION CTS, distinct from process). |
| 651-677 | `CancelBackupOperationAsync` (rejects when xEdit active) | Backup-session | `IBackupSessionCoordinator.CancelActiveOperationAsync` (knows whether xEdit is active via termination coordinator query, or facade gates the call). |
| 679-725 | `BackupPluginAsync` private helper (CTS + progress + state pub/clear) | Backup-session | `IBackupSessionCoordinator.RunPluginBackupAsync` |
| 727-776 | `CleanupOldSessionsAsync` private helper | Backup-session | `IBackupSessionCoordinator.RunRetentionCleanupAsync` |
| 778-808 | `CreateBackupOperationCts` / `ClearBackupOperationCts` | Backup-session | Coordinator's internal helpers (state moves out of facade entirely). |
| 810-811 | `GetBackupFailureReasonText` | Backup-session | Coordinator helper. |
| 813-889 | `StopCleaningAsync`: second-click escalation, CTS cancel, graceful terminate, refuses self-pid | Termination | `ICleaningTerminationCoordinator.StopAsync` (keeps `_isStopRequested`, `_lastTerminationResult`). Facade method becomes a thin pass-through that also cancels the session CTS. |
| 891-951 | `ForceStopCleaningAsync` | Termination | `ICleaningTerminationCoordinator.ForceStopAsync` |
| 953-958 | `MarkLeftRunningByUser` | Termination | `ICleaningTerminationCoordinator.MarkLeftRunningByUser` |
| 960-964 | `ToStopCleaningResult`, `MayProcessStillBeRunning` | Termination | Termination coordinator's helpers (already pure). |
| 966-1075 | `RunDryRunAsync` | Preflight + facade | Calls `ICleaningPreflight.PrepareAsync(mode: DryRun)` and projects rows into `DryRunResult`. The reason-mapping switch (1052-1060) is part of D-15. |
| 1077-1095 | `ValidateConfigurationAsync` private helper | Preflight | Coordinator helper. |
| 1098-1104 | `RequiresFileLoadOrder` static helper | Preflight | Coordinator helper (or shared policy class — but per D-04 keep narrow; defer to a unified game policy until Phase 9). |
| 1106-1116 | `StartHangMonitoring` private helper | Termination | Termination coordinator. |
| 1118-1138 | `LogSessionSummary` | Facade | Stays. |
| 1140-1151 | `Dispose` | Facade + termination | Facade disposes session CTS; termination coordinator disposes hang subject + monitor. |

## Concrete Extraction Proposal

### Collaborator 1: `ICleaningPreflight` (D-13–D-16)

**Recommended location:** `AutoQAC/Services/Cleaning/ICleaningPreflight.cs` and `CleaningPreflight.cs`

**Recommended interface:**

```csharp
public interface ICleaningPreflight
{
    /// <summary>
    /// Runs the shared preflight pipeline: flush pending config, validate
    /// environment, detect game and variant, apply skip lists/exclusions,
    /// validate MO2 path (when active), and validate plugin files (when not
    /// in MO2 mode). Does NOT mutate state, start processes, create cleaning
    /// CTS, or run backups. State mutation is the caller's job (per D-14).
    /// </summary>
    Task<CleaningPreflightPlan> PrepareAsync(CancellationToken ct);
}

public sealed record CleaningPreflightPlan
{
    public required GameType DetectedGameType { get; init; }
    public required GameVariant DetectedGameVariant { get; init; }
    public required IReadOnlyList<PreflightPluginRow> PluginRows { get; init; }
    public required bool IsMo2ModeActive { get; init; }
    public required bool BackupSkippedByPolicy { get; init; }      // D-16
    public required bool FileValidationSkippedByPolicy { get; init; } // D-16
    public required string LaunchModeLabel { get; init; }           // D-16: "MO2" or "direct xEdit"
    public required int CleaningTimeoutSeconds { get; init; }
    public required bool BackupEnabled { get; init; }
    public required int BackupMaxSessions { get; init; }
}

public sealed record PreflightPluginRow(
    PluginInfo Plugin,
    PreflightDecision Decision,
    PreflightSkipReason? SkipReason);

public enum PreflightDecision { Clean, Skip }

public enum PreflightSkipReason
{
    NotSelected,
    InSkipList,
    FileNotFound,
    Unreadable,
    ZeroByte,
    MalformedEntry,
    InvalidExtension
}
```

| Field | Value |
|-------|-------|
| **Inputs** | `IStateService` (read-only `CurrentState`), `IConfigurationService`, `IGameDetectionService`, `IPluginValidationService`, `ICleaningService` (for `ValidateEnvironmentAsync`), `ILoggingService`, `CancellationToken` |
| **Outputs** | `CleaningPreflightPlan` |
| **Owned state** | None (stateless) |
| **Throws** | `InvalidOperationException` for missing config, unknown game type, MO2 path missing — matches current orchestrator messages verbatim (D-12 keeps user text stable). |
| **Existing code migrated** | Lines 76-222, 1010-1064 (dry-run reason mapping), 1077-1104 |

**Mode adaptation:** A single `PrepareAsync` method works for both modes because the plan returns the same facts. The CALLER chooses what to do:
- `StartCleaningAsync` calls `stateService.UpdateState(s => s with { CurrentGameType = plan.DetectedGameType })` after preflight (D-14 mutation).
- `RunDryRunAsync` does not touch state; it projects `PreflightPluginRow` to `DryRunResult` using the existing reason-string mapping.

### Collaborator 2: `IBackupSessionCoordinator` (D-06)

**Recommended location:** `AutoQAC/Services/Cleaning/IBackupSessionCoordinator.cs` and `BackupSessionCoordinator.cs`

> **Naming note:** `BackupSessionCoordinator` lives in `Services/Cleaning/` per D-04 because it is cleaning-session-scoped policy that *uses* `IBackupService`, not a generalized backup primitive. Putting it under `Services/Backup/` would cross-cut into Phase 7 territory.

**Recommended interface:**

```csharp
public interface IBackupSessionCoordinator
{
    /// <summary>
    /// Creates the backup session directory if backup is enabled and not in MO2 mode.
    /// Returns null when backup is skipped (MO2 mode, no rooted plugin path, or backup disabled).
    /// </summary>
    Task<string?> BeginSessionAsync(CleaningPreflightPlan plan, CancellationToken sessionToken);

    /// <summary>
    /// Runs per-plugin backup with progress/cancellation. Returns a structured outcome
    /// the facade can dispatch on. NEVER throws on expected backup failures.
    /// </summary>
    Task<PluginBackupOutcome> RunPluginBackupAsync(
        PluginInfo plugin,
        string sessionDir,
        BackupFailureCallback? onBackupFailure,
        CancellationToken sessionToken);

    /// <summary>
    /// Writes session metadata (full or partial) and runs retention cleanup.
    /// Returns retention result so the facade can attach it to CleaningSessionResult.
    /// </summary>
    Task<BackupRetentionCleanupResult?> FinalizeSessionAsync(
        string sessionDir,
        GameType gameType,
        IReadOnlyList<BackupPluginEntry> entries,
        int maxSessions,
        CancellationToken sessionToken);

    /// <summary>
    /// Best-effort partial metadata write on cancellation/abort. Swallows write failures
    /// to a Warning log. Used from the facade's OperationCanceledException handler.
    /// </summary>
    Task WritePartialMetadataAsync(
        string sessionDir,
        GameType gameType,
        IReadOnlyList<BackupPluginEntry> entries,
        CancellationToken ct);

    /// <summary>
    /// Cancels the active non-xEdit backup or retention file operation. No-op when no
    /// backup operation is active. Caller is responsible for asserting xEdit is not active.
    /// </summary>
    Task CancelActiveOperationAsync();
}

public sealed record PluginBackupOutcome
{
    public required PluginBackupOutcomeKind Kind { get; init; }
    public BackupPluginEntry? Entry { get; init; }                  // when Succeeded
    public PluginCleaningResult? SkippedResult { get; init; }       // when Canceled or UserSkipped
    public string? FailureReasonText { get; init; }                 // for ContinueWithoutBackup logging
}

public enum PluginBackupOutcomeKind
{
    Succeeded,            // entry added; proceed to xEdit
    Canceled,             // user canceled this plugin's backup; skip xEdit; SkippedResult provided
    UserSkipped,          // BackupFailureChoice.SkipPlugin; skip xEdit; SkippedResult provided
    AbortSession,         // BackupFailureChoice.AbortSession; facade must finalize
    ContinueWithoutBackup // BackupFailureChoice.ContinueWithoutBackup; proceed to xEdit, no entry
}
```

| Field | Value |
|-------|-------|
| **Inputs** | `IBackupService`, `IStateService` (for `SetBackupOperation`/`ClearBackupOperation`), `ILoggingService`, `IConfigurationService` (for `LoadUserConfigAsync` to read `Backup.Enabled` — though the preflight already gives this; consider passing through `CleaningPreflightPlan`) |
| **Outputs** | Per method above |
| **Owned state** | `_backupOperationCts` (linked to session token), `_backupOperationLock` |
| **Throws** | Only on truly unexpected exceptions — expected backup failures return `PluginBackupOutcome` |
| **Existing code migrated** | Lines 239-263, 281-383, 526-546, 564-588, 651-677, 679-808 |

**State publication contract (preserved):** `state.SetBackupOperation` is set inside the coordinator before each backup/retention call and cleared in the `finally`. Phase 7 lock D-Phase07 ("represent backup and retention cleanup as separate AppState.BackupOperation state") is preserved by keeping these calls on the same boundaries as today.

**AbortSession contract:** The facade receives `AbortSession` outcome and is responsible for calling `WritePartialMetadataAsync`, building `CleaningSessionResult`, and calling `stateService.FinishCleaningWithResults` — same as today (lines 333-362). Coordinator does NOT finalize the session itself because it does not own session-level result construction.

### Collaborator 3: `IPluginCleaningRunner` (D-07, runner half)

**Recommended location:** `AutoQAC/Services/Cleaning/IPluginCleaningRunner.cs` and `PluginCleaningRunner.cs`

```csharp
public interface IPluginCleaningRunner
{
    /// <summary>
    /// Runs xEdit for one plugin with attempt/retry, log-offset capture before each attempt,
    /// and process-attach delegated to the termination coordinator. Returns the runner output
    /// for the finalizer to interpret. Per D-20: log offset capture happens inside the retry loop,
    /// before each xEdit launch — this ordering is protected behavior.
    /// </summary>
    Task<PluginRunnerOutput> RunAsync(
        PluginInfo plugin,
        GameType gameType,
        string xEditDir,
        TimeoutRetryCallback? onTimeout,
        int timeoutSeconds,
        Action<System.Diagnostics.Process> attachProcess,
        Action detachProcess,
        CancellationToken ct);
}

public sealed record PluginRunnerOutput
{
    public required CleaningResult LastAttemptResult { get; init; }
    public required int AttemptCount { get; init; }
    public required long MainLogOffset { get; init; }
    public required long ExceptionLogOffset { get; init; }
    public required TimeSpan Duration { get; init; }
    public required bool ReachedMaxRetryAttempts { get; init; }
}
```

| Field | Value |
|-------|-------|
| **Inputs** | `ICleaningService`, `IXEditLogFileService`, `ILoggingService` |
| **Outputs** | `PluginRunnerOutput` |
| **Owned state** | None (stateless per call) |
| **Existing code migrated** | Lines 386-440 |

**Why two delegates instead of injecting `ICleaningTerminationCoordinator` directly:** Keeps the runner unaware of termination policy. The termination coordinator's `attachProcess(proc)` captures the process and starts hang monitoring; `detachProcess()` clears the process reference and stops hang monitoring. This makes the runner trivially testable without a real termination coordinator and means termination semantics changes do not edit runner code. Alternative: inject `ICleaningTerminationCoordinator` — simpler dependency graph but creates a back-edge that violates D-08 ownership semantics.

### Collaborator 4: `IPluginResultFinalizer` (D-07, finalizer half)

**Recommended location:** `AutoQAC/Services/Cleaning/IPluginResultFinalizer.cs` and `PluginResultFinalizer.cs`

```csharp
public interface IPluginResultFinalizer
{
    /// <summary>
    /// Builds the per-plugin PluginCleaningResult from the runner output, log file content,
    /// and the termination coordinator's view of whether the process may still be running.
    /// Honors D-04 from prior phases: skip log read when termination is unsafe or stop was requested.
    /// </summary>
    Task<PluginCleaningResult> FinalizeAsync(
        PluginInfo plugin,
        GameType gameType,
        string xEditDir,
        PluginRunnerOutput runnerOutput,
        TerminationFinalizeContext terminationContext,
        CancellationToken ct);
}

public sealed record TerminationFinalizeContext(
    bool ProcessMayStillBeRunning,
    bool StopWasRequested);
```

| Field | Value |
|-------|-------|
| **Inputs** | `IXEditLogFileService`, `IXEditOutputParser`, `ILoggingService` |
| **Outputs** | `PluginCleaningResult` |
| **Owned state** | None (stateless per call) |
| **Existing code migrated** | Lines 456-513 |

**No-log-after-unsafe-termination invariant (Phase 5 D-09 lock):** The finalizer receives `TerminationFinalizeContext` so the guard at line 461 (`!MayProcessStillBeRunning(_lastTerminationResult) && !_isStopRequested && result.Status != CleaningStatus.Skipped`) moves into `FinalizeAsync` verbatim. The termination coordinator computes `ProcessMayStillBeRunning` at the moment the runner returns; this snapshot is then passed to the finalizer.

### Collaborator 5: `ICleaningTerminationCoordinator` (D-08)

**Recommended location:** `AutoQAC/Services/Cleaning/ICleaningTerminationCoordinator.cs` and `CleaningTerminationCoordinator.cs`

```csharp
public interface ICleaningTerminationCoordinator
{
    /// <summary>
    /// Attaches the active xEdit process and starts hang monitoring. Called from inside
    /// IPluginCleaningRunner via the attachProcess delegate.
    /// </summary>
    void AttachProcess(System.Diagnostics.Process process);

    /// <summary>
    /// Detaches the active xEdit process and stops hang monitoring. Idempotent.
    /// </summary>
    void DetachProcess();

    /// <summary>
    /// Two-stage stop. First call: sets stop-requested flag, requests graceful termination.
    /// Returns GracePeriodExpired when graceful close did not exit within 2.5s; ViewModel
    /// then prompts the user. Second call during grace: escalates to ForceStopAsync.
    /// (Phase 5 D-01..D-05 locks.)
    /// </summary>
    Task<StopCleaningResult> StopAsync();

    /// <summary>
    /// Immediate Kill(true) of the process tree. (Phase 5 D-04: explicit user escalation.)
    /// </summary>
    Task<StopCleaningResult> ForceStopAsync();

    /// <summary>
    /// User declined the force-terminate prompt; mark xEdit as left running by user choice.
    /// (Phase 5 D-03 lock.)
    /// </summary>
    StopCleaningResult MarkLeftRunningByUser();

    /// <summary>
    /// Resets per-session termination state (stop flag, last result). Called by the facade
    /// at session start and in the finally block.
    /// </summary>
    void ResetForNewSession();

    /// <summary>True when a stop was requested in the current session.</summary>
    bool IsStopRequested { get; }

    /// <summary>Last termination result for this session, or null if no termination occurred.</summary>
    TerminationResult? LastTerminationResult { get; }

    /// <summary>True when xEdit is currently attached (used by backup-cancel rejection).</summary>
    bool HasActiveProcess { get; }

    /// <summary>True when log read should be skipped because the process may still be running.</summary>
    bool ProcessMayStillBeRunning { get; }

    /// <summary>Hang detection observable forwarded to the facade.</summary>
    IObservable<bool> HangDetected { get; }
}
```

| Field | Value |
|-------|-------|
| **Inputs** | `IProcessExecutionService`, `IHangDetectionService`, `IStateService` (for `SetTerminating`), `ILoggingService` |
| **Outputs** | Per method above |
| **Owned state** | `_isStopRequested` (volatile bool), `_lastTerminationResult`, `_currentProcess`, `_processLock`, `_hangMonitorSubscription`, `_hangDetected` Subject |
| **Throws** | Never on expected disposal/already-exited cases (matches current behavior in lines 880-885, 942-947) |
| **Existing code migrated** | Lines 813-964, 1106-1116, plus `_lastTerminationResult` and `_isStopRequested` fields |

**Critical invariant kept inside coordinator:** Self-PID refusal (lines 858-862, 925-929) — must remain inside `StopAsync`/`ForceStopAsync`. This is a defensive safety check Phase 5 explicitly preserves.

**CTS ownership:** The session CTS (`_cleaningCts`) stays in the **facade**, not the termination coordinator. Stop calls cancel the session CTS by invoking a delegate the facade gives the coordinator at construction OR the facade handles CTS cancellation in its own `StopAsync` wrapper before delegating to the coordinator. **Recommendation:** facade wraps. The facade's `StopCleaningAsync` becomes:
```
1. Cancel _cleaningCts
2. await _terminationCoordinator.StopAsync()
3. Return result
```
This keeps session-scope CTS lifetime aligned with the facade and prevents accidental coordinator-owned CTS leaks.

## Migration Ordering (D-05)

The recommended sequence minimizes risk by extracting collaborators in order of state-coupling complexity (least → most):

| Order | Extraction | Why First/Next | Pre-Extraction Tests to Add |
|-------|-----------|----------------|-----------------------------|
| 1 | `ICleaningPreflight` | Pure function over inputs; no shared mutable state with other concerns. Both StartCleaning and RunDryRun call it, so test gains are immediate. Closes the dry-run/real-run drift risk early. | Dual-mode preflight equivalence test (same skip-list filtering, same MO2 validation, same file-existence reasons). Dry-run reason-string regression test for all 7 reasons in D-15. |
| 2 | `IBackupSessionCoordinator` | Already isolated to `BackupPluginAsync`/`CleanupOldSessionsAsync` private helpers (lines 679-808) plus the inline switch at 281-383. Self-contained CTS and lock are easy to move atomically. | Backup outcome characterization: Succeeded, Canceled, SkipPlugin, AbortSession, ContinueWithoutBackup. Most exist (lines 1446, 1539, 1579, 1629); add a coordinator-level test that exercises FinalizeSessionAsync's retention return value for warning vs canceled. |
| 3 | `ICleaningTerminationCoordinator` | Highest risk because it touches Phase 5 locks. Extract before runner so the runner can take delegates without coupling to the coordinator's internals. | Verify all existing stop tests still pass (lines 617, 679, 743, 804, 870). Add a test that asserts `_isStopRequested` is reset for a new session even after a previous session had stop requested (currently implicit; lock it explicitly). |
| 4 | `IPluginCleaningRunner` + `IPluginResultFinalizer` | Bottom of the dependency graph; depend on already-extracted termination coordinator (via delegates) and preflight (via `gameType`/`xEditDir`/`timeoutSeconds` inputs). | Runner: retry-on-timeout, retry-callback-false-stops-retry, max-attempts-reached. Finalizer: log-skipped-when-stop-requested, log-skipped-when-may-still-be-running, already-clean detection, exception-content surfacing. |
| 5 | Facade integration + final regression sweep | All four collaborators wired through DI. Facade becomes thin: preflight → state mutation → backup begin → loop (backup, run, finalize) → backup finalize → session result. Run full `dotnet test AutoQACSharp.slnx` to verify behavior preservation. | Source-level guard test that `CleaningOrchestrator.cs` does NOT contain `Parallel`, `Task.WhenAll(`, or `Task.Run(` over plugin loop (D-19 hard-to-observe invariant). |

## Stable Invariants Checklist

Each invariant must hold after the refactor and is mapped to a proving test or assertion. The planner uses this list to scope verification.

### Phase 5 Stop / Force-Stop Invariants

| ID | Invariant | Proving Test |
|----|-----------|--------------|
| INV-5.1 | First StopCleaningAsync call attempts graceful termination, returns `GracePeriodExpired` when 2.5s elapses without exit | `StopCleaningAsync_ShouldTerminateActiveProcess_Gracefully_AndStoreGracePeriodExpiredResult` (line 679) |
| INV-5.2 | Second StopCleaningAsync during grace escalates to ForceStop without prompt | `StopCleaningAsync_WhenCalledTwiceDuringActiveProcess_ShouldEscalateToForceStop` (line 804) |
| INV-5.3 | ForceStopCleaningAsync calls TerminateProcessAsync with `forceKill: true` | `ForceStopCleaningAsync_ShouldTerminateActiveProcess_WithForceKillTrue` (line 743) |
| INV-5.4 | Self-PID refusal: orchestrator never terminates the AutoQAC process | `StopCleaningAsync_ShouldNotTerminateCurrentProcess_WhenTrackedProcessIsSelf` (line 870) |
| INV-5.5 | `MarkLeftRunningByUser` returns `LeftRunningByUser` and `MayStillBeRunning=true` | NEW TEST NEEDED — characterize before extraction |
| INV-5.6 | No log read after unsafe termination (`MayProcessStillBeRunning` true OR `_isStopRequested` true) | `StartCleaningAsync_ShouldSkipLogRead_WhenProcessWasCancelled` (line 1974) |
| INV-5.7 | `LastTerminationResult` is reset to null at session start and end | NEW TEST NEEDED — characterize before extraction |

### Phase 6 Command/Launch Invariants (Should Survive Untouched)

| ID | Invariant | Proving Location |
|----|-----------|------------------|
| INV-6.1 | Command construction goes through `IXEditCommandBuilder` only; orchestrator never builds argv directly | Phase 8 should not touch `XEditCommandBuilder.cs` or `CleaningService.CleanPluginAsync`; runner calls `cleaningService.CleanPluginAsync` exactly as today |
| INV-6.2 | Concise launch-failure messaging on null command-build (no path/argv exposure) | `CleaningServiceTests` already covers; runner passes through |
| INV-6.3 | MO2 mode failure on missing/empty `Mo2ExecutablePath` | `StartCleaningAsync_MO2Mode_ShouldThrow_WhenMO2BinaryPathEmpty` (line 1737), `StartCleaningAsync_MO2Mode_ShouldThrow_WhenMO2BinaryNotFound` (line 1781) — preflight inherits these |

### Phase 7 Backup / Retention Invariants

| ID | Invariant | Proving Test |
|----|-----------|--------------|
| INV-7.1 | Per-plugin backup runs immediately before that plugin's xEdit launch | `StartCleaningAsync_ShouldProcessPluginsSequentially_NeverInParallel` (line 534) covers ordering; add an explicit sequence assertion |
| INV-7.2 | MO2 mode skips backup; `BackupPluginAsync` never called | `Mo2Mode_DoesNotCallBackupPluginAsync` (line 1694) |
| INV-7.3 | Canceled backup → plugin skipped; xEdit not launched | `BackupCancellation_DoesNotLaunchXEdit_ForCanceledPlugin` (line 1446) |
| INV-7.4 | `BackupFailureChoice.SkipPlugin` → skipped result published, plugin in `SkippedPlugins` | `BackupFailureChoice_SkipPlugin_PublishesSkippedResultAndCompletesSessionAccounting` (line 1579) |
| INV-7.5 | `BackupFailureChoice.AbortSession` → finalize partial metadata, session canceled, return early | `BackupFailureChoice_AbortSession_FinalizesCanceledSessionWithPreviousResults` (line 1629) |
| INV-7.6 | `BackupFailureChoice.ContinueWithoutBackup` → proceed with xEdit, no entry added | `BackupFailure_StillUsesBackupFailureCallback_ForNonCanceledFailures` (line 1539) — verify ContinueWithoutBackup branch specifically |
| INV-7.7 | `CancelBackupOperationAsync` is no-op when xEdit is active (does not stop xEdit) | `CancelBackupOperation_DuringXEdit_DoesNotStopOrKillXEdit` (line 1500) |
| INV-7.8 | Retention warning vs canceled classification arrives in `CleaningSessionResult.BackupCleanup` | NEW TEST NEEDED — characterization gap (D-18) |
| INV-7.9 | `state.SetBackupOperation` set/cleared on backup boundary; never bleeds into xEdit launch state | NEW TEST — assert `ClearBackupOperation` called before `_processSlots` is taken |

### Phase 8 New Invariants (D-09 through D-12)

| ID | Invariant | Proving Mechanism |
|----|-----------|-------------------|
| INV-8.1 | `ICleaningOrchestrator` public surface unchanged | Source-level test: enumerate `ICleaningOrchestrator` members and snapshot signature list |
| INV-8.2 | No parallel xEdit launches anywhere in cleaning code | `CleaningOrchestrator_Source_DoesNotParallelizePluginCleaning` (line 2023) — extend to scan all four new collaborators |
| INV-8.3 | Preflight (real run) and dry-run produce equivalent skip lists for the same inputs | NEW TEST: shared-preflight equivalence (D-13) |
| INV-8.4 | Preflight does NOT mutate state | NEW TEST: assert `stateService.UpdateState` is never called from `ICleaningPreflight.PrepareAsync` |
| INV-8.5 | Log offset capture occurs inside the retry loop, before each xEdit launch (D-20) | NEW TEST or preserved `XEditLogFileServiceTests` coverage on offset-per-attempt |

## Threading / Cancellation Analysis

The current orchestrator manages five distinct cancellation/synchronization primitives. Decomposition must redistribute them without widening lock scope or inverting acquisition order.

| Primitive | Current Owner | After Refactor | Risk |
|-----------|---------------|----------------|------|
| `_cleaningCts` (session CTS, linked to caller `ct`) | Orchestrator (`_ctsLock`) | **Facade** — keep at orchestrator level | None; this is session-scoped lifetime aligned with `StartCleaningAsync` |
| `_backupOperationCts` (linked to session CTS, lifetime: one backup or retention call) | Orchestrator (`_backupOperationLock`) | **Backup-session coordinator** | Coordinator must validate `HasActiveProcess == false` before using. Recommend: facade gates `CancelBackupOperationAsync` by checking termination coordinator's `HasActiveProcess` first, then delegates. |
| `_processLock` (guards `_currentProcess`) | Orchestrator | **Termination coordinator** | None; ownership stays atomic |
| `_isStopRequested` (volatile bool) | Orchestrator | **Termination coordinator** | Termination coordinator must expose `IsStopRequested` for finalizer's log-skip guard (Phase 5 INV-5.6). Read-only access; no lock needed (volatile). |
| `_hangMonitorSubscription` + `_hangDetected` Subject | Orchestrator | **Termination coordinator** | Subject lifetime moves with coordinator. Facade exposes `HangDetected` by forwarding `coordinator.HangDetected` (or just delegates the property getter). |

### Lock Acquisition Order

Current order (no deadlocks observed today): `_ctsLock` → `_processLock` → `_backupOperationLock` (always acquired separately, never nested).

After refactor:
- Facade holds `_ctsLock` (session CTS)
- Termination coordinator holds `_processLock`
- Backup-session coordinator holds `_backupOperationLock`

These never nest because each lock guards a single field assignment. **Risk:** if a planner instinct says "let me make the termination coordinator query the backup coordinator before stopping," resist it — that introduces nested cross-collaborator lock acquisition. Use *event-based* or *callback* communication if cross-collaborator coordination becomes necessary, not direct synchronous calls under a lock.

### UI Thread Safety

No existing code blocks the UI thread; service-layer code uses `ConfigureAwait(false)` consistently. Risk during refactor: a planner accidentally adds a `.Result` or `.Wait()` while wiring DI or building helpers. **Mitigation:** keep CLAUDE.md guideline visible — async-all-the-way. The facade's `StopCleaningAsync` is callable from a `[RelayCommand]` and the chain is awaited end-to-end.

### CancellationToken Propagation

Every collaborator method takes a `CancellationToken`. The session token (`cts.Token` derived from caller `ct`) flows from the facade into:
- `IBackupSessionCoordinator.RunPluginBackupAsync(..., sessionToken)` — coordinator builds linked operation CTS internally
- `IPluginCleaningRunner.RunAsync(..., ct: cts.Token)` — runner passes through to `cleaningService.CleanPluginAsync`
- `IPluginResultFinalizer.FinalizeAsync(..., ct: cts.Token)` — passes through to `logFileService.ReadLogContentAsync`

The termination coordinator's `StopAsync`/`ForceStopAsync` use `CancellationToken.None` for the actual `processService.TerminateProcessAsync` call (Phase 5 lock: termination cleanup must not be abandoned by caller cancellation; lines 866 and 933 today). **This is intentional and must be preserved verbatim.**

## Test Seam Strategy

| Collaborator | TDD-Eligible | Reason | RED Test Names (proposed) |
|--------------|-------------|--------|---------------------------|
| `ICleaningPreflight` | YES | Pure async function; deterministic; no process work | 1. `PrepareAsync_RealMode_AndDryRunMode_ProduceSamePluginRows_ForIdenticalInputs`<br>2. `PrepareAsync_MO2Mode_ReportsBackupSkippedAndFileValidationSkippedPolicyFacts`<br>3. `PrepareAsync_NonMo2Mode_MapsFileValidationFailureToCorrectSkipReason` (parametrized over the 7 reasons in D-15) |
| `IBackupSessionCoordinator` | YES | Mockable `IBackupService`; outcome enum makes assertions clean | 1. `RunPluginBackupAsync_OnCancellation_ReturnsCanceledOutcomeWithSkippedResult`<br>2. `RunPluginBackupAsync_OnFailureWithSkipPluginChoice_ReturnsUserSkippedOutcome`<br>3. `FinalizeSessionAsync_RetentionWarning_ReturnsRetentionResult_WithoutThrowing` |
| `IPluginCleaningRunner` | YES | Mockable `ICleaningService` and `IXEditLogFileService`; retry loop is an FSM | 1. `RunAsync_TimedOut_AndCallbackReturnsTrue_RetriesAndCapturesNewLogOffsetsBeforeEachAttempt`<br>2. `RunAsync_TimedOut_AndCallbackReturnsFalse_StopsAfterFirstAttempt`<br>3. `RunAsync_AttachAndDetachDelegates_AreCalledExactlyOncePerAttempt_AroundCleanPluginAsync` |
| `IPluginResultFinalizer` | YES | Pure function over inputs; trivial to characterize | 1. `FinalizeAsync_WhenProcessMayStillBeRunning_DoesNotReadLogs_AndAttachesTerminationWarningMessage`<br>2. `FinalizeAsync_CompletionLineWithZeroStats_ProducesAlreadyCleanStatus`<br>3. `FinalizeAsync_ExceptionLogContent_ProducesFailedStatus_WithExceptionContentInWarning` |
| `ICleaningTerminationCoordinator` | YES (with care) | Some tests need real `Process` instances (already done in existing orchestrator tests via helper) | 1. `StopAsync_FirstCall_AttemptsGraceful_AndStoresGracePeriodExpiredResult`<br>2. `StopAsync_SecondCallDuringGrace_EscalatesToForceStop_WithoutPrompt`<br>3. `ResetForNewSession_ClearsStopFlagAndLastTerminationResult` |
| `CleaningOrchestrator` (facade) | NO (execute-only) | Glue: DI wiring, session CTS lifecycle, sequencing collaborators. Existing 41 tests serve as characterization regression suite. | N/A — extend existing orchestrator tests as needed |

### Existing Coverage vs D-18 Outcome List

| D-18 Outcome | Existing Test Coverage | Gap |
|--------------|------------------------|-----|
| Successful plugin | `StartCleaningAsync_ShouldParseStatsFromLogFile_WhenCleaningSucceeds` (1830) | Covered |
| Skipped plugin | `StartCleaningAsync_ShouldExcludeSkippedPlugins_WhenDisableSkipListsDisabled` (1235) | Covered |
| Failed plugin | `StartCleaningAsync_ShouldContinueOnError_WhenPluginFails` (458) | Covered |
| Stopped (graceful) | `StopCleaningAsync_ShouldTerminateActiveProcess_Gracefully_AndStoreGracePeriodExpiredResult` (679) | Covered |
| Force-stopped | `ForceStopCleaningAsync_ShouldTerminateActiveProcess_WithForceKillTrue` (743) | Covered |
| Left-running | None — `MarkLeftRunningByUser` not currently characterized end-to-end | **GAP — add Wave 1** |
| Already-clean | `StartCleaningAsync_ShouldSetAlreadyClean_WhenCompletionLineButZeroStats` (1879) | Covered |
| Backup-canceled | `BackupCancellation_DoesNotLaunchXEdit_ForCanceledPlugin` (1446) | Covered |
| Backup-failed-choice (Skip) | `BackupFailureChoice_SkipPlugin_PublishesSkippedResultAndCompletesSessionAccounting` (1579) | Covered |
| Backup-failed-choice (Abort) | `BackupFailureChoice_AbortSession_FinalizesCanceledSessionWithPreviousResults` (1629) | Covered |
| Backup-failed-choice (Continue) | `BackupFailure_StillUsesBackupFailureCallback_ForNonCanceledFailures` (1539) — verify the Continue branch is asserted | **POSSIBLE GAP — verify in Wave 1** |
| Retention-warning | None — retention is mocked away in current orchestrator tests | **GAP — add Wave 1** |
| Retention-canceled | None | **GAP — add Wave 1** |
| Dry-run | `RunDryRunAsync` covered separately in `CleaningOrchestratorTests` and `CleaningCommandsViewModelTests` (verify) | Covered, but extend to enforce preflight equivalence (Wave 1) |

**Recommended Wave 1 characterization additions before any extraction:**
1. `MarkLeftRunningByUser_AfterStopCleaning_ReportsLeftRunningTerminationResult`
2. `Retention_WhenWarningResultReturned_SessionResultClassifiesAsSuccessfulWithBackupCleanup`
3. `Retention_WhenCanceledResultReturned_SessionResultIncludesCanceledBackupCleanup`
4. `RunDryRunAsync_AndStartCleaningAsyncPreflight_ProduceSameSkipDecisions_ForIdenticalState`

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 + FluentAssertions 8.8.0 + NSubstitute 5.3.0 |
| Config file | `AutoQAC.Tests/AutoQAC.Tests.csproj` (no separate xunit config; uses defaults) |
| Quick run command | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Cleaning" --nologo` |
| Full suite command | `dotnet test AutoQACSharp.slnx --nologo` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|--------------|
| REF-01 | Maintainer can change preflight without editing other concerns | unit (collaborator-isolated) | `dotnet test --filter "FullyQualifiedName~CleaningPreflightTests" --nologo` | ❌ Wave 0 (`AutoQAC.Tests/Services/Cleaning/CleaningPreflightTests.cs`) |
| REF-01 | Maintainer can change backup-session handling without editing other concerns | unit (collaborator-isolated) | `dotnet test --filter "FullyQualifiedName~BackupSessionCoordinatorTests" --nologo` | ❌ Wave 0 (`AutoQAC.Tests/Services/Cleaning/BackupSessionCoordinatorTests.cs`) |
| REF-01 | Maintainer can change runner / finalizer without editing termination | unit (collaborator-isolated) | `dotnet test --filter "FullyQualifiedName~PluginCleaningRunnerTests\|FullyQualifiedName~PluginResultFinalizerTests" --nologo` | ❌ Wave 0 (`AutoQAC.Tests/Services/Cleaning/PluginCleaningRunnerTests.cs`, `PluginResultFinalizerTests.cs`) |
| REF-01 | Maintainer can change termination without editing runner | unit (collaborator-isolated) | `dotnet test --filter "FullyQualifiedName~CleaningTerminationCoordinatorTests" --nologo` | ❌ Wave 0 (`AutoQAC.Tests/Services/Cleaning/CleaningTerminationCoordinatorTests.cs`) |
| REF-01 | Public `ICleaningOrchestrator` surface unchanged | source-level | `dotnet test --filter "FullyQualifiedName~CleaningOrchestratorPublicSurface"` | ❌ Wave 0 (or extend `CleaningOrchestratorTests.cs`) |
| REF-01 | All 41 existing characterization tests still pass | regression | `dotnet test --filter "FullyQualifiedName~CleaningOrchestratorTests" --nologo` | Existing |
| REF-01 | No `Parallel`/`Task.WhenAll` over plugin loop in any cleaning collaborator | source-level | extend `CleaningOrchestrator_Source_DoesNotParallelizePluginCleaning` (line 2023) to scan four new files | Partially exists |

### Sampling Rate

- **Per task commit:** `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Cleaning" --nologo` (~10s, runs all cleaning service + orchestrator + new collaborator tests)
- **Per wave merge:** `dotnet test AutoQACSharp.slnx --nologo` (full suite, includes process integration tests and view-model tests)
- **Phase gate:** Full suite green before `/gsd-verify-work`. Source-level guard tests must pass.

### Wave 0 Gaps

- [ ] `AutoQAC.Tests/Services/Cleaning/CleaningPreflightTests.cs` — covers REF-01 preflight isolation, dry-run/real-run equivalence, MO2 policy facts
- [ ] `AutoQAC.Tests/Services/Cleaning/BackupSessionCoordinatorTests.cs` — covers REF-01 backup isolation, Begin/Run/Finalize/CancelActiveOperation
- [ ] `AutoQAC.Tests/Services/Cleaning/PluginCleaningRunnerTests.cs` — covers REF-01 runner isolation, retry FSM, attach/detach delegates
- [ ] `AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs` — covers REF-01 finalizer isolation, log-skip guards, already-clean detection
- [ ] `AutoQAC.Tests/Services/Cleaning/CleaningTerminationCoordinatorTests.cs` — covers REF-01 termination isolation, stop/force-stop, Phase 5 invariants
- [ ] **NEW characterization tests in existing `CleaningOrchestratorTests.cs`** before any extraction:
  - `MarkLeftRunningByUser_AfterStopCleaning_ReportsLeftRunningTerminationResult`
  - `Retention_WhenWarningResultReturned_SessionResultClassifiesAsSuccessfulWithBackupCleanup`
  - `Retention_WhenCanceledResultReturned_SessionResultIncludesCanceledBackupCleanup`
  - `RunDryRunAsync_AndStartCleaningAsyncPreflight_ProduceSameSkipDecisions_ForIdenticalState`
  - `BackupFailure_ContinueWithoutBackup_ProceedsToXEdit_AndDoesNotAddBackupEntry` (verify gap closes)
  - `LastTerminationResult_IsResetToNull_AtSessionStartAndEnd`
- [ ] No framework install needed (xUnit/FluentAssertions/NSubstitute already present)

### Test Isolation Plan

- **All collaborator tests** use `Substitute.For<...>()` — no real `Process`, no real filesystem outside `Path.GetTempPath()`.
- **Termination coordinator tests** that need a real `Process` follow the existing pattern in `CleaningOrchestratorTests` (using `Process.Start` with a long-running helper) plus `try/finally` cleanup. Reuse `AutoQAC.TestProcessHelper` from Phase 5.
- **No Avalonia.Headless** — all tests pure xUnit (CLAUDE.md constraint).

### Validation Dimensions

| Dimension | Concrete Check |
|-----------|----------------|
| Behavior preservation | All 41 existing `CleaningOrchestratorTests` pass unmodified except where new RED tests are added |
| Sequential cleaning invariant | Source-level scan of `CleaningOrchestrator.cs`, `CleaningPreflight.cs`, `BackupSessionCoordinator.cs`, `PluginCleaningRunner.cs`, `PluginResultFinalizer.cs`, `CleaningTerminationCoordinator.cs` for `Parallel\|Task.WhenAll(\|Task.Run(.*foreach`. Existing test at line 2023 extended. |
| Public surface stability | New test enumerates `typeof(ICleaningOrchestrator).GetMembers()` and asserts the snapshot matches a checked-in expected-list of method/property signatures |
| Phase 5 stop semantics | Existing tests at lines 617, 679, 743, 804, 870 unchanged; plus new termination coordinator unit tests |
| Phase 6 launch contract | `XEditCommandBuilderTests.cs` and `CleaningServiceTests.cs` unchanged — neither file should be edited in Phase 8 |
| Phase 7 backup/retention | Existing tests at lines 1446, 1500, 1539, 1579, 1629, 1694 unchanged; plus new retention warning/canceled characterization |
| Threading / cancellation | Test asserts `CancellationToken.None` is used for the `TerminateProcessAsync` call (preserve Phase 5 termination cleanup invariant) |
| Logging / message stability | Tests do NOT assert exact log message text (D-12 allows internal log line text changes). They DO assert user-facing exception messages on missing config / unknown game / MO2 missing path remain verbatim. |
| Test isolation | Code-review checklist item: no test touches network, no test depends on a real xEdit/MO2 install, all temp directories cleaned in `try/finally`. |
| Error/result domain model | `PluginBackupOutcome` / `PluginRunnerOutput` / `TerminationFinalizeContext` are records returning structured results; expected failures do not throw. |

## Risks + Open Questions for Planner

1. **AbortSession ownership: facade or coordinator?** The current code does session finalization inside the abort branch (lines 333-362) before returning. If the backup-session coordinator finalizes the session, the facade loses control of session result construction. **Recommendation:** Coordinator returns `AbortSession` outcome with the partial state; facade owns `CleaningSessionResult` construction and `FinishCleaningWithResults` call. This keeps session lifecycle in one place. **No user input needed** unless planner deviates.
2. **Backup-cancel rejection when xEdit active.** Today, `CancelBackupOperationAsync` checks `_currentProcess` (lines 653-659). After refactor, this check needs to query the termination coordinator (`HasActiveProcess`). **Recommendation:** Facade gates the call, then delegates to the backup coordinator only if termination coordinator says no xEdit is active. **No user input needed.**
3. **`MayProcessStillBeRunning` snapshot timing.** The current code reads `_lastTerminationResult` *after* the runner returns (line 461). The termination coordinator must expose this as a property the finalizer reads at the right moment. The runner's `detachProcess()` callback must already have completed before the finalizer is called so `_lastTerminationResult` reflects the final state. Sequence: runner.RunAsync returns → facade calls termination.DetachProcess → facade snapshots `(ProcessMayStillBeRunning, IsStopRequested)` → facade calls finalizer.FinalizeAsync. **Document in PLAN.**
4. **Hang detection observable lifetime.** Current code has the orchestrator own `_hangDetected` Subject and forward to it inside `StartHangMonitoring`. After refactor, the termination coordinator owns it. `ICleaningOrchestrator.HangDetected` becomes `=> terminationCoordinator.HangDetected`. **No user input needed.**
5. **Self-PID refusal behavior on coordinator construction.** The coordinator must not be wired with a way to bypass the self-PID check. Self-PID check stays inside `StopAsync`/`ForceStopAsync` verbatim. **No user input needed.**
6. **Preflight returning `BackupEnabled` and `BackupMaxSessions`.** These come from `userConfig.Backup` (lines 240, 540). Putting them on the preflight plan keeps the backup coordinator from needing its own `IConfigurationService` dependency. **Recommendation:** Yes, include on the plan record. **No user input needed.**
7. **CONCERNS.md mentions duplicated `RequiresFileLoadOrder` in 5 places.** This is `REF-02` territory (Phase 9) and `REF-01` does not cover it. **D-11 applies:** if planner is tempted to consolidate this, ASK FIRST. Phase 8 should keep `RequiresFileLoadOrder` private inside `ICleaningPreflight` (and leave the duplicates in `CleaningService`, ConfigurationViewModel, etc. for Phase 9).
8. **Test file location.** `STRUCTURE.md` says new tests go under `AutoQAC.Tests/Services/Cleaning/` (line 267). This subfolder does NOT yet exist; only flat `Services/` is used. **Recommendation:** Create `AutoQAC.Tests/Services/Cleaning/` for the five new collaborator test files. Keep `CleaningOrchestratorTests.cs` at `AutoQAC.Tests/Services/` (existing location) so its file path doesn't churn. **No user input needed.**

## Wave Structure Recommendation

This expands on Migration Ordering above with explicit task-grouping suggestions for the planner. Adjust based on planner's task-sizing constraints.

### Wave 0 (Characterization, no production code changes)
**Single plan, parallel-safe within wave**

- **Task 0.1:** Add 6 new characterization tests to `CleaningOrchestratorTests.cs` covering D-18 gaps (Left-running, retention warning, retention canceled, dry-run/preflight equivalence, ContinueWithoutBackup branch, last-termination-result reset).
- **Task 0.2:** Add public-surface snapshot test for `ICleaningOrchestrator`.

**Exit criteria:** All 47 tests green. No production code changed. Characterization is locked.

### Wave 1 (Preflight Extraction)
**Depends on Wave 0**

- **Task 1.1 (TDD):** Create `ICleaningPreflight`, `CleaningPreflight`, `CleaningPreflightPlan`, `PreflightPluginRow`, `PreflightDecision`, `PreflightSkipReason` with RED tests for the 3 RED scenarios above.
- **Task 1.2 (Execute-only):** Wire `CleaningPreflight` into facade. Replace lines 76-222 in `StartCleaningAsync` and lines 972-1064 in `RunDryRunAsync` with calls to `_preflight.PrepareAsync`. State mutation per D-14: facade owns `stateService.UpdateState` for detected game type. DI registration in `ServiceCollectionExtensions.cs` Singleton.
- **Task 1.3:** Add source-level guard test that no parallel constructs exist in `CleaningPreflight.cs`.

**Exit criteria:** All 47+ characterization tests + new collaborator tests green. `CleaningOrchestrator.cs` shrinks by ~250 lines.

### Wave 2 (Backup-Session Extraction)
**Depends on Wave 1**

- **Task 2.1 (TDD):** Create `IBackupSessionCoordinator`, `BackupSessionCoordinator`, `PluginBackupOutcome`, `PluginBackupOutcomeKind` with RED tests.
- **Task 2.2 (Execute-only):** Move `BackupPluginAsync`, `CleanupOldSessionsAsync`, `CreateBackupOperationCts`, `ClearBackupOperationCts`, `GetBackupFailureReasonText` into the coordinator. Replace lines 239-263, 281-383, 526-546, 564-588, 651-677, 679-808 in facade. Coordinator returns `PluginBackupOutcome`; facade dispatches.
- **Task 2.3:** Add source-level guard that `BackupSessionCoordinator.cs` does not parallelize.

**Exit criteria:** Facade no longer references `_backupOperationCts` or `_backupOperationLock`. All tests green.

### Wave 3 (Termination Coordinator Extraction)
**Depends on Wave 2 — ISOLATED because it touches Phase 5 locks**

- **Task 3.1 (TDD):** Create `ICleaningTerminationCoordinator`, `CleaningTerminationCoordinator` with RED tests for 3 RED scenarios. Test self-PID refusal explicitly.
- **Task 3.2 (Execute-only):** Move `_currentProcess`, `_processLock`, `_isStopRequested`, `_lastTerminationResult`, `_hangMonitorSubscription`, `_hangDetected` Subject, `StartHangMonitoring`, `StopCleaningAsync`, `ForceStopCleaningAsync`, `MarkLeftRunningByUser`, `ToStopCleaningResult`, `MayProcessStillBeRunning` into the coordinator. Facade methods become thin: cancel session CTS, then delegate to coordinator. Forward `LastTerminationResult` and `HangDetected` properties from the coordinator.
- **Task 3.3:** Adjust `CleaningOrchestratorTests` to verify behavior survives — no test edits should be needed except possibly mock setup.

**Exit criteria:** Facade no longer has any `_processLock`, `_isStopRequested`, or hang-monitoring code. All Phase 5 invariant tests pass unchanged. **Code review depth: deep (per `.planning/config.json`).**

### Wave 4 (Per-Plugin Runner + Finalizer Extraction)
**Depends on Wave 3**

- **Task 4.1 (TDD):** Create `IPluginCleaningRunner`, `PluginCleaningRunner`, `PluginRunnerOutput` with RED tests.
- **Task 4.2 (TDD):** Create `IPluginResultFinalizer`, `PluginResultFinalizer`, `TerminationFinalizeContext` with RED tests.
- **Task 4.3 (Execute-only):** Replace lines 386-440 with `_runner.RunAsync(...)` (passing termination's `AttachProcess`/`DetachProcess` as delegates). Replace lines 456-513 with `_finalizer.FinalizeAsync(...)` (passing `TerminationFinalizeContext` snapshot).
- **Task 4.4:** Add source-level guards.

**Exit criteria:** Facade's `StartCleaningAsync` foreach body is < 50 lines and reads as: state-update → backup-coordinator.RunPluginBackupAsync → dispatch outcome → runner.RunAsync → termination.DetachProcess → snapshot context → finalizer.FinalizeAsync → state.AddDetailedCleaningResult.

### Wave 5 (Final Integration + Regression Sweep)
**Depends on Wave 4**

- **Task 5.1:** Verify `CleaningOrchestrator.cs` is now < 250 lines (estimate).
- **Task 5.2:** Run full `dotnet test AutoQACSharp.slnx --nologo` and confirm all green.
- **Task 5.3:** Run code review (`code_review_depth: deep` per config). Verify no comments stripped, all new methods have XML docs, all `ConfigureAwait(false)` preserved, all empty catch/finally blocks have explanatory comments.
- **Task 5.4:** Update `.planning/STATE.md` with Phase 8 completion notes.

**Exit criteria:** REF-01 satisfied; planner can hand off to `/gsd-verify-work`.

## Out-of-Scope Traps (D-11 Reminders)

The planner must explicitly decline to address these even if decomposition tempts them:

1. **Consolidating `RequiresFileLoadOrder` across the codebase** — duplicated in `CleaningOrchestrator`, `CleaningService`, `CleaningCommandsViewModel`, `ConfigurationViewModel`, `PluginLoadingService`. This is REF-02 (Phase 9). Keep duplicates; do not introduce a shared `IGameCapabilityPolicy` here.
2. **Replacing `IStateService.ConfigurationValidChanged`** — CONCERNS.md flags it as stale. Do not remove or refactor; it's outside REF-01.
3. **Improving `BackupService.CleanupOldSessionsAsync` performance** — CONCERNS.md notes sequential deletion. Phase 7 explicitly chose sequential for filesystem safety; do not parallelize.
4. **Renaming user-facing messages for clarity** — D-12 allows internal log changes only. Any "while I'm here" tweak to user-facing dialog text is forbidden.
5. **Adding the unified game capability registry** — Out of scope for REF-01.
6. **Fixing `_pendingConfig` debounce edge cases** — That's REF-03 / Phase 10.
7. **Restructuring `IBackupService` to have a session-scoped sub-interface** — Tempting because `IBackupService` has many methods, but Phase 8 should not edit `IBackupService.cs`. The new `IBackupSessionCoordinator` is a *consumer* of `IBackupService`, not a refactor of it.
8. **Moving `XEditCommandBuilder` arguments inside the runner** — Phase 6 lock; the runner calls `cleaningService.CleanPluginAsync` which calls the builder. Don't restructure the launch contract.
9. **Adding logging instrumentation to new collaborators** — D-12 says collaborator names in logs may change. The planner can add NEW log lines if useful, but should NOT delete or rewrite existing log lines.
10. **Creating an Avalonia.Headless test project** — CLAUDE.md and CONCERNS.md both say this is a future/medium-priority gap. Phase 8 does not need it.

## Sources

### Primary (HIGH confidence — direct code/file reads in this session)

- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` (1151 lines, full read)
- `AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs` (88 lines, full read)
- `AutoQAC/Services/Cleaning/CleaningService.cs` (171 lines, full read)
- `AutoQAC/Services/Backup/IBackupService.cs` (108 lines, full read)
- `AutoQAC/Services/Process/IProcessExecutionService.cs` (53 lines, full read)
- `AutoQAC/Services/State/IStateService.cs` (85 lines, full read)
- `AutoQAC/Services/Monitoring/IHangDetectionService.cs` (20 lines, full read)
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs` (2083 lines, structural index + spot reads)
- `.planning/phases/08-cleaning-orchestrator-decomposition/08-CONTEXT.md` (full read — locks D-01..D-20)
- `.planning/phases/05-process-stop-pid-safety/05-CONTEXT.md` (full read)
- `.planning/phases/06-command-launch-escaping/06-CONTEXT.md` (full read)
- `.planning/phases/07-backup-restore-retention-safety/07-CONTEXT.md` (full read)
- `.planning/REQUIREMENTS.md` (full read)
- `.planning/ROADMAP.md` (full read)
- `.planning/STATE.md` (full read)
- `.planning/codebase/CONCERNS.md` (full read)
- `.planning/codebase/STRUCTURE.md` (cleaning sections grep)
- `.planning/codebase/ARCHITECTURE.md` (cleaning request path grep)
- `./CLAUDE.md` and `~/.claude/CLAUDE.md` (read via system reminder context)

### Secondary (training knowledge — no external lookups required)

- C# 13 / .NET 10 idioms (sealed records, primary constructors, file-scoped namespaces, init-only properties) — already established in this codebase, no doc lookup needed
- xUnit / FluentAssertions / NSubstitute test patterns — already in use across `AutoQAC.Tests`, no doc lookup needed

### Not Needed

- No Mutagen/Avalonia/MVVM-toolkit doc lookups required: Phase 8 does not touch UI tiers, Mutagen, or any external library APIs. The refactor is internal to `AutoQAC/Services/Cleaning/`.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | The planner will create a new `AutoQAC.Tests/Services/Cleaning/` subfolder for collaborator tests rather than putting them flat in `AutoQAC.Tests/Services/`. | Test Isolation Plan | Low — STRUCTURE.md line 267 endorses this; if planner picks flat path that's also fine. |
| A2 | `IBackupSessionCoordinator` should NOT own session result construction (`CleaningSessionResult` + `FinishCleaningWithResults`); facade owns it. | Open Question 1 | Low — alternative is one-method-larger coordinator; either is correct, my recommendation matches today's structure. |
| A3 | The runner takes `attachProcess`/`detachProcess` delegates rather than `ICleaningTerminationCoordinator` directly. | Collaborator 3 | Low — alternative is direct injection; both work. Delegate form keeps runner pure. |
| A4 | DI lifetime for all five new collaborators is Singleton (matches existing pattern at `ServiceCollectionExtensions.cs:57-62`). | Wave 1.2 | Negligible — singletons can be tested with NSubstitute substitutes. |
| A5 | `LeftRunningByUser` characterization gap is real and not redundantly tested under another name. | Test Coverage table | Low — verified by grep over test method names; no method matches `LeftRunning` semantics. |

**If user wants to confirm assumptions A1–A5 before planning starts:** A2 is the highest-impact (changes facade vs. coordinator scope). A1, A3, A4, A5 are low-risk drift even if wrong.

## Metadata

**Confidence breakdown:**
- Section-by-Section Map: HIGH — derived from full read of `CleaningOrchestrator.cs`
- Extraction proposal: HIGH — interfaces respect every locked decision in 08-CONTEXT.md
- Migration ordering: HIGH — follows D-05 explicitly with risk-graded sequencing
- Stable invariants: HIGH — each invariant tied to existing test or new test
- Threading analysis: HIGH — derived from current orchestrator field/lock inventory
- Test seam strategy: HIGH — collaborators have clean async I/O contracts
- Test coverage gaps: MEDIUM-HIGH — based on grep over test method names; missed gaps possible only if a test name is misleading
- Wave structure: MEDIUM — actual task counts depend on planner's granularity preference (`fine` per config.json)

**Research date:** 2026-04-29
**Valid until:** 2026-05-29 (30 days — codebase area is stable; no upcoming Mutagen or Avalonia upgrades expected in scope)

## RESEARCH COMPLETE

Decomposition seams are mapped, four collaborators (preflight, backup-session, runner+finalizer, termination coordinator) are specified with concrete interfaces, migration ordering follows D-05 (characterize-then-extract), all Phase 5/6/7 invariants are mapped to proving tests, and the planner has a 5-wave (+ Wave 0 characterization) structure to consume. Validation Architecture section is present per Nyquist gate. No assumptions block planning — A2 is the only one a planner might want to confirm with the user, but my recommendation matches today's behavior so the safe path is to follow it.