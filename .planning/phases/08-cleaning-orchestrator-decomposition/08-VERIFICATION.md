---
phase: 08-cleaning-orchestrator-decomposition
verified: 2026-04-30T02:39:38Z
status: gaps_found
score: 7/9 must-haves verified
overrides_applied: 0
gaps:
  - truth: "User-observable stopped behavior remains unchanged across startup/preflight and plugin execution paths."
    status: failed
    reason: "Stop requests before the session CTS exists are ignored; CleaningOrchestrator runs orphan cleanup and preflight with the caller token, then creates a fresh session CTS after preflight, so StopCleaningAsync has no CTS to cancel during startup and xEdit can still launch after the user clicked Stop."
    artifacts:
      - path: "AutoQAC/Services/Cleaning/CleaningOrchestrator.cs"
        issue: "Lines 56-69 call CleanOrphanedProcessesAsync/preflight.PrepareAsync before CreateSessionCts; StopCleaningAsync only cancels _cleaningCts at lines 223-226."
      - path: "AutoQAC.Tests/Services/CleaningOrchestratorTests.cs"
        issue: "No regression test found for StopCleaningAsync while preflight/orphan cleanup is blocked before session CTS creation."
    missing:
      - "Create and publish the session CTS before cancellable startup work, pass cts.Token to orphan cleanup and preflight, and honor cancellation before entering the plugin loop."
      - "Add a regression test that blocks preflight, calls StopCleaningAsync, releases preflight, and asserts no plugin cleaning occurs and the session is canceled."
  - truth: "Result finalization preserves failed and exception-log outcomes without reclassifying failures as successful/AlreadyClean."
    status: failed
    reason: "PluginResultFinalizer reads logs for failed non-skipped results and applies AlreadyClean reclassification solely from completion-line + zero stats; it also returns Success = result.Success after changing finalStatus to Failed for exception logs."
    artifacts:
      - path: "AutoQAC/Services/Cleaning/PluginResultFinalizer.cs"
        issue: "Lines 29-53 can reclassify a failed xEdit result as AlreadyClean; lines 57-60 set finalStatus = Failed for exception content, but line 74 keeps the original Success flag."
      - path: "AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs"
        issue: "Existing finalizer tests cover termination-skip and already-clean success paths, but no test was found for failed-result completion-line reclassification or exception-log Success consistency."
    missing:
      - "Gate AlreadyClean promotion on a successful cleaned result (for example result.Success && result.Status == CleaningStatus.Cleaned)."
      - "Derive PluginCleaningResult.Success from the final status after log parsing overrides."
      - "Add tests for failed xEdit + completion line + zero stats staying Failed, and exception log content returning Success = false."
---

# Phase 8: Cleaning Orchestrator Decomposition Verification Report

**Phase Goal:** Maintainers can reduce regression risk by changing cleaning preflight, backup, execution, result finalization, or termination coordination in focused collaborators instead of one monolithic orchestrator.
**Verified:** 2026-04-30T02:39:38Z
**Status:** gaps_found
**Re-verification:** No — initial verification

## Goal Achievement

