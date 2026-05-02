---
phase: 15-stop-escalation-ownership-closure
verified: 2026-05-02T00:29:17Z
status: gaps_found
score: 15/19 must-haves verified
overrides_applied: 0
gaps:
  - truth: "Progress-window Stop cannot lose the xEdit force-termination target between GracePeriodExpired and the user's Force Terminate choice, even if process execution has returned."
    status: failed
    reason: "The coordinator retains the disposable Process object from ProcessExecutionService.ExecuteAsync, but ExecuteAsync creates it with using var and disposes it when the method returns. Confirmed force escalation after that point uses a disposed wrapper rather than stable process identity, so it may not kill the still-running process."
    artifacts:
      - path: "AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs"
        issue: "Retains System.Diagnostics.Process in _pendingForceEscalationProcess and later passes that same wrapper to force termination."
      - path: "AutoQAC/Services/Process/ProcessExecutionService.cs"
        issue: "Creates the process with using var; the wrapper is disposed once ExecuteAsync returns after GracePeriodExpired."
    missing:
      - "Retain stable force target identity (PID plus start time or equivalent safe ownership) and reopen/validate it at confirmed force time, or otherwise guarantee the retained process wrapper remains usable after ExecuteAsync returns."
      - "Add a regression test that goes through the real ProcessExecutionService.ExecuteAsync ownership path or explicitly disposes the retained wrapper before ForceStopAsync."
  - truth: "If AutoQAC cannot force terminate after confirmation, the user sees the shared safe force-failure dialog and persistent Progress summary warning."
    status: failed
    reason: "The UI path is wired for ForceKillFailed, but the disposed-wrapper production path can be misclassified before that outcome is produced; a still-running process may be reported as AlreadyExited or otherwise not surface ForceKillFailed."
    artifacts:
      - path: "AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs"
        issue: "Accepts terminal results from TerminateProcessAsync against the retained wrapper without proving the target was killed/reopened after ExecuteAsync disposed it."
      - path: "AutoQAC/ViewModels/ProgressViewModel.cs"
        issue: "Correctly shows the warning only when ForceKillFailed is returned; it cannot compensate if the service misreports the outcome."
    missing:
      - "Map disposed/unavailable/unprovable confirmed pending-target paths to ForceKillFailed unless the original target is proven exited."
  - truth: "Automated tests cover GracePeriodExpired followed by runner detach/process execution return and confirmed Force Terminate using production ownership."
    status: failed
    reason: "Current tests use test-owned or mocked Process instances that remain valid through ForceStopAsync; they do not exercise the using-var Process wrapper disposal in ProcessExecutionService.ExecuteAsync."
    artifacts:
      - path: "AutoQAC.Tests/Services/Cleaning/CleaningTerminationCoordinatorTests.cs"
        issue: "Detached force tests attach a test-owned process and do not dispose it before ForceStopAsync."
      - path: "AutoQAC.Tests/Services/CleaningOrchestratorTests.cs"
        issue: "Orchestrator detached force test injects a test-owned Process through a mocked cleaning service instead of the real ExecuteAsync ownership path."
    missing:
      - "Add production-ownership coverage for GracePeriodExpired -> ExecuteAsync return/dispose -> runner detach/finalization -> confirmed ForceStopCleaningAsync."
human_verification:
  - test: "Product decision: should Progress Stop remain disabled while IsTerminating is true?"
    expected: "Either document that the dialog-confirmation flow is the only supported Progress force-escalation path, or keep Stop executable during active cleaning so the coordinator's documented second-stop escalation path is reachable from UI."
    why_human: "This affects intended UX semantics beyond the Phase 15 confirmed-dialog goal; code review CR-02 is verified in code, but whether it blocks this phase requires product intent."
---

# Phase 15: Stop Escalation Ownership Closure Verification Report

