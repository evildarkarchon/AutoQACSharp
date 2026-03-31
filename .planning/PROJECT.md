# AutoQAC — xEdit Log Parsing Fix

## What This Is

AutoQAC is a Windows-only Avalonia desktop app that runs xEdit Quick Auto Clean (`-QAC`) safely, one plugin at a time, with Mutagen-based plugin analysis. This milestone fixes the fundamental bug where the app tries to parse xEdit's stdout/stderr for cleaning results, when xEdit actually writes its output to log files in its install directory.

## Core Value

Correctly parse xEdit cleaning results from log files so users get accurate feedback on what was cleaned, skipped, removed, or undeleted.

## Requirements

### Validated

- ✓ Sequential plugin cleaning via xEdit `-QAC` flag — existing
- ✓ Game detection via registry probing — existing
- ✓ MO2 mode wrapping xEdit with `ModOrganizer.exe run` — existing
- ✓ Mutagen-based plugin discovery for Skyrim/Fallout variants — existing
- ✓ File-based load order for Fallout 3, New Vegas, Oblivion — existing
- ✓ Skip list merging (bundled defaults, user overrides, variant-specific) — existing
- ✓ Optional backup before cleaning (skipped in MO2 mode) — existing
- ✓ CPU-based hang detection during xEdit execution — existing
- ✓ Two-stage stop behavior (graceful then force) — existing
- ✓ YAML-based configuration with file watching and auto-save — existing
- ✓ Plugin issue approximations via Mutagen analysis — existing
- ✓ Avalonia MVVM UI with reactive state management — existing

### Active

None — all milestone requirements validated.

### Recently Validated (Phases 1-4)

- ✓ Parse xEdit cleaning results from log files instead of stdout — Validated in Phase 3
- ✓ Read log files from xEdit install directory after process exit — Validated in Phase 3
- ✓ Handle appending log behavior (track file position before launch, read only new content) — Validated in Phase 1
- ✓ Detect and surface xEdit exceptions from `<basename>Exception.log` — Validated in Phase 1, wired in Phase 3
- ✓ Maintain "running" status with hang detection during xEdit execution — Validated in Phase 3 (unchanged, confirmed working)
- ✓ Parse results post-exit using existing regex patterns against log file content — Validated in Phase 3
- ✓ Dead stdout parsing code paths removed — Validated in Phase 4
- ✓ Timestamp-based log staleness detection replaced by offset-based reading — Validated in Phase 4
- ✓ Stale test mocks and unused parameters cleaned up — Validated in Phase 4

### Out of Scope

- New features or UI enhancements — this milestone is strictly bugfixes
- Real-time line-by-line progress during xEdit execution — xEdit only writes logs on exit
- Changes to the Mutagen submodule or QueryPlugins library
- Parallelizing cleaning work

## Context

**The bug:** The current `ProcessExecutionService` and `IXEditOutputParser` assume xEdit writes to stdout/stderr. xEdit does not — it writes log files to its own install directory on process exit.

**Log file naming:**
- Normal output: `<game>Edit_log.txt` (e.g., `FO4Edit_log.txt`, `SSEEdit_log.txt`)
- Exceptions: `<basename of executable>Exception.log`

**Log behavior:** xEdit appends to log files on exit, so the app must record the file size/offset before launching xEdit and read only the new content after exit.

**Regex patterns:** The existing patterns (`Removing:`, `Undeleting:`, `Skipping:`, `Making Partial Form:`) are correct — they just need to be applied to log file content instead of stdout.

**Progress during execution:** Since no output is available until exit, the UI should show a "running" status indicator while CPU-based hang detection continues to operate.

## Constraints

- **Platform**: Windows-only — xEdit and its log paths are Windows filesystem
- **Sequential**: One xEdit process at a time — no parallelization
- **Read-only Mutagen/**: Do not modify the Mutagen submodule
- **MVVM boundaries**: Service layer reads logs; ViewModels receive parsed results via state

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Parse from log files, not stdout | xEdit does not write to stdout/stderr | Phase 2 removed stdout redirect; Phase 3 will wire log parsing |
| Remove dead stdout/stderr redirection | ProcessExecutionService captured empty streams; xEdit uses log files | Implemented in Phase 2 |
| Offset-based log reading in orchestrator | Per-plugin offset capture isolates each plugin's log output | Implemented in Phase 3 |
| AlreadyClean status for nothing-to-clean | Completion line + zero stats = distinct status, not misleading zeros | Implemented in Phase 3 |
| Force-kill guard before log read | Terminated xEdit may not flush log; skip read and return failure | Implemented in Phase 3 |
| Track file offset before launch | Logs append, need to isolate current run's output | Implemented in Phase 1 |
| Keep hang detection during execution | No stdout progress available, but CPU monitoring still valuable | Confirmed working in Phase 3 |
| Remove dead code after log-first pipeline | Obsolete stdout parsing, timestamp detection, unused params all removed | Implemented in Phase 4 |
| GameType-based log naming (not executable stem) | Supports universal xEdit.exe with game flags; maps to xEdit wbAppName convention | Implemented in Phase 1 |
| Exponential backoff retry for file contention | Windows antivirus/indexer may briefly lock log files after xEdit exits | Implemented in Phase 1 (3 retries, 100/200/400ms) |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd:transition`):
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone** (via `/gsd:complete-milestone`):
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-03-31 after Phase 4 completion — all dead stdout code removed, milestone complete*
