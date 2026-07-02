# CleaningSession Seam Replacement Implementation Plan

This document is the handoff for a fresh agent implementing the `ICleaningOrchestrator` to `CleaningSession` refactor.

Authoritative context:

- `CONTEXT.md`: canonical domain term is **Cleaning session**.
- `docs/adr/0001-replace-cleaning-orchestrator-seam.md`: replace `ICleaningOrchestrator` directly; do not keep a compatibility facade.
- `AGENTS.md`: sequential cleaning is a hard runtime requirement; do not parallelize plugin cleaning or xEdit launches.

## Goal

Replace the current shallow `ICleaningOrchestrator` interface with a deeper `CleaningSession` module whose interface gives callers leverage while keeping session lifecycle, user decisions, backup meaning, cancellation, and state publication local to one implementation.

The target module is **deep**: callers learn a small interface, while the implementation owns the full Cleaning session from preflight through final state publication.

## Non-Goals

Do not include these in the same implementation slice:

- Do not redesign the xEdit process lifecycle seam.
- Do not redesign plugin discovery, MO2 plugin loading, skip-list policy, or game capability rules.
- Do not redesign progress publication beyond the small state-publication adapter needed by `CleaningSession`.
- Do not redesign the AutoQAC-to-QueryPlugins adapter.
- Do not parallelize plugin cleaning or xEdit launches.
- Do not modify `Mutagen/`.

## Target Public Interface

Create `AutoQAC/Services/Cleaning/ICleaningSession.cs` and remove the public `ICleaningOrchestrator` seam once all callers are migrated.

```csharp
public interface ICleaningSession
{
    /// <summary>
    /// Emits xEdit hang state during an active Cleaning session. Emits false when the warning should clear.
    /// </summary>
    IObservable<bool> HangDetected { get; }

    /// <summary>
    /// Runs a full Cleaning session from preflight through final state publication.
    /// </summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Runs the same preflight and plugin-decision logic without backing up, launching xEdit, or mutating cleaning state.
    /// </summary>
    Task<IReadOnlyList<DryRunResult>> PreviewAsync(CancellationToken ct = default);

    /// <summary>
    /// Applies a live control intent to the active Cleaning session.
    /// </summary>
    Task<CleaningSessionControlResult> ControlAsync(
        CleaningSessionControl control,
        CancellationToken ct = default);
}

public enum CleaningSessionControl
{
    RequestStop,
    ForceStop,
    CancelBackupOperation
}

public sealed record CleaningSessionControlResult(
    CleaningSessionControl Control,
    CleaningSessionControlStatus Status,
    TerminationResult? TerminationResult = null);

public enum CleaningSessionControlStatus
{
    NoActiveSession,
    StopRequested,
    GracefullyStopped,
    ForceStopped,
    LeftRunningByUser,
    ForceKillFailed,
    BackupCancellationRequested,
    NoActiveBackupOperation
}
```

Interface rules:

- `StartAsync` replaces all `StartCleaningAsync` overloads.
- `PreviewAsync` replaces `RunDryRunAsync`.
- `ControlAsync(RequestStop)` replaces the full stop flow, including graceful stop, grace-period-expired prompt, force stop, and left-running acknowledgement.
- `ControlAsync(ForceStop)` replaces direct force stop for hang-warning actions.
- `ControlAsync(CancelBackupOperation)` replaces backup/retention cancellation.
- `LastTerminationResult` is no longer public. Return `TerminationResult?` through `CleaningSessionControlResult` when callers need to project a status.
- `MarkLeftRunningByUser` is no longer public. The implementation calls the existing termination adapter after the user-decision adapter chooses leave-running.

## New Adapters

The external seam is `ICleaningSession`. The following adapters sit behind that seam.

### User Decisions

Add `AutoQAC/Services/Cleaning/ICleaningSessionDecisionAdapter.cs`.

```csharp
public interface ICleaningSessionDecisionAdapter
{
    Task<bool> ShouldRetryTimedOutPluginAsync(
        string pluginName,
        int timeoutSeconds,
        int attemptNumber,
        int maxAttempts,
        CancellationToken ct);

    Task<BackupFailureChoice> ChooseBackupFailureAsync(
        string pluginName,
        string errorMessage,
        CancellationToken ct);

    Task<CleaningSessionStopDecision> ChooseAfterGracePeriodExpiredAsync(
        TerminationResult terminationResult,
        CancellationToken ct);
}

public enum CleaningSessionStopDecision
{
    ForceTerminate,
    LeaveRunning
}
```

Production adapter:

