# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-02-06)

**Core value:** Reliably clean every plugin in a load order with one click, without corrupting game data or cleaning plugins that shouldn't be touched.
**Current focus:** Phase 6 - UI Polish & Monitoring (NEXT)

## Current Position

Phase: 6 of 7 (UI Polish & Monitoring)
Plan: 0 of 3 in current phase
Status: Not started
Last activity: 2026-02-07 -- Completed Phase 5 (Safety Features)

Progress: [############..] 80% (12/15 plans)

## Performance Metrics

**Velocity:**
- Total plans completed: 12
- Average duration: 8.8 minutes
- Total execution time: 1.8 hours

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 1 - Foundation | 2/2 | 15m | 7.5m |
| 2 - Plugin Pipeline | 2/2 | 14.5m | 7.25m |
| 3 - Real-Time Feedback | 3/3 | 30m | 10m |
| 4 - Configuration | 2/2 | 21.5m | 10.75m |
| 5 - Safety Features | 2/2 | 28m | 14m |

**Recent Trend:**
- Last 5 plans: 04-01 (6.5m), 04-02 (15m), 05-01 (6m), 05-02 (22m)
- Trend: Stable

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
- [02-02]: DetectVariant scans load order for TTW/Enderal marker ESMs
- [02-02]: Enderal uses separate "Enderal" key in skip list config, not "SSE"
- [02-02]: MO2 binary path validated from AppState.Mo2ExecutablePath (not UserConfiguration)
- [02-02]: GameType.Unknown throws InvalidOperationException (was: log warning, continue)
- [02-02]: GetSkipListAsync gains GameVariant optional parameter (backward-compatible)
- [03-01]: Log file stats preferred over stdout stats when available; stdout stats kept as fallback
- [03-01]: Single retry after 200ms delay for IOException (xEdit may briefly hold file lock after exit)
- [03-01]: Staleness detection compares log file modification time to process start time (UTC)
- [03-03]: Modal dialog validation replaced with non-modal inline panel per user decision
- [03-03]: InvalidOperationException from orchestrator shown as inline error; generic Exception still uses modal
- [03-03]: ValidatePreClean reads from _stateService.CurrentState for authoritative state
- [03-02]: ObserveOn(MainThreadScheduler) used instead of Sample(100ms) -- dispatcher naturally coalesces rapid updates
- [03-02]: Counter badges show last-completed plugin stats (not live during-clean) since log parsed after exit
- [03-02]: IsCleaning transition detection via _wasPreviouslyCleaning flag for session reset
- [04-01]: SHA256.HashData for config file content hashing (not MD5, not timestamps)
- [04-01]: FSW + Rx Throttle(500ms) for config change detection; triple hash gate prevents circular reloads
- [04-01]: Config changes during cleaning deferred until session ends
- [04-01]: Invalid external YAML edits rejected, previous config kept
- [04-01]: Legacy migration uses backup-then-delete order; backup failure prevents deletion
- [04-01]: Migration is one-time bootstrap only; no merge when C# config exists
- [04-02]: Skip(1) + _isLoading double guard for Rx path validation (prevents constructor emission leak)
- [04-02]: Nullable bool 3-state validation: null=untouched/empty, true=valid, false=invalid
- [04-02]: ValidateLoadedPaths() on settings open for immediate indicators (not deferred to first interaction)
- [04-02]: Direct boolean properties (IsAgeBasedMode/IsCountBasedMode) preferred over IntEqualsConverter for XAML visibility
- [04-02]: Journal Settings removed; Log Retention replaces it as unified retention control
- [05-01]: RunDryRunAsync duplicates validation steps rather than refactoring StartCleaningAsync -- minimal risk to working pipeline
- [05-01]: Preview reuses ProgressWindow with IsPreviewMode flag instead of separate window
- [05-01]: PreviewCommand shares same canStart observable as StartCleaningCommand
- [05-02]: BackupPlugin uses File.Copy with overwrite:false to prevent accidental overwrites
- [05-02]: MO2 mode silently skips backup with log warning (MO2 manages files through virtual filesystem)
- [05-02]: Individual plugin restore has no confirmation; Restore All requires confirmation dialog
- [05-02]: Backup enabled by default for new users (BackupSettings.Enabled = true)
- [05-02]: Session metadata stored as session.json using System.Text.Json (not YAML)
- [05-02]: Backup root derived from first valid plugin's FullPath directory parent

### Pending Todos

- TODO(01-02): Add confirmation dialog for Path A (patient user) force kill prompt in MainWindowViewModel.HandleStopAsync
- TODO(future): Wire IsTerminatingChanged observable to ViewModel for Stopping... spinner UI

### Blockers/Concerns

- [Research]: CPU monitoring accuracy via Process.TotalProcessorTime varies by OS scheduler -- needs empirical validation in Phase 6
- [Research]: FileSystemWatcher reliability with xEdit file locks needs investigation -- may need polling fallback in Phase 6
- [Research]: xUnit v2 required for Avalonia.Headless.XUnit -- do not migrate to xUnit v3

## Session Continuity

Last session: 2026-02-07
Stopped at: Completed Phase 5 (Safety Features)
Resume file: None
