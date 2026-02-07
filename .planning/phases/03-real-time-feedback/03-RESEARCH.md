# Phase 3: Real-Time Feedback - Research

**Researched:** 2026-02-06
**Domain:** xEdit log file parsing, Avalonia progress UI, ReactiveUI state management, pre-clean validation
**Confidence:** HIGH

## Summary

This phase transforms the existing progress window from a basic counter display into a live feedback system with per-plugin cleaning statistics (ITM, UDR, Nav badges), a session summary bar, and an inline validation error panel. The key architectural change is switching from stdout-based output parsing to post-completion xEdit log file parsing -- the user decided xEdit runs visibly, so AutoQAC only needs to read xEdit's log file after each plugin finishes, not stream stdout in real-time.

The existing codebase has solid foundations: `XEditOutputParser` already parses the correct regex patterns, `ProgressViewModel` already subscribes to `IStateService.StateChanged` and `PluginProcessed`, and `CleaningOrchestrator` already accumulates `PluginCleaningResult` objects per plugin. The work is primarily: (1) adding a log file reader service that locates and reads xEdit's log after each plugin, (2) enriching the `ProgressViewModel` with per-plugin stats and session counters, (3) redesigning the progress window XAML for counter badges and the summary bar, (4) adding an inline validation error panel to the main window, and (5) making the progress window transition to a results summary on completion.

**Primary recommendation:** Build an `IXEditLogFileService` that computes the log path from the xEdit executable path (same directory, `{STEM}_log.txt`), reads it after each plugin completes, and feeds lines to the existing `XEditOutputParser`. Wire per-plugin stats into a new observable on `IStateService` so the `ProgressViewModel` can display live counter badges. Throttle state change notifications using Rx `.Sample()` or `.Throttle()` operators for 100+ plugin sessions.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- **Progress display**: Live counter badges (ITM: X, UDR: X, Nav: X) next to the current plugin name, ticking up as stats are parsed. Focus on the current plugin with counters; show a compact summary bar ("12/87 plugins -- 3 skipped, 1 failed") rather than a full queue list. Plugin count progress bar (fills based on plugins completed out of total). Completed plugin stats persist through the entire session (accumulate below current plugin) until the next cleaning run starts.
- **xEdit output handling**: Do NOT display raw xEdit output in the progress window -- xEdit runs visibly and shows its own output. Parse xEdit's log file after each plugin completes (not stdout redirection). Stats appear immediately when xEdit exits for a plugin. If log file can't be parsed (missing, corrupted, unexpected format): show warning icon inline with tooltip explaining the parse failure, continue cleaning.
- **Error message design**: Pre-clean validation errors shown as inline error panel in the main window (non-blocking, not a modal dialog). Validation runs only when user clicks Clean (not on startup or settings change). Error messages include actionable fix steps. Multiple validation errors shown all at once in the panel so the user sees everything that needs fixing.
- **Multi-plugin session flow**: On plugin failure (crash, timeout, parse error): mark failed, log it, automatically continue to next plugin (no pause/prompt). No time estimates. Session completion: progress window transforms into a results summary (total cleaned, skipped, failed) with persisted per-plugin stats. On cancel (Stop): show partial summary of completed plugins + clear indication of what was cancelled/remaining.

### Claude's Discretion
- Exact layout/spacing of counter badges and summary bar
- Progress bar visual style (color, animation)
- Warning icon design and tooltip formatting
- How the progress window "transforms" into the results summary (animation, layout shift)
- Log file parsing implementation details (regex patterns, error recovery)
- Throttling strategy for UI updates with 100+ plugins

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope
</user_constraints>

## Standard Stack

The project already has all necessary libraries. No new NuGet packages are required.

### Core (Already Installed)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Avalonia | 11.3.11 | UI framework | Already the project's UI platform |
| ReactiveUI.Avalonia | 11.3.8 | MVVM reactive bindings | Already used for all ViewModels |
| System.Reactive | (transitive) | Rx operators (Throttle, Sample, ObserveOn) | Ships with ReactiveUI |

### Supporting (Already Installed)
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Serilog | 4.3.0 | Structured logging for parse failures | Already used project-wide |
| Microsoft.Extensions.DependencyInjection | 10.0.2 | Service registration | Already used for DI |

