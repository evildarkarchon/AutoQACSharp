# Phase 2: Plugin Pipeline Robustness - Research

**Researched:** 2026-02-06
**Domain:** Plugin path resolution, load order parsing, game variant detection, error reporting
**Confidence:** HIGH (all findings from direct codebase analysis of existing code)

## Summary

Phase 2 addresses six concrete deficiencies in the plugin pipeline: FullPath is a placeholder (FileName copied verbatim), load order parsing ignores edge cases (BOM, separators, encoding, malformed entries), TTW skip list inheritance is missing, game variant detection (Enderal) is not implemented, XEditCommandBuilder accepts GameType.Unknown silently, and PluginValidationService has a bifurcated code path (rooted vs. non-rooted paths). Every finding comes from direct inspection of the existing C# code cross-referenced with the Python reference implementation.

The existing architecture is well-designed for these changes. All affected services use dependency injection, interfaces, and are already covered by unit tests. The changes are surgical -- no new libraries are needed, no architectural shifts required. The primary risk is cascading mock updates in the existing 428-test suite when interface signatures change.

**Primary recommendation:** Split work into two plans. Plan 02-01 handles PluginValidationService parsing robustness and FullPath resolution (the hard parts), while Plan 02-02 handles game variant detection and skip list inheritance (the simpler but safety-critical parts). Both plans modify different service layers, minimizing merge conflicts.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Path resolution strategy:**
- Missing plugins: warn and skip (show warning per plugin, continue cleaning the rest)
- MO2 users: resolve plugin paths through MO2's virtual filesystem (query mod list/profile to find real mod folder)
- Validation depth: check both existence AND readability (try opening file briefly to verify not locked or zero-byte)
- Known extensions only: accept .esp, .esm, .esl -- reject anything else as malformed

**Malformed input handling:**
- MO2 separator lines (lines starting with `*` or containing separator markers): strip them, log at debug level
- Encoding: auto-detect common encodings (UTF-8, UTF-16, Latin-1) rather than requiring UTF-8
- Malformed entries (path separators, control characters, missing valid extension): warn and skip -- log warning with bad line content, continue with valid entries
- No best-effort parsing of bad entries -- if it doesn't look like a valid plugin name, skip it

**TTW / game variant detection:**
- TTW detection: check for TaleOfTwoWastelands.esm (or equivalent TTW marker plugin) in the load order
- TTW skip list behavior: auto-merge FO3 skip list entries into FNV list silently -- no user prompt, no log noise
- Enderal: also needs special handling -- detect via Enderal-specific plugin, maintain its own separate skip list in config (not inherited from Skyrim)
- These are the only two game variants needing special skip list logic for now

**Error communication:**
- GameType.Unknown: block cleaning completely -- refuse to proceed without skip lists (safety critical)
- Multiple path failures: aggregated summary ("5 plugins not found: [list]") rather than one-per-plugin
- MO2 errors: include actionable fix guidance in error messages (e.g., "Check MO2 profile path in Settings")
- Missing/empty load order file: hard error with clear message pointing to the expected path

### Claude's Discretion

- Path caching strategy (session cache vs re-resolve per run)
- Exact BOM handling implementation details
- Internal error codes/categories for pipeline failures
- Order of validation steps in the pipeline

### Deferred Ideas (OUT OF SCOPE)

- Browse dialog for missing load order file recovery -- requires new UI dialog, belongs in Phase 4 or 6
- Additional game variant detection beyond TTW and Enderal -- future phase if needed
</user_constraints>

## Standard Stack

No new libraries are needed. All work uses existing .NET BCL and project dependencies.

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET BCL `System.IO` | .NET 9 | File reading, StreamReader with encoding | Built-in, no dependency needed |
| .NET BCL `System.Text.Encoding` | .NET 9 | BOM detection, encoding auto-detect | Built-in StreamReader handles BOM natively |
| Existing `IPluginValidationService` | N/A | Plugin parsing and validation | Already wired in DI, well-tested |
| Existing `IGameDetectionService` | N/A | Game type detection from executables/load orders | Already wired in DI, well-tested |
| Existing `IConfigurationService` | N/A | Skip list management | Already handles per-game skip lists |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `System.Text.Encoding.CodePages` | NuGet | Latin-1 (ISO-8859-1) support | Only if `Encoding.GetEncoding(28591)` fails without it -- .NET 9 may need this NuGet for non-UTF codepages |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Manual BOM detection | `StreamReader(path, detectEncodingFromByteOrderMarks: true)` | StreamReader handles UTF-8 BOM, UTF-16 LE/BE, UTF-32 LE/BE natively -- no custom code needed |
| Ude.NetStandard (charset detection) | Manual fallback chain | Charset detection library is overkill -- user decision says only UTF-8, UTF-16, Latin-1 |

