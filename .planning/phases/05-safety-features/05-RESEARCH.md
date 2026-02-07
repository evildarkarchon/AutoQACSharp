# Phase 5: Safety Features - Research

**Researched:** 2026-02-06
**Domain:** File backup/restore, dry-run preview, Avalonia UI
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Dry-run presentation:**
- Reuse the existing progress window with a "Preview" banner -- no separate window
- Each plugin shows status + reason: "Will clean" or "Will skip: [reason]" (skip list, file not found, master file, etc.)
- Triggered via a separate "Preview" / "Dry Run" button on the main window alongside the existing Clean button
- Inline disclaimer at the top of results: "Preview only -- does not detect ITMs/UDRs (requires xEdit)"
- No xEdit invocation during dry-run -- purely local validation logic

**Backup organization:**
- Session-based folders: each cleaning session gets a timestamped directory (e.g., `Backups/2026-02-07_14-30/`)
- Backup root lives next to the game's Data directory (sibling folder), not inside AutoQAC Data/
- Each plugin is backed up individually, right before xEdit opens it for cleaning (not all at once at session start)
- If backup copy fails (disk full, permissions), show a dialog asking the user whether to skip that plugin, abort the session, or continue without backup

**Restore experience:**
- Accessed via a "Restore Backups" button/menu on the main window (not buried in Settings)
- Two-level browser: first shows list of backup sessions (date + plugin count), then drill into a session to see individual plugins
- Restoring overwrites the current file in place with the backed-up version
- Both "Restore All" (whole session) and individual plugin restore supported
- Confirmation dialog required for "Restore All" session-wide restore; individual plugin restores proceed without confirmation

**Settings integration:**
- Backup enabled by default for new users
- Retention configured by session count (keep last N sessions, delete older ones)
- Fixed backup location (next to game Data/) -- no user-configurable path
- No disk usage display -- just the enable/disable toggle and retention count

### Claude's Discretion
- Default retention count (e.g., 5 or 10 sessions)
- Exact backup folder naming convention details
- Session metadata format (how to record which plugins were in a session)
- Progress window adaptations for preview mode vs cleaning mode
- Error handling for restore failures

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope
</user_constraints>

## Summary

This phase adds two safety features: a dry-run preview mode (SAFE-01) and plugin backup with restore capability (SAFE-02, SAFE-03). The dry-run reuses the existing validation pipeline to show which plugins would be cleaned or skipped without invoking xEdit. The backup service copies each plugin file before xEdit processes it, storing backups in timestamped session directories alongside the game's Data folder.

The implementation is straightforward because all the prerequisite infrastructure exists. Phase 2 completed FullPath resolution, Phase 1 ensured process handles are released, and the existing `CleaningOrchestrator` has a clear per-plugin loop where backup can be inserted. The dry-run shares logic with the existing `ValidatePreClean` method and the orchestrator's skip-list filtering.

The main technical challenge is the backup failure dialog (3-choice: skip/abort/continue-without-backup), which requires a callback from the orchestrator to the ViewModel, similar to the existing `TimeoutRetryCallback` pattern. The restore UI is a new modal window with a two-level master-detail layout, which Avalonia supports well via ListBox selection binding.

**Primary recommendation:** Use `File.Copy` for backup (synchronous, fast for small plugin files <100MB), store session metadata as a JSON sidecar file, and implement the backup failure callback using the same delegate pattern as `TimeoutRetryCallback`.

## Standard Stack

No new libraries are needed. This phase uses only existing project dependencies.

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.IO | .NET 10 built-in | File.Copy, Directory.CreateDirectory for backup/restore | Built-in, no dependencies |
| System.Text.Json | .NET 10 built-in | Session metadata serialization (JSON sidecar) | Lighter than YamlDotNet for simple structured data |
| ReactiveUI | existing | Reactive MVVM for dry-run ViewModel and restore window | Already used throughout project |
| Avalonia UI | 11.x | Restore browser window, preview mode UI | Already used throughout project |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| YamlDotNet | existing | Backup settings in UserConfiguration | Already used for all config |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| System.Text.Json for metadata | YamlDotNet | JSON is simpler for machine-only data; YAML better for human-edited config. Use JSON for session metadata since users never edit it directly |
| File.Copy | FileStream.CopyToAsync | File.Copy is simpler and just as fast for plugin files (typically <100MB). Async copy adds complexity without benefit since we copy one file at a time right before xEdit launches |

