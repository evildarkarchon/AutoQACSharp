# T06: 08-cleaning-orchestrator-decomposition 06

**Slice:** S09 — **Milestone:** M001

## Description

Final integration + regression sweep. Verify the thin-facade `CleaningOrchestrator` correctly composes all 5 collaborators, the public surface is unchanged (Wave 0 snapshot still green), and a source-level guard test scans all six new cleaning files for prohibited parallel constructs.

Purpose: Wave 5. Confirm REF-01 is satisfied — a maintainer can change preflight, backup, runner, finalizer, or termination logic by editing exactly one collaborator file plus its tests, without touching the others.

Output: 1 modified facade (final cleanup pass), 1 new source-level guard test. Run full `dotnet test AutoQACSharp.slnx --nologo` and confirm zero regressions.

## Must-Haves

- [ ] "D-01: focused collaborators — final composition wires the 5 collaborators (preflight, backupCoordinator, terminationCoordinator, runner, finalizer) selected to satisfy Phase 8 success criteria; not a minimal helper-only cleanup and not a pipeline/stage rewrite."
- [ ] "D-02: CleaningOrchestrator remains the public sequential shell/facade — owns high-level session ordering and delegates detailed policies to the 5 collaborators."
- [ ] "D-03: ICleaningOrchestrator public surface is identical to pre-refactor — Wave 0 snapshot test green; member count = 10. start-cleaning overloads, RunDryRunAsync, stop/force-stop, backup-operation cancellation, LastTerminationResult, and HangDetected all remain on the same interface."
- [ ] "D-09: zero intentional user-visible behavior changes — successful, skipped, failed, stopped, force-stopped, left-running, already-clean, backup-canceled, backup-failed-choice, retention-warning, retention-canceled, and dry-run paths all preserved (verified by Wave 0 + collaborator tests)."
- [ ] "D-10: prior phase locks non-negotiable — sequential xEdit cleaning, two-stage stop/force-stop, no log parse after unsafe termination, exact launch argv intent, concise user-facing launch/error messages, backup cancellation semantics, restore/retention safety, MO2 backup skip — all preserved."
- [ ] "D-12: user-facing messages and result meanings stay stable — internal log line placement and collaborator names in logs may change, but no user-visible string is reworded."
- [ ] "Source-level guard test verifies no `Parallel`, `Task.WhenAll`, or `Task.Run` over plugin lists exists in any of the six new cleaning files (orchestrator, preflight, backupCoordinator, runner, finalizer, terminationCoordinator)."
- [ ] "All 41+ pre-existing CleaningOrchestratorTests + 7 Wave 0 characterization tests + Wave 1-4 collaborator tests pass."
- [ ] "Sequential xEdit cleaning preserved — `ProcessExecutionService` single-slot semaphore stays in the launch path."
- [ ] "Phase 5 two-stage stop/force-stop semantics preserved — graceful CloseMainWindow with 2.5s grace then force kill; no log parse after unsafe termination."
- [ ] "Phase 6 launch argv intent preserved — exact xEdit flags and MO2 wrapping unchanged."
- [ ] "Phase 7 backup cancellation, restore/retention warning/canceled semantics preserved — MO2 backup-skip preserved."
- [ ] "CleaningOrchestrator.cs is now a thin facade — final file size measurably smaller (target < 350 lines, was 1151)."
- [ ] "R-01 enforced in facade: `const int maxRetryAttempts = 3` matches CleaningOrchestrator.cs:63; the literal `maxRetryAttempts: 2` must NOT appear anywhere in the facade or its target-shape comments."
- [ ] "R-08 enforced in facade: runner and finalizer are passed `plan.XEditDirectory` directly — the facade does NOT re-derive via `Path.GetDirectoryName(config.XEditExecutablePath)` and does NOT introduce a `var xEditDir =` local."
- [ ] "R-09 enforced in facade AbortSession branch: full 5-step sequence — WritePartialMetadataAsync → wasCancelled = true → build CleaningSessionResult → FinishCleaningWithResults → LogSessionSummary → return — preserved verbatim from CleaningOrchestrator.cs:333–362; 08-03 already wires this in Wave 2 and Wave 5 audit must not regress it."
- [ ] "D-11 honored — if a non-REF-01 bug is found during integration, executor MUST stop and ask, not silently fix."

## Files

- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`
