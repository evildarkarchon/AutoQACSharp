# Project Research Summary

**Project:** AutoQAC -- xEdit Log File Parsing Fix
**Domain:** Desktop app bugfix -- migrating from dead stdout capture to game-aware log file parsing
**Researched:** 2026-03-30
**Confidence:** HIGH

## Executive Summary

AutoQAC has a fundamental data pipeline bug: it captures xEdit's stdout (which is always empty -- xEdit never writes to stdout) and tries to parse cleaning results from nothing. A secondary fallback reads the log file but uses the wrong filename for universal `xEdit.exe` setups and reads the entire file including historical content from prior sessions. The fix is surgical: four existing services need modification, zero new services are required, and the existing `XEditOutputParser` regex engine is correct as-is.

The most critical discovery from xEdit source code verification is that log file naming follows xEdit's internal game-mode resolution (`wbAppName + "Edit_log.txt"`), NOT the executable filename. When users run `xEdit.exe -SSE`, the log is `SSEEdit_log.txt`, but AutoQAC looks for `XEDIT_log.txt` -- which does not exist. This single naming bug means all universal-xEdit users get zero cleaning statistics. The fix requires a `GameType`-to-prefix lookup table derived from xEdit's `xeInit.pas` source. A second structural change -- offset-based file reading to isolate current-run content from appended history -- eliminates the inflated statistics problem caused by parsing all historical log entries.

Key risks are low because the changes are well-scoped: file I/O with `FileStream`/`FileShare.ReadWrite` (standard .NET pattern), a static lookup table (verified against source), and orchestrator rewiring that follows existing service boundaries. The only medium-confidence area is exception log format details, since the `nxExceptionHook` library is third-party and not in the xEdit repository.

## Key Findings

### Recommended Stack

No new dependencies are needed. All changes use `System.IO` and `System.Text` from .NET 10.

**Core technologies:**
- **FileStream + StreamReader**: Read log files from byte offset with `FileShare.ReadWrite` for lock-safe access
- **FileInfo.Length**: Capture pre-launch file size as the read-start offset (lightweight stat call, no file handle)
- **Encoding.UTF8 (pragmatic)**: xEdit writes ANSI/CP1252, but all regex keywords are ASCII -- UTF-8 decodes identically for the matching portion

**Rejected alternatives:**
- `RandomAccess` -- overkill for small text files, requires manual buffer/encoding management
- `File.ReadAllLinesAsync` -- no offset support, uses `FileShare.Read` which causes IOExceptions when file is still locked

### Expected Features

**Must have (table stakes):**
- Game-aware log file naming using `GameType`-to-prefix mapping (fixes the critical naming bug)
- Offset-based reading to isolate current run's log content from historical appended data
- Parse ITM removal lines (`Removing:`), UDR lines (`Undeleting:`/`Skipping:`), and completion summaries
- Exception log detection and error surfacing when xEdit crashes
- Handle force-killed xEdit (no log written -- skip parsing, report failure)
- Support universal `xEdit.exe` with game flags (`-SSE`, `-FO4`, etc.)

**Should have (differentiators):**
- Parse per-record detail (FormIDs, record paths) from cleaning lines
- Parse deleted navmesh warnings for actionable user feedback
- Parse "nothing to clean" state (completion lines with zero counts = already-clean plugin)
- Parse partial form creation lines (`Making Partial Form:` in xEdit 4.1.6+)
- Surface exception log content in cleaning results (not just "failed")

**Defer (v2+):**
- `-R:<path>` custom log path support (edge case, document as known limitation)
- Per-record detail extraction beyond counting
- "Can't remove" warning surfacing (rare occurrence)
- Elapsed time parsing from summary lines

### Architecture Approach

This is a four-service modification with no new abstractions. `XEditLogFileService` gets game-aware naming and offset-based reading. `ProcessExecutionService` stops redirecting stdout/stderr. `CleaningService` drops its parser dependency. `CleaningOrchestrator` becomes the single owner of log-based parsing, replacing the current dual-path (stdout primary + log fallback) with a single path (log file only).

**Components and responsibilities after fix:**

1. **XEditLogFileService** -- Game-aware log path computation, offset capture, new-content-only reading, exception log detection
2. **ProcessExecutionService** -- Process lifecycle only (launch, wait, kill); no stdout/stderr capture
3. **CleaningService** -- Command building and execution; returns exit status without statistics
4. **XEditOutputParser** -- Unchanged; parses line arrays into `CleaningStatistics` via regex
5. **CleaningOrchestrator** -- Single owner of the parse pipeline: compute path, record offset, launch, read log, parse, combine results

