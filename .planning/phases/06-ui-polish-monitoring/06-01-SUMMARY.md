---
phase: 06-ui-polish-monitoring
plan: 01
subsystem: ui
tags: [avalonia, reactiveui, mvvm, viewmodel-decomposition, compiled-bindings]

# Dependency graph
requires:
  - phase: 05-safety-features
    provides: "Dry-run preview, backup/restore, cleaning commands, validation panel -- all features now split across sub-VMs"
provides:
  - "ConfigurationViewModel (723 lines) -- path management, file dialogs, game selection, auto-save, migration warning"
  - "PluginListViewModel (100 lines) -- plugin collection, select/deselect all, skip list filtering"
  - "CleaningCommandsViewModel (456 lines) -- start/stop/preview, validation errors, status, menu commands"
  - "Slim MainWindowViewModel orchestrator (100 lines) -- owns Interactions, composes sub-VMs, dispatches state"
  - "All XAML bindings use dotted path notation (Configuration.X, PluginList.X, Commands.X)"
affects:
  - 06-02 (About dialog will add ShowAboutInteraction to parent, command stays in CleaningCommandsViewModel)
  - 06-03 (logging/monitoring work may add properties to ConfigurationViewModel or new sub-VM)
  - 07-hardening-cleanup (test coverage expansion targets sub-VMs directly)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Sub-ViewModel composition: parent creates sub-VMs, passes Interaction references and service dependencies"
    - "Dotted path XAML bindings: Configuration.XEditPath, PluginList.PluginsToClean, Commands.StartCleaningCommand"
    - "State-driven canExecute: CleaningCommandsViewModel derives canStart entirely from IStateService.StateChanged"
    - "OnStateChanged dispatch: parent subscribes to StateChanged once, fans out to all three sub-VMs"

key-files:
  created:
    - AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs
    - AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs
    - AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs
  modified:
    - AutoQAC/ViewModels/MainWindowViewModel.cs
    - AutoQAC/Views/MainWindow.axaml
    - AutoQAC/Views/MainWindow.axaml.cs
    - AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs
    - AutoQAC.Tests/ViewModels/ErrorDialogTests.cs

key-decisions:
  - "Interaction references passed from parent to CleaningCommandsViewModel constructor (avoids extra event plumbing)"
  - "canStart observable derives entirely from IStateService.StateChanged (avoids cross-VM reactive chains)"
  - "Sub-VMs created directly by parent (not DI-registered) -- simpler lifecycle, no container bloat"
  - "StatusText lives on ConfigurationViewModel (status bar), not CleaningCommandsViewModel"
  - "ResetSettingsCommand moved to ConfigurationViewModel (settings concern, not cleaning concern)"

patterns-established:
  - "Sub-ViewModel pattern: parent owns Interactions, creates sub-VMs, dispatches OnStateChanged to each"
  - "XAML dotted path bindings for sub-VM access through x:DataType=MainWindowViewModel"
  - "Tests construct MainWindowViewModel and access sub-VM properties via vm.Configuration.X, vm.Commands.X, vm.PluginList.X"

# Metrics
duration: 12min
completed: 2026-02-07
---

# Phase 6 Plan 1: ViewModel Decomposition Summary

**Decomposed 1186-line MainWindowViewModel into three sub-VMs (Configuration, PluginList, CleaningCommands) with slim 100-line orchestrator and dotted-path XAML bindings**

## Performance

- **Duration:** ~12 min
- **Started:** 2026-02-07T07:50:00Z
- **Completed:** 2026-02-07T08:03:00Z
- **Tasks:** 2
- **Files modified:** 8

## Accomplishments
- Extracted ConfigurationViewModel (723 lines): all path management, file dialogs, game selection, path validation, auto-save subscriptions, migration warning banner
- Extracted PluginListViewModel (100 lines): plugin ObservableCollection, select/deselect all commands with state-driven canExecute
- Extracted CleaningCommandsViewModel (456 lines): start/stop/preview commands, pre-clean validation, validation error panel, menu commands (exit, about, settings, skip list, restore)
- Slimmed MainWindowViewModel to 100-line orchestrator: owns Interactions (registered in code-behind), composes sub-VMs, dispatches state changes
- Updated all XAML bindings to dotted path notation with zero compiled binding errors
- Migrated all 461 tests to target new sub-VM structure (all passing)

## Task Commits

Each task was committed atomically:

1. **Task 1: Extract sub-ViewModels and slim down MainWindowViewModel** - `ba42c1b` (refactor)
2. **Task 2: Update XAML bindings and migrate tests** - `4977cfe` (test)

## Files Created/Modified
- `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs` - Path management, file dialogs, game selection, validation, auto-save, migration warning (723 lines)
- `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs` - Plugin collection, select/deselect all, skip list filtering (100 lines)
- `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` - Start/stop/preview, validation errors, status, menu commands (456 lines)
- `AutoQAC/ViewModels/MainWindowViewModel.cs` - Slim orchestrator: Interactions, sub-VM composition, state dispatch (100 lines, down from 1186)
- `AutoQAC/Views/MainWindow.axaml` - All bindings updated with Configuration., PluginList., Commands. prefixes
- `AutoQAC/Views/MainWindow.axaml.cs` - ShowRestoreAsync updated to access vm.Configuration.GameDataFolder
- `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs` - All property/command access paths updated to sub-VM notation
- `AutoQAC.Tests/ViewModels/ErrorDialogTests.cs` - All property/command access paths updated to sub-VM notation

