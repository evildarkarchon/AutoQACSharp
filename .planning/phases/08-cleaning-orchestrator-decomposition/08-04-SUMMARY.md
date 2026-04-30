---
phase: 08-cleaning-orchestrator-decomposition
plan: 04
subsystem: cleaning
tags: [refactor, cleaning, termination, phase5-locks, tdd]

# Dependency graph
requires:
  - phase: 08-cleaning-orchestrator-decomposition
    provides: Backup session coordinator seam and Wave 0 Phase 5 characterization guards
provides:
  - ICleaningTerminationCoordinator contract for xEdit process attachment, stop/force-stop, reset, and hang state
  - CleaningTerminationCoordinator implementation owning active process, process lock, stop flag, last termination result, hang subscription, and hang subject
  - Coordinator-level tests for Phase 5 termination invariants and ResetForNewSession UI-state clearing
affects: [cleaning-orchestrator-decomposition, REF-01, termination-coordination, phase5-stop-semantics]

# Tech tracking
tech-stack:
  added: []
  patterns: [stateful singleton coordinator, thin facade delegation, observable forwarding, Phase 5 invariant tests]

key-files:
  created:
    - AutoQAC/Services/Cleaning/ICleaningTerminationCoordinator.cs
    - AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs
    - AutoQAC.Tests/Services/Cleaning/CleaningTerminationCoordinatorTests.cs
  modified:
    - AutoQAC/Services/Cleaning/CleaningOrchestrator.cs
    - AutoQAC/Infrastructure/ServiceCollectionExtensions.cs
    - AutoQAC.Tests/Services/CleaningOrchestratorTests.cs
    - AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs

key-decisions:
  - "CleaningTerminationCoordinator owns all active process, stop escalation, termination result, and hang observable state while CleaningOrchestrator retains only session CTS lifetime."
  - "CleaningOrchestrator remains the Stop/ForceStop facade that cancels the session CTS first, then delegates process termination to the coordinator."
  - "Wave 3 keeps the per-plugin onProcessStarted lambda inline and changes only its body to call terminationCoordinator.AttachProcess; delegate hoisting remains deferred to 08-05."

patterns-established:
  - "Phase 5 stop invariants are pinned in collaborator tests before implementation and still covered by existing CleaningOrchestrator tests."
  - "ResetForNewSession centralizes all five reset actions: stop flag, terminating UI flag, last result, hang monitor false emission, and current process nulling under lock."

requirements-completed: [REF-01]

# Metrics
duration: 6 min
completed: 2026-04-30
---

# Phase 08 Plan 04: Cleaning Termination Coordinator Extraction Summary

**xEdit termination coordination extracted into a focused coordinator preserving Phase 5 two-stage stop, self-PID refusal, CancellationToken.None cleanup, and hang-state reset semantics.**

## Performance

- **Duration:** 6 min
- **Started:** 2026-04-30T02:04:29Z
- **Completed:** 2026-04-30T02:10:40Z
- **Tasks:** 2 completed
- **Files modified:** 7

## Accomplishments

- Added `ICleaningTerminationCoordinator` and `CleaningTerminationCoordinator` to own active xEdit process state, process locking, stop-request escalation state, termination result snapshots, and hang monitor forwarding.
- Rewired `CleaningOrchestrator` into a thinner facade that cancels session CTS before delegating stop/force-stop work, forwards `LastTerminationResult`/`HangDetected`, and gates backup cancellation through `terminationCoordinator.HasActiveProcess`.
- Added 10 coordinator-level tests covering graceful stop, second-click force escalation, immediate force stop, self-PID refusal on both stop paths, left-running marking, reset semantics, `CancellationToken.None`, and hang observable forwarding.
- Verified all cleaning regressions and the full solution test suite remain green.

## Task Commits

Each task was committed atomically:

1. **Task 1: RED — define ICleaningTerminationCoordinator + write failing tests** - `c3baa1c` (test)
2. **Task 2: GREEN + REFACTOR — implement CleaningTerminationCoordinator, thin facade, DI, verify Phase 5 invariants** - `e04edd6` (feat)

