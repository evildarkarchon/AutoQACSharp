# Phase 6: UI Polish & Monitoring - Research

**Researched:** 2026-02-07
**Domain:** Avalonia UI MVVM decomposition, process CPU monitoring, GitHub API integration, application logging
**Confidence:** HIGH

## Summary

Phase 6 covers four areas: (1) decomposing the bloated MainWindowViewModel (~1186 lines, 8 constructor dependencies, 29 commands/interactions) into focused sub-ViewModels, (2) adding an About dialog with version info and GitHub update checking, (3) improving application logging at startup and session completion, and (4) detecting hung xEdit processes via CPU usage monitoring with inline notification in the progress window.

The MainWindowViewModel is the primary refactoring target. It contains configuration path management, plugin list management, cleaning commands, file dialog orchestration, validation, migration warnings, and settings state all in a single class. Functional grouping by feature area aligns naturally with the existing code regions. The About dialog and hang detection are greenfield additions with well-understood patterns.

**Primary recommendation:** Decompose MainWindowViewModel first (highest risk, most test migration), then add About dialog, then hang detection, then logging improvements.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- About dialog: Classic centered layout, app icon at top, version info stacked below, links at bottom
- About dialog info: app version, build date, .NET runtime version, key library versions (Avalonia, ReactiveUI)
- About dialog links: GitHub repository, GitHub issues (bug report), xEdit project
- About dialog update check: button that hits GitHub API to compare current vs latest release tag
- Hang detection threshold: 60 seconds of near-zero CPU usage before flagging xEdit as potentially hung
- Hang detection notification: inline warning banner in progress window with action buttons (not modal dialog)
- Hang detection actions: Wait or Kill (two simple choices)
- Hang detection auto-dismiss: if xEdit resumes CPU activity, automatically clear the warning
- No in-app log viewer -- explicitly removed from scope
- ViewModel decomposition split strategy: functional grouping by feature area
- ViewModel decomposition behavior changes: minor polish acceptable if obvious UX improvements surface
- ViewModel decomposition test migration: existing tests migrated to target new sub-ViewModels directly

### Claude's Discretion
- Sub-ViewModel communication pattern (parent mediation vs shared reactive state)
- Exact decomposition boundaries (which properties/commands go where)
- Logging format and verbosity levels for startup/session summary
- About dialog visual polish (spacing, typography, icon size)
- Update check error handling and UI feedback

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope
</user_constraints>

## Standard Stack

### Core (already in project)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Avalonia | 11.3.11 | UI framework | Already in use, cross-platform XAML |
| ReactiveUI.Avalonia | 11.3.8 | MVVM + reactive patterns | Already in use, sub-VM composition via property binding |
| Serilog | 4.3.0 | Structured logging | Already in use, supports startup/session logging |
| System.Diagnostics.Process | .NET 10 built-in | CPU monitoring | Already used for xEdit process management |

### New (to add)
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.Net.Http.HttpClient | .NET 10 built-in | GitHub API calls | About dialog update check -- no new NuGet package needed |
| System.Text.Json | .NET 10 built-in | Parse GitHub API response | Already used elsewhere in the project |
| System.Reflection | .NET 10 built-in | Assembly version/build info | About dialog version display |
| System.Runtime.InteropServices.RuntimeInformation | .NET 10 built-in | .NET runtime version | About dialog .NET version display |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Raw HttpClient for GitHub API | Octokit.NET | Overkill for a single endpoint; adds dependency for one API call |
| Manual CPU polling | PerformanceCounter | PerformanceCounter is Windows-only; Process.TotalProcessorTime is cross-platform |
| Assembly.GetEntryAssembly() | SourceLink metadata | SourceLink is a build-time tool; runtime reflection is simpler and sufficient |

**Installation:**
No new NuGet packages required. All capabilities are in the .NET 10 BCL or already-installed packages.

## Architecture Patterns

### Recommended ViewModel Decomposition

Based on analysis of the existing MainWindowViewModel (1186 lines), the natural functional groupings are:

```
AutoQAC/ViewModels/
  MainWindowViewModel.cs          # Slim orchestrator (~150 lines)
  MainWindow/
    ConfigurationViewModel.cs     # Path management, file dialogs, validation indicators
    PluginListViewModel.cs        # Plugin collection, select all/none, skip list display
    CleaningCommandsViewModel.cs  # Start/Stop/Preview commands, validation errors, progress interaction
```