**Installation:**
```bash
# No new packages needed
```

## Architecture Patterns

### Recommended Project Structure
```
AutoQAC/
├── Models/
│   ├── BackupSession.cs           # Session metadata model
│   ├── BackupPluginEntry.cs       # Per-plugin backup entry
│   ├── DryRunResult.cs            # Dry-run preview result for a plugin
│   └── Configuration/
│       └── BackupSettings.cs      # Backup enable/disable, retention count
├── Services/
│   └── Backup/
│       ├── IBackupService.cs      # Interface for backup/restore operations
│       └── BackupService.cs       # File copy, metadata, restore, retention
├── ViewModels/
│   ├── RestoreViewModel.cs        # Two-level backup browser
│   └── ProgressViewModel.cs       # Extended with preview mode support
└── Views/
    ├── RestoreWindow.axaml        # Backup restore browser
    └── ProgressWindow.axaml       # Extended with preview banner
```

### Pattern 1: Delegate Callback for Backup Failures
**What:** The orchestrator calls a delegate when backup fails, letting the ViewModel show a dialog and return the user's choice (skip/abort/continue).
**When to use:** When the service layer needs a UI decision mid-operation.
**Example:**
```csharp
// Follows the existing TimeoutRetryCallback pattern
public enum BackupFailureChoice
{
    SkipPlugin,
    AbortSession,
    ContinueWithoutBackup
}

public delegate Task<BackupFailureChoice> BackupFailureCallback(
    string pluginName, string errorMessage);

// In CleaningOrchestrator.StartCleaningAsync:
if (backupEnabled)
{
    var backupResult = await _backupService.BackupPluginAsync(plugin, sessionDir, ct);
    if (!backupResult.Success)
    {
        if (onBackupFailure != null)
        {
            var choice = await onBackupFailure(plugin.FileName, backupResult.Error!);
            switch (choice)
            {
                case BackupFailureChoice.SkipPlugin:
                    continue; // Skip to next plugin
                case BackupFailureChoice.AbortSession:
                    return; // End the session
                case BackupFailureChoice.ContinueWithoutBackup:
                    break; // Proceed without backup for this plugin
            }
        }
    }
}
```

### Pattern 2: Dry-Run as Orchestrator Method
**What:** A separate `RunDryRunAsync` method on `ICleaningOrchestrator` that runs the same validation/filtering pipeline as `StartCleaningAsync` but stops before launching xEdit.
**When to use:** The dry-run shares validation logic with the real cleaning flow.
**Example:**
```csharp
// In ICleaningOrchestrator:
Task<List<DryRunResult>> RunDryRunAsync(CancellationToken ct = default);

// In CleaningOrchestrator:
public async Task<List<DryRunResult>> RunDryRunAsync(CancellationToken ct = default)
{
    var results = new List<DryRunResult>();

    // Same validation as StartCleaningAsync steps 1-4b
    // But instead of cleaning, produce DryRunResult for each plugin

    foreach (var plugin in allPlugins)
    {
        if (plugin.IsInSkipList)
        {
            results.Add(new DryRunResult(plugin.FileName, DryRunStatus.WillSkip, "In skip list"));
            continue;
        }

        var warning = _pluginService.ValidatePluginFile(plugin);
        if (warning != PluginWarningKind.None)
        {
            results.Add(new DryRunResult(plugin.FileName, DryRunStatus.WillSkip, $"File issue: {warning}"));
            continue;
        }

        if (!plugin.IsSelected)
        {
            results.Add(new DryRunResult(plugin.FileName, DryRunStatus.WillSkip, "Not selected"));
            continue;
        }

        results.Add(new DryRunResult(plugin.FileName, DryRunStatus.WillClean, "Ready for cleaning"));
    }

    return results;
}
```