**Phase Goal:** Users can confirm force termination after graceful stop expires and receive either actual force termination or a safe persistent force-failure warning, even if process execution has returned.
**Verified:** 2026-05-02T00:29:17Z
**Status:** gaps_found
**Re-verification:** No — initial goal-backward verification after code review evidence

## Goal Achievement

Phase 15 is **not achieved**. The implementation satisfies several local coordinator/UI behaviors, and the targeted tests pass, but goal-backward verification found the same production ownership hole described by review CR-01: the retained pending target is the `Process` wrapper created inside `ProcessExecutionService.ExecuteAsync`, and that wrapper is disposed when `ExecuteAsync` returns. That directly violates the phase goal's "even if process execution has returned" clause.

Review CR-02 is also confirmed in code (`CanStop() => IsCleaning && !IsTerminating`), but it is treated as a human/product-decision warning rather than a phase-goal blocker because Phase 15's roadmap goal is the confirmation-dialog path after `GracePeriodExpired`, not necessarily the coordinator's historical second-click shortcut.

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Progress-window Stop cannot lose the xEdit force-termination target between `GracePeriodExpired` and the user's `Force Terminate` choice, even if process execution has returned. | ✗ FAILED | `ProcessExecutionService.ExecuteAsync` creates `using var process` at line 58 and invokes `onProcessStarted` at line 101; `CleaningTerminationCoordinator` stores that same wrapper in `_pendingForceEscalationProcess` at lines 32, 150, and 326. Once `ExecuteAsync` returns, the wrapper is disposed while the OS process may remain running. |
| 2 | If AutoQAC cannot force terminate after confirmation, the user sees the shared safe force-failure dialog and persistent Progress summary warning. | ✗ FAILED | `ProgressViewModel` correctly shows the shared warning for `ForceKillFailed` at lines 275-293, but the production disposed-wrapper path can fail before a truthful `ForceKillFailed` is produced. The service must first classify the force outcome correctly. |
| 3 | Automated tests cover `GracePeriodExpired` followed by runner detach and confirmed `Force Terminate`, proving actual force termination or explicit failure reporting. | ✗ FAILED | Current coordinator/orchestrator tests cover retained test-owned processes (`CleaningTerminationCoordinatorTests.cs:237-339`, `CleaningOrchestratorTests.cs:2457-2515`) but do not cover the real `ExecuteAsync` owner disposing the `Process` wrapper. |
| 4 | Current verification artifacts prove SAF-01, SAF-02, and TEST-01 are satisfied after the ownership fix. | ✗ FAILED | The previous `15-VERIFICATION.md` asserted pass from test rows, but those tests do not prove production ownership after `ExecuteAsync` returns. This report supersedes it with gaps. |
| 5 | Coordinator retains a separate pending force-escalation target after `GracePeriodExpired` without counting it as active cleaning. | ✓ VERIFIED | `_pendingForceEscalationProcess` is distinct from `_currentProcess` (`CleaningTerminationCoordinator.cs:31-32`); `HasActiveProcess` checks only `_currentProcess` (`65-74`); `DetachProcess` clears only `_currentProcess` (`101-105`). |
| 6 | Active cleaning semantics remain separate from pending escalation ownership. | ✓ VERIFIED | Hang monitoring is disposed and emits false during detach/finalization (`96-105`, `291-304`), while `HasActiveProcess` remains tied to `_currentProcess`. |
| 7 | PID/start-time fallback is not part of the implemented primary path. | ✓ VERIFIED | No pending-target PID/start-time record exists; implementation uses a retained `Process` reference. This matches the original plan decision but is the root of the production gap above. |
| 8 | Confirmed `ForceStopAsync` after detach does not return cached `GracePeriodExpired`. | ✓ VERIFIED | `ForceStopAsync` selects `_pendingForceEscalationProcess` when the last result may still be running (`179-188`) and maps missing target to `ForceKillFailed` (`234-238`) rather than returning cached `GracePeriodExpired`. |
| 9 | Already-exited retained targets return `AlreadyExited`; unavailable/unprovable targets return `ForceKillFailed`. | ✗ FAILED | Unit paths exist, but the disposed production wrapper can be interpreted as already exited/unassociated rather than reopened/proven; unavailable/unprovable target handling is not robust for the real owner path. |
| 10 | Controlled helper-process coordinator proof covers detach then `ForceStopAsync`. | ✓ VERIFIED | `ForceStopAsync_AfterGracePeriodExpiredAndDetach_ForceKillsPendingProcess` exists at `CleaningTerminationCoordinatorTests.cs:237-267`. |
| 11 | Tests use existing helper-process/service/ViewModel patterns without Avalonia.Headless or real xEdit/MO2. | ✓ VERIFIED | Tests use xUnit, NSubstitute, helper sleeper processes, and synchronous dispatcher; no Avalonia.Headless project was introduced. |
| 12 | Normal session finalization preserves unresolved `GracePeriodExpired` pending target until user resolution. | ✓ VERIFIED | `CleaningOrchestrator` calls `CompleteSessionFinalization()` in `finally` (`CleaningOrchestrator.cs:133-137`); coordinator preserves pending target when `_lastTerminationResult == GracePeriodExpired` (`CleaningTerminationCoordinator.cs:299-303`). |
| 13 | Progress Stop does not call `ForceStopCleaningAsync` before affirmative confirmation. | ✓ VERIFIED | `ProgressViewModel.StopAsync` calls `ShowChoiceAsync` before `ForceStopCleaningAsync` (`220-228`); test `StopCommand_WhenGracePeriodExpires_ShouldPromptBeforeForceStop` asserts `DidNotReceive().ForceStopCleaningAsync()` while the dialog task is pending (`ProgressViewModelTests.cs:110-146`). |
| 14 | Confirmed `ForceKillFailed` displays shared safe copy and persistent warning. | ✓ VERIFIED | `ShowForceFailureDialogSafelyAsync` sets `StopOutcomeWarningText` and calls `ShowErrorAsync` with `StopTerminationDialogContent.ForceFailureTitle/ForceFailureMessage` (`ProgressViewModel.cs:286-293`); test exists at `ProgressViewModelTests.cs:177-207`. |
| 15 | `15-VERIFICATION.md` exists and maps the required IDs. | ✓ VERIFIED | This report maps SAF-01, SAF-02, TEST-01 and the audit gap IDs in the requirements table below. |
| 16 | Verification evidence is concise and does not paste long passing outputs. | ✓ VERIFIED | This report records command/grep evidence and concise result rows only. |
| 17 | Phase 15 does not perform Phase 16 marker reconciliation. | ✓ VERIFIED | No roadmap/requirements marker edits are part of this verification; Phase 16 remains responsible for reconciliation. |
| 18 | Start-of-next-session reset clears stale pending state. | ✓ VERIFIED | `ResetForNewSession` clears `_pendingForceEscalationProcess` and `_lastTerminationResult` (`CleaningTerminationCoordinator.cs:253-280`); tests sample cleared state at session 2 start (`CleaningOrchestratorTests.cs:2523-2594`). |
| 19 | Targeted Phase 15 tests still pass. | ✓ VERIFIED | Ran `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningTerminationCoordinatorTests\|FullyQualifiedName~CleaningOrchestratorTests\|FullyQualifiedName~ProgressViewModelTests\|FullyQualifiedName~ProcessExecutionIntegrationTests" --nologo`: 111 passed, 0 failed. Passing tests do not cover the production ownership gap. |

