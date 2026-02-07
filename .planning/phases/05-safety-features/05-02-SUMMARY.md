---
phase: 05-safety-features
plan: 02
subsystem: backup
tags: [backup, restore, file-safety, System.Text.Json, BackupService, RestoreWindow]

# Dependency graph
requires:
  - phase: 02-plugin-pipeline
    provides: "PluginInfo with FullPath, game variant detection, plugin file validation"
  - phase: 04-configuration-enhancement
    provides: "UserConfiguration, ConfigurationService, YAML serialization, IMessageDialogService"
provides:
  - "BackupService with timestamped session directories, per-plugin backup, restore, retention cleanup"
  - "BackupSettings in UserConfiguration (enabled by default, max sessions count)"
  - "RestoreViewModel and RestoreWindow with two-level session/plugin browser"
  - "BackupFailureCallback with 3-choice dialog (skip/abort/continue)"
  - "MO2 mode silent backup skip with log warning"
affects: [06-operational-polish, 07-testing]

# Tech tracking
tech-stack:
  added:
    - System.Text.Json (for session.json metadata serialization)
  patterns:
    - "Timestamped session directories (yyyy-MM-dd_HH-mm-ss) for backup organization"
    - "Session metadata sidecar (session.json) with BackupSession record"
    - "Backup-before-clean integration point in CleaningOrchestrator per-plugin loop"
    - "File.Copy with overwrite:false for backup to prevent accidental overwrites"

key-files:
  created:
    - AutoQAC/Models/Configuration/BackupSettings.cs
    - AutoQAC/Models/BackupSession.cs
    - AutoQAC/Models/BackupResult.cs
    - AutoQAC/Services/Backup/IBackupService.cs
    - AutoQAC/Services/Backup/BackupService.cs
    - AutoQAC/ViewModels/RestoreViewModel.cs
    - AutoQAC/Views/RestoreWindow.axaml
    - AutoQAC/Views/RestoreWindow.axaml.cs
  modified:
    - AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs
    - AutoQAC/Services/Cleaning/CleaningOrchestrator.cs
    - AutoQAC/Models/Configuration/UserConfiguration.cs
    - AutoQAC/ViewModels/MainWindowViewModel.cs
    - AutoQAC/ViewModels/SettingsViewModel.cs
    - AutoQAC/Views/MainWindow.axaml
    - AutoQAC/Views/MainWindow.axaml.cs
    - AutoQAC/Views/SettingsWindow.axaml
    - AutoQAC/Services/UI/IMessageDialogService.cs
    - AutoQAC/Services/UI/MessageDialogService.cs
    - AutoQAC/Infrastructure/ServiceCollectionExtensions.cs

key-decisions:
  - "BackupPlugin uses File.Copy with overwrite:false to prevent accidental overwrites"
  - "MO2 mode silently skips backup with log warning (MO2 manages files through virtual filesystem)"
  - "Individual plugin restore has no confirmation; Restore All requires confirmation dialog"
  - "Backup enabled by default for new users (BackupSettings.Enabled = true)"
  - "Session metadata stored as session.json using System.Text.Json (not YAML)"
  - "Backup root derived from first valid plugin's FullPath directory parent"

patterns-established:
  - "Backup lifecycle: create session dir -> per-plugin backup in loop -> write metadata -> retention cleanup"
  - "Two-level browser pattern: session list -> drill into plugin list for granular restore"
  - "Failure callback pattern with enum choice return for orchestration flow control"

# Metrics
duration: 22min
completed: 2026-02-07
---

# Phase 5 Plan 2: Backup and Restore Summary

**Timestamped plugin backups before xEdit cleaning with session browser, individual/bulk restore, failure dialog, backup settings UI, and MO2 mode skip handling**

## Performance

- **Duration:** 22 min (estimated)
- **Started:** 2026-02-07T06:30:00Z (estimated)
- **Completed:** 2026-02-07T06:52:00Z
- **Tasks:** 3 (2 auto + 1 human-verify checkpoint)
- **Files modified:** 24

## Accomplishments
- BackupService creates timestamped session directories, copies plugin files before xEdit processes them, writes session.json metadata with System.Text.Json
- CleaningOrchestrator integrates backup before each plugin in cleaning loop with MO2 mode detection and failure callback support
- RestoreWindow provides two-level browser (sessions -> plugins) with individual and Restore All functionality
- Backup settings in SettingsWindow with enable/disable toggle, retention count (max sessions), and MO2 mode note
- BackupFailureDialog presents 3-choice decision (Skip Plugin, Abort Session, Continue Without Backup) when backup fails
- Retention cleanup automatically deletes oldest sessions beyond max count after cleaning session completes

## Task Commits

