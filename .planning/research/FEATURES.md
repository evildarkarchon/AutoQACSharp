# Feature Research

**Domain:** Desktop batch automation tool for Bethesda game plugin cleaning (xEdit subprocess management)
**Researched:** 2026-02-06
**Confidence:** HIGH

## Context: What Already Exists

The C# implementation (AutoQACSharp) already has core cleaning functionality:
- Sequential plugin processing via xEdit subprocess
- CancellationToken-based stop/cancel
- Basic progress tracking (count/total, current plugin name)
- Timeout handling with retry prompts
- Skip list management (game-specific + universal)
- xEdit output parsing (ITMs, UDRs, partial forms)
- Session results with per-plugin statistics
- Serilog-based rotating file logs
- MO2 mode support
- Game auto-detection
- Configuration via YAML (MainConfig + UserConfig)
- CleaningResultsWindow showing session summary
- ProgressWindow with stop button and basic log output

The Python reference implementation has additional features not yet ported:
- Deferred config saves (QTimer-based debouncing to prevent deadlocks)
- Real-time parsed progress (line-by-line xEdit output piped to UI with stat extraction)
- CPU monitoring (`check_process` via psutil)
- Subprocess resource management (semaphore limiting concurrent processes)
- Graceful termination escalation (terminate -> wait -> kill)
- Comprehensive cleanup on shutdown

## Feature Landscape

### Table Stakes (Users Expect These)

Features users assume exist. Missing these = product feels incomplete or untrustworthy.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| **Reliable stop/cancel** | Users batch 100-500 plugins (30min-4hr sessions). They MUST be able to stop and walk away with confidence their data is intact. Current implementation has CancellationToken but no guaranteed subprocess cleanup. | MEDIUM | Python ref uses terminate -> wait(0.5s) -> kill escalation. C# has `TerminateProcessGracefullyAsync` but the cancellation path doesn't guarantee the process is killed before moving on. Must ensure xEdit is dead before reporting cancelled. |
| **Real-time progress feedback** | At 30s-5min per plugin, users stare at a window for a long time. "Processing: plugin.esp" with no activity indication feels frozen. Users need per-plugin sub-progress (seeing xEdit output scroll). | MEDIUM | Python ref parses each output line in real-time and emits `plugin_progress` signal with stats dict. C# has `IProgress<string>` wired but the ProgressViewModel only appends `[HH:mm:ss] plugin: status` -- no real-time xEdit output. Need to pipe `OutputDataReceived` events to the progress UI. |
| **Graceful subprocess termination** | xEdit holds file locks on plugins. If the process is killed mid-write, the plugin file could be corrupted. Users expect that "Stop" means "finish what you're doing safely, then stop." | HIGH | C# has `CloseMainWindow()` -> wait -> `Kill()` pattern in `ProcessExecutionService`. But xEdit is a Delphi app running with `CreateNoWindow=true`, so `CloseMainWindow()` may be a no-op. Need to verify behavior. `Process.Kill(true)` on .NET 9 terminates child processes too. This is critical for user trust. |
| **Environment validation messages** | Users need clear, actionable messages when paths are wrong. "xEdit not found" is not enough -- they need "xEdit executable not found at: C:\path\here. Please update in Settings." | LOW | C# has `ValidateEnvironmentAsync` returning bool. Python ref returns `(bool, str)` with descriptive messages. The C# orchestrator already checks paths but error messages to the user may be generic. |
| **Configuration persistence (deferred saves)** | Config changes during cleaning or rapid UI interaction must not deadlock or corrupt YAML files. | MEDIUM | Python ref uses QTimer(100ms) debounce with mutex-protected pending saves list. C# uses `SaveUserConfigAsync` but has no debouncing. If called rapidly from reactive property changes, could cause file contention. |
| **Session result summary** | After cleaning 200+ plugins, users need a clear breakdown: X cleaned, Y skipped, Z failed, with per-plugin details. | LOW | **Already implemented** via `CleaningSessionResult` and `CleaningResultsWindow`. This is table stakes and it exists. May need polish (duration display, export capability). |
| **Plugin validation edge cases** | Load order files have inconsistent formatting across tools (MO2, Vortex, manual). Lines with separators, extra whitespace, BOM markers, mixed encodings. | MEDIUM | Python ref handles prefix chars (`*`, `+`, `-`), separator chars (`,`, `;`), and content after extensions. C# `PluginLoadingService` exists but needs verification against all edge cases the Python ref handles. |
| **Logging (structured, rotated, useful)** | When something goes wrong, users and support need logs that tell the story. Must log: session start/end, each plugin attempt, xEdit commands, output, errors, timings. | LOW | **Already implemented** via Serilog with rotating files, 5MB limit, 5-day retention. May need: startup info logging (version, platform, working directory) like the Python ref's `log_startup_info`. |
| **About dialog** | Users need to know what version they're running, for bug reports and updates. Standard desktop application convention. | LOW | Not yet implemented. Simple dialog showing version, build info, links to project/issues page. |