## Architecture Patterns

### Recommended Changes by File

```
AutoQAC/
├── Models/
│   ├── PluginInfo.cs                    # Add validation result property
│   └── GameType.cs                      # No changes needed (TTW/Enderal use existing types)
├── Services/
│   ├── Plugin/
│   │   ├── IPluginValidationService.cs  # Expand interface with data folder context
│   │   └── PluginValidationService.cs   # Major rewrite: encoding, validation, path resolution
│   ├── GameDetection/
│   │   ├── IGameDetectionService.cs     # Add TTW/Enderal detection methods
│   │   └── GameDetectionService.cs      # Add variant detection from load order content
│   ├── Cleaning/
│   │   ├── XEditCommandBuilder.cs       # Reject GameType.Unknown
│   │   └── CleaningOrchestrator.cs      # Block cleaning on Unknown, aggregate path errors
│   └── Configuration/
│       └── ConfigurationService.cs      # Merge FO3 skip list for TTW, Enderal skip list
└── Tests/
    ├── PluginValidationServiceTests.cs  # Major expansion
    ├── GameDetectionServiceTests.cs     # TTW/Enderal detection tests
    ├── XEditCommandBuilderTests.cs      # Unknown rejection test
    └── CleaningOrchestratorTests.cs     # Unknown blocking test
```

### Pattern 1: Encoding-Aware File Reading
**What:** Replace `File.ReadAllLinesAsync(path, ct)` with `StreamReader`-based reading that auto-detects BOM and falls back through encodings.
**When to use:** In `PluginValidationService.GetPluginsFromLoadOrderAsync` and `GameDetectionService.DetectFromLoadOrderAsync`.
**Example:**
```csharp
// .NET's StreamReader auto-detects BOM (UTF-8, UTF-16 LE/BE, UTF-32)
// For non-BOM files, it defaults to the encoding you provide
private static async Task<string[]> ReadLinesWithEncodingDetectionAsync(
    string path, CancellationToken ct)
{
    // Try BOM-aware reading first (covers UTF-8 BOM, UTF-16 LE/BE)
    // StreamReader with detectEncodingFromByteOrderMarks=true is the default constructor
    using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
    var content = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    return content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
}
```
**Source:** .NET BCL documentation -- StreamReader constructor defaults to UTF-8 with BOM detection.

### Pattern 2: Plugin Line Validation Pipeline
**What:** A structured validation pipeline that processes each line through: trim -> skip blanks/comments -> strip prefix chars -> validate extension -> extract plugin name.
**When to use:** In `PluginValidationService.GetPluginsFromLoadOrderAsync`.
**Example:**
```csharp
// Reference: Python's plugin_validator.py lines 60-93
private static readonly HashSet<string> ValidExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".esp", ".esm", ".esl" };

private static readonly HashSet<char> PrefixChars = new() { '*', '+', '-' };

private string? ValidatePluginLine(string line, int lineNumber)
{
    if (string.IsNullOrWhiteSpace(line)) return null;

    var trimmed = line.Trim();
    if (trimmed.StartsWith('#')) return null;

    // Strip prefix characters (*, +, -)
    if (trimmed.Length > 0 && PrefixChars.Contains(trimmed[0]))
        trimmed = trimmed[1..].TrimStart();

    if (string.IsNullOrEmpty(trimmed)) return null;

    // Reject lines with path separators or control characters
    if (trimmed.IndexOfAny(new[] { '/', '\\', '\0' }) >= 0)
    {
        _logger.Warning("Line {LineNum}: Malformed entry with path separators: '{Line}'", lineNumber, line.Trim());
        return null;
    }

    // Check for valid plugin extension
    var ext = Path.GetExtension(trimmed);
    if (!ValidExtensions.Contains(ext))
    {
        _logger.Warning("Line {LineNum}: No valid plugin extension: '{Line}'", lineNumber, line.Trim());
        return null;
    }

    return trimmed;
}
```