Phase 8 substantially achieved the structural decomposition goal: all five planned collaborators exist, are DI-registered, and are called by a much smaller `CleaningOrchestrator` facade. However, the roadmap contract also requires user-observable cleaning behavior to remain unchanged. Source inspection confirms the advisory review findings CR-01, CR-02, and WR-01 are still present and affect stopped/failed result semantics, so the phase cannot pass.

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Maintainer can change cleaning preflight selection without editing backup, xEdit execution, or result finalization code. | ✓ VERIFIED | `ICleaningPreflight`/`CleaningPreflight` own `PrepareAsync`, skip-list/file-validation decisions, MO2 policy facts, and `XEditDirectory`; `CleaningOrchestrator` consumes `preflight.PrepareAsync` and projects rows only. |
| 2 | Maintainer can change backup-session handling without editing plugin execution or termination coordination code. | ✓ VERIFIED | `IBackupSessionCoordinator` exposes begin/run/finalize/partial metadata/cancel methods; `BackupSessionCoordinator` owns backup CTS/lock and retention cleanup; facade dispatches `PluginBackupOutcome`. |
| 3 | Maintainer can change per-plugin execution and result finalization without changing session-level sequential coordination. | ✓ VERIFIED | `PluginCleaningRunner` owns retry/launch/offset capture and takes attach/detach delegates; `PluginResultFinalizer` owns log read/parse/result construction; facade retains sequential foreach ordering. |
| 4 | User-observable cleaning behavior remains sequential and unchanged across successful, skipped, failed, stopped, and already-clean plugin outcomes. | ✗ FAILED | Sequentiality is preserved, but stopped and failed/finalized outcomes regressed: CR-01 startup stop requests can be ignored; CR-02/WR-01 finalizer can convert failed runs or exception-log failures into inconsistent successful-looking results. |
| 5 | All five collaborators exist, are DI-registered, and isolate intended policy areas. | ✓ VERIFIED | `ServiceCollectionExtensions.cs` registers `ICleaningPreflight`, `IBackupSessionCoordinator`, `ICleaningTerminationCoordinator`, `IPluginCleaningRunner`, and `IPluginResultFinalizer` before `ICleaningOrchestrator`; implementation files are substantive. |
| 6 | `ICleaningOrchestrator` public surface remains stable. | ✓ VERIFIED | `ICleaningOrchestrator.cs` still exposes the same 10 members; `ICleaningOrchestrator_PublicSurface_MatchesLockedSnapshot` exists in tests. |
| 7 | Sequential xEdit cleaning is preserved. | ✓ VERIFIED | `CleaningOrchestrator.cs` uses a plain sequential `foreach`; `PluginCleaningRunner` launches one plugin attempt at a time; grep found no `Parallel.ForEach`, `Task.WhenAll`, `Task.WhenAny`, or `Task.Run` in `AutoQAC/Services/Cleaning/*.cs`; `ProcessExecutionService` remains in the launch path via `ICleaningService`. |
| 8 | Stop/force-stop, backup cancellation/retention, and result finalization behavior did not regress. | ✗ FAILED | Stop/force-stop coordinator preserves active-process termination semantics, and backup cancellation/retention seams are present, but CR-01 breaks preflight-time stop behavior and CR-02/WR-01 break finalization semantics. |
| 9 | REF-01 is traced through all Phase 8 plans and requirement coverage. | ✓ VERIFIED | All six `08-*-PLAN.md` files declare `requirements: [REF-01]`; `.planning/REQUIREMENTS.md` maps `REF-01` to Phase 8 and marks it complete; implementation provides focused collaborator seams, pending behavior gaps above. |

