---
phase: 08-cleaning-orchestrator-decomposition
verified: 2026-04-30T03:36:01Z
status: gaps_found
score: 8/10 must-haves verified
overrides_applied: 0
re_verification:
  previous_status: gaps_found
  previous_score: 7/9
  gaps_closed:
    - "User-observable stopped behavior remains unchanged across startup/preflight and plugin execution paths."
    - "Result finalization preserves failed and exception-log outcomes without reclassifying failures as successful/AlreadyClean."
  gaps_remaining: []
  regressions:
    - "08-REVIEW CR-01 remains true: StartCleaningAsync has no in-flight session guard, so concurrent public calls can overlap session orchestration and overwrite _cleaningCts."
    - "08-REVIEW CR-02 remains true: CleaningPreflight validates file-load-order requirements before Unknown-game detection and never revalidates the detected file-based game."
gaps:
  - truth: "User-observable cleaning behavior remains sequential and unchanged when StartCleaningAsync is invoked more than once."
    status: failed
    reason: "CleaningOrchestrator has no session gate. A second StartCleaningAsync call can enter while the first session is active, CreateSessionCts overwrites _cleaningCts, and both workflows can mutate state and reach runner orchestration."
    artifacts:
      - path: "AutoQAC/Services/Cleaning/CleaningOrchestrator.cs"
        issue: "Lines 42-80 create a new session CTS and proceed without SemaphoreSlim/Interlocked/in-flight guard; lines 260-284 only publish/cancel/dispose the latest _cleaningCts."
      - path: "AutoQAC.Tests/Services/CleaningOrchestratorTests.cs"
        issue: "Sequential test covers one session's plugin loop, but no test rejects or no-ops a second concurrent StartCleaningAsync call."
    missing:
      - "Add a non-blocking in-flight session guard around the whole StartCleaningAsync workflow and define the public behavior for a second start (reject/no-op)."
      - "Add a regression test with one blocked active cleaning session and a second StartCleaningAsync call proving no second session starts and the first CTS remains cancellable."
  - truth: "Preflight validates file-load-order requirements for the final detected game before xEdit launch."
    status: failed
    reason: "ValidateConfigurationAsync checks RequiresFileLoadOrder(config.CurrentGameType) before Unknown-game detection. If CurrentGameType starts Unknown and executable detection later resolves Fallout3/FalloutNewVegas/Oblivion, missing or nonexistent LoadOrderPath is not rechecked."
    artifacts:
      - path: "AutoQAC/Services/Cleaning/CleaningPreflight.cs"
        issue: "Lines 38-44 validate using the initial state; lines 50-74 detect the final game type; lines 76-188 build a plan without a second RequiresFileLoadOrder(gameType) check."
      - path: "AutoQAC.Tests/Services/Cleaning/CleaningPreflightTests.cs"
        issue: "No collaborator test covers CurrentGameType=Unknown, DetectFromExecutable=>Fallout3/FNV/Oblivion, and null/missing LoadOrderPath."
    missing:
      - "After game detection succeeds, revalidate LoadOrderPath for detected file-load-order games before skip-list/plugin-row construction."
      - "Add regression coverage for Unknown -> Fallout3/FalloutNewVegas/Oblivion detection with missing LoadOrderPath."
---

# Phase 8: Cleaning Orchestrator Decomposition Verification Report

**Phase Goal:** Maintainers can reduce regression risk by changing cleaning preflight, backup, execution, result finalization, or termination coordination in focused collaborators instead of one monolithic orchestrator.
**Verified:** 2026-04-30T03:36:01Z
**Status:** gaps_found
**Re-verification:** Yes — after 08-07/08-08 gap-closure plans

## Goal Achievement

The structural decomposition exists: all five collaborators are real, DI-registered, substantive, and called by a thin `CleaningOrchestrator` facade. The prior verification gaps for startup Stop cancellation and finalizer result consistency are closed in code and tests.