### No New Dependencies Needed
| Instead of | Don't Use | Reason |
|------------|-----------|--------|
| DynamicData | N/A | ObservableCollection with batch updates is sufficient for this use case (completed plugins list is append-only, 100-200 items max) |
| FileSystemWatcher | N/A | Log file is read post-completion, not watched in real-time; STATE.md already flags FSW reliability concerns for Phase 6 |

**Installation:** No new packages needed.

## Architecture Patterns

### Recommended Changes to Project Structure
```
AutoQAC/
├── Services/
│   └── Cleaning/
│       ├── IXEditLogFileService.cs       # NEW: Log file location + reading
│       ├── XEditLogFileService.cs         # NEW: Implementation
│       ├── XEditOutputParser.cs           # MODIFY: Add ParseLogFile method
│       ├── IXEditOutputParser.cs          # MODIFY: Extract interface
│       ├── CleaningOrchestrator.cs        # MODIFY: Wire log parsing after each plugin
│       └── CleaningService.cs             # MODIFY: Use log file instead of stdout
├── ViewModels/
│   ├── ProgressViewModel.cs              # MODIFY: Add per-plugin stats, session counters
│   ├── MainWindowViewModel.cs            # MODIFY: Add validation error panel
│   └── ValidationErrorViewModel.cs       # NEW: Validation error display model
├── Models/
│   ├── ValidationError.cs                # NEW: Validation error with fix message
│   └── PluginCleaningResult.cs           # Already has per-plugin stats
└── Views/
    ├── ProgressWindow.axaml              # MODIFY: Counter badges, summary bar, results view
    └── MainWindow.axaml                  # MODIFY: Inline validation error panel
```

### Pattern 1: xEdit Log File Reading (Post-Completion)

**What:** After each plugin's xEdit process exits, read the xEdit log file from disk to extract cleaning statistics.
**When to use:** After every `CleanPluginAsync` call, before constructing the `PluginCleaningResult`.

The xEdit log file follows the naming convention: `{XEDIT_STEM_UPPERCASE}_log.txt` in the same directory as the xEdit executable.

Examples:
- `C:\Tools\SSEEdit.exe` -> `C:\Tools\SSEEDIT_log.txt`
- `C:\Tools\FO4Edit64.exe` -> `C:\Tools\FO4EDIT64_log.txt`
- `C:\Tools\xEdit.exe` -> `C:\Tools\XEDIT_log.txt`

**Confidence: HIGH** -- Verified from PACT reference implementation (same tool chain) at https://github.com/GuidanceOfGrace/XEdit-PACT/blob/main/PACT_Start.py

```csharp
// IXEditLogFileService.cs
public interface IXEditLogFileService
{
    /// <summary>
    /// Gets the expected log file path for the given xEdit executable.
    /// </summary>
    string GetLogFilePath(string xEditExecutablePath);

    /// <summary>
    /// Reads the xEdit log file and returns its lines.
    /// Returns empty list if file doesn't exist or can't be read.
    /// </summary>
    Task<(List<string> lines, string? error)> ReadLogFileAsync(
        string xEditExecutablePath,
        CancellationToken ct = default);
}
```

```csharp
// XEditLogFileService.cs
public sealed class XEditLogFileService : IXEditLogFileService
{
    public string GetLogFilePath(string xEditExecutablePath)
    {
        var dir = Path.GetDirectoryName(xEditExecutablePath)!;
        var stem = Path.GetFileNameWithoutExtension(xEditExecutablePath).ToUpperInvariant();
        return Path.Combine(dir, $"{stem}_log.txt");
    }

    public async Task<(List<string> lines, string? error)> ReadLogFileAsync(
        string xEditExecutablePath,
        CancellationToken ct = default)
    {
        var logPath = GetLogFilePath(xEditExecutablePath);

        if (!File.Exists(logPath))
            return (new List<string>(), $"Log file not found: {logPath}");

        try
        {
            // xEdit may still hold a brief lock, retry with delay
            var lines = await File.ReadAllLinesAsync(logPath, ct).ConfigureAwait(false);
            return (lines.ToList(), null);
        }
        catch (IOException ex)
        {
            return (new List<string>(), $"Failed to read log file: {ex.Message}");
        }
    }
}
```