### Pattern 3: FullPath Resolution with Data Folder Context
**What:** Resolve relative plugin filenames to absolute paths using the game's data folder. For Mutagen-supported games, Mutagen already provides full paths. For file-based games (FO3, FNV, Oblivion), the data folder must be provided externally (via config or MO2).
**When to use:** After loading plugins in `PluginValidationService` or in the orchestrator before cleaning starts.
**Example:**
```csharp
// GetPluginsFromLoadOrderAsync needs the data folder to resolve paths
public async Task<List<PluginInfo>> GetPluginsFromLoadOrderAsync(
    string loadOrderPath,
    string? dataFolderPath = null,  // NEW parameter
    CancellationToken ct = default)
{
    // ... parse lines ...

    string fullPath;
    if (!string.IsNullOrEmpty(dataFolderPath))
    {
        fullPath = Path.Combine(dataFolderPath, fileName);
    }
    else
    {
        fullPath = fileName; // Fallback: still just the filename
    }

    // Validate existence AND readability per user decision
    var validationResult = ValidatePluginFile(fullPath);

    // ...
}
```

### Pattern 4: Game Variant Detection
**What:** After detecting the base game type, scan the load order for variant-specific marker plugins.
**When to use:** In `GameDetectionService` after initial game type detection.
**Example:**
```csharp
// Reference: Python's cleaning_service.py lines 192-197, game_detection.py line 133
public GameVariant DetectVariant(GameType baseGame, IReadOnlyList<string> pluginNames)
{
    if (baseGame == GameType.FalloutNewVegas)
    {
        if (pluginNames.Any(p => p.Equals("TaleOfTwoWastelands.esm", StringComparison.OrdinalIgnoreCase)))
            return GameVariant.TTW;
    }

    if (baseGame == GameType.SkyrimSe)
    {
        if (pluginNames.Any(p => p.Equals("Enderal - Forgotten Stories.esm", StringComparison.OrdinalIgnoreCase)))
            return GameVariant.Enderal;
    }

    return GameVariant.None;
}
```

### Anti-Patterns to Avoid
- **Two code paths for FullPath:** Current `ValidatePluginExists` has `if (Path.IsPathRooted(plugin.FullPath))` branching -- this must be eliminated. FullPath should always be absolute after Phase 2, or explicitly null/empty if unresolvable.
- **Swallowing encoding errors:** Current `File.ReadAllLinesAsync` throws on bad encoding. Don't catch and return empty -- try fallback encodings first, THEN warn and skip individual bad lines.
- **Silent game type fallthrough:** Current orchestrator logs a warning for Unknown but continues cleaning without skip lists. This is the exact bug Phase 2 must fix.

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| BOM detection | Manual byte-sequence matching | `StreamReader(path, detectEncodingFromByteOrderMarks: true)` | StreamReader handles UTF-8 BOM, UTF-16 LE/BE, UTF-32 LE/BE natively |
| Latin-1 fallback | Custom charset detection | `Encoding.GetEncoding("iso-8859-1")` or `Encoding.Latin1` | .NET 9 has `Encoding.Latin1` as a static property |
| Plugin extension validation | Regex pattern matching | `Path.GetExtension()` + HashSet lookup | Built-in, handles edge cases like double extensions |
| Path.Combine for resolution | String concatenation | `Path.Combine(dataFolder, fileName)` | Handles trailing separators, relative paths correctly |

**Key insight:** .NET's `StreamReader` with default constructor already detects BOM encodings. The only thing we need to add is a Latin-1 fallback chain for non-BOM files that fail UTF-8 decoding.

## Common Pitfalls

### Pitfall 1: Moq Parameter Count Mismatch
**What goes wrong:** Adding an optional parameter to `IPluginValidationService.GetPluginsFromLoadOrderAsync` (the `dataFolderPath` parameter) breaks all existing Moq Setup/Verify calls because Moq matches by exact parameter count.
**Why it happens:** Moq does NOT treat C# optional parameters as optional in mock expressions.
**How to avoid:** After adding the parameter, immediately grep ALL test files for `GetPluginsFromLoadOrderAsync` and update every Setup and Verify call to include the new parameter matcher (e.g., `It.IsAny<string?>()`).
**Warning signs:** Tests fail with "Moq: Expected invocation on the mock at least once, but was never performed" even though the code is clearly calling the method.

