---
phase: 15-stop-escalation-ownership-closure
reviewed: 2026-05-02T00:38:07Z
depth: deep
files_reviewed: 6
files_reviewed_list:
  - AutoQAC.Tests/Services/Cleaning/CleaningTerminationCoordinatorTests.cs
  - AutoQAC.Tests/Services/CleaningOrchestratorTests.cs
  - AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs
  - AutoQAC/Services/Cleaning/CleaningOrchestrator.cs
  - AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs
  - AutoQAC/Services/Cleaning/ICleaningTerminationCoordinator.cs
findings:
  critical: 1
  warning: 1
  info: 0
  total: 2
status: issues_found
---

# Phase 15: Code Review Report

**Reviewed:** 2026-05-02T00:38:07Z
**Depth:** deep
**Files Reviewed:** 6
**Status:** issues_found

## Summary

Reviewed the stop-escalation ownership changes across the orchestrator, termination coordinator, and affected tests. The retained pending-force process target is not safe across the production process ownership boundary: `ProcessExecutionService.ExecuteAsync` creates the xEdit `Process` in a `using var`, so the coordinator can retain a disposed `Process` object and later fail/crash when the user confirms force termination after finalization. The new tests use externally owned test processes, which masks this production failure mode.

## Critical Issues

### CR-01: BLOCKER - Retained force-escalation target can be disposed before confirmed force kill

**File:** `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs:150`

**Issue:** `RetainPendingForceEscalationProcess(proc)` stores a `System.Diagnostics.Process` object so a later confirmed force stop can target it. In the real call chain, however, that object is created inside `ProcessExecutionService.ExecuteAsync` as `using var process` and is disposed as soon as the cleaning attempt returns. If the graceful stop returns `GracePeriodExpired`, the runner detaches/finalizes, the UI prompt remains visible, and the user then confirms force termination, `ForceStopAsync` uses the retained object at lines 195/201/203. At that point the `Process` instance may be disposed, so reading `Id`/`HasExited` or passing it to `TerminateProcessAsync` can throw instead of killing xEdit, leaving the process running after the user explicitly chose force terminate.

**Fix:** Do not retain a borrowed `Process` object beyond the lifetime of its owner. Retain a durable process identity and reacquire/validate the process for confirmed escalation, or transfer ownership so the process is not disposed until the pending escalation is resolved. For example:

```csharp
private sealed record PendingForceTarget(int ProcessId, DateTime? StartTime);
private PendingForceTarget? _pendingForceEscalationTarget;

private static DateTime? TryGetStartTime(Process process)
{
    try { return process.StartTime; }
    catch (InvalidOperationException) { return null; }
    catch (System.ComponentModel.Win32Exception) { return null; }
}

private void RetainPendingForceEscalationProcess(Process process)
{
    lock (_processLock)
    {
        _pendingForceEscalationTarget = new PendingForceTarget(process.Id, TryGetStartTime(process));
    }
}

private static Process? TryReopenPendingTarget(PendingForceTarget target)
{
    try
    {
        var process = Process.GetProcessById(target.ProcessId);
        if (target.StartTime is { } expected && process.StartTime != expected)
        {
            process.Dispose();
            return null;
        }

        return process;
    }
    catch (ArgumentException) { return null; }
    catch (InvalidOperationException) { return null; }
    catch (System.ComponentModel.Win32Exception) { return null; }
}
```

Then have `ForceStopAsync` reopen the pending target, dispose that reopened handle after termination, and convert unavailable/recycled targets to `ForceKillFailed` or `AlreadyExited` as appropriate.

## Warnings

### WR-01: WARNING - Tests mask the production ownership/lifetime boundary

**File:** `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs:2457-2514`

**Issue:** The regression tests for post-finalization force escalation pass because they inject a sleeper `Process` owned by the test (`StartSleeperProcess`) and keep it alive until assertions complete. Production uses `ProcessExecutionService.ExecuteAsync`, which owns the `Process` with `using var` and disposes it before the delayed confirmation path runs. The tests therefore verify a different ownership model than the application actually uses and would not catch CR-01.

**Fix:** Add a regression test that exercises the real ownership boundary or a fake runner that disposes the process before `ForceStopCleaningAsync`. The assertion should prove confirmed force termination does not throw and returns an explicit terminal failure/success instead of reusing a disposed `Process` object. For example:

```csharp
[Fact]
public async Task ForceStopAsync_AfterPendingProcessObjectDisposed_DoesNotThrowAndDoesNotReuseDisposedHandle()
{
    using var process = StartSleeperProcess();
    _sut.AttachProcess(process);
    _processMock.TerminateProcessAsync(process, forceKill: false, Arg.Any<CancellationToken>())
        .Returns(TerminationResult.GracePeriodExpired);

    await _sut.StopAsync();
    _sut.DetachProcess();
    process.Dispose();

    var act = () => _sut.ForceStopAsync();

    await act.Should().NotThrowAsync();
    // Expected result depends on the production fix: ForceKillFailed if the target cannot be safely reopened,
    // or ForceKilled/AlreadyExited if it can be reacquired and validated by PID/start time.
}
```

---

_Reviewed: 2026-05-02T00:38:07Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: deep_