### Pattern 3: Session Metadata as JSON Sidecar
**What:** Each backup session directory contains a `session.json` file recording session metadata.
**When to use:** For the restore browser to enumerate sessions without scanning file contents.
**Example:**
```csharp
// session.json lives inside each backup session directory
public sealed record BackupSession
{
    public DateTime Timestamp { get; init; }
    public string GameType { get; init; } = string.Empty;
    public List<BackupPluginEntry> Plugins { get; init; } = new();
}

public sealed record BackupPluginEntry
{
    public string FileName { get; init; } = string.Empty;
    public string OriginalPath { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
}

// Directory structure:
// [GameInstallDir]/AutoQAC Backups/
//   2026-02-07_14-30-45/
//     session.json
//     MyMod.esp
//     AnotherMod.esp
//   2026-02-08_09-15-22/
//     session.json
//     SomeMod.esm
```

### Pattern 4: Progress Window Preview Mode
**What:** Reuse the existing ProgressWindow with a mode flag that switches the UI between cleaning mode and preview mode.
**When to use:** Dry-run results display.
**Example:**
```csharp
// ProgressViewModel gains a preview mode
private bool _isPreviewMode;
public bool IsPreviewMode
{
    get => _isPreviewMode;
    set => this.RaiseAndSetIfChanged(ref _isPreviewMode, value);
}

// In AXAML, the "Currently cleaning:" header becomes conditional:
// IsPreviewMode ? "Dry-Run Preview" : "Currently cleaning:"
// The counter badges (ITM/UDR/Nav) are hidden in preview mode
// The disclaimer is shown only in preview mode
```

### Anti-Patterns to Avoid
- **Backing up all plugins at session start:** User decision explicitly requires per-plugin backup right before xEdit processes each one. If session is interrupted, only relevant plugins have backups.
- **Putting backups in AutoQAC Data/:** User decision says backup root lives next to the game's Data directory as a sibling folder.
- **Creating a separate dry-run window:** User decision requires reusing the existing progress window with a "Preview" banner.
- **Async file copy for small files:** Plugin files are typically <100MB. `File.Copy` is simpler and equally fast. `FileStream.CopyToAsync` adds unnecessary complexity.
- **Storing metadata in YAML:** Session metadata is machine-only data that users never edit. JSON (via System.Text.Json) is simpler and faster for this use case.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| File copying | Custom stream-based copier | `File.Copy(source, dest, overwrite: false)` | Built-in, handles ACLs, attributes, timestamps correctly |
| JSON serialization | Manual string building | `System.Text.Json.JsonSerializer` | Type-safe, handles escaping, built-in |
| Directory enumeration | Recursive manual walk | `Directory.GetDirectories` + `Directory.GetFiles` | Built-in, efficient |
| Timestamp formatting | Custom format strings | `DateTime.ToString("yyyy-MM-dd_HH-mm-ss")` | Consistent, sortable, filesystem-safe |

**Key insight:** This phase is entirely standard .NET file I/O. No third-party libraries are needed. The complexity is in the workflow orchestration (backup timing, failure callbacks, dry-run pipeline), not in the file operations themselves.

## Common Pitfalls

### Pitfall 1: File.Copy Fails When Destination Exists
**What goes wrong:** `File.Copy(src, dst)` throws `IOException` if dst already exists.
**Why it happens:** Default behavior does not overwrite.
**How to avoid:** For backup, use `File.Copy(src, dst, overwrite: false)` and ensure unique directory names (timestamped). For restore, use `File.Copy(src, dst, overwrite: true)`.
**Warning signs:** IOException during backup with "file already exists" message.

