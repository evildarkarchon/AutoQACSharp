---
phase: 15
slug: stop-escalation-ownership-closure
status: complete
researched: 2026-05-01
---

# Phase 15 Research — Stop Escalation Ownership Closure

## Research Complete

Phase 15 is a narrow stop/process ownership fix. The current implementation already has the desired Progress-window confirmation copy and force-failure UI path from Phase 12, but the active process reference can be cleared before the user confirms `Force Terminate`.

## Source Findings

### Existing Flow

- `CleaningTerminationCoordinator.AttachProcess(Process)` stores `_currentProcess` and starts hang monitoring.
- `CleaningTerminationCoordinator.StopAsync()` sets `_lastTerminationResult = GracePeriodExpired` when graceful termination times out.
- `PluginCleaningRunner.RunAsync()` always calls `detachProcess()` in `finally` after the retry loop.
- `CleaningTerminationCoordinator.DetachProcess()` currently stops hang monitoring and sets `_currentProcess = null`.
- `CleaningTerminationCoordinator.ForceStopAsync()` only force-kills `_currentProcess`; if it is null, it returns the cached `_lastTerminationResult`, which can be `GracePeriodExpired`.
- `CleaningOrchestrator.StartCleaningAsync()` calls `terminationCoordinator.ResetForNewSession()` in `finally`; this can erase state before the user resolves the already-shown `GracePeriodExpired` prompt.
- `ProgressViewModel.StopAsync()` correctly holds the confirmation dialog before calling `ForceStopCleaningAsync()` and shows shared force-failure copy only for `ForceKillFailed`.

### Locked Decision Implications

- D-01 through D-05 require a retained `Process` handle, not PID/start-time recovery, as the primary Phase 15 mechanism.
- D-02 requires the pending target to survive runner detach and normal session finalization until user resolution or a next-session reset boundary.
- D-03 and D-04 require pending escalation ownership to remain separate from active cleaning semantics. `HasActiveProcess`, hang monitoring, backup-cancel gating, and current plugin state must continue to mean active cleaning.
- D-06 through D-09 require confirmed force escalation to return a terminal outcome (`ForceKilled`, `AlreadyExited`, or `ForceKillFailed`) and never silently reuse `GracePeriodExpired`.
- D-10 through D-14 require layered automated proof using coordinator/service, controlled helper process, and Progress ViewModel tests without Avalonia.Headless or real xEdit/MO2 setup.
- D-15 through D-18 require a concise `15-VERIFICATION.md` evidence artifact and defer milestone marker reconciliation to Phase 16.

## .NET Process Semantics

Official Microsoft documentation for `System.Diagnostics.Process.Kill` states that `Kill` forces termination, while `CloseMainWindow` only requests termination. `Kill` executes asynchronously; after calling it, code should call `WaitForExit` or check `HasExited` to determine whether the process exited. `Kill(entireProcessTree: true)` can throw `Win32Exception`, `NotSupportedException`, `InvalidOperationException`, or `AggregateException` depending on process state and permission/tree conditions.

Official documentation for `Process.HasExited` states that it can throw `InvalidOperationException` when no process is associated, `Win32Exception` when exit code retrieval fails, and `NotSupportedException` for remote processes. Therefore Phase 15 should treat inability to prove exit after confirmation as `ForceKillFailed` unless the retained target clearly reports `HasExited == true`.

## Recommended Implementation Shape

### Coordinator State

Add one separate pending target field in `CleaningTerminationCoordinator`, for example:

```csharp
private System.Diagnostics.Process? _pendingForceEscalationProcess;
```

Rules:

1. When `StopAsync()` receives `GracePeriodExpired`, preserve the currently attached process in `_pendingForceEscalationProcess`.
2. `DetachProcess()` must stop hang monitoring and clear `_currentProcess`, but it must not clear `_pendingForceEscalationProcess` while `_lastTerminationResult` is `GracePeriodExpired` and the retained process may still need confirmed force escalation.
3. `ForceStopAsync()` must prefer `_currentProcess`; if none exists and `_lastTerminationResult` indicates the process may still be running, use `_pendingForceEscalationProcess`.
4. `ForceStopAsync()` must map outcomes as follows after confirmation:
   - target exists and `HasExited == false` and force termination returns `ForceKilled` -> `ForceKilled`.
   - target exists and `HasExited == true` -> `AlreadyExited`.
   - target unavailable, disposed, invalid, inaccessible, or cannot prove exited -> `ForceKillFailed`.
5. `MarkLeftRunningByUser()` must release the pending target and store `LeftRunningByUser`.
6. The normal end-of-session cleanup path must preserve an unresolved pending target after `GracePeriodExpired`; the start-of-next-session reset boundary may release stale unresolved pending target before new cleaning begins.

### Orchestrator Wiring

Because `CleaningOrchestrator.StartCleaningAsync()` currently calls `ResetForNewSession()` at both session start and session finalization, the coordinator contract needs either a new method or parameter that distinguishes:

- **New session reset:** clear stale pending escalation state before starting a new session.
- **Session finalization cleanup:** clear active cleaning/hang/session UI state but preserve unresolved `GracePeriodExpired` pending target for the already-shown Progress confirmation.

Keep this distinction explicit in interface documentation so later cleanup does not reintroduce the ownership gap.

## Architectural Responsibility Map

| Tier | Responsibility | Phase 15 Assignment |
|------|----------------|---------------------|
| `CleaningTerminationCoordinator` | Owns active and pending process references, stop/force-stop terminal outcomes, hang monitor lifecycle | Implement retained pending escalation target and terminal result mapping |
| `CleaningOrchestrator` | Coordinates session CTS and calls termination coordinator | Use the new reset/finalization distinction without adding process manipulation |
| `PluginCleaningRunner` | Runs a plugin and calls attach/detach delegates | No behavior change unless required by tests; detach remains once per plugin |
| `ProgressViewModel` | Shows confirmation and safe failure UI through services | Preserve confirmation-before-force and shared force-failure display |
| `ProcessExecutionService` | Performs actual graceful/force process termination | Reuse existing `TerminateProcessAsync`; no new direct process launch in UI/workflow code |

## Common Pitfalls

- Do not make `_pendingForceEscalationProcess` count as active cleaning; `HasActiveProcess` must stay tied to `_currentProcess`.
- Do not keep hang monitoring active after runner detach; the pending target is awaiting user resolution, not active cleaning progress.
- Do not return cached `GracePeriodExpired` from a confirmed force action.
- Do not add PID recovery unless the retained handle approach fails a required timing edge.
- Do not broaden into Hang Kill, orphan cleanup, timeout redesign, command launch/MO2 behavior, or milestone marker reconciliation.
- Do not add Avalonia.Headless or require real xEdit/MO2.

## Validation Architecture

Targeted automated evidence should run before the full suite:

| Concern | Primary files | Command |
|---------|---------------|---------|
| Coordinator retained pending target | `AutoQAC.Tests/Services/Cleaning/CleaningTerminationCoordinatorTests.cs` | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~CleaningTerminationCoordinatorTests --nologo` |
| Progress confirmation/failure visibility | `AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs` | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~ProgressViewModelTests --nologo` |
| Process/PID helper evidence | `AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs` | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~ProcessExecutionIntegrationTests --nologo` |
| Phase-level regression sweep | solution | `dotnet test AutoQACSharp.slnx --nologo` |

`15-VERIFICATION.md` must record command rows with command, scope, result, relevant test names, and source files. Long output should be omitted unless documenting a failure.
