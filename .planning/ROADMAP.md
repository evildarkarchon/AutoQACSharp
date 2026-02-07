# Roadmap: AutoQACSharp

## Overview

This roadmap hardens AutoQACSharp's existing cleaning pipeline from a functional-but-fragile state into a robust, user-trustworthy tool. The journey starts by fixing critical bugs in process management and state handling that risk data corruption, then builds outward through plugin path resolution, real-time user feedback, configuration improvements, safety features (dry-run and backup), UI polish, and finally comprehensive test coverage with dependency cleanup. Every phase delivers a coherent capability that users can observe and verify.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [x] **Phase 1: Foundation Hardening** - Fix critical process, state, and config bugs that block all subsequent work
- [x] **Phase 2: Plugin Pipeline Robustness** - Resolve plugin path tech debt and validation gaps that block safety features
- [x] **Phase 3: Real-Time Feedback** - Give users live progress and actionable error messages during cleaning sessions
- [x] **Phase 4: Configuration Enhancement** - Batch config operations, cache invalidation, validation UI, and settings cleanup
- [x] **Phase 5: Safety Features** - Dry-run preview and plugin backup/rollback so users trust the tool with their load order
- [ ] **Phase 6: UI Polish & Monitoring** - Decompose bloated ViewModel, add About dialog, logging improvements, and diagnostic monitoring
- [ ] **Phase 7: Hardening & Cleanup** - Comprehensive test coverage, dependency updates, and remove reference code

## Phase Details

### Phase 1: Foundation Hardening
**Goal**: The cleaning pipeline is safe from process ghost handles, state deadlocks, and config data loss -- the foundation every other feature depends on
**Depends on**: Nothing (first phase)
**Requirements**: PROC-01, PROC-02, PROC-03, PROC-04, PROC-06, STAT-01, CONF-01, CONF-05
**Success Criteria** (what must be TRUE):
  1. User can click Stop during cleaning and xEdit is guaranteed dead (no orphan processes, no lingering file handles) before the UI reports "cancelled"
  2. Rapid property changes in the UI (toggling settings, selecting plugins quickly) never cause the application to freeze or deadlock
  3. User can change multiple settings in quick succession and all changes persist to disk even if the application is closed shortly after
  4. Starting a new cleaning session after cancelling a previous one never fails due to leftover process state or locked files
  5. Configuration changes made just before starting a cleaning run are flushed to disk before xEdit launches
**Plans**: 2 plans

Plans:
- [x] 01-01-PLAN.md -- Process termination hardening: escalating stop, PID tracking, orphan cleanup, CTS race fix (PROC-01, PROC-02, PROC-03, PROC-04, PROC-06)
- [x] 01-02-PLAN.md -- State deadlock fix and debounced config saves with pre-clean/shutdown flush (STAT-01, CONF-01, CONF-05)

### Phase 2: Plugin Pipeline Robustness
**Goal**: Every plugin in the load order has a verified real file path, and edge-case inputs are handled gracefully instead of silently failing
**Depends on**: Phase 1
**Requirements**: PLUG-01, PLUG-02, PLUG-03, PLUG-04, PLUG-05, GAME-01
**Success Criteria** (what must be TRUE):
  1. Plugins loaded from file-based load orders (Oblivion, FO3, FNV) have correct absolute paths that point to real files on disk
  2. Load order files with separators, BOM markers, blank lines, or malformed entries are handled without crashing or silently skipping valid plugins
  3. TTW users see Fallout New Vegas skip list entries inherited automatically when their game is detected as TTW
  4. Attempting to clean with GameType.Unknown shows a clear error message instead of proceeding without skip lists
  5. MO2 users see correct plugin paths resolved through the virtual filesystem
**Plans**: 2 plans

Plans:
- [x] 02-01-PLAN.md -- Plugin line validation, encoding-aware parsing, and FullPath resolution (PLUG-01, PLUG-02, PLUG-03)
- [x] 02-02-PLAN.md -- Game variant detection (TTW/Enderal), skip list inheritance, Unknown rejection, aggregated errors (PLUG-04, PLUG-05, GAME-01)

### Phase 3: Real-Time Feedback
**Goal**: Users see live cleaning progress with per-plugin stats parsed from xEdit log files and receive actionable error messages when something is misconfigured
**Depends on**: Phase 1 (robust process management for reliable output streaming)
**Requirements**: PROG-01, PROG-02, PROG-03, STAT-02
**Success Criteria** (what must be TRUE):
  1. During cleaning, the progress window shows live ITM/UDR/navmesh counts updating as xEdit processes each plugin (not just after completion)
  2. xEdit runs visibly so the user can see its output directly; AutoQAC parses the xEdit log file after each plugin completes for statistics
  3. When environment validation fails (missing xEdit, bad load order path, invalid MO2 config), the error message tells the user exactly what is wrong and how to fix it
  4. Cleaning sessions with 100+ plugins do not cause UI lag from excessive property change notifications
**Plans**: 3 plans

Plans:
- [x] 03-01-PLAN.md -- xEdit log file service, orchestrator wiring, DetailedPluginResult observable (PROG-01, PROG-02, STAT-02 backend)
- [x] 03-02-PLAN.md -- Progress window redesign with counter badges, summary bar, and results transformation (PROG-01, STAT-02 frontend)
- [x] 03-03-PLAN.md -- Inline pre-clean validation error panel with actionable messages (PROG-03)

