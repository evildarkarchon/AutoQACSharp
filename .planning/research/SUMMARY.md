# Project Research Summary

**Project:** AutoQACSharp - Desktop application for automating Bethesda plugin cleaning
**Domain:** Desktop batch automation tool (C# Avalonia MVVM, xEdit subprocess management)
**Researched:** 2026-02-06
**Confidence:** HIGH

## Executive Summary

AutoQACSharp is a desktop batch automation tool that orchestrates xEdit to clean hundreds of Bethesda game plugins (Skyrim, Fallout 4) sequentially. Experts build these tools with strict MVVM separation, reactive state management, and robust subprocess lifecycle handling. The critical architectural constraint is **sequential-only processing** — xEdit uses file-level locks on master files, making parallel cleaning impossible. The recommended approach leverages Avalonia for cross-platform UI, ReactiveUI for observables, and Polly for subprocess resilience, with deferred configuration saves and real-time progress parsing as the primary UX improvements.

The biggest risks are subprocess lifecycle mishandling (file handle ghosts causing corruption) and configuration durability (deferred saves losing data on crash). These are mitigated by adding post-kill file handle verification with exponential backoff and implementing atomic config writes with critical-change classification. The project has solid foundations already in place — the core cleaning pipeline works. This milestone focuses on hardening process management, enhancing real-time feedback, and adding safety features (backup/dry-run) that require the foundation to be bulletproof first.

Architecture follows strict three-layer MVVM with services as the business logic tier. State flows unidirectionally through ReactiveUI observables from `StateService` to ViewModels to Views. The current tech debt (904-line `MainWindowViewModel`, placeholder `FullPath` values) must be resolved before implementing backup/rollback features. Phase ordering should prioritize process management robustness, then user feedback enhancements, then safety features, deferring diagnostic features (CPU monitoring, log tailing) to later phases.

## Key Findings

### Recommended Stack

The project already has a modern, well-chosen stack (.NET 10, Avalonia 11.3.11, ReactiveUI, YamlDotNet, Mutagen, Serilog). Research identified targeted additions for robustness and testing, not wholesale changes.

**Core technologies to add:**
- **Polly.Core 8.6.5**: Resilience pipelines for xEdit subprocess timeout/retry — eliminates hand-rolled timeout logic and adds jitter, circuit breaking, and telemetry-ready patterns. Use the standalone Core package to avoid ASP.NET HTTP client dependencies.
- **FluentValidation 12.1.1**: Configuration validation with composable rules and structured error messages suitable for UI display. Pairs with DataAnnotations for simple property checks; handles complex cross-field rules (e.g., "if MO2 enabled, MO2 path must exist").
- **Serilog.Enrichers.Thread/Process 4.0.0/3.0.0**: Add thread/process context to all log events for debugging subprocess management. Essential for correlating application logs with xEdit subprocess activity.
- **Verify.Xunit 31.10.0**: Snapshot testing for xEdit output parsing results — reduces assertion code by 80%+ and catches format regressions automatically.
- **Microsoft.Extensions.TimeProvider.Testing 10.2.0**: Fake time provider for testing timeout logic without waiting real seconds. Built by Microsoft, designed for .NET 10.
- **Avalonia.Headless.XUnit 11.3.11**: Headless UI testing. **CRITICAL: Requires xUnit v2 (2.9.3) — incompatible with xUnit v3 until Avalonia ships a v3 adapter. Do not migrate to xUnit v3.**

**What NOT to use:**
- xUnit v3 (blocks headless UI testing)
- PerformanceCounter (deprecated in .NET Core+, use `Process.TotalProcessorTime`)
- Microsoft.Extensions.Resilience (ASP.NET-oriented, pulls unnecessary HTTP infrastructure)
- Hand-rolled retry loops (Polly handles edge cases you'll miss)

### Expected Features

**Must have (table stakes) — users expect these:**
- **Reliable stop/cancel with guaranteed subprocess cleanup** — xEdit runs 30s-5min per plugin; users must trust the Stop button terminates xEdit completely and leaves no file handle ghosts. Current implementation has CancellationToken but termination path needs hardening.
- **Real-time progress feedback** — pipe xEdit stdout to UI line-by-line with parsed stats (ITMs removed, UDRs fixed) updating mid-plugin. Current implementation only shows post-completion summary.
- **Graceful subprocess termination** — escalation pattern (close main window → terminate → kill process tree) with post-kill file handle verification before proceeding to next plugin.
- **Deferred configuration saves** — dirty-flag + debounce pattern to prevent file contention from rapid UI changes. Python ref uses 100ms debounce; C# should use 500ms with System.Reactive.
- **Environment validation messages** — return actionable error descriptions ("xEdit not found at: C:\path") not just booleans. Surface in both startup checks and Settings UI.

**Should have (competitive advantage):**
- **Dry-run mode (list preview)** — show "these N plugins would be cleaned" without invoking xEdit. Massive confidence booster for first-time users. Start with list-only preview (skip validation + filtering), not full xEdit dry-run.
- **Plugin backup before cleaning** — timestamped copies to `AutoQAC Data/backups/{timestamp}/` with configurable retention. Requires `PluginInfo.FullPath` tech debt resolution first.
- **Config validation UI** — real-time path validation in Settings with green checkmarks/red X icons as user types. Use ReactiveUI `WhenAnyValue` for live feedback.
- **CPU monitoring for xEdit subprocess** — detect hangs (CPU < 1% for 30s = likely frozen). Poll `Process.TotalProcessorTime` every 5s. Enhances timeout-based hang detection.

**Defer (v2+):**
- **Backup rollback UI** — depends on backup existing first. Needs backup listing, file comparison, selective restore, confirmation dialogs.
- **Log file monitoring (xEdit log tailing)** — xEdit writes to disk logs beyond stdout. FileSystemWatcher + tail logic. Complex due to file locking. Stdout/stderr already captures most information.
- **Comprehensive test coverage** — ongoing effort, not single deliverable. Add tests as features are built. Target 80% on critical paths (cleaning service, orchestrator, process execution).

**Anti-features (deliberately NOT building):**
- **Parallel plugin cleaning** — xEdit file locks make this impossible. Would corrupt plugins or crash.
- **Auto-updating** — security surface, complexity, user annoyance. Power users can check GitHub releases manually.
- **Built-in xEdit** — separate maintenance, version mismatch issues, redistribution concerns.
- **Real-time xEdit log file monitoring** — file locking race conditions. Stdout/stderr capture is sufficient.

### Architecture Approach

Strict three-layer MVVM with ReactiveUI observables for unidirectional state flow. Services layer owns business logic, ViewModels own presentation logic, Views are pure XAML data binding. The `StateService` is the reactive hub — all state changes flow through `BehaviorSubject<AppState>` to observers. Configuration is cached in-memory and synced to YAML via debounced saves.

**Major components:**
1. **StateService (reactive hub)** — centralized `AppState` holder, emits observables for `StateChanged`, `ProgressChanged`, `PluginProcessed`, `CleaningCompleted`. Thread-safe via `Lock`. **Pitfall risk:** Lock reentrancy can deadlock if `Subject.OnNext()` is called inside lock scope.
2. **CleaningOrchestrator (workflow coordinator)** — validates config → detects game → loads skip list → filters plugins → processes sequentially → creates session results. Entry point for all cleaning operations. Future dry-run mode adds a boolean flag here.
3. **ProcessExecutionService (subprocess lifecycle)** — manages xEdit `Process` objects with semaphore slot limiting, async output capture, timeout with Polly, graceful+forced termination. **Pitfall risk:** File handles not fully released after `Kill()` — needs post-kill verification delay.
4. **ConfigurationService (YAML I/O)** — thread-safe config read/write with `SemaphoreSlim`. Caches main config, emits `UserConfigurationChanged` observable. **Planned change:** Add dirty-flag debounce to defer saves.
5. **XEditOutputParser (stateless regex parser)** — parses xEdit stdout for ITM/UDR/partial form counts. Currently batch-only; needs incremental stateful variant for real-time progress.

**New services needed:**
- `IBackupService` / `BackupService` — copies plugins to timestamped backup directory before cleaning, manages retention, provides rollback.
- `ICpuMonitorService` / `CpuMonitorService` — polls `Process.TotalProcessorTime` to detect hung xEdit processes.
- `ILogFileMonitorService` / `LogFileMonitorService` — (optional/deferred) tails xEdit log files via FileSystemWatcher.

**Key patterns:**
- **Dirty-flag debounce:** Mark config dirty on property change, auto-save after 500ms idle. Flush before cleaning starts and on app close.
- **Incremental stateful parser:** Parse xEdit output line-by-line, maintain running totals, emit `CleaningProgressInfo` records via `IProgress<T>`.
- **Observable service monitor:** CPU monitoring as `IObservable<bool>` (process active/inactive) created via `Observable.Create` with periodic timer.

### Critical Pitfalls

1. **xEdit process ghost holds file handles after Kill()** — `Process.Kill()` is asynchronous at OS level. Process object reports `HasExited` before kernel releases file handles. Next plugin immediately tries to launch xEdit, fails or corrupts data. **Prevention:** After `Kill()`, call `WaitForExit(5000)`, then add post-kill delay (500ms) with file handle polling (attempt to open plugin with `FileShare.None`, retry with exponential backoff up to 5s). Additionally, `CloseMainWindow()` is ineffective with `CreateNoWindow=true` — either skip graceful termination or switch to `WindowStyle=Hidden`.

2. **Backup path resolution fails for MO2 virtual filesystem** — MO2 mode uses virtual overlay; plugin "real" file is in `overwrite/` or mod folder, not game data directory. `PluginInfo.FullPath` is currently a placeholder in some paths. Backup copying wrong file or non-existent path. **Prevention:** Resolve `FullPath` tech debt BEFORE implementing backup. Validate `File.Exists(plugin.FullPath)` before copy. Document that backup is target plugin only, not masters.

3. **Deferred config saves lose data on crash** — dirty-flag + timer pattern trades write performance for durability. User force-closes during cleaning after changing skip list; change is lost. **Prevention:** Classify changes by criticality. Skip list modifications and path changes flush immediately. UI preferences defer. Use atomic writes (`File.Replace()`) for flush. Add pre-cleaning flush in `StartCleaningAsync()`. Detect unclean shutdown on next launch and warn user.

4. **StateService lock reentrancy deadlock** — `UpdateState()` acquires `_lock`, calls `Subject.OnNext()`, which fires subscribers synchronously. If subscriber reads `CurrentState` (also acquires `_lock`), deadlock occurs. .NET 9 `Lock` is non-reentrant. **Prevention:** Never call `Subject.OnNext()` inside lock. Compute new state inside lock, exit lock, then fire `OnNext()`. Consider `ReaderWriterLockSlim` for read-heavy pattern.

5. **CancellationTokenSource disposed during callback** — `StopCleaning()` calls `Cancel()` while `finally` block disposes CTS. Cancellation callbacks run asynchronously on threadpool; may access disposed state. **Prevention:** Do not dispose CTS in finally block (let GC handle it), or add delay after `Cancel()` before `Dispose()`, or use `Interlocked.Exchange` instead of lock for null-check.

6. **Dry-run gives false confidence** — validating config without running xEdit cannot verify: xEdit version compatibility, timeout/hang issues, MO2 virtual filesystem resolution. Users see "success" in dry-run then have real cleaning fail. **Prevention:** Use distinct `CleaningStatus.DryRun` instead of reusing `Skipped`. Show validation report format (pass/fail per check), not cleaning results window. Document what dry-run does NOT verify.

## Implications for Roadmap

Based on research, suggested phase structure prioritizes process robustness, then user feedback, then safety features:

### Phase 1: Foundation Hardening
**Rationale:** Core subprocess lifecycle has race conditions and state management has deadlock risk. These block all subsequent features that depend on reliable process termination (backup, CPU monitoring) or concurrent state updates (real-time progress).

**Delivers:**
- Enhanced process termination with post-kill file handle verification
- StateService lock reentrancy fix (OnNext outside lock scope)
- CancellationTokenSource disposal race fix
- Deferred configuration saves with dirty-flag debounce

**Addresses:**
- Reliable stop/cancel (table stakes from FEATURES.md)
- Graceful subprocess termination (table stakes)
- Deferred config saves (table stakes)

**Avoids:**
- Pitfall 1 (process ghost file handles)
- Pitfall 4 (StateService deadlock)
- Pitfall 5 (CTS disposal race)
- Pitfall 3 (config durability, via atomic writes + flush)

**Stack additions:** None for this phase — uses existing infrastructure.

**Research flag:** Standard patterns — no additional research needed. Process termination and reactive state management are well-documented.

### Phase 2: Real-Time Feedback
**Rationale:** With process lifecycle robust, add real-time progress parsing. This enhances UX significantly (users stare at progress for 30min-4hr sessions) and establishes the structured progress pipeline that log monitoring would later reuse.

**Delivers:**
- Incremental xEdit output parser with running stats
- `CleaningProgressInfo` model with action + stats
- Structured progress observable in StateService
- ProgressViewModel displays live ITM/UDR counts mid-plugin

**Addresses:**
- Real-time progress feedback (table stakes)
- Environment validation messages (table stakes, same validation logic surfaces in Settings UI)

**Uses:**
- System.Reactive (already via ReactiveUI) for progress debouncing if needed

**Implements:**
- Incremental stateful parser pattern (ARCHITECTURE.md)

**Avoids:**
- Anti-pattern of passing stateful parsers across threads without synchronization (parser only accessed from output callback)

**Research flag:** Standard patterns — output parsing and IProgress<T> are straightforward.

### Phase 3: Dry-Run Safety Feature
**Rationale:** Lightweight safety feature that reuses the entire validation/filtering pipeline. No new services. Builds user confidence before implementing the heavier backup feature.

**Delivers:**
- Dry-run mode with `CleaningStatus.DryRun`
- Validation-only execution path (skip xEdit subprocess)
- Distinct validation report UI (not reusing CleaningResultsWindow)

**Addresses:**
- Dry-run mode (competitive advantage from FEATURES.md)

**Avoids:**
- Pitfall 6 (false confidence, via clear labeling and documenting limitations)

**Stack additions:** None — uses existing orchestrator + validation services.

**Research flag:** Standard patterns — subset of existing cleaning pipeline.

### Phase 4: Tech Debt Resolution (FullPath Placeholder)
**Rationale:** Blocking prerequisite for backup feature. `PluginInfo.FullPath` is currently a placeholder in file-based load order paths. Backup cannot work without knowing real file locations, especially for MO2 virtual filesystem.

**Delivers:**
- `PluginLoadingService` resolves full paths using game data folder
- `PluginValidationService` resolves paths consistently
- All `PluginInfo` instances have rooted `FullPath` with `File.Exists() == true`

**Addresses:**
- Tech debt from CONCERNS.md and ARCHITECTURE.md prerequisite section

**Avoids:**
- Pitfall 2 (backup path resolution, especially MO2 mismatch)
- Anti-pattern 4 (backup without FullPath resolution)

**Research flag:** Standard patterns — path resolution is straightforward file I/O.

### Phase 5: Backup and Rollback
**Rationale:** Now that FullPath is resolved and process termination is robust (file handles released), implement backup. This is the most new infrastructure but provides significant user trust for a destructive batch operation.

**Delivers:**
- `IBackupService` / `BackupService` with timestamped backup directory
- Pre-cleaning plugin copy with configurable retention
- `BackupEntry` metadata (path, hash, timestamp, game type)
- CleaningResultsViewModel rollback command per plugin
- Settings for backup enable/disable and retention days

**Addresses:**
- Plugin backup (competitive advantage)
- Backup rollback UI (deferred to v2+ in FEATURES.md but delivering here if time allows)

**Uses:**
- System.IO file copy (no new library)

**Implements:**
- Backup service pattern (ARCHITECTURE.md)

**Avoids:**
- Pitfall 2 (MO2 path mismatch, via Phase 4 FullPath resolution)
- Anti-pattern of backup without validation (`File.Exists` check before copy)

**Depends on:** Phase 4 (FullPath resolution) and Phase 1 (process handle cleanup)

**Research flag:** Standard patterns — file operations and manifest management are well-documented.

### Phase 6: Advanced Monitoring (Deferred)
**Rationale:** CPU monitoring and log file tailing are diagnostic enhancements, not user-facing features. Defer to later milestone unless users specifically request.

**Delivers:**
- `ICpuMonitorService` / `CpuMonitorService` polling `Process.TotalProcessorTime`
- Hang detection (CPU < 1% for 30s configurable threshold)
- Optional: `ILogFileMonitorService` for xEdit log tailing

**Addresses:**
- CPU monitoring (should-have from FEATURES.md)
- Log file monitoring (deferred from FEATURES.md)

**Uses:**
- Process.TotalProcessorTime (BCL)
- FileSystemWatcher (BCL)

**Implements:**
- Observable service monitor pattern (ARCHITECTURE.md)

**Avoids:**
- Anti-pattern 3 (FileSystemWatcher without debounce — use Observable.Throttle)
- Performance trap of PerformanceCounter (deprecated)

**Depends on:** Phase 1 (robust process handles for accurate monitoring)

**Research flag:** Medium complexity — CPU measurement accuracy varies; FileSystemWatcher reliability on Windows needs validation testing.

### Phase Ordering Rationale

- **Phase 1 first:** Process management and state deadlocks are foundational bugs that risk data corruption. Every subsequent feature depends on reliable subprocess termination and thread-safe state updates.
- **Phase 2 before Phase 5:** Backup feature needs the structured progress pipeline to show backup operation progress. Real-time feedback establishes this pipeline.
- **Phase 4 blocks Phase 5:** Backup cannot work without FullPath resolution. Tech debt must be resolved first.
- **Phase 3 lightweight, can move:** Dry-run is independent and small. Could go earlier, but Phase 2 (real-time feedback) has higher user value.
- **Phase 6 deferred:** CPU monitoring and log tailing are diagnostic, not user-facing. Low ROI compared to backup and real-time progress.

**Critical path:** Phase 1 → Phase 2 → Phase 4 → Phase 5. Phases 3 and 6 can be moved or skipped.

### Research Flags

**Phases needing deeper research during planning:**
- **Phase 6 (CPU monitoring):** CPU measurement via `Process.TotalProcessorTime` accuracy varies by platform and scheduling. Needs validation testing on Windows 10/11 with xEdit process patterns.
- **Phase 6 (Log file monitoring):** FileSystemWatcher reliability with file locks (xEdit holds logs open) needs investigation. May require polling fallback pattern.

**Phases with standard patterns (skip research-phase):**
- **Phase 1:** Process termination, reactive state, and debounced saves are well-documented .NET patterns.
- **Phase 2:** Output parsing and IProgress<T> are straightforward. Parser regex patterns already exist in Python ref.
- **Phase 3:** Dry-run is a subset of existing pipeline with a boolean flag. No new patterns.
- **Phase 4:** Path resolution is standard file I/O.
- **Phase 5:** Backup is file copy + manifest management. Rollback is file copy in reverse. No complex patterns.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All versions verified via NuGet search, Context7, and official docs. Version compatibility matrix confirms no conflicts. xUnit v2 constraint documented with clear rationale. |
| Features | HIGH | Based on direct code analysis (C# implementation) and reference implementation (Python). Feature dependencies mapped. MVP prioritization clear. |
| Architecture | HIGH | Direct analysis of all source files. Existing component responsibilities documented. Integration points for new services identified. Build order is dependency-driven. |
| Pitfalls | HIGH | Based on codebase analysis + .NET runtime issue tracker (dotnet/runtime #16848, #51277, #18034). Each pitfall has concrete prevention steps and test verification. |

**Overall confidence:** HIGH

The research is grounded in primary sources: the existing codebase (complete visibility), the Python reference implementation (authoritative for feature parity), official .NET runtime documentation, and verified NuGet package versions. There are no areas of uncertainty that would block roadmap creation. All architectural decisions are informed by existing patterns in the codebase.

### Gaps to Address

- **CPU monitoring accuracy:** `Process.TotalProcessorTime` measurement varies by OS scheduler. Needs empirical testing with xEdit process patterns on target platforms (Windows 10/11). Recommend starting with generous threshold (CPU < 1% for 30s) and making configurable. **Phase 6 validation step.**

- **FileSystemWatcher reliability:** Known issues with buffer overflows under heavy load. For single-file monitoring (xEdit log), this should be fine, but needs validation testing. Recommend hybrid pattern (FSW + 2s polling fallback). **Phase 6 validation step.**

- **Backup rollback UX:** Research documents the feature but not the detailed UX. During Phase 5 planning, design decisions needed: show diff view? selective restore (single plugin vs session)? confirmation dialogs with warnings? **Phase 5 planning step.**

- **MainWindowViewModel split strategy:** 904 lines is tech debt. Before adding dry-run or backup UI, this needs splitting. Recommend: extract `SkipListManagementViewModel`, `PathConfigurationViewModel`, `CleaningControlViewModel`. Ensure Interactions (progress window, results window) route correctly. **Tech debt reduction, before Phase 3 or Phase 5.**

## Sources

### Primary (HIGH confidence)
- Direct codebase analysis: `J:\AutoQACSharp\AutoQAC\` (all source files inspected)
- Python reference implementation: `J:\AutoQACSharp\Code_To_Port\AutoQACLib\` (authoritative for feature parity)
- [NuGet Gallery](https://www.nuget.org/) — all package versions verified 2026-02-06
- [dotnet/runtime issue tracker](https://github.com/dotnet/runtime) — issues #16848 (process wait-kill race), #51277 (WaitForExit deadlock), #18034 (File.Replace atomic)
- [Microsoft Learn - .NET Documentation](https://learn.microsoft.com/en-us/dotnet/) — Process API, TimeProvider, EventCounters
- [Avalonia Documentation](https://docs.avaloniaui.net/) — Headless testing setup
- [ReactiveUI Documentation](https://www.reactiveui.net/docs/) — Testing handbook, commands handbook

### Secondary (MEDIUM confidence)
- [xEdit Cleaning Documentation](https://tes5edit.github.io/docs/7-mod-cleaning-and-error-checking.html) — official xEdit docs
- [LOOT Documentation](https://loot.github.io/docs/help/dirty-edits-mod-cleaning--crcs/) — competitor analysis
- [XEdit-PACT GitHub](https://github.com/GuidanceOfGrace/XEdit-PACT) — upstream reference tool
- [Serilog Wiki](https://github.com/serilog/serilog/wiki) — enrichment, configuration
- [Nielsen Norman Group - Designing for Long Waits](https://www.nngroup.com/articles/designing-for-waits-and-interruptions/) — UX patterns

### Tertiary (LOW confidence)
- Community articles on .NET graceful shutdown patterns — validated against primary sources before inclusion

---
*Research completed: 2026-02-06*
*Ready for roadmap: yes*