### Pitfall 2: BOM Bytes Corrupting Plugin Names
**What goes wrong:** A UTF-8 BOM file (EF BB BF) read without BOM detection prepends invisible characters to the first line. The first plugin name becomes `\uFEFFSkyrim.esm` instead of `Skyrim.esm`, failing skip list matching and game detection.
**Why it happens:** `File.ReadAllLinesAsync` with default encoding (UTF-8) does strip BOM, but UTF-16 BOM files read as UTF-8 produce garbled text silently.
**How to avoid:** Use `StreamReader` with `detectEncodingFromByteOrderMarks: true` (the default) which handles all BOM variants.
**Warning signs:** First plugin in load order is never recognized by skip list or game detection.

### Pitfall 3: MO2 Separator Lines Parsed as Plugins
**What goes wrong:** MO2's `modlist.txt` (not `plugins.txt`) contains lines like `*Separator Name` or `-Separator Name`. Current code strips `*` prefix and treats the remainder as a plugin name.
**Why it happens:** Current code only checks for `#` comments. MO2 separators don't use `#`.
**How to avoid:** After stripping prefix, validate that the resulting name has a valid plugin extension (.esp/.esm/.esl). Lines without valid extensions are rejected as malformed. This handles separators naturally.
**Warning signs:** Cleaning attempts on non-existent "plugin" files named after MO2 separators.

### Pitfall 4: Enderal Plugin Marker Name Uncertainty
**What goes wrong:** Using the wrong marker plugin name for Enderal detection causes the variant to never be detected.
**Why it happens:** Enderal's exact ESM filename may vary between versions (e.g., `Enderal - Forgotten Stories.esm` vs `Enderal.esm`).
**How to avoid:** Research the exact plugin name(s) used by Enderal SE. The most common is `Enderal - Forgotten Stories.esm` for the Special Edition version.
**Warning signs:** Enderal users never get variant-specific skip list behavior.

### Pitfall 5: TTW Skip List Double-Merging
**What goes wrong:** If the user manually adds FO3 DLC plugins to their FNV skip list AND the code auto-merges FO3 entries, duplicates appear.
**Why it happens:** Auto-merge appends FO3 entries to FNV entries without deduplication.
**How to avoid:** The existing `GetSkipListAsync` already calls `.Distinct(StringComparer.OrdinalIgnoreCase)` at the end. Ensure the TTW merge happens before this deduplication step.
**Warning signs:** Skip list contains duplicate entries (functionally harmless but messy).

### Pitfall 6: File Lock Validation Opens Files During Cleaning
**What goes wrong:** The "try opening file briefly to verify not locked" validation check could fail during a cleaning session if xEdit has the file open.
**Why it happens:** Validation runs before cleaning, but if it runs mid-session for the next plugin while xEdit still holds the previous one, false positives occur.
**How to avoid:** Validation should check existence and readability BEFORE the cleaning loop starts (during initial plugin loading), not per-plugin during cleaning.
**Warning signs:** Valid plugins reported as "locked/unreadable" intermittently during cleaning sessions.

## Code Examples

Verified patterns from existing codebase and .NET BCL:

### Encoding Fallback Chain
```csharp
// Source: .NET BCL StreamReader + Encoding docs
private static async Task<string[]> ReadLinesWithEncodingFallbackAsync(
    string path, ILoggingService logger, CancellationToken ct)
{
    // Attempt 1: StreamReader with BOM detection (default constructor)
    // This handles: UTF-8 (with or without BOM), UTF-16 LE/BE (with BOM), UTF-32 (with BOM)
    try
    {
        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        return content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
    }
    catch (DecoderFallbackException)
    {
        // Attempt 2: Latin-1 (ISO-8859-1) -- cannot fail because every byte is valid
        logger.Debug("UTF-8/BOM detection failed for {Path}, falling back to Latin-1", path);
        using var reader = new StreamReader(path, Encoding.Latin1);
        var content = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        return content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
    }
}
```

**Note:** `Encoding.Latin1` is available as a static property in .NET 5+. ISO-8859-1 cannot fail because every single byte value (0x00-0xFF) maps to a valid character. This makes it the perfect last-resort fallback.