### Pitfall 2: Backup Directory Next to Data Folder Requires Write Permissions
**What goes wrong:** The game's Data folder may be in Program Files or a read-only location.
**Why it happens:** User decision places backups next to Data directory, which may be under UAC protection.
**How to avoid:** Catch `UnauthorizedAccessException` during `Directory.CreateDirectory` and surface a clear error message. The backup failure dialog already handles this (user can abort or continue without backup).
**Warning signs:** UnauthorizedAccessException on first backup attempt.

### Pitfall 3: MO2 Virtual Filesystem Complicates Backup Paths
**What goes wrong:** In MO2 mode, plugins are scattered across mod directories. The FullPath on PluginInfo may not point to the real file location.
**Why it happens:** MO2 uses a virtual filesystem that maps multiple mod directories into a single virtual Data directory.
**How to avoid:** In MO2 mode, PluginInfo.FullPath may only contain the filename (no rooted path). The orchestrator already skips file-existence validation in MO2 mode (line 186-218 of CleaningOrchestrator.cs). For backup in MO2 mode, skip backup silently and log a warning -- MO2 has its own file management. Alternatively, require the user to locate the real file via MO2's mod list, which is impractical.
**Warning signs:** PluginInfo.FullPath is not rooted when MO2 mode is active.

### Pitfall 4: Dry-Run Does Not Show ITM/UDR Counts
**What goes wrong:** Users may expect dry-run to tell them how many records will be cleaned.
**Why it happens:** ITM/UDR detection requires running xEdit. Dry-run explicitly does NOT invoke xEdit.
**How to avoid:** The disclaimer banner must be prominent: "Preview only -- does not detect ITMs/UDRs (requires xEdit)". This is a locked user decision.
**Warning signs:** User confusion about why counts show zero.

### Pitfall 5: Session Retention Deletes Backup While Cleaning
**What goes wrong:** If retention cleanup runs during a cleaning session, it might delete the current session's backup directory.
**Why it happens:** Race between retention cleanup and active backup creation.
**How to avoid:** Run retention cleanup only at session END (after all plugins are processed), not at session start. Never delete the current session directory. Sort directories by timestamp, keep the newest N.
**Warning signs:** Missing backup files for plugins cleaned late in a large session.

### Pitfall 6: Restore Overwrites a File Modified Since Backup
**What goes wrong:** User restores a backup, overwriting a file that was manually modified after cleaning.
**Why it happens:** No check for whether the current file differs from what was expected.
**How to avoid:** For individual plugin restores, proceed without confirmation (per user decision). For "Restore All", require confirmation. Both are user-decided behaviors. Optionally log the current file's timestamp vs the backup timestamp.
**Warning signs:** User restores old backup and loses manual edits.

### Pitfall 7: Backup Location Depends on Game Detection
**What goes wrong:** Backup root is "next to game Data directory" but the Data directory may not be known yet (GameType.Unknown).
**Why it happens:** Backup location requires knowing the game's Data directory path.
**How to avoid:** Backup is only possible when GameType is known and Data folder is resolved. The orchestrator already rejects cleaning with GameType.Unknown (Phase 2 decision). Guard backup initialization behind the same check.
**Warning signs:** NullReferenceException when constructing backup path with null Data folder.

## Code Examples

### File Copy for Backup
```csharp
// Source: .NET built-in File.Copy API
public async Task<BackupResult> BackupPluginAsync(
    PluginInfo plugin, string sessionDir, CancellationToken ct)
{
    ct.ThrowIfCancellationRequested();

    var sourcePath = plugin.FullPath;
    if (string.IsNullOrEmpty(sourcePath) || !Path.IsPathRooted(sourcePath))
    {
        return BackupResult.Failure($"Cannot backup: path not resolved for {plugin.FileName}");
    }

    if (!File.Exists(sourcePath))
    {
        return BackupResult.Failure($"Cannot backup: file not found at {sourcePath}");
    }

    try
    {
        Directory.CreateDirectory(sessionDir);
        var destPath = Path.Combine(sessionDir, plugin.FileName);

        // File.Copy is synchronous but fast for plugin files (<100MB)
        // Using overwrite: false to catch unexpected duplicates
        File.Copy(sourcePath, destPath, overwrite: false);

        var info = new FileInfo(destPath);
        return BackupResult.Ok(info.Length);
    }
    catch (UnauthorizedAccessException ex)
    {
        return BackupResult.Failure($"Permission denied: {ex.Message}");
    }
    catch (IOException ex)
    {
        return BackupResult.Failure($"I/O error: {ex.Message}");
    }
}
```

