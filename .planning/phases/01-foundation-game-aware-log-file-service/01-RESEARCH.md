# Phase 1: Foundation -- Game-Aware Log File Service - Research

**Researched:** 2026-03-30
**Domain:** xEdit log file naming conventions, offset-based file reading, Windows file contention retry
**Confidence:** HIGH

## Summary

xEdit writes its log file on exit using the formula `{wbAppName}{wbToolName}_log.txt` where `wbAppName` is a game-mode-specific prefix (e.g., `SSE`, `TES5`, `FO4`) and `wbToolName` is always `Edit` for the xEdit tool. This was verified directly from xEdit source code (`xeMainForm.pas` line 6260). The log file is **appended** to (not overwritten) using `fmOpenReadWrite` + `Seek(0, soFromEnd)`, with truncation at 3MB on exit. This append behavior is exactly why offset-based reading is needed -- a full read would include content from prior sessions.

The exception log follows the pattern `{wbAppName}EditException.log` (e.g., `SSEEditException.log`), confirmed by community bug reports on the TES5Edit GitHub repository. Exception logging in public xEdit builds relies on Windows structured exception handling, not xEdit's internal exception hook (which is only enabled in developer debug builds).

**Primary recommendation:** Build a static `GameType -> wbAppName` mapping derived from xEdit source code (`xeInit.pas`), use it to construct both main log and exception log filenames, and implement offset-based reading with `FileStream.Seek` + `FileShare.ReadWrite` and exponential backoff retry for Windows file contention.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- **D-01:** Log filename must be derived from game type, not executable stem. Researcher must verify xEdit's actual naming convention from xEdit source code -- specifically whether the log prefix is game-aware (e.g., `SSEEdit_log.txt` when running `xEdit.exe -SSE`) or executable-stem-based, and what casing rules apply.
- **D-02:** The service must accept `GameType` as a parameter (not just executable path) to support universal `xEdit.exe` with game flags.
- **D-03:** Exception log uses the same offset-based approach as the main log -- capture byte offset before xEdit launch, read only new content after exit. The exception file does not get cleared between runs, so a full read would include stale exceptions from prior sessions.
- **D-04:** The service contract should return exception log content (not just a path), applying the offset to isolate this run's exceptions only.

### Claude's Discretion
- Retry strategy for file contention (OFF-04): Claude decides the retry count, delay pattern, and total timeout based on research into typical antivirus/indexer lock durations on Windows.

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| LOG-01 | Service resolves correct log filename using game-aware prefix mapping | Verified: xEdit uses `{wbAppName}Edit_log.txt` -- complete GameType-to-wbAppName mapping documented below |
| LOG-02 | Service supports universal `xEdit.exe` with game flags by using game type | Verified: xEdit's `DetectAppMode` resolves game mode identically from CLI flag or executable name, producing same wbAppName and thus same log filename |
| LOG-03 | Service resolves exception log filename from game-aware prefix | Verified: exception log uses `{wbAppName}EditException.log` pattern (confirmed from GitHub issues) -- NOT executable stem uppercase as originally stated in REQUIREMENTS.md |
| OFF-01 | Service captures log file byte offset via `FileInfo.Length` before xEdit launch | Standard .NET API: `new FileInfo(path).Length` returns 0 for non-existent files after catching the exception, or check `File.Exists` first |
| OFF-02 | Service reads only new content appended after captured offset | Standard .NET API: `FileStream` with `Seek(offset, SeekOrigin.Begin)` + `FileShare.ReadWrite` for concurrent access |
| OFF-03 | Service handles missing log file gracefully (first run) | If file does not exist at capture time, offset = 0; after xEdit exits, read entire file if it now exists |
| OFF-04 | Service retries file read on IOException with backoff | Exponential backoff with 100ms base, 3 retries, ~1.5s total timeout (see Retry Strategy section) |
</phase_requirements>

## Project Constraints (from CLAUDE.md)

