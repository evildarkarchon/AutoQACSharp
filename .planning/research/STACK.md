# Technology Stack: xEdit Log File Parsing

**Project:** AutoQAC - xEdit Log Parsing Fix
**Researched:** 2026-03-30 (updated with xEdit source verification)
**Focus:** .NET 10 APIs for reading log files written by an external process on exit

## Problem Statement

AutoQAC currently captures xEdit stdout/stderr via `ProcessExecutionService.RedirectStandardOutput`. xEdit does not write to stdout -- it writes log files to its install directory on process exit. The service needs to:

1. Compute the correct log file path using game-aware naming (NOT executable stem)
2. Record the log file's byte offset before launching xEdit
3. Wait for xEdit to exit
4. Read only the new content appended after that offset
5. Handle the brief window where xEdit may still hold the file lock after its process reports as exited

## Recommended Approach

### Use `FileStream` with Seek -- Not `RandomAccess`, Not `File.ReadAllLinesAsync`

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| `FileStream` | .NET 10 (System.IO) | Read log file bytes from a recorded offset | Provides `FileShare.ReadWrite` for lock-safe reads, `Seek` for offset positioning, and `ReadAsync(Memory<byte>)` for async I/O. Standard, well-understood, matches codebase conventions. |
| `StreamReader` | .NET 10 (System.IO) | Decode bytes to text lines | Wraps `FileStream` to handle encoding. Must be constructed on the already-seeked stream. |
| `FileInfo.Length` | .NET 10 (System.IO) | Capture file size as pre-launch offset | Lightweight stat call, does not open the file. Returns `0` if file does not exist yet. |
| `Encoding.UTF8` or system default | .NET 10 (System.Text) | Text decoding | xEdit writes ANSI (code page 1252) but regex keywords are all ASCII, so UTF-8 decoding works for counting. See Encoding section below. |

**Confidence:** HIGH -- verified against Microsoft .NET 10 API documentation and xEdit source code

### Why Not `RandomAccess`?

`RandomAccess` (introduced .NET 6) provides offset-based, thread-safe, stateless file I/O via `File.OpenHandle()` + `RandomAccess.ReadAsync(handle, buffer, offset)`.

**However, it is wrong for this use case because:**
- It operates on raw bytes (`Memory<byte>`), not text lines. We need line-delimited text for the regex parser.
- It requires manual buffer management and encoding.
- The file is small (xEdit logs are typically 1-50 KB per run).
- The codebase uses `StreamReader` elsewhere. `RandomAccess` would be inconsistent.

### Why Not `File.ReadAllLinesAsync`?

- Reads the **entire file** every time (no offset support)
- Opens with `FileShare.Read` internally (throws `IOException` if file is still locked)
- Cannot isolate current run's content from previous runs

## Game-Aware Log File Path Mapping

**NEW REQUIREMENT (discovered from xEdit source):** The log file is NOT named after the executable. It uses `wbAppName + wbToolName + '_log.txt'` where `wbAppName` is the game prefix from xEdit's internal mode detection.

| AutoQAC GameType | xEdit AppName | Log Filename |
|-----------------|---------------|-------------|
| Oblivion | TES4 | TES4Edit_log.txt |
| SkyrimLe | TES5 | TES5Edit_log.txt |
| SkyrimSe | SSE | SSEEdit_log.txt |
| SkyrimVr | TES5VR | TES5VREdit_log.txt |
| Fallout3 | FO3 | FO3Edit_log.txt |
| FalloutNewVegas | FNV | FNVEdit_log.txt |
| Fallout4 | FO4 | FO4Edit_log.txt |
| Fallout4Vr | FO4VR | FO4VREdit_log.txt |

The exception log uses executable-stem naming: `<STEM_UPPER>Exception.log`.

**Implementation:** `GetLogFilePath` must accept a `GameType` parameter (or the method must resolve the game type internally). Use a `Dictionary<GameType, string>` or switch expression for the mapping.

## Recommended Implementation Pattern

### Step 0: Compute Log File Path (Game-Aware)

```csharp
// GameType-to-log-prefix mapping (from xeInit.pas)
private static string GetLogFileName(GameType gameType) => gameType switch
{
    GameType.Oblivion => "TES4Edit_log.txt",
    GameType.SkyrimLe => "TES5Edit_log.txt",
    GameType.SkyrimSe => "SSEEdit_log.txt",
    GameType.SkyrimVr => "TES5VREdit_log.txt",
    GameType.Fallout3 => "FO3Edit_log.txt",
    GameType.FalloutNewVegas => "FNVEdit_log.txt",
    GameType.Fallout4 => "FO4Edit_log.txt",
    GameType.Fallout4Vr => "FO4VREdit_log.txt",
    _ => throw new ArgumentException($"Unknown game type: {gameType}")
};

public string GetLogFilePath(string xEditExecutablePath, GameType gameType)
{
    var dir = Path.GetDirectoryName(xEditExecutablePath);
    return Path.Combine(dir!, GetLogFileName(gameType));
}
```

