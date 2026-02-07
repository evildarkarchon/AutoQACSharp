---
phase: 03-real-time-feedback
plan: 03
subsystem: ui
tags: [avalonia, xaml, validation, reactiveui, mvvm]

# Dependency graph
requires:
  - phase: 01-foundation-hardening
    provides: StateService with CurrentState access
provides:
  - ValidationError record model with Title, Message, FixStep
  - Inline pre-clean validation panel in MainWindow
  - ValidatePreClean method on MainWindowViewModel
  - DismissValidationCommand for clearing errors
affects: [07-comprehensive-testing]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Inline validation panel pattern: ObservableCollection<ValidationError> + HasValidationErrors bool + IsVisible binding"
    - "Pre-clean validation gate: ValidatePreClean runs before orchestrator, returns early on failure"

key-files:
  created:
    - AutoQAC/Models/ValidationError.cs
  modified:
    - AutoQAC/ViewModels/MainWindowViewModel.cs
    - AutoQAC/Views/MainWindow.axaml
    - AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs
    - AutoQAC.Tests/ViewModels/ErrorDialogTests.cs

key-decisions:
  - "Modal dialog validation replaced with non-modal inline panel per user decision"
  - "InvalidOperationException from orchestrator now shown as inline error instead of modal"
  - "Generic Exception (truly unexpected) still uses modal dialog for visibility"

patterns-established:
  - "Inline validation panel: Border with IsVisible binding, ItemsControl with DataTemplate for errors"
  - "ValidatePreClean pattern: check state before orchestrator call, populate ValidationErrors on failure"

# Metrics
duration: 9min
completed: 2026-02-07
---

# Phase 3 Plan 3: Inline Pre-Clean Validation Summary

**Non-modal inline validation error panel with actionable fix steps replaces modal xEdit/config dialogs**

## Performance

- **Duration:** 9 min
- **Started:** 2026-02-07T01:00:20Z
- **Completed:** 2026-02-07T01:08:53Z
- **Tasks:** 2
- **Files modified:** 5

## Accomplishments
- ValidationError record with Title, Message, and FixStep for structured error display
- ValidatePreClean method checks xEdit path (empty/not found), plugins (none loaded/none selected), MO2 config (enabled but missing)
- Inline validation error panel in MainWindow between config panel and plugin list
- All 8 broken ErrorDialogTests updated to assert inline validation instead of modal dialogs
- All 467 tests pass

## Task Commits

Each task was committed atomically:

1. **Task 1: Create ValidationError model and add pre-clean validation to MainWindowViewModel** - `6662c1a` (feat)
2. **Task 2: Add inline validation error panel to MainWindow XAML** - `1412de1` (feat)

## Files Created/Modified
- `AutoQAC/Models/ValidationError.cs` - Sealed record with Title, Message, FixStep properties
- `AutoQAC/ViewModels/MainWindowViewModel.cs` - ValidationErrors collection, HasValidationErrors, DismissValidationCommand, ValidatePreClean method; replaced modal dialog validation in StartCleaningAsync
- `AutoQAC/Views/MainWindow.axaml` - Inline validation error panel with ItemsControl, Dismiss button, amber/warning styling; Grid expanded to 4 rows
- `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs` - Updated StartCleaning tests to provide valid CurrentState for ValidatePreClean
- `AutoQAC.Tests/ViewModels/ErrorDialogTests.cs` - Updated 8 tests: xEdit validation tests assert inline errors, orchestrator tests use CreateViewModelWithValidState helper

## Decisions Made
- Modal dialog validation replaced with non-modal inline panel (per user decision in plan)
- InvalidOperationException from orchestrator caught and displayed as inline "Configuration error" instead of modal dialog
- Generic Exception still uses modal ShowErrorAsync since truly unexpected errors warrant prominent modal display
- Validation reads from `_stateService.CurrentState` (not ViewModel properties) for authoritative state

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Updated 8 ErrorDialogTests for inline validation behavior**
- **Found during:** Task 1 (test verification)
- **Issue:** ErrorDialogTests assumed modal ShowErrorAsync for xEdit validation and configuration invalid errors; tests failed because validation now uses inline panel
- **Fix:** Updated xEdit validation tests to assert HasValidationErrors/ValidationErrors instead of ShowErrorAsync. Added CreateViewModelWithValidState helper for tests that need to reach the orchestrator. Configuration Invalid test now asserts inline error panel.
- **Files modified:** AutoQAC.Tests/ViewModels/ErrorDialogTests.cs
- **Verification:** All 467 tests pass
- **Committed in:** 6662c1a (Task 1 commit)

**2. [Rule 1 - Bug] Updated StartCleaningCommand_ShouldCallOrchestrator test state setup**
- **Found during:** Task 1 (test verification)
- **Issue:** Test used empty AppState as CurrentState, but ValidatePreClean now checks state.PluginsToClean and state.XEditExecutablePath, causing test to fail before reaching orchestrator
- **Fix:** Updated test to provide AppState with valid XEditExecutablePath (temp file) and PluginsToClean
- **Files modified:** AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs
- **Verification:** Test passes, orchestrator.StartCleaningAsync verified called
- **Committed in:** 6662c1a (Task 1 commit)

---

**Total deviations:** 2 auto-fixed (2 bugs - test assertions no longer matching new validation behavior)
**Impact on plan:** Both fixes necessary for test correctness after behavioral change. No scope creep.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Phase 3 Real-Time Feedback is now complete (3/3 plans done)
- All validation, progress, and feedback infrastructure in place
- Ready for Phase 4 (Cleaning Pipeline Enhancement) or subsequent phases

## Self-Check: PASSED

---
*Phase: 03-real-time-feedback*
*Completed: 2026-02-07*