**Data flow:**
```
Orchestrator -> LogFileService.GetLogFilePath(xEditPath, gameType)
Orchestrator -> LogFileService.GetCurrentOffset(logPath)
Orchestrator -> CleaningService.CleanPluginAsync(plugin, ...)
Orchestrator -> LogFileService.ReadNewLogContentAsync(logPath, offset)
Orchestrator -> LogFileService.ReadExceptionLogAsync(xEditPath, startTime)
Orchestrator -> OutputParser.ParseOutput(logLines)
Orchestrator -> StateService.AddDetailedCleaningResult(combined)
```

### Critical Pitfalls

1. **Log file naming mismatch** -- `GetLogFilePath` uses executable stem (`XEDIT_log.txt`) instead of game prefix (`SSEEdit_log.txt`). Fix: GameType-to-prefix lookup table from xEdit source. This is the root cause of zero statistics for universal xEdit users.

2. **Reading entire log file inflates statistics** -- xEdit appends across sessions. Reading everything counts historical ITMs. Fix: Record byte offset before launch via `FileInfo.Length`, seek to that offset after exit. Handle 3MB truncation edge case (offset > file size means read from 0).

3. **File lock contention after process exit** -- `File.ReadAllLinesAsync` uses `FileShare.Read`, which throws if Windows Defender or the indexer holds a write handle. Fix: `FileStream` with `FileShare.ReadWrite` plus exponential backoff retry (250ms, 500ms, 750ms).

4. **Force-killed xEdit writes no log** -- `Process.Kill()` skips `FormClose`, so `SaveLogs` never runs. Fix: Check termination status before attempting log read; report as `CleaningStatus.Failed` if force-killed.

5. **Two naming conventions for two log types** -- Main log uses game prefix (`SSEEdit_log.txt`). Exception log uses executable stem (`SSEEDITException.log`). These are different systems within xEdit. Do not conflate them.

## Implications for Roadmap

Based on the combined research, this is a 4-phase fix with clear dependency ordering. Each phase is independently testable.

### Phase 1: Foundation -- Game-Aware Log File Service

**Rationale:** All downstream phases depend on correct log file naming and offset-based reading. This phase is purely additive -- new methods on an existing service -- so it cannot break current behavior.

**Delivers:**
- `GetLogFilePath(string xEditPath, GameType gameType)` with full game-to-prefix mapping
- `GetExceptionLogPath(string xEditPath)` using executable stem convention
- `GetCurrentOffset(string logFilePath)` returning file size or 0
- `ReadNewLogContentAsync(string logFilePath, long previousOffset, CancellationToken)` with `FileShare.ReadWrite`, truncation handling, and retry logic
- `ReadExceptionLogAsync(string xEditPath, DateTime processStartTime, CancellationToken)` with staleness check
- Full unit test coverage for all 8 game types, edge cases (missing file, truncated file, locked file, stale exception log)

**Features addressed:** Game-aware naming, offset isolation, exception detection, lock-safe reads
**Pitfalls avoided:** #1 (naming mismatch), #2 (full-file read), #4 (FileShare), #6 (encoding -- documented), #7 (stale exception), #8 (truncation)

### Phase 2: Process Layer -- Stop Stdout Capture

**Rationale:** Independent from Phase 1; can be done in parallel or sequentially. Isolated to `ProcessExecutionService` only. Removes the useless stdout/stderr redirect that creates the illusion of output.

**Delivers:**
- `RedirectStandardOutput = false`, `RedirectStandardError = false`
- Removal of `OutputDataReceived`/`ErrorDataReceived` handlers
- Removal of `BeginOutputReadLine()`/`BeginErrorReadLine()` calls
- `UseShellExecute` stays `false` (preserves MO2 wrapping, PID tracking, `WaitForExitAsync`)
- Updated process service tests

**Features addressed:** Eliminate dead stdout capture
**Pitfalls avoided:** #10 (UseShellExecute change would break MO2)

### Phase 3: Integration -- Log-First Parsing in Orchestrator

**Rationale:** Depends on Phases 1 and 2. This is where the behavioral change lands -- the orchestrator switches from "parse stdout, enrich from log" to "parse log file exclusively."

**Delivers:**
- Orchestrator computes game-aware log path using detected `GameType`
- Records offset before each plugin launch
- Reads new log content after process exit
- Checks exception log for crash information
- Skips log parsing on force-kill (reports failure directly)
- Removes `IXEditOutputParser` dependency from `CleaningService`
- Adds verification test for "File has not changed, removing:" case-sensitivity (non-issue but worth proving)
- Adds multi-pass QAC output test

**Features addressed:** Correct end-to-end parsing, exception surfacing, force-kill handling
**Pitfalls avoided:** #3 (false positive verification), #5 (force-kill), #11 (multi-pass)