### Step 1: Capture Offset Before Launch

```csharp
var logPath = logFileService.GetLogFilePath(xEditExecutablePath, gameType);
long preOffset = File.Exists(logPath) ? new FileInfo(logPath).Length : 0;
```

### Step 2: Read From Offset After Process Exit

```csharp
using var fs = new FileStream(
    logPath,
    FileMode.Open,
    FileAccess.Read,
    FileShare.ReadWrite,
    bufferSize: 4096);

// Handle truncation: xEdit truncates at 3MB before writing new content
if (preOffset > fs.Length)
    preOffset = 0;

fs.Seek(preOffset, SeekOrigin.Begin);

using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
var newContent = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
var lines = newContent.Split('\n', StringSplitOptions.RemoveEmptyEntries)
    .Select(l => l.TrimEnd('\r'))
    .ToList();
```

### Step 3: Retry on IOException

```csharp
const int retryDelayMs = 250;
const int maxRetries = 3;

for (int attempt = 0; attempt < maxRetries; attempt++)
{
    try
    {
        return await ReadFromOffset(logPath, preOffset, ct);
    }
    catch (IOException) when (attempt < maxRetries - 1)
    {
        await Task.Delay(retryDelayMs * (attempt + 1), ct).ConfigureAwait(false);
    }
}
```

## Encoding Considerations

**CORRECTION from previous version:** xEdit log files are NOT UTF-8. xEdit writes using Delphi's `AnsiString` type (verified: `xeMainForm.pas` line 6154), which uses Windows code page 1252 (ANSI Latin I). The log files have no BOM.

**Pragmatic approach (recommended):** The regex patterns match ASCII-only keywords (`Removing:`, `Undeleting:`, `Skipping:`, `Making Partial Form:`). Code page 1252 and UTF-8 produce identical byte sequences for ASCII (0x00-0x7F). The keyword matching works correctly with UTF-8 decoding. Only the captured text after the colon (record descriptions) may contain non-ASCII characters, but these are counted, not displayed.

**If descriptions are displayed later:** Register `CodePagesEncodingProvider` and use `Encoding.GetEncoding(1252)`:
```csharp
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // once at startup
using var reader = new StreamReader(fs, Encoding.GetEncoding(1252));
```

**Line endings:** xEdit appends `#13#10` (CRLF) after the content block. Lines within the content use CRLF as well (from Delphi's `mmoMessages.Lines.Text`).

## API Surface for the Updated Interface

```csharp
public interface IXEditLogFileService
{
    // CHANGED: Now requires GameType for game-aware naming
    string GetLogFilePath(string xEditExecutablePath, GameType gameType);

    // NEW: Exception log path (still uses executable stem)
    string GetExceptionLogPath(string xEditExecutablePath);

    // NEW: Get current file size as offset, returns 0 if file does not exist
    long GetCurrentOffset(string logFilePath);

    // NEW: Read only new content from offset position
    Task<(List<string> lines, string? error)> ReadNewLogContentAsync(
        string logFilePath,
        long previousOffset,
        CancellationToken ct = default);

    // NEW: Read exception log if it exists and is from this run
    Task<(string? content, string? error)> ReadExceptionLogAsync(
        string xEditExecutablePath,
        DateTime processStartTime,
        CancellationToken ct = default);
}
```

## No New Dependencies Required

All recommended APIs are part of `System.IO` and `System.Text` in .NET 10. No NuGet packages need to be added. If code page 1252 support is needed later, `System.Text.Encoding.CodePages` is already available in .NET 10.

## Alternatives Considered

| Category | Recommended | Alternative | Why Not |
|----------|-------------|-------------|---------|
| File reading API | `FileStream` + `StreamReader` | `RandomAccess` + manual decoding | Overkill for small text files |
| File reading API | `FileStream` + `StreamReader` | `File.ReadAllLinesAsync` | No offset support; `FileShare.Read` default causes lock conflicts |
| Log file naming | GameType lookup table | Executable stem uppercased | Wrong for universal `xEdit.exe` with game flags |
| Offset tracking | `FileInfo.Length` before launch | Timestamp-based staleness check | Fragile; doesn't isolate current run's output |
| Encoding | UTF-8 (pragmatic) | Code page 1252 (correct) | UTF-8 works for ASCII keyword matching; 1252 only needed if displaying descriptions |

## Sources

- [FileStream Class - Microsoft Learn (.NET 10)](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream?view=net-10.0)
- [FileShare Enum - Microsoft Learn (.NET 10)](https://learn.microsoft.com/en-us/dotnet/api/system.io.fileshare?view=net-10.0)
- xEdit source code: `xeMainForm.pas` line 6166 (log file naming), `xeInit.pas` lines 710-832 (wbAppName per game mode)
- [TES5Edit/TES5Edit GitHub Repository](https://github.com/TES5Edit/TES5Edit)

---

*Stack research: 2026-03-30 -- Updated with xEdit source verification*
