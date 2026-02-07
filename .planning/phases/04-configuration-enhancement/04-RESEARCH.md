# Phase 4: Configuration Enhancement - Research

**Researched:** 2026-02-07
**Domain:** Avalonia UI validation, YAML config management, file change detection, log retention
**Confidence:** HIGH

## Summary

This phase enhances the existing configuration infrastructure across four areas: (1) real-time path validation in Settings with visual feedback, (2) legacy Python config migration with proper error handling, (3) YAML cache invalidation via content hashing, and (4) log/journal retention cleanup. The project already has a solid ConfigurationService with debounced saves, reactive patterns, and YamlDotNet serialization -- this phase builds on that foundation.

The primary technical challenge is the path validation UI, which requires extending the SettingsViewModel with path properties, debounced file-existence checks, and Avalonia custom styling for red/green border indicators. The config edit detection via content hashing is straightforward with System.Security.Cryptography.SHA256. Legacy migration is already partially implemented (the `MigrateLegacyConfigAsync` method exists but has bugs per CONF-06). Log retention is a new startup service that enumerates and deletes old files.

**Primary recommendation:** Use Avalonia's built-in DataValidationErrors with custom styling for path validation indicators. Use SHA256 content hashing with a polling timer (5-second interval) for config change detection. Use FileSystemWatcher as the primary watcher with polling as a fallback only if FSW proves unreliable.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

#### Path validation feedback
- Red border + icon indicator style: text fields get red border with X icon when invalid, green border with checkmark when valid
- Validation triggers on debounced input (300-500ms after user pauses typing)
- Browse button selections validate immediately (no debounce -- deliberate user action)
- All path fields in Settings are validated: xEdit path, MO2 path, load order path, data directory

#### Legacy config migration
- Auto-detect legacy Python config files on every app startup
- Migration only runs when no C# config exists yet -- one-time bootstrap, not a merge
- After successful migration: copy old files to a backup folder, then delete originals
- Migration failures show a non-modal warning banner at the top of the main window, persists until dismissed, with details on what failed

#### Log/journal retention
- Users choose between age-based (days) OR count-based (keep last N) retention model -- both options available in Settings
- Retention covers both app logs (Serilog output) and cleaning session journals
- Cleanup runs on app startup
- Ships with sensible defaults active out of the box (e.g., 30 days / 100 files) -- user can adjust in Settings

#### Config edit detection
- Silent reload when YAML config files change on disk -- no user prompt, new values take effect automatically
- Invalid external edits rejected: keep previous known-good config, log a warning
- Config changes during an active cleaning session are deferred until the session ends
- Change detection uses content hashing (not file timestamps) for reliability

### Claude's Discretion
- Exact hash algorithm for config change detection
- Default retention values (suggested ~30 days / ~100 files but Claude can tune)
- Cache invalidation polling interval or FileSystemWatcher approach
- Migration backup folder location and naming convention
- Warning banner styling and dismiss behavior

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope
</user_constraints>

## Standard Stack

The established libraries/tools for this domain:

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Avalonia | 11.3.11 | UI framework with DataValidationErrors | Already in project, has built-in validation template system |
| ReactiveUI | 11.3.8 | MVVM with WhenAnyValue + Throttle for debounced validation | Already in project, provides Rx operators for debounce |
| YamlDotNet | 16.3.0 | YAML config serialization | Already in project for all config I/O |
| System.Security.Cryptography | (built-in) | SHA256 content hashing | Built into .NET, no external dependency |
| System.IO.FileSystemWatcher | (built-in) | File change notification | Built into .NET, primary approach for change detection |
| Serilog | 4.3.0 | Structured logging with file sink | Already in project with rolling file configuration |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.Reactive | (via ReactiveUI) | Rx operators (Throttle, DistinctUntilChanged, Where) | Debounced validation, config reload pipeline |
| Microsoft.Extensions.DI | 10.0.2 | Service registration for new services | Register ILogRetentionService, IConfigWatcherService |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| SHA256 hashing | MD5 hashing | SHA256 is marginally slower but more collision-resistant; for small YAML files the difference is negligible -- use SHA256 |
| FileSystemWatcher | Pure polling | FSW is event-driven and lower CPU; polling is more reliable on network drives, but configs are local files -- use FSW with polling fallback |
| ReactiveUI.Validation NuGet | Manual INotifyDataErrorInfo | RUI.Validation adds a dependency; manual validation with custom styles is simpler for path-only validation -- use manual approach |