### Differentiators (Competitive Advantage)

Features that set the product apart from the Python reference. Not required, but valuable.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| **Dry-run mode** | Users can preview what WOULD be cleaned without touching files. Massive confidence booster for first-time users or new mod setups. No competitor offers this. | MEDIUM | Would need to either: (a) run xEdit with output capture but discard changes, or (b) simulate based on skip lists and validation only. Option (b) is more feasible -- show "these N plugins would be processed" without actually invoking xEdit. True dry-run (running xEdit without saving) would require xEdit support for a no-save flag. **Recommendation**: Start with list preview (skip list applied, validation run, show what would happen). |
| **Plugin backup before cleaning** | Create timestamped copies of .esp/.esm/.esl files before xEdit modifies them. Users can rollback if something goes wrong. | MEDIUM | xEdit's QAC itself is generally safe, but users are rightfully cautious with hundreds of plugins worth potentially hundreds of hours of modding. Backup to `AutoQAC Data/backups/{timestamp}/` with configurable retention. Restore UI to select a backup point and copy files back. Must handle large file counts efficiently. |
| **Config validation UI** | Real-time path validation in Settings: green checkmarks for valid paths, red X for invalid, with specific error descriptions. Live feedback as user types or browses. | LOW | Python ref validates on save. C# could use ReactiveUI `WhenAnyValue` to validate reactively as paths change. Show inline validation state per field. Already has `ValidatePathsAsync` but it's not surfaced to the UI in real-time. |
| **CPU monitoring for xEdit subprocess** | Detect when xEdit is stuck (CPU at 0% for extended period, likely hung/frozen). Auto-suggest termination. Prevent infinite waits on hung processes. | MEDIUM | Python ref has `check_process(pid, threshold=5)` using psutil. C# equivalent: `System.Diagnostics.Process` properties or P/Invoke for CPU time. Can poll during the wait loop. The current `ProcessExecutionService` only uses timeout -- CPU monitoring would detect hangs that happen within the timeout window (e.g., xEdit hung at 20s of a 300s timeout). |
| **Log file monitoring (xEdit log tailing)** | Show the xEdit log file content in real-time, not just stdout. xEdit writes detailed logs to disk that aren't captured by stdout redirection. | HIGH | xEdit may write to its own log file alongside stdout. Need `FileSystemWatcher` + tail logic. Complex due to file locking (xEdit holds the log file open). Potential alternative: Just surface the stdout/stderr already captured, which covers most information. **Recommendation**: Defer unless users specifically request xEdit log monitoring beyond stdout. |
| **Subprocess resource management** | Track and limit concurrent subprocesses system-wide. Even though cleaning is sequential, the semaphore pattern prevents accidental parallel invocations from bugs. | LOW | **Already implemented** via `SemaphoreSlim` in `ProcessExecutionService`. Python ref has equivalent. This is a safety net, not a user feature. Already done. |
| **Backup rollback UI** | Let users select a backup point and restore specific plugins. Visual diff showing what changed. | HIGH | Depends on backup feature existing first. Needs: backup listing, file comparison (size/hash), selective restore, confirmation dialog. This is a v2+ feature. |
| **Enhanced validation messages** | Not just "invalid path" but contextual guidance: "Did you mean SSEEdit64.exe? We found it at C:\path", auto-discovery of common installation locations. | MEDIUM | Could scan Program Files, Steam directories, MO2 mods folder for xEdit executables. Would need to know common install patterns per game. Good UX but not essential for initial milestone. |
| **Comprehensive test coverage** | Unit tests for all services, integration tests for workflows, ViewModel tests for UI logic. Target 80%+ on critical paths. | HIGH | Currently has ~20 test files covering models, services, and viewmodels. Need to audit coverage gaps, add edge case tests, ensure all Python ref behaviors are tested. This is an ongoing effort, not a single feature. |

