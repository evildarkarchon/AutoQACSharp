---
phase: 01-foundation-hardening
plan: 01
subsystem: process-management
tags: [process-termination, pid-tracking, orphan-cleanup, cancellation, cts-race-fix]

dependency_graph:
  requires: []
  provides:
    - "Escalating process termination (graceful -> grace period -> force kill tree)"
    - "PID file tracking for orphan detection across crashes"
    - "Race-safe CancellationTokenSource handling in CleaningOrchestrator"
    - "Dual-path stop escalation (patient user path A + impatient user path B)"
    - "Partial result preservation on cancellation"
  affects:
    - "01-02 (state deadlock fix may interact with OperationCanceledException handling)"
    - "Future UI plan for Stopping... spinner and force kill confirmation dialog"

tech_stack:
  added: []
  patterns:
    - "WaitForExitAsync instead of Exited event (workaround for .NET Kill(true) bug)"
    - "PID file tracking with JSON serialization for crash recovery"
    - "Lock-capture-then-cancel pattern for CTS race safety"
    - "Semaphore timeout safety net with 10s warning + 60s hard timeout"
    - "onProcessStarted callback for process reference wiring"

key_files:
  created:
    - "AutoQAC/Models/TerminationResult.cs"
    - "AutoQAC/Models/TrackedProcess.cs"
  modified:
    - "AutoQAC/Services/Process/IProcessExecutionService.cs"
    - "AutoQAC/Services/Process/ProcessExecutionService.cs"
    - "AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs"
    - "AutoQAC/Services/Cleaning/CleaningOrchestrator.cs"
    - "AutoQAC/ViewModels/MainWindowViewModel.cs"
    - "AutoQAC/ViewModels/ProgressViewModel.cs"
    - "AutoQAC.Tests/Services/CleaningOrchestratorTests.cs"
    - "AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs"
    - "AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs"

decisions:
  - id: "grace-period-2500ms"
    description: "Grace period set to 2500ms (2.5s) as recommended in RESEARCH.md"
    rationale: "Sweet spot between xEdit typical shutdown time (1-2s) and user patience"
  - id: "close-main-window-false-escalation"
    description: "When CloseMainWindow returns false (no window handle), immediately return GracePeriodExpired"
    rationale: "xEdit launched with CreateNoWindow=true may not have a window; skipping grace period avoids pointless wait"
  - id: "auto-force-on-grace-expire"
    description: "ViewModel auto-escalates to ForceStopCleaningAsync when LastTerminationResult=GracePeriodExpired (placeholder for future confirmation dialog)"
    rationale: "Confirmation dialog deferred to Stopping... spinner UI plan; current behavior is safe"

metrics:
  duration: "7 minutes"
  completed: "2026-02-06"
  tasks: 4
  tests_affected: 3
  tests_total: 428
  tests_passing: 428
---

# Phase 1 Plan 1: Process Termination Hardening Summary

**One-liner:** Escalating process termination (graceful + force kill tree) with PID-tracked orphan cleanup and race-safe CTS cancellation.

## What Was Done

### Task 1a+1b: TerminationResult, TrackedProcess, Hardened ProcessExecutionService
- Created `TerminationResult` enum: `AlreadyExited`, `GracefulExit`, `GracePeriodExpired`, `ForceKilled`
- Created `TrackedProcess` record for PID file entries (`Pid`, `StartTime`, `PluginName`)
- Replaced the `TaskCompletionSource` + `process.Exited` event pattern with `process.WaitForExitAsync()` (workaround for known .NET bug with `Kill(entireProcessTree: true)`)
- Implemented `TerminateProcessAsync` with two paths:
  - Graceful (forceKill=false): `CloseMainWindow()` + 2.5s grace period via `WaitForExitAsync`
  - Force (forceKill=true): `Process.Kill(entireProcessTree: true)` + `WaitForExitAsync`
- Added PID tracking to `autoqac-pids.json` via `TrackProcessAsync`/`UntrackProcessAsync`
- Added `CleanOrphanedProcessesAsync` with dual validation: process name check (against known xEdit names) + start time proximity (5s window)
- Added 60-second semaphore timeout safety net with 10-second warning log
- Ensured semaphore release in `finally` block to prevent deadlock
- Added `onProcessStarted` callback and `pluginName` parameters to `ExecuteAsync`