### Pattern 2: Enriched State for Per-Plugin Stats

**What:** Extend `IStateService` with an observable that emits `PluginCleaningResult` objects as they are added, so the ProgressViewModel can display per-plugin stats in real-time.
**When to use:** After each plugin completes, emit the result with statistics.

```csharp
// Already exists in IStateService:
// IObservable<(string plugin, CleaningStatus status)> PluginProcessed { get; }
// void AddDetailedCleaningResult(PluginCleaningResult result);

// NEW observable on IStateService:
IObservable<PluginCleaningResult> DetailedPluginResult { get; }
```

The `AddDetailedCleaningResult` already stores results. Adding a new `Subject<PluginCleaningResult>` that emits when each detailed result is added allows the ProgressViewModel to subscribe and display per-plugin ITM/UDR/Nav badges immediately.

### Pattern 3: Progress Window State Machine (Cleaning -> Results)

**What:** The progress window has two visual states: "active cleaning" (current plugin, badges, progress bar) and "results summary" (totals, per-plugin list). Rather than opening a separate window, toggle visibility of XAML panels.
**When to use:** When `IsCleaning` transitions from true to false.

```csharp
// In ProgressViewModel
private bool _isShowingResults;
public bool IsShowingResults
{
    get => _isShowingResults;
    set => this.RaiseAndSetIfChanged(ref _isShowingResults, value);
}

// Trigger: when cleaning finishes
_stateService.CleaningCompleted
    .ObserveOn(RxApp.MainThreadScheduler)
    .Subscribe(session =>
    {
        SessionResult = session;
        IsShowingResults = true;
    });
```

In the AXAML, use `IsVisible="{Binding !IsShowingResults}"` for the active view and `IsVisible="{Binding IsShowingResults}"` for the results view. This fulfills the user decision that the progress window "transforms" into a results summary -- it is simply a visibility toggle with the same window.

### Pattern 4: Inline Validation Error Panel

**What:** A non-modal error panel at the top of the main window that shows when pre-clean validation fails.
**When to use:** When user clicks "Start Cleaning" and validation fails.

```csharp
// ValidationError model
public sealed record ValidationError(string Title, string Message, string FixStep);

// In MainWindowViewModel
private ObservableCollection<ValidationError> _validationErrors = new();
public ObservableCollection<ValidationError> ValidationErrors
{
    get => _validationErrors;
    set => this.RaiseAndSetIfChanged(ref _validationErrors, value);
}

private bool _hasValidationErrors;
public bool HasValidationErrors
{
    get => _hasValidationErrors;
    set => this.RaiseAndSetIfChanged(ref _hasValidationErrors, value);
}
```

### Pattern 5: UI Update Throttling (STAT-02)

**What:** For sessions with 100+ plugins, throttle RaisePropertyChanged notifications to avoid UI lag.
**When to use:** On observables that fire per-plugin or per-state-change.

```csharp
// In ProgressViewModel constructor:
_stateService.StateChanged
    .Sample(TimeSpan.FromMilliseconds(100))  // At most 10 updates/sec
    .ObserveOn(RxApp.MainThreadScheduler)
    .Subscribe(OnStateChanged);
```

**Key insight:** Use `Sample` (not `Throttle`) for progress updates. `Throttle` waits for silence, which means during continuous rapid updates the UI would never refresh. `Sample` takes the latest value at fixed intervals, ensuring steady UI updates during continuous processing. Use `.Sample(100ms)` for the state stream and process the `DetailedPluginResult` observable without throttling (it fires once per plugin, not rapidly).