- Add `AutoQAC/Services/UI/CleaningSessionDialogDecisionAdapter.cs` or equivalent.
- Use `IMessageDialogService`.
- Move the timeout prompt from `CleaningCommandsViewModel.HandleTimeoutRetryAsync`.
- Move the backup-failure prompt from `CleaningCommandsViewModel.HandleBackupFailureAsync`.
- Move only the grace-period-expired choice prompt into this adapter. Callers may still project final status messages, but callers must not decide whether to force stop or mark left-running.

Test adapter:

- Use a scripted fake or NSubstitute implementation in `CleaningSession` scenario tests.
- This is a real seam: production uses WinUI dialogs; tests use deterministic choices.

### State Publication

Add `AutoQAC/Services/Cleaning/ICleaningSessionStatePublisher.cs` and an `IStateService` adapter.

Suggested shape:

```csharp
public interface ICleaningSessionStatePublisher
{
    void PublishDetectedGame(GameType gameType);
    void PublishStarted(IReadOnlyList<PluginInfo> pluginsToClean);
    void PublishCurrentPlugin(string pluginName);
    void PublishPluginResult(PluginCleaningResult result);
    void PublishSkippedPlugin(string pluginName);
    void PublishTerminating(bool isTerminating);
    void PublishCompleted(CleaningSessionResult sessionResult);
}
```

Implementation notes:

- `StateServiceCleaningSessionStatePublisher` should wrap current `IStateService` calls.
- Keep backup operation publication in `BackupSessionCoordinator` for this slice unless moving it into the publisher is a small mechanical change.
- The purpose is locality: `CleaningSession` owns when state publication happens; the adapter owns how it maps to `IStateService`.

## Production File Migration

### Add or Rename

- Add `AutoQAC/Services/Cleaning/ICleaningSession.cs`.
- Add `AutoQAC/Services/Cleaning/CleaningSession.cs` by moving the current implementation from `CleaningOrchestrator.cs` and reshaping the public members.
- Add `AutoQAC/Services/Cleaning/CleaningSessionControlModels.cs` for `CleaningSessionControl`, `CleaningSessionControlResult`, and `CleaningSessionControlStatus` if not kept in `ICleaningSession.cs`.
- Add `AutoQAC/Services/Cleaning/ICleaningSessionDecisionAdapter.cs`.
- Add `AutoQAC/Services/Cleaning/ICleaningSessionStatePublisher.cs`.
- Add a production decision adapter under `AutoQAC/Services/UI/`.
- Add `AutoQAC/Services/Cleaning/StateServiceCleaningSessionStatePublisher.cs`.

### Remove or Replace

- Replace `AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs` with `ICleaningSession.cs`.
- Replace `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` with `CleaningSession.cs`.
- Remove `TimeoutRetryCallback` from the public seam. If any internal helper still needs a delegate, keep it internal or replace it with `ICleaningSessionDecisionAdapter` calls.

### Keep As Internal Implementation Seams

Keep these modules unless the deletion test proves one is shallow:

- `CleaningPreflight`
- `BackupSessionCoordinator`
- `CleaningTerminationCoordinator`
- `PluginCleaningRunner`
- `PluginResultFinalizer`
- `ProcessExecutionService`
- `CleaningService`

Deletion test guidance:

- If deleting a helper only moves a pass-through call into `CleaningSession`, delete or merge it.
- If deleting a helper spreads game detection, process handling, backup metadata, log parsing, or termination rules across the implementation, keep it as an internal seam.

## CleaningSession Implementation Notes

Start from the current `CleaningOrchestrator.StartCleaningAsync` implementation and preserve behavior.

Changes to make:

- Rename `StartCleaningAsync(...)` to `StartAsync(CancellationToken ct = default)`.
- Replace timeout callback calls with `_decisions.ShouldRetryTimedOutPluginAsync(...)` through `PluginCleaningRunner` or an adapter path.
- Replace backup failure callback calls with `_decisions.ChooseBackupFailureAsync(...)`.
- Rename `RunDryRunAsync` to `PreviewAsync` and return `IReadOnlyList<DryRunResult>`.
- Replace `StopCleaningAsync`, `ForceStopCleaningAsync`, `CancelBackupOperationAsync`, and `MarkLeftRunningByUser` with `ControlAsync`.
- Keep `HangDetected` public by delegating to the current termination adapter.
- Keep `LastTerminationResult` private or internal to the implementation.

`ControlAsync(RequestStop)` behavior:

1. Cancel the current session CTS first.
2. Call the existing termination adapter graceful stop path.
3. If the termination result is not `GracePeriodExpired`, return a corresponding `CleaningSessionControlResult`.
4. If `GracePeriodExpired`, call `_decisions.ChooseAfterGracePeriodExpiredAsync(...)`.
5. If the decision is `ForceTerminate`, call the existing force-stop path and return force status.
6. If the decision is `LeaveRunning`, call the existing `MarkLeftRunningByUser` behavior internally and return `LeftRunningByUser`.

`ControlAsync(ForceStop)` behavior:

- Cancel the current session CTS.
- Call the existing force-stop path.
- Return `ForceStopped` or `ForceKillFailed` based on `TerminationResult`.

`ControlAsync(CancelBackupOperation)` behavior:

- If xEdit is active, ignore backup cancellation exactly as today.
- Otherwise call the backup cancellation path.
- Do not terminate xEdit.
- Return `BackupCancellationRequested` or `NoActiveBackupOperation` if the implementation can know there is no active operation. If it cannot know yet, returning `BackupCancellationRequested` for a no-op is acceptable for this slice.

## Caller Migration

### `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`

Replace:

```csharp
services.AddSingleton<ICleaningOrchestrator, CleaningOrchestrator>();
```

With registrations for:

- `ICleaningSessionDecisionAdapter`
- `ICleaningSessionStatePublisher`
- `ICleaningSession`

Example:

```csharp
services.AddSingleton<ICleaningSessionStatePublisher, StateServiceCleaningSessionStatePublisher>();
services.AddSingleton<ICleaningSession, CleaningSession>();
```

Register the production dialog decision adapter after UI services are available. Registration order in `IServiceCollection` is safe as long as all services are registered before `BuildServiceProvider()`.

### `AutoQAC/App.xaml.cs`

- Resolve `ICleaningSession` instead of `ICleaningOrchestrator`.
- Pass `ICleaningSession` into `MainWindow`.

### `AutoQAC/Views/MainWindow.xaml.cs`

- Store `ICleaningSession?` instead of `ICleaningOrchestrator?`.
- Pass `ICleaningSession` to `ProgressViewModel` creation.

### `AutoQAC/ViewModels/MainWindowViewModel.cs`

- Constructor takes `ICleaningSession`.
- Pass it to `CleaningCommandsViewModel`.

### `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`

- Constructor takes `ICleaningSession` instead of `ICleaningOrchestrator`.
- `StartCleaningAsync`: call `await cleaningSession.StartAsync();`.
- `PreviewAsync`: call `await cleaningSession.PreviewAsync();` and convert to `List<DryRunResult>` only where the existing interaction requires it.
- `StopCleaningAsync`: call `await cleaningSession.ControlAsync(CleaningSessionControl.RequestStop);`.
- Do not call force stop or mark-left-running from this ViewModel.
- Remove timeout and backup-failure prompt helpers after moving them to the production decision adapter.

### `AutoQAC/ViewModels/ProgressViewModel.cs`

- Constructor takes `ICleaningSession`.
- Subscribe to `cleaningSession.HangDetected`.
- `StopAsync`: call `ControlAsync(RequestStop)` once, then project the result to warning text if needed.
- `KillHungProcessAsync`: call `ControlAsync(ForceStop)`.
- `CancelBackupOperationAsync`: call `ControlAsync(CancelBackupOperation)`.
- Remove reads of `LastTerminationResult`.
- Remove direct calls to `ForceStopCleaningAsync` and `MarkLeftRunningByUser` after a graceful stop result.

## Test Migration

### New Scenario Test Surface

Create `AutoQAC.Tests/Services/CleaningSessionTests.cs` or rename `CleaningOrchestratorTests.cs` after migrating assertions.

Primary scenario tests should cross the `ICleaningSession` interface. The interface is the test surface.

Move or rewrite these groups from `CleaningOrchestratorTests.cs`:

- Successful session processes plugins and publishes final result.
- Invalid configuration/preflight failures do not launch xEdit.
- Stop during orphan cleanup or preflight cancels before xEdit launch.
- User cancellation mid-batch preserves partial results.
- Timeout retry decision retries or skips as selected by the decision adapter.
- Backup failure decisions skip plugin, continue without backup, or abort session.
- Backup cancellation does not launch xEdit for the canceled plugin.
- Backup cancellation during xEdit does not stop or kill xEdit.
- MO2 mode skips backup and direct file validation.
- Dry-run and real preflight produce the same skip decisions for identical state.
- Retention cleanup results are included in final `CleaningSessionResult`.
- `HangDetected` forwards true/false and clears on session end.
- A new session starts with stale termination state cleared.

Keep helper-module tests where they already give independent leverage:

- `CleaningPreflightTests`
- `BackupSessionCoordinatorTests`
- `PluginCleaningRunnerTests`
- `PluginResultFinalizerTests`
- `CleaningTerminationCoordinatorTests`
- `ProcessExecutionServiceTests`

Update ViewModel tests to mock `ICleaningSession` rather than `ICleaningOrchestrator`:

- `CleaningCommandsViewModelTests.cs`
- `ProgressViewModelTests.cs`
- `MainWindowViewModelTests.cs`
- `MainWindowViewModelInitializationTests.cs`
- `MainWindowThreadingTests.cs`
- `ErrorDialogTests.cs`
- `Phase11DiagnosticsBoundaryTests.cs`

Update DI tests:

- `AutoQAC.Tests/Integration/DependencyInjectionTests.cs` should resolve `ICleaningSession` instead of `ICleaningOrchestrator`.

### Snapshot and Source Guard Tests

Update `ICleaningOrchestrator_PublicSurface_MatchesLockedSnapshot` to target `ICleaningSession`.

Expected public surface should be similar to:

```text
IObservable<Boolean> HangDetected { get; }
Task StartAsync(CancellationToken)
Task<IReadOnlyList<DryRunResult>> PreviewAsync(CancellationToken)
Task<CleaningSessionControlResult> ControlAsync(CleaningSessionControl, CancellationToken)
```

Update sequential source guards:

- Rename `CleaningOrchestrator_Source_DoesNotParallelizePluginCleaning` to target `CleaningSession.cs`.
- Update file scans from `CleaningOrchestrator.cs` to `CleaningSession.cs`.
- Keep scanning helper files that still participate in the cleaning loop.
- Keep the backup-before-runner assertion against `CleaningSession.cs`.

## Invariants To Preserve

Runtime invariants:

- xEdit cleaning remains sequential.
- `ProcessExecutionService` remains a single process slot.
- Session CTS is created before orphan cleanup and preflight so stop can cancel startup work.
- Flush pending config before launching xEdit.
- Stop behavior is two-stage: graceful first, force after user confirmation or second-stop path inside existing termination adapter.
- `GracePeriodExpired` pending force target survives session finalization until resolved.
- `HangDetected` emits `false` on detach/session end so warnings clear.
- Backup cancellation never terminates xEdit.
- MO2 mode wraps xEdit through MO2 and skips backups.
- Real cleaning updates `CurrentGameType` after preflight detection.
- Dry-run does not mutate cleaning state, create backups, start processes, or create a session CTS.
- Detailed plugin results are published as each plugin completes.
- Final `CleaningSessionResult` is published once per real session, including cancellation and recoverable error paths.
- Partial backup metadata is written on cancellation/abort when entries exist.
- The finalizer must skip log reads when the process may still be running or stop was requested.

Code-shape invariants:

- Do not reintroduce per-call timeout or backup callbacks on the public `ICleaningSession` interface.
- Do not expose `LastTerminationResult` publicly.
- Do not expose `MarkLeftRunningByUser` publicly.
- Do not expose raw `System.Diagnostics.Process` through `ICleaningSession`.
- Do not add adapters around every helper method. One adapter is hypothetical; two adapters are real. Keep adapters only where production and test behavior actually differ.

## Suggested Implementation Order

1. Add new model/interface files: `ICleaningSession`, control result models, decision adapter, state publisher.
2. Add production adapters for user decisions and state publication.
3. Move `CleaningOrchestrator` implementation into `CleaningSession`, initially preserving behavior as much as possible.
4. Convert start/dry-run/stop methods to the new public interface.
5. Update DI and production callers from `ICleaningOrchestrator` to `ICleaningSession`.
6. Move timeout, backup-failure, and grace-period-expired prompts into the decision adapter.
7. Update ViewModels to call `StartAsync`, `PreviewAsync`, and `ControlAsync` without implementing stop escalation themselves.
8. Migrate tests to `ICleaningSession` and update snapshot/source guards.
9. Delete `ICleaningOrchestrator` and `CleaningOrchestrator` once no references remain.
10. Run the full validation suite.

## Validation Commands

Run from the repository root:

```bash
dotnet build AutoQACSharp.slnx
dotnet test AutoQACSharp.slnx
```

If a failure is specific to WinUI build/runtime prerequisites, use the WinUI development workflow before changing architecture.

## Suggested Skills For The Implementing Agent

- `codebase-design`: keep the module/interface/seam/adapter/depth/locality/leverage vocabulary consistent.
- `systematic-debugging`: use before fixing any unexpected build or test failure.
- `winui-dev-workflow`: use for WinUI build/run issues.
- `winui-code-review`: use before finalizing if XAML, ViewModel, dialog, or window lifecycle code changes.