### Task 2: CTS Race Fix and Dual-Path Escalation Stop
- Changed `StopCleaning()` to `StopCleaningAsync()` with race-safe CTS pattern (capture reference inside lock, cancel outside)
- Added `ForceStopCleaningAsync()` for immediate process tree kill
- Implemented dual-path escalation:
  - **Path A (patient user):** First click cancels CTS + attempts graceful termination. If grace period expires, `LastTerminationResult = GracePeriodExpired` signals ViewModel to prompt user.
  - **Path B (impatient user):** Second click during grace period immediately calls `ForceStopCleaningAsync()` (no prompt).
- Added orphan cleanup call at the start of `StartCleaningAsync`
- Added separate `catch (OperationCanceledException)` to preserve partial results with `WasCancelled = true`
- Added `IProcessExecutionService` as constructor dependency
- Reset `_isStopRequested` and `_lastTerminationResult` at session start and in `finally` block

### Task 3: All Callers and Tests Updated
- `MainWindowViewModel.StopCleaningCommand` now uses `ReactiveCommand.CreateFromTask(HandleStopAsync)` with dual-path escalation logic (auto-force-kills as placeholder for future confirmation dialog)
- `ProgressViewModel.StopCommand` now calls `StopCleaningAsync()`
- Updated 3 test files:
  - `CleaningOrchestratorTests`: added `IProcessExecutionService` mock, `StopCleaning()` -> `StopCleaningAsync()`
  - `MainWindowViewModelTests`: updated `StopCleaningCommand` test for async interface
  - `ProgressViewModelTests`: updated `StopCommand` test for async interface
- All 428 tests passing

## Decisions Made

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Grace period duration | 2500ms | Sweet spot: long enough for xEdit cleanup (1-2s typical), short enough user doesn't feel stuck |
| CloseMainWindow false handling | Return GracePeriodExpired immediately | No window = no graceful exit possible; skip pointless wait |
| Path A confirmation dialog | Auto-escalate (placeholder) | Full dialog deferred to Stopping... spinner UI plan |
| PID file location | Same `AutoQAC Data/` dir as config | Consistent with ConfigurationService, survives app crashes |
| Orphan validation | Name + StartTime check | Prevents false matches from PID reuse |
| Semaphore timeout | 60s hard, 10s warning | Safety net against deadlock without being too aggressive |

## Deviations from Plan

None -- plan executed exactly as written.

## Task Commits

| Task | Name | Commit | Key Files |
|------|------|--------|-----------|
| 1a+1b | Harden ProcessExecutionService | 63711d5 | TerminationResult.cs, TrackedProcess.cs, IProcessExecutionService.cs, ProcessExecutionService.cs |
| 2 | Fix CTS race + dual-path stop | 6d52675 | ICleaningOrchestrator.cs, CleaningOrchestrator.cs |
| 3 | Update callers and tests | 5c9303c | MainWindowViewModel.cs, ProgressViewModel.cs, 3 test files |

## Verification Results

1. `dotnet build` -- 0 errors, 0 warnings
2. `dotnet test` -- 428 passed, 0 failed, 0 skipped
3. No `TaskCompletionSource` or `process.Exited` in ProcessExecutionService
4. `Process.Kill(entireProcessTree: true)` used in 2 locations (TerminateProcessAsync force path + CleanOrphanedProcessesAsync)
5. PID tracking via `autoqac-pids.json` with read/write in Track/Untrack methods
6. CTS race fix: `StopCleaningAsync` captures reference inside lock, calls `Cancel()` outside
7. Dual-path escalation verified: `_isStopRequested` flag toggles Path A vs Path B
8. `OperationCanceledException` caught separately with partial result preservation
9. No `ObjectDisposedException` possible: captured reference keeps CTS alive, catch block as fallback
10. Restart-after-cancel safe: UntrackProcessAsync in finally, semaphore in finally, CTS disposed+nulled in finally, stop flags reset in finally

## Next Phase Readiness

**Blockers:** None

**Notes for Plan 01-02:**
- The `Stopping...` spinner UI and force kill confirmation dialog (Path A prompt) are deferred
- Currently `HandleStopAsync` in MainWindowViewModel auto-escalates to force kill when grace period expires
- The `TODO(01-02)` comment marks where the dialog should be wired

## Self-Check: PASSED