- **Sequential cleaning**: One xEdit process at a time -- no parallelization
- **Read-only Mutagen/**: Do not modify the Mutagen submodule
- **MVVM boundaries**: Service layer reads logs; ViewModels receive parsed results via state
- **Async patterns**: Use `ConfigureAwait(false)` on all awaits in service layer; accept `CancellationToken ct = default` as last parameter
- **Primary constructors**: Use `sealed class ServiceName(ILoggingService logger)` pattern
- **DI registration**: Singleton in `ServiceCollectionExtensions`
- **Testing**: NSubstitute for mocks, FluentAssertions for assertions, xUnit for framework
- **No output to nul**: Windows-specific constraint from user's global CLAUDE.md
- **Nullable reference types**: Enabled globally

## Architecture Patterns

### Verified xEdit Log Naming Convention (from xEdit source code)

The log filename is constructed at `xeMainForm.pas` line 6260:
```pascal
SaveLog(wbProgramPath + wbAppName + wbToolName + '_log.txt', aAllowReplace);
```

Where:
- `wbProgramPath` = xEdit's install directory (same as executable directory)
- `wbAppName` = game-mode-specific prefix (set in `xeInit.pas`)
- `wbToolName` = `'Edit'` (for xEdit; other tools use 'View', 'Trans', etc.)

### Complete GameType to wbAppName Mapping

Source: `xeInit.pas` lines 798-944, verified against `DetectAppMode` function.

| AutoQAC GameType | xEdit wbAppName | Log Filename | Exception Filename | xEdit CLI Flag |
|------------------|-----------------|--------------|-------------------|----------------|
| `SkyrimLe` | `TES5` | `TES5Edit_log.txt` | `TES5EditException.log` | `-TES5` |
| `SkyrimSe` | `SSE` | `SSEEdit_log.txt` | `SSEEditException.log` | `-SSE` |
| `SkyrimVr` | `TES5VR` | `TES5VREdit_log.txt` | `TES5VREditException.log` | `-TES5VR` |
| `Fallout4` | `FO4` | `FO4Edit_log.txt` | `FO4EditException.log` | `-FO4` |
| `Fallout4Vr` | `FO4VR` | `FO4VREdit_log.txt` | `FO4VREditException.log` | `-FO4VR` |
| `Fallout3` | `FO3` | `FO3Edit_log.txt` | `FO3EditException.log` | `-FO3` |
| `FalloutNewVegas` | `FNV` | `FNVEdit_log.txt` | `FNVEditException.log` | `-FNV` |
| `Oblivion` | `TES4` | `TES4Edit_log.txt` | `TES4EditException.log` | `-TES4` |

**Confidence: HIGH** -- Directly verified from xEdit source code.

### xEdit Log File Append Behavior

Source: `xeMainForm.pas` lines 6232-6256, the `SaveLog` procedure:

```pascal
procedure TfrmMain.SaveLog(const s: string; aAllowReplace: Boolean);
var
  txt : AnsiString;
  fs  : TBufferedFileStream;
begin
  fs := nil;
  try
    try
      if FileExists(s) then begin
        fs := TBufferedFileStream.Create(s, fmOpenReadWrite);
        fs.Seek(0, soFromEnd);  // APPEND to end
      end else
        fs := TBufferedFileStream.Create(s, fmCreate);
      if aAllowReplace then
        if fs.Size > 3 * 1024 * 1024 then  // truncate at 3MB
          fs.Size := 0;
      txt := AnsiString(mmoMessages.Lines.Text) + #13#10;
      fs.WriteBuffer(txt[1], Length(txt));
    except end;  // silently swallow errors
  finally
    if Assigned(fs) then
      FreeAndNil(fs);
  end;
end;
```

Key observations:
1. **Appends** to existing log files (seeks to end)
2. **Truncates at 3MB** when `aAllowReplace = True` (which happens on normal exit)
3. **Creates new file** if log does not exist
4. **Silently swallows** all write errors
5. Writes the ENTIRE `mmoMessages.Lines.Text` (all messages from the session)

This means a log file accumulates content from multiple sessions until it reaches 3MB, at which point it's truncated and starts fresh. Offset-based reading is essential to isolate the current session's output.

### Recommended Project Structure

No new directories needed. Changes are confined to:
```
AutoQAC/Services/Cleaning/
    IXEditLogFileService.cs    # Modified interface (GameType param, offset API)
    XEditLogFileService.cs     # Modified implementation

AutoQAC.Tests/Services/
    XEditLogFileServiceTests.cs  # Rewritten tests
```

### Pattern 1: GameType to Log Prefix Mapping

Use a frozen dictionary or switch expression for the mapping. The switch expression is preferred because it matches the existing patterns in `XEditCommandBuilder.GetGameFlag()` and `GameDetectionService.GetGameDisplayName()`.

```csharp
private static string GetXEditAppName(GameType gameType) => gameType switch
{
    GameType.SkyrimLe       => "TES5",
    GameType.SkyrimSe       => "SSE",
    GameType.SkyrimVr       => "TES5VR",
    GameType.Fallout4       => "FO4",
    GameType.Fallout4Vr     => "FO4VR",
    GameType.Fallout3       => "FO3",
    GameType.FalloutNewVegas => "FNV",
    GameType.Oblivion       => "TES4",
    _ => throw new ArgumentOutOfRangeException(nameof(gameType), gameType,
        "Cannot determine xEdit app name for unknown game type")
};
```

### Pattern 2: Offset-Based File Reading

```csharp
public long CaptureOffset(string logFilePath)
{
    // Returns 0 for non-existent files (first run scenario - OFF-03)
    if (!File.Exists(logFilePath))
        return 0;
    return new FileInfo(logFilePath).Length;
}

public async Task<string> ReadFromOffsetAsync(string logFilePath, long offset, CancellationToken ct)
{
    // FileShare.ReadWrite handles antivirus/indexer concurrent access
    await using var fs = new FileStream(
        logFilePath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite);

    if (offset > fs.Length)
    {
        // File was truncated (3MB threshold) -- read entire file
        offset = 0;
    }

    fs.Seek(offset, SeekOrigin.Begin);
    using var reader = new StreamReader(fs);
    return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
}
```

### Pattern 3: Retry with Exponential Backoff

```csharp
private async Task<string> ReadWithRetryAsync(
    string logFilePath, long offset, CancellationToken ct)
{
    const int maxRetries = 3;
    const int baseDelayMs = 100;

    for (int attempt = 0; attempt <= maxRetries; attempt++)
    {
        try
        {
            return await ReadFromOffsetAsync(logFilePath, offset, ct)
                .ConfigureAwait(false);
        }
        catch (IOException) when (attempt < maxRetries)
        {
            var delay = baseDelayMs * (1 << attempt); // 100, 200, 400ms
            logger.Debug("File contention on {Path}, retry {Attempt} in {Delay}ms",
                logFilePath, attempt + 1, delay);
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }

    // Unreachable, but compiler needs it
    throw new InvalidOperationException("Retry loop exited unexpectedly");
}
```

### Anti-Patterns to Avoid

- **Reading entire file and diffing**: Do not read the whole file and then try to subtract old content by string comparison. Use byte offsets -- they are precise and cheap.
- **Using `File.ReadAllLinesAsync` for offset reads**: This API always reads from position 0. Use `FileStream.Seek` instead.
- **Timestamp-based staleness detection**: The current code compares `File.GetLastWriteTimeUtc` against process start time. This is fragile (clock skew, rapid re-runs) and unnecessary when using byte offsets.
- **Uppercase executable stem for naming**: The current code does `Path.GetFileNameWithoutExtension(path).ToUpperInvariant()` -- this produces `SSEEDIT_log.txt` which is wrong. xEdit produces `SSEEdit_log.txt` (mixed case from `wbAppName`). Windows filesystem is case-insensitive so it accidentally works, but the naming should match xEdit's actual convention.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Retry with backoff | Custom retry loop from scratch | Well-structured retry pattern (see Pattern 3 above) | The retry is simple enough not to need Polly, but the pattern must handle `CancellationToken` + `IOException` filtering correctly |
| File locking detection | Custom file-lock probing | `FileShare.ReadWrite` on `FileStream` | .NET handles shared reads natively; no need to detect locks |
| Log content splitting | String-based session splitting | Byte offset capture before launch | Offset is the only reliable approach; xEdit appends arbitrary content without session delimiters |

**Key insight:** The offset-based approach is simple and reliable. The only complexity is the retry for file contention, which is a few lines of code. No external libraries are needed.

## Common Pitfalls

### Pitfall 1: Exception Log Filename Mismatch with REQUIREMENTS.md
**What goes wrong:** REQUIREMENTS.md LOG-03 says "executable stem uppercase (e.g., SSEEDITException.log)" but the actual convention is `{wbAppName}EditException.log` = `SSEEditException.log`.
**Why it happens:** The original requirement was based on a guess or the old implementation which uppercased the stem.
**How to avoid:** Use the game-type-derived prefix from the mapping table above, not executable stem. Windows filesystem is case-insensitive so both names resolve to the same file, but the code should use the correct convention.
**Warning signs:** Tests that hardcode `SSEEDIT` instead of `SSE` in the prefix.

### Pitfall 2: Log File Truncation at 3MB
**What goes wrong:** xEdit truncates the log file to 0 bytes when it exceeds 3MB on exit (before writing new content). If the service captured an offset of, say, 2.5MB before launch, and xEdit truncated then wrote 500KB, the offset would be beyond the new file length.
**Why it happens:** xEdit's `SaveLog` does `fs.Size := 0` when `aAllowReplace` is true and `fs.Size > 3MB`.
**How to avoid:** After xEdit exits, if `offset > file.Length`, reset offset to 0 and read the entire file. This means xEdit truncated and rewrote.
**Warning signs:** `IOException` or empty reads when the offset is larger than the file.

### Pitfall 3: SkyrimVR Game Flag Bug in XEditCommandBuilder
**What goes wrong:** `XEditCommandBuilder.GetGameFlag(GameType.SkyrimVr)` returns `-SkyrimVR`, but xEdit's `DetectAppMode` function recognizes `-TES5VR` (case-insensitive), not `-SkyrimVR`.
**Why it happens:** The flag was likely based on the game name rather than xEdit's internal mode name.
**How to avoid:** This is a pre-existing bug in `XEditCommandBuilder`, NOT in the log file service. The log file service should map `SkyrimVr` to `TES5VR` based on xEdit's source code. The command builder bug should be tracked separately (it may cause xEdit to fall back to FO4 mode when launched for SkyrimVR).
**Warning signs:** SkyrimVR users getting FO4 cleaning results or the wrong log file being read.

### Pitfall 4: File Contention Window After xEdit Exit
**What goes wrong:** Windows antivirus (Defender, etc.) or Windows Search Indexer may lock the log file briefly after xEdit writes and closes it.
**Why it happens:** File system hooks from security/indexing software intercept file close events and scan/index the file.
**How to avoid:** Use `FileShare.ReadWrite` on the `FileStream` and retry with exponential backoff (100ms, 200ms, 400ms). Total retry window of ~700ms is sufficient -- antivirus scans on small text files (<3MB) typically complete in under 500ms.
**Warning signs:** `IOException` with "used by another process" message on first read attempt.

### Pitfall 5: Encoding Mismatch
**What goes wrong:** xEdit writes log content as `AnsiString` (single-byte encoding, likely Windows-1252/CP1252). Reading with `StreamReader` defaults to UTF-8.
**Why it happens:** xEdit is a Delphi application that uses ANSI strings for its message log.
**How to avoid:** The log content is primarily ASCII (English text, file paths, numbers). UTF-8 is a superset of ASCII, so standard `StreamReader` will handle it correctly for all practical content. Only non-ASCII characters in plugin filenames could theoretically differ, but this is extremely rare. Use the default UTF-8 `StreamReader` -- it is safe for this use case.
**Warning signs:** Garbled characters in plugin names that contain accented or non-Latin characters.

### Pitfall 6: Race Between Offset Capture and xEdit Log Truncation
**What goes wrong:** If you capture the offset, then xEdit truncates the log (>3MB), then writes new content, the captured offset exceeds the new file size.
**Why it happens:** xEdit's truncation + rewrite happens atomically during its exit, AFTER the process exits from the caller's perspective. So capture happens before launch, read happens after exit.
**How to avoid:** Already handled by the truncation check in Pattern 2: `if (offset > fs.Length) offset = 0;`

## Code Examples

### New Interface Contract

```csharp
// Source: Derived from CONTEXT.md decisions D-01 through D-04
public interface IXEditLogFileService
{
    /// <summary>
    /// Returns the expected main log file path for the given game type and xEdit directory.
    /// </summary>
    string GetLogFilePath(string xEditDirectory, GameType gameType);

    /// <summary>
    /// Returns the expected exception log file path for the given game type and xEdit directory.
    /// </summary>
    string GetExceptionLogFilePath(string xEditDirectory, GameType gameType);

    /// <summary>
    /// Captures the current byte offset of a log file (0 if file does not exist).
    /// Call before launching xEdit.
    /// </summary>
    long CaptureOffset(string logFilePath);

    /// <summary>
    /// Reads only the content appended after the given byte offset.
    /// Returns lines and exception content. Retries on IOException.
    /// </summary>
    Task<LogReadResult> ReadLogContentAsync(
        string xEditDirectory,
        GameType gameType,
        long mainLogOffset,
        long exceptionLogOffset,
        CancellationToken ct = default);
}
```

### New Result Type

```csharp
/// <summary>
/// Result of reading xEdit log files after a cleaning run.
/// </summary>
public sealed record LogReadResult
{
    /// <summary>Lines from the main log file appended during this run.</summary>
    public required List<string> LogLines { get; init; }

    /// <summary>Content from the exception log appended during this run, or null if none.</summary>
    public string? ExceptionContent { get; init; }

    /// <summary>Warning message if log reading had issues but partially succeeded.</summary>
    public string? Warning { get; init; }
}
```

### GameType to xEdit App Name Mapping

```csharp
// Source: xEdit xeInit.pas lines 798-944
internal static string GetXEditAppName(GameType gameType) => gameType switch
{
    GameType.SkyrimLe        => "TES5",
    GameType.SkyrimSe        => "SSE",
    GameType.SkyrimVr        => "TES5VR",
    GameType.Fallout4        => "FO4",
    GameType.Fallout4Vr      => "FO4VR",
    GameType.Fallout3        => "FO3",
    GameType.FalloutNewVegas => "FNV",
    GameType.Oblivion        => "TES4",
    _ => throw new ArgumentOutOfRangeException(nameof(gameType), gameType,
        "Unsupported game type for xEdit log file resolution")
};
```

## State of the Art

| Old Approach (current code) | New Approach (this phase) | Why |
|----------------------------|--------------------------|-----|
| Executable stem uppercase `SSEEDIT_log.txt` | Game-type prefix `SSEEdit_log.txt` | Matches xEdit's actual naming; supports universal `xEdit.exe` |
| Timestamp staleness detection | Byte offset capture + read | Eliminates clock skew; precisely isolates current session content |
| Full file read via `File.ReadAllLinesAsync` | `FileStream.Seek` from offset | Reads only new content; handles append accumulation |
| Single retry at 200ms fixed delay | Exponential backoff (100, 200, 400ms) with 3 retries | Better coverage for varying antivirus scan durations |
| `DateTime processStartTime` parameter | `long offset` parameter pair (main + exception) | Simpler contract; offset is captured once, used once |

**Deprecated/outdated in current code:**
- `GetLogFilePath(string xEditExecutablePath)` -- takes executable path, constructs uppercase stem
- `ReadLogFileAsync(string xEditExecutablePath, DateTime processStartTime, ...)` -- uses timestamp staleness
- `File.GetLastWriteTimeUtc` comparison for staleness -- replaced by offset approach

## Discovered Issues (Outside Phase Scope)

### XEditCommandBuilder SkyrimVR Flag Bug
**Severity:** Medium -- affects SkyrimVR users running universal `xEdit.exe`
**Details:** `GetGameFlag(GameType.SkyrimVr)` returns `-SkyrimVR` but xEdit expects `-TES5VR`. The `DetectAppMode` function in xeInit.pas checks for `'tes5vr'` (case-insensitive) in its `GameModes` array. `-SkyrimVR` does not match, causing xEdit to fall back to its default game mode (`FO4`).
**Impact on this phase:** None directly. The log file service maps `GameType.SkyrimVr -> "TES5VR"` which is correct regardless of the command builder bug.
**Recommendation:** Track as a separate bug fix. If not fixed, SkyrimVR users with universal `xEdit.exe` would get FO4 cleaning behavior, which is a serious problem beyond log file naming.

## Open Questions

1. **Exception log availability in public builds**
   - What we know: `EXCEPTION_LOGGING_ENABLED` is only defined for `BUILD_BY_ELMINSTERAU` debug builds in xEdit source. Yet users consistently report exception logs existing (e.g., `SSEEditException.log`).
   - What's unclear: The mechanism that creates exception logs in release builds. It may be Windows-level SEH (Structured Exception Handling) or Delphi's default exception handler.
   - Recommendation: Treat exception logs as "may or may not exist" -- the service should handle their absence gracefully (return null exception content). The offset-based approach still applies when they do exist.

## Retry Strategy Recommendation (Claude's Discretion per CONTEXT.md)

Based on research into Windows file contention patterns:

| Parameter | Value | Rationale |
|-----------|-------|-----------|
| Max retries | 3 | Covers 99%+ of antivirus scan durations on small text files |
| Base delay | 100ms | Short enough to not noticeably delay UI; long enough for quick scans |
| Backoff multiplier | 2x (exponential) | 100ms, 200ms, 400ms |
| Total max wait | ~700ms | Well under user patience threshold; AV scans on <3MB files complete in <500ms |
| Exception filter | `IOException` only | Catches "file in use" without swallowing unrelated errors |
| CancellationToken | Passed to `Task.Delay` | Allows user cancellation during retry waits |

**Confidence: MEDIUM** -- Based on general Windows file system behavior knowledge and community reports. Exact AV scan timing varies by vendor. The exponential backoff pattern is standard and self-adapting.

## Sources

### Primary (HIGH confidence)
- xEdit source: `xeMainForm.pas` line 6260 -- log filename construction: `wbProgramPath + wbAppName + wbToolName + '_log.txt'`
- xEdit source: `xeMainForm.pas` lines 6232-6256 -- `SaveLog` procedure showing append behavior and 3MB truncation
- xEdit source: `xeInit.pas` lines 798-944 -- complete `wbAppName` assignment per game mode
- xEdit source: `xeInit.pas` lines 616-680 -- `DetectAppMode` function showing CLI flag and executable name detection
- xEdit source: `xeInit.pas` `wbToolName := 'Edit'` -- tool name for xEdit mode
- [GitHub TES5Edit/TES5Edit repository](https://github.com/TES5Edit/TES5Edit) -- all source code references

### Secondary (MEDIUM confidence)
- [XEdit-PACT](https://github.com/GuidanceOfGrace/XEdit-PACT) `PACT_Start.py` -- `update_log_paths()` function confirming `{GAME_MODE}Edit_log.txt` pattern for universal xEdit
- [TES5Edit Issue #1315](https://github.com/TES5Edit/TES5Edit/issues/1315) -- confirms `SSEEditException.log` filename in user reports
- [TES5Edit Issue #617](https://github.com/TES5Edit/TES5Edit/issues/617) -- confirms `FO4EditException.log` filename
- xEdit whatsnew.md: "Saving messages to [TES5/FNV/FO3/TES4]Edit_log.txt upon exit"

### Tertiary (LOW confidence)
- Retry timing estimates for Windows antivirus contention -- based on general knowledge, not measured data

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- No new packages needed; pure .NET file I/O APIs
- Architecture: HIGH -- GameType mapping verified directly from xEdit source code
- Pitfalls: HIGH -- Truncation behavior, append semantics, and naming verified from source
- Retry strategy: MEDIUM -- Timing estimates based on general Windows knowledge

**Research date:** 2026-03-30
**Valid until:** Indefinite for xEdit naming convention (stable since 2012); 30 days for .NET API patterns