**Plan metadata:** pending final docs commit

## Files Created/Modified

- `AutoQAC/Services/Cleaning/ICleaningTerminationCoordinator.cs` - New termination coordination contract with documented Phase 5 stop/reset responsibilities.
- `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs` - New implementation owning active process, lock-protected attach/detach, two-stage stop, force-stop, termination-result mapping, reset, and hang monitoring.
- `AutoQAC.Tests/Services/Cleaning/CleaningTerminationCoordinatorTests.cs` - New 10-test collaborator suite for termination invariants.
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` - Facade now forwards termination properties, delegates stop/force-stop/left-running logic, calls attach/detach from the existing inline lambda path, and uses coordinator state for log-skip guards.
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` - Registers `ICleaningTerminationCoordinator` before `ICleaningOrchestrator`.
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs` - Direct orchestrator construction updated to pass a real termination coordinator backed by existing mocks.
- `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs` - Direct orchestrator construction updated to pass a real termination coordinator backed by existing mocks.

## Decisions Made

- Cleaning termination state moved as a unit into `CleaningTerminationCoordinator`; the facade no longer owns `_currentProcess`, `_processLock`, `_isStopRequested`, `_lastTerminationResult`, `_hangMonitorSubscription`, or `_hangDetected`.
- The session CTS remains facade-owned. `StopCleaningAsync` and `ForceStopCleaningAsync` cancel `_cleaningCts` before delegating because CTS lifetime is tied to the cleaning session rather than the coordinator singleton.
- The inline `onProcessStarted` lambda remains in `CleaningOrchestrator` for Wave 3 and calls `terminationCoordinator.AttachProcess(proc)`; 08-05 will hoist this into the runner once.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- The initial RED-gate production build ran in parallel with the expected test-build failure and hit transient `AutoQAC.dll` file-lock contention. Re-running the production build sequentially passed.

## TDD Gate Compliance

- RED gate commit present: `c3baa1c test(08-04): add termination coordinator RED contract`.
- GREEN gate commit present after RED: `e04edd6 feat(08-04): extract cleaning termination coordinator`.
- No separate REFACTOR commit was needed; cleanup was included in the GREEN task after verification.

## Known Stubs

None.

## User Setup Required

None - no external service configuration required.

## Threat Flags

None - the plan moved an existing live-process trust boundary behind the planned coordinator seam and added the planned self-PID, cancellation, hang-lifetime, and reset mitigations; no new network endpoints, auth paths, file access primitives, or schema boundaries were introduced.

## Verification

- `dotnet build AutoQAC/AutoQAC.csproj --nologo` — passed during RED gate after rerun.
- `dotnet build AutoQAC.Tests/AutoQAC.Tests.csproj --nologo` — failed during RED gate with missing `CleaningTerminationCoordinator`, as expected.
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningTerminationCoordinatorTests" --nologo` — passed, 10 tests.
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Cleaning" --nologo` — passed, 180 tests.
- `dotnet test AutoQACSharp.slnx --nologo` — passed, 811 AutoQAC tests + 59 QueryPlugins tests.
- Acceptance source checks confirmed coordinator self-PID refusal, `CancellationToken.None`, reset actions, facade delegation calls, absence of former private termination fields/helpers, and DI registration.

## Next Phase Readiness

Ready for `08-05`: termination coordination is isolated, and the next plan can hoist the inline launch/retry lambda into the plugin runner using the coordinator attach/detach API.

## Self-Check: PASSED

- `FOUND: summary` — `.planning/phases/08-cleaning-orchestrator-decomposition/08-04-SUMMARY.md` exists.
- `FOUND: ICleaningTerminationCoordinator` — `AutoQAC/Services/Cleaning/ICleaningTerminationCoordinator.cs` exists.
- `FOUND: CleaningTerminationCoordinator` — `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs` exists.
- `FOUND: c3baa1c` — RED commit exists in git log.
- `FOUND: e04edd6` — GREEN commit exists in git log.

---
*Phase: 08-cleaning-orchestrator-decomposition*
*Completed: 2026-04-30*
