# AutoQAC

## What This Is

AutoQAC is a Windows-only Avalonia desktop app that runs xEdit Quick Auto Clean (`-QAC`) safely, one plugin at a time, with Mutagen-based plugin analysis. It correctly parses xEdit cleaning results from log files, providing accurate feedback on what was cleaned, skipped, removed, or undeleted.

## Core Value

Accurate, automated xEdit Quick Auto Clean with reliable result reporting.

## Current Milestone: v1.0 Cleanup

**Goal:** Address the concrete risks identified in `.planning/codebase/CONCERNS.md` while preserving sequential xEdit cleaning behavior.

**Target features:**
- Fix high-priority safety bugs in stop/force-kill behavior, command-line escaping, and backup restore failure handling.
- Refactor the highest-risk responsibility concentrations around cleaning orchestration, configuration/plugin refresh, config persistence/watchers, and PID tracking.
- Add coverage for process termination/orphan cleanup, command escaping, config watcher races, and backup restore safety.
- Improve security posture for user-configured executable launches, user-facing error detail, and log/path exposure boundaries.
- Reduce performance risk in approximation refresh, ITM context lookup, config cloning, and backup/retention operations.

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
- ✓ Parse xEdit cleaning results from log files instead of stdout — v1.0
- ✓ Read log files from xEdit install directory after process exit — v1.0
- ✓ Handle appending log behavior (offset-based reading) — v1.0
- ✓ Detect and surface xEdit exceptions from exception log files — v1.0
- ✓ Maintain "running" status with hang detection during xEdit execution — v1.0
- ✓ Parse results post-exit using regex patterns against log content — v1.0
- ✓ Dead stdout parsing code paths fully removed — v1.0
- ✓ Timestamp-based log staleness replaced by offset-based reading — v1.0
- ✓ Stale test mocks and unused parameters cleaned up — v1.0

### Active

- [ ] Fix high-priority safety bugs identified in `.planning/codebase/CONCERNS.md`.
- [ ] Refactor cleanup targets only where doing so reduces concrete regression risk.
- [ ] Add missing safety and regression tests before or alongside risky changes.
- [ ] Improve user-facing security/error boundaries without changing core cleaning behavior.
- [ ] Reduce documented performance bottlenecks without parallelizing xEdit cleaning.

### Out of Scope

- Real-time line-by-line progress during xEdit execution — xEdit only writes logs on exit
- Changes to the Mutagen submodule or QueryPlugins library
- Parallelizing cleaning work
- Offline mode or non-Windows support

## Context

Shipped v1.0 with the xEdit log parsing fix. The app now correctly reads xEdit results from log files (`<game>Edit_log.txt`) using offset-based reading that isolates each plugin's output. All dead stdout parsing code has been removed. 838 tests passing across AutoQAC and QueryPlugins after Phase 07 (779 + 59).

Current cleanup scope is driven by `.planning/codebase/CONCERNS.md` from 2026-04-28, covering safety bugs, refactor debt, test gaps, security polish, and performance bottlenecks. Phase 12 (process-stop-verification-progress-flow-closure) is complete — Progress-window Stop now shares the confirmed two-stage termination contract with main Stop, Hang Kill preserves its immediate action while sharing force-failure reporting, and current stop/PID evidence is captured for SAF-01, SAF-02, REF-04, and TEST-01.

Tech stack: .NET 10, C# 13, Avalonia 11.3, ReactiveUI, Mutagen 0.53.1, Serilog, YamlDotNet.

## Constraints

- **Platform**: Windows-only — xEdit and its log paths are Windows filesystem
- **Sequential**: One xEdit process at a time — no parallelization
- **Read-only Mutagen/**: Do not modify the Mutagen submodule
- **MVVM boundaries**: Service layer reads logs; ViewModels receive parsed results via state

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Parse from log files, not stdout | xEdit does not write to stdout/stderr | ✓ Implemented in v1.0 |
| Remove dead stdout/stderr redirection | ProcessExecutionService captured empty streams | ✓ Implemented in v1.0 |
| Offset-based log reading in orchestrator | Per-plugin offset capture isolates each plugin's log output | ✓ Implemented in v1.0 |
| AlreadyClean status for nothing-to-clean | Completion line + zero stats = distinct status, not misleading zeros | ✓ Implemented in v1.0 |
| Force-kill guard before log read | Terminated xEdit may not flush log; skip read and return failure | ✓ Implemented in v1.0 |
| GameType-based log naming (not executable stem) | Supports universal xEdit.exe with game flags; maps to xEdit wbAppName convention | ✓ Implemented in v1.0 |
| Exponential backoff retry for file contention | Windows antivirus/indexer may briefly lock log files after xEdit exits | ✓ Implemented in v1.0 |
| Remove dead code after log-first pipeline | Obsolete stdout parsing, timestamp detection, unused params all removed | ✓ Implemented in v1.0 |
| Async cancellable backup/restore/retention with structured per-file results | Replace blocking sync APIs with row-level success/failure reporting; preserve sequential xEdit cleaning | ✓ Implemented in Phase 07 |
| Trusted-restore-root containment for restore + Delete Session | Tampered metadata cannot redirect or recursively delete outside the loaded backup root | ✓ Implemented in Phase 07 (Plans 07-09, 07-11, 07-13) |
| Shared `BackupPathContainment.IsContained` helper | Single canonical path-containment policy reused by `BackupService.IsRestoreTargetInsideTrustedRoot` and Delete Session, eliminating duplicated `Path.GetFullPath`+`StartsWith` logic | ✓ Implemented in Phase 07 (Plan 07-14) |
| Filesystem deletion lives in IBackupService, not RestoreViewModel | Cross-AI architectural consensus + project rule "All business logic lives in services, not ViewModels" — recursive `Directory.Delete` moved into `BackupService.DeleteSessionAsync` via `IBackupSessionDeleter` | ✓ Implemented in Phase 07 (Plan 07-13) |
| Defense-in-depth ProgressWindow close/disposal wiring | `MainWindow.ShowProgressAsync` adds `CloseRequested` + `Closed` handlers alongside the pre-existing `ProgressWindow.OnDataContextChanged`/`OnClosed` contract; both paths are idempotent | ✓ Implemented in Phase 07 (Plan 07-12) |
| Serialized user-config persistence authority | One `ConfigPersistenceCoordinator` owns save, flush, reload, watcher, deferral, and failure publication ordering; production DI explicitly shares it between the configuration facade and watcher service | ✓ Implemented in Phase 10 |
| Manual user-config copy graph | User-configuration cloning uses model-owned `Copy()` methods instead of YAML serialization round-trips for in-memory copies | ✓ Implemented in Phase 10 |
| Shared stop termination copy and choice contract | Main Stop, Progress Stop, and Hang Kill force-failure paths use one fixed, Phase 11-safe stop outcome text contract with explicit `Force Terminate` / `Leave Running` labels | ✓ Implemented in Phase 12 |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition:**
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone:**
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-05-01 after Phase 12 (process-stop-verification-progress-flow-closure) completion*
