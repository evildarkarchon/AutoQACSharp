# Project Retrospective

*A living document updated after each milestone. Lessons feed forward into future planning.*

## Milestone: v1.0 — xEdit Log Parsing Fix

**Shipped:** 2026-03-31
**Phases:** 4 | **Plans:** 7 | **Tasks:** 13

### What Was Built
- Game-aware log file service with offset-based reading for all 8 supported game types
- Comprehensive test suite (680 tests) covering game naming, offset isolation, retry, and edge cases
- Rewired CleaningOrchestrator to read xEdit results from log files post-exit instead of empty stdout
- AlreadyClean status detection for plugins with nothing to clean
- Full dead code removal: obsolete methods, unused parameters, stale test mocks

### What Worked
- Bottom-up phase ordering (foundation → process → integration → cleanup) made each phase independently testable with no regressions
- Detailed CONTEXT.md with canonical file references and line numbers gave executors precise targets, reducing exploratory work
- Phase 1 and Phase 2 being independent allowed fast parallel discussion/planning
- Offset-based reading design cleanly isolated per-plugin log output without needing log file truncation or rotation

### What Was Inefficient
- Phase 1 summaries used a non-standard format missing the `one_liner` field, requiring manual extraction during milestone completion
- Phase 2 and 3 roadmap checkboxes weren't auto-marked `[x]` during execution, requiring manual correction

### Patterns Established
- Primary constructor injection for service classes (C# 13 style)
- `sealed record` for immutable state with `with` expressions for transitions
- Offset-based reading pattern for xEdit log files (capture before launch, read delta after exit)
- `CleaningStatus.AlreadyClean` as a success variant, not a separate category

### Key Lessons
1. xEdit writes absolutely nothing to stdout/stderr — any code that captures process output from xEdit is dead by definition
2. Windows file contention (antivirus, indexer) is real — exponential backoff retry is essential for any file I/O immediately after process exit
3. Dead code removal is safest as a final phase after the new pipeline is fully integrated and verified

### Cost Observations
- Model mix: 100% Opus (quality profile)
- Sessions: 4 (one per phase, plus milestone completion)
- Notable: Cleanup phase (Phase 4) was the fastest — pure deletion with well-defined boundaries

---

## Cross-Milestone Trends

### Process Evolution

| Milestone | Sessions | Phases | Key Change |
|-----------|----------|--------|------------|
| v1.0 | 4 | 4 | First milestone — established GSD workflow with bottom-up phase ordering |

### Cumulative Quality

| Milestone | Tests | Coverage | Zero-Dep Additions |
|-----------|-------|----------|-------------------|
| v1.0 | 680 | 52% line / 52% branch | 0 (bugfix milestone, no new dependencies) |

### Top Lessons (Verified Across Milestones)

1. Bottom-up phase ordering (foundation first, integration last, cleanup final) prevents regressions and enables independent testing
2. Canonical file references with line numbers in planning documents dramatically reduce executor exploration overhead