### Anti-Patterns to Avoid
- **String concatenation for LogOutput:** The current ProgressViewModel appends to `LogOutput` string -- this creates O(n^2) memory pressure for large sessions. Replace with an `ObservableCollection<PluginCleaningResult>` for the completed plugins list.
- **Separate results window:** Do NOT open a separate `CleaningResultsWindow` from the progress window. The user decision says the progress window transforms into the results summary. The existing `CleaningResultsWindow` can remain for viewing past results from the main menu, but is not shown automatically.
- **Stdout redirection for stats:** Do NOT use `ProcessStartInfo.RedirectStandardOutput` for obtaining cleaning stats. The user explicitly decided to parse log files. Note: stdout IS still redirected for process management purposes (the `ProcessExecutionService` already does this), but the stats should come from the log file.
- **Modal validation dialogs:** Do NOT use `_messageDialog.ShowErrorAsync()` for validation errors. Use the inline panel instead.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Observable throttling | Custom timer-based throttle | `System.Reactive.Linq.Sample()` | Battle-tested, handles edge cases, thread-safe |
| UI thread dispatch | Manual Dispatcher calls | `.ObserveOn(RxApp.MainThreadScheduler)` | ReactiveUI standard, testable with test schedulers |
| Log file retry | Custom retry loop | Simple `try/catch` with single retry after 200ms delay | xEdit releases the log file quickly; a polling loop is unnecessary for post-exit reads |
| Validation message formatting | Custom string builder per error type | Data-driven validation rules returning `ValidationError` records | Clean separation, easy to add new validations |

**Key insight:** The existing `XEditOutputParser` regex patterns are already correct for xEdit log file content. The same patterns (`Undeleting:`, `Removing:`, `Skipping:`, `Making Partial Form:`) appear in both stdout and log files. Do not write new parsing logic -- reuse the existing parser with log file lines as input.

## Common Pitfalls

### Pitfall 1: xEdit Log File Lock After Exit
**What goes wrong:** xEdit may hold a brief file lock on `SSEEDIT_log.txt` even after the process exits, causing `IOException` on first read attempt.
**Why it happens:** Windows file handle cleanup can lag behind process exit notification.
**How to avoid:** In `ReadLogFileAsync`, if the first read throws `IOException`, wait 200ms and retry once. If it still fails, return the error (show warning icon per user decision).
**Warning signs:** Sporadic "log file not found" or "access denied" errors that don't reproduce consistently.

### Pitfall 2: Log File Contains Multiple Runs
**What goes wrong:** xEdit's log file is overwritten on each run (not appended), so this is NOT a concern. However, if the log file somehow contains stale data from a previous run, stats could be wrong.
**Why it happens:** If xEdit crashes before writing the log, the log might contain data from the previous successful run.
**How to avoid:** Record the log file's last-modified timestamp before starting the xEdit process. After exit, verify the log file's timestamp is newer than the start time. If not, treat as "log not available for this plugin."
**Warning signs:** Stats that seem too high or match the previous plugin's results exactly.

### Pitfall 3: Progress Window Closed During Cleaning
**What goes wrong:** User closes the progress window (X button) while cleaning is still in progress. The cleaning continues in the background, but there's no way to see results.
**Why it happens:** The window's `OnClosed` handler disposes the ViewModel's subscriptions.
**How to avoid:** Either prevent closing during cleaning (set `CanClose = false` in code-behind while `IsCleaning`), or allow closing but keep the ViewModel alive so results can be shown later. The simplest approach: intercept the close event and show a confirmation.
**Warning signs:** Users reporting "cleaning finished but I never saw results."

### Pitfall 4: Sample() Drops Final State
**What goes wrong:** Using `.Sample(100ms)` on the state stream may miss the final state update (cleaning finished) if it arrives just after the last sample tick.
**Why it happens:** `Sample` is time-based and doesn't guarantee the last value is emitted.
**How to avoid:** Use `.Sample(100ms).Merge(stateChanged.Where(s => !s.IsCleaning))` to ensure the "cleaning finished" state always gets through regardless of throttling.
**Warning signs:** Progress bar stuck at 99% or last plugin's stats not showing.

### Pitfall 5: Validation Runs in Orchestrator (Too Late)
**What goes wrong:** The current `CleaningOrchestrator.ValidateConfigurationAsync` runs after cleaning starts, which means validation errors appear as exceptions, not as friendly inline messages.
**Why it happens:** The user decision says validation runs when the user clicks Clean. If validation happens inside the orchestrator, the error is an exception that gets caught generically.
**How to avoid:** Move pre-clean validation to a new method on the orchestrator (or a separate validation service) that returns a `List<ValidationError>` BEFORE calling `StartCleaningAsync`. The `MainWindowViewModel.StartCleaningAsync` calls validation first, shows inline errors if any, and only proceeds to start cleaning if validation passes.
**Warning signs:** Validation errors appearing as modal error dialogs instead of the inline panel.

