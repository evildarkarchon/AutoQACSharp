# Roadmap: AutoQAC -- xEdit Log Parsing Fix

## Overview

AutoQAC currently captures xEdit's stdout (which is always empty) and attempts to parse cleaning results from nothing. This milestone fixes the data pipeline by building game-aware log file reading from the bottom up: first the log file service (naming + offset), then the process layer (stop capturing nothing), then orchestrator integration (wire it all together), and finally dead code removal. Each phase is independently testable and the fix is surgical -- four existing services modified, zero new services created.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [ ] **Phase 1: Foundation -- Game-Aware Log File Service** - Build correct log file naming and offset-based reading as the foundation for all downstream parsing
- [ ] **Phase 2: Process Layer -- Stop Stdout Capture** - Remove dead stdout/stderr redirection from ProcessExecutionService
- [ ] **Phase 3: Integration -- Log-First Parsing** - Wire the orchestrator to read log files post-exit and parse results from log content
- [ ] **Phase 4: Cleanup -- Remove Dead Code** - Remove old stdout parsing paths, stale detection, and unused test mocks

## Phase Details

### Phase 1: Foundation -- Game-Aware Log File Service
**Goal**: The log file service can resolve correct filenames for any game type and read only new content from appended log files
**Depends on**: Nothing (first phase)
**Requirements**: LOG-01, LOG-02, LOG-03, OFF-01, OFF-02, OFF-03, OFF-04
**Success Criteria** (what must be TRUE):
  1. Given any supported game type (SkyrimLe, SkyrimSe, SkyrimVr, Fallout4, Fallout4Vr, Fallout3, FalloutNewVegas, Oblivion), the service returns the correct log filename matching xEdit's internal naming convention
  2. When a user runs universal `xEdit.exe` with a game flag, the service resolves the log filename from game type (not executable name), so `xEdit.exe -SSE` yields `SSEEdit_log.txt`
  3. After xEdit exits, the service reads only the new content appended during that run, not historical entries from prior sessions
  4. When the log file does not yet exist (first run), the service handles this gracefully and reads the entire file after exit
  5. When the log file is briefly locked by antivirus or Windows indexer after xEdit exits, the service retries and succeeds without crashing
**Plans**: 2 plans

Plans:
- [ ] 01-01-PLAN.md -- Define contracts (LogReadResult model + IXEditLogFileService interface) and implement game-aware XEditLogFileService with offset-based reading
- [ ] 01-02-PLAN.md -- Rewrite XEditLogFileServiceTests with comprehensive coverage for all 7 phase requirements

### Phase 2: Process Layer -- Stop Stdout Capture
**Goal**: ProcessExecutionService no longer redirects stdout/stderr, eliminating the dead capture that produces empty output
**Depends on**: Nothing (independent of Phase 1)
**Requirements**: PRC-01, PRC-02
**Success Criteria** (what must be TRUE):
  1. xEdit launches and completes successfully without stdout/stderr redirection, and the process exits normally
  2. MO2-wrapped xEdit launches continue to work correctly (process lifecycle, PID tracking, WaitForExitAsync all intact)
**Plans**: TBD

Plans:
- [ ] 02-01: TBD

### Phase 3: Integration -- Log-First Parsing
**Goal**: The cleaning orchestrator reads xEdit results from log files after process exit, replacing the broken stdout-based pipeline with accurate log-based parsing
**Depends on**: Phase 1, Phase 2
**Requirements**: PAR-01, PAR-02, PAR-03, ORC-01, ORC-02, ORC-03
**Success Criteria** (what must be TRUE):
  1. After a cleaning run completes, the user sees accurate ITM/UDR/deleted navmesh counts parsed from the xEdit log file
  2. When a plugin is already clean (xEdit reports completion but zero cleaning actions), the user sees a "nothing to clean" status instead of misleading zero counts
  3. When xEdit crashes and writes an exception log, the error details are surfaced to the user in the cleaning results
  4. When xEdit is force-killed (hang detection triggered), the user sees a failure status instead of the app trying to parse a nonexistent log
  5. During xEdit execution, the user sees a "running" status with hang detection still active
**Plans**: TBD

Plans:
- [ ] 03-01: TBD
- [ ] 03-02: TBD

### Phase 4: Cleanup -- Remove Dead Code
**Goal**: All dead stdout parsing code paths, stale timestamp detection, and unused test infrastructure are removed so the codebase reflects the log-only parsing reality
**Depends on**: Phase 3
**Requirements**: CLN-01, CLN-02, CLN-03
**Success Criteria** (what must be TRUE):
  1. No code path in the application attempts to read or parse xEdit stdout/stderr output
  2. All tests pass with updated mocks that reflect the log-file-only parsing pipeline (no stale stdout-based assertions)
  3. The old timestamp-based log staleness detection is fully replaced by offset-based reading
**Plans**: TBD

Plans:
- [ ] 04-01: TBD

## Progress

**Execution Order:**
Phases execute in numeric order: 1 -> 2 -> 3 -> 4
(Note: Phase 1 and Phase 2 are independent and could execute in parallel)

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Foundation -- Game-Aware Log File Service | 0/2 | Planned | - |
| 2. Process Layer -- Stop Stdout Capture | 0/? | Not started | - |
| 3. Integration -- Log-First Parsing | 0/? | Not started | - |
| 4. Cleanup -- Remove Dead Code | 0/? | Not started | - |