### Phase 4: Configuration Enhancement
**Goal**: Configuration management is efficient, validated, and gives users confidence their settings are correct before they start cleaning
**Depends on**: Phase 1 (deferred saves foundation)
**Requirements**: CONF-02, CONF-03, CONF-04, CONF-06, CONF-07
**Success Criteria** (what must be TRUE):
  1. In Settings, file path fields show real-time visual feedback (valid/invalid indicators) as the user types or browses
  2. Legacy configuration files from the Python version are migrated safely with validation, and failures produce a clear warning instead of silent data loss
  3. YAML configuration is re-read from disk only when the file has actually changed (not on every access)
  4. Journal/log expiration settings are available and old log files are cleaned up according to the configured retention period
**Plans**: 2 plans

Plans:
- [x] 04-01-PLAN.md -- Config helpers, config watcher (FSW + SHA256), and legacy migration rewrite (CONF-03, CONF-04, CONF-06)
- [x] 04-02-PLAN.md -- Settings path validation UI, log retention service, and migration warning banner (CONF-02, CONF-07)

### Phase 5: Safety Features
**Goal**: Users can preview what will happen before committing, and have a safety net to undo cleaning if something goes wrong
**Depends on**: Phase 2 (FullPath resolution required for backup file copying), Phase 1 (process handles released for safe file operations)
**Requirements**: SAFE-01, SAFE-02, SAFE-03
**Success Criteria** (what must be TRUE):
  1. User can run a dry-run that shows exactly which plugins would be cleaned, which would be skipped, and why -- without invoking xEdit
  2. Dry-run results are clearly labeled as a preview (not cleaning results) and document what dry-run does NOT verify
  3. When backup is enabled, every plugin is copied to a timestamped backup directory before xEdit touches it
  4. User can browse previous backup sessions and restore individual plugins to their pre-cleaning state
  5. Backup enable/disable and retention settings are configurable in Settings
**Plans**: 2 plans

Plans:
- [x] 05-01-PLAN.md -- Dry-run preview mode: DryRunResult model, RunDryRunAsync orchestrator method, Preview button, progress window preview panel (SAFE-01)
- [x] 05-02-PLAN.md -- Backup service, orchestrator integration, restore browser window, settings backup section, failure callback dialog (SAFE-02, SAFE-03)

### Phase 6: UI Polish & Monitoring
**Goal**: The UI is well-organized, informative, and provides diagnostic tools for troubleshooting xEdit issues
**Depends on**: Phase 3 (progress pipeline for monitoring integration), Phase 1 (robust process handles for CPU monitoring)
**Requirements**: UI-01, UI-02, UI-03, PROC-05, MON-01
**Success Criteria** (what must be TRUE):
  1. About dialog shows application version, build info, .NET runtime version, and links to the project
  2. MainWindowViewModel is decomposed into focused sub-ViewModels, and the application behaves identically to before the split
  3. Application startup logs xEdit path, game type, MO2 status, and load order size; session completion logs a summary of results
  4. When xEdit appears hung (CPU usage near zero for an extended period), the application detects it and offers the user a choice to wait or terminate
  5. xEdit log file contents are accessible for diagnostics without leaving the application
**Plans**: TBD

Plans:
- [ ] 06-01: MainWindowViewModel decomposition (UI-02)
- [ ] 06-02: About dialog and logging improvements (UI-01, UI-03)
- [ ] 06-03: CPU monitoring and log file tailing (PROC-05, MON-01)

### Phase 7: Hardening & Cleanup
**Goal**: Critical paths have 80%+ test coverage, dependencies are current, and the reference implementation code is removed
**Depends on**: All previous phases (tests cover features built in phases 1-6)
**Requirements**: TEST-01, TEST-02, TEST-03, TEST-04, TEST-05, TEST-06, DEP-01, DEP-02, POST-01
**Success Criteria** (what must be TRUE):
  1. Process termination edge cases (kill failures, timeout, orphan detection) have dedicated test coverage
  2. Configuration migration, skip list loading, concurrent state updates, and non-rooted path validation all have test coverage for failure paths
  3. New features from phases 1-6 have tests achieving 80%+ coverage on critical paths
  4. Mutagen and YamlDotNet are updated to latest compatible versions with no regressions
  5. Code_To_Port/ directory is removed and the application builds and passes all tests without it
**Plans**: TBD

Plans:
- [ ] 07-01: Targeted test coverage for existing gaps (TEST-01, TEST-02, TEST-03, TEST-04, TEST-05)
- [ ] 07-02: Feature test coverage, dependency updates, and cleanup (TEST-06, DEP-01, DEP-02, POST-01)

## Progress

**Execution Order:**
Phases execute in numeric order: 1 -> 2 -> 3 -> 4 -> 5 -> 6 -> 7

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Foundation Hardening | 2/2 | Complete | 2026-02-06 |
| 2. Plugin Pipeline Robustness | 2/2 | Complete | 2026-02-06 |
| 3. Real-Time Feedback | 3/3 | Complete | 2026-02-07 |
| 4. Configuration Enhancement | 2/2 | Complete | 2026-02-07 |
| 5. Safety Features | 2/2 | Complete | 2026-02-07 |
| 6. UI Polish & Monitoring | 0/3 | Not started | - |
| 7. Hardening & Cleanup | 0/2 | Not started | - |
