# AutoQAC

## What This Is

AutoQAC is a Windows-only Avalonia desktop app that runs xEdit Quick Auto Clean (`-QAC`) safely, one plugin at a time, with Mutagen-based plugin analysis. It correctly parses xEdit cleaning results from log files, providing accurate feedback on what was cleaned, skipped, removed, or undeleted.

## Core Value

Accurate, automated xEdit Quick Auto Clean with reliable result reporting.

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

None — planning next milestone.

### Out of Scope

- Real-time line-by-line progress during xEdit execution — xEdit only writes logs on exit
- Changes to the Mutagen submodule or QueryPlugins library
- Parallelizing cleaning work
- Offline mode or non-Windows support

## Context

Shipped v1.0 with the xEdit log parsing fix. The app now correctly reads xEdit results from log files (`<game>Edit_log.txt`) using offset-based reading that isolates each plugin's output. All dead stdout parsing code has been removed. 680 tests passing across AutoQAC and QueryPlugins.

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
*Last updated: 2026-03-31 after v1.0 milestone — xEdit log parsing fix shipped*