However, the required code review gate findings were not all false positives. Two unresolved blocker findings still invalidate the roadmap success criterion that user-observable cleaning behavior remains sequential/unchanged and the preflight boundary remains safe.

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Maintainer can change cleaning preflight selection without editing backup, xEdit execution, or result finalization code. | ✓ VERIFIED | `ICleaningPreflight`/`CleaningPreflight` own plan creation, skip-list filtering, MO2 policy facts, validation mapping, and `XEditDirectory`; facade consumes `preflight.PrepareAsync`. |
| 2 | Maintainer can change backup-session handling without editing plugin execution or termination coordination code. | ✓ VERIFIED | `IBackupSessionCoordinator`/`BackupSessionCoordinator` own begin/run/finalize/partial metadata/cancel methods and are called from facade backup dispatch only. |
| 3 | Maintainer can change per-plugin execution and result finalization without changing session-level sequential coordination. | ✓ VERIFIED | `PluginCleaningRunner` owns retry/launch/offset capture; `PluginResultFinalizer` owns log read/parse/result construction; facade retains the sequential `foreach`. |
| 4 | All five collaborators exist, are substantive, and are DI-registered before `ICleaningOrchestrator`. | ✓ VERIFIED | `ServiceCollectionExtensions.cs:62-67` registers preflight, backup coordinator, termination coordinator, runner, finalizer, then orchestrator. |
| 5 | `ICleaningOrchestrator` public surface remains stable. | ✓ VERIFIED | Interface still exposes the same start overloads, stop/force/cancel, left-running mark, termination properties, and dry-run; snapshot test `ICleaningOrchestrator_PublicSurface_MatchesLockedSnapshot` exists and targeted run passed. |
| 6 | Sequential per-session xEdit plugin cleaning is preserved. | ✓ VERIFIED | `CleaningOrchestrator.cs:80-100` uses `foreach`; `PluginCleaningRunner` runs attempts in a `do` loop; grep found no `Parallel.ForEach`, `Task.WhenAll`, `Task.WhenAny`, or `Task.Run` in `AutoQAC/Services/Cleaning/*.cs`. |
| 7 | Stop during orphan cleanup/preflight cancels before xEdit launch. | ✓ VERIFIED | `CleaningOrchestrator.cs:60-78` creates CTS before orphan cleanup/preflight, passes `cts.Token`, and checks cancellation before the plugin loop; two new startup-window Stop tests exist and targeted run passed. |
| 8 | Result finalization preserves failed and exception-log outcomes. | ✓ VERIFIED | `PluginResultFinalizer.cs:53-58` gates AlreadyClean on `result.Success && result.Status == Cleaned`; `PluginResultFinalizer.cs:81-87` derives returned `Success` from final status; three regression tests exist and targeted run passed. |
| 9 | User-observable cleaning behavior remains sequential and unchanged when StartCleaningAsync is invoked concurrently. | ✗ FAILED | 08-REVIEW CR-01 is true: no session gate exists in `CleaningOrchestrator`; `_cleaningCts` can be overwritten by a second public start while the first session is still running. |
| 10 | Preflight validates file-load-order requirements for the final detected game before launch. | ✗ FAILED | 08-REVIEW CR-02 is true: `CleaningPreflight` validates `CurrentGameType` before Unknown-game detection and never revalidates the detected file-based game. |

