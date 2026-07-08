---
phase: 08-cleaning-orchestrator-decomposition
plan: 05
subsystem: cleaning
tags: [refactor, cleaning, runner, finalizer, tdd]

# Dependency graph
requires:
  - phase: 08-cleaning-orchestrator-decomposition
    provides: preflight, backup-session, and termination coordinator seams for delegate-based runner/finalizer extraction
provides:
  - IPluginCleaningRunner and PluginCleaningRunner for per-plugin retry, launch, attach delegation, and log-offset capture
  - IPluginResultFinalizer and PluginResultFinalizer for termination-aware log reads, parsing, already-clean promotion, and result construction
  - Runner/finalizer output models and collaborator-level regression tests
affects: [cleaning-orchestrator-decomposition, REF-01, plugin-cleaning-runner, plugin-result-finalizer]

# Tech tracking
tech-stack:
  added: []
  patterns: [primary-constructor stateless services, delegate handoff to termination coordinator, termination-state snapshot before async finalization]

key-files:
  created:
    - AutoQAC/Services/Cleaning/IPluginCleaningRunner.cs
    - AutoQAC/Services/Cleaning/PluginCleaningRunner.cs
    - AutoQAC/Services/Cleaning/IPluginResultFinalizer.cs
    - AutoQAC/Services/Cleaning/PluginResultFinalizer.cs
    - AutoQAC/Services/Cleaning/RunnerFinalizerModels.cs
    - AutoQAC.Tests/Services/Cleaning/PluginCleaningRunnerTests.cs
    - AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs
  modified:
    - AutoQAC/Services/Cleaning/CleaningOrchestrator.cs
    - AutoQAC/Infrastructure/ServiceCollectionExtensions.cs
    - AutoQAC.Tests/Services/CleaningOrchestratorTests.cs
    - AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs

key-decisions:
  - "PluginCleaningRunner receives attach/detach delegates instead of depending on ICleaningTerminationCoordinator, preserving the planned no-back-edge dependency shape."
  - "PluginResultFinalizer consumes a TerminationFinalizeContext snapshot captured after runner detach so log reads do not query live coordinator state during async finalization."
  - "The existing sequential source guard now checks orchestrator plus runner/finalizer files and verifies RunPluginBackupAsync remains before runner.RunAsync."

patterns-established:
  - "Runner/finalizer collaborators are stateless services registered as singletons and invoked from the sequential foreach shell."
  - "Per-plugin process attachment is per attempt, while process detach remains exactly once per plugin in PluginCleaningRunner finally."

requirements-completed: [REF-01]

# Metrics
duration: 6 min
completed: 2026-04-30
---

# Phase 08 Plan 05: Plugin Runner and Result Finalizer Extraction Summary

**Per-plugin xEdit retry/launch and termination-aware log result construction extracted behind runner/finalizer collaborators with delegate handoff to termination coordination.**

## Performance

- **Duration:** 6 min
- **Started:** 2026-04-30T02:13:26Z
- **Completed:** 2026-04-30T02:19:49Z
- **Tasks:** 2 completed
- **Files modified:** 11

## Accomplishments

- Added `IPluginCleaningRunner`/`PluginCleaningRunner` to own the timeout retry FSM, per-attempt log-offset capture, xEdit launch, and attach/detach delegate contract.
- Added `IPluginResultFinalizer`/`PluginResultFinalizer` plus `PluginRunnerOutput` and `TerminationFinalizeContext` to own log reads, parser integration, already-clean promotion, exception-log surfacing, timeout message shaping, and the no-log-after-unsafe-termination guard.
- Rewired `CleaningOrchestrator` into a thinner sequential shell that runs backup, delegates per-plugin execution, snapshots termination state after detach, finalizes the result, and publishes detailed state.
- Registered both new collaborators in DI and updated direct orchestrator test construction.

## Task Commits

Each task was committed atomically:

1. **Task 1: RED — define runner/finalizer contracts and tests** - `2e3392c` (test)
2. **Task 2: GREEN + REFACTOR — implement runner/finalizer and wire facade/DI** - `ddeb209` (feat)

**Plan metadata:** pending final docs commit

## Files Created/Modified

