# Domain Pitfalls: xEdit Log File Parsing on Windows

**Domain:** Windows desktop app reading log files written by an external Delphi-based process (xEdit)
**Researched:** 2026-03-30 (updated with xEdit source verification)

## Critical Pitfalls

Mistakes that cause silent data loss, incorrect parsing results, or total failure.

### Pitfall 1: Log File Naming Mismatch with Universal xEdit.exe

**What goes wrong:** The current `XEditLogFileService.GetLogFilePath()` constructs the log filename from the executable stem: `Path.GetFileNameWithoutExtension(xEditExecutablePath).ToUpperInvariant() + "_log.txt"`. For game-specific executables like `SSEEdit.exe`, this produces `SSEEDIT_log.txt`, which is close enough (xEdit writes `SSEEdit_log.txt` -- case difference is benign on NTFS). But for the universal `xEdit.exe` with a `-SSE` flag, this produces `XEDIT_log.txt`, which **does not exist**. xEdit writes to `SSEEdit_log.txt` based on the resolved game mode.

**Why it happens:** xEdit internally constructs the log filename as `wbAppName + wbToolName + '_log.txt'` (verified: `xeMainForm.pas` line 6166). `wbAppName` is set from the detected game mode (e.g., `SSE`, `FO4`, `TES5`), NOT from the executable filename. When using `xEdit.exe -SSE`, `wbAppName` becomes `SSE` and `wbToolName` becomes `Edit`, producing `SSEEdit_log.txt`.

**Consequences:** Log file is never found for universal `xEdit.exe` users. All cleaning results show zero statistics. Users who use the common modding setup of a single `xEdit.exe` with game flags get no feedback at all.

**Detection:** PACT (the most popular xEdit automation tool) has the same convention in its code: `f"{path.stem.upper()}_log.txt"`. But PACT works around this by calling `clear_xedit_logs()` which deletes old logs before each run, meaning there is only ever one log to find. AutoQAC does not delete logs and therefore must find the correct one.

**Prevention:**
- `GetLogFilePath` must accept a `GameType` parameter and use a lookup table:
  ```
  GameType.SkyrimSe -> "SSEEdit_log.txt"
  GameType.Fallout4 -> "FO4Edit_log.txt"
  GameType.SkyrimLe -> "TES5Edit_log.txt"
  etc.
  ```
- The mapping is: `GameTypeToAppName[gameType] + "Edit_log.txt"`
- Full mapping (from `xeInit.pas` lines 710-832):
  | GameType | wbAppName | Log Filename |
  |----------|-----------|-------------|
  | Oblivion | TES4 | TES4Edit_log.txt |
  | SkyrimLe | TES5 | TES5Edit_log.txt |
  | SkyrimSe | SSE | SSEEdit_log.txt |
  | SkyrimVr | TES5VR | TES5VREdit_log.txt |
  | Fallout3 | FO3 | FO3Edit_log.txt |
  | FalloutNewVegas | FNV | FNVEdit_log.txt |
  | Fallout4 | FO4 | FO4Edit_log.txt |
  | Fallout4Vr | FO4VR | FO4VREdit_log.txt |
- For the exception log, the naming IS based on executable stem (PACT convention, `nxExceptionHook` behavior): `<STEM_UPPER>Exception.log`

**Phase:** Phase 1 -- this is the first thing to fix.

---

### Pitfall 2: Reading the Entire Log File Instead of New Content

**What goes wrong:** xEdit appends to its log file across runs. If the app reads the entire file (as the current `ReadLogFileAsync` does with `File.ReadAllLinesAsync`), it parses all historical content from every previous cleaning session, inflating statistics. A plugin with "3 ITMs removed" this run appears as "47 ITMs removed" because 44 came from prior sessions.

**Why it happens:** The current timestamp-based staleness detection (comparing `File.GetLastWriteTimeUtc` to `processStartTime`) only verifies the file was touched during this run. It does not isolate which lines are new. The file always passes the staleness check because xEdit just wrote to it, so all historical lines get parsed.

**Consequences:** Wildly inflated cleaning statistics. Each successive run reports cumulatively larger numbers.

**Prevention:**
- Record the log file size (byte offset) before launching xEdit: `new FileInfo(logPath).Length` (or 0 if file does not exist).
- After process exit, open the file and `Seek(previousOffset, SeekOrigin.Begin)` to read only newly-appended content.
- Handle the edge case where xEdit truncated the file (offset > file.Length): read from 0.