**Rationale for 3 sub-ViewModels (not more):**
- **ConfigurationViewModel**: LoadOrderPath, XEditPath, Mo2Path, GameDataFolder, Mo2ModeEnabled, PartialFormsEnabled, DisableSkipListsEnabled, SelectedGame, path validation state (IsXEditPathValid etc.), file browse commands, game selection subscription, auto-save subscriptions. Approximately 550 lines of the original.
- **PluginListViewModel**: PluginsToClean collection, SelectedPlugin, SelectAll/DeselectAll commands, skip list subscription, RefreshPluginsForGameAsync. Approximately 200 lines.
- **CleaningCommandsViewModel**: StartCleaningCommand, StopCleaningCommand, PreviewCommand, ExitCommand, ShowAboutCommand, RestoreBackupsCommand, ResetSettingsCommand, ShowSettingsCommand, ShowSkipListCommand, ValidationErrors, HasValidationErrors, StatusText, IsCleaning, CanStartCleaning, all Interactions, ValidatePreClean. Approximately 400 lines.

### Pattern 1: Parent Mediation (Recommended for Sub-ViewModel Communication)

**What:** MainWindowViewModel holds references to sub-ViewModels as public properties. Sub-ViewModels communicate through the parent, not directly to each other.

**When to use:** When sub-ViewModels need data from each other (e.g., CleaningCommands needs PluginList's count, Configuration's xEdit path).

**Why recommended over shared reactive state:** The existing architecture already funnels all state through IStateService. Sub-ViewModels can each subscribe to IStateService.StateChanged independently. No new shared state mechanism needed. Parent mediation is only for cross-VM reactive chains like `canStart` observable.

**Example:**
```csharp
public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    // Sub-ViewModels as public properties for XAML binding
    public ConfigurationViewModel Configuration { get; }
    public PluginListViewModel PluginList { get; }
    public CleaningCommandsViewModel CleaningCommands { get; }

    // Interactions stay on parent (they're wired in MainWindow.axaml.cs)
    public Interaction<Unit, Unit> ShowProgressInteraction { get; } = new();
    // ... other interactions

    public MainWindowViewModel(
        IConfigurationService configService,
        IStateService stateService,
        ICleaningOrchestrator orchestrator,
        ILoggingService logger,
        IFileDialogService fileDialog,
        IMessageDialogService messageDialog,
        IPluginValidationService pluginService,
        IPluginLoadingService pluginLoadingService)
    {
        Configuration = new ConfigurationViewModel(configService, stateService, logger, fileDialog, messageDialog, pluginService, pluginLoadingService);
        PluginList = new PluginListViewModel(stateService, configService, pluginLoadingService, logger);
        CleaningCommands = new CleaningCommandsViewModel(stateService, orchestrator, logger, messageDialog);

        // Wire cross-VM reactive chains
        // e.g., CleaningCommands.canStart depends on PluginList and Configuration
    }
}
```

### Pattern 2: XAML Binding Path Update

**What:** MainWindow.axaml bindings change from `{Binding PropertyName}` to `{Binding Configuration.PropertyName}`.

**Example:**
```xml
<!-- Before -->
<TextBox Text="{Binding XEditPath}" />

<!-- After -->
<TextBox Text="{Binding Configuration.XEditPath}" />
```

**Critical:** With `AvaloniaUseCompiledBindingsByDefault=true` (already set in .csproj), the `x:DataType` on sub-controls must be updated to match new ViewModel types, or the top-level DataType must expose sub-VM properties correctly.

### Pattern 3: CPU Monitoring via Polling Timer

**What:** A service that periodically samples `Process.TotalProcessorTime` and computes CPU delta over time intervals.

