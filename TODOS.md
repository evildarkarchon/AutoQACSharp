# AutoQACSharp - Outstanding TODOs

*Generated: 2026-02-07*

## Active Code TODOs

### 1. User Confirmation Dialog for Force Kill

- **File:** `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs:384`
- **Tag:** `TODO(01-02)`
- **Origin:** Phase 1 - Foundation Hardening (Plan 01-01)
- **Description:** When a user clicks Stop and the grace period expires (Path A: patient user), the code currently auto-escalates to force kill. This should instead show a confirmation dialog asking the user whether to force-terminate the xEdit process.
- **Current behavior:** `await _orchestrator.ForceStopCleaningAsync()` is called automatically.
- **Desired behavior:** Show a dialog like "xEdit did not exit gracefully. Force terminate?" with Yes/No options.

## Planned Enhancements (from STATE.md)

### 2. Stopping Spinner UI

- **Tag:** `TODO(future)`
- **Description:** Wire `IsTerminatingChanged` observable to the ViewModel to display a "Stopping..." spinner in the UI while termination is in progress. Currently the status text is set to "Stopping..." but there is no animated spinner indicator.

## Stale / Resolved TODOs

The following TODOs appear in `GEMINI.md` but are **no longer applicable** — all referenced items (DI setup, Models, Services) were fully implemented during the v1.0 milestone:

- `App.axaml(.cs) - Entry point, DI setup (TODO)` — DI is fully wired in `App.axaml.cs`
- `Models/ - Data models (Empty - TODO)` — Models directory is populated
- `Services/ - Service interfaces & impl (Missing - TODO)` — Services directory has 15+ service classes
- `Code_To_Port/` reference — Removed in Phase 7 (Plan 07-02)