### Restore Plugin from Backup
```csharp
// Source: .NET built-in File.Copy API
public void RestorePlugin(BackupPluginEntry entry, string sessionDir)
{
    var backupPath = Path.Combine(sessionDir, entry.FileName);

    if (!File.Exists(backupPath))
        throw new FileNotFoundException($"Backup file missing: {backupPath}");

    // Overwrite the current file with the backed-up version
    File.Copy(backupPath, entry.OriginalPath, overwrite: true);
}
```

### Session Metadata Write
```csharp
// Source: System.Text.Json built-in
public async Task WriteSessionMetadataAsync(
    string sessionDir, BackupSession session, CancellationToken ct)
{
    var jsonPath = Path.Combine(sessionDir, "session.json");
    var options = new JsonSerializerOptions { WriteIndented = true };

    await using var stream = File.Create(jsonPath);
    await JsonSerializer.SerializeAsync(stream, session, options, ct);
}
```

### Session Enumeration for Restore Browser
```csharp
// Source: System.IO built-in
public async Task<List<BackupSession>> GetBackupSessionsAsync(
    string backupRoot, CancellationToken ct)
{
    var sessions = new List<BackupSession>();

    if (!Directory.Exists(backupRoot))
        return sessions;

    foreach (var dir in Directory.GetDirectories(backupRoot)
        .OrderByDescending(d => d)) // Newest first (sorted by name = by timestamp)
    {
        ct.ThrowIfCancellationRequested();
        var metadataPath = Path.Combine(dir, "session.json");

        if (!File.Exists(metadataPath))
            continue; // Skip directories without metadata

        try
        {
            await using var stream = File.OpenRead(metadataPath);
            var session = await JsonSerializer.DeserializeAsync<BackupSession>(stream, cancellationToken: ct);
            if (session != null)
            {
                sessions.Add(session with { /* add directory path if needed */ });
            }
        }
        catch (JsonException)
        {
            // Skip corrupt metadata files
        }
    }

    return sessions;
}
```

### Backup Retention Cleanup
```csharp
// Source: pattern from existing LogRetentionService
public void CleanupOldSessions(string backupRoot, int maxSessionCount)
{
    if (!Directory.Exists(backupRoot))
        return;

    var sessionDirs = Directory.GetDirectories(backupRoot)
        .OrderByDescending(d => d) // Newest first
        .ToList();

    // Delete oldest sessions beyond the retention count
    foreach (var dir in sessionDirs.Skip(maxSessionCount))
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception)
        {
            // Log warning, continue with next
        }
    }
}
```

### DryRunResult Model
```csharp
public enum DryRunStatus
{
    WillClean,
    WillSkip
}

public sealed record DryRunResult(
    string PluginName,
    DryRunStatus Status,
    string Reason);
```

### Backup Settings in UserConfiguration
```csharp
// Add to UserConfiguration
[YamlMember(Alias = "Backup")]
public BackupSettings Backup { get; set; } = new();

public sealed class BackupSettings
{
    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; } = true; // Enabled by default

    [YamlMember(Alias = "max_sessions")]
    public int MaxSessions { get; set; } = 10; // Keep last 10 sessions
}
```