### Anti-Features (Commonly Requested, Often Problematic)

Features that seem good but create problems. Deliberately NOT building these.

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| **Parallel plugin cleaning** | "Clean 4 plugins at once, go 4x faster!" | xEdit uses file-level locks on master files (Skyrim.esm, etc.). Running two xEdit instances simultaneously will corrupt plugins or crash. The Python ref, Rust port, and C# port ALL enforce sequential processing. This is a fundamental constraint of xEdit. | Sequential cleaning with real-time progress makes the wait tolerable. Optimizing xEdit startup time per plugin (warm caches) would be more impactful. |
| **Auto-updating** | "Check for updates on startup" | Desktop apps with auto-update add significant security surface (MITM, supply chain attacks), complexity (differential updates, rollback), and user annoyance. For a modding tool used by power users, this is overkill. | Display version in About dialog. Users can check GitHub releases manually. Consider a "check for updates" button that opens the releases page. |
| **Built-in xEdit** | "Bundle xEdit so users don't need to install it separately" | xEdit is maintained by a separate team, has its own license, and receives frequent updates. Bundling creates version mismatch issues, increases installer size, and creates redistribution concerns. | Clear setup instructions. Auto-detect xEdit from common locations (Steam, manual installs). Show a "Download xEdit" link if not found. |
| **Plugin content analysis/preview** | "Show me what ITMs/UDRs are in this plugin before cleaning" | This requires parsing Bethesda plugin binary format (complex), duplicating xEdit's analysis engine, and maintaining compatibility across game versions. Enormous scope for marginal benefit since xEdit itself shows this. | Show xEdit's output in real-time during cleaning. Users who want pre-analysis can use xEdit directly. |
| **Cloud sync of settings** | "Sync my config across computers" | YAML config files contain machine-specific absolute paths (xEdit location, load order location). These paths are never portable between machines. | Config export/import for shareable settings (timeouts, skip lists only -- not paths). |
| **Real-time xEdit log file monitoring** | "Tail xEdit's own log file for richer output" | xEdit holds file locks on its log during execution. FileSystemWatcher + file locking = race conditions, I/O contention, and platform-specific headaches. The stdout/stderr capture already provides the essential information. | Pipe stdout/stderr to UI (already partially implemented). After cleaning completes, offer to open the xEdit log file. |
| **Plugin dependency resolution** | "Clean plugins in dependency order" | xEdit's QAC already handles master dependencies internally. Reimplementing dependency resolution is a massive scope expansion for no practical benefit -- the cleaning operation itself doesn't depend on load order. | Trust xEdit to handle its own master resolution. Process in load order sequence (which the load order file already provides). |

## Feature Dependencies

```
[Deferred Config Saves]
    (standalone - no dependencies)

[Real-time Progress Feedback]
    (standalone - pipes existing OutputDataReceived to UI)

[Reliable Stop/Cancel]
    requires: [Graceful Subprocess Termination]
        "Cannot reliably cancel if we can't reliably kill xEdit"

[Environment Validation Messages]
    enhances: [Config Validation UI]
        "Validation logic feeds both startup checks and settings UI"

[Plugin Backup]
    (standalone - runs before cleaning starts)
    enables: [Backup Rollback UI]
        "Rollback requires backups to exist"

[Dry-Run Mode]
    requires: [Plugin Validation Edge Cases]
        "Dry-run preview needs accurate skip list + validation"

[CPU Monitoring]
    enhances: [Reliable Stop/Cancel]
        "CPU monitoring detects hangs; stop/cancel terminates them"

[Comprehensive Test Coverage]
    requires: ALL other features to be implemented first
    "Test what exists, not what's planned"

[About Dialog]
    (standalone - no dependencies)
```

