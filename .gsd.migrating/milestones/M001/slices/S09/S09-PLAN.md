# S09: Cleaning Orchestrator Decomposition

**Goal:** Add Wave 0 characterization tests that lock current `CleaningOrchestrator` behavior BEFORE any extraction begins.
**Demo:** Add Wave 0 characterization tests that lock current `CleaningOrchestrator` behavior BEFORE any extraction begins.

## Must-Haves


## Tasks

- [x] **T01: 08-cleaning-orchestrator-decomposition 01** `est:20 min`
  - Add Wave 0 characterization tests that lock current `CleaningOrchestrator` behavior BEFORE any extraction begins. This is pure test-only work: no production code is modified. The test IS the deliverable.

Purpose: D-05 mandates "characterize-then-extract." Six characterization gaps from RESEARCH.md must be filled, plus a public-surface snapshot must be locked, so subsequent waves can refactor with confidence.

Output: 7 new test methods in `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`, all green against current production code.
- [x] **T02: 08-cleaning-orchestrator-decomposition 02** `est:7 min`
  - Extract preflight/selection logic into `ICleaningPreflight`/`CleaningPreflight` (D-13–D-16) and wire it into the facade. Both `StartCleaningAsync` and `RunDryRunAsync` consume the same plan, eliminating dry-run/real-run drift.

Purpose: First extraction — preflight is the least-coupled seam (async function over inputs with no cleaning state mutation, no process launch, no backup, no CTS creation). Establishes the pattern for subsequent extractions.

Output: 3 new production files, 1 new test file, modified facade, modified DI registration. ~250 lines removed from `CleaningOrchestrator.cs`.
- [x] **T03: 08-cleaning-orchestrator-decomposition 03** `est:5 min`
  - Extract backup-session lifecycle into `IBackupSessionCoordinator`/`BackupSessionCoordinator` (D-06). Coordinator owns `_backupOperationCts`, `_backupOperationLock`, the `BackupPluginAsync` and `CleanupOldSessionsAsync` private helpers, the backup-failure choice switch, and metadata writes. Facade dispatches on a `PluginBackupOutcome` enum.

Purpose: Second extraction. Already-isolated helpers (BackupPluginAsync, CleanupOldSessionsAsync) move atomically. Phase 7 invariants (backup cancellation, retention, MO2 skip) are preserved by lifting code verbatim.

Output: 3 new production files, 1 new test file, modified facade, modified DI. ~300 more lines removed from `CleaningOrchestrator.cs`.
- [x] **T04: 08-cleaning-orchestrator-decomposition 04** `est:6 min`
  - Extract termination coordination into `ICleaningTerminationCoordinator`/`CleaningTerminationCoordinator` (D-08). HIGHEST RISK extraction — touches Phase 5 stop/force-stop locks. Coordinator owns `_currentProcess`, `_processLock`, `_isStopRequested`, `_lastTerminationResult`, hang-monitor subscription, and `_hangDetected` Subject.

Purpose: Third extraction. Must preserve Phase 5 invariants verbatim. Code is lifted move-by-move; behavior unchanged. Facade thins to delegation.

**Lambda hoisting (per R-04):** Wave 3 (THIS plan) keeps the orchestrator's foreach lambda inline; the lambda body just changes from `lock + StartHangMonitoring` private-state mutation to a single `terminationCoordinator.AttachProcess(proc)` call. Wave 4 (08-05) hoists the lambda into the runner via delegate parameters. The same code is not extracted twice.

**DI Subject lifetime note (R-11):** The `_hangDetected` Subject is now owned by `CleaningTerminationCoordinator.Dispose`. Any future ViewModel subscribing to `HangDetected` must treat the observable as having app-lifetime semantics (Singleton DI scope). The behavior is preserved from current code (Subject also lives in a Singleton today inside the orchestrator), but lifetime ownership is now explicit on a different Singleton.

Output: 2 new production files, 1 new test file, modified facade, modified DI. ~150 more lines removed from `CleaningOrchestrator.cs`.
- [x] **T05: 08-cleaning-orchestrator-decomposition 05** `est:6 min`
  - Extract per-plugin work into runner + finalizer (D-07). Runner owns retry/launch/offset capture; finalizer owns log read + result construction. Both depend on already-extracted preflight/termination via delegate handoff (Research A3) — no back-edge to termination coordinator.

Purpose: Fourth extraction. Bottom of dependency graph. After this, the orchestrator's foreach body shrinks to: state-update → backupCoordinator.RunPluginBackupAsync → dispatch → runner.RunAsync → terminationCoordinator.DetachProcess → snapshot → finalizer.FinalizeAsync → state.AddDetailedCleaningResult.

Note on lambda hoisting (per R-04): Wave 3 (plan 08-04) keeps the orchestrator's existing inline foreach lambda — it just rewrites the lambda body to call `terminationCoordinator.AttachProcess(proc)` instead of mutating private fields. THIS plan (Wave 4) is where the lambda→delegate parameter conversion happens, by hoisting it into the runner via `attachProcess`/`detachProcess` parameters. Same code is extracted ONCE, not twice.