## Decisions Made

1. **Interaction references passed to CleaningCommandsViewModel**: Instead of extra event plumbing (Action/IObservable), Interaction objects are passed from the parent constructor. CleaningCommandsViewModel calls Handle() on them directly. Interactions are still "owned" by MainWindowViewModel (registered in MainWindow.axaml.cs code-behind).

2. **canStart derives from IStateService.StateChanged only**: Rather than cross-VM reactive chains (Configuration.WhenAnyValue + PluginList.WhenAnyValue), CleaningCommandsViewModel reads hasPlugins, hasXEditPath, and isCleaning all from IStateService.StateChanged. Simpler, no cross-VM dependencies.

3. **Sub-VMs not DI-registered**: Created directly by MainWindowViewModel constructor. No container registration needed. Simpler lifecycle management -- parent Dispose cascades to all three.

4. **StatusText on ConfigurationViewModel**: The status bar text is a configuration/status concern (displays data folder info), not a cleaning concern. Kept on ConfigurationViewModel.

5. **ResetSettingsCommand on ConfigurationViewModel**: Although the plan listed it under CleaningCommandsViewModel, it's a settings operation that calls _configService.ResetSettingsAsync(). Placed on ConfigurationViewModel where it logically belongs.

6. **IPluginLoadingService removed from CleaningCommandsViewModel**: The plan listed it as a constructor param, but CleaningCommandsViewModel doesn't need it -- canStart reads from IStateService, not from plugin loading. Removed to avoid unnecessary coupling.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] XAML bindings moved to Task 1 (from planned Task 2)**
- **Found during:** Task 1 (extract sub-ViewModels)
- **Issue:** Avalonia compiled bindings (AvaloniaUseCompiledBindingsByDefault=true) cause build errors when binding targets don't exist. After extracting sub-VMs, the XAML referenced non-existent properties on MainWindowViewModel, failing Task 1's verification ("dotnet build succeeds").
- **Fix:** Updated all XAML bindings with dotted path notation in Task 1 to satisfy build verification. Task 2 focused on code-behind and test migration.
- **Files modified:** AutoQAC/Views/MainWindow.axaml
- **Verification:** `dotnet build` passes with zero errors
- **Committed in:** ba42c1b (Task 1 commit)

**2. [Rule 3 - Blocking] Code-behind GameDataFolder path fix**
- **Found during:** Task 1 (extract sub-ViewModels)
- **Issue:** MainWindow.axaml.cs line 196 accessed `vm?.GameDataFolder` which no longer exists on MainWindowViewModel (moved to ConfigurationViewModel)
- **Fix:** Changed to `vm?.Configuration.GameDataFolder`
- **Files modified:** AutoQAC/Views/MainWindow.axaml.cs
- **Verification:** `dotnet build` passes
- **Committed in:** ba42c1b (Task 1 commit)

**3. [Rule 3 - Blocking] ErrorDialogTests.cs not listed in plan but required migration**
- **Found during:** Task 2 (migrate tests)
- **Issue:** ErrorDialogTests.cs references MainWindowViewModel properties (XEditPath, StartCleaningCommand, etc.) that moved to sub-VMs. Not listed in plan's files_modified.
- **Fix:** Updated all property/command access paths in ErrorDialogTests.cs to use sub-VM notation (vm.Configuration.XEditPath, vm.Commands.StartCleaningCommand, etc.)
- **Files modified:** AutoQAC.Tests/ViewModels/ErrorDialogTests.cs
- **Verification:** `dotnet test` -- all 461 tests pass
- **Committed in:** 4977cfe (Task 2 commit)

---

**Total deviations:** 3 auto-fixed (3 blocking)
**Impact on plan:** All auto-fixes were necessary for builds and tests to pass. No scope creep. The XAML timing change was forced by compiled binding requirements.

## Issues Encountered

- **IPluginLoadingService on CleaningCommandsViewModel:** Plan specified this as a constructor param, but at extraction time the dependency wasn't needed. The canStart observable reads entirely from IStateService.StateChanged (hasPlugins, hasXEditPath, isCleaning). Removed to keep dependencies minimal.
- **MainWindowViewModelInitializationTests.cs:** Plan listed this as needing migration, but after examination, these tests only verify service mock calls during construction (not property access). No changes required.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Sub-ViewModel structure in place for Phase 6 Plan 2 (About dialog, logging improvements)
- New commands/properties should target the appropriate sub-VM (configuration -> ConfigurationViewModel, cleaning workflow -> CleaningCommandsViewModel, plugin display -> PluginListViewModel)
- Test pattern established: construct MainWindowViewModel, access through vm.Configuration.X / vm.Commands.X / vm.PluginList.X

---
*Phase: 06-ui-polish-monitoring*
*Completed: 2026-02-07*

## Self-Check: PASSED