**No new NuGet packages needed.** All required functionality is available through existing dependencies.

## Architecture Patterns

### Recommended Project Structure
```
AutoQAC/
├── Models/
│   └── Configuration/
│       ├── MainConfiguration.cs          # (existing)
│       ├── UserConfiguration.cs          # (existing, add retention settings)
│       └── RetentionSettings.cs          # NEW: age-based or count-based retention config
├── Services/
│   └── Configuration/
│       ├── IConfigurationService.cs      # (existing, extend interface)
│       ├── ConfigurationService.cs       # (existing, add hashing + watcher)
│       ├── IConfigWatcherService.cs      # NEW: config file change detection
│       ├── ConfigWatcherService.cs       # NEW: FSW + hash-based reload
│       ├── ILegacyMigrationService.cs    # NEW: Python config migration
│       ├── LegacyMigrationService.cs     # NEW: migration logic extracted from ConfigService
│       ├── ILogRetentionService.cs       # NEW: log/journal cleanup
│       └── LogRetentionService.cs        # NEW: startup cleanup by age or count
├── ViewModels/
│   ├── SettingsViewModel.cs              # (existing, extend with path props + validation)
│   └── MainWindowViewModel.cs            # (existing, add migration banner state)
└── Views/
    ├── SettingsWindow.axaml              # (existing, add path fields + validation styling)
    └── MainWindow.axaml                  # (existing, add migration warning banner)
```

### Pattern 1: Debounced Path Validation with ReactiveUI
**What:** Use WhenAnyValue + Throttle to validate file paths as user types, with immediate validation on browse
**When to use:** All path text fields in Settings (xEdit, MO2, load order, data directory)
**Example:**
```csharp
// In SettingsViewModel constructor
var xEditPathValid = this.WhenAnyValue(x => x.XEditPath)
    .Throttle(TimeSpan.FromMilliseconds(400))
    .ObserveOn(RxApp.MainThreadScheduler)
    .Select(path => ValidateFilePath(path, ".exe"));

// Separate immediate validation for browse button results
public async Task BrowseXEditAsync()
{
    var path = await _fileDialog.OpenFileDialogAsync(...);
    if (path != null)
    {
        XEditPath = path;
        // Validate immediately -- no debounce for deliberate user action
        XEditPathValidation = ValidateFilePath(path, ".exe");
    }
}
```

### Pattern 2: Content Hash-Based Config Reload
**What:** Compute SHA256 of config file on load; periodically re-hash and reload only when content changed
**When to use:** For the config edit detection feature
**Example:**
```csharp
private string? _lastKnownHash;

private string ComputeFileHash(string filePath)
{
    using var stream = File.OpenRead(filePath);
    var hashBytes = SHA256.HashData(stream);
    return Convert.ToHexString(hashBytes);
}

private async Task CheckForConfigChangesAsync()
{
    var currentHash = ComputeFileHash(_configFilePath);
    if (currentHash != _lastKnownHash)
    {
        _lastKnownHash = currentHash;
        // Reload config, validate, apply if valid
    }
}
```

### Pattern 3: Migration Warning Banner (non-modal)
**What:** A dismissible warning banner at the top of MainWindow, similar to the existing validation error panel
**When to use:** When legacy migration fails
**Example (follows existing ValidationErrors panel pattern in MainWindow.axaml):**
```xml
<!-- Migration Warning Banner (same pattern as validation error panel) -->
<Border Grid.Row="0" DockPanel.Dock="Top"
        IsVisible="{Binding HasMigrationWarning}"
        Background="#FFECB3"
        BorderBrush="#FF9800"
        BorderThickness="1"
        CornerRadius="4"
        Padding="10">
    <Grid ColumnDefinitions="*,Auto">
        <StackPanel Grid.Column="0">
            <TextBlock Text="Legacy Configuration Migration Warning"
                       FontWeight="Bold" Foreground="#E65100" />
            <TextBlock Text="{Binding MigrationWarningMessage}"
                       TextWrapping="Wrap" Foreground="#BF360C" />
        </StackPanel>
        <Button Grid.Column="1" Content="Dismiss"
                Command="{Binding DismissMigrationWarningCommand}" />
    </Grid>
</Border>
```

