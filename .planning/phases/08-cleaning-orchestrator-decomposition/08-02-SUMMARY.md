---
phase: 08-cleaning-orchestrator-decomposition
plan: 02
subsystem: cleaning
tags: [refactor, cleaning, preflight, tdd]

# Dependency graph
requires:
  - phase: 08-cleaning-orchestrator-decomposition
    provides: Wave 0 characterization and public-surface guard for CleaningOrchestrator extraction
provides:
  - Shared ICleaningPreflight contract consumed by StartCleaningAsync and RunDryRunAsync
  - CleaningPreflightPlan with full clean/skip rows, MO2 policy facts, timeout/backup settings, and XEditDirectory
  - Collaborator-isolated CleaningPreflight tests for idempotence, MO2 policy facts, warning mapping, and no state mutation
affects: [cleaning-orchestrator-decomposition, REF-01, dry-run, cleaning-preflight]

# Tech tracking
tech-stack:
  added: []
  patterns: [primary-constructor sealed service, required-init preflight record, shared plan projection]

key-files:
  created:
    - AutoQAC/Services/Cleaning/ICleaningPreflight.cs
    - AutoQAC/Services/Cleaning/CleaningPreflightModels.cs
    - AutoQAC/Services/Cleaning/CleaningPreflight.cs
    - AutoQAC.Tests/Services/Cleaning/CleaningPreflightTests.cs
  modified:
    - AutoQAC/Services/Cleaning/CleaningOrchestrator.cs
    - AutoQAC/Infrastructure/ServiceCollectionExtensions.cs
    - AutoQAC.Tests/Services/CleaningOrchestratorTests.cs
    - AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs

key-decisions:
  - "ICleaningPreflight.PrepareAsync is the single source for preflight selection rows used by real cleaning and dry-run projection."
  - "CleaningPreflightPlan carries XEditDirectory so downstream runner/finalizer work can avoid re-deriving the xEdit directory."
  - "MO2 executable validation preserves the legacy file-existence guard before consulting IMo2ValidationService so existing MO2 not-found behavior remains stable."

patterns-established:
  - "Shared preflight plans return full PreflightPluginRow decisions instead of separate clean-list and dry-run implementations."
  - "The facade owns mode-specific state mutation: StartCleaningAsync applies CurrentGameType while RunDryRunAsync only projects plan rows."

requirements-completed: [REF-01]

# Metrics
duration: 7 min
completed: 2026-04-30
---

# Phase 08 Plan 02: Cleaning Preflight Extraction Summary

**Shared cleaning preflight planner with full plugin decision rows, MO2 policy facts, and XEditDirectory feeding both real cleaning and dry-run preview.**

## Performance

- **Duration:** 7 min
- **Started:** 2026-04-30T01:47:48Z
- **Completed:** 2026-04-30T01:54:24Z
- **Tasks:** 2 completed
- **Files modified:** 8

## Accomplishments

- Added `ICleaningPreflight`, immutable preflight models, and `CleaningPreflight` as the new preflight/selection seam.
- Rewired `StartCleaningAsync` and `RunDryRunAsync` to consume one shared `CleaningPreflightPlan`, eliminating duplicate selection logic.
- Added five collaborator-level tests and preserved the existing Cleaning test suite plus full solution test pass.

## Task Commits

Each task was committed atomically:

1. **Task 1: RED — define ICleaningPreflight contract + models, write failing tests** - `824a198` (test)
2. **Task 2: GREEN — implement CleaningPreflight, wire into facade + DI, REFACTOR for clarity** - `ffd74d0` (feat)

**Plan metadata:** `b895409` (docs)

## Files Created/Modified