**Score:** 15/19 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs` | Pending force target ownership and terminal force-stop mapping | ✗ HOLLOW for production owner | Exists and is substantive/wired, but retains a disposable `Process` wrapper rather than stable process identity. |
| `AutoQAC/Services/Cleaning/ICleaningTerminationCoordinator.cs` | Documented reset/finalization lifetime contract | ✓ VERIFIED | Documents unresolved `GracePeriodExpired` target survival and finalization/reset split (`22-55`). |
| `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` | Distinct start reset vs finalization cleanup | ✓ VERIFIED | Calls `ResetForNewSession()` at start (`51-52`) and `CompleteSessionFinalization()` in finally (`133-137`). |
| `AutoQAC/ViewModels/ProgressViewModel.cs` | Confirmation-before-force and shared failure UI | ⚠️ PARTIAL | Confirmation and ForceKillFailed UI are wired, but `CanStop()` disables Stop while terminating (`205`), and UI depends on service returning truthful `ForceKillFailed`. |
| `AutoQAC.Tests/Services/Cleaning/CleaningTerminationCoordinatorTests.cs` | Coordinator detached escalation tests | ⚠️ PARTIAL | Tests exist and pass, but use valid test-owned process wrappers rather than production disposed wrappers. |
| `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs` | Orchestrator finalization/lifetime tests | ⚠️ PARTIAL | Test exists and passes, but injects a test-owned process through mocked cleaning service rather than `ProcessExecutionService.ExecuteAsync`. |
| `AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs` | Confirmation and failure visibility tests | ✓ VERIFIED | Tests confirm prompt ordering and shared warning when `ForceKillFailed` is returned. |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `ProcessExecutionService.ExecuteAsync` | `CleaningTerminationCoordinator.AttachProcess` | `onProcessStarted?.Invoke(process)` | ⚠️ PARTIAL | The link passes a process wrapper owned by `using var process`; ownership is not safe after `ExecuteAsync` returns. |
| `CleaningTerminationCoordinator.StopAsync` | `CleaningTerminationCoordinator.ForceStopAsync` | `_pendingForceEscalationProcess` after `GracePeriodExpired` | ⚠️ PARTIAL | In-memory link exists, but the retained object can be disposed by the real process execution owner. |
| `CleaningOrchestrator.StartCleaningAsync finally` | coordinator pending target | `CompleteSessionFinalization()` | ✓ WIRED | Finalization preserves unresolved pending target state. |
| `ProgressViewModel.StopAsync` | `ICleaningOrchestrator.ForceStopCleaningAsync` | `ShowChoiceAsync` affirmative result | ✓ WIRED | Force is called only after `MessageDialogResult.Yes`. |
| `ForceKillFailed` service result | Progress safe warning UI | `ReportForceStopFailureIfNeededAsync` | ✓ WIRED | Shared dialog and persistent warning are used when the result is actually `ForceKillFailed`. |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|--------------------|--------|
| `CleaningTerminationCoordinator.cs` | `_pendingForceEscalationProcess` | `StopAsync()` retains `proc` from `_currentProcess` | No, not after `ExecuteAsync` disposal | ⚠️ HOLLOW — wired but unsafe ownership |
| `ProgressViewModel.cs` | `terminationResult` | `StopCleaningAsync()` then `ForceStopCleaningAsync()` | Yes if service result is truthful | ⚠️ DEPENDS ON FAILED SERVICE OUTCOME |
| `CleaningOrchestrator.cs` | `LastTerminationResult` / pending target lifetime | coordinator reset/finalization calls | Yes for state preservation | ✓ FLOWING |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Targeted Phase 15 tests | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningTerminationCoordinatorTests\|FullyQualifiedName~CleaningOrchestratorTests\|FullyQualifiedName~ProgressViewModelTests\|FullyQualifiedName~ProcessExecutionIntegrationTests" --nologo` | 111 passed, 0 failed | ✓ PASS (coverage gap remains) |
| Disposed process wrapper cannot be force-killed directly | PowerShell started a sleeper process, disposed its `Process` wrapper, then called `Kill(true)` on the disposed wrapper | `KillException=... "No process is associated with this object."`; cleanup required reopening by PID | ✗ FAIL for retained-wrapper ownership |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| SAF-01 | 15-01, 15-02, 15-03 | User can stop cleaning without AutoQAC force-killing xEdit before the confirmation path is shown. | ✗ BLOCKED | Confirmation-before-force is verified in `ProgressViewModel`, but the overall stop flow can lose/mis-handle the target after confirmation when `ExecuteAsync` has returned. |
| SAF-02 | 15-01, 15-02, 15-03 | User can see an accurate failure outcome when force-killing xEdit fails. | ✗ BLOCKED | UI displays the safe warning for `ForceKillFailed`, but the service may not produce an accurate failure outcome for a disposed pending target. |
| TEST-01 | 15-01, 15-02, 15-03 | Maintainer can verify real child-process timeout, graceful stop, force kill, and PID cleanup behavior through controlled integration tests. | ✗ BLOCKED | Existing process integration tests cover ProcessExecutionService behaviors, but no test covers the cross-service returned/disposed process ownership path central to Phase 15. |
| INT-STOP-01 | 15 plans / audit gap | Integration gap for detached stop escalation ownership. | ✗ BLOCKED | Detached integration proof is mocked/test-owned and misses production ProcessExecutionService ownership. |
| FLOW-STOP-ESCALATION-01 | 15 plans / audit gap | Flow gap for Stop -> graceful expiry -> confirmed Force Terminate -> accurate outcome. | ✗ BLOCKED | The confirmed flow can produce neither actual force termination nor safe persistent warning if the pending target wrapper is disposed and misclassified. |

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `AutoQAC/Services/Process/ProcessExecutionService.cs` | 58 | `using var process` passed outside via callback | 🛑 Blocker | The coordinator stores a wrapper whose lifetime ends before delayed user confirmation. |
| `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs` | 32, 326 | Retained pending target is a `Process` object, not stable identity | 🛑 Blocker | Cannot guarantee force kill or safe failure once the original owner disposes the wrapper. |
| `AutoQAC.Tests/Services/Cleaning/CleaningTerminationCoordinatorTests.cs` | 237-339 | Test-owned retained process | ⚠️ Warning | Tests prove only the easy path where the wrapper remains valid. |
| `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs` | 2457-2515 | Mocked cleaning service injects test-owned process | ⚠️ Warning | Does not exercise `ProcessExecutionService.ExecuteAsync` returning/disposal. |
| `AutoQAC/ViewModels/ProgressViewModel.cs` | 205 | Stop disabled while `IsTerminating` | ⚠️ Warning | Confirms review CR-02; requires product decision for second-click escalation semantics. |