**Example:**
```csharp
public interface IHangDetectionService : IDisposable
{
    /// <summary>
    /// Start monitoring a process for hang detection.
    /// Emits true when process appears hung, false when it resumes.
    /// </summary>
    IObservable<bool> MonitorProcess(Process process);
}

public sealed class HangDetectionService : IHangDetectionService
{
    private const int PollIntervalMs = 5000;        // Check every 5 seconds
    private const int HangThresholdMs = 60_000;     // 60 seconds threshold
    private const double CpuThreshold = 0.5;        // Near-zero = less than 0.5%

    public IObservable<bool> MonitorProcess(Process process)
    {
        return Observable.Create<bool>(observer =>
        {
            var lastCpuTime = process.TotalProcessorTime;
            var lastCheckTime = DateTime.UtcNow;
            var nearZeroDuration = TimeSpan.Zero;
            var wasHung = false;

            var timer = Observable.Interval(TimeSpan.FromMilliseconds(PollIntervalMs))
                .Subscribe(_ =>
                {
                    try
                    {
                        if (process.HasExited) return;

                        var currentCpuTime = process.TotalProcessorTime;
                        var now = DateTime.UtcNow;
                        var elapsed = now - lastCheckTime;
                        var cpuDelta = (currentCpuTime - lastCpuTime).TotalMilliseconds;
                        var cpuPercent = (cpuDelta / elapsed.TotalMilliseconds) * 100.0;

                        if (cpuPercent < CpuThreshold)
                        {
                            nearZeroDuration += elapsed;
                            if (nearZeroDuration.TotalMilliseconds >= HangThresholdMs && !wasHung)
                            {
                                wasHung = true;
                                observer.OnNext(true); // Hung detected
                            }
                        }
                        else
                        {
                            if (wasHung)
                            {
                                wasHung = false;
                                observer.OnNext(false); // Resumed
                            }
                            nearZeroDuration = TimeSpan.Zero;
                        }

                        lastCpuTime = currentCpuTime;
                        lastCheckTime = now;
                    }
                    catch (InvalidOperationException)
                    {
                        // Process exited
                        observer.OnCompleted();
                    }
                });

            return timer;
        });
    }
}
```

### Pattern 4: GitHub Release Check

**What:** Hit `https://api.github.com/repos/{owner}/{repo}/releases/latest` and compare `tag_name` against current assembly version.

**Example:**
```csharp
public sealed class UpdateCheckService
{
    private static readonly HttpClient _httpClient = new();

    static UpdateCheckService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AutoQAC/1.0");
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetStringAsync(
                "https://api.github.com/repos/OWNER/REPO/releases/latest", ct);

            var doc = JsonDocument.Parse(response);
            var tagName = doc.RootElement.GetProperty("tag_name").GetString();
            var htmlUrl = doc.RootElement.GetProperty("html_url").GetString();

            if (tagName == null) return new UpdateCheckResult(false, null, null);

            // Strip leading 'v' if present
            var remoteVersion = tagName.TrimStart('v');
            var currentVersion = GetCurrentVersion();

            var isNewer = Version.TryParse(remoteVersion, out var remote)
                          && Version.TryParse(currentVersion, out var current)
                          && remote > current;

            return new UpdateCheckResult(isNewer, remoteVersion, htmlUrl);
        }
        catch (HttpRequestException)
        {
            return new UpdateCheckResult(false, null, null, "Network error");
        }
        catch (TaskCanceledException)
        {
            return new UpdateCheckResult(false, null, null, "Request timed out");
        }
    }

    private static string GetCurrentVersion()
    {
        var assembly = System.Reflection.Assembly.GetEntryAssembly();
        var version = assembly?.GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
    }
}

public sealed record UpdateCheckResult(
    bool UpdateAvailable,
    string? LatestVersion,
    string? ReleaseUrl,
    string? Error = null);
```

### Anti-Patterns to Avoid

- **Decomposing too finely:** Creating 6+ micro-ViewModels makes XAML binding paths long and cross-VM wiring complex. Three sub-VMs is the sweet spot for this ViewModel.
- **Shared mutable state between sub-ViewModels:** Don't create a shared context object. Use IStateService (already the authoritative state source) and parent mediation.
- **PerformanceCounter for CPU monitoring:** Windows-only, requires special permissions. Use `Process.TotalProcessorTime` which is cross-platform and already available.
- **Blocking HttpClient in About dialog:** Always use async. The "Check for Updates" button should be an async ReactiveCommand.
- **Moving Interactions to sub-ViewModels:** Keep Interactions on MainWindowViewModel since they're registered in MainWindow.axaml.cs code-behind. Sub-VMs can raise events/observables that the parent handles.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Version string parsing | Custom semver parser | `System.Version.TryParse` | Handles major.minor.build.revision natively |
| GitHub API HTTP handling | Custom retry/timeout | `HttpClient` with timeout + try/catch | Single fire-and-forget request, no retry needed |
| CPU usage calculation | PerformanceCounter wrapper | `Process.TotalProcessorTime` delta over time | Cross-platform, no special permissions |
| Assembly metadata | Hard-coded version strings | `Assembly.GetEntryAssembly()` reflection | Automatically picks up build metadata |
| .NET runtime version | Environment.Version string parsing | `RuntimeInformation.FrameworkDescription` | Returns ".NET 10.0.0" formatted string |
| Timer-based polling | Manual Thread+Sleep | `Observable.Interval` from System.Reactive | Already using Rx throughout the project, composes cleanly |