### Pattern 4: Deferred Config Reload During Cleaning
**What:** Queue config changes detected during an active cleaning session, apply after session ends
**When to use:** When ConfigWatcher detects a change while IsCleaning is true
**Example:**
```csharp
private bool _hasDeferredChanges;

private void OnConfigFileChanged()
{
    if (_stateService.CurrentState.IsCleaning)
    {
        _hasDeferredChanges = true;
        _logger.Information("Config change detected during cleaning, deferring reload");
        return;
    }
    ReloadConfig();
}

// Subscribe to IsCleaning transitions
_stateService.StateChanged
    .Select(s => s.IsCleaning)
    .DistinctUntilChanged()
    .Where(cleaning => !cleaning && _hasDeferredChanges)
    .Subscribe(_ => {
        _hasDeferredChanges = false;
        ReloadConfig();
    });
```

### Anti-Patterns to Avoid
- **Blocking file I/O on UI thread:** All file hash computation and directory enumeration must be async or run on background thread
- **Using file timestamps for change detection:** User decision explicitly requires content hashing, not timestamps
- **Merging legacy config into existing config:** User decision says migration is one-time bootstrap, never merge
- **Modal dialog for migration failures:** User decision says non-modal warning banner, not a dialog
- **Polling without hashing:** If using FSW, still verify with hash before reloading to avoid spurious reloads from save-then-write patterns

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| SHA256 hashing | Custom byte comparison | `SHA256.HashData(stream)` | Built-in, optimized, one-line API in .NET 10 |
| Debounced input | Manual timer management | `Observable.Throttle(TimeSpan)` | Already using Rx throughout project; Throttle handles edge cases |
| File path validation | String regex parsing | `File.Exists()` + `Path.GetExtension()` | OS-level validation is authoritative; no need to parse paths manually |
| Rolling log files | Custom file rotation | Serilog's `rollingInterval: RollingInterval.Day` | Already configured in LoggingService |
| Config serialization | Custom YAML writer | YamlDotNet `Serialize`/`Deserialize` | Already in use throughout project |

**Key insight:** The project already has reactive infrastructure (ReactiveUI, System.Reactive), YAML tooling (YamlDotNet), and structured logging (Serilog). Every feature in this phase builds on those existing patterns.

## Common Pitfalls

### Pitfall 1: FileSystemWatcher Fires Multiple Events for Single Save
**What goes wrong:** Writing a file triggers Changed, then possibly another Changed event. The config reload fires twice for one save.
**Why it happens:** Many programs write files in stages (create temp, write, rename) or the OS buffers writes.
**How to avoid:** Use the content hash as the gate -- even if FSW fires multiple times, the hash comparison prevents redundant reloads. Also use Rx `Throttle(500ms)` on the FSW event stream to coalesce rapid events.
**Warning signs:** Log shows "Config reloaded" multiple times in quick succession.

### Pitfall 2: Race Between Debounced Save and Config Watcher
**What goes wrong:** ConfigurationService writes config to disk (debounced save). ConfigWatcherService detects the change and reloads, overwriting in-memory state with the just-saved state. Circular reload loop.
**Why it happens:** The watcher does not distinguish between "our app saved this" vs "external editor saved this."
**How to avoid:** When ConfigurationService saves, it updates `_lastKnownHash` immediately after writing. The watcher compares against this hash and sees no change. Alternatively, the watcher can pause monitoring during app-initiated saves.
**Warning signs:** Config values flicker or revert after saving in Settings.

### Pitfall 3: Legacy Migration Deletes Files Before Backup
**What goes wrong:** The current `MigrateLegacyConfigAsync` deletes the legacy file outside the lock after migration. Per CONF-06, deletion should happen after backup, not before.
**Why it happens:** Original implementation did deletion outside the lock as a best-effort cleanup.
**How to avoid:** New migration flow: (1) read legacy, (2) validate, (3) write new config, (4) copy legacy to backup folder, (5) delete originals. All within proper error handling.
**Warning signs:** User's Python config files disappear without a backup copy.

