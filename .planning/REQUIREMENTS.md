# Requirements: AutoQACSharp

**Defined:** 2026-02-06
**Core Value:** Reliably clean every plugin in a load order with one click, without corrupting game data or cleaning plugins that shouldn't be touched.

## v1 Requirements

### Process Management

- [ ] **PROC-01**: Graceful subprocess termination with escalation pattern (close → wait → terminate → wait → kill entire tree)
- [ ] **PROC-02**: Reliable stop/cancel that guarantees xEdit is dead before reporting cancelled
- [ ] **PROC-03**: Fix process termination race condition (file handles persisting after disposal)
- [ ] **PROC-04**: Fix CancellationTokenSource lock synchronization (race on null check in StopCleaning + Dispose)
- [ ] **PROC-05**: CPU usage threshold monitoring to detect hung xEdit processes
- [ ] **PROC-06**: Improved subprocess resource management (wait condition pattern, timeout on slot acquisition)

### Progress & Feedback

- [ ] **PROG-01**: Real-time parsed progress callbacks during cleaning (per-record stats, not just raw lines)
- [ ] **PROG-02**: Pipe OutputDataReceived events to ProgressWindow with live xEdit output scrolling
- [ ] **PROG-03**: Enhanced environment validation messages (detailed error strings with actionable guidance)

### Configuration

- [ ] **CONF-01**: Deferred configuration saves (debounced batch writes to prevent deadlocks)
- [ ] **CONF-02**: Configuration validation UI in Settings (real-time path validation with visual feedback)
- [ ] **CONF-03**: Configuration helper methods (get_all, update_multiple, batch operations)
- [ ] **CONF-04**: YAML cache invalidation by file modification time
- [ ] **CONF-05**: Configuration file disk I/O batching (dirty-flag pattern with debounce)
- [ ] **CONF-06**: Fix legacy configuration migration (deletion outside lock, no validation)
- [ ] **CONF-07**: Journal/log expiration setting support

### Plugin Handling

- [ ] **PLUG-01**: Plugin line validation edge cases (separator detection, malformed entries, BOM, encoding)
- [ ] **PLUG-02**: Fix PluginInfo FullPath resolution (FileName used as placeholder)
- [ ] **PLUG-03**: Fix PluginValidationService two code paths (relative vs absolute FullPath)
- [ ] **PLUG-04**: TTW (Tale of Two Wastelands) skip list inheritance from FNV
- [ ] **PLUG-05**: XEdit command building validation (reject GameType.Unknown)

### State Management

- [ ] **STAT-01**: Fix potential lock deadlock in StateService (Lock vs ReaderWriterLockSlim or async patterns)
- [ ] **STAT-02**: Bulk state change optimization (reduce signal overhead for multi-property updates)

### Safety Features

- [ ] **SAFE-01**: Dry-run mode (preview which plugins would be cleaned without invoking xEdit)
- [ ] **SAFE-02**: Plugin backup before cleaning (timestamped copies to backup directory)
- [ ] **SAFE-03**: Backup rollback UI (select backup point, selective plugin restore)

### Game Detection

- [ ] **GAME-01**: Fix game detection fallback path (validate game type before proceeding, reject Unknown)

### UI/UX

- [ ] **UI-01**: About dialog with version, build info, .NET version, links to project
- [ ] **UI-02**: MainWindowViewModel decomposition (extract plugin selection, results display)
- [ ] **UI-03**: Logging improvements (startup info, xEdit commands, session summary)

### Monitoring

- [ ] **MON-01**: Log file monitoring (tail xEdit log files for diagnostics)

### Test Coverage

- [ ] **TEST-01**: ProcessExecutionService process termination edge cases
- [ ] **TEST-02**: Configuration migration failure paths (merge, disk full, permission denied)
- [ ] **TEST-03**: Skip list loading for Unknown GameType
- [ ] **TEST-04**: Concurrent state updates from multiple tasks
- [ ] **TEST-05**: PluginValidationService with non-rooted paths
- [ ] **TEST-06**: Comprehensive test coverage for all new features (target 80% on critical paths)

### Dependencies

- [ ] **DEP-01**: Evaluate and update Mutagen (0.52.0 → latest compatible)
- [ ] **DEP-02**: YamlDotNet security advisory check and update if needed

### Post-Parity

- [ ] **POST-01**: Remove Code_To_Port/ directory after feature parity verified

## v2 Requirements

### Enhanced UX

- **UX-01**: Auto-discovery of xEdit from common install locations (Steam, Program Files)
- **UX-02**: Config export/import for shareable settings (timeouts, skip lists — not paths)
- **UX-03**: Backup retention policy UI (configurable days/count, disk space monitoring)

## Out of Scope

| Feature | Reason |
|---------|--------|
| Parallel plugin cleaning | xEdit file locking makes this impossible — fundamental constraint |
| Auto-updating | Security surface, complexity, and maintenance burden for a modding tool |
| Built-in xEdit | Redistribution concerns, version mismatch issues, separate project |
| Plugin content analysis/preview | Duplicates xEdit's analysis engine, enormous scope for marginal benefit |
| Cloud sync of settings | Config contains machine-specific absolute paths, not portable |
| Plugin dependency resolution | xEdit handles master dependencies internally during QAC |
| Cross-platform support | xEdit is Windows-only |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| PROC-01 | Pending | Pending |
| PROC-02 | Pending | Pending |
| PROC-03 | Pending | Pending |
| PROC-04 | Pending | Pending |
| PROC-05 | Pending | Pending |
| PROC-06 | Pending | Pending |
| PROG-01 | Pending | Pending |
| PROG-02 | Pending | Pending |
| PROG-03 | Pending | Pending |
| CONF-01 | Pending | Pending |
| CONF-02 | Pending | Pending |
| CONF-03 | Pending | Pending |
| CONF-04 | Pending | Pending |
| CONF-05 | Pending | Pending |
| CONF-06 | Pending | Pending |
| CONF-07 | Pending | Pending |
| PLUG-01 | Pending | Pending |
| PLUG-02 | Pending | Pending |
| PLUG-03 | Pending | Pending |
| PLUG-04 | Pending | Pending |
| PLUG-05 | Pending | Pending |
| STAT-01 | Pending | Pending |
| STAT-02 | Pending | Pending |
| SAFE-01 | Pending | Pending |
| SAFE-02 | Pending | Pending |
| SAFE-03 | Pending | Pending |
| GAME-01 | Pending | Pending |
| UI-01 | Pending | Pending |
| UI-02 | Pending | Pending |
| UI-03 | Pending | Pending |
| MON-01 | Pending | Pending |
| TEST-01 | Pending | Pending |
| TEST-02 | Pending | Pending |
| TEST-03 | Pending | Pending |
| TEST-04 | Pending | Pending |
| TEST-05 | Pending | Pending |
| TEST-06 | Pending | Pending |
| DEP-01 | Pending | Pending |
| DEP-02 | Pending | Pending |
| POST-01 | Pending | Pending |

**Coverage:**
- v1 requirements: 39 total
- Mapped to phases: 0
- Unmapped: 39 ⚠️

---
*Requirements defined: 2026-02-06*
*Last updated: 2026-02-06 after initial definition*