**Key insight:** All four sub-features (decomposition, About, hang detection, logging) use technologies already in the project. No new NuGet packages needed.

## Common Pitfalls

### Pitfall 1: Breaking Compiled Bindings During Decomposition
**What goes wrong:** With `AvaloniaUseCompiledBindingsByDefault=true`, changing ViewModel structure without updating XAML `x:DataType` or binding paths causes silent failures (bindings silently don't bind).
**Why it happens:** Compiled bindings are validated at build time against the DataType. Moving properties to sub-VMs changes the type hierarchy.
**How to avoid:** Update ALL binding paths in MainWindow.axaml simultaneously with the ViewModel split. Build immediately after XAML changes to catch compile-time binding errors.
**Warning signs:** UI elements showing default/empty values after the split.

### Pitfall 2: Process.TotalProcessorTime Accuracy on Different Schedulers
**What goes wrong:** CPU delta calculation can report zero even when xEdit is doing small amounts of work, because the OS scheduler may not have granted the process a time slice in the polling interval.
**Why it happens:** `TotalProcessorTime` reflects actual CPU time granted by the OS, not wall-clock activity. On busy systems, a 5-second poll interval may miss small bursts.
**How to avoid:** Use a generous threshold (the user specified 60 seconds of near-zero, which is very conservative). Don't make the poll interval too short (5 seconds is reasonable). The auto-dismiss on resume handles false positives.
**Warning signs:** Hang warnings appearing during legitimate xEdit disk I/O phases.

### Pitfall 3: Process.HasExited Race Condition
**What goes wrong:** `process.TotalProcessorTime` throws `InvalidOperationException` if the process has already exited. Checking `HasExited` first doesn't prevent the race.
**Why it happens:** The process can exit between the `HasExited` check and the `TotalProcessorTime` read.
**How to avoid:** Always wrap `TotalProcessorTime` access in try/catch for `InvalidOperationException`. On catch, treat as monitoring complete.
**Warning signs:** Unhandled exceptions when cleaning finishes normally.

### Pitfall 4: GitHub API Rate Limiting
**What goes wrong:** Unauthenticated GitHub API requests are rate-limited to 60 per hour per IP.
**Why it happens:** No auth token, shared IP (NAT, VPN, etc.).
**How to avoid:** Cache the last check result (e.g., don't re-check within 10 minutes). Show a user-friendly message on 403 responses. This is a manual button press, not automatic -- so rate limiting is unlikely to be hit.
**Warning signs:** HttpRequestException with 403 status on "Check for Updates" click.

### Pitfall 5: Test Migration Breaking Existing Coverage
**What goes wrong:** Moving code to sub-ViewModels breaks existing test mock setups because constructor parameters change.
**Why it happens:** Tests construct MainWindowViewModel directly with 8 mock parameters. After decomposition, sub-VMs have different constructor signatures.
**How to avoid:** Migrate tests methodically -- for each sub-VM, create a new test class with the appropriate mocks, copy relevant tests, update assertions. Don't delete old tests until new ones pass.
**Warning signs:** Tests failing to compile after the split.

### Pitfall 6: Cross-VM Observable Chain Disposal
**What goes wrong:** The `canStart` observable combines data from multiple sub-VMs. If subscriptions aren't properly disposed, memory leaks or stale updates occur.
**Why it happens:** `CompositeDisposable` on parent doesn't automatically dispose sub-VM disposables.
**How to avoid:** Each sub-VM manages its own `CompositeDisposable`. Parent's `Dispose()` calls `Dispose()` on all sub-VMs. Cross-VM observables are subscribed in the parent and added to the parent's disposables.
**Warning signs:** Property change notifications continuing after window close.

## Code Examples

### Assembly Version and Build Info Retrieval
```csharp
// Source: .NET BCL documentation
using System.Reflection;
using System.Runtime.InteropServices;

public static class AppInfo
{
    public static string Version
    {
        get
        {
            var assembly = Assembly.GetEntryAssembly();
            var version = assembly?.GetName().Version;
            return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "Unknown";
        }
    }

    public static string InformationalVersion
    {
        get
        {
            var assembly = Assembly.GetEntryAssembly();
            var attr = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            return attr?.InformationalVersion ?? Version;
        }
    }

    public static string DotNetVersion => RuntimeInformation.FrameworkDescription;

    public static string AvaloniaVersion
    {
        get
        {
            var avaloniaAssembly = typeof(Avalonia.Application).Assembly;
            var version = avaloniaAssembly.GetName().Version;
            return version?.ToString() ?? "Unknown";
        }
    }

    public static string ReactiveUIVersion
    {
        get
        {
            var rxuiAssembly = typeof(ReactiveUI.ReactiveObject).Assembly;
            var version = rxuiAssembly.GetName().Version;
            return version?.ToString() ?? "Unknown";
        }
    }
}
```

### Inline Hang Warning Banner in Progress Window XAML
```xml
<!-- Hang Detection Warning Banner -->
<Border IsVisible="{Binding IsHangWarningVisible}"
        Background="#FFF3E0"
        BorderBrush="#E65100"
        BorderThickness="1"
        CornerRadius="4"
        Padding="10,8"
        Margin="0,8,0,0">
    <Grid ColumnDefinitions="Auto,*,Auto,Auto">
        <TextBlock Grid.Column="0"
                   Text="&#x26A0;"
                   FontSize="16"
                   VerticalAlignment="Center"
                   Margin="0,0,8,0" />
        <TextBlock Grid.Column="1"
                   Text="xEdit appears to be unresponsive"
                   TextWrapping="Wrap"
                   VerticalAlignment="Center"
                   Foreground="#BF360C" />
        <Button Grid.Column="2"
                Content="Wait"
                Command="{Binding DismissHangWarningCommand}"
                Padding="8,3"
                Margin="5,0"
                VerticalAlignment="Center" />
        <Button Grid.Column="3"
                Content="Kill"
                Command="{Binding KillHungProcessCommand}"
                Padding="8,3"
                VerticalAlignment="Center" />
    </Grid>
</Border>
```

### Startup Logging Pattern
```csharp
// In App.axaml.cs or a startup service
public void LogStartupInfo(ILoggingService logger, IStateService stateService, IConfigurationService configService)
{
    var state = stateService.CurrentState;
    var config = configService.LoadUserConfigAsync().GetAwaiter().GetResult();

    logger.Information("=== AutoQAC Session Start ===");
    logger.Information("Version: {Version}", AppInfo.Version);
    logger.Information(".NET Runtime: {Runtime}", AppInfo.DotNetVersion);
    logger.Information("xEdit Path: {XEditPath}", state.XEditExecutablePath ?? "(not configured)");
    logger.Information("Game Type: {GameType}", state.CurrentGameType);
    logger.Information("MO2 Mode: {Mo2Mode}", state.Mo2ModeEnabled);
    logger.Information("Load Order: {PluginCount} plugins", state.PluginsToClean.Count);
}
```

### Session Summary Logging Pattern
```csharp
// After cleaning session completes (in orchestrator or ViewModel)
public void LogSessionSummary(ILoggingService logger, CleaningSessionResult session)
{
    logger.Information("=== AutoQAC Session Complete ===");
    logger.Information("Duration: {Duration}", session.Duration);
    logger.Information("Plugins processed: {Total} (Cleaned: {Cleaned}, Skipped: {Skipped}, Failed: {Failed})",
        session.PluginResults.Count,
        session.TotalCleaned,
        session.TotalSkipped,
        session.TotalFailed);
    logger.Information("ITMs removed: {Itm}, UDRs fixed: {Udr}, Navmeshes: {Nav}",
        session.TotalItemsRemoved,
        session.TotalItemsUndeleted,
        session.TotalPartialFormsCreated);
    if (session.WasCancelled)
        logger.Information("Session was cancelled by user");
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| PerformanceCounter for CPU | Process.TotalProcessorTime | .NET Core era | Cross-platform, no admin rights needed |
| Assembly.GetExecutingAssembly() | Assembly.GetEntryAssembly() | .NET Core | GetExecutingAssembly returns the calling assembly, not the app |
| HttpWebRequest | HttpClient | .NET 5+ | HttpClient is pooled, async-native |
| Environment.Version | RuntimeInformation.FrameworkDescription | .NET 5+ | Returns full ".NET X.Y.Z" string instead of just version number |

**Deprecated/outdated:**
- `PerformanceCounter`: Windows-only, requires elevated permissions in some scenarios
- `HttpWebRequest`: Replaced by HttpClient in modern .NET
- `Assembly.GetExecutingAssembly().GetName().Version`: Can return wrong assembly in DI/plugin scenarios

## Open Questions

1. **CPU Threshold Calibration**
   - What we know: User specified 60 seconds of near-zero CPU as threshold. Process.TotalProcessorTime is the right API.
   - What's unclear: What counts as "near-zero" exactly -- xEdit doing disk I/O might show very low CPU but not be hung. Need empirical validation.
   - Recommendation: Start with 0.5% CPU threshold (effectively zero). If false positives occur, the auto-dismiss handles it gracefully. Document the threshold as a constant for easy tuning.

2. **GitHub Repository URL for Update Check**
   - What we know: GitHub API endpoint pattern is `https://api.github.com/repos/{owner}/{repo}/releases/latest`
   - What's unclear: The actual repository owner/name for this project.
   - Recommendation: Use a configuration constant or read from assembly metadata. Hard-code the URL during implementation; it can be made configurable later.

3. **Build Date Embedding**
   - What we know: User wants build date in About dialog. .NET no longer embeds deterministic build timestamps by default (deterministic builds since .NET 5).
   - What's unclear: Whether to use MSBuild-injected timestamp or last-modified date.
   - Recommendation: Use an MSBuild property in the .csproj to embed build date as `AssemblyMetadataAttribute`. Example: `<AssemblyMetadata Include="BuildDate" Value="$([System.DateTime]::UtcNow.ToString('yyyy-MM-dd'))" />`

4. **Hang Detection Service Lifetime**
   - What we know: The hang detection needs to monitor a specific process instance during cleaning.
   - What's unclear: Whether it should be an IDisposable singleton service or created per-cleaning-session.
   - Recommendation: Singleton service with `StartMonitoring(process)` / `StopMonitoring()` methods. The observable pattern handles lifecycle naturally -- subscribe when cleaning starts, dispose subscription when cleaning ends.

## Sources

### Primary (HIGH confidence)
- **Codebase analysis**: Direct reading of MainWindowViewModel.cs (1186 lines), ProgressViewModel.cs, CleaningOrchestrator.cs, ProcessExecutionService.cs, and all related interfaces
- **[Process.TotalProcessorTime - Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.totalprocessortime)**: CPU monitoring API documentation
- **[GitHub REST API Releases](https://docs.github.com/en/rest/releases/releases)**: Release endpoint documentation
- **[Assembly versioning - Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/standard/assembly/versioning)**: Version metadata best practices
- **[ReactiveUI WhenActivated](https://www.reactiveui.net/docs/handbook/when-activated)**: ViewModel activation patterns

### Secondary (MEDIUM confidence)
- **[Getting CPU usage in .NET Core](https://medium.com/@jackwild/getting-cpu-usage-in-net-core-7ef825831b8b)**: CPU delta calculation pattern
- **[Avalonia Dialogs Discussion](https://github.com/AvaloniaUI/Avalonia/discussions/12551)**: Dialog patterns for Avalonia MVVM
- **[ReactiveUI ViewModels](https://www.reactiveui.net/docs/handbook/view-models/)**: Sub-ViewModel composition patterns

### Tertiary (LOW confidence)
- CPU "near-zero" threshold (0.5%) is an educated guess pending empirical validation

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - all libraries already in project, no new dependencies
- Architecture (ViewModel decomposition): HIGH - based on direct analysis of 1186-line ViewModel with clear functional boundaries
- Architecture (Hang detection): MEDIUM - CPU monitoring pattern is well-understood but threshold calibration needs empirical validation per STATE.md blocker
- Architecture (About dialog): HIGH - straightforward dialog with well-known .NET APIs
- Architecture (Logging improvements): HIGH - extending existing Serilog infrastructure with additional log statements
- Pitfalls: HIGH - based on project-specific analysis of compiled bindings, test structure, and Process API behavior

**Research date:** 2026-02-07
**Valid until:** 2026-03-07 (stable domain, no fast-moving dependencies)