Output: 5 new production files (+1 model file extension to add `XEditDirectory`), 2 new test files, modified facade, modified DI.
- [x] **T06: 08-cleaning-orchestrator-decomposition 06** `est:8 min`
  - Final integration + regression sweep. Verify the thin-facade `CleaningOrchestrator` correctly composes all 5 collaborators, the public surface is unchanged (Wave 0 snapshot still green), and a source-level guard test scans all six new cleaning files for prohibited parallel constructs.

Purpose: Wave 5. Confirm REF-01 is satisfied — a maintainer can change preflight, backup, runner, finalizer, or termination logic by editing exactly one collaborator file plus its tests, without touching the others.

Output: 1 modified facade (final cleanup pass), 1 new source-level guard test. Run full `dotnet test AutoQACSharp.slnx --nologo` and confirm zero regressions.
- [x] **T07: 08-cleaning-orchestrator-decomposition 07** `est:4 min`
  - Close gap CR-01 (Blocker): A user Stop click during the startup window (orphan cleanup or preflight) is silently dropped because the session CTS is created AFTER preflight returns. After the fix, calling `StopCleaningAsync` during preflight cancels the session before any plugin enters the cleaning loop, and no xEdit launch occurs.

Purpose: Restore behavior preservation truth #4 and #8 from `08-VERIFICATION.md` — "User-observable stopped behavior remains unchanged across startup/preflight and plugin execution paths."

Output: Updated `CleaningOrchestrator.cs` with session CTS created early + threaded into orphan cleanup and preflight, plus a new regression test in `CleaningOrchestratorTests.cs` proving Stop wins the race against preflight.
- [x] **T08: 08-cleaning-orchestrator-decomposition 08** `est:2 min`
  - Close gaps CR-02 (Blocker) and WR-01 (Warning) in `PluginResultFinalizer.cs`:

- CR-02: Stop converting failed xEdit runs into AlreadyClean. The "completion line + zero stats" promotion must require a SUCCESSFUL cleaned runner result.
- WR-01: Stop returning `Status=Failed, Success=true` for exception-log failures. Derive the returned `Success` from the FINAL status after log-parse overrides.

These two fixes share the same file and would collide on the same return statement, so they are landed together in one plan.

Purpose: Restore behavior preservation truth #4 and #8 from `08-VERIFICATION.md` — "Result finalization preserves failed and exception-log outcomes without reclassifying failures as successful/AlreadyClean."

Output: Updated `PluginResultFinalizer.cs` with the AlreadyClean gate tightened and `Success` derived from `finalStatus`, plus three unit tests in `PluginResultFinalizerTests.cs` (two RED reproducers and one skipped-path characterization).
- [x] **T09: 08-cleaning-orchestrator-decomposition 09** `est:8min`
  - Close Phase 8 verification gap 1: concurrent public `StartCleaningAsync` calls must not overlap session orchestration or overwrite the active session CTS.

Purpose: preserve sequential cleaning/session state invariants from D-02, D-09, D-10, and REF-01 after the facade decomposition.
Output: one failing-then-passing regression test and a non-blocking active-session guard in `CleaningOrchestrator`.
- [x] **T10: 08-cleaning-orchestrator-decomposition 10** `est:7min`
  - Close Phase 8 verification gap 2: preflight must revalidate file-load-order requirements for the final detected game before xEdit launch.

Purpose: preserve D-13/D-15 preflight safety and REF-01 after moving validation into `CleaningPreflight`.
Output: post-detection load-order validation plus focused collaborator tests.

## Files Likely Touched

- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`
- `AutoQAC/Services/Cleaning/ICleaningPreflight.cs`
- `AutoQAC/Services/Cleaning/CleaningPreflight.cs`
- `AutoQAC/Services/Cleaning/CleaningPreflightModels.cs`
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC.Tests/Services/Cleaning/CleaningPreflightTests.cs`
- `AutoQAC/Services/Cleaning/IBackupSessionCoordinator.cs`
- `AutoQAC/Services/Cleaning/BackupSessionCoordinator.cs`
- `AutoQAC/Services/Cleaning/BackupSessionModels.cs`
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC.Tests/Services/Cleaning/BackupSessionCoordinatorTests.cs`
- `AutoQAC/Services/Cleaning/ICleaningTerminationCoordinator.cs`
- `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs`
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC.Tests/Services/Cleaning/CleaningTerminationCoordinatorTests.cs`
- `AutoQAC/Services/Cleaning/IPluginCleaningRunner.cs`
- `AutoQAC/Services/Cleaning/PluginCleaningRunner.cs`
- `AutoQAC/Services/Cleaning/IPluginResultFinalizer.cs`
- `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs`
- `AutoQAC/Services/Cleaning/RunnerFinalizerModels.cs`
- `AutoQAC/Services/Cleaning/CleaningPreflightModels.cs`
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC.Tests/Services/Cleaning/PluginCleaningRunnerTests.cs`
- `AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs`
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`
- `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs`
- `AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs`
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`
- `AutoQAC/Services/Cleaning/CleaningPreflight.cs`
- `AutoQAC.Tests/Services/Cleaning/CleaningPreflightTests.cs`
