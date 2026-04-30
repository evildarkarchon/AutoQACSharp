# Phase 8: Wave 1 - Preflight Extraction Summary

**Completed:** 2026-04-30
**Goal:** Extract preflight and plugin selection logic into a dedicated collaborator.

## Work Completed

### Task 1: RED — define ICleaningPreflight contract + models
Created the following files:
- `AutoQAC/Services/Cleaning/ICleaningPreflight.cs`: Defines the `PrepareAsync` contract.
- `AutoQAC/Services/Cleaning/CleaningPreflightModels.cs`: Contains `CleaningPreflightPlan`, `PreflightPluginRow`, and related enums.
- `AutoQAC.Tests/Services/Cleaning/CleaningPreflightTests.cs`: Added 5 unit tests verifying:
    - Real-run vs dry-run equivalence (D-13).
    - MO2 policy facts (D-16).
    - Skip reason mapping (D-15).
    - No-mutation policy (D-14).

### Task 2: GREEN — implement CleaningPreflight, wire into facade + DI
- Implemented `CleaningPreflight.cs` by lifting logic from `CleaningOrchestrator`.
- Consolidated game detection, variant detection, skip-list filtering, and file validation.
- Updated `CleaningOrchestrator.cs` to inject `ICleaningPreflight` and consume the shared plan.
- Registered `CleaningPreflight` as a Singleton in `ServiceCollectionExtensions.cs`.
- Deleted now-unused private helpers (`ValidateConfigurationAsync`, `RequiresFileLoadOrder`) from `CleaningOrchestrator`.
- Updated `Cleaning_Source_DoesNotParallelizePluginCleaning` to scan the new preflight file.

## Verification Results

- **Maintainability (REF-01):** Preflight logic is now isolated. Maintainers can change plugin selection rules without touching the main orchestration loop.
- **Drift Prevention (D-13):** Both real cleaning and dry-run now use the exact same logic path.
- **Public Surface:** Snapshot test from Wave 0 remains green.
- **Line Count:** `CleaningOrchestrator.cs` shrank from 1151 lines to 885 lines (approx. -266 lines).

## Next Steps

Proceeding to **Wave 2: Backup-Session Extraction** (Plan 08-03), where backup session handling will be moved into `IBackupSessionCoordinator`.