## Code Examples

### Log File Path Computation (verified from PACT reference)
```csharp
// Source: PACT reference implementation (GitHub)
// xEdit log naming convention: {STEM_UPPERCASE}_log.txt in same directory
public string GetLogFilePath(string xEditExecutablePath)
{
    var dir = Path.GetDirectoryName(xEditExecutablePath)
              ?? throw new ArgumentException("Invalid xEdit path");
    var stem = Path.GetFileNameWithoutExtension(xEditExecutablePath)
                   .ToUpperInvariant();
    return Path.Combine(dir, $"{stem}_log.txt");
}

// Examples:
// "C:\Tools\SSEEdit.exe"      -> "C:\Tools\SSEEDIT_log.txt"
// "C:\Tools\FO4Edit64.exe"    -> "C:\Tools\FO4EDIT64_log.txt"
// "C:\Tools\xEdit.exe"        -> "C:\Tools\XEDIT_log.txt"
```

### Reusing Existing Parser with Log File Lines
```csharp
// The existing XEditOutputParser.ParseOutput(List<string>) works unchanged.
// Log file lines contain the same patterns as stdout:
//   "Undeleting: [NAVM:00012345] in \"TestMod.esp\""
//   "Removing: [REFR:00054321] in \"TestMod.esp\""
//   "Skipping: [CELL:00098765] in \"TestMod.esp\""
//   "Making Partial Form: [NAVM:00099999] in \"TestMod.esp\""

var (logLines, error) = await _logFileService.ReadLogFileAsync(xEditPath, ct);
if (error != null)
{
    // Show warning icon with tooltip per user decision
    return new CleaningResult
    {
        Success = true,  // Cleaning succeeded, parsing failed
        Status = CleaningStatus.Cleaned,
        Message = $"Cleaned (log parse warning: {error})",
        Statistics = null,  // No stats available
        LogParseWarning = error
    };
}
var stats = _outputParser.ParseOutput(logLines);
```

### Counter Badges in AXAML
```xml
<!-- Per-plugin counter badges -->
<StackPanel Orientation="Horizontal" Spacing="10"
            IsVisible="{Binding HasCurrentPluginStats}">
    <Border Background="#2D7D46" CornerRadius="3" Padding="8,3">
        <StackPanel Orientation="Horizontal" Spacing="4">
            <TextBlock Text="ITM:" Foreground="White" FontWeight="SemiBold" FontSize="12"/>
            <TextBlock Text="{Binding CurrentItmCount}" Foreground="White" FontSize="12"/>
        </StackPanel>
    </Border>
    <Border Background="#B8860B" CornerRadius="3" Padding="8,3">
        <StackPanel Orientation="Horizontal" Spacing="4">
            <TextBlock Text="UDR:" Foreground="White" FontWeight="SemiBold" FontSize="12"/>
            <TextBlock Text="{Binding CurrentUdrCount}" Foreground="White" FontSize="12"/>
        </StackPanel>
    </Border>
    <Border Background="#8B4513" CornerRadius="3" Padding="8,3">
        <StackPanel Orientation="Horizontal" Spacing="4">
            <TextBlock Text="Nav:" Foreground="White" FontWeight="SemiBold" FontSize="12"/>
            <TextBlock Text="{Binding CurrentNavCount}" Foreground="White" FontSize="12"/>
        </StackPanel>
    </Border>
</StackPanel>
```

### Summary Bar
```xml
<!-- Compact summary bar -->
<Border Background="{DynamicResource SystemControlBackgroundBaseLowBrush}"
        CornerRadius="4" Padding="10,6" Margin="0,5">
    <TextBlock>
        <Run Text="{Binding Progress}"/>
        <Run Text="/"/>
        <Run Text="{Binding Total}"/>
        <Run Text=" plugins"/>
        <Run Text=" -- "/>
        <Run Text="{Binding SkippedCount}"/>
        <Run Text=" skipped, "/>
        <Run Text="{Binding FailedCount}"/>
        <Run Text=" failed"/>
    </TextBlock>
</Border>
```