### Phase 4: Cleanup -- Remove Dead Code

**Rationale:** Can only happen after all functional changes are verified working. Removes confusing dead paths that would mislead future maintainers.

**Delivers:**
- Remove old `ReadLogFileAsync` and single-arg `GetLogFilePath(string)` overload
- Remove `OutputLines`/`ErrorLines` from `ProcessResult` (or make always-empty)
- Remove timestamp-based staleness detection
- Remove `IsCompletionLine` from parser if unused
- Update all remaining test mocks to reflect new behavior
- Final test pass

**Features addressed:** Code hygiene
**Pitfalls avoided:** #14 (timestamp precision), #15 (stale test mocks)

### Phase Ordering Rationale

- **Phase 1 first** because it is purely additive. New methods on an existing service, fully unit-testable in isolation. No behavioral change to existing code paths.
- **Phase 2 is independent** and can run alongside Phase 1. It only touches `ProcessExecutionService`. Ordering it second is logical but not strictly required.
- **Phase 3 must follow 1 and 2** because it integrates the new log service methods and requires stdout to already be removed (otherwise the dual parsing path creates confusion about which statistics are authoritative).
- **Phase 4 must be last** because removing old APIs before the new path is wired up would break the application.

### Research Flags

**Phases needing verification during planning:**
- **Phase 2:** Verify that `CreateNoWindow` behavior with no stdout redirect works correctly in MO2 mode. Manual test recommended.
- **Phase 3:** Decide whether multi-pass QAC output should sum across all passes (correct total) or report only first pass (user expectation). Research says summing is correct because subsequent passes produce zero-count lines only.

**Phases with standard patterns (skip deeper research):**
- **Phase 1:** All file I/O patterns are standard .NET. GameType mapping is fully documented from xEdit source.
- **Phase 4:** Pure deletion of dead code. No research needed.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | No new dependencies; all APIs verified against .NET 10 docs |
| Features | HIGH | Verified against xEdit Delphi source (`xeMainForm.pas`, `xeInit.pas`); real log examples cross-referenced |
| Architecture | HIGH | Based on direct analysis of all 5 affected AutoQAC services plus xEdit source |
| Pitfalls | HIGH | 14 of 16 pitfalls verified from source; exception log format is MEDIUM (third-party `nxExceptionHook`) |

**Overall confidence:** HIGH

### Gaps to Address

- **Exception log format:** The `nxExceptionHook` unit is third-party and not included in the xEdit repository. Known patterns (`which can not be found`, `which it does not have`) come from PACT, not direct source verification. Handle by implementing a conservative staleness check and treating exception content as opaque text.
- **MO2 + no stdout redirect:** When MO2 wraps xEdit and stdout is not redirected, verify the process completes normally. MO2 does not depend on xEdit's stdout, so this should work, but needs a manual smoke test during Phase 2.
- **Multi-pass aggregation policy:** Should AutoQAC sum cleaning counts across all QAC passes or report only the first? Summing is technically correct (total work done). Recommend summing with a code comment explaining why subsequent passes report zeros.

## Sources

### Primary (HIGH confidence)
- xEdit source code: `xeMainForm.pas` lines 6138-6168 (SaveLog/SaveLogs), 11120-11600 (UDR/ITM cleaning output), 20260-20340 (QAC execution flow)
- xEdit source code: `xeInit.pas` lines 696-832 (wbAppName per game mode)
- [TES5Edit/TES5Edit GitHub Repository](https://github.com/TES5Edit/TES5Edit)
- [FileStream Class - Microsoft .NET 10](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream?view=net-10.0)
- [FileShare Enum - Microsoft .NET 10](https://learn.microsoft.com/en-us/dotnet/api/system.io.fileshare?view=net-10.0)
- Direct codebase analysis of AutoQAC services (CleaningOrchestrator, ProcessExecutionService, XEditLogFileService, CleaningService, XEditOutputParser)

### Secondary (MEDIUM confidence)
- [XEdit-PACT Source](https://github.com/GuidanceOfGrace/XEdit-PACT) -- log naming conventions, exception log detection patterns
- [xEdit What's New](https://github.com/TES5Edit/TES5Edit.github.io/blob/master/whatsnew.md) -- "Log file is overwritten at 3MB" (v3.0.30), log filename conventions (v3.0.23)
- [GitHub Issue SkyrimLL/SDPlus#953](https://github.com/SkyrimLL/SDPlus/issues/953) -- real xEdit log output examples

### Tertiary (LOW confidence)
- Exception log format details from PACT (nxExceptionHook is third-party, not in xEdit repo) -- needs validation during implementation

---
*Research completed: 2026-03-30*
*Ready for roadmap: yes*
