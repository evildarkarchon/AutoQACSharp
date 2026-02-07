---
phase: 05-safety-features
plan: 01
subsystem: ui
tags: [dry-run, preview, validation, ProgressWindow, ReactiveUI]

# Dependency graph
requires:
  - phase: 02-plugin-pipeline
    provides: "PluginWarningKind enum, ValidatePluginFile, skip list filtering, game variant detection"
  - phase: 03-real-time-feedback
    provides: "ProgressWindow dual-panel layout, ProgressViewModel with results mode"
provides:
  - "DryRunResult model (DryRunStatus enum + sealed record)"
  - "RunDryRunAsync on ICleaningOrchestrator / CleaningOrchestrator"
  - "PreviewCommand on MainWindowViewModel with ShowPreviewInteraction"
  - "IsPreviewMode flag and DryRunResults collection on ProgressViewModel"
  - "Preview panel in ProgressWindow with disclaimer banner"
affects: [05-02, 06-operational-polish, 07-testing]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Dry-run preview reuses validation pipeline without state mutation"
    - "Interaction<List<DryRunResult>, Unit> for preview window wiring"

key-files:
  created:
    - AutoQAC/Models/DryRunResult.cs
  modified:
    - AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs
    - AutoQAC/Services/Cleaning/CleaningOrchestrator.cs
    - AutoQAC/ViewModels/ProgressViewModel.cs
    - AutoQAC/ViewModels/MainWindowViewModel.cs
    - AutoQAC/Views/ProgressWindow.axaml
    - AutoQAC/Views/MainWindow.axaml
    - AutoQAC/Views/MainWindow.axaml.cs

key-decisions:
  - "RunDryRunAsync duplicates validation steps rather than refactoring StartCleaningAsync -- minimal risk to working pipeline"
  - "Preview reuses ProgressWindow with IsPreviewMode flag instead of creating a separate window"
  - "Preview sets IsShowingResults=true to hide active cleaning panel (badges, stop button, progress bar)"

patterns-established:
  - "Dry-run pattern: duplicate validation logic locally without state mutation for safe preview"

# Metrics
duration: 6min
completed: 2026-02-07
---

# Phase 5 Plan 1: Dry-Run Preview Mode Summary

**DryRunResult model, RunDryRunAsync orchestrator method, Preview button on main window, preview panel in ProgressWindow with disclaimer and per-plugin Will Clean/Will Skip entries**

## Performance

- **Duration:** 6 min
- **Started:** 2026-02-07T05:45:40Z
- **Completed:** 2026-02-07T05:51:46Z
- **Tasks:** 2
- **Files modified:** 8

## Accomplishments
- DryRunResult sealed record with WillClean/WillSkip statuses and human-readable reason strings
- RunDryRunAsync on CleaningOrchestrator reuses same validation pipeline (selection check, skip list, file validation) without invoking xEdit or mutating state
- Preview button on main window with same canStart guard as Start Cleaning
- Preview panel in ProgressWindow with "Dry-Run Preview" header, disclaimer banner, and per-plugin results
- Counter badges (ITM/UDR/Nav) and Stop button automatically hidden in preview mode

## Task Commits

Each task was committed atomically:

1. **Task 1: DryRunResult model and RunDryRunAsync orchestrator method** - `67d2f96` (feat)
2. **Task 2: Preview mode in ProgressViewModel, PreviewCommand in MainWindowViewModel, UI updates** - `d09f9a8` (feat)

## Files Created/Modified
- `AutoQAC/Models/DryRunResult.cs` - DryRunStatus enum and DryRunResult sealed record
- `AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs` - Added RunDryRunAsync method signature
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` - Implemented RunDryRunAsync with validation pipeline
- `AutoQAC/ViewModels/ProgressViewModel.cs` - Added IsPreviewMode, DryRunResults, WillCleanCount/WillSkipCount, LoadDryRunResults
- `AutoQAC/ViewModels/MainWindowViewModel.cs` - Added PreviewCommand, ShowPreviewInteraction, RunPreviewAsync
- `AutoQAC/Views/ProgressWindow.axaml` - Added dry-run preview panel with disclaimer, summary bar, plugin list
- `AutoQAC/Views/MainWindow.axaml` - Added Preview button to control buttons panel
- `AutoQAC/Views/MainWindow.axaml.cs` - Registered ShowPreviewInteraction handler

## Decisions Made
- RunDryRunAsync intentionally duplicates validation steps from StartCleaningAsync rather than refactoring shared logic -- keeps the change minimal and avoids risking the working cleaning pipeline
- Preview reuses ProgressWindow with IsPreviewMode flag rather than creating a separate window class -- per plan specification
- PreviewCommand shares the same canStart observable as StartCleaningCommand (requires plugins + xEdit path + not cleaning)

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Dry-run preview feature complete and ready for user testing
- Ready for 05-02-PLAN.md (next plan in safety features phase)
- All 472 existing tests continue to pass

## Self-Check: PASSED

---
*Phase: 05-safety-features*
*Completed: 2026-02-07*