- `AutoQAC/Services/Cleaning/IPluginCleaningRunner.cs` - New runner interface with XML docs for retry, offset, attach, and detach contracts.
- `AutoQAC/Services/Cleaning/PluginCleaningRunner.cs` - New runner implementation preserving retry behavior, offset-before-launch ordering, and once-per-plugin detach semantics.
- `AutoQAC/Services/Cleaning/IPluginResultFinalizer.cs` - New finalizer interface documenting termination-aware log-read decisions.
- `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs` - New finalizer implementation preserving log parsing, exception surfacing, already-clean detection, and terminated-log warning behavior.
- `AutoQAC/Services/Cleaning/RunnerFinalizerModels.cs` - New `PluginRunnerOutput` and `TerminationFinalizeContext` records.
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` - Facade now delegates runner/finalizer work and snapshots termination state after runner detach.
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` - Registers runner/finalizer before `ICleaningOrchestrator`.
- `AutoQAC.Tests/Services/Cleaning/PluginCleaningRunnerTests.cs` - Four collaborator tests for retry/offset and attach/detach behavior.
- `AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs` - Three collaborator tests for termination-skip and already-clean finalization.
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs` - Direct construction and sequential source guard updated for extracted seams.
- `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs` - Direct construction updated for extracted seams.

## Decisions Made

- Kept `PluginCleaningRunner` free of any direct `ICleaningTerminationCoordinator` dependency; the orchestrator passes `AttachProcess` and `DetachProcess` delegates.
- Captured `TerminationFinalizeContext` after `runner.RunAsync` returns, relying on the runner's `finally` to invoke detach before finalization.
- Preserved the literal `const int maxRetryAttempts = 3;` at the orchestrator method level and passed it into the runner rather than changing retry behavior.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Qualified Process type in the cleaning namespace**
- **Found during:** Task 1 (RED contracts)
- **Issue:** `Action<Process>` resolved against the sibling `AutoQAC.Services.Process` namespace inside `AutoQAC.Services.Cleaning`, causing the production build to fail.
- **Fix:** Used `Action<System.Diagnostics.Process>` in runner contracts/implementation so the service namespace cannot shadow the process type.
- **Files modified:** `AutoQAC/Services/Cleaning/IPluginCleaningRunner.cs`, `AutoQAC/Services/Cleaning/PluginCleaningRunner.cs`
- **Verification:** `dotnet build AutoQAC/AutoQAC.csproj --nologo` passed; full solution tests passed.
- **Committed in:** `2e3392c`, `ddeb209`

**2. [Rule 1 - Test Guard Drift] Updated sequential source guard for extracted seam names**
- **Found during:** Task 2 (full solution verification)
- **Issue:** The existing source guard searched for `BackupPluginAsync` before `CleanPluginAsync` in `CleaningOrchestrator.cs`, but those calls moved behind `RunPluginBackupAsync` and `runner.RunAsync` as intended.
- **Fix:** Updated the guard to check orchestrator, runner, and finalizer for parallel constructs and to verify `RunPluginBackupAsync` remains before `runner.RunAsync`.
- **Files modified:** `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`
- **Verification:** `dotnet test AutoQACSharp.slnx --nologo` passed.
- **Committed in:** `ddeb209`

---

**Total deviations:** 2 auto-fixed (1 Rule 3, 1 Rule 1)
**Impact on plan:** Both fixes were required to complete the planned extraction and keep existing verification meaningful. No user-visible behavior changed and no scope was expanded beyond REF-01.

## Issues Encountered

- The first Task 1 production build was run concurrently with the expected RED test build and hit transient `AutoQAC.dll` file-lock contention. Re-running the production build sequentially passed.
- The RED test build failed with missing `PluginCleaningRunner`/`PluginResultFinalizer`, as expected for the TDD RED gate.

## TDD Gate Compliance

- RED gate commit present: `2e3392c test(08-05): add runner and finalizer RED contracts`.
- GREEN gate commit present after RED: `ddeb209 feat(08-05): extract plugin runner and finalizer`.
- No separate REFACTOR commit was needed; cleanup was included in the GREEN task after verification.

## Known Stubs

None.

## User Setup Required

None - no external service configuration required.

## Threat Flags

None - the plan moved existing xEdit process launch/log-read surfaces behind the planned runner/finalizer seams and preserved the offset-bounded log read plus termination-skip mitigations; no new network endpoints, auth paths, filesystem trust boundaries, or schema boundaries were introduced.

## Verification

- `dotnet build AutoQAC/AutoQAC.csproj --nologo` — passed during RED gate after rerun.
- `dotnet build AutoQAC.Tests/AutoQAC.Tests.csproj --nologo` — failed during RED gate with missing `PluginCleaningRunner`/`PluginResultFinalizer`, as expected.
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~PluginCleaningRunnerTests|FullyQualifiedName~PluginResultFinalizerTests" --nologo` — passed, 7 tests.
- `dotnet test AutoQACSharp.slnx --nologo` — passed, 818 AutoQAC tests + 59 QueryPlugins tests.
- Acceptance source checks confirmed runner/finalizer files, preserved offset and termination comments, DI registrations, `runner.RunAsync`, `finalizer.FinalizeAsync`, `preflightPlan.XEditDirectory`, `maxRetryAttempts = 3`, and absence of parallel constructs in runner/finalizer.
- `ccc index` refreshed the semantic code index after the extraction.

## Next Phase Readiness

Ready for `08-06`: per-plugin execution/result finalization is isolated, leaving final integration/regression cleanup as the next Phase 8 step.

## Self-Check: PASSED

- `FOUND: IPluginCleaningRunner` — `AutoQAC/Services/Cleaning/IPluginCleaningRunner.cs` exists.
- `FOUND: PluginCleaningRunner` — `AutoQAC/Services/Cleaning/PluginCleaningRunner.cs` exists.
- `FOUND: IPluginResultFinalizer` — `AutoQAC/Services/Cleaning/IPluginResultFinalizer.cs` exists.
- `FOUND: PluginResultFinalizer` — `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs` exists.
- `FOUND: RunnerFinalizerModels` — `AutoQAC/Services/Cleaning/RunnerFinalizerModels.cs` exists.
- `FOUND: PluginCleaningRunnerTests` — `AutoQAC.Tests/Services/Cleaning/PluginCleaningRunnerTests.cs` exists.
- `FOUND: PluginResultFinalizerTests` — `AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs` exists.
- `FOUND: summary` — `.planning/phases/08-cleaning-orchestrator-decomposition/08-05-SUMMARY.md` exists.
- `FOUND: 2e3392c` — RED task commit exists in git log.
- `FOUND: ddeb209` — GREEN task commit exists in git log.

---
*Phase: 08-cleaning-orchestrator-decomposition*
*Completed: 2026-04-30*
