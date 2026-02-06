# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-02-06)

**Core value:** Reliably clean every plugin in a load order with one click, without corrupting game data or cleaning plugins that shouldn't be touched.
**Current focus:** Phase 2 - Plugin Pipeline Robustness (IN PROGRESS)

## Current Position

Phase: 2 of 7 (Plugin Pipeline Robustness)
Plan: 1 of 2 in current phase
Status: In progress
Last activity: 2026-02-06 -- Completed 02-01-PLAN.md (Plugin Line Validation and FullPath Resolution)

Progress: [###...........] 20% (3/15 plans)

## Performance Metrics

**Velocity:**
- Total plans completed: 3
- Average duration: 7.2 minutes
- Total execution time: 0.36 hours

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 1 - Foundation | 2/2 | 15m | 7.5m |
| 2 - Plugin Pipeline | 1/2 | 6.5m | 6.5m |

**Recent Trend:**
- Last 5 plans: 01-01 (7m), 01-02 (8m), 02-01 (6.5m)
- Trend: Stable/improving

*Updated after each plan completion*

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work:

- [Roadmap]: Phase 1 prioritizes process termination, state deadlock, and deferred config saves -- these are foundational bugs that risk data corruption
- [Roadmap]: FullPath resolution (Phase 2) must complete before backup feature (Phase 5) -- backup cannot copy files it cannot locate
- [Roadmap]: Tests (Phase 7) deferred to end so they cover all new features, with incremental testing during feature phases
- [01-01]: Grace period set to 2500ms; CloseMainWindow false returns GracePeriodExpired immediately
- [01-01]: Path A confirmation dialog deferred to future UI plan; ViewModel auto-escalates for now
- [01-01]: PID file in AutoQAC Data/ directory; orphan validation uses name + start time
- [01-02]: Authoritative _currentState field (volatile) separate from BehaviorSubject for concurrent UpdateState correctness
- [01-02]: Config events emit immediately in SaveUserConfigAsync; disk write deferred to 500ms debounce
- [01-02]: Cross-instance persistence tests must call FlushPendingSavesAsync before creating second service instance
- [02-01]: StreamReader with detectEncodingFromByteOrderMarks replaces File.ReadAllLinesAsync for BOM auto-detection
- [02-01]: 7-step line validation pipeline (blanks, comments, prefix strip, control chars, path separators, extension check)
- [02-01]: ValidatePluginFile returns PluginWarningKind enum instead of bool -- eliminates dual code path
- [02-01]: Non-rooted paths return NotFound from ValidatePluginFile (not optimistic true)

### Pending Todos

- TODO(01-02): Add confirmation dialog for Path A (patient user) force kill prompt in MainWindowViewModel.HandleStopAsync
- TODO(future): Wire IsTerminatingChanged observable to ViewModel for Stopping... spinner UI

### Blockers/Concerns

- [Research]: CPU monitoring accuracy via Process.TotalProcessorTime varies by OS scheduler -- needs empirical validation in Phase 6
- [Research]: FileSystemWatcher reliability with xEdit file locks needs investigation -- may need polling fallback in Phase 6
- [Research]: xUnit v2 required for Avalonia.Headless.XUnit -- do not migrate to xUnit v3

## Session Continuity

Last session: 2026-02-06T23:55Z
Stopped at: Completed 02-01-PLAN.md
Resume file: None
