---
phase: 04-configuration-enhancement
plan: 01
subsystem: configuration
tags: [config-watcher, legacy-migration, retention-settings, sha256, fsw, yaml]
dependency-graph:
  requires: [01-02]
  provides: [config-watcher-service, legacy-migration-service, retention-model, config-batch-helpers]
  affects: [04-02, 05, 06]
tech-stack:
  added: []
  patterns: [FileSystemWatcher+Rx, SHA256-hash-gating, backup-then-delete-migration]
key-files:
  created:
    - AutoQAC/Models/Configuration/RetentionSettings.cs
    - AutoQAC/Services/Configuration/IConfigWatcherService.cs
    - AutoQAC/Services/Configuration/ConfigWatcherService.cs
    - AutoQAC/Services/Configuration/ILegacyMigrationService.cs
    - AutoQAC/Services/Configuration/LegacyMigrationService.cs
  modified:
    - AutoQAC/Models/Configuration/UserConfiguration.cs
    - AutoQAC/Services/Configuration/IConfigurationService.cs
    - AutoQAC/Services/Configuration/ConfigurationService.cs
    - AutoQAC/Infrastructure/ServiceCollectionExtensions.cs
    - AutoQAC/App.axaml.cs
    - AutoQAC/ViewModels/MainWindowViewModel.cs
decisions:
  - SHA256.HashData used for content hashing (not MD5, not file timestamps)
  - ConfigWatcherService uses FSW Changed events throttled at 500ms via Rx
  - Invalid external YAML edits are rejected with warning log, keeping previous config
  - Config changes during cleaning are deferred until session ends
  - Migration uses backup-then-delete order; backup failure prevents deletion
  - Migration is one-time bootstrap only; no merge when C# config exists
metrics:
  duration: 6.5m
  completed: 2026-02-07
---

# Phase 4 Plan 1: Config Watcher, Migration, and Helpers Summary

**Config watcher with FSW + SHA256 hash gating, legacy migration rewrite with backup-then-delete, and batch config helpers for Settings UI**

## What Was Done

### Task 1: RetentionSettings model, config helpers, and config watcher service

**RetentionSettings Model:**
- Created `RetentionMode` enum with `AgeBased` and `CountBased` values
- Created `RetentionSettings` class with `Mode`, `MaxAgeDays` (default 30), `MaxFileCount` (default 50)
- Added `LogRetention` property to `UserConfiguration` with YAML alias `Log_Retention`

**Config Helper Methods (IConfigurationService):**
- `GetAllSettingsAsync` -- returns flat dictionary of all user-facing settings (13 keys including nested LogRetention properties)
- `UpdateMultipleAsync` -- loads config, applies action delegate, saves once (replaces load-mutate-save cycles)
- `ReloadFromDiskAsync` -- clears all caches, reads fresh from disk, emits via observable
- `GetLastWrittenHash()` -- returns SHA256 hex hash of last app-initiated save

**ConfigWatcherService:**
- FileSystemWatcher monitors `AutoQAC Settings.yaml` for LastWrite and Size changes
- FSW events piped through `Observable.FromEventPattern` -> `Throttle(500ms)` -> `ObserveOn(TaskpoolScheduler)` -> handler
- Triple hash gate: (1) skip if matches app's last written hash, (2) skip if matches last known external hash, (3) skip if invalid YAML
- Cleaning-session deferral: sets `_hasDeferredChanges` flag, subscribes to `IsCleaning` transition to apply when cleaning ends
- YAML validation before reload rejects corrupt external edits

**ConfigurationService changes:**
- SHA256 hash computed after every successful disk save
- Removed `MigrateLegacyConfigAsync` and `_migrationCompleted` (extracted to dedicated service)
- Removed `LegacyUserConfigFile` constant

### Task 2: Legacy migration service rewrite and migration banner wiring

**LegacyMigrationService:**
- Detects `AutoQAC Config.yaml` on every startup
- Only migrates when no `AutoQAC Settings.yaml` exists (one-time bootstrap, not merge)
- 6-step flow: detect -> check-existing -> parse -> write-new -> backup -> delete
- Backup-then-delete order fixes CONF-06 bug (backup failure prevents deletion)
- Backup stored in `migration_backup/` with timestamp prefix
- Returns structured `MigrationResult` record with Attempted, Success, WarningMessage, MigratedFiles, FailedFiles

**MainWindowViewModel migration banner:**
- `HasMigrationWarning` and `MigrationWarningMessage` reactive properties
- `DismissMigrationWarningCommand` to dismiss the banner
- `ShowMigrationWarning(string)` public method called from App.axaml.cs

**App.axaml.cs wiring:**
- Resolves and starts `IConfigWatcherService` after service provider built
- Resolves `ILegacyMigrationService` and runs `MigrateIfNeededAsync` (fire-and-forget)
- Failed migration shows warning banner via `ShowMigrationWarning`
- Config watcher disposed on shutdown

## Task Commits

| Task | Name | Commit | Key Files |
|------|------|--------|-----------|
| 1 | RetentionSettings, config helpers, config watcher | e52e483 | RetentionSettings.cs, ConfigWatcherService.cs, ConfigurationService.cs |
| 2 | Legacy migration service and banner wiring | 1c58ce0 | LegacyMigrationService.cs, App.axaml.cs, MainWindowViewModel.cs |

## Decisions Made

1. **SHA256 for content hashing** -- `SHA256.HashData(stream)` with `Convert.ToHexString()` for file hash comparison; fast enough and cryptographically robust
2. **FSW + Rx throttle at 500ms** -- handles Windows FSW duplicate events; Rx pipeline simplifies async composition
3. **Triple hash gate** -- app-written hash, last-known external hash, and YAML validation prevent circular reloads, duplicate events, and corrupt edits
4. **Cleaning deferral** -- deferred changes applied via `StateChanged.Select(s => s.IsCleaning).DistinctUntilChanged().Where(!cleaning && deferred)`
5. **Backup-then-delete migration** -- backup failure prevents deletion; partial success returns warning
6. **One-time bootstrap** -- migration skipped when C# config exists, no merge logic

## Deviations from Plan

None -- plan executed exactly as written.

## Test Results

All 472 existing tests pass with zero failures and zero regressions.

## Next Phase Readiness

Plan 04-02 can proceed immediately. The following services are available for its Settings UI expansion:
- `IConfigWatcherService` for external change detection
- `ILegacyMigrationService` for migration status display
- `GetAllSettingsAsync` and `UpdateMultipleAsync` for batch settings management
- `RetentionSettings` model for log retention UI controls
- `HasMigrationWarning` / `MigrationWarningMessage` for the migration warning banner in the UI

## Self-Check: PASSED