### Pitfall 4: Retention Cleanup Deletes Active Log File
**What goes wrong:** Serilog is actively writing to today's log file. Retention cleanup tries to delete it based on count or age.
**Why it happens:** The cleanup logic does not exclude the current active log file.
**How to avoid:** When enumerating log files for cleanup, always exclude the most recent file (the one Serilog is actively writing to). For Serilog's rolling files, the current file has today's date in the name.
**Warning signs:** Application crashes or silently stops logging after startup cleanup.

### Pitfall 5: Path Validation Shows Errors Before User Has Typed
**What goes wrong:** When Settings opens with empty path fields, validation immediately shows red borders everywhere.
**Why it happens:** The validation observable fires on initial property set.
**How to avoid:** Use a separate "touched" state for each field. Only show validation indicators after the user has interacted with the field (typed or browsed). The `Skip(1)` pattern or a boolean flag per field handles this.
**Warning signs:** User opens Settings and sees all fields marked as invalid before doing anything.

### Pitfall 6: Moq Optional Parameter Matching
**What goes wrong:** Adding optional parameters to `IConfigurationService` methods breaks all existing mock setups.
**Why it happens:** Moq matches by exact parameter count, not C# optional parameter semantics (documented in project MEMORY.md).
**How to avoid:** When extending interfaces with new parameters, update ALL mock Setup/Verify calls in tests to include the new parameter matcher (use `It.IsAny<T>()`).
**Warning signs:** 50+ test failures after adding a method parameter.

## Code Examples

Verified patterns from the existing codebase:

### Debounced Reactive Validation (existing pattern from SettingsViewModel)
```csharp
// Source: j:/AutoQACSharp/AutoQAC/ViewModels/SettingsViewModel.cs
var cleaningTimeoutValid = this.WhenAnyValue(x => x.CleaningTimeout)
    .Select(ValidateCleaningTimeout);

// For path validation, add Throttle for debounce:
var xEditPathValid = this.WhenAnyValue(x => x.XEditPath)
    .Throttle(TimeSpan.FromMilliseconds(400))
    .ObserveOn(RxApp.MainThreadScheduler)
    .Select(path => ValidateExecutablePath(path));
```

### SHA256 File Hashing (.NET 10 API)
```csharp
// Source: Microsoft Learn - System.Security.Cryptography.SHA256
using System.Security.Cryptography;

private static string ComputeFileHash(string filePath)
{
    using var stream = File.OpenRead(filePath);
    var hashBytes = SHA256.HashData(stream);
    return Convert.ToHexString(hashBytes);
}
```

### FileSystemWatcher with Rx Throttle
```csharp
// Combine FSW events with Rx for debounced, hash-verified reload
private IDisposable SetupConfigWatcher(string configPath)
{
    var watcher = new FileSystemWatcher(Path.GetDirectoryName(configPath)!)
    {
        Filter = Path.GetFileName(configPath),
        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
        EnableRaisingEvents = true
    };

    return Observable.FromEventPattern<FileSystemEventArgs>(watcher, nameof(watcher.Changed))
        .Throttle(TimeSpan.FromMilliseconds(500))
        .ObserveOn(RxApp.TaskpoolScheduler)
        .Subscribe(_ => CheckForConfigChangesAsync());
}
```

### Log File Enumeration and Cleanup
```csharp
// Serilog rolling file pattern: autoqac-YYYYMMDD.log
public async Task CleanupLogsAsync(RetentionSettings retention)
{
    var logDir = new DirectoryInfo("logs");
    if (!logDir.Exists) return;

    var logFiles = logDir.GetFiles("autoqac-*.log")
        .OrderByDescending(f => f.LastWriteTimeUtc)
        .Skip(1) // Always keep the most recent (active) file
        .ToList();

    if (retention.Mode == RetentionMode.AgeBased)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retention.MaxAgeDays);
        foreach (var file in logFiles.Where(f => f.LastWriteTimeUtc < cutoff))
            file.Delete();
    }
    else // CountBased
    {
        foreach (var file in logFiles.Skip(retention.MaxFileCount - 1))
            file.Delete();
    }
}
```

