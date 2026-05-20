# Phase 14: Orchestrator Decomposition Reverification - Research

**Researched:** 2026-05-01  
**Mode:** forced phase research (`--research`)  
**Scope:** Plan current REF-01 evidence collection for Phase 14 without broad refactoring.

## Research Complete

Phase 14 is an evidence-first reverification phase. No new external libraries, SDKs, or framework research are required. The implementation plan should focus on deterministic repository-local evidence from existing source, existing targeted tests, and a full solution test run.

## Phase-Specific Findings

### Locked deliverable

- `14-CONTEXT.md` D-01 locks the deliverable to `.planning/phases/14-orchestrator-decomposition-reverification/14-VERIFICATION.md` only.
- `14-SPEC.md` requires that artifact to give an explicit `REF-01` pass/fail verdict and pass/fail rows for the four Phase 14 roadmap success criteria.
- Do not create `14-VALIDATION.md` unless a later approved workflow decision changes D-01.

### Historical gap chain to cite

- `.planning/v1.0-MILESTONE-AUDIT.md` marks `REF-01` unsatisfied because stale Phase 8 verification still reported two blockers.
- `.planning/phases/08-cleaning-orchestrator-decomposition/08-VERIFICATION.md` is historical/stale and reports:
  - overlapping `StartCleaningAsync` sessions can replace active session CTS;
  - `Unknown` game detection can bypass post-detection file-load-order validation.
- `.planning/phases/08-cleaning-orchestrator-decomposition/08-VALIDATION.md` rows `08-09-*` and `08-10-*` show later gap-closure coverage.
- `08-09-SUMMARY.md` records the `Interlocked.CompareExchange` active-session guard and concurrent-start test.
- `08-10-SUMMARY.md` records post-detection `ValidateDetectedLoadOrderPath` and Unknown-to-file-load-order tests.

### Current code evidence anchors

- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
  - `_sessionActive` active-session slot is present.
  - `EnterSessionOrThrow()` uses `Interlocked.CompareExchange(ref _sessionActive, 1, 0)` and throws `InvalidOperationException("A cleaning session is already in progress.")`.
  - `ExitSession()` uses `Volatile.Write(ref _sessionActive, 0)`.
  - plugin processing remains a sequential `foreach` loop over `pluginsToClean`.
  - preflight, backup, termination, runner, and finalizer are injected collaborators.
- `AutoQAC/Services/Cleaning/CleaningPreflight.cs`
  - `PrepareAsync` performs initial validation, final game detection, then calls `ValidateDetectedLoadOrderPath(gameType, config.LoadOrderPath)` before variant detection, skip-list loading, or plugin-row construction.
  - `RequiresFileLoadOrder` returns true for `Fallout3`, `FalloutNewVegas`, and `Oblivion`.
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
  - `AddBusinessLogic` registers `ICleaningPreflight`, `IBackupSessionCoordinator`, `ICleaningTerminationCoordinator`, `IPluginCleaningRunner`, `IPluginResultFinalizer`, then `ICleaningOrchestrator`.

### Current test evidence anchors

- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`
  - `StartCleaningAsync_WhenSessionAlreadyActive_ShouldRejectSecondStartAndKeepFirstSessionCancellable` proves overlapping public starts reject and first session remains cancellable.
  - `CleaningOrchestrator_Source_DoesNotParallelizePluginCleaning` checks orchestrator/runner/finalizer source for parallel constructs.
  - `Cleaning_Source_NoFileParallelizesPluginLoop` scans the cleaning service tier for `Parallel.ForEach`, `Parallel.ForEachAsync`, `Task.WhenAll(`, `Task.WhenAny(`, and `Task.Run(`.
  - `ICleaningOrchestrator_PublicSurface_MatchesLockedSnapshot` remains useful collaborator-boundary evidence.
- `AutoQAC.Tests/Services/Cleaning/CleaningPreflightTests.cs`
  - `PrepareAsync_UnknownGameDetectedAsFileLoadOrderGame_WithMissingLoadOrderPath_Throws` covers Fallout3, FalloutNewVegas, and Oblivion detected from `Unknown`.
  - `PrepareAsync_UnknownGameDetectedAsMutagenSupportedGame_WithMissingLoadOrderPath_Succeeds` proves the Mutagen-supported control path.

## Recommended Evidence Commands

Use concise command-result rows in `14-VERIFICATION.md` per D-03:

```powershell
dotnet test AutoQACSharp.slnx --nologo --filter "FullyQualifiedName~StartCleaningAsync_WhenSessionAlreadyActive_ShouldRejectSecondStartAndKeepFirstSessionCancellable"
dotnet test AutoQACSharp.slnx --nologo --filter "FullyQualifiedName~PrepareAsync_UnknownGameDetectedAsFileLoadOrderGame_WithMissingLoadOrderPath_Throws|FullyQualifiedName~PrepareAsync_UnknownGameDetectedAsMutagenSupportedGame_WithMissingLoadOrderPath_Succeeds"
dotnet test AutoQACSharp.slnx --nologo --filter "FullyQualifiedName~Cleaning_Source_NoFileParallelizesPluginLoop|FullyQualifiedName~CleaningOrchestrator_Source_DoesNotParallelizePluginCleaning|FullyQualifiedName~ICleaningOrchestrator_PublicSurface_MatchesLockedSnapshot"
dotnet test AutoQACSharp.slnx --nologo
```

If a command fails, `14-VERIFICATION.md` must record `gaps_found` and must not claim `REF-01` satisfied until the evidence passes. If the failure is a real live Phase 14 gap, use the smallest SPEC-scoped remediation path and rerun focused plus full evidence before changing the verdict.

## Planning Constraints

- Keep Phase 14 phase-local: do not edit Phase 8 files, `ROADMAP.md` completion/status markers, `REQUIREMENTS.md`, or `.planning/v1.0-MILESTONE-AUDIT.md` during execution.
- No real xEdit, real MO2, or manual smoke evidence is required for this internal refactor verification.
- Preserve sequential xEdit cleaning. No plan should introduce or approve parallel plugin cleaning, parallel xEdit launches, or process-launch path changes.
- If current checks pass without production or test changes, `14-VERIFICATION.md` should explicitly state Phase 14 was evidence-only.

## Source Audit

| Source | Item | Coverage in plan |
|--------|------|------------------|
| ROADMAP | Phase 14 goal and four success criteria | Covered by `14-VERIFICATION.md` rows and final verdict. |
| REQUIREMENTS.md | `REF-01` | Covered by requirement verdict and collaborator-boundary evidence. |
| SPEC.md | Requirements 1-5 and acceptance criteria | Covered by artifact creation, focused evidence, source/DI evidence, and full-suite evidence. |
| CONTEXT.md | D-01 through D-16 | Covered by task actions and verification artifact content requirements. |
| Research | Current code/test anchors and commands | Covered by the Phase 14 execution plan. |

## Research Complete

Proceed to planning with a single evidence-focused plan. The natural execution shape is one autonomous plan with two tasks: focused/source evidence collection, then full-suite evidence and final verdict writing.