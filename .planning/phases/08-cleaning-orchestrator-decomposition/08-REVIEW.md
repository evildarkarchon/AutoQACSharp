---
phase: 08-cleaning-orchestrator-decomposition
reviewed: 2026-04-29T00:00:00Z
depth: standard
files_reviewed: 21
files_reviewed_list:
  - AutoQAC.Tests/Services/CleaningOrchestratorTests.cs
  - AutoQAC/Services/Cleaning/CleaningOrchestrator.cs
  - AutoQAC/Services/Cleaning/ICleaningPreflight.cs
  - AutoQAC/Services/Cleaning/CleaningPreflight.cs
  - AutoQAC/Services/Cleaning/CleaningPreflightModels.cs
  - AutoQAC/Infrastructure/ServiceCollectionExtensions.cs
  - AutoQAC.Tests/Services/Cleaning/CleaningPreflightTests.cs
  - AutoQAC/Services/Cleaning/IBackupSessionCoordinator.cs
  - AutoQAC/Services/Cleaning/BackupSessionCoordinator.cs
  - AutoQAC/Services/Cleaning/BackupSessionModels.cs
  - AutoQAC.Tests/Services/Cleaning/BackupSessionCoordinatorTests.cs
  - AutoQAC/Services/Cleaning/ICleaningTerminationCoordinator.cs
  - AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs
  - AutoQAC.Tests/Services/Cleaning/CleaningTerminationCoordinatorTests.cs
  - AutoQAC/Services/Cleaning/IPluginCleaningRunner.cs
  - AutoQAC/Services/Cleaning/PluginCleaningRunner.cs
  - AutoQAC/Services/Cleaning/IPluginResultFinalizer.cs
  - AutoQAC/Services/Cleaning/PluginResultFinalizer.cs
  - AutoQAC/Services/Cleaning/RunnerFinalizerModels.cs
  - AutoQAC.Tests/Services/Cleaning/PluginCleaningRunnerTests.cs
  - AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs
findings:
  critical: 2
  warning: 1
  info: 0
  total: 3
status: issues_found
---

# Phase 08: Code Review Report

**Reviewed:** 2026-04-29T00:00:00Z
**Depth:** standard
**Files Reviewed:** 21
**Status:** issues_found

## Summary

Standard review of the Phase 08 cleaning decomposition found two BLOCKER correctness risks and one WARNING-level test reliability defect. The main production risks are that the public cleaning facade can be entered concurrently, violating the one-xEdit-at-a-time invariant, and that a game detected as a file-load-order title after preflight validation can bypass the required load-order path check.

## Critical Issues

### CR-01: Concurrent StartCleaningAsync calls can run overlapping sessions

**Classification:** BLOCKER
**File:** `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:42-63,260-264`
**Issue:** `StartCleaningAsync` has no in-flight session guard. A second caller can enter while the first session is still running; `CreateSessionCts` then overwrites `_cleaningCts`, so `StopCleaningAsync` only cancels the newest session while both loops can continue to call `runner.RunAsync`. This violates the project’s hard sequential xEdit requirement and can corrupt state/backup accounting.
**Fix:** Add an interlocked or semaphore-based session gate around the whole workflow and reject or no-op a second start before creating a new CTS. Add a regression test that starts one blocked cleaning session, invokes `StartCleaningAsync` again, and verifies the second call is rejected and no second `CleanPluginAsync` begins.

```csharp
private readonly SemaphoreSlim _sessionGate = new(1, 1);

public async Task StartCleaningAsync(
    TimeoutRetryCallback? onTimeout,
    BackupFailureCallback? onBackupFailure,
    CancellationToken ct = default)
{
    if (!await _sessionGate.WaitAsync(0, ct).ConfigureAwait(false))
    {
        throw new InvalidOperationException("A cleaning session is already running.");
    }

    try
    {
        // existing workflow
    }
    finally
    {
        _sessionGate.Release();
    }
}
```

### CR-02: Unknown game detection can bypass required load-order validation for file-based games

**Classification:** BLOCKER
**File:** `AutoQAC/Services/Cleaning/CleaningPreflight.cs:38-74,242-249`
**Issue:** `ValidateConfigurationAsync` runs before unknown-game detection and checks `RequiresFileLoadOrder(config.CurrentGameType)`. If `CurrentGameType` starts as `Unknown`, executable detection can later set `gameType` to `Fallout3`, `FalloutNewVegas`, or `Oblivion` with a missing/nonexistent `LoadOrderPath`, but no second validation runs. Those games are documented as file-load-order based, so this can launch cleaning with an invalid environment.
**Fix:** Re-validate file-load-order requirements after detection, using the detected `gameType`, before building plugin rows or starting cleaning. Add a test for `CurrentGameType = Unknown`, executable detection returning `Fallout3`, and `LoadOrderPath = null`/missing.

```csharp
// After gameType detection succeeds, before variant/skip-list work.
if (RequiresFileLoadOrder(gameType) &&
    (string.IsNullOrWhiteSpace(config.LoadOrderPath) || !File.Exists(config.LoadOrderPath)))
{
    logger.Error(null, "Configuration is invalid, cannot start cleaning.");
    throw new InvalidOperationException("Configuration is invalid");
}
```

## Warnings

### WR-01: Source guard checks a comment instead of the real backup-before-runner call order

**Classification:** WARNING
**File:** `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs:2574-2576`
**Issue:** `CleaningOrchestrator_Source_DoesNotParallelizePluginCleaning` asserts that the first occurrence of `RunPluginBackupAsync` appears before `runner.RunAsync`. The first occurrence is currently a comment in `CleaningOrchestrator.cs:152`, not the executable backup call inside `HandleBackupOutcomeAsync`. The guard can pass even if backup execution is accidentally moved after the runner, so it no longer reliably protects the backup-before-xEdit invariant.
**Fix:** Assert on executable code that cannot be satisfied by comments, or strip comments before token checks. For example, compare the local helper invocation before `runner.RunAsync` and separately test the coordinator helper contains the `RunPluginBackupAsync` call.

```csharp
source.IndexOf("HandleBackupOutcomeAsync(plugin", StringComparison.Ordinal)
    .Should().BeLessThan(
        source.IndexOf("runner.RunAsync", StringComparison.Ordinal),
        "backup handling must remain before xEdit cleaning");
```

---

_Reviewed: 2026-04-29T00:00:00Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: standard_