### Human Verification Required

### 1. Product decision: Progress Stop second-click behavior

**Test:** Decide whether Progress Stop must remain executable while `IsTerminating` is true so a second click can invoke the coordinator's documented immediate force escalation path.
**Expected:** Either document dialog confirmation as the only Progress force-escalation path, or change `CanStop()`/tests so Stop remains executable during active cleaning with reentrancy guarded separately.
**Why human:** This is UX/product intent. It is code-review critical CR-02, and code evidence confirms the behavior, but it is not clearly required by the Phase 15 confirmed-dialog goal.

### Gaps Summary

The phase goal is blocked by one root cause: pending force-escalation ownership is retained as a disposable `Process` wrapper, while the production owner (`ProcessExecutionService.ExecuteAsync`) disposes that wrapper when process execution returns. Because Phase 15 explicitly promises correct behavior **even if process execution has returned**, this is a blocker. The current tests pass because they avoid that ownership path; they keep test-owned process wrappers valid through `ForceStopAsync` or mock the process owner.

Fix direction: retain and validate stable target identity (PID plus start time or equivalent), reopen the target at confirmed force time, prove `AlreadyExited` only when the original target is actually gone, and otherwise return `ForceKillFailed` so the already-wired Progress warning path runs.

---

_Verified: 2026-05-02T00:29:17Z_
_Verifier: the agent (gsd-verifier)_
