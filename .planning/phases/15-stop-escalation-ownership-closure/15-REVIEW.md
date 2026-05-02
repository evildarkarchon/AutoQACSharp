---
phase: 15-stop-escalation-ownership-closure
reviewed: 2026-05-01T00:00:00Z
depth: deep
files_reviewed: 6
files_reviewed_list:
  - AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs
  - AutoQAC/Services/Cleaning/ICleaningTerminationCoordinator.cs
  - AutoQAC/Services/Cleaning/CleaningOrchestrator.cs
  - AutoQAC.Tests/Services/Cleaning/CleaningTerminationCoordinatorTests.cs
  - AutoQAC.Tests/Services/CleaningOrchestratorTests.cs
  - AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs
findings:
  critical: 2
  warning: 1
  info: 0
  total: 3
status: issues_found
---

# Phase 15: Code Review Report

**Reviewed:** 2026-05-01T00:00:00Z
**Depth:** deep
**Files Reviewed:** 6
**Status:** issues_found

## Summary

Reviewed the termination coordinator, orchestrator integration, and related service/view-model tests with cross-file tracing into DI registration, process termination semantics, and the Progress view-model command surface. The implementation still has correctness gaps around already-exited active processes and the UI path for second-click escalation; one test guard is also overbroad enough to block legitimate future async coordination work.

## Critical Issues

### CR-01: BLOCKER — Already-exited attached process returns stale/null termination state

**File:** `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs:143-173`

**Issue:** `StopAsync` checks `if (!proc.HasExited)` and only records a termination result inside that branch. If the attached xEdit process exits naturally after attachment but before the stop request is processed, execution falls through to lines 172-173 and returns `_lastTerminationResult` unchanged. That can report `null` instead of `AlreadyExited`, or a stale previous may-still-be-running value if this path is ever reached after an unresolved result. The coordinator should explicitly classify the observed attached process as exited and clear any pending force target.

**Fix:** Handle the `HasExited` case as a terminal termination result before falling through.

```csharp
if (proc != null)
{
    try
    {
        if (proc.Id == Environment.ProcessId)
        {
            _logger.Error(null, "[Termination] Refusing to terminate the AutoQAC process during stop request");
            _stateService.SetTerminating(false);
            return new StopCleaningResult(null, MayStillBeRunning: false);
        }

        if (!proc.HasExited)
        {
            var result = await _processService.TerminateProcessAsync(proc, forceKill: false, ct: CancellationToken.None)
                .ConfigureAwait(false);
            _lastTerminationResult = result;
            // existing result handling...
            return ToStopCleaningResult(result);
        }

        _lastTerminationResult = TerminationResult.AlreadyExited;
        ReleasePendingForceEscalationTarget();
        _stateService.SetTerminating(false);
        return ToStopCleaningResult(TerminationResult.AlreadyExited);
    }
    catch (InvalidOperationException)
    {
        _logger.Debug("[Termination] Process already exited during graceful stop");
        _lastTerminationResult = TerminationResult.AlreadyExited;
        ReleasePendingForceEscalationTarget();
        return ToStopCleaningResult(TerminationResult.AlreadyExited);
    }
}
```

### CR-02: BLOCKER — Tests codify a UI state that makes second-click force escalation unreachable

**File:** `AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs:776-795`

**Issue:** The project and coordinator contract require two-stage stop behavior where a second stop during the graceful termination window escalates directly to force kill. This test explicitly asserts that `StopCommand` is disabled while `IsTerminating` is true. Cross-file tracing shows `CleaningTerminationCoordinator.StopAsync` sets the terminating flag before awaiting graceful termination, so the Progress UI disables the only Stop command during the grace window. The service-level Path B exists, but the user-facing command path cannot invoke it.

**Fix:** Change the view-model command contract and tests so Stop remains executable while cleaning is active, or expose a separate force-stop action during termination. The regression test should assert the second command invocation reaches `ForceStopCleaningAsync` without a prompt.

```csharp
// Production direction:
private bool CanStop() => IsCleaning;

// Test direction:
_isTerminatingSubject.OnNext(true);
vm.StopCommand.CanExecute(null).Should().BeTrue(
    "the second stop click during grace must remain available for force escalation");
```

## Warnings

### WR-01: WARNING — Source guard forbids legitimate `Task.WhenAny` usage unrelated to parallel cleaning

**File:** `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs:2853-2871`

**Issue:** `Cleaning_Source_NoFileParallelizesPluginLoop` scans several cleaning service files and fails on any `Task.WhenAny(` token. `Task.WhenAny` is not inherently plugin-loop parallelization; it is commonly used for timeout, cancellation, and race coordination. This overbroad guard will create false failures if termination or backup code later uses a correct `Task.WhenAny` pattern while still preserving sequential xEdit cleaning.

**Fix:** Narrow the guard to constructs that actually parallelize plugin processing, or scan only the plugin loop/runner call sites. If source-level guards are retained, allow `Task.WhenAny` and rely on behavioral sequential tests for concurrency safety.

```csharp
var prohibitedTokens = new[]
{
    "Parallel.ForEach",
    "Parallel.ForEachAsync",
    "Task.WhenAll(",
    "Task.Run("
};
```

---

_Reviewed: 2026-05-01T00:00:00Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: deep_