### Inline Validation Error Panel
```xml
<!-- Validation error panel (in MainWindow.axaml) -->
<Border IsVisible="{Binding HasValidationErrors}"
        Background="#FFF2CC"
        BorderBrush="#E6A817"
        BorderThickness="1"
        CornerRadius="4"
        Padding="10"
        Margin="0,5">
    <StackPanel Spacing="5">
        <StackPanel Orientation="Horizontal" Spacing="5">
            <TextBlock Text="Cannot start cleaning:" FontWeight="Bold"
                       Foreground="#8B4513"/>
            <Button Content="Dismiss" Command="{Binding DismissValidationCommand}"
                    Padding="5,1" FontSize="11"
                    HorizontalAlignment="Right"/>
        </StackPanel>
        <ItemsControl ItemsSource="{Binding ValidationErrors}">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <StackPanel Margin="0,3">
                        <TextBlock Text="{Binding Title}" FontWeight="SemiBold"
                                   Foreground="#8B4513"/>
                        <TextBlock Text="{Binding FixStep}" TextWrapping="Wrap"
                                   Foreground="#5D4037" FontSize="12"/>
                    </StackPanel>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </StackPanel>
</Border>
```

### Throttled State Subscription
```csharp
// In ProgressViewModel: throttle rapid state changes for 100+ plugin sessions
var throttledState = _stateService.StateChanged
    .Sample(TimeSpan.FromMilliseconds(100))
    .Merge(_stateService.StateChanged.Where(s => !s.IsCleaning)) // Always show final state
    .DistinctUntilChanged()
    .ObserveOn(RxApp.MainThreadScheduler);

var stateSubscription = throttledState.Subscribe(OnStateChanged);
_disposables.Add(stateSubscription);

// Per-plugin results: NOT throttled (fires once per plugin, not rapidly)
var detailedResultSubscription = _stateService.DetailedPluginResult
    .ObserveOn(RxApp.MainThreadScheduler)
    .Subscribe(OnDetailedResult);
_disposables.Add(detailedResultSubscription);
```

### Pre-Clean Validation (Returns All Errors At Once)
```csharp
public List<ValidationError> ValidatePreClean()
{
    var errors = new List<ValidationError>();
    var state = _stateService.CurrentState;

    if (string.IsNullOrEmpty(state.XEditExecutablePath))
    {
        errors.Add(new ValidationError(
            "xEdit not configured",
            "xEdit executable path is not set.",
            "Click Edit > Settings > xEdit Path to browse for your xEdit executable (SSEEdit.exe, FO4Edit.exe, etc.)."));
    }
    else if (!File.Exists(state.XEditExecutablePath))
    {
        errors.Add(new ValidationError(
            "xEdit not found",
            $"xEdit not found at {state.XEditExecutablePath}.",
            "Click Edit > Settings > xEdit Path to browse for the correct location."));
    }

    if (string.IsNullOrEmpty(state.LoadOrderPath) || !File.Exists(state.LoadOrderPath))
    {
        errors.Add(new ValidationError(
            "Load order not configured",
            "No load order file is configured or the file does not exist.",
            "Select your game, or click the Load Order browse button to select your load order file."));
    }

    if (state.Mo2ModeEnabled)
    {
        if (string.IsNullOrEmpty(state.Mo2ExecutablePath))
        {
            errors.Add(new ValidationError(
                "MO2 not configured",
                "MO2 mode is enabled but no MO2 executable path is set.",
                "Click Edit > Settings > MO2 Path, or disable MO2 mode if not using Mod Organizer 2."));
        }
        else if (!File.Exists(state.Mo2ExecutablePath))
        {
            errors.Add(new ValidationError(
                "MO2 not found",
                $"MO2 executable not found at {state.Mo2ExecutablePath}.",
                "Check MO2 executable path in Settings, or disable MO2 mode."));
        }
    }

    return errors;
}
```

## State of the Art

