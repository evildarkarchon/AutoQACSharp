# T04: 08-cleaning-orchestrator-decomposition 04

**Slice:** S09 — **Milestone:** M001

## Description

Extract termination coordination into `ICleaningTerminationCoordinator`/`CleaningTerminationCoordinator` (D-08). HIGHEST RISK extraction — touches Phase 5 stop/force-stop locks. Coordinator owns `_currentProcess`, `_processLock`, `_isStopRequested`, `_lastTerminationResult`, hang-monitor subscription, and `_hangDetected` Subject.

Purpose: Third extraction. Must preserve Phase 5 invariants verbatim. Code is lifted move-by-move; behavior unchanged. Facade thins to delegation.

**Lambda hoisting (per R-04):** Wave 3 (THIS plan) keeps the orchestrator's foreach lambda inline; the lambda body just changes from `lock + StartHangMonitoring` private-state mutation to a single `terminationCoordinator.AttachProcess(proc)` call. Wave 4 (08-05) hoists the lambda into the runner via delegate parameters. The same code is not extracted twice.

**DI Subject lifetime note (R-11):** The `_hangDetected` Subject is now owned by `CleaningTerminationCoordinator.Dispose`. Any future ViewModel subscribing to `HangDetected` must treat the observable as having app-lifetime semantics (Singleton DI scope). The behavior is preserved from current code (Subject also lives in a Singleton today inside the orchestrator), but lifetime ownership is now explicit on a different Singleton.

Output: 2 new production files, 1 new test file, modified facade, modified DI. ~150 more lines removed from `CleaningOrchestrator.cs`.

## Must-Haves

- [ ] "Phase 5 two-stage stop/force-stop semantics preserved exactly — graceful CloseMainWindow with 2.5s grace, then force kill on second click; no log parse after unsafe termination."
- [ ] "Self-PID refusal preserved verbatim inside StopAsync and ForceStopAsync (Phase 5 INV-5.4)."
- [ ] "CancellationToken.None is passed to processService.TerminateProcessAsync — termination cleanup must not be abandoned by caller cancellation (Phase 5 lock at CleaningOrchestrator.cs:866, 933)."
- [ ] "D-08: ICleaningTerminationCoordinator owns _currentProcess, _processLock, _isStopRequested, _lastTerminationResult, _hangMonitorSubscription, _hangDetected Subject — current-process tracking, stop/force-stop escalation state, LastTerminationResult, hang-monitor lifecycle, and MayProcessStillBeRunning all migrate behind the orchestrator facade while preserving Phase 5 semantics."
- [ ] "Facade keeps _cleaningCts (session CTS) — termination coordinator does NOT own session CTS lifetime."
- [ ] "Facade.LastTerminationResult forwards from coordinator; Facade.HangDetected forwards from coordinator."
- [ ] "Facade.CancelBackupOperationAsync gates on terminationCoordinator.HasActiveProcess before delegating to backupCoordinator.CancelActiveOperationAsync."
- [ ] "ResetForNewSession resets all 5 fields from CleaningOrchestrator.cs:622-648 verbatim — stop flag (_isStopRequested = false), SetTerminating(false), last termination result (_lastTerminationResult = null), hang subscription dispose (_hangMonitorSubscription?.Dispose() + null), hang detected OnNext(false), AND _currentProcess = null under lock. Per R-03: omitting SetTerminating(false) leaks 'terminating' UI state across sessions; omitting _hangDetected.OnNext(false) leaves stale hang flag (Subject is on a Singleton); both are user-visible D-09 violations."
- [ ] "Wave 3 (THIS plan) keeps the orchestrator's existing inline foreach lambda in place — it just rewrites the lambda body to call terminationCoordinator.AttachProcess(proc) instead of mutating private fields directly. The lambda→delegate parameter conversion happens ONCE in Wave 4 (08-05). Per R-04: same code is not extracted twice across waves."
- [ ] "ICleaningOrchestrator public surface unchanged (Wave 0 snapshot test green)."
- [ ] "Sequential xEdit cleaning preserved — `ProcessExecutionService` single-slot semaphore stays in the launch path."
- [ ] "D-11 honored — if a non-REF-01 bug is found during extraction, executor MUST stop and ask, not silently fix."

## Files

- `AutoQAC/Services/Cleaning/ICleaningTerminationCoordinator.cs`
- `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs`
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC.Tests/Services/Cleaning/CleaningTerminationCoordinatorTests.cs`
