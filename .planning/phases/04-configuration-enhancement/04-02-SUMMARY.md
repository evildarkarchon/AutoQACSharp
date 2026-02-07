---
phase: 04-configuration-enhancement
plan: 02
subsystem: configuration-ui
tags: [path-validation, log-retention, settings-ui, migration-banner, rx-throttle]
dependency-graph:
  requires: [04-01]
  provides: [settings-path-validation, log-retention-service, migration-banner-ui]
  affects: [05, 06]
tech-stack:
  added: []
  patterns: [WhenAnyValue+Throttle+Skip, nullable-bool-validation-state, optional-vs-required-path-validation]
key-files:
  created:
    - AutoQAC/Services/Configuration/ILogRetentionService.cs
    - AutoQAC/Services/Configuration/LogRetentionService.cs
    - AutoQAC/Converters/NullableBoolConverters.cs
    - AutoQAC/Converters/IntEqualsConverter.cs
  modified:
    - AutoQAC/ViewModels/SettingsViewModel.cs
    - AutoQAC/Views/SettingsWindow.axaml
    - AutoQAC/Views/MainWindow.axaml
    - AutoQAC/Views/MainWindow.axaml.cs
    - AutoQAC/ViewModels/MainWindowViewModel.cs
    - AutoQAC/Infrastructure/ServiceCollectionExtensions.cs
    - AutoQAC/App.axaml.cs
    - AutoQAC/App.axaml
decisions:
  - Debounced path validation at 400ms via WhenAnyValue + Throttle + Skip(1)
  - Nullable bool validation state (null=untouched, true=valid, false=invalid)
  - Optional paths return null for empty (no indicator), not true (green checkmark)
  - ValidateLoadedPaths() called after LoadSettingsAsync for immediate indicators on open
  - IsAgeBasedMode/IsCountBasedMode booleans instead of IntEqualsConverter for retention visibility
  - Journal Settings section removed (replaced by Log Retention)
  - Log Retention section placed above Cleaning Settings
  - Main window path fields have same validation indicators as Settings window
  - Active Serilog log file always skipped by retention cleanup (first file in descending order)
metrics:
  duration: ~15m (including 3 checkpoint rounds)
  completed: 2026-02-07
---

# Phase 4 Plan 2: Settings Validation UI, Log Retention, and Migration Banner

**Real-time path validation in Settings and MainWindow, log retention cleanup service, and migration warning banner**

## What Was Done

### Task 1: Path validation, log retention service, and Settings UI

**LogRetentionService:**
- `ILogRetentionService` with `CleanupAsync` method
- Reads retention settings from config (AgeBased or CountBased mode)
- Enumerates `autoqac-*.log` files ordered by LastWriteTimeUtc descending
- ALWAYS skips the most recent file (active Serilog log)
- AgeBased: deletes files older than MaxAgeDays; CountBased: keeps MaxFileCount files
- Each deletion wrapped in try/catch (continues on locked files)
- Runs on app startup via App.axaml.cs (fire-and-forget)

**SettingsViewModel path validation:**
- Four path properties (XEditPath, Mo2Path, LoadOrderPath, DataFolderPath) with nullable bool validation state
- Required paths (xEdit): `SetupPathValidation` returns true/false via `ValidateExecutablePath`
- Optional paths (MO2, Load Order, Data Folder): `SetupOptionalPathValidation` returns null for empty, true/false for non-empty
- Rx pipeline: `WhenAnyValue → Skip(1) → Where(!_isLoading) → Throttle(400ms) → ObserveOn(MainThread) → Select(validator)`
- `ValidateLoadedPaths()` called after `LoadSettingsAsync` for immediate indicators on open
- Browse commands validate immediately (bypass debounce)
- Retention mode: `IsAgeBasedMode`/`IsCountBasedMode` boolean properties wired via WhenAnyValue subscription

**SettingsWindow XAML:**
- File Paths section with four path fields, each with TextBox + validation indicator (checkmark/X) + Browse button
- Log Retention section with mode ComboBox and conditional age/count inputs
- Journal Settings section removed (redundant with Log Retention)
- Layout order: File Paths → Log Retention → Cleaning Settings → Mode Settings

**MainWindow validation indicators:**
- Added IsXEditPathValid, IsMo2PathValid, IsLoadOrderPathValid, IsGameDataFolderValid to MainWindowViewModel
- WhenAnyValue subscriptions validate paths reactively (null for empty optional, true/false for populated)
- Checkmark/X indicators added to all four path fields in MainWindow.axaml

**Migration warning banner:**
- MainWindow.axaml has red warning banner at top bound to HasMigrationWarning
- Dismissible via DismissMigrationWarningCommand
- Only visible when legacy migration fails (wired in App.axaml.cs)

### Checkpoint Fixes (3 rounds)

**Round 1 issues (fixed by continuation agent):**
1. Initial WhenAnyValue emission leaked through before _isLoading was set → added Skip(1)
2. Empty optional paths showed green checkmark → new SetupOptionalPathValidation returns null for empty
3. Both retention inputs always visible → replaced IntEqualsConverter with direct boolean properties
4. Main window missing validation indicators → added to MainWindowViewModel + MainWindow.axaml

**Round 2 issues (fixed by orchestrator):**
1. Settings didn't show indicators until user made a change → added ValidateLoadedPaths() after load
2. Log Retention below Cleaning Settings → moved above

**Round 3 issues (fixed by orchestrator):**
1. Journal Settings section redundant → removed

## Task Commits

| Task | Name | Commit | Key Files |
|------|------|--------|-----------|
| 1 | Path validation, log retention, Settings UI | 892d644 | LogRetentionService.cs, SettingsViewModel.cs, SettingsWindow.axaml |
| fix1 | Validation timing, retention booleans, main window indicators | c05c3c9 | SettingsViewModel.cs, MainWindowViewModel.cs, MainWindow.axaml |
| fix2 | Validate on load, reorder sections, remove journal | fa4ec56 | SettingsViewModel.cs, SettingsWindow.axaml |

## Decisions Made

1. **Skip(1) + _isLoading double guard** — Skip(1) prevents constructor emission leaking; _isLoading prevents LoadSettingsAsync emissions. Both needed.
2. **Nullable bool = 3-state validation** — null (untouched/empty optional) → no indicator; true → green; false → red
3. **ValidateLoadedPaths on open** — Users expect to see validation status immediately, not after first interaction
4. **Direct boolean properties for retention mode** — More reliable than XAML ConverterParameter which may not pass types correctly
5. **Journal Settings removed** — Log Retention replaces it as the unified retention control

## Deviations from Plan

- Added main window validation indicators (not in original plan, user feedback)
- Removed Journal Settings section (user feedback: redundant with Log Retention)
- Three checkpoint rounds instead of one (validation timing bugs required iterative fixes)

## Test Results

All 472 existing tests pass with zero failures and zero regressions.

## Self-Check: PASSED