### Aggregated Error Reporting
```csharp
// Source: user decision "aggregated summary rather than one-per-plugin"
public class PluginPipelineResult
{
    public List<PluginInfo> ValidPlugins { get; init; } = new();
    public List<(string PluginName, string Reason)> Warnings { get; init; } = new();
    public bool HasErrors => Warnings.Count > 0;

    public string FormatWarnings()
    {
        if (Warnings.Count == 0) return string.Empty;

        var notFound = Warnings.Where(w => w.Reason.Contains("not found")).ToList();
        var malformed = Warnings.Where(w => !w.Reason.Contains("not found")).ToList();

        var sb = new StringBuilder();
        if (notFound.Count > 0)
            sb.AppendLine($"{notFound.Count} plugins not found: [{string.Join(", ", notFound.Select(w => w.PluginName))}]");
        if (malformed.Count > 0)
            sb.AppendLine($"{malformed.Count} malformed entries skipped");

        return sb.ToString();
    }
}
```

### GameType.Unknown Rejection in XEditCommandBuilder
```csharp
// Source: user decision "block cleaning completely for GameType.Unknown"
// Current code in XEditCommandBuilder.GetGameFlag returns string.Empty for Unknown
// New behavior: BuildCommand should return null (or throw) for Unknown
public ProcessStartInfo? BuildCommand(PluginInfo plugin, GameType gameType)
{
    if (gameType == GameType.Unknown)
    {
        // Safety: refuse to build command without game type
        return null;
    }
    // ... rest of existing logic
}
```

### TTW Skip List Merging in ConfigurationService.GetSkipListAsync
```csharp
// Source: Python cleaning_service.py lines 192-197
// Current GetSkipListAsync already supports per-game keys.
// TTW merge inserts FO3 entries when variant is detected.
public async Task<List<string>> GetSkipListAsync(GameType gameType, GameVariant variant = GameVariant.None)
{
    var result = new List<string>();
    var key = GetGameKey(gameType);

    // ... existing logic to gather user + main config skip lists ...

    // TTW: auto-merge FO3 skip list entries into FNV list
    if (variant == GameVariant.TTW && gameType == GameType.FalloutNewVegas)
    {
        var fo3Key = GetGameKey(GameType.Fallout3);
        if (_mainConfigCache.Data.SkipLists.TryGetValue(fo3Key, out var fo3List))
            result.AddRange(fo3List);
        // Also include user's FO3 skip list
        var userConfig = await LoadUserConfigAsync().ConfigureAwait(false);
        if (userConfig.SkipLists.TryGetValue(fo3Key, out var userFo3List))
            result.AddRange(userFo3List);
    }

    return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
```

## State of the Art

| Old Approach (Current) | New Approach (Phase 2) | Impact |
|------------------------|------------------------|--------|
| `FullPath = FileName` placeholder | FullPath = resolved absolute path or validation warning | Phase 5 backup feature unblocked |
| `File.ReadAllLinesAsync` (UTF-8 only) | StreamReader with BOM detection + Latin-1 fallback | Non-English load orders work correctly |
| No line validation beyond `#` comments | Extension check + control char rejection + prefix stripping | MO2 separators, malformed entries handled |
| GameType.Unknown proceeds to cleaning | GameType.Unknown blocks cleaning with error | Skip list safety enforced |
| No TTW skip list merging | FO3 entries auto-merged for TTW variant | TTW users protected from cleaning FO3 DLC plugins |
| No Enderal detection | Enderal variant detected, separate skip list | Enderal users get proper skip list |
| Path validation returns true for non-rooted | All paths must be absolute or trigger warning | Eliminates the two-code-path problem |

## Discretion Recommendations

### Path Caching Strategy
**Recommendation:** Re-resolve per run (no session cache).
**Rationale:** Plugin files can be added/removed between cleaning runs. Caching would serve stale paths. The file existence check is a single `File.Exists()` call per plugin -- even with 500 plugins, this is sub-millisecond on modern SSDs. The readability check (brief file open) adds minimal overhead. Caching complexity is not justified.

### BOM Handling Implementation
**Recommendation:** Use `StreamReader` with default constructor (BOM auto-detection enabled), with `Encoding.Latin1` as fallback.
**Rationale:** .NET's `StreamReader` constructor with `detectEncodingFromByteOrderMarks: true` (the default) handles all common BOM variants. For non-BOM files, it uses the provided encoding (defaulting to UTF-8). If UTF-8 decoding fails (which is rare since `StreamReader` uses a replacement fallback by default, not an exception), fall back to Latin-1. **Important:** To actually catch bad UTF-8, we need to explicitly use `Encoding.UTF8` with `DecoderExceptionFallback`, then catch `DecoderFallbackException`. Otherwise `StreamReader` silently replaces bad bytes with `?`.

