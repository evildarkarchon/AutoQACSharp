---
phase: 12-process-stop-verification-progress-flow-closure
plan: 01
subsystem: ui
tags: [avalonia, mvvm, dialogs, stop-flow, tdd]

requires:
  - phase: 11-user-facing-diagnostics-boundaries
    provides: Phase 11-safe user-facing diagnostic copy constraints
provides:
  - Shared stop termination dialog copy and action labels
  - Custom two-choice message dialog API preserving Yes/No result semantics
  - Main Stop flow wired to shared Force Terminate / Leave Running contract
affects: [progress-stop-flow, hang-kill-flow, process-stop-verification]

tech-stack:
  added: []
  patterns: [shared model copy contract, explicit dialog action labels, TDD red-green commits]

key-files:
  created:
    - AutoQAC/Models/StopTerminationDialogContent.cs
  modified:
    - AutoQAC/Services/UI/IMessageDialogService.cs
    - AutoQAC/Services/UI/MessageDialogService.cs
    - AutoQAC/ViewModels/MessageDialogViewModel.cs
    - AutoQAC/Views/MessageDialog.axaml
    - AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs
    - AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs

key-decisions:
  - "Stop escalation copy lives in AutoQAC.Models.StopTerminationDialogContent so main and Progress stop surfaces can share one exact text contract."
  - "ShowChoiceAsync maps the primary custom button to MessageDialogResult.Yes and the secondary custom button to MessageDialogResult.No, preserving existing dialog result semantics."

patterns-established:
  - "Shared stop outcome constants: Phase 12 stop copy and labels are asserted exactly in tests before UI callers consume them."
  - "Custom choice dialogs: callers pass explicit action labels while existing ShowConfirmAsync callers retain default Yes/No labels."

requirements-completed: [SAF-01, SAF-02]

duration: 5 min
completed: 2026-05-01
---

# Phase 12 Plan 01: Shared Stop Outcome Dialog Contract Summary

**Shared Phase 11-safe stop escalation copy with explicit Force Terminate / Leave Running dialog labels for main Stop parity**

## Performance

- **Duration:** 5 min
- **Started:** 2026-05-01T09:19:23Z
- **Completed:** 2026-05-01T09:23:41Z
- **Tasks:** 2
- **Files modified:** 7

## Accomplishments

- Added `StopTerminationDialogContent` as the exact shared contract for stop confirmation, force-failure, and left-running outcome copy.
- Added `IMessageDialogService.ShowChoiceAsync` plus bindable `YesButtonText` / `NoButtonText` so dialogs can show explicit action labels while returning existing Yes/No result values.
- Updated the main Stop grace-expired path to use `Force Terminate` / `Leave Running` labels and the shared safe outcome copy.
- Added TDD coverage for shared constants, default Yes/No labels, main Stop label propagation, force-failure copy, and left-running copy.

## Task Commits

Each task was committed atomically:

1. **Task 1 RED: Add failing tests for stop dialog contract** - `fae3587` (test)
2. **Task 1 GREEN: Add shared stop copy contract and custom choice dialog API** - `247d724` (feat)
3. **Task 2 RED: Require shared stop choice in main Stop** - `088a22c` (test)
4. **Task 2 GREEN: Update main Stop to use shared choice contract** - `3420158` (feat)

**Plan metadata:** pending final docs commit

## Files Created/Modified

- `AutoQAC/Models/StopTerminationDialogContent.cs` - Shared exact stop confirmation, force-failure, and left-running text constants.
- `AutoQAC/Services/UI/IMessageDialogService.cs` - Added `ShowChoiceAsync` for custom two-button labels.
- `AutoQAC/Services/UI/MessageDialogService.cs` - Implements custom choice dialogs by setting button text and preserving Yes/No results.
- `AutoQAC/ViewModels/MessageDialogViewModel.cs` - Adds bindable `YesButtonText` and `NoButtonText` defaults.
- `AutoQAC/Views/MessageDialog.axaml` - Binds Yes/No button content to the new ViewModel label properties.
- `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` - Uses the shared stop dialog contract and exact labels in `StopCleaningAsync`.
- `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs` - Adds/updates exact-copy and action-label assertions for main Stop behavior.

## Decisions Made

- Stop dialog content is centralized under `AutoQAC.Models` to avoid a ViewModel-to-service dependency and keep copy reusable by later Progress Stop work.
- Custom choice dialogs return `MessageDialogResult.Yes` for the primary button and `MessageDialogResult.No` for the secondary button so existing stop flow branching remains simple and testable.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## TDD Gate Compliance

- RED commits present: `fae3587`, `088a22c`
- GREEN commits present after RED: `247d724`, `3420158`
- Refactor commit: not needed

## Known Stubs

None.

## User Setup Required

None - no external service configuration required.

## Verification

- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~MainWindowViewModelTests` — Passed, 33/33 tests.
- Source search confirmed `CleaningCommandsViewModel.cs` contains `ShowChoiceAsync(StopTerminationDialogContent.ConfirmationTitle`.
- Source search confirmed no `ShowConfirmAsync(` call remains in `CleaningCommandsViewModel.cs`.

## Next Phase Readiness

Ready for Plan 12-02 to wire Progress Stop and Hang Kill through the shared stop outcome contract.

---
*Phase: 12-process-stop-verification-progress-flow-closure*
*Completed: 2026-05-01*

## Self-Check: PASSED

- Created file exists: `AutoQAC/Models/StopTerminationDialogContent.cs`
- Summary file exists: `.planning/phases/12-process-stop-verification-progress-flow-closure/12-01-SUMMARY.md`
- Commits found: `fae3587`, `247d724`, `088a22c`, `3420158`