### Avalonia Custom Validation Style (Path Fields)
```xml
<!-- Custom DataValidationErrors style for path fields with checkmark/X indicators -->
<Style Selector="TextBox.path-validated">
    <Style Selector="^ /template/ Border#PART_BorderElement">
        <Setter Property="BorderBrush" Value="{DynamicResource SystemControlForegroundBaseMediumBrush}" />
    </Style>
</Style>
<Style Selector="TextBox.path-valid">
    <Style Selector="^ /template/ Border#PART_BorderElement">
        <Setter Property="BorderBrush" Value="#4CAF50" />
        <Setter Property="BorderThickness" Value="2" />
    </Style>
</Style>
<Style Selector="TextBox.path-invalid">
    <Style Selector="^ /template/ Border#PART_BorderElement">
        <Setter Property="BorderBrush" Value="#F44336" />
        <Setter Property="BorderThickness" Value="2" />
    </Style>
</Style>
```

### Existing Migration Pattern (needs fixing per CONF-06)
```csharp
// Source: j:/AutoQACSharp/AutoQAC/Services/Configuration/ConfigurationService.cs
// Current migration has bugs:
// 1. Deletes without backup
// 2. No validation of migrated data
// 3. Merges when it should bootstrap only
// New implementation should:
// 1. Detect "AutoQAC Config.yaml" (legacy Python format)
// 2. Read and validate legacy content
// 3. Write to "AutoQAC Settings.yaml" (new C# format)
// 4. Copy original to backup folder
// 5. Delete original
// 6. Report success/failure via observable
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| MD5 for file hashing | SHA256.HashData (span-based) | .NET 7+ | One-line static API, no Create/Dispose needed |
| Manual timer for debounce | Rx `Throttle` operator | Already in use | Cleaner, composable, handles edge cases |
| FileSystemWatcher only | FSW + hash verification | Best practice | Prevents spurious reloads from multi-event writes |
| INotifyDataErrorInfo manual | Avalonia DataValidationErrors control | Avalonia 11+ | Built-in template system for validation visuals |

**Deprecated/outdated:**
- `SHA256.Create()` then `ComputeHash()`: Still works but `SHA256.HashData()` is simpler in .NET 7+
- `FileSystemWatcher.InternalBufferSize` tuning: Less relevant with hash-based verification as safety net

## Discretion Recommendations

### Hash Algorithm: SHA256
**Recommendation:** Use `System.Security.Cryptography.SHA256.HashData()` -- the .NET 10 span-based static API.
**Rationale:** SHA256 is the standard for integrity checking. For small YAML config files (< 10KB), performance is negligible. MD5 would also work but SHA256 is the modern default. No external package needed.

### Default Retention Values: 30 days / 50 files
**Recommendation:** Default to age-based retention of 30 days with a secondary count limit of 50 files.
**Rationale:** Serilog already retains 5 rolling files (hardcoded in LoggingService). The new retention system covers cleanup of older files beyond Serilog's built-in limit. 30 days is generous enough that users rarely lose useful logs. 50 files as a count cap prevents unbounded growth if the app runs daily. These defaults should be stored in the UserConfiguration and editable in Settings.

### Config Change Detection: FileSystemWatcher + Hash Verification
**Recommendation:** Use FileSystemWatcher as the primary notification mechanism, with SHA256 hash verification before actually reloading. Add a 500ms Rx Throttle to coalesce rapid FSW events.
**Rationale:** FSW is event-driven (low CPU) and reliable for local files. The hash verification prevents spurious reloads from multi-event writes and also handles the race condition where our own app's save triggers the watcher. Polling is unnecessary as a primary mechanism for local config files, but if FSW proves unreliable with xEdit file locks (noted in STATE.md), polling at 5-second intervals can be added as a fallback in Phase 6.

### Migration Backup Folder: `AutoQAC Data/migration_backup/`
**Recommendation:** Place backup copies in `AutoQAC Data/migration_backup/` with timestamp prefix: `2026-02-07_AutoQAC Config.yaml`.
**Rationale:** Keeping backups inside the config directory keeps them discoverable. The timestamp prefix prevents overwrites if migration somehow runs again. The folder name clearly communicates its purpose.

### Warning Banner: Amber/orange theme with dismiss button
**Recommendation:** Use the same styling pattern as the existing validation error panel in MainWindow.axaml (amber background `#FFECB3`, orange border `#FF9800`, dismiss button). This matches the existing UI language for "attention needed but not blocking."
**Rationale:** The project already has a non-modal warning panel pattern (the validation errors panel). Reusing that visual language maintains consistency. The banner should show: (1) migration status title, (2) which files failed, (3) dismiss button.

