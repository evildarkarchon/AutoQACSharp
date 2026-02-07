# Requirements: AutoQACSharp

**Defined:** 2026-02-06
**Core Value:** Reliably clean every plugin in a load order with one click, without corrupting game data or cleaning plugins that shouldn't be touched.

## v1 Requirements

### Process Management

- [x] **PROC-01**: Graceful subprocess termination with escalation pattern (close -> wait -> terminate -> wait -> kill entire tree)
- [x] **PROC-02**: Reliable stop/cancel that guarantees xEdit is dead before reporting cancelled
- [x] **PROC-03**: Fix process termination race condition (file handles persisting after disposal)
- [x] **PROC-04**: Fix CancellationTokenSource lock synchronization (race on null check in StopCleaning + Dispose)
- [ ] **PROC-05**: CPU usage threshold monitoring to detect hung xEdit processes
- [x] **PROC-06**: Improved subprocess resource management (wait condition pattern, timeout on slot acquisition)

### Progress & Feedback

- [x] **PROG-01**: Real-time parsed progress callbacks during cleaning (per-record stats, not just raw lines)
- [x] **PROG-02**: Pipe OutputDataReceived events to ProgressWindow with live xEdit output scrolling
- [x] **PROG-03**: Enhanced environment validation messages (detailed error strings with actionable guidance)

### Configuration

- [x] **CONF-01**: Deferred configuration saves (debounced batch writes to prevent deadlocks)
- [x] **CONF-02**: Configuration validation UI in Settings (real-time path validation with visual feedback)
- [x] **CONF-03**: Configuration helper methods (get_all, update_multiple, batch operations)
- [x] **CONF-04**: YAML cache invalidation by file modification time
- [x] **CONF-05**: Configuration file disk I/O batching (dirty-flag pattern with debounce)
- [x] **CONF-06**: Fix legacy configuration migration (deletion outside lock, no validation)
- [x] **CONF-07**: Journal/log expiration setting support

### Plugin Handling

- [x] **PLUG-01**: Plugin line validation edge cases (separator detection, malformed entries, BOM, encoding)
- [x] **PLUG-02**: Fix PluginInfo FullPath resolution (FileName used as placeholder)
- [x] **PLUG-03**: Fix PluginValidationService two code paths (relative vs absolute FullPath)
- [x] **PLUG-04**: TTW (Tale of Two Wastelands) skip list inheritance from FNV
- [x] **PLUG-05**: XEdit command building validation (reject GameType.Unknown)

### State Management

- [x] **STAT-01**: Fix potential lock deadlock in StateService (Lock vs ReaderWriterLockSlim or async patterns)
- [x] **STAT-02**: Bulk state change optimization (reduce signal overhead for multi-property updates)

### Safety Features

- [x] **SAFE-01**: Dry-run mode (preview which plugins would be cleaned without invoking xEdit)
- [x] **SAFE-02**: Plugin backup before cleaning (timestamped copies to backup directory)
- [x] **SAFE-03**: Backup rollback UI (select backup point, selective plugin restore)

### Game Detection

- [x] **GAME-01**: Fix game detection fallback path (validate game type before proceeding, reject Unknown)

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

- [ ] **DEP-01**: Evaluate and update Mutagen (0.52.0 -> latest compatible)
- [ ] **DEP-02**: YamlDotNet security advisory check and update if needed

### Post-Parity

- [ ] **POST-01**: Remove Code_To_Port/ directory after feature parity verified

## v2 Requirements

### Enhanced UX

- **UX-01**: Auto-discovery of xEdit from common install locations (Steam, Program Files)
- **UX-02**: Config export/import for shareable settings (timeouts, skip lists -- not paths)
- **UX-03**: Backup retention policy UI (configurable days/count, disk space monitoring)

## Out of Scope

| Feature | Reason |
|---------|--------|
| Parallel plugin cleaning | xEdit file locking makes this impossible -- fundamental constraint |
| Auto-updating | Security surface, complexity, and maintenance burden for a modding tool |
| Built-in xEdit | Redistribution concerns, version mismatch issues, separate project |
| Plugin content analysis/preview | Duplicates xEdit's analysis engine, enormous scope for marginal benefit |
| Cloud sync of settings | Config contains machine-specific absolute paths, not portable |
| Plugin dependency resolution | xEdit handles master dependencies internally during QAC |
| Cross-platform support | xEdit is Windows-only |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| PROC-01 | Phase 1 | Complete |
| PROC-02 | Phase 1 | Complete |
| PROC-03 | Phase 1 | Complete |
| PROC-04 | Phase 1 | Complete |
| PROC-05 | Phase 6 | Pending |
| PROC-06 | Phase 1 | Complete |
| PROG-01 | Phase 3 | Complete |
| PROG-02 | Phase 3 | Complete |
| PROG-03 | Phase 3 | Complete |
| CONF-01 | Phase 1 | Complete |
| CONF-02 | Phase 4 | Complete |
| CONF-03 | Phase 4 | Complete |
| CONF-04 | Phase 4 | Complete |
| CONF-05 | Phase 1 | Complete |
| CONF-06 | Phase 4 | Complete |
| CONF-07 | Phase 4 | Complete |
| PLUG-01 | Phase 2 | Complete |
| PLUG-02 | Phase 2 | Complete |
| PLUG-03 | Phase 2 | Complete |
| PLUG-04 | Phase 2 | Complete |
| PLUG-05 | Phase 2 | Complete |
| STAT-01 | Phase 1 | Complete |
| STAT-02 | Phase 3 | Complete |
| SAFE-01 | Phase 5 | Complete |
| SAFE-02 | Phase 5 | Complete |
| SAFE-03 | Phase 5 | Complete |
| GAME-01 | Phase 2 | Complete |
| UI-01 | Phase 6 | Pending |
| UI-02 | Phase 6 | Pending |
| UI-03 | Phase 6 | Pending |
| MON-01 | Phase 6 | Pending |
| TEST-01 | Phase 7 | Pending |
| TEST-02 | Phase 7 | Pending |
| TEST-03 | Phase 7 | Pending |
| TEST-04 | Phase 7 | Pending |
| TEST-05 | Phase 7 | Pending |
| TEST-06 | Phase 7 | Pending |
| DEP-01 | Phase 7 | Pending |
| DEP-02 | Phase 7 | Pending |
| POST-01 | Phase 7 | Pending |

**Coverage:**
- v1 requirements: 40 total
- Mapped to phases: 40
- Unmapped: 0

---
*Requirements defined: 2026-02-06*
*Last updated: 2026-02-07 after Phase 5 completion*