### Dependency Notes

- **Reliable Stop/Cancel requires Graceful Subprocess Termination:** The cancel operation's trustworthiness depends entirely on whether the subprocess actually dies. If `StopCleaning()` signals cancellation but xEdit keeps running, users lose trust.
- **Config Validation UI enhances Environment Validation Messages:** The same validation logic that runs at cleaning start can run reactively in Settings. Build the validation logic once, surface it in two places.
- **Dry-Run Mode requires Plugin Validation Edge Cases:** A dry-run preview that misidentifies which plugins would be cleaned (due to load order parsing bugs) is worse than no dry-run at all. Get validation right first.
- **Backup Rollback UI requires Plugin Backup:** Obviously cannot rollback without backups. But backup can exist without the rollback UI -- users can manually copy files from the backup directory.
- **CPU Monitoring enhances Reliable Stop/Cancel:** CPU monitoring is a detection mechanism; stop/cancel is the response mechanism. Useful together but CPU monitoring is not required for stop/cancel to work.

## MVP Definition (This Milestone)

### Priority 1: Ship These First

These are the features that directly affect user trust and session reliability.

- [ ] **Reliable stop/cancel with guaranteed cleanup** -- Users must trust the Stop button. Fix the `TerminateProcessGracefullyAsync` path to ensure xEdit is dead before continuing. Verify behavior with `CreateNoWindow=true`.
- [ ] **Graceful subprocess termination** -- Escalation pattern: close main window -> wait 2s -> terminate -> wait 1s -> kill(entire tree). Ensure no orphaned xEdit processes.
- [ ] **Real-time progress feedback** -- Pipe `OutputDataReceived` events to the ProgressWindow. Show xEdit output scrolling line by line. Parse stats in real-time (ITMs found, UDRs fixed).
- [ ] **Deferred config saves** -- Port the Python ref's QTimer debounce pattern to C#. Use `System.Reactive` `Throttle` operator on save requests.
- [ ] **Environment validation messages** -- Return `(bool, string)` from validation with actionable messages. Surface them to user in both startup check and settings UI.

### Priority 2: Add After P1 Is Solid

These improve confidence and polish but aren't blocking.

- [ ] **Plugin validation edge cases** -- Audit against the Python ref's `PluginValidator`. Add test cases for all edge cases (separators, BOM, encoding, prefix chars).
- [ ] **Config validation UI** -- Real-time path validation in Settings with visual feedback (checkmark/X icons).
- [ ] **Dry-run mode (list preview)** -- Show "these N plugins would be cleaned" with skip list applied. No xEdit invocation.
- [ ] **About dialog** -- Version, build date, .NET version, links to GitHub.
- [ ] **Logging improvements** -- Add startup info logging, log xEdit commands being run, log session summary.

### Priority 3: Future Consideration (v2+)

Features to defer until the core is rock-solid.