## Open Questions

Things that could not be fully resolved:

1. **Legacy Python config format differences**
   - What we know: The Python version uses `AutoQAC Config.yaml` with keys like `Load_Order.File`, `xEdit.Binary`, `AutoQAC_Settings.*`. The C# version uses `AutoQAC Settings.yaml` with the same YAML structure (same YamlDotNet aliases match Python keys).
   - What is unclear: Whether any Python-only keys exist that have no C# equivalent (e.g., `Install_Path` fields seen in Python's `get_paths()` but not in C#'s `UserConfiguration`). Also unclear if the Python config uses `AutoQAC_Settings.Disable_Skip_Lists` (the C# version does).
   - Recommendation: During migration, deserialize with `IgnoreUnmatchedProperties()` (already configured in the deserializer) so unknown Python-only keys are silently dropped. Log what was migrated for transparency.

2. **Journal files location and naming**
   - What we know: The `JournalExpiration` setting exists (default 7 days) in UserConfiguration. The Python version mentions "AutoQAC Journal.txt".
   - What is unclear: Whether the C# app currently writes journal files anywhere. The existing `logs/` directory has Serilog rolling logs (`autoqac-YYYYMMDD.log`) but no separate journal files.
   - Recommendation: The retention service should handle Serilog log files in `logs/` directory. If journal files are added later, the same service can be extended. For now, apply retention to `logs/autoqac-*.log` files only.

3. **Config watcher during xEdit file locks**
   - What we know: STATE.md notes "FileSystemWatcher reliability with xEdit file locks needs investigation -- may need polling fallback in Phase 6."
   - What is unclear: Whether xEdit's file locking behavior (on plugin files in the game's Data directory) can interfere with FSW watching YAML config files in the `AutoQAC Data/` directory.
   - Recommendation: Since config files are in a separate directory from game files, xEdit file locks should not affect FSW. Implement FSW now; defer polling fallback to Phase 6 only if testing reveals issues.

## Sources

### Primary (HIGH confidence)
- Existing codebase: `ConfigurationService.cs` (debounced saves, legacy migration, file locking)
- Existing codebase: `SettingsViewModel.cs` (ReactiveUI validation pattern with WhenAnyValue)
- Existing codebase: `MainWindow.axaml` (validation error panel pattern for banner UI)
- Existing codebase: `LoggingService.cs` (Serilog rolling file configuration)
- Existing codebase: `UserConfiguration.cs` (JournalExpiration, config structure)
- [Avalonia Data Validation Docs](https://docs.avaloniaui.net/docs/guides/development-guides/data-validation) - DataValidationErrors template system
- [Microsoft Learn - SHA256.HashData](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.sha256.hashdata) - Static span-based API
- [Microsoft Learn - FileSystemWatcher](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher) - Event patterns and buffer management

### Secondary (MEDIUM confidence)
- [ReactiveUI Validation Handbook](https://www.reactiveui.net/docs/handbook/user-input-validation) - IValidatableViewModel patterns
- [Avalonia GitHub Discussion #8282](https://github.com/AvaloniaUI/Avalonia/discussions/8282) - Custom ErrorTemplate styling
- [dotnet/runtime Issue #17111](https://github.com/dotnet/runtime/issues/17111) - FSW polling API discussion, confirms FSW limitations on network drives

### Tertiary (LOW confidence)
- WebSearch for custom Avalonia border styling on validation states - multiple community examples agree on the pseudo-class/style approach but exact syntax varies by Avalonia version

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - All libraries already in project, no new dependencies needed
- Architecture: HIGH - Patterns follow established codebase conventions (ReactiveUI, DI, MVVM)
- Path validation UI: HIGH - Avalonia DataValidationErrors documented, existing validation pattern in SettingsViewModel
- Config change detection: HIGH - SHA256 is standard .NET API, FSW is well-documented
- Legacy migration: MEDIUM - Current migration code exists but needs rewrite; Python config format inferred from code but not exhaustively verified
- Log retention: HIGH - Simple file enumeration and deletion; Serilog rolling file pattern is known
- Pitfalls: HIGH - Based on direct codebase analysis and documented issues in STATE.md

**Research date:** 2026-02-07
**Valid until:** 2026-03-07 (30 days - stable domain, no fast-moving dependencies)