| Old Approach (Current) | New Approach (This Phase) | Impact |
|------------------------|--------------------------|--------|
| Stats from stdout via `ProcessResult.OutputLines` | Stats from xEdit log file (`{STEM}_log.txt`) | Matches user decision; xEdit window stays visible |
| Generic `IProgress<string>` callback | `IXEditLogFileService` reads log after exit | Cleaner separation; no stdout dependency for stats |
| Separate `CleaningResultsWindow` shown on completion | Progress window transforms into results view | Single window; user decision |
| Modal dialog validation errors | Inline validation error panel | Non-blocking; user sees all errors at once |
| `ProgressViewModel.LogOutput` string concatenation | `ObservableCollection<PluginCleaningResult>` | O(n) vs O(n^2); proper data binding |
| All state changes trigger UI update | `.Sample(100ms)` throttled state subscription | Handles 100+ plugin sessions without UI lag |

**Note on PROG-02 conflict:** The roadmap success criterion #2 says "Raw xEdit output lines scroll in real-time in the progress window." This directly contradicts the CONTEXT.md decision "Do NOT display raw xEdit output." The CONTEXT.md decision takes precedence. PROG-02 is satisfied by the xEdit window being visible (user can see xEdit's output there), not by AutoQAC displaying it.

## Open Questions

1. **Log file overwrite timing**
   - What we know: xEdit overwrites (not appends) the log file on each run. PACT reads it post-completion successfully.
   - What's unclear: Whether xEdit writes the log atomically or incrementally. If incremental, a crash mid-write could leave a partial log.
   - Recommendation: Check file modification time against process start time. If the log predates the process start, treat as stale (warning icon).

2. **MO2 log file location**
   - What we know: In MO2 mode, `ModOrganizer.exe run` wraps xEdit execution. The xEdit executable still writes its log in its own directory.
   - What's unclear: Whether MO2's virtual filesystem affects log file location.
   - Recommendation: Use the same log file path logic (based on xEdit executable path, not MO2 path). Test in MO2 mode during verification. LOW confidence on MO2-specific behavior.

3. **Progress window lifecycle with double-duty**
   - What we know: User wants the progress window to serve as both live progress and results summary.
   - What's unclear: If the user closes and reopens the progress window, should it show the last session's results?
   - Recommendation: Keep session data in `IStateService.LastSessionResult` (already exists). If the progress window is opened while not cleaning, show last results if available.

## Sources

### Primary (HIGH confidence)
- **Codebase analysis** - Direct reading of CleaningOrchestrator.cs, CleaningService.cs, XEditOutputParser.cs, ProgressViewModel.cs, StateService.cs, ProcessExecutionService.cs
- **PACT reference implementation** - https://github.com/GuidanceOfGrace/XEdit-PACT/blob/main/PACT_Start.py (log file naming convention: `{STEM_UPPERCASE}_log.txt`)
- **xEdit official docs** - https://tes5edit.github.io/docs/2-overview.html (`-R:<path><filename>` custom log filename parameter confirms log file feature exists)
- **Python reference implementation** - `Code_To_Port/AutoQACLib/cleaning_service.py` (regex patterns match existing C# parser)

### Secondary (MEDIUM confidence)
- **ReactiveUI Rx operators** - https://www.reactiveui.net/docs/handbook/scheduling (Sample vs Throttle for UI updates)
- **Avalonia performance** - https://github.com/AvaloniaUI/Avalonia/discussions/19239 (real-time UI update patterns)
- **LOOT SSEEdit cleaning reports** - https://github.com/loot/skyrimse/issues/467 (confirms log file format from community usage)

### Tertiary (LOW confidence)
- **MO2 + xEdit log file interaction** - No authoritative source found; based on general understanding that MO2 virtualizes game files but not tool directories

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - No new dependencies, all libraries already in use
- Architecture (log file service): HIGH - Verified from PACT reference implementation and xEdit docs
- Architecture (UI patterns): HIGH - Standard ReactiveUI/Avalonia patterns already used in codebase
- xEdit log file format: HIGH - Same regex patterns as stdout, confirmed by PACT
- Pitfalls: MEDIUM - File locking timing and MO2 interaction need empirical validation
- Throttling strategy: HIGH - Standard Rx operators, well-documented

**Research date:** 2026-02-06
**Valid until:** 2026-03-06 (stable domain, no fast-moving dependencies)
