# AutoQAC C# Feature Parity Roadmap

**Version:** 1.0
**Date:** 2025-11-17
**Status:** Foundation Stage → Feature Parity

This document outlines the complete path from the current foundation stage to achieving feature parity with the Python/Qt reference implementation in `Code_To_Port/`.

---

## Table of Contents

1. [Current State Summary](#current-state-summary)
2. [Feature Parity Goals](#feature-parity-goals)
3. [Implementation Phases](#implementation-phases)
4. [Phase 1: Core Infrastructure](#phase-1-core-infrastructure)
5. [Phase 2: Configuration System](#phase-2-configuration-system)
6. [Phase 3: Business Logic Layer](#phase-3-business-logic-layer)
7. [Phase 4: xEdit Integration](#phase-4-xedit-integration)
8. [Phase 5: UI Implementation](#phase-5-ui-implementation)
9. [Phase 6: Advanced Features](#phase-6-advanced-features)
10. [Phase 7: Testing & Quality](#phase-7-testing--quality)
11. [Phase 8: Polish & Deployment](#phase-8-polish--deployment)
12. [Dependencies & Prerequisites](#dependencies--prerequisites)
13. [Success Criteria](#success-criteria)

---

## Current State Summary

### What Exists (Complete)
- ✅ Avalonia 11.3.8 project structure
- ✅ ReactiveUI integration
- ✅ MVVM foundation (ViewModelBase, ViewLocator)
- ✅ .NET 8 with nullable reference types
- ✅ FluentTheme styling
- ✅ xUnit test project scaffolding

### What's Missing (Critical Gaps)
- ❌ **Models directory**: Completely empty
- ❌ **Services layer**: No implementations
- ❌ **Business logic**: No cleaning, detection, or configuration code
- ❌ **UI components**: Only placeholder MainWindow
- ❌ **Dependencies**: Missing YamlDotNet, logging framework
- ❌ **Tests**: No actual test implementations

### Reference Implementation Features
The Python/Qt implementation has **25+ major features** across **30+ source files** totaling **4,000+ lines of production code**. See detailed analysis in sections below.

---

## Feature Parity Goals

### Primary Goal
Create a C# Avalonia application that can clean Bethesda game plugins with the same functionality, reliability, and user experience as the Python/Qt reference implementation.

### Specific Targets
1. **Functional Parity**: All core features from Python implementation
2. **Architecture Parity**: MVVM pattern with proper separation of concerns
3. **Performance Parity**: Sequential processing, responsive UI, proper async patterns
4. **UX Parity**: Similar workflow, dialogs, progress tracking
5. **Reliability Parity**: Equivalent error handling, logging, thread safety

### Non-Goals (Out of Scope)
- Python code translation (we're porting concepts, not code)
- Backwards compatibility with Python implementation
- Support for Python configuration file formats (we use C# conventions)

---

## Implementation Phases

### Phase Overview

| Phase | Focus Area | Estimated Effort | Dependencies |
|-------|-----------|------------------|--------------|
| 1 | Core Infrastructure | 2-3 sessions | NuGet packages |
| 2 | Configuration System | 3-4 sessions | Phase 1 |
| 3 | Business Logic Layer | 4-5 sessions | Phase 2 |
| 4 | xEdit Integration | 3-4 sessions | Phase 3 |
| 5 | UI Implementation | 4-5 sessions | Phase 4 |
| 6 | Advanced Features | 3-4 sessions | Phase 5 |
| 7 | Testing & Quality | 4-5 sessions | All phases |
| 8 | Polish & Deployment | 2-3 sessions | Phase 7 |

**Total Estimated Effort:** 25-33 development sessions

---

## Phase 1: Core Infrastructure

**Goal:** Establish foundational services, models, and patterns that all other phases depend on.

### 1.1 NuGet Package Installation

**Priority:** CRITICAL
**Effort:** 1 hour
**Files:** `AutoQAC.csproj`, `AutoQAC.Tests.csproj`

**Packages to Add:**
```xml
<!-- Main Project -->
<PackageReference Include="YamlDotNet" Version="16.2.0" />
<PackageReference Include="Serilog" Version="4.2.0" />
<PackageReference Include="Serilog.Sinks.File" Version="6.1.0" />
<PackageReference Include="Serilog.Sinks.Console" Version="6.1.0" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.1" />

<!-- Test Project -->
<PackageReference Include="FluentAssertions" Version="6.12.2" />
<PackageReference Include="Moq" Version="4.20.72" />
```

**Reference Files:**
- None (new infrastructure)

**Success Criteria:**
- All packages install without conflicts
- Build succeeds
- No runtime errors on application startup

---

### 1.2 Logging Infrastructure

**Priority:** CRITICAL
**Effort:** 2-3 hours
**Files:**
- `AutoQAC/Infrastructure/Logging/LoggingService.cs`
- `AutoQAC/Infrastructure/Logging/ILoggingService.cs`

**Python Reference:**
- `Code_To_Port/AutoQACLib/Core/logging_config.py` (100+ lines)

**Implementation Details:**
```csharp
// ILoggingService interface
public interface ILoggingService
{
    void Debug(string message, params object[] args);
    void Information(string message, params object[] args);
    void Warning(string message, params object[] args);
    void Error(Exception? ex, string message, params object[] args);
    void Fatal(Exception? ex, string message, params object[] args);
}

// LoggingService with Serilog
public class LoggingService : ILoggingService
{
    // Configuration:
    // - Rotating file: logs/autoqac-.log
    // - File size: 5MB per file
    // - Retention: 5 backup files
    // - Console: WARNING and above only
    // - Format: [timestamp] [level] message
}
```

**Success Criteria:**
- Log files created in `logs/` directory
- Log rotation works (test with large logs)
- Thread-safe logging (no corruption under concurrent writes)
- Structured logging with timestamps and levels

---

### 1.3 Dependency Injection Setup

**Priority:** HIGH
**Effort:** 2-3 hours
**Files:**
- `AutoQAC/App.axaml.cs` (modify)
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` (new)

**Python Reference:**
- `Code_To_Port/AutoQAC_Interface.py` (manual DI pattern)

**Implementation Details:**
```csharp
// In App.axaml.cs
public class App : Application
{
    public IServiceProvider? Services { get; private set; }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();

        // Register services
        services.AddLogging();
        services.AddConfiguration();
        services.AddBusinessLogic();
        services.AddViewModels();

        Services = services.BuildServiceProvider();

        // Create MainWindow with DI
        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.DataContext = Services.GetRequiredService<MainWindowViewModel>();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
```

**Success Criteria:**
- Services registered and resolved correctly
- ViewModels receive injected dependencies
- No circular dependencies
- Proper service lifetimes (singleton, transient, scoped)

---

### 1.4 Base Model Classes

**Priority:** HIGH
**Effort:** 2-3 hours
**Files:**
- `AutoQAC/Models/CleaningResult.cs`
- `AutoQAC/Models/GameType.cs`
- `AutoQAC/Models/PluginInfo.cs`

**Python Reference:**
- `Code_To_Port/AutoQACLib/Core/cleaning_service.py` (CleanResult dataclass)
- `Code_To_Port/AutoQACLib/Utilities/game_detection.py` (game types)

**Implementation Details:**
```csharp
// CleaningResult.cs
public sealed record CleaningResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public CleaningStatus Status { get; init; }
    public TimeSpan Duration { get; init; }
    public CleaningStatistics? Statistics { get; init; }
}

public enum CleaningStatus
{
    Cleaned,
    Skipped,
    Failed
}

public sealed record CleaningStatistics
{
    public int ItemsRemoved { get; init; }
    public int ItemsUndeleted { get; init; }
    public int ItemsSkipped { get; init; }
    public int PartialFormsCreated { get; init; }
}

// GameType.cs
public enum GameType
{
    Unknown,
    Fallout3,
    FalloutNewVegas,
    Fallout4,
    SkyrimSpecialEdition,
    Fallout4VR,
    SkyrimVR
}

// PluginInfo.cs
public sealed record PluginInfo
{
    public required string FileName { get; init; }
    public required string FullPath { get; init; }
    public bool IsInSkipList { get; init; }
    public GameType DetectedGameType { get; init; }
}
```

**Success Criteria:**
- Immutable record types with proper null handling
- Enums cover all supported games
- Models are serializable (for potential future use)
- No business logic in models (pure data)

---

## Phase 2: Configuration System

**Goal:** Implement YAML-based configuration management with thread-safe access.

### 2.1 YAML Configuration Models

**Priority:** CRITICAL
**Effort:** 3-4 hours
**Files:**
- `AutoQAC/Models/Configuration/MainConfiguration.cs`
- `AutoQAC/Models/Configuration/UserConfiguration.cs`
- `AutoQAC/Models/Configuration/GameConfiguration.cs`

**Python Reference:**
- `Code_To_Port/AutoQACLib/Core/config_manager.py`
- `Code_To_Port/AutoQAC Main.yaml`
- `Code_To_Port/AutoQAC Config.yaml`

**Implementation Details:**
```csharp
// MainConfiguration.cs (maps to AutoQAC Main.yaml)
public sealed class MainConfiguration
{
    [YamlMember(Alias = "PACT_Data")]
    public PactData Data { get; set; } = new();
}

public sealed class PactData
{
    [YamlMember(Alias = "version")]
    public string Version { get; set; } = string.Empty;

    [YamlMember(Alias = "XEdit_Lists")]
    public Dictionary<string, List<string>> XEditLists { get; set; } = new();

    [YamlMember(Alias = "Skip_Lists")]
    public Dictionary<string, List<string>> SkipLists { get; set; } = new();
}

// UserConfiguration.cs (maps to AutoQAC Config.yaml)
public sealed class UserConfiguration
{
    [YamlMember(Alias = "Load_Order")]
    public LoadOrderConfig LoadOrder { get; set; } = new();

    [YamlMember(Alias = "Mod_Organizer")]
    public ModOrganizerConfig ModOrganizer { get; set; } = new();

    [YamlMember(Alias = "xEdit")]
    public XEditConfig XEdit { get; set; } = new();

    [YamlMember(Alias = "PACT_Settings")]
    public PactSettings Settings { get; set; } = new();
}

public sealed class PactSettings
{
    [YamlMember(Alias = "Journal_Expiration")]
    public int JournalExpiration { get; set; } = 7;

    [YamlMember(Alias = "Cleaning_Timeout")]
    public int CleaningTimeout { get; set; } = 300;

    [YamlMember(Alias = "CPU_Threshold")]
    public int CpuThreshold { get; set; } = 5;

    [YamlMember(Alias = "MO2Mode")]
    public bool MO2Mode { get; set; }

    [YamlMember(Alias = "Max_Concurrent_Subprocesses")]
    public int MaxConcurrentSubprocesses { get; set; } = 3;
}
```

**Success Criteria:**
- YamlDotNet deserializes both config files correctly
- Handles missing keys with defaults
- Preserves YAML structure on write (round-trip)
- Proper null handling with nullable reference types

---

### 2.2 Configuration Service

**Priority:** CRITICAL
**Effort:** 4-5 hours
**Files:**
- `AutoQAC/Services/Configuration/IConfigurationService.cs`
- `AutoQAC/Services/Configuration/ConfigurationService.cs`

**Python Reference:**
- `Code_To_Port/AutoQACLib/Core/config_manager.py` (252 lines)

**Implementation Details:**
```csharp
public interface IConfigurationService
{
    // Configuration loading
    Task<MainConfiguration> LoadMainConfigAsync(CancellationToken ct = default);
    Task<UserConfiguration> LoadUserConfigAsync(CancellationToken ct = default);

    // Configuration saving
    Task SaveUserConfigAsync(UserConfiguration config, CancellationToken ct = default);

    // Path validation
    Task<bool> ValidatePathsAsync(UserConfiguration config, CancellationToken ct = default);

    // Game-specific queries
    List<string> GetSkipList(GameType gameType);
    List<string> GetXEditExecutableNames(GameType gameType);

    // Reactive configuration changes
    IObservable<UserConfiguration> UserConfigurationChanged { get; }
}

public sealed class ConfigurationService : IConfigurationService
{
    private readonly ILoggingService _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly Subject<UserConfiguration> _configChanges = new();

    private const string ConfigDirectory = "AutoQAC Data";
    private const string MainConfigFile = "AutoQAC Main.yaml";
    private const string UserConfigFile = "AutoQAC Config.yaml";

    // Thread-safe YAML I/O with async file operations
    // Caching with file modification time tracking
    // Auto-create config directory if missing
}
```

**Success Criteria:**
- Thread-safe file access (no corruption under concurrent access)
- Async I/O operations (never blocks UI thread)
- Graceful error handling (missing files, invalid YAML, permissions)
- Observable pattern for config changes (ReactiveUI integration)
- Proper disposal of resources (SemaphoreSlim, Subject)

---

### 2.3 Application State Management

**Priority:** CRITICAL
**Effort:** 5-6 hours
**Files:**
- `AutoQAC/Models/AppState.cs`
- `AutoQAC/Services/State/IStateService.cs`
- `AutoQAC/Services/State/StateService.cs`

**Python Reference:**
- `Code_To_Port/AutoQACLib/Core/state_manager.py` (356 lines)

**Implementation Details:**
```csharp
// AppState.cs - Immutable state snapshot
public sealed record AppState
{
    // Configuration paths
    public string? LoadOrderPath { get; init; }
    public string? MO2ExecutablePath { get; init; }
    public string? XEditExecutablePath { get; init; }

    // Configuration validity
    public bool IsLoadOrderConfigured { get; init; }
    public bool IsMO2Configured { get; init; }
    public bool IsXEditConfigured { get; init; }

    // Runtime state
    public bool IsCleaning { get; init; }
    public string? CurrentPlugin { get; init; }
    public string? CurrentOperation { get; init; }

    // Progress
    public int Progress { get; init; }
    public int TotalPlugins { get; init; }
    public List<string> PluginsToClean { get; init; } = new();

    // Results
    public HashSet<string> CleanedPlugins { get; init; } = new();
    public HashSet<string> FailedPlugins { get; init; } = new();
    public HashSet<string> SkippedPlugins { get; init; } = new();

    // Settings
    public int CleaningTimeout { get; init; } = 300;
    public bool MO2ModeEnabled { get; init; }
    public bool PartialFormsEnabled { get; init; }
    public GameType CurrentGameType { get; init; } = GameType.Unknown;
}

// IStateService - Reactive state container
public interface IStateService
{
    // Current state
    AppState CurrentState { get; }

    // State updates
    void UpdateState(Func<AppState, AppState> updateFunc);
    void UpdateConfigurationPaths(string? loadOrder, string? mo2, string? xEdit);
    void StartCleaning(List<string> plugins);
    void FinishCleaning();
    void AddCleaningResult(string plugin, CleaningStatus status);
    void UpdateProgress(int current, int total);

    // Reactive observables
    IObservable<AppState> StateChanged { get; }
    IObservable<bool> ConfigurationValidChanged { get; }
    IObservable<(int current, int total)> ProgressChanged { get; }
    IObservable<(string plugin, CleaningStatus status)> PluginProcessed { get; }
}

// StateService - Thread-safe implementation
public sealed class StateService : IStateService, IDisposable
{
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly Subject<AppState> _stateChanged = new();
    private AppState _currentState = new();

    // All state mutations are atomic (write lock)
    // All reads use read lock (multiple concurrent readers)
    // Observables emit after lock release (no deadlocks)
}
```

**Success Criteria:**
- Thread-safe state access (no race conditions)
- Immutable state snapshots (no external mutation)
- Reactive observables work correctly with ReactiveUI
- No deadlocks under concurrent access
- Proper disposal of locks and observables

---

## Phase 3: Business Logic Layer

**Goal:** Implement core cleaning, detection, and validation logic without UI dependencies.

### 3.1 Game Detection Service

**Priority:** HIGH
**Effort:** 4-5 hours
**Files:**
- `AutoQAC/Services/GameDetection/IGameDetectionService.cs`
- `AutoQAC/Services/GameDetection/GameDetectionService.cs`

**Python Reference:**
- `Code_To_Port/AutoQACLib/Utilities/game_detection.py` (200+ lines)

**Implementation Details:**
```csharp
public interface IGameDetectionService
{
    // Detect from xEdit executable name
    GameType DetectFromExecutable(string executablePath);

    // Detect from load order file (first master)
    Task<GameType> DetectFromLoadOrderAsync(string loadOrderPath, CancellationToken ct = default);

    // Validate game type detection
    bool IsValidGameType(GameType gameType);

    // Get game-specific information
    string GetGameDisplayName(GameType gameType);
    string GetDefaultLoadOrderFileName(GameType gameType);
}

public sealed class GameDetectionService : IGameDetectionService
{
    private static readonly Dictionary<string, GameType> ExecutablePatterns = new()
    {
        { "fo3edit", GameType.Fallout3 },
        { "fnvedit", GameType.FalloutNewVegas },
        { "fo4edit", GameType.Fallout4 },
        { "sseedit", GameType.SkyrimSpecialEdition },
        { "tes5edit", GameType.SkyrimSpecialEdition },
        { "fo4vredit", GameType.Fallout4VR },
        { "skyrimvredit", GameType.SkyrimVR },
    };

    private static readonly Dictionary<string, GameType> MasterFilePatterns = new()
    {
        { "Skyrim.esm", GameType.SkyrimSpecialEdition },
        { "Fallout3.esm", GameType.Fallout3 },
        { "FalloutNV.esm", GameType.FalloutNewVegas },
        { "Fallout4.esm", GameType.Fallout4 },
    };

    // Case-insensitive pattern matching
    // UTF-8 load order file reading with error handling
}
```

**Success Criteria:**
- Detects all supported games correctly
- Handles universal xEdit executables (fallback to load order)
- Graceful handling of invalid/missing files
- Case-insensitive matching (FO4Edit.exe vs fo4edit.exe)

---

### 3.2 Plugin Validation Service

**Priority:** HIGH
**Effort:** 3-4 hours
**Files:**
- `AutoQAC/Services/Plugin/IPluginValidationService.cs`
- `AutoQAC/Services/Plugin/PluginValidationService.cs`

**Python Reference:**
- `Code_To_Port/AutoQACLib/Utilities/plugin_validator.py` (80+ lines)

**Implementation Details:**
```csharp
public interface IPluginValidationService
{
    // Extract plugins from load order file
    Task<List<PluginInfo>> GetPluginsFromLoadOrderAsync(
        string loadOrderPath,
        CancellationToken ct = default);

    // Filter plugins by skip list
    List<PluginInfo> FilterSkippedPlugins(
        List<PluginInfo> plugins,
        List<string> skipList);

    // Validate plugin file exists
    bool ValidatePluginExists(PluginInfo plugin);
}

public sealed class PluginValidationService : IPluginValidationService
{
    private readonly ILoggingService _logger;

    // Read load order:
    //   - Skip comment lines (starting with #)
    //   - Handle both formats: "plugin.esp" and "*plugin.esp" (enabled marker)
    //   - Trim whitespace
    //   - UTF-8 encoding with error handling
    //   - Return PluginInfo list with full paths

    // Skip list matching:
    //   - Case-insensitive comparison
    //   - Exact filename match
    //   - Log skipped plugins
}
```

**Success Criteria:**
- Correctly parses plugins.txt and loadorder.txt formats
- Handles enabled/disabled plugin markers (`*`)
- Skips comment lines
- Case-insensitive skip list matching
- UTF-8 encoding support

---

### 3.3 Cleaning Service (Core)

**Priority:** CRITICAL
**Effort:** 6-8 hours
**Files:**
- `AutoQAC/Services/Cleaning/ICleaningService.cs`
- `AutoQAC/Services/Cleaning/CleaningService.cs`

**Python Reference:**
- `Code_To_Port/AutoQACLib/Core/cleaning_service.py` (451+ lines)

**Implementation Details:**
```csharp
public interface ICleaningService
{
    // Main cleaning entry point
    Task<CleaningResult> CleanPluginAsync(
        PluginInfo plugin,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    // Pre-cleaning validation
    Task<bool> ValidateEnvironmentAsync(CancellationToken ct = default);

    // Cancel current operation
    void StopCurrentOperation();
}

public sealed class CleaningService : ICleaningService
{
    private readonly IConfigurationService _configService;
    private readonly IGameDetectionService _gameDetection;
    private readonly IStateService _stateService;
    private readonly ILoggingService _logger;
    private readonly IProcessExecutionService _processService;

    private bool _stopRequested;

    public async Task<CleaningResult> CleanPluginAsync(...)
    {
        // 1. Check stop flag
        if (_stopRequested) return Skipped();

        // 2. Check skip list
        if (plugin.IsInSkipList) return Skipped("In skip list");

        // 3. Detect game type if needed
        var gameType = await DetectGameTypeAsync(...);

        // 4. Build xEdit command
        var command = BuildCleaningCommand(plugin, gameType);

        // 5. Execute subprocess with timeout
        var result = await ExecuteCleaningAsync(command, progress, ct);

        // 6. Parse output
        var statistics = ParseCleaningOutput(result.Output);

        // 7. Return result
        return new CleaningResult { ... };
    }

    private string BuildCleaningCommand(PluginInfo plugin, GameType gameType)
    {
        // Base command: xEdit.exe -QAC -autoexit -autoload "plugin.esp"

        // Add game type flag if universal xEdit
        // Add MO2 wrapper if MO2 mode enabled
        // Add partial forms flags if enabled

        // Special quoting for MO2:
        //   ModOrganizer.exe run xEdit.exe -a "-QAC -autoexit ..."
    }

    private CleaningStatistics ParseCleaningOutput(string output)
    {
        // Regex patterns for xEdit output:
        //   - Undeleting: (.*)  → UDRs restored
        //   - Removing: (.*)    → Items removed
        //   - Skipping: (.*)    → Items skipped
        //   - Making Partial Form: (.*)  → Partial forms created

        // Return statistics object
    }
}
```

**Success Criteria:**
- Builds correct xEdit command for all scenarios
- Executes subprocess with proper timeout handling
- Parses xEdit output accurately
- Reports progress in real-time
- Handles cancellation gracefully
- No parallel execution (sequential only!)

---

### 3.4 Process Execution Service

**Priority:** CRITICAL
**Effort:** 5-6 hours
**Files:**
- `AutoQAC/Services/Process/IProcessExecutionService.cs`
- `AutoQAC/Services/Process/ProcessExecutionService.cs`

**Python Reference:**
- `Code_To_Port/AutoQACLib/Utilities/process_utils.py` (250+ lines)

**Implementation Details:**
```csharp
public interface IProcessExecutionService
{
    // Execute process with real-time output
    Task<ProcessResult> ExecuteAsync(
        ProcessStartInfo startInfo,
        IProgress<string>? outputProgress = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default);

    // Resource limit management
    Task<IDisposable> AcquireProcessSlotAsync(
        CancellationToken ct = default);
}

public sealed record ProcessResult
{
    public int ExitCode { get; init; }
    public List<string> OutputLines { get; init; } = new();
    public List<string> ErrorLines { get; init; } = new();
    public bool TimedOut { get; init; }
}

public sealed class ProcessExecutionService : IProcessExecutionService
{
    private readonly SemaphoreSlim _processSlots;
    private readonly ILoggingService _logger;

    public ProcessExecutionService(IStateService stateService, ILoggingService logger)
    {
        _logger = logger;

        // Initialize semaphore with max concurrent processes
        var maxProcesses = stateService.CurrentState.MaxConcurrentSubprocesses ?? 3;
        _processSlots = new SemaphoreSlim(maxProcesses, maxProcesses);
    }

    public async Task<ProcessResult> ExecuteAsync(...)
    {
        using var processSlot = await AcquireProcessSlotAsync(ct);

        using var process = new Process { StartInfo = startInfo };

        // Redirect standard output and error
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;

        var outputLines = new List<string>();
        var errorLines = new List<string>();

        // Async output reading
        process.OutputDataReceived += (s, e) => {
            if (e.Data != null)
            {
                outputLines.Add(e.Data);
                outputProgress?.Report(e.Data);
            }
        };

        process.ErrorDataReceived += (s, e) => {
            if (e.Data != null) errorLines.Add(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Wait with timeout and cancellation
        var timeoutCts = timeout.HasValue
            ? new CancellationTokenSource(timeout.Value)
            : null;
        var linkedCts = timeoutCts != null
            ? CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token)
            : CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Timeout or cancellation
            await TerminateProcessGracefullyAsync(process);

            return new ProcessResult
            {
                ExitCode = -1,
                OutputLines = outputLines,
                ErrorLines = errorLines,
                TimedOut = timeoutCts?.IsCancellationRequested ?? false
            };
        }

        return new ProcessResult
        {
            ExitCode = process.ExitCode,
            OutputLines = outputLines,
            ErrorLines = errorLines
        };
    }

    private async Task TerminateProcessGracefullyAsync(Process process)
    {
        if (process.HasExited) return;

        // Try graceful termination first
        try
        {
            process.CloseMainWindow();
            await Task.Delay(2000);
        }
        catch { }

        // Force kill if still running
        if (!process.HasExited)
        {
            try
            {
                process.Kill();
            }
            catch { }
        }
    }
}
```

**Success Criteria:**
- Async process execution (never blocks UI)
- Real-time output streaming
- Proper timeout handling (graceful → forced termination)
- Resource limit enforcement (max concurrent processes)
- Cancellation support
- No resource leaks (proper disposal)

---

## Phase 4: xEdit Integration

**Goal:** Complete xEdit subprocess management with all flags and modes.

### 4.1 xEdit Command Builder

**Priority:** HIGH
**Effort:** 3-4 hours
**Files:**
- `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`

**Python Reference:**
- `Code_To_Port/AutoQACLib/Core/cleaning_service.py` (_build_cleaning_command)

**Implementation Details:**
```csharp
public sealed class XEditCommandBuilder
{
    private readonly IConfigurationService _configService;
    private readonly IStateService _stateService;

    public ProcessStartInfo BuildCommand(PluginInfo plugin, GameType gameType)
    {
        var config = _stateService.CurrentState;
        var xEditPath = config.XEditExecutablePath!;

        var args = new List<string>();

        // Add game type flag if universal xEdit
        if (IsUniversalXEdit(xEditPath))
        {
            args.Add(GetGameFlag(gameType));
        }

        // Core cleaning flags
        args.Add("-QAC");
        args.Add("-autoexit");
        args.Add($"-autoload \"{plugin.FileName}\"");

        // Partial forms (experimental)
        if (config.PartialFormsEnabled)
        {
            args.Add("-iknowwhatimdoing");
            args.Add("-allowmakepartial");
        }

        // Build ProcessStartInfo
        if (config.MO2ModeEnabled)
        {
            return BuildMO2Command(xEditPath, args);
        }
        else
        {
            return new ProcessStartInfo
            {
                FileName = xEditPath,
                Arguments = string.Join(" ", args),
                WorkingDirectory = Path.GetDirectoryName(xEditPath)
            };
        }
    }

    private ProcessStartInfo BuildMO2Command(string xEditPath, List<string> args)
    {
        // MO2 special quoting:
        //   ModOrganizer.exe run xEdit.exe -a "xEdit args here"

        var mo2Path = _stateService.CurrentState.MO2ExecutablePath!;
        var xEditArgs = string.Join(" ", args);

        return new ProcessStartInfo
        {
            FileName = mo2Path,
            Arguments = $"run \"{xEditPath}\" -a \"{xEditArgs}\"",
            WorkingDirectory = Path.GetDirectoryName(mo2Path)
        };
    }

    private static string GetGameFlag(GameType gameType) => gameType switch
    {
        GameType.Fallout3 => "-FO3",
        GameType.FalloutNewVegas => "-FNV",
        GameType.Fallout4 => "-FO4",
        GameType.SkyrimSpecialEdition => "-SSE",
        GameType.Fallout4VR => "-FO4VR",
        GameType.SkyrimVR => "-SkyrimVR",
        _ => string.Empty
    };
}
```

**Success Criteria:**
- Correct command generation for all game types
- MO2 wrapper with proper quoting
- Partial forms flags only for SSE/FO4
- Universal xEdit gets game type flag

---

### 4.2 xEdit Output Parser

**Priority:** HIGH
**Effort:** 2-3 hours
**Files:**
- `AutoQAC/Services/Cleaning/XEditOutputParser.cs`

**Python Reference:**
- `Code_To_Port/AutoQACLib/Core/cleaning_service.py` (_parse_cleaning_output)

**Implementation Details:**
```csharp
public sealed class XEditOutputParser
{
    private static readonly Regex UndeletedPattern =
        new(@"Undeleting:\s*(.*)", RegexOptions.Compiled);
    private static readonly Regex RemovedPattern =
        new(@"Removing:\s*(.*)", RegexOptions.Compiled);
    private static readonly Regex SkippedPattern =
        new(@"Skipping:\s*(.*)", RegexOptions.Compiled);
    private static readonly Regex PartialFormsPattern =
        new(@"Making Partial Form:\s*(.*)", RegexOptions.Compiled);

    public CleaningStatistics ParseOutput(List<string> outputLines)
    {
        int undeleted = 0;
        int removed = 0;
        int skipped = 0;
        int partialForms = 0;

        foreach (var line in outputLines)
        {
            if (UndeletedPattern.IsMatch(line)) undeleted++;
            else if (RemovedPattern.IsMatch(line)) removed++;
            else if (SkippedPattern.IsMatch(line)) skipped++;
            else if (PartialFormsPattern.IsMatch(line)) partialForms++;
        }

        return new CleaningStatistics
        {
            ItemsRemoved = removed,
            ItemsUndeleted = undeleted,
            ItemsSkipped = skipped,
            PartialFormsCreated = partialForms
        };
    }

    public bool IsCompletionLine(string line)
    {
        return line.Contains("Done.") ||
               line.Contains("Cleaning completed");
    }
}
```

**Success Criteria:**
- Accurately counts all cleaning operations
- Handles malformed output gracefully
- Detects completion correctly
- Performance: O(n) single pass

---

## Phase 5: UI Implementation

**Goal:** Create functional Avalonia UI with MVVM data binding.

### 5.1 MainWindow Layout

**Priority:** HIGH
**Effort:** 4-5 hours
**Files:**
- `AutoQAC/Views/MainWindow.axaml` (complete rewrite)
- `AutoQAC/Views/MainWindow.axaml.cs` (minimal changes)

**Python Reference:**
- `Code_To_Port/AutoQACLib/UI/main_window.py`
- All mixin files in `Code_To_Port/AutoQACLib/UI/mixins/`

**Implementation Details:**
```xaml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:AutoQAC.ViewModels"
        x:Class="AutoQAC.Views.MainWindow"
        x:DataType="vm:MainWindowViewModel"
        Title="AutoQAC - Plugin Auto Cleaning Tool"
        Width="900" Height="600"
        MinWidth="700" MinHeight="400"
        Icon="/Assets/avalonia-logo.ico">

    <Design.DataContext>
        <vm:MainWindowViewModel />
    </Design.DataContext>

    <DockPanel>
        <!-- Menu Bar -->
        <Menu DockPanel.Dock="Top">
            <MenuItem Header="_File">
                <MenuItem Header="_Exit" Command="{Binding ExitCommand}" />
            </MenuItem>
            <MenuItem Header="_Help">
                <MenuItem Header="_About" Command="{Binding ShowAboutCommand}" />
            </MenuItem>
        </Menu>

        <!-- Status Bar -->
        <StatusBar DockPanel.Dock="Bottom">
            <StatusBarItem>
                <TextBlock Text="{Binding StatusText}" />
            </StatusBarItem>
        </StatusBar>

        <!-- Main Content -->
        <Grid Margin="10" RowDefinitions="Auto,*,Auto">

            <!-- Configuration Panel -->
            <StackPanel Grid.Row="0" Spacing="10">
                <TextBlock Text="Configuration"
                           FontSize="16" FontWeight="Bold" />

                <Grid ColumnDefinitions="120,*,Auto">
                    <TextBlock Grid.Column="0" Text="Load Order:"
                               VerticalAlignment="Center" />
                    <TextBox Grid.Column="1"
                             Text="{Binding LoadOrderPath}"
                             IsReadOnly="True"
                             Watermark="Not configured" />
                    <Button Grid.Column="2"
                            Content="Browse..."
                            Command="{Binding ConfigureLoadOrderCommand}"
                            Margin="5,0,0,0" />
                </Grid>

                <Grid ColumnDefinitions="120,*,Auto">
                    <TextBlock Grid.Column="0" Text="xEdit:"
                               VerticalAlignment="Center" />
                    <TextBox Grid.Column="1"
                             Text="{Binding XEditPath}"
                             IsReadOnly="True"
                             Watermark="Not configured" />
                    <Button Grid.Column="2"
                            Content="Browse..."
                            Command="{Binding ConfigureXEditCommand}"
                            Margin="5,0,0,0" />
                </Grid>

                <Grid ColumnDefinitions="120,*,Auto">
                    <TextBlock Grid.Column="0" Text="Mod Organizer 2:"
                               VerticalAlignment="Center" />
                    <TextBox Grid.Column="1"
                             Text="{Binding MO2Path}"
                             IsReadOnly="True"
                             Watermark="Optional - for MO2 mode" />
                    <Button Grid.Column="2"
                            Content="Browse..."
                            Command="{Binding ConfigureMO2Command}"
                            Margin="5,0,0,0" />
                </Grid>

                <StackPanel Orientation="Horizontal" Spacing="10">
                    <CheckBox Content="MO2 Mode"
                              IsChecked="{Binding MO2ModeEnabled}" />
                    <CheckBox Content="Partial Forms (Experimental)"
                              IsChecked="{Binding PartialFormsEnabled}"
                              Command="{Binding TogglePartialFormsCommand}" />
                </StackPanel>
            </StackPanel>

            <!-- Plugin List -->
            <Border Grid.Row="1"
                    BorderBrush="{DynamicResource SystemControlForegroundBaseMediumBrush}"
                    BorderThickness="1"
                    CornerRadius="4"
                    Margin="0,10,0,10">
                <Grid RowDefinitions="Auto,*">
                    <TextBlock Grid.Row="0"
                               Text="Plugins to Clean"
                               FontSize="14" FontWeight="SemiBold"
                               Margin="10,5" />

                    <ListBox Grid.Row="1"
                             Items="{Binding PluginsToClean}"
                             SelectedItem="{Binding SelectedPlugin}"
                             Margin="5">
                        <ListBox.ItemTemplate>
                            <DataTemplate>
                                <TextBlock Text="{Binding FileName}" />
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                    </ListBox>
                </Grid>
            </Border>

            <!-- Control Buttons -->
            <StackPanel Grid.Row="2"
                        Orientation="Horizontal"
                        HorizontalAlignment="Right"
                        Spacing="10">
                <Button Content="Start Cleaning"
                        Command="{Binding StartCleaningCommand}"
                        IsEnabled="{Binding CanStartCleaning}"
                        MinWidth="120" />
                <Button Content="Stop"
                        Command="{Binding StopCleaningCommand}"
                        IsEnabled="{Binding IsCleaning}"
                        MinWidth="120" />
            </StackPanel>
        </Grid>
    </DockPanel>
</Window>
```

**Success Criteria:**
- Responsive layout (resize works correctly)
- All controls data-bound to ViewModel
- Fluent theme applied consistently
- Professional appearance matching Python version
- Accessibility support (keyboard navigation, screen readers)

---

### 5.2 MainWindowViewModel

**Priority:** CRITICAL
**Effort:** 6-8 hours
**Files:**
- `AutoQAC/ViewModels/MainWindowViewModel.cs` (complete rewrite)

**Python Reference:**
- `Code_To_Port/AutoQACLib/UI/main_window.py` (all mixins combined)

**Implementation Details:**
```csharp
public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly IConfigurationService _configService;
    private readonly IStateService _stateService;
    private readonly ICleaningOrchestrator _orchestrator;
    private readonly ILoggingService _logger;

    // Observable properties
    private string? _loadOrderPath;
    private string? _xEditPath;
    private string? _mo2Path;
    private bool _mo2ModeEnabled;
    private bool _partialFormsEnabled;
    private string _statusText = "Ready";
    private ObservableCollection<PluginInfo> _pluginsToClean = new();
    private PluginInfo? _selectedPlugin;

    // Computed properties
    private readonly ObservableAsPropertyHelper<bool> _canStartCleaning;
    private readonly ObservableAsPropertyHelper<bool> _isCleaning;

    // Commands
    public ReactiveCommand<Unit, Unit> ConfigureLoadOrderCommand { get; }
    public ReactiveCommand<Unit, Unit> ConfigureXEditCommand { get; }
    public ReactiveCommand<Unit, Unit> ConfigureMO2Command { get; }
    public ReactiveCommand<Unit, Unit> TogglePartialFormsCommand { get; }
    public ReactiveCommand<Unit, Unit> StartCleaningCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCleaningCommand { get; }
    public ReactiveCommand<Unit, Unit> ExitCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowAboutCommand { get; }

    public MainWindowViewModel(
        IConfigurationService configService,
        IStateService stateService,
        ICleaningOrchestrator orchestrator,
        ILoggingService logger)
    {
        _configService = configService;
        _stateService = stateService;
        _orchestrator = orchestrator;
        _logger = logger;

        // Initialize commands
        ConfigureLoadOrderCommand = ReactiveCommand.CreateFromTask(ConfigureLoadOrderAsync);
        ConfigureXEditCommand = ReactiveCommand.CreateFromTask(ConfigureXEditAsync);
        ConfigureMO2Command = ReactiveCommand.CreateFromTask(ConfigureMO2Async);

        TogglePartialFormsCommand = ReactiveCommand.Create(TogglePartialForms);

        var canStart = this.WhenAnyValue(
            x => x.LoadOrderPath,
            x => x.XEditPath,
            x => x.IsCleaning,
            (loadOrder, xEdit, isCleaning) =>
                !string.IsNullOrEmpty(loadOrder) &&
                !string.IsNullOrEmpty(xEdit) &&
                !isCleaning);

        StartCleaningCommand = ReactiveCommand.CreateFromTask(
            StartCleaningAsync,
            canStart);

        StopCleaningCommand = ReactiveCommand.Create(
            StopCleaning,
            this.WhenAnyValue(x => x.IsCleaning));

        ExitCommand = ReactiveCommand.Create(Exit);
        ShowAboutCommand = ReactiveCommand.Create(ShowAbout);

        // Subscribe to state changes
        _stateService.StateChanged
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(OnStateChanged);

        _stateService.ProgressChanged
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(OnProgressChanged);

        // Computed properties
        _canStartCleaning = canStart
            .ToProperty(this, x => x.CanStartCleaning);

        _isCleaning = _stateService.StateChanged
            .Select(s => s.IsCleaning)
            .ToProperty(this, x => x.IsCleaning);
    }

    private async Task ConfigureLoadOrderAsync()
    {
        // Open file dialog
        // Validate file
        // Update state
        // Save configuration
        // Reload plugins
    }

    private async Task StartCleaningAsync()
    {
        // Validate configuration
        // Load plugins
        // Show progress dialog
        // Start orchestrator
    }

    private void OnStateChanged(AppState state)
    {
        LoadOrderPath = state.LoadOrderPath;
        XEditPath = state.XEditExecutablePath;
        MO2Path = state.MO2ExecutablePath;
        MO2ModeEnabled = state.MO2ModeEnabled;
        PartialFormsEnabled = state.PartialFormsEnabled;
    }
}
```

**Success Criteria:**
- All properties implement `RaiseAndSetIfChanged`
- Commands use `ReactiveCommand` with proper `CanExecute`
- State changes propagate to UI automatically
- No code-behind in View
- No direct UI manipulation in ViewModel
- Proper disposal of subscriptions

---

### 5.3 Progress Dialog

**Priority:** HIGH
**Effort:** 3-4 hours
**Files:**
- `AutoQAC/Views/ProgressWindow.axaml`
- `AutoQAC/Views/ProgressWindow.axaml.cs`
- `AutoQAC/ViewModels/ProgressViewModel.cs`

**Python Reference:**
- `Code_To_Port/AutoQACLib/UI/dialogs/cleaning_progress.py`

**Implementation Details:**
```xaml
<Window xmlns="https://github.com/avaloniaui"
        x:Class="AutoQAC.Views.ProgressWindow"
        x:DataType="vm:ProgressViewModel"
        Title="Cleaning Progress"
        Width="600" Height="400"
        CanResize="False"
        WindowStartupLocation="CenterOwner">

    <Grid Margin="20" RowDefinitions="Auto,Auto,*,Auto">

        <!-- Current Plugin -->
        <StackPanel Grid.Row="0" Spacing="5">
            <TextBlock Text="Current Plugin:" FontWeight="SemiBold" />
            <TextBlock Text="{Binding CurrentPlugin}" FontSize="14" />
        </StackPanel>

        <!-- Progress Bar -->
        <StackPanel Grid.Row="1" Spacing="5" Margin="0,10,0,0">
            <Grid ColumnDefinitions="*,Auto">
                <TextBlock Grid.Column="0" Text="Progress:" FontWeight="SemiBold" />
                <TextBlock Grid.Column="1" Text="{Binding ProgressText}" />
            </Grid>
            <ProgressBar Value="{Binding Progress}"
                         Maximum="{Binding Total}"
                         Height="25" />
        </StackPanel>

        <!-- Statistics -->
        <StackPanel Grid.Row="2" Spacing="10" Margin="0,10,0,0">
            <TextBlock Text="Statistics:" FontWeight="SemiBold" />
            <Grid ColumnDefinitions="150,*" RowDefinitions="Auto,Auto,Auto,Auto"
                  RowGap="5">
                <TextBlock Grid.Row="0" Grid.Column="0" Text="Cleaned:" />
                <TextBlock Grid.Row="0" Grid.Column="1"
                           Text="{Binding CleanedCount}" />

                <TextBlock Grid.Row="1" Grid.Column="0" Text="Skipped:" />
                <TextBlock Grid.Row="1" Grid.Column="1"
                           Text="{Binding SkippedCount}" />

                <TextBlock Grid.Row="2" Grid.Column="0" Text="Failed:" />
                <TextBlock Grid.Row="2" Grid.Column="1"
                           Text="{Binding FailedCount}" />

                <TextBlock Grid.Row="3" Grid.Column="0" Text="Total:" />
                <TextBlock Grid.Row="3" Grid.Column="1"
                           Text="{Binding Total}" />
            </Grid>

            <!-- Log Output -->
            <TextBlock Text="Output:" FontWeight="SemiBold" Margin="0,10,0,0" />
            <ScrollViewer Height="150">
                <TextBlock Text="{Binding LogOutput}"
                           FontFamily="Consolas"
                           TextWrapping="Wrap" />
            </ScrollViewer>
        </StackPanel>

        <!-- Stop Button -->
        <Button Grid.Row="3"
                Content="Stop"
                Command="{Binding StopCommand}"
                HorizontalAlignment="Right"
                MinWidth="100"
                Margin="0,10,0,0" />
    </Grid>
</Window>
```

```csharp
public sealed class ProgressViewModel : ViewModelBase
{
    private readonly IStateService _stateService;
    private readonly ICleaningOrchestrator _orchestrator;

    private string? _currentPlugin;
    private int _progress;
    private int _total;
    private int _cleanedCount;
    private int _skippedCount;
    private int _failedCount;
    private string _logOutput = string.Empty;

    public ReactiveCommand<Unit, Unit> StopCommand { get; }

    // Progress text computed property: "5 / 20 (25%)"
    private readonly ObservableAsPropertyHelper<string> _progressText;

    public ProgressViewModel(IStateService stateService, ICleaningOrchestrator orchestrator)
    {
        _stateService = stateService;
        _orchestrator = orchestrator;

        StopCommand = ReactiveCommand.Create(() => _orchestrator.StopCleaning());

        // Subscribe to state changes
        _stateService.StateChanged
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(OnStateChanged);

        _stateService.PluginProcessed
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(OnPluginProcessed);

        // Computed progress text
        _progressText = this.WhenAnyValue(
            x => x.Progress,
            x => x.Total,
            (current, total) => total > 0
                ? $"{current} / {total} ({current * 100 / total}%)"
                : "0 / 0 (0%)")
            .ToProperty(this, x => x.ProgressText);
    }

    private void OnStateChanged(AppState state)
    {
        CurrentPlugin = state.CurrentPlugin;
        Progress = state.Progress;
        Total = state.TotalPlugins;
        CleanedCount = state.CleanedPlugins.Count;
        SkippedCount = state.SkippedPlugins.Count;
        FailedCount = state.FailedPlugins.Count;
    }

    private void OnPluginProcessed((string plugin, CleaningStatus status) args)
    {
        LogOutput += $"[{DateTime.Now:HH:mm:ss}] {args.plugin}: {args.status}\n";
    }
}
```

**Success Criteria:**
- Real-time progress updates
- Non-modal dialog (can interact with main window)
- Accurate statistics
- Scrollable log output
- Stop button works immediately
- No UI freezing

---

### 5.4 File Dialogs

**Priority:** MEDIUM
**Effort:** 2-3 hours
**Files:**
- `AutoQAC/Services/UI/IFileDialogService.cs`
- `AutoQAC/Services/UI/FileDialogService.cs`

**Python Reference:**
- Qt file dialogs in configuration_dialogs.py

**Implementation Details:**
```csharp
public interface IFileDialogService
{
    Task<string?> OpenFileDialogAsync(
        string title,
        string filter,
        string? initialDirectory = null);
}

public sealed class FileDialogService : IFileDialogService
{
    public async Task<string?> OpenFileDialogAsync(
        string title,
        string filter,
        string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            AllowMultiple = false,
            Directory = initialDirectory
        };

        // Parse filter: "Text Files (*.txt)|*.txt"
        if (!string.IsNullOrEmpty(filter))
        {
            dialog.Filters = ParseFilter(filter);
        }

        var result = await dialog.ShowAsync(GetMainWindow());
        return result?.FirstOrDefault();
    }

    private static List<FileDialogFilter> ParseFilter(string filter)
    {
        // Convert Windows-style filter to Avalonia format
        // "Text Files (*.txt)|*.txt|All Files (*.*)|*.*"
        // → [{ Name = "Text Files (*.txt)", Extensions = ["txt"] }, ...]
    }

    private static Window GetMainWindow()
    {
        return Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow!
            : throw new InvalidOperationException("No main window");
    }
}
```

**Success Criteria:**
- Cross-platform file dialogs work
- Initial directory remembered
- File filters applied correctly
- Modal dialog behavior

---

## Phase 6: Advanced Features

**Goal:** Implement MO2 integration, partial forms, and other advanced capabilities.

### 6.1 Cleaning Orchestrator

**Priority:** HIGH
**Effort:** 4-5 hours
**Files:**
- `AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs`
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`

**Python Reference:**
- `Code_To_Port/AutoQACLib/Core/cleaning_worker.py`
- `Code_To_Port/AutoQACLib/UI/gui_controller.py`

**Implementation Details:**
```csharp
public interface ICleaningOrchestrator
{
    // Start cleaning workflow
    Task StartCleaningAsync(CancellationToken ct = default);

    // Stop current operation
    void StopCleaning();

    // Cleaning state
    IObservable<bool> IsCleaningChanged { get; }
}

public sealed class CleaningOrchestrator : ICleaningOrchestrator
{
    private readonly ICleaningService _cleaningService;
    private readonly IPluginValidationService _pluginService;
    private readonly IStateService _stateService;
    private readonly IConfigurationService _configService;
    private readonly ILoggingService _logger;

    private CancellationTokenSource? _cleaningCts;

    public async Task StartCleaningAsync(CancellationToken ct = default)
    {
        try
        {
            _logger.Information("Starting cleaning workflow");

            // 1. Validate configuration
            var isValid = await ValidateConfigurationAsync();
            if (!isValid)
            {
                throw new InvalidOperationException("Configuration is invalid");
            }

            // 2. Load plugins from load order
            var config = _stateService.CurrentState;
            var plugins = await _pluginService.GetPluginsFromLoadOrderAsync(
                config.LoadOrderPath!, ct);

            // 3. Filter skip list
            var skipList = _configService.GetSkipList(config.CurrentGameType);
            var pluginsToClean = _pluginService.FilterSkippedPlugins(plugins, skipList);

            // 4. Update state - cleaning started
            _stateService.StartCleaning(
                pluginsToClean.Select(p => p.FileName).ToList());

            // 5. Create cancellation token
            _cleaningCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            // 6. Process plugins SEQUENTIALLY (CRITICAL!)
            foreach (var plugin in pluginsToClean)
            {
                if (_cleaningCts.Token.IsCancellationRequested)
                {
                    _logger.Information("Cleaning cancelled by user");
                    break;
                }

                _logger.Information("Processing plugin: {Plugin}", plugin.FileName);
                _stateService.UpdateState(s => s with
                {
                    CurrentPlugin = plugin.FileName
                });

                // Progress reporting
                var progress = new Progress<string>(output =>
                {
                    _logger.Debug("xEdit output: {Output}", output);
                });

                // Clean plugin
                var result = await _cleaningService.CleanPluginAsync(
                    plugin,
                    progress,
                    _cleaningCts.Token);

                // Update results
                _stateService.AddCleaningResult(plugin.FileName, result.Status);

                _logger.Information(
                    "Plugin {Plugin} processed: {Status} - {Message}",
                    plugin.FileName,
                    result.Status,
                    result.Message);
            }

            // 7. Finish cleaning
            _stateService.FinishCleaning();
            _logger.Information("Cleaning workflow completed");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during cleaning workflow");
            _stateService.FinishCleaning();
            throw;
        }
        finally
        {
            _cleaningCts?.Dispose();
            _cleaningCts = null;
        }
    }

    public void StopCleaning()
    {
        _logger.Information("Stop requested");
        _cleaningCts?.Cancel();
        _cleaningService.StopCurrentOperation();
    }

    private async Task<bool> ValidateConfigurationAsync()
    {
        var config = _stateService.CurrentState;

        if (string.IsNullOrEmpty(config.LoadOrderPath) ||
            string.IsNullOrEmpty(config.XEditExecutablePath))
        {
            return false;
        }

        return await _cleaningService.ValidateEnvironmentAsync();
    }
}
```

**Success Criteria:**
- Sequential processing enforced (no parallel!)
- Cancellation works immediately
- Progress updates in real-time
- Error handling doesn't stop entire workflow
- Proper state management throughout

---

### 6.2 MO2 Detection & Validation

**Priority:** MEDIUM
**Effort:** 2-3 hours
**Files:**
- `AutoQAC/Services/MO2/IMO2ValidationService.cs`
- `AutoQAC/Services/MO2/MO2ValidationService.cs`

**Python Reference:**
- `Code_To_Port/AutoQACLib/Utilities/configuration_dialogs.py` (_check_mo2_running)

**Implementation Details:**
```csharp
public interface IMO2ValidationService
{
    // Check if MO2 is running
    bool IsMO2Running();

    // Validate MO2 executable
    Task<bool> ValidateMO2ExecutableAsync(string mo2Path);

    // Get warning message if MO2 is running
    string GetMO2RunningWarning();
}

public sealed class MO2ValidationService : IMO2ValidationService
{
    public bool IsMO2Running()
    {
        var processes = Process.GetProcessesByName("ModOrganizer");
        return processes.Length > 0;
    }

    public async Task<bool> ValidateMO2ExecutableAsync(string mo2Path)
    {
        if (!File.Exists(mo2Path))
            return false;

        var fileName = Path.GetFileName(mo2Path).ToLowerInvariant();
        return fileName == "modorganizer.exe";
    }

    public string GetMO2RunningWarning()
    {
        return "Warning: Mod Organizer 2 is currently running. " +
               "For best results, close MO2 before cleaning plugins.";
    }
}
```

**Success Criteria:**
- Detects MO2 process correctly
- Validates MO2 executable path
- Warns user if MO2 is running
- MO2 command wrapper works correctly

---

### 6.3 Partial Forms Warning Dialog

**Priority:** MEDIUM
**Effort:** 2 hours
**Files:**
- `AutoQAC/Views/PartialFormsWarningDialog.axaml`
- `AutoQAC/ViewModels/PartialFormsWarningViewModel.cs`

**Python Reference:**
- `Code_To_Port/AutoQACLib/UI/dialogs/partial_forms.py`

**Implementation Details:**
```xaml
<Window xmlns="https://github.com/avaloniaui"
        x:Class="AutoQAC.Views.PartialFormsWarningDialog"
        Title="Partial Forms Warning"
        Width="500" Height="300"
        CanResize="False"
        WindowStartupLocation="CenterOwner">

    <Grid Margin="20" RowDefinitions="*,Auto">

        <StackPanel Grid.Row="0" Spacing="15">
            <TextBlock Text="⚠ Warning: Experimental Feature"
                       FontSize="16" FontWeight="Bold"
                       Foreground="Red" />

            <TextBlock TextWrapping="Wrap">
                Partial Forms cleaning is an experimental feature that may cause issues:
            </TextBlock>

            <StackPanel Margin="20,0,0,0" Spacing="5">
                <TextBlock Text="• Only works with xEdit 4.1.5b or newer" />
                <TextBlock Text="• Only supported for SSE and FO4" />
                <TextBlock Text="• May cause unexpected behavior" />
                <TextBlock Text="• Always backup your plugins first" />
            </StackPanel>

            <TextBlock TextWrapping="Wrap" FontWeight="SemiBold">
                Do you want to enable Partial Forms cleaning?
            </TextBlock>
        </StackPanel>

        <StackPanel Grid.Row="1"
                    Orientation="Horizontal"
                    HorizontalAlignment="Right"
                    Spacing="10">
            <Button Content="Enable"
                    Command="{Binding EnableCommand}"
                    MinWidth="100" />
            <Button Content="Cancel"
                    Command="{Binding CancelCommand}"
                    MinWidth="100" />
        </StackPanel>
    </Grid>
</Window>
```

**Success Criteria:**
- Modal dialog with clear warning
- Red/orange styling for warning
- Enable/Cancel buttons
- Returns dialog result
- Only shown when user tries to enable partial forms

---

## Phase 7: Testing & Quality

**Goal:** Comprehensive test coverage for critical components.

### 7.1 Unit Tests - Models & Services

**Priority:** HIGH
**Effort:** 6-8 hours
**Files:**
- `AutoQAC.Tests/Models/CleaningResultTests.cs`
- `AutoQAC.Tests/Services/GameDetectionServiceTests.cs`
- `AutoQAC.Tests/Services/PluginValidationServiceTests.cs`
- `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`

**Python Reference:**
- `Code_To_Port/Tests/test_state_manager.py`
- `Code_To_Port/Tests/test_config_manager.py`
- `Code_To_Port/Tests/test_cleaning_service.py`

**Example Test:**
```csharp
public sealed class GameDetectionServiceTests
{
    [Theory]
    [InlineData("SSEEdit.exe", GameType.SkyrimSpecialEdition)]
    [InlineData("FO4Edit64.exe", GameType.Fallout4)]
    [InlineData("FNVEdit.exe", GameType.FalloutNewVegas)]
    [InlineData("xEdit.exe", GameType.Unknown)]
    public void DetectFromExecutable_ShouldReturnCorrectGameType(
        string executable,
        GameType expected)
    {
        // Arrange
        var service = new GameDetectionService(Mock.Of<ILoggingService>());

        // Act
        var result = service.DetectFromExecutable(executable);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public async Task DetectFromLoadOrder_WithSkyrimMaster_ShouldReturnSSE()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "# Load Order\nSkyrim.esm\nUpdate.esm\n");

        var service = new GameDetectionService(Mock.Of<ILoggingService>());

        try
        {
            // Act
            var result = await service.DetectFromLoadOrderAsync(tempFile);

            // Assert
            result.Should().Be(GameType.SkyrimSpecialEdition);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
```

**Success Criteria:**
- 80%+ code coverage for services
- All edge cases tested (null, empty, invalid input)
- Async tests use proper async patterns
- Mock dependencies correctly
- Tests are isolated (no shared state)
- Fast execution (< 5 seconds total)

---

### 7.2 Integration Tests

**Priority:** MEDIUM
**Effort:** 4-5 hours
**Files:**
- `AutoQAC.Tests/Integration/CleaningWorkflowTests.cs`
- `AutoQAC.Tests/Integration/ConfigurationLoadingTests.cs`

**Example:**
```csharp
public sealed class CleaningWorkflowTests : IAsyncLifetime
{
    private ServiceProvider? _services;
    private string? _tempConfigDir;

    public async Task InitializeAsync()
    {
        // Set up temp config directory
        _tempConfigDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempConfigDir);

        // Create test configuration files
        await CreateTestConfigurationAsync();

        // Build DI container
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddConfiguration();
        services.AddBusinessLogic();

        _services = services.BuildServiceProvider();
    }

    [Fact]
    public async Task CleaningWorkflow_EndToEnd_ShouldSucceed()
    {
        // Arrange
        var orchestrator = _services!.GetRequiredService<ICleaningOrchestrator>();
        var stateService = _services.GetRequiredService<IStateService>();

        // Configure paths
        stateService.UpdateConfigurationPaths(
            loadOrder: Path.Combine(_tempConfigDir!, "plugins.txt"),
            mo2: null,
            xEdit: "C:\\xEdit\\SSEEdit.exe");  // Mock path

        // Act
        await orchestrator.StartCleaningAsync();

        // Assert
        var finalState = stateService.CurrentState;
        finalState.IsCleaning.Should().BeFalse();
        finalState.CleanedPlugins.Should().NotBeEmpty();
    }

    public async Task DisposeAsync()
    {
        _services?.Dispose();

        if (_tempConfigDir != null && Directory.Exists(_tempConfigDir))
        {
            Directory.Delete(_tempConfigDir, recursive: true);
        }
    }
}
```

**Success Criteria:**
- End-to-end workflow tests pass
- Configuration loading/saving works
- State management integrates correctly
- DI container resolves all dependencies
- Cleanup after tests (no temp file leaks)

---

### 7.3 Thread Safety Tests

**Priority:** HIGH
**Effort:** 3-4 hours
**Files:**
- `AutoQAC.Tests/Threading/StateServiceThreadSafetyTests.cs`
- `AutoQAC.Tests/Threading/ConfigurationServiceThreadSafetyTests.cs`

**Python Reference:**
- `Code_To_Port/Tests/test_thread_safety.py`
- `Code_To_Port/Tests/test_deadlock_scenarios.py`

**Example:**
```csharp
public sealed class StateServiceThreadSafetyTests
{
    [Fact]
    public async Task ConcurrentStateUpdates_ShouldNotCorruptState()
    {
        // Arrange
        var stateService = new StateService(Mock.Of<ILoggingService>());

        // Act - 100 concurrent state updates
        var tasks = Enumerable.Range(0, 100)
            .Select(i => Task.Run(() =>
            {
                stateService.UpdateState(s => s with
                {
                    Progress = i
                });
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert
        var finalState = stateService.CurrentState;
        finalState.Progress.Should().BeInRange(0, 99);
        // State should be consistent (no corruption)
    }

    [Fact]
    public async Task ConcurrentReadsAndWrites_ShouldNotDeadlock()
    {
        // Arrange
        var stateService = new StateService(Mock.Of<ILoggingService>());
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act - Concurrent reads and writes for 5 seconds
        var readTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                _ = stateService.CurrentState;
                await Task.Delay(1, cts.Token);
            }
        }, cts.Token);

        var writeTask = Task.Run(async () =>
        {
            int counter = 0;
            while (!cts.Token.IsCancellationRequested)
            {
                stateService.UpdateState(s => s with { Progress = counter++ });
                await Task.Delay(1, cts.Token);
            }
        }, cts.Token);

        // Assert - Should complete without timeout/deadlock
        await Task.WhenAll(readTask, writeTask);
    }
}
```

**Success Criteria:**
- No race conditions under concurrent access
- No deadlocks (tests complete within timeout)
- State remains consistent
- No memory corruption
- Thread-safe observables work correctly

---

## Phase 8: Polish & Deployment

**Goal:** Final polish, documentation, and deployment preparation.

### 8.1 Error Handling & User Feedback

**Priority:** HIGH
**Effort:** 3-4 hours
**Files:** Various (add error dialogs, validation feedback)

**Tasks:**
- Add error dialogs for all failure scenarios
- Validation feedback on configuration
- User-friendly error messages (no stack traces)
- Graceful degradation (app doesn't crash)
- Logging of all errors

**Success Criteria:**
- No unhandled exceptions
- User always knows what went wrong
- Clear guidance on how to fix errors
- Application never crashes

---

### 8.2 Configuration File Management

**Priority:** MEDIUM
**Effort:** 2-3 hours
**Files:**
- `AutoQAC/Services/Configuration/ConfigFileManager.cs`

**Tasks:**
- Auto-create `AutoQAC Data/` directory
- Copy default `AutoQAC Main.yaml` if missing
- Create blank `AutoQAC Config.yaml` if missing
- Backup config files before saving (optional)
- Validate YAML on load with helpful errors

**Success Criteria:**
- First-run experience works (creates configs)
- Invalid YAML shows clear error
- Config directory always exists

---

### 8.3 Documentation

**Priority:** MEDIUM
**Effort:** 3-4 hours
**Files:**
- `README.md` (user-facing documentation)
- `DEVELOPMENT.md` (developer guide)
- XML comments on all public APIs

**Tasks:**
- Update README with usage instructions
- Document configuration file format
- Add troubleshooting section
- Developer setup instructions
- Architecture overview

**Success Criteria:**
- New users can understand how to use the app
- New developers can build and run the project
- All public APIs have XML documentation

---

### 8.4 Deployment Preparation

**Priority:** MEDIUM
**Effort:** 2-3 hours
**Files:**
- Build scripts
- Release configuration
- Installer (optional)

**Tasks:**
- Optimize release build settings
- Trim unused dependencies
- Self-contained deployment option
- Version numbering
- Release notes

**Success Criteria:**
- Release build is optimized
- Application runs on clean Windows machine
- File size is reasonable (< 100 MB)
- No .NET runtime dependency (self-contained)

---

## Dependencies & Prerequisites

### Phase Dependencies

| Phase | Depends On | Critical Path |
|-------|-----------|---------------|
| 1 | None | YES |
| 2 | Phase 1 | YES |
| 3 | Phase 2 | YES |
| 4 | Phase 3 | YES |
| 5 | Phase 4 | YES |
| 6 | Phase 5 | NO |
| 7 | All phases | NO |
| 8 | Phase 7 | NO |

**Critical Path Phases:** 1 → 2 → 3 → 4 → 5
These must be completed sequentially for a functional application.

**Parallel Work Opportunities:**
- Phase 6 can start once Phase 5 is complete
- Phase 7 tests can be written alongside implementation
- Phase 8 documentation can be written throughout

---

### External Dependencies

**Required:**
- .NET 8 SDK
- Avalonia 11.3.8
- ReactiveUI
- YamlDotNet
- Serilog

**Optional:**
- xEdit (for integration testing)
- Bethesda game installation (for testing)
- Mod Organizer 2 (for MO2 feature testing)

---

## Success Criteria

### Feature Parity Achieved When:

1. **Configuration Management:**
   - ✅ Load/save YAML configuration files
   - ✅ Validate paths and settings
   - ✅ Game-specific skip lists work

2. **Plugin Cleaning:**
   - ✅ Sequential plugin processing (CRITICAL!)
   - ✅ xEdit subprocess execution with timeout
   - ✅ Real-time output parsing
   - ✅ Accurate statistics reporting

3. **User Interface:**
   - ✅ Functional main window with all controls
   - ✅ Progress dialog with real-time updates
   - ✅ Configuration dialogs for paths
   - ✅ Error feedback and validation

4. **Advanced Features:**
   - ✅ MO2 integration with command wrapper
   - ✅ Partial forms warning and support
   - ✅ Game type auto-detection
   - ✅ Cancellation support

5. **Quality:**
   - ✅ 80%+ test coverage for critical paths
   - ✅ No deadlocks or race conditions
   - ✅ Proper error handling
   - ✅ Comprehensive logging

6. **Documentation:**
   - ✅ User documentation (README)
   - ✅ Developer documentation
   - ✅ API documentation (XML comments)

---

## Tracking Progress

### Recommended Workflow

1. **Start Each Session:**
   - Review this roadmap
   - Identify current phase and task
   - Check dependencies completed

2. **During Development:**
   - Update CLAUDE.md with implementation notes
   - Write tests alongside features
   - Commit frequently with descriptive messages

3. **End Each Session:**
   - Mark completed tasks in this document
   - Document any blockers or decisions
   - Plan next session's tasks

4. **Phase Completion:**
   - Review all tasks in phase
   - Run all tests
   - Update this roadmap with completion status

---

## Notes

- This roadmap is a living document - update as needed
- Estimated efforts are guidelines, not strict deadlines
- Focus on correctness over speed
- When in doubt, refer to Python reference implementation
- Ask for clarification if reference implementation behavior is unclear
- **CRITICAL:** Remember sequential processing requirement throughout

---

## Removal of Code_To_Port/

Once feature parity is achieved and all success criteria are met:

1. Verify all features work correctly
2. Run full test suite (100% pass rate)
3. Document any intentional deviations from reference
4. Remove `Code_To_Port/` directory
5. Update CLAUDE.md to remove references to porting

**Do not remove Code_To_Port/ until feature parity is confirmed!**