**Phase:** Phase 1 (XEditLogFileService) -- `GetLogFileOffset` and `ReadNewLogContentAsync` methods.

---

### Pitfall 3: "File has not changed, removing:" False Positive

**What goes wrong:** During QAC, if a plugin was not modified in a cleaning pass, xEdit outputs: `File has not changed, removing: MyMod.esp.save.2026_03_30_12_00_00`. The existing `Removing:\s*(.*)` regex matches this line and counts it as an ITM removal.

**Why it happens:** The regex is not anchored to require `Removing:` at the start of the line, and even if it were, the "removing:" in this message follows the exact `Removing:` pattern. The word is being used in a file-management context (removing a temp save file), not a record-cleaning context.

**Consequences:** Inflated ITM count by 1 per QAC pass where the plugin was unchanged. With 3 QAC passes, a clean plugin could show "2 ITMs removed" (from passes 2 and 3 where nothing changed) when the correct answer is 0.

**Prevention:** Two options:
1. **Filter approach:** Before parsing, filter out lines that start with `File has not changed, removing:` or `Saving:`.
2. **Regex refinement:** Tighten the regex to require the `Removing:` token at the start of the line: `^\s*Removing:\s*(.*)`. This won't help because `File has not changed, removing:` has `removing:` in lowercase. Actually -- check: in the xEdit source, the save message uses lowercase "removing:" while the ITM message uses `Operation+'ing:'` which produces `Removing:` (capital R). So the regex IS case-sensitive and the `R` vs `r` distinguishes them.
3. **Verification:** Check the existing regex: `[GeneratedRegex(@"Removing:\s*(.*)")]`. This is case-sensitive by default. Since xEdit outputs `Removing:` (capital R) for ITMs and `removing:` (lowercase r) for save file cleanup, the current regex actually does NOT match the false positive. **This pitfall may be a non-issue due to case sensitivity.**

**Confidence:** HIGH that this is a non-issue. xEdit source confirms: ITM removal uses `Operation+'ing: '` where Operation is `'Remov'`, producing `Removing:`. File save uses `'removing: '` (lowercase). The generated regex is case-sensitive.

**Phase:** Phase 3 -- verify during integration testing. Add a test case with the `"File has not changed, removing:"` line to confirm it does not match.

---

### Pitfall 4: File.ReadAllLinesAsync Uses FileShare.Read -- Sharing Violation

**What goes wrong:** `File.ReadAllLinesAsync()` (the current implementation) opens the file with `FileShare.Read` internally. This throws `IOException` if xEdit or any other process still holds a write handle on the log file.

**Why it happens:** Three concurrent lock sources: (1) xEdit handle release delay after process exit, (2) Windows Defender scanning newly-written files (100-500ms), (3) Windows Search Indexer.

**Consequences:** The log file read fails, returning zero statistics for a successfully cleaned plugin.

**Prevention:**
- Use `FileStream` with explicit `FileShare.ReadWrite`:
  ```csharp
  using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
  using var reader = new StreamReader(fs);
  ```
- Implement exponential backoff retry: 200ms, 400ms, 800ms.

**Phase:** Phase 1 -- use `FileStream` in the new `ReadNewLogContentAsync` method.

---

### Pitfall 5: Force-Killed xEdit Does Not Write Log File

**What goes wrong:** If the user cancels or hang detection triggers force termination, xEdit is killed via `Process.Kill(entireProcessTree: true)`. A killed process does not execute `FormClose`, so `SaveLogs` never runs. The log file contains no new content.

**Why it happens:** xEdit writes its log file during `FormClose` (verified: `xeMainForm.pas` line 6248). `Process.Kill()` calls `TerminateProcess()`, which destroys the process without running any Delphi cleanup.

**Consequences:** After a forced kill, offset-based reading finds no new content. If interpreted as "nothing to clean," the user gets a misleading result.

**Prevention:**
- Check `CleaningResult` for cancellation or force-kill status BEFORE attempting to read the log file.
- If force-killed, skip log parsing and report as `CleaningStatus.Failed` with message "Process was terminated."
- If gracefully stopped (`CloseMainWindow`), the log might have been written. Attempt to read.

**Phase:** Phase 3 (Orchestrator integration).

---