- `AutoQAC/Services/Cleaning/ICleaningPreflight.cs` - New shared preflight contract with D-14 non-cleaning-state mutation documentation.
- `AutoQAC/Services/Cleaning/CleaningPreflightModels.cs` - New plan, row, decision, and skip-reason model types including `XEditDirectory`.
- `AutoQAC/Services/Cleaning/CleaningPreflight.cs` - New implementation lifting environment validation, game/variant detection, skip-list filtering, MO2 validation, file validation, and policy facts.
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` - Facade now calls `preflight.PrepareAsync` in both real and dry-run paths and projects preflight rows.
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` - Registers `ICleaningPreflight` before `ICleaningOrchestrator`.
- `AutoQAC.Tests/Services/Cleaning/CleaningPreflightTests.cs` - New collaborator-isolated tests.
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs` - Updated constructors to use a real preflight collaborator with existing mocks.
- `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs` - Updated orchestrator construction for process/orphan cleanup regression coverage.

## Decisions Made

- `CleaningPreflight` is stateless and does not call `IStateService.UpdateState`; `StartCleaningAsync` remains the owner of applying detected game state.
- `RunDryRunAsync` keeps user-facing reason strings in the facade projection while relying on preflight reason enums for the source decision.
- MO2 validation retains the existing `File.Exists` behavior to preserve MO2-not-found exceptions, then calls `IMo2ValidationService` for the service seam.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Behavior Preservation] Preserved MO2 file-existence validation before service validation**
- **Found during:** Task 2 (GREEN implementation)
- **Issue:** Using only the injected MO2 validation service let existing orchestrator tests bypass the legacy not-found exception when the mock returned true.
- **Fix:** `CleaningPreflight` now checks `File.Exists(mo2Path)` before awaiting `ValidateMo2ExecutableAsync`, preserving the legacy behavior while still wiring the validation service.
- **Files modified:** `AutoQAC/Services/Cleaning/CleaningPreflight.cs`, `AutoQAC.Tests/Services/Cleaning/CleaningPreflightTests.cs`
- **Verification:** `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Cleaning" --nologo` passed with 170 tests.
- **Committed in:** `ffd74d0`

---

**Total deviations:** 1 auto-fixed (1 Rule 1)
**Impact on plan:** The deviation preserved existing MO2 error behavior while keeping the planned preflight service seam; no scope expansion.

## Issues Encountered

- Existing tests constructing `CleaningOrchestrator` directly needed real `CleaningPreflight` instances backed by their current mocks so behavior characterization stayed meaningful after DI-level extraction.

## TDD Gate Compliance

- RED gate commit present: `824a198 test(08-02): add failing tests for cleaning preflight`.
- GREEN gate commit present after RED: `ffd74d0 feat(08-02): extract cleaning preflight pipeline`.
- No separate REFACTOR commit was needed; cleanup was included in the GREEN task commit after verification.

## Known Stubs

None.

## User Setup Required

None - no external service configuration required.

## Verification

- `dotnet build AutoQAC/AutoQAC.csproj --nologo` — passed during RED gate.
- `dotnet build AutoQAC.Tests/AutoQAC.Tests.csproj --nologo` — failed during RED gate with missing `CleaningPreflight`, as expected.
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningPreflightTests" --nologo` — passed, 5 tests.
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Cleaning" --nologo` — passed, 170 tests.
- `dotnet test AutoQACSharp.slnx --nologo` — passed, 793 AutoQAC tests + 59 QueryPlugins tests.
- `dotnet build AutoQACSharp.slnx --nologo` — passed with 0 warnings.
- Acceptance pattern checks confirmed `CleaningPreflight`, `PluginWarningKind`, `XEditDirectory`, `preflight.PrepareAsync`, and DI registration are present; removed private validation helpers are absent.

## Next Phase Readiness

Ready for `08-03`: preflight selection is now a focused collaborator and the orchestrator facade is prepared for the next extraction seam.

## Self-Check: PASSED

- `FOUND: summary` — `.planning/phases/08-cleaning-orchestrator-decomposition/08-02-SUMMARY.md` exists.
- `FOUND: ICleaningPreflight` — `AutoQAC/Services/Cleaning/ICleaningPreflight.cs` exists.
- `FOUND: CleaningPreflight` — `AutoQAC/Services/Cleaning/CleaningPreflight.cs` exists.
- `FOUND: 824a198` — RED commit exists in git log.
- `FOUND: ffd74d0` — GREEN commit exists in git log.

---
*Phase: 08-cleaning-orchestrator-decomposition*
*Completed: 2026-04-30*
