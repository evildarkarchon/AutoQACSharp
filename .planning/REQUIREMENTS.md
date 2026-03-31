# Requirements: AutoQAC — xEdit Log Parsing Fix

**Defined:** 2026-03-30
**Core Value:** Correctly parse xEdit cleaning results from log files so users get accurate feedback on what was cleaned, skipped, removed, or undeleted.

## v1 Requirements

Requirements for this bugfix milestone. Each maps to roadmap phases.

### Log File Naming

- [x] **LOG-01**: Service resolves correct log filename using game-aware prefix mapping (e.g., `SSEEdit_log.txt` for SkyrimSe, not executable-stem-based)
- [x] **LOG-02**: Service supports universal `xEdit.exe` with game flags by using game type, not executable name, to determine log filename
- [x] **LOG-03**: Service resolves exception log filename from game-aware prefix (e.g., `SSEEditException.log`)

### Offset-Based Reading

- [x] **OFF-01**: Service captures log file byte offset (via `FileInfo.Length`) before xEdit launch
- [x] **OFF-02**: Service reads only new content appended after the captured offset using `FileStream.Seek` with `FileShare.ReadWrite`
- [x] **OFF-03**: Service handles missing log file gracefully (first run, file doesn't exist yet — offset = 0)
- [x] **OFF-04**: Service retries file read on `IOException` with backoff (file contention from antivirus/indexer)

### Result Parsing

- [ ] **PAR-01**: Existing regex patterns (`Removing:`, `Undeleting:`, `Skipping:`, `Making Partial Form:`) are applied to log file content instead of stdout
- [ ] **PAR-02**: Parser detects "nothing to clean" state (completion lines present but zero cleaning actions)
- [ ] **PAR-03**: Exception log content is surfaced in cleaning results when present

### Process Layer

- [ ] **PRC-01**: `ProcessExecutionService` no longer redirects stdout/stderr (xEdit writes nothing there)
- [ ] **PRC-02**: MO2 wrapping continues to work correctly without stdout redirection

### Orchestration

- [ ] **ORC-01**: `CleaningOrchestrator` captures log offset before launching xEdit and reads log after process exit
- [ ] **ORC-02**: Orchestrator checks process termination status before attempting log read (force-killed xEdit writes no log)
- [ ] **ORC-03**: Hang detection and "running" status continue to display during xEdit execution

### Cleanup

- [ ] **CLN-01**: Dead stdout parsing code paths removed from `CleaningService`
- [ ] **CLN-02**: Old timestamp-based log staleness detection replaced by offset-based approach
- [ ] **CLN-03**: Stale test mocks and unused parameters cleaned up

## v2 Requirements

Deferred to future milestone. Tracked but not in current roadmap.

### Enhanced Parsing

- **ENH-01**: Parse per-record detail from Removing/Undeleting lines for detailed result display
- **ENH-02**: Parse deleted navmesh warnings for actionable user guidance
- **ENH-03**: Parse elapsed time and processed/removed counts from summary lines for cross-validation
- **ENH-04**: Parse "Can't remove" warnings to surface records xEdit couldn't clean
- **ENH-05**: Support `-R:` custom log path flag for users who redirect xEdit logs

## Out of Scope

| Feature | Reason |
|---------|--------|
| Real-time log tailing during xEdit execution | xEdit writes logs only on exit, not incrementally |
| New UI for raw log display | Scope creep beyond the parsing fix |
| Log file truncation/management | xEdit manages its own log files (truncates at 3MB) |
| LOOT dirty info parsing | Separate LOOT-compatible block, not part of cleaning messages |
| Parallel log reading | Sequential cleaning is a hard requirement |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| LOG-01 | Phase 1 | Complete |
| LOG-02 | Phase 1 | Complete |
| LOG-03 | Phase 1 | Complete |
| OFF-01 | Phase 1 | Complete |
| OFF-02 | Phase 1 | Complete |
| OFF-03 | Phase 1 | Complete |
| OFF-04 | Phase 1 | Complete |
| PRC-01 | Phase 2 | Pending |
| PRC-02 | Phase 2 | Pending |
| PAR-01 | Phase 3 | Pending |
| PAR-02 | Phase 3 | Pending |
| PAR-03 | Phase 3 | Pending |
| ORC-01 | Phase 3 | Pending |
| ORC-02 | Phase 3 | Pending |
| ORC-03 | Phase 3 | Pending |
| CLN-01 | Phase 4 | Pending |
| CLN-02 | Phase 4 | Pending |
| CLN-03 | Phase 4 | Pending |

**Coverage:**
- v1 requirements: 18 total
- Mapped to phases: 18
- Unmapped: 0

---
*Requirements defined: 2026-03-30*
*Last updated: 2026-03-31 after 01-01-PLAN.md completion*