### Backup Root Path Resolution
```csharp
// Backup root is a sibling of the game's Data directory
public string GetBackupRoot(string dataFolderPath)
{
    // dataFolderPath: e.g., "C:\Games\Skyrim\Data"
    // Parent: "C:\Games\Skyrim"
    // Result: "C:\Games\Skyrim\AutoQAC Backups"
    var gameDir = Path.GetDirectoryName(dataFolderPath)
        ?? throw new InvalidOperationException("Cannot determine game directory from Data path");

    return Path.Combine(gameDir, "AutoQAC Backups");
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| File.Copy (sync) | File.Copy (sync) still preferred for small files | Always | No change for this use case -- plugin files are small enough that async overhead is unnecessary |
| Manual JSON building | System.Text.Json source generation | .NET 8+ | Source generators available but not needed for simple models |

**Deprecated/outdated:**
- Nothing relevant is deprecated. File.Copy, Directory operations are stable .NET APIs.

## Open Questions

1. **MO2 mode backup behavior**
   - What we know: In MO2 mode, plugins live in scattered mod directories. PluginInfo.FullPath may not be rooted. The orchestrator skips file-existence validation in MO2 mode.
   - What's unclear: Whether to attempt backup at all in MO2 mode, or skip silently.
   - Recommendation: Skip backup in MO2 mode with a log warning. MO2 users manage mods through MO2's own file management. Attempting to backup from unknown paths is unreliable. The backup toggle in settings should show a note: "Backup is unavailable in MO2 mode."

2. **Default retention count**
   - What we know: User left this to Claude's discretion.
   - Recommendation: Default to 10 sessions. This provides a generous safety net without consuming excessive disk space. A typical plugin is 1-50MB; with 200 plugins per session at 10MB average, one session is ~2GB. 10 sessions = ~20GB worst case, which is reasonable for modern drives.

3. **Backup folder naming convention**
   - What we know: User specified "timestamped directory (e.g., `Backups/2026-02-07_14-30/`)."
   - Recommendation: Use `yyyy-MM-dd_HH-mm-ss` format for uniqueness and filesystem sorting. Example: `2026-02-07_14-30-45`. This avoids collisions if a user starts two sessions within the same minute.

4. **What happens when FullPath is resolved but Data folder is different**
   - What we know: Backup root is derived from the Data folder. Plugin FullPath is derived from Data folder too. They should always be consistent.
   - What's unclear: Edge case where user changes Data folder between cleaning sessions -- old backups would have OriginalPath pointing to the old location.
   - Recommendation: Store OriginalPath in session metadata. On restore, use the stored OriginalPath, not the current Data folder. Warn if the file does not exist at OriginalPath.

## Sources

### Primary (HIGH confidence)
- Direct codebase analysis: CleaningOrchestrator.cs, PluginValidationService.cs, MainWindowViewModel.cs, ProgressViewModel.cs, SettingsViewModel.cs, UserConfiguration.cs, ServiceCollectionExtensions.cs
- .NET File.Copy API: [Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.copy)
- .NET System.Text.Json: [Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json)

### Secondary (MEDIUM confidence)
- Avalonia TreeView/ListBox: [Avalonia Docs](https://docs.avaloniaui.net/docs/reference/controls/treeview-1)
- Avalonia Window.ShowDialog: [Avalonia Docs](https://docs.avaloniaui.net/docs/reference/controls/window)
- C# file copy performance analysis: [WebSearch verified](https://www.hiveworkshop.com/threads/c-filestream-copytoasync-vs-file-copy.248211/)

### Tertiary (LOW confidence)
- None

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - No new libraries needed, all standard .NET APIs
- Architecture: HIGH - Patterns directly follow existing codebase conventions (TimeoutRetryCallback, LogRetentionService, ProgressViewModel mode)
- Pitfalls: HIGH - Identified from direct codebase analysis (MO2 mode skip, permission errors, retention timing)
- Dry-run: HIGH - Reuses existing validation pipeline code already in CleaningOrchestrator
- Backup/restore: HIGH - Standard File.Copy/Directory operations with clear patterns
- UI patterns: HIGH - Follows established Avalonia MVVM patterns already used in project

**Research date:** 2026-02-06
**Valid until:** 2026-03-06 (30 days -- stable .NET APIs, no fast-moving dependencies)