### Pitfall 6: Log File Encoding is ANSI, Not UTF-8

**What goes wrong:** xEdit writes log content as `AnsiString` (verified: `xeMainForm.pas` line 6154), which is Windows code page 1252. `StreamReader` defaults to UTF-8 when no BOM is present. Plugin names with accented characters will be decoded incorrectly.

**Why it happens:** xEdit is a Delphi application. The `SaveLog` procedure explicitly uses `AnsiString(mmoMessages.Lines.Text)`.

**Prevention:** The regex patterns match only ASCII keywords (`Removing:`, `Undeleting:`, etc.). The keyword matching portion works correctly with UTF-8 decoding. Only the captured group text (record descriptions) after the colon may be garbled, but these are only counted, not displayed.

**Pragmatic approach:** Use UTF-8 (default) and add a code comment documenting the assumption. If descriptions are displayed later, switch to `Encoding.GetEncoding(1252)`.

**Phase:** Phase 1 -- document in code comments.

## Moderate Pitfalls

### Pitfall 7: Exception Log Persists Across Runs

**What goes wrong:** xEdit's exception log from a previous crash persists. After a successful run, the app reads the stale exception log and incorrectly reports an error.

**Prevention:**
- Check `LastWriteTimeUtc` against process start time, or record byte offset before launch.
- The offset approach is more reliable (timestamps have second-level granularity issues).

**Phase:** Phase 1 -- implement exception log reading with staleness check.

---

### Pitfall 8: Offset Becomes Invalid If Log File Is Truncated

**What goes wrong:** xEdit truncates the log file at 3MB on normal exit (verified: `xeMainForm.pas` line 6153, `if fs.Size > 3 * 1024 * 1024 then fs.Size := 0`). If the pre-launch offset was > 0 and xEdit truncated the file before appending new content, the offset exceeds the new file size.

**Important detail:** The truncation check happens BEFORE writing new content (with `aAllowReplace=True`). So xEdit opens the file, checks size, optionally truncates to 0, then appends new content. The resulting file contains ONLY the current session's messages.

**Prevention:**
```csharp
if (previousOffset > currentFileLength)
{
    // File was truncated by xEdit (>3MB cleanup) -- read from beginning
    previousOffset = 0;
}
```

**Phase:** Phase 1 -- add bounds check in `ReadNewLogContentAsync`.

---

### Pitfall 9: MO2 Mode Log File Path is Unchanged

**What goes wrong:** Developers might assume MO2's VFS redirects xEdit's log file. It does not. Log files are written to xEdit's install directory, which is outside VFS scope.

**Prevention:** Use the same log file path resolution in both modes. The current code correctly uses `xEditExecutablePath` from config. Preserve this.

**Phase:** All phases -- design invariant. Write a test.

---

### Pitfall 10: Breaking MO2 with UseShellExecute

**What goes wrong:** Changing `ProcessExecutionService` to `UseShellExecute = true` (to "fix" stdout) breaks MO2 wrapping because `ShellExecute` handles argument quoting differently and prevents `Process.Id` access.

**Prevention:** Keep `UseShellExecute = false`. Set `RedirectStandardOutput = false` and `RedirectStandardError = false`. Remove `BeginOutputReadLine()` / `BeginErrorReadLine()` calls.

**Phase:** Phase 2.

---

### Pitfall 11: Multi-Pass QAC Output Accumulation

**What goes wrong:** QAC runs cleaning up to 3 times. The log contains output from ALL passes. If the parser does not account for this, it may double-count items from the first pass that appear in log content from subsequent passes.

**Why it happens:** Each QAC pass runs "Undelete and Disable References" and "Remove Identical to Master" sequentially. Subsequent passes typically find 0 items (the first pass cleaned everything). But if the parser sums Removing: lines across all passes AND xEdit writes per-record lines for all passes, counts could be wrong.

**In practice:** This is NOT a real problem because subsequent passes that find 0 items produce only summary lines (`Processed Records: N, Removed Records: 0`), not per-record `Removing:` lines. The per-record lines only appear when something is actually removed. So summing all `Removing:` lines across passes gives the correct total.

**Prevention:** No special handling needed, but add a test with multi-pass log output to verify.

**Phase:** Phase 3 -- add test case.

---

### Pitfall 12: xEdit -R: Flag Overrides Log File Location