- [ ] **Plugin backup before cleaning** -- Defer because: adds I/O overhead per plugin, needs retention policy, needs disk space monitoring. Build when users ask for it.
- [ ] **CPU monitoring** -- Defer because: timeout already catches most hangs. CPU monitoring is an optimization for edge cases where xEdit freezes within the timeout window.
- [ ] **Backup rollback UI** -- Defer because: requires backup feature first, and is a complex UI with selective restore.
- [ ] **Log file monitoring** -- Defer because: stdout/stderr capture is sufficient. xEdit log file tailing has file locking issues.
- [ ] **Comprehensive test coverage** -- This is ongoing, not a single deliverable. Add tests as features are built. Target 80% on critical paths (cleaning service, orchestrator, process execution, config service).

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| Reliable stop/cancel | HIGH | MEDIUM | P1 |
| Graceful subprocess termination | HIGH | MEDIUM | P1 |
| Real-time progress feedback | HIGH | MEDIUM | P1 |
| Deferred config saves | MEDIUM | LOW | P1 |
| Environment validation messages | MEDIUM | LOW | P1 |
| Plugin validation edge cases | MEDIUM | MEDIUM | P2 |
| Config validation UI | MEDIUM | LOW | P2 |
| Dry-run mode (list preview) | HIGH | LOW | P2 |
| About dialog | LOW | LOW | P2 |
| Logging improvements | LOW | LOW | P2 |
| Plugin backup | HIGH | MEDIUM | P3 |
| CPU monitoring | LOW | MEDIUM | P3 |
| Backup rollback UI | MEDIUM | HIGH | P3 |
| Log file monitoring | LOW | HIGH | P3 |
| Comprehensive test coverage | MEDIUM | HIGH | Ongoing |

## Competitor Feature Analysis

| Feature | Python PACT (Reference) | LOOT | Our Approach |
|---------|------------------------|------|--------------|
| Plugin cleaning | Sequential QAC via subprocess | N/A (identifies dirty plugins, doesn't clean) | Sequential QAC via `Process` class, same approach as Python |
| Progress feedback | Real-time stdout parsing + progress signal with stats dict | N/A | **Gap**: Need to pipe `OutputDataReceived` to UI in real-time. Currently only appends status line after completion. |
| Stop/cancel | `_should_stop` flag + `terminate()` -> `kill()` on subprocess | N/A | **Gap**: CancellationToken exists but subprocess termination path needs hardening |
| Deferred config saves | QTimer(100ms) debounce with mutex-protected queue | N/A | **Gap**: Not implemented. Config saves are synchronous. |
| CPU monitoring | `psutil.Process.cpu_percent(interval=1)` | N/A | **Gap**: Not implemented. Timeout is the only hang detection. |
| Backup | Not implemented | Not implemented | **Opportunity**: Neither competitor offers pre-cleaning backup |
| Dry-run | Not implemented | Identifies dirty plugins but no action preview | **Opportunity**: Show what would be cleaned without running xEdit |
| Skip lists | YAML per-game + universal, with UI editor | Metadata-based (masterlist) | Already implemented with UI editor. Feature parity. |
| MO2 integration | `ModOrganizer.exe run` wrapper | Native plugin | Already implemented. Feature parity. |
| Game detection | Executable name matching + load order heuristics | Game-specific (separate LOOT executables) | Already implemented. Feature parity. |
| Partial forms | `-iknowwhatimdoing -allowmakepartial` flags | N/A | Already implemented with warning dialog. Feature parity. |

## Sources

- Python reference implementation: `J:\AutoQACSharp\Code_To_Port\AutoQACLib\` (direct code analysis, HIGH confidence)
- C# implementation: `J:\AutoQACSharp\AutoQAC\` (direct code analysis, HIGH confidence)
- [XEdit-PACT on GitHub](https://github.com/GuidanceOfGrace/XEdit-PACT) -- original PACT tool
- [xEdit Cleaning Documentation](https://tes5edit.github.io/docs/7-mod-cleaning-and-error-checking.html) -- official xEdit docs
- [LOOT: Dirty Edits, Mod Cleaning & CRCs](https://loot.github.io/docs/help/dirty-edits-mod-cleaning--crcs/) -- LOOT documentation
- [NN/g: Designing for Long Waits and Interruptions](https://www.nngroup.com/articles/designing-for-waits-and-interruptions/) -- UX patterns for long-running tasks
- [.NET Graceful Shutdown patterns](https://nelsonbn.com/blog/dotnet-graceful-shutdown/) -- subprocess cleanup best practices
- [Kill Process in C# - Best Practices 2026](https://copyprogramming.com/howto/c-task-kill-process) -- `Process.Kill(true)` for process tree termination

---
*Feature research for: Desktop batch automation tool (Bethesda plugin cleaning)*
*Researched: 2026-02-06*