### Internal Error Categories
**Recommendation:** Use a simple enum + result type, not exception-based error codes.
```csharp
public enum PluginWarningKind { NotFound, Unreadable, ZeroByte, MalformedEntry, InvalidExtension }
```
**Rationale:** Error categories enable the aggregated summary format the user requested. An enum is simpler and more testable than exception hierarchies. The `PluginPipelineResult` type shown in Code Examples carries both valid plugins and categorized warnings.

### Validation Step Order
**Recommendation:**
1. Read file with encoding detection
2. Parse lines (strip blanks, comments, prefixes)
3. Validate each line (extension check, control chars, malformed)
4. Resolve paths (combine with data folder)
5. Validate files (existence, readability)
6. Aggregate warnings
7. Return valid plugins + warning summary

**Rationale:** Parsing before path resolution allows rejecting obviously bad entries early. Path resolution needs the data folder context which may require game detection first. File validation is last because it's the most expensive step (I/O).

## Open Questions

Things that couldn't be fully resolved:

1. **Enderal marker plugin exact name**
   - What we know: Enderal SE uses a specific master plugin. Common candidates are `Enderal - Forgotten Stories.esm` and `Enderal.esm`.
   - What's unclear: The exact filename in the official Enderal SE release.
   - Recommendation: Use `Enderal - Forgotten Stories.esm` as the primary marker (this is the most commonly referenced name in modding communities). Add both names to the check to be safe. Verify with actual Enderal installation if available.

2. **MO2 virtual filesystem path resolution**
   - What we know: The user decision says "resolve plugin paths through MO2's virtual filesystem (query mod list/profile to find real mod folder)". MO2 creates virtual links at runtime.
   - What's unclear: MO2 does not have a simple API for querying resolved paths from outside. The Python implementation wraps xEdit execution through MO2 (`ModOrganizer.exe run`) which handles VFS transparently. xEdit itself resolves paths through MO2's VFS when launched through MO2.
   - Recommendation: When MO2 mode is enabled, the actual path resolution happens at xEdit runtime (MO2 wraps the process). The C# app does NOT need to resolve MO2 virtual paths manually -- it needs to pass plugin filenames to xEdit (via MO2), and xEdit handles the rest. For the "validate existence" check, we should skip file-existence validation when MO2 mode is active, or validate against MO2's mods directory if known. The simplest correct approach: when MO2 mode is on, skip the "file exists on disk" check for individual plugins, because the real file locations are only known to MO2's VFS. Log a note explaining why validation is skipped.

3. **`System.Text.Encoding.CodePages` NuGet dependency**
   - What we know: `Encoding.Latin1` is available as a static property in .NET 5+ (including .NET 9).
   - What's unclear: Whether the target framework `net10.0-windows10.0.19041.0` (noted in csproj, though PROJECT.md says .NET 9) includes Latin-1 without the CodePages NuGet.
   - Recommendation: Test `Encoding.Latin1` availability first. If it works without additional NuGet packages (it should on .NET 9+), no dependency needed.

## Sources

### Primary (HIGH confidence)
- Direct codebase analysis of all files in `AutoQAC/Services/Plugin/`, `AutoQAC/Services/GameDetection/`, `AutoQAC/Services/Cleaning/`, `AutoQAC/Services/Configuration/`
- Python reference implementation at `Code_To_Port/AutoQACLib/plugin_validator.py`, `Code_To_Port/AutoQACLib/game_detection.py`, `Code_To_Port/AutoQACLib/cleaning_service.py`
- Existing test suite at `AutoQAC.Tests/Services/`
- `AutoQAC Data/AutoQAC Main.yaml` skip list structure analysis
- .NET BCL `StreamReader` documentation via Context7 (`/dotnet/docs`)

### Secondary (MEDIUM confidence)
- Enderal marker plugin name -- based on modding community conventions; verify with actual game

### Tertiary (LOW confidence)
- None -- all findings are from primary codebase analysis

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- no new dependencies, all existing .NET BCL
- Architecture: HIGH -- surgical changes to existing well-structured services, no architectural shifts
- Pitfalls: HIGH -- all identified from direct code analysis and known Moq behavior (documented in MEMORY.md)
- Code examples: HIGH -- all adapted from existing codebase patterns and .NET BCL

**Research date:** 2026-02-06
**Valid until:** Indefinite (findings are from direct codebase analysis, not versioned library documentation)