**Score:** 7/9 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` | Thin sequential facade wiring all five collaborators | ⚠️ PARTIAL | 347 lines and structurally thin, but session CTS is created after orphan cleanup/preflight, causing CR-01. |
| `AutoQAC/Services/Cleaning/ICleaningPreflight.cs` | Preflight contract | ✓ VERIFIED | `Task<CleaningPreflightPlan> PrepareAsync(CancellationToken ct = default)`. |
| `AutoQAC/Services/Cleaning/CleaningPreflight.cs` | Preflight/selection implementation | ✓ VERIFIED | Handles flush, validation, game/variant detection, skip reasons, MO2 policy facts, and `XEditDirectory`; no cleaning state mutation found. |
| `AutoQAC/Services/Cleaning/IBackupSessionCoordinator.cs` | Backup lifecycle contract | ✓ VERIFIED | Declares begin/run/finalize/partial metadata/cancel methods. |
| `AutoQAC/Services/Cleaning/BackupSessionCoordinator.cs` | Backup lifecycle implementation | ✓ VERIFIED | Owns `_backupOperationCts`/lock, state publication, backup failure choices, metadata, retention cleanup. |
| `AutoQAC/Services/Cleaning/ICleaningTerminationCoordinator.cs` | Termination coordination contract | ✓ VERIFIED | Declares attach/detach, stop, force stop, reset, left-running, state queries, and hang observable. |
| `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs` | Termination implementation | ✓ VERIFIED | Owns process lock/current process/stop flag/last result/hang subject; preserves self-PID checks and `CancellationToken.None` termination calls. |
| `AutoQAC/Services/Cleaning/IPluginCleaningRunner.cs` | Per-plugin execution contract | ✓ VERIFIED | Declares `RunAsync` with attach/detach delegates and retry parameters. |
| `AutoQAC/Services/Cleaning/PluginCleaningRunner.cs` | Retry/launch/offset implementation | ✓ VERIFIED | Captures log offsets before each attempt; calls `CleanPluginAsync`; detaches once in `finally`. |
| `AutoQAC/Services/Cleaning/IPluginResultFinalizer.cs` | Finalization contract | ✓ VERIFIED | Declares `FinalizeAsync` over runner output and termination context. |
| `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs` | Log/result finalization implementation | ✗ FAILED | Substantive and wired, but CR-02/WR-01 show finalization can misclassify failed outcomes and return inconsistent `Success`. |
| `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs` and `AutoQAC.Tests/Services/Cleaning/*.cs` | Characterization and collaborator tests | ⚠️ PARTIAL | Many relevant tests exist and full suite passes, but tests are missing for CR-01, CR-02, and WR-01 edge cases. |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `CleaningOrchestrator.cs` | `ICleaningPreflight` | Constructor injection and `preflight.PrepareAsync` | ✓ WIRED | Used by `StartCleaningAsync` and `RunDryRunAsync`. |
| `CleaningOrchestrator.cs` | `IBackupSessionCoordinator` | Constructor injection and `BeginSessionAsync`/`RunPluginBackupAsync`/`FinalizeSessionAsync`/`WritePartialMetadataAsync`/`CancelActiveOperationAsync` | ✓ WIRED | Facade dispatches backup outcomes. |
| `CleaningOrchestrator.cs` | `ICleaningTerminationCoordinator` | Constructor injection and stop/force/attach/detach/reset/property forwarding | ✓ WIRED | Stop/force-stop delegate; process attach/detach passed through runner delegates. |
| `CleaningOrchestrator.cs` | `IPluginCleaningRunner` | Constructor injection and `runner.RunAsync` | ✓ WIRED | Called inside sequential plugin loop after backup handling. |
| `CleaningOrchestrator.cs` | `IPluginResultFinalizer` | Constructor injection and `finalizer.FinalizeAsync` | ✓ WIRED | Called after runner output and termination-context snapshot. |
| `ServiceCollectionExtensions.cs` | All five collaborators | `AddSingleton<Interface, Implementation>()` | ✓ WIRED | Lines 62-66 register all five before `ICleaningOrchestrator`. |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|--------------------|--------|
| `CleaningPreflight.cs` | `CleaningPreflightPlan.PluginRows` | `stateService.CurrentState.PluginsToClean`, config skip list, plugin validation | Yes | ✓ FLOWING |
| `BackupSessionCoordinator.cs` | `PluginBackupOutcome` / `BackupPluginEntry` | `IBackupService.BackupPluginAsync`, metadata and retention APIs | Yes | ✓ FLOWING |
| `PluginCleaningRunner.cs` | `PluginRunnerOutput` | `ICleaningService.CleanPluginAsync` and `IXEditLogFileService.CaptureOffset` | Yes | ✓ FLOWING |
| `PluginResultFinalizer.cs` | `PluginCleaningResult` | `IXEditLogFileService.ReadLogContentAsync`, `IXEditOutputParser.ParseOutput`, runner result | Partially | ✗ HOLLOW/BUGGY for failed-result and exception-log final statuses (CR-02/WR-01). |
| `CleaningTerminationCoordinator.cs` | `StopCleaningResult`, `LastTerminationResult`, `HangDetected` | Attached `System.Diagnostics.Process`, process service, hang monitor | Yes | ✓ FLOWING |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Full solution regression suite | `dotnet test "AutoQACSharp.slnx" --nologo` | Passed: 819 AutoQAC tests + 59 QueryPlugins tests, 0 failed. | ✓ PASS |
| Source guard for parallelization | Grep `Parallel.ForEach|Task.WhenAll|Task.WhenAny|Task.Run` in `AutoQAC/Services/Cleaning/*.cs` | No matches. | ✓ PASS |

Passing tests are not sufficient evidence for the failed truths because source inspection shows uncovered edge cases from `08-REVIEW.md` remain present.

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| REF-01 | 08-01 through 08-06 | Maintainer can change cleaning preflight, backup, execution, result finalization, or termination logic without editing one monolithic cleaning orchestrator. | ⚠️ PARTIAL | Structural collaborator decomposition is implemented and all six plans declare `requirements: [REF-01]`; however, behavior-preservation success criteria are not satisfied because CR-01/CR-02/WR-01 remain. |

No additional Phase 8 requirement IDs were found in `.planning/REQUIREMENTS.md`; all REF-01 references in plan frontmatter are accounted for.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` | 56-69, 223-226 | Session CTS created after cancellable startup work; stop only cancels `_cleaningCts` | 🛑 Blocker | User stop during orphan cleanup/preflight is ignored and cleaning can proceed to xEdit launch. |
| `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs` | 29-53 | Failed non-skipped results are eligible for AlreadyClean reclassification | 🛑 Blocker | A failed xEdit run can be hidden as `AlreadyClean`, violating unchanged failed/already-clean outcome semantics. |
| `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs` | 57-74 | `finalStatus` can be `Failed` while `Success` remains original `true` | ⚠️ Warning | Result row can become internally inconsistent for exception-log failures. |

### Human Verification Required

None. The blocking issues are source-verifiable and have concrete missing tests/fixes.

### Gaps Summary

The codebase proves the collaborator decomposition exists, but the phase goal includes behavior preservation. `08-REVIEW.md` identified three issues; source inspection confirms all three remain. CR-01 blocks stopped-path behavior, and CR-02/WR-01 block result-finalization behavior. These are not deferred to later roadmap phases: Phase 9 addresses plugin refresh/approximation performance, Phase 10 configuration persistence, and Phase 11 diagnostics boundaries, none of which specifically cover startup stop cancellation or finalizer status consistency.

---

_Verified: 2026-04-30T02:39:38Z_
_Verifier: the agent (gsd-verifier)_