**Score:** 8/10 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` | Thin sequential facade wiring all collaborators | ✗ FAILED | Structurally thin and CTS-startup gap closed, but lacks an in-flight session guard for concurrent `StartCleaningAsync`. |
| `AutoQAC/Services/Cleaning/ICleaningPreflight.cs` | Preflight contract | ✓ VERIFIED | Single `PrepareAsync` contract. |
| `AutoQAC/Services/Cleaning/CleaningPreflight.cs` | Preflight implementation | ✗ FAILED | Substantive and wired, but post-detection file-load-order validation is missing. |
| `AutoQAC/Services/Cleaning/IBackupSessionCoordinator.cs` / `BackupSessionCoordinator.cs` | Backup lifecycle seam | ✓ VERIFIED | Backup CTS/operation state and metadata/retention work live outside the facade. |
| `AutoQAC/Services/Cleaning/ICleaningTerminationCoordinator.cs` / `CleaningTerminationCoordinator.cs` | Stop/force/hang/process seam | ✓ VERIFIED | Stop/force/left-running/hang observable state live outside the facade. |
| `AutoQAC/Services/Cleaning/IPluginCleaningRunner.cs` / `PluginCleaningRunner.cs` | Per-plugin execution seam | ✓ VERIFIED | Retry loop, offsets, attach/detach delegate flow are substantive and wired. |
| `AutoQAC/Services/Cleaning/IPluginResultFinalizer.cs` / `PluginResultFinalizer.cs` | Result finalization seam | ✓ VERIFIED | Prior finalizer classification gaps are fixed. |
| `AutoQAC.Tests/Services/Cleaning*.cs` | Characterization/collaborator tests | ⚠️ PARTIAL | Many tests exist and targeted regressions pass; missing concurrent-start and Unknown→file-based-load-order tests. |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `CleaningOrchestrator.cs` | `ICleaningPreflight` | Constructor injection, `preflight.PrepareAsync` | ✓ WIRED | Used in real cleaning with `cts.Token` and dry-run with caller token. |
| `CleaningOrchestrator.cs` | `IBackupSessionCoordinator` | `BeginSessionAsync`, `RunPluginBackupAsync`, metadata/finalize/cancel | ✓ WIRED | Backup branch dispatches outcome kinds. |
| `CleaningOrchestrator.cs` | `ICleaningTerminationCoordinator` | Stop/force/attach/detach/reset/property forwarding | ✓ WIRED | Facade delegates termination concerns. |
| `CleaningOrchestrator.cs` | `IPluginCleaningRunner` | `runner.RunAsync` in sequential loop | ✓ WIRED | Called after backup handling. |
| `CleaningOrchestrator.cs` | `IPluginResultFinalizer` | `finalizer.FinalizeAsync` after runner output | ✓ WIRED | Uses `plan.XEditDirectory` and termination snapshot. |
| `ServiceCollectionExtensions.cs` | All collaborators | Singleton registrations | ✓ WIRED | Lines 62-67 register expected services. |
| `CleaningOrchestrator.StartCleaningAsync` | concurrent public callers | session gate | ✗ NOT_WIRED | No guard rejects/serializes a second active session. |
| `CleaningPreflight` detected game | file-load-order validation | post-detection check | ✗ NOT_WIRED | `RequiresFileLoadOrder` is only applied to pre-detection `config.CurrentGameType`. |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|--------------------|--------|
| `CleaningPreflight.cs` | `CleaningPreflightPlan.PluginRows` / `DetectedGameType` | `stateService.CurrentState`, game detection, config skip lists, plugin validation | Partially | ⚠️ FLOWING but missing detected file-based load-order revalidation. |
| `CleaningOrchestrator.cs` | `_cleaningCts` | `CreateSessionCts(ct)` and public Stop/ForceStop | Partially | ⚠️ FLOWING for one session; unsafe when multiple starts overwrite the field. |
| `BackupSessionCoordinator.cs` | `PluginBackupOutcome` / `BackupPluginEntry` | `IBackupService` calls | Yes | ✓ FLOWING |
| `PluginCleaningRunner.cs` | `PluginRunnerOutput` | `ICleaningService.CleanPluginAsync` and log-offset service | Yes | ✓ FLOWING |
| `PluginResultFinalizer.cs` | `PluginCleaningResult` | runner result + bounded log read + parser | Yes | ✓ FLOWING; CR-02/WR-01 prior finalizer gaps are closed. |
| `CleaningTerminationCoordinator.cs` | `StopCleaningResult` / `HangDetected` | attached process + process service + hang monitor | Yes | ✓ FLOWING |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Targeted Phase 8 regressions | `dotnet test "AutoQACSharp.slnx" --nologo --filter "FullyQualifiedName~PluginResultFinalizerTests|FullyQualifiedName~StopCleaningAsync_DuringPreflight_CancelsSessionAndDoesNotInvokeCleaningService|FullyQualifiedName~StopCleaningAsync_DuringOrphanCleanup_CancelsSessionBeforePreflightAndDoesNotInvokeCleaningService|FullyQualifiedName~ICleaningOrchestrator_PublicSurface_MatchesLockedSnapshot"` | AutoQAC.Tests: 9 passed, 0 failed. QueryPlugins had no matching tests. | ✓ PASS |
| Source guard for parallel constructs | Grep `Task.WhenAll|Task.WhenAny|Task.Run|Parallel.ForEach|Parallel.For` in `AutoQAC/Services/Cleaning/*.cs` | No files found. | ✓ PASS |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| REF-01 | 08-01 through 08-08 | Maintainer can change cleaning preflight, backup, execution, result finalization, or termination logic without editing one monolithic cleaning orchestrator. | ⚠️ PARTIAL | Structural seams satisfy the refactoring requirement, and all eight plans declare `requirements: [REF-01]`. But two behavior-preservation blockers remain in those seams/facade, so Phase 8 cannot be marked achieved. |

No additional Phase 8 requirement IDs were found in `.planning/REQUIREMENTS.md`. Every REF-01 claim in PLAN frontmatter is accounted for.

### Code Review Findings Adjudication

| Review Finding | Verdict | Verification Evidence |
|----------------|---------|-----------------------|
| CR-01: Concurrent `StartCleaningAsync` calls can run overlapping sessions | 🛑 TRUE GAP | `CleaningOrchestrator` has no session gate; grep found no `SemaphoreSlim`, `Interlocked`, `_sessionGate`, or already-running guard in the orchestrator. |
| CR-02: Unknown game detection can bypass required load-order validation for file-based games | 🛑 TRUE GAP | `CleaningPreflight.ValidateConfigurationAsync` checks only initial `config.CurrentGameType`; detected `gameType` is not revalidated before building rows. |
| WR-01: Source guard checks a comment instead of executable backup-before-runner call order | ⚠️ TRUE WARNING / ADVISORY DEBT | `CleaningOrchestratorTests.cs:2574-2576` still uses first `IndexOf("RunPluginBackupAsync")`, and `CleaningOrchestrator.cs:152` contains that token in a comment before `runner.RunAsync`. Runtime code currently calls the backup helper before runner, so this is not a phase blocker by itself, but the guard is weak. |

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` | 42-80, 260-284 | No in-flight session guard; `_cleaningCts` overwrite possible | 🛑 Blocker | Second public start can overlap session orchestration and break the hard sequential cleaning/session state invariant. |
| `AutoQAC/Services/Cleaning/CleaningPreflight.cs` | 38-74, 230-268 | Validation before final game detection only | 🛑 Blocker | File-load-order games detected from Unknown can bypass required LoadOrderPath validation. |
| `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs` | 2574-2576 | Source guard satisfied by comment token | ⚠️ Warning | Backup-before-runner regression guard can pass for the wrong reason. |

Stub-pattern scan found only intentional nullable/default empty values in service internals, log DTO construction, and tests; no placeholder implementation was identified in the Phase 8 collaborators.

### Human Verification Required

None. The blocking issues are source-verifiable and reproducible with targeted tests to add.

### Gaps Summary

Plans 08-07 and 08-08 closed the prior verification gaps. The phase still cannot pass because the code review gate surfaced two additional true blockers: concurrent `StartCleaningAsync` calls are not guarded at the facade boundary, and file-load-order validation is not rerun after Unknown-game detection resolves to Fallout3/FalloutNewVegas/Oblivion. Neither is clearly deferred to Phases 9-11; those phases cover plugin refresh, configuration persistence, and diagnostics boundaries, not cleaning-session concurrency or Phase 8 preflight validation safety.

---

_Verified: 2026-04-30T03:36:01Z_
_Verifier: the agent (gsd-verifier)_
