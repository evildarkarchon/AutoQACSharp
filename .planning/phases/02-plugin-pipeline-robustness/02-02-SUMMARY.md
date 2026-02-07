---
phase: 02-plugin-pipeline-robustness
plan: 02
subsystem: game-detection-skip-lists
tags: [TTW, Enderal, GameVariant, Unknown-blocking, MO2-validation, aggregated-errors]
dependency-graph:
  requires: [02-01]
  provides: [game-variant-detection, ttw-skip-list-merge, enderal-skip-list, unknown-blocking, mo2-validation-skip, aggregated-path-errors]
  affects: [phase-5-backup, phase-6-ui]
tech-stack:
  added: []
  patterns: [variant-detection-from-load-order, skip-list-key-override, mo2-vfs-aware-validation]
key-files:
  created:
    - AutoQAC/Models/GameVariant.cs
  modified:
    - AutoQAC/Services/GameDetection/IGameDetectionService.cs
    - AutoQAC/Services/GameDetection/GameDetectionService.cs
    - AutoQAC/Services/Configuration/IConfigurationService.cs
    - AutoQAC/Services/Configuration/ConfigurationService.cs
    - AutoQAC/Services/Cleaning/XEditCommandBuilder.cs
    - AutoQAC/Services/Cleaning/CleaningOrchestrator.cs
    - AutoQAC.Tests/Services/GameDetectionServiceTests.cs
    - AutoQAC.Tests/Services/XEditCommandBuilderTests.cs
    - AutoQAC.Tests/Services/CleaningOrchestratorTests.cs
    - AutoQAC.Tests/Services/ConfigurationServiceSkipListTests.cs
decisions:
  - id: 02-02-01
    description: "DetectVariant scans load order plugin names for marker ESMs (TaleOfTwoWastelands.esm, Enderal - Forgotten Stories.esm / Enderal.esm)"
    rationale: "Direct marker plugin detection is the most reliable method, matching Python reference implementation"
  - id: 02-02-02
    description: "Enderal uses separate 'Enderal' key in skip list config instead of 'SSE'"
    rationale: "Enderal is a total conversion; using SSE skip list would protect wrong plugins"
  - id: 02-02-03
    description: "MO2 binary path validated from AppState.Mo2ExecutablePath (not UserConfiguration)"
    rationale: "Orchestrator operates on AppState throughout; UserConfiguration is only loaded for settings flags"
  - id: 02-02-04
    description: "GameType.Unknown throws InvalidOperationException instead of logging warning and continuing"
    rationale: "Safety-critical: cleaning without skip lists risks corrupting base game plugins"
  - id: 02-02-05
    description: "GetSkipListAsync gains GameVariant optional parameter (default None) -- backward-compatible for all existing callers"
    rationale: "Avoids breaking all downstream callers; only the orchestrator passes the variant"
metrics:
  duration: 8m 2s
  completed: 2026-02-07
---

# Phase 2 Plan 2: MO2 Path Resolution and Error Guidance Summary

Game variant detection (TTW/Enderal) wired through to skip list loading, GameType.Unknown blocked in orchestrator and command builder, aggregated path-failure reporting, MO2 mode skips file validation with actionable MO2 error messages.

## What Changed

### GameVariant Enum (NEW)
- `GameVariant` enum: None, TTW, Enderal
- Created at `AutoQAC/Models/GameVariant.cs`

### IGameDetectionService Interface
- Added `DetectVariant(GameType baseGame, IReadOnlyList<string> pluginNames)` method
- Scans load order for marker plugins to identify TTW and Enderal variants

### GameDetectionService Implementation
- TTW: detects `TaleOfTwoWastelands.esm` (case-insensitive) when base game is FalloutNewVegas
- Enderal: detects `Enderal - Forgotten Stories.esm` or `Enderal.esm` (case-insensitive) when base game is SkyrimSe
- Returns `GameVariant.None` for wrong base game or missing markers

### IConfigurationService / ConfigurationService
- `GetSkipListAsync` gains `GameVariant variant = GameVariant.None` optional parameter
- TTW variant: merges FO3 skip list entries (both user and Main.yaml) into FNV list, then deduplicates
- Enderal variant: uses "Enderal" key instead of "SSE" key for skip list lookup
- Non-variant calls are backward-compatible (default parameter)

### XEditCommandBuilder
- `BuildCommand` returns `null` immediately for `GameType.Unknown`
- Prevents building commands with empty game flags

### CleaningOrchestrator
- **Unknown blocking**: throws `InvalidOperationException` when game type cannot be determined (was: log warning and continue)
- **Variant detection wiring**: calls `DetectVariant` after game type is resolved, passes variant to `GetSkipListAsync`
- **MO2 binary validation**: early check with actionable error messages ("Check MO2 executable path in Settings")
- **MO2 mode validation skip**: skips `ValidatePluginFile` calls entirely when MO2 mode active (VFS resolves at runtime)
- **Aggregated path errors**: collects all plugin path failures into single summary message, removes invalid plugins from list

### Test Suite
- 7 DetectVariant tests (TTW/Enderal detection, case sensitivity, wrong base game, empty list)
- 1 XEditCommandBuilder test (Unknown returns null)
- 5 CleaningOrchestrator tests (Unknown blocking, MO2 validation skip, MO2 binary validation, actionable errors)
- 3 ConfigurationService tests (TTW merge, non-TTW exclusion, Enderal separate key)
- Total: 467 tests pass (was 451)

## Task Commits

| Task | Name | Commit | Type |
|------|------|--------|------|
| 1 | Write failing tests for variant detection, skip list merge, Unknown blocking, MO2 mode | 05e3cb5 | test |
| 2 | Implement variant detection, skip list merge, Unknown blocking, MO2 validation | 36e3026 | feat |

## Decisions Made

1. **Marker plugin detection** (02-02-01): DetectVariant scans plugin names for specific ESMs rather than checking file content or checksums.
2. **Enderal separate key** (02-02-02): Uses "Enderal" key in skip list config, not "SSE", because Enderal is a total conversion with different base plugins.
3. **AppState for MO2 path** (02-02-03): MO2 binary validation uses AppState.Mo2ExecutablePath rather than UserConfiguration.ModOrganizer.Binary for consistency with orchestrator flow.
4. **Unknown = hard error** (02-02-04): Changed from "log warning, continue anyway" to throwing InvalidOperationException. This is safety-critical since cleaning without skip lists could corrupt base game ESMs.
5. **Backward-compatible parameter** (02-02-05): GameVariant added as optional parameter with default None to avoid breaking all existing callers.

## Deviations from Plan

None -- plan executed exactly as written.

## Next Phase Readiness

- Phase 2 is now complete (both plans 02-01 and 02-02 done)
- The variant detection and skip list merge are fully wired from detection through to the cleaning loop
- MO2 mode validation skip ensures MO2 users can clean plugins without false file-not-found errors
- All 467 tests pass with zero regressions

## Self-Check: PASSED