Each task was committed atomically:

1. **Task 1: BackupService, models, orchestrator integration, and DI wiring** - `a7f2938` (feat)
2. **Task 2: RestoreWindow, Settings backup section, MainWindow buttons, and callback wiring** - `e18f505` (feat)
3. **Task 3: Human verification checkpoint** - User approved

## Files Created/Modified
- `AutoQAC/Models/Configuration/BackupSettings.cs` - BackupSettings class with Enabled and MaxSessions properties (default: enabled, 10 sessions)
- `AutoQAC/Models/BackupSession.cs` - BackupSession sealed record with Timestamp, GameType, SessionDirectory, and Plugins collection; BackupPluginEntry sealed record
- `AutoQAC/Models/BackupResult.cs` - BackupResult class with static factory methods (Ok/Failure), BackupFailureChoice enum, BackupFailureCallback delegate
- `AutoQAC/Services/Backup/IBackupService.cs` - Interface with CreateSessionDirectory, BackupPlugin, WriteSessionMetadataAsync, GetBackupSessionsAsync, RestorePlugin, RestoreSession, CleanupOldSessions, GetBackupRoot
- `AutoQAC/Services/Backup/BackupService.cs` - Full implementation with File.Copy, JSON serialization, retention cleanup, error handling
- `AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs` - Added 3-param StartCleaningAsync overload with BackupFailureCallback parameter
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` - Integrated backup logic in per-plugin loop, backup root resolution from first plugin FullPath, session metadata writing, retention cleanup
- `AutoQAC/Models/Configuration/UserConfiguration.cs` - Added Backup property with BackupSettings instance
- `AutoQAC/ViewModels/RestoreViewModel.cs` - Two-level browser ViewModel with Sessions/SelectedSession/SelectedSessionPlugins, LoadSessionsAsync, RestorePluginAsync, RestoreAllAsync, DeleteSessionCommand
- `AutoQAC/Views/RestoreWindow.axaml` - Two-panel layout (session list on left, plugin details on right) with restore and delete buttons
- `AutoQAC/Views/RestoreWindow.axaml.cs` - Standard code-behind with CloseRequested event subscription and LoadSessionsCommand execution on open
- `AutoQAC/ViewModels/MainWindowViewModel.cs` - Added RestoreBackupsCommand, ShowRestoreInteraction, HandleBackupFailureAsync callback wiring
- `AutoQAC/ViewModels/SettingsViewModel.cs` - Added BackupEnabled and BackupMaxSessions properties with validation, unsaved changes tracking, reset to defaults
- `AutoQAC/Views/MainWindow.axaml` - Added "Restore Backups" button on left side of control panel
- `AutoQAC/Views/MainWindow.axaml.cs` - Registered ShowRestoreInteraction handler to open RestoreWindow
- `AutoQAC/Views/SettingsWindow.axaml` - Added "Plugin Backup" section with enable checkbox, sessions count NumericUpDown, MO2 mode note
- `AutoQAC/Services/UI/IMessageDialogService.cs` - Added ShowBackupFailureDialogAsync method signature
- `AutoQAC/Services/UI/MessageDialogService.cs` - Implemented 3-button BackupFailureDialog window with Skip/Abort/Continue choices
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` - Registered IBackupService, RestoreViewModel, RestoreWindow in DI container

## Decisions Made
- BackupPlugin uses `File.Copy` with `overwrite:false` to prevent accidental overwrites -- ensures each backup is unique and fails loudly if file already exists
- MO2 mode silently skips backup with log warning (MO2 manages files through its virtual filesystem) -- backup would copy from MO2's VFS cache, not the real game Data folder
- Individual plugin restore has no confirmation dialog; Restore All requires confirmation -- balances safety with UX (single-file restore is low risk, bulk restore needs confirmation)
- Backup enabled by default for new users (`BackupSettings.Enabled = true`) -- safety-first default protects users from data loss
- Session metadata stored as `session.json` using `System.Text.Json` (not YAML) -- JSON is more appropriate for machine-generated metadata, YAML reserved for human-editable config
- Backup root derived from first valid plugin's FullPath directory parent -- `Path.GetDirectoryName(plugin.FullPath)` gives Data folder, then parent + "AutoQAC Backups" creates backup root next to game install

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Safety Features phase (Phase 5) now complete -- both dry-run preview (05-01) and backup/restore (05-02) delivered
- Ready for Phase 6: Operational Polish (performance monitoring, CPU/memory tracking, advanced logging)
- All 472 existing tests continue to pass
- Backup feature ready for user testing with real plugin cleaning workflows

## Self-Check: PASSED

---
*Phase: 05-safety-features*
*Completed: 2026-02-07*