**What goes wrong:** xEdit supports `-R:<path>` to redirect logs to a custom location. AutoQAC does not pass this flag, but users may have it configured in shortcuts or mod manager profiles.

**Prevention:** Document as a known limitation. Do not attempt to detect external `-R:` configuration.

**Phase:** Not addressed in this milestone. Document in release notes.

## Minor Pitfalls

### Pitfall 13: Partial First Line After Byte Offset Seek

**What goes wrong:** If seeking to a byte offset and the previous content did not end with a newline, the first "line" is a partial garbled fragment.

**Prevention:** xEdit's `SaveLog` appends `#13#10` (CRLF) after content. This means the new content starts on a fresh line. Defensive check: if first character at offset is not the start of a recognized line, skip to the next newline.

**Phase:** Phase 1 -- defensive check.

---

### Pitfall 14: DateTime Precision in Staleness Detection

**What goes wrong:** NTFS timestamps have ~2-second granularity. If cleaning starts within the same 2-second window as a previous log write, timestamp detection fails.

**Prevention:** The offset-based approach eliminates this. Remove timestamp-based staleness detection after offset reading is implemented.

**Phase:** Phase 4 (cleanup).

---

### Pitfall 15: Existing Test Mocks Return stdout Output Lines

**What goes wrong:** Test mocks set `ProcessResult.OutputLines` with xEdit-like content. After the fix, these lines will be empty. Tests pass for the wrong reason.

**Prevention:** Update all test mocks to reflect the new behavior. Update assertions.

**Phase:** Phases 2-4 -- update tests as each service changes.

---

### Pitfall 16: Log Filename Casing Detail

**What goes wrong:** The existing code uppercases the entire stem: `SSEEDIT_log.txt`. xEdit actually writes `SSEEdit_log.txt` (mixed case: `wbAppName` is `SSE`, `wbToolName` is `Edit`). On case-insensitive NTFS this doesn't matter, but it's technically wrong.

**Prevention:** Use the correct casing from the mapping table. No practical impact on Windows, but cleaner code.

**Phase:** Phase 1 -- use correct casing in the lookup table.

## Phase-Specific Warnings

| Phase Topic | Likely Pitfall | Mitigation |
|-------------|---------------|------------|
| Phase 1: Game-aware log naming | Pitfall 1 (naming mismatch) | GameType-to-prefix lookup table from xEdit source |
| Phase 1: Offset methods | Pitfall 2 (appended content), Pitfall 4 (FileShare), Pitfall 8 (truncation) | Record offset before launch, use FileStream(FileShare.ReadWrite), bounds check |
| Phase 1: Exception log | Pitfall 7 (stale exception) | Timestamp or offset check |
| Phase 2: Stdout removal | Pitfall 10 (UseShellExecute) | Keep UseShellExecute = false, only remove redirect flags |
| Phase 3: Orchestrator integration | Pitfall 3 (false positive), Pitfall 5 (force-kill), Pitfall 11 (multi-pass) | Verify case sensitivity handles "removing:" vs "Removing:", check termination status, add multi-pass test |
| Phase 4: Cleanup | Pitfall 14 (timestamp removal), Pitfall 15 (test mocks) | Remove timestamp detection, update all tests |

## Sources

- xEdit source code (`xeMainForm.pas`, `xeInit.pas`) on [TES5Edit GitHub](https://github.com/TES5Edit/TES5Edit), branches dev-4.1.5 and dev-4.1.6 (HIGH confidence)
- [XEdit-PACT Source](https://github.com/GuidanceOfGrace/XEdit-PACT) - `PACT_Start.py` for log naming conventions and exception log patterns (HIGH confidence)
- [xEdit What's New](https://github.com/TES5Edit/TES5Edit.github.io/blob/master/whatsnew.md) - "Log file is overwritten at 3MB" (v3.0.30), "Saving messages to [TES5/FNV/FO3/TES4]Edit_log.txt upon exit" (v3.0.23) (HIGH confidence)
- [.NET FileShare Enum](https://learn.microsoft.com/en-us/dotnet/api/system.io.fileshare) (HIGH confidence)
- Direct codebase analysis of `XEditLogFileService.cs`, `CleaningOrchestrator.cs`, `ProcessExecutionService.cs`, `XEditCommandBuilder.cs` (HIGH confidence)

---
*Pitfalls analysis: 2026-03-30 -- Updated with xEdit source verification*
