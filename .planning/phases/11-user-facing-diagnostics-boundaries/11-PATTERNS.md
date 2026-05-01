# Phase 11: User-Facing Diagnostics Boundaries - Pattern Map

**Mapped:** 2026-04-30
**Files analyzed:** 18 new/modified files
**Analogs found:** 18 / 18

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `AutoQAC/Services/UI/DiagnosticTextFormatter.cs` | utility | transform | `AutoQAC/Models/BackupOperationResults.cs` | role-match |
| `AutoQAC/Services/UI/IDiagnosticTextFormatter.cs` | service contract | transform | `AutoQAC/Services/UI/IMessageDialogService.cs` | role-match |
| `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` | config | request-response | `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` | exact |
| `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` | viewmodel | request-response | `AutoQAC/ViewModels/RestoreViewModel.cs` | exact |
| `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs` | viewmodel | file-I/O | `AutoQAC/ViewModels/RestoreViewModel.cs` | role-match |
| `AutoQAC/ViewModels/RestoreViewModel.cs` | viewmodel | file-I/O | `AutoQAC/ViewModels/RestoreViewModel.cs` | exact |
| `AutoQAC/Services/Configuration/LegacyMigrationService.cs` | service | file-I/O | `AutoQAC/Services/Configuration/ConfigPersistenceStatus.cs` | partial |
| `AutoQAC/App.axaml.cs` | application startup | event-driven | `AutoQAC/App.axaml.cs` | exact |
| `AutoQAC/Services/Process/ProcessExecutionService.cs` | service | request-response | `AutoQAC/Services/Cleaning/CleaningService.cs` | role-match |
| `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs` | service | transform | `AutoQAC/Services/Cleaning/CleaningService.cs` | role-match |
| `AutoQAC/Models/PluginCleaningResult.cs` | model | transform | `AutoQAC/Models/BackupOperationResults.cs` | role-match |
| `AutoQAC/Models/CleaningSessionResult.cs` | model | transform | `AutoQAC/Models/CleaningSessionResult.cs` | exact |
| `AutoQAC.Tests/ViewModels/ErrorDialogTests.cs` | test | request-response | `AutoQAC.Tests/ViewModels/ErrorDialogTests.cs` | exact |
| `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs` | test | request-response | `AutoQAC.Tests/ViewModels/ErrorDialogTests.cs` | role-match |
| `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs` | test | file-I/O | `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs` | exact |
| `AutoQAC.Tests/Models/CleaningSessionResultTests.cs` | test | transform | `AutoQAC.Tests/Models/CleaningSessionResultTests.cs` | exact |
| `AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs` | test | transform | `AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs` | exact |
| `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs` | test | request-response | `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs` | exact |

## Pattern Assignments

### `AutoQAC/Services/UI/DiagnosticTextFormatter.cs` (utility, transform)

**Analog:** `AutoQAC/Models/BackupOperationResults.cs`

**Imports/namespace pattern** (lines 1-4):
```csharp
using System.Collections.Generic;
using System.Linq;

namespace AutoQAC.Models;
```

**Core transform pattern** (lines 54-73):
```csharp
/// <summary>
/// Maps operation-neutral failure reasons to the concise labels allowed in user-facing restore and retention rows.
/// </summary>
public static class BackupFailureReasonExtensions
{
    private static readonly IReadOnlyDictionary<BackupFailureReason, string> DisplayLabels = new Dictionary<BackupFailureReason, string>
    {
        [BackupFailureReason.MissingBackupFile] = "Missing backup file",
        [BackupFailureReason.AccessDenied] = "Access denied",
        [BackupFailureReason.TargetFolderCreationFailed] = "Target folder creation failed",
        [BackupFailureReason.TargetWriteFailed] = "Target write failed",
        [BackupFailureReason.Canceled] = "Canceled",
        [BackupFailureReason.CleanupDeletionFailed] = "Cleanup deletion failed"
    };

    /// <summary>
    /// Returns the approved UI label for a reason, or null when callers must map neutral reasons themselves.
    /// </summary>
    public static string? ToDisplayLabel(this BackupFailureReason reason) =>
        DisplayLabels.TryGetValue(reason, out var label) ? label : null;
}
```

**Typed-safe payload pattern** (from `AutoQAC/Services/Configuration/ConfigPersistenceStatus.cs`, lines 42-58):
```csharp
/// <summary>
/// Safe failure payload for ViewModel-facing configuration persistence status. Per D-28,
/// <paramref name="SafeSummary" /> is category-shaped human text such as
/// "Could not write settings file (write_failed)" and must not contain raw exception types,
/// stack traces, or full internal paths.
/// </summary>
public sealed record ConfigPersistenceFailure(
    ConfigPersistenceOperationKind Operation,
    ConfigPersistenceFailureKind Kind,
    string SafeSummary,
    string? LogReference,
    long Generation);
```

**Use for Phase 11:** Implement basename/path display, operation-specific latest-log guidance, and unsafe-detail sentinel helpers as small named transform methods. Add XML docs because this is a public/cross-cutting helper.

---

### `AutoQAC/Services/UI/IDiagnosticTextFormatter.cs` (service contract, transform)

**Analog:** `AutoQAC/Services/UI/IMessageDialogService.cs`

**Interface/doc pattern** (lines 43-61):
```csharp
/// <summary>
/// Service for displaying message dialogs to the user.
/// </summary>
public interface IMessageDialogService
{
    /// <summary>
    /// Shows a message dialog with the specified options.
    /// </summary>
    Task<MessageDialogResult> ShowAsync(
        string title,
        string message,
        MessageDialogButtons buttons = MessageDialogButtons.Ok,
        MessageDialogIcon icon = MessageDialogIcon.None,
        string? details = null);

    /// <summary>
    /// Shows an error dialog.
    /// </summary>
    Task ShowErrorAsync(string title, string message, string? details = null);
}
```

**Use for Phase 11:** If planner chooses DI instead of a static helper, keep the interface next to implementation under `Services/UI`, with XML docs on each public method and no Avalonia types in the contract.

---

### `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` (config, request-response)

**Analog:** `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`

**Imports pattern** (lines 1-14):
```csharp
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Services.Backup;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameDetection;
using AutoQAC.Services.MO2;
using AutoQAC.Services.Monitoring;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.Process;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using AutoQAC.ViewModels;
using AutoQAC.Views;
using Microsoft.Extensions.DependencyInjection;
```

**DI registration pattern** (lines 79-84):
```csharp
public static IServiceCollection AddUiServices(this IServiceCollection services)
{
    services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
    services.AddSingleton<IFileDialogService, FileDialogService>();
    services.AddSingleton<IMessageDialogService, MessageDialogService>();
    return services;
}
```

**Use for Phase 11:** Register `IDiagnosticTextFormatter, DiagnosticTextFormatter` in `AddUiServices()` if formatter is injectable. If static/internal, do not modify DI.

---

### `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` (viewmodel, request-response)

**Analog:** `AutoQAC/ViewModels/RestoreViewModel.cs`

**Safe unexpected error pattern** (lines 216-223):
```csharp
catch (Exception ex)
{
    _logger.Error(ex, "Failed to restore plugin {Plugin}", plugin.FileName);
    await _messageDialog.ShowErrorAsync(
        "Restore Failed",
        $"Failed to restore '{plugin.FileName}'.",
        "Technical details were written to the log.");
    StatusText = $"Failed to restore: {plugin.FileName}";
}
```

**Current unsafe cleanup target** (from `CleaningCommandsViewModel.cs`, lines 155-163):
```csharp
catch (Exception ex)
{
    StatusText = $"Error: {ex.Message}";
    _logger.Error(ex, "StartCleaningAsync failed");
    await _messageDialog.ShowErrorAsync(
        "Cleaning Failed",
        "An error occurred during the cleaning process.",
        $"Error: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}");
}
```

**Validation row model pattern** (from `CleaningCommandsViewModel.cs`, lines 356-366):
```csharp
errors.Add(new ValidationError(
    "xEdit not configured",
    "xEdit executable path is not set.",
    "Go to Edit > Settings and set the xEdit Path to your xEdit executable (SSEEdit.exe, FO4Edit.exe, etc.)."));
```

**Use for Phase 11:** Preserve the `InvalidOperationException` validation branch but route general `Exception` status/dialog/details through safe operation copy. Replace full-path validation strings with safe identifiers such as `xEdit Path (SSEEdit.exe)` and `Load Order File (plugins.txt)`.

---

### `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs` (viewmodel, file-I/O)

**Analog:** `AutoQAC/ViewModels/RestoreViewModel.cs`

**Safe category/status pattern** (lines 338-357):
```csharp
case BackupSessionDeleteStatus.RejectedOutsideBackupRoot:
    // One canonical user-facing sentence is shared between StatusText and dialog
    // details so the safety message stays consistent across the restore-window
    // surface (LOW finding from cross-AI review).
    StatusText = "The selected backup session is outside the configured backup folder.";
    await _messageDialog.ShowErrorAsync(
        "Delete Failed",
        "Failed to delete the backup session.",
        "The selected backup session is outside the configured backup folder.");
    break;

case BackupSessionDeleteStatus.Failed:
default:
    // Generic IO failure copy: the technical exception detail lives in the
    // service log, not in the dialog (D-04 concise reason pattern).
    StatusText = "Failed to delete the backup session. Technical details were written to the log.";
    await _messageDialog.ShowErrorAsync(
        "Delete Failed",
        "Failed to delete the backup session.",
        "Technical details were written to the log.");
    break;
```

**Current unsafe browse target** (from `ConfigurationViewModel.cs`, lines 284-311):
```csharp
catch (FileNotFoundException ex)
{
    _logger.Error(ex, "Load order file not found");
    await _messageDialog.ShowErrorAsync(
        "File Not Found",
        "The load order file could not be found.",
        $"Path: {path}\n\nError: {ex.Message}");
    StatusText = "Load order file not found";
    return;
}
```

**Use for Phase 11:** Keep file dialog and refresh flow unchanged. Only replace dialog details/status copy with safe file/folder identifiers and latest-log guidance for technical read/parse failures. Keep `StatusText = "Data folder override set for {SelectedGame}"` style (lines 372-379) because it uses game label, not path.

---

### `AutoQAC/ViewModels/RestoreViewModel.cs` (viewmodel, file-I/O)

**Analog:** `AutoQAC/ViewModels/RestoreViewModel.cs`

**Current unsafe session-load target** (lines 173-177):
```csharp
catch (Exception ex)
{
    _logger.Error(ex, "Failed to load backup sessions");
    StatusText = $"Error loading sessions: {ex.Message}";
}
```

**Safe restore summary pattern** (lines 499-515):
```csharp
private static string BuildRestoreSummaryText(BackupRestoreResult result)
{
    var counts = $"{result.RestoredCount} restored, {result.FailedCount} failed, {result.CanceledCount} canceled";

    return result.Status switch
    {
        BackupOperationStatus.Complete => $"Restore completed: {result.RestoredCount} plugin(s) restored.",
        BackupOperationStatus.Partial => $"Restore partially completed: {counts}. Review the rows below. Technical details were written to the log.",
        BackupOperationStatus.Failed => $"Restore failed: {counts}. Review the failed rows, fix missing files or permissions, then try again. Technical details were written to the log.",
        BackupOperationStatus.Canceled => $"Restore canceled: {counts}. Partial files were removed and completed/failed/canceled rows remain visible.",
        _ => $"Restore completed with status {result.Status}: {counts}."
    };
}
```

**Use for Phase 11:** Make session-load failure match the generic technical-detail pattern above; do not include `ex.Message` in `StatusText`.

---

### `AutoQAC/Services/Configuration/LegacyMigrationService.cs` and `AutoQAC/App.axaml.cs` (service/startup, event-driven)

**Analog:** `AutoQAC/Services/Configuration/ConfigPersistenceStatus.cs`

**Safe failure contract** (lines 42-58):
```csharp
/// <summary>
/// Safe failure payload for ViewModel-facing configuration persistence status. Per D-28,
/// <paramref name="SafeSummary" /> is category-shaped human text such as
/// "Could not write settings file (write_failed)" and must not contain raw exception types,
/// stack traces, or full internal paths.
/// </summary>
public sealed record ConfigPersistenceFailure(
    ConfigPersistenceOperationKind Operation,
    ConfigPersistenceFailureKind Kind,
    string SafeSummary,
    string? LogReference,
    long Generation);
```

**Current unsafe migration target** (from `App.axaml.cs`, lines 167-171):
```csharp
catch (Exception ex)
{
    logger.Error(ex, "[Migration] Unexpected error during legacy migration");
    viewModel.ShowMigrationWarning($"Legacy config migration failed unexpectedly: {ex.Message}");
}
```

**Startup log target** (from `App.axaml.cs`, lines 126-132):
```csharp
logger.Information("=== AutoQAC Session Start ===");
logger.Information("Version: {Version}", versionStr);
logger.Information(".NET Runtime: {Runtime}", RuntimeInformation.FrameworkDescription);
logger.Information("xEdit Path: {XEditPath}", state.XEditExecutablePath ?? "(not configured)");
logger.Information("Game Type: {GameType}", state.CurrentGameType);
logger.Information("MO2 Mode: {Mo2Mode}", state.Mo2ModeEnabled);
logger.Information("Load Order: {PluginCount} plugins", state.PluginsToClean.Count);
```

**Use for Phase 11:** Keep exception object in logs, but map migration warning to safe category/generic copy. Replace `xEdit Path` startup property with safe configuration status/basename/count fields.

---

### `AutoQAC/Services/Process/ProcessExecutionService.cs` (service, request-response)

**Analog:** `AutoQAC/Services/Cleaning/CleaningService.cs`

**Safe launch-failure logging/result pattern** (lines 57-68):
```csharp
var launchMode = state.Mo2ModeEnabled ? "MO2" : "direct xEdit";
logger.Warning(
    "Failed to build {LaunchMode} launch command for {Plugin}; no process was started.",
    launchMode,
    plugin.FileName);

return new CleaningResult
{
    Success = false,
    Status = CleaningStatus.Failed,
    Message = $"Could not build {launchMode} launch command for {plugin.FileName}. No process was started. See logs for technical details.",
    Duration = sw.Elapsed
};
```

**Current unsafe process log target** (from `ProcessExecutionService.cs`, lines 51-69):
```csharp
var fileName = startInfo.FileName;
var arguments = GetArgumentSummary(startInfo);
var workingDirectory = startInfo.WorkingDirectory;

var processStartInfo = CloneStartInfoForLaunch(startInfo, fileName, workingDirectory);

using var process = new System.Diagnostics.Process();
process.StartInfo = processStartInfo;

logger.Debug("Starting process: {FileName} {Arguments}", startInfo.FileName, arguments);

try
{
    process.Start();
}
catch (Exception ex)
{
    logger.Error(ex, "Failed to start process: {FileName}", startInfo.FileName);
    return new ProcessResult { ExitCode = -1 };
}
```

**Argument summary helper** (from `ProcessExecutionService.cs`, lines 195-201):
```csharp
/// <summary>
/// Returns debug-safe argument context without treating legacy Arguments as the only launch source.
/// </summary>
private static string GetArgumentSummary(ProcessStartInfo startInfo) =>
    startInfo.ArgumentList.Count > 0
        ? $"{startInfo.ArgumentList.Count} argument-list entries"
        : startInfo.Arguments;
```

**Use for Phase 11:** Preserve `CloneStartInfoForLaunch` behavior. Change log templates/arguments only: operation, launch mode, plugin filename, PID after start, argument count, and safe reason/category. Avoid logging `FileName`, raw `Arguments`, or MO2 nested payloads.

---

### `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs` (service, transform)

**Analog:** `AutoQAC/Services/Cleaning/CleaningService.cs`

**Safe result-message pattern** (lines 132-141):
```csharp
catch (Exception ex)
{
    logger.Error(ex, "Error cleaning {Plugin}", plugin.FileName);
    return new CleaningResult
    {
        Success = false,
        Status = CleaningStatus.Failed,
        Message = $"Cleaning failed for {plugin.FileName}. See logs for technical details.",
        Duration = sw.Elapsed
    };
}
```

**Current xEdit exception-log leakage target** (from `PluginResultFinalizer.cs`, lines 62-69):
```csharp
// PAR-03: Exception log surfacing (per D-06)
if (logResult.ExceptionContent != null)
{
    logParseWarning = logResult.ExceptionContent;
    finalStatus = CleaningStatus.Failed;
    logger.Warning("xEdit exception log for {Plugin}: {Content}",
        plugin.FileName, logResult.ExceptionContent);
}
```

**Final result construction pattern** (from `PluginResultFinalizer.cs`, lines 83-94):
```csharp
return new PluginCleaningResult
{
    PluginName = plugin.FileName,
    Status = finalStatus,
    Success = finalSuccess,
    Message = result.TimedOut && runnerOutput.ReachedMaxRetryAttempts
        ? $"Cleaning timed out after {runnerOutput.AttemptCount} attempts."
        : result.Message,
    Duration = runnerOutput.Duration,
    Statistics = logStats,
    LogParseWarning = logParseWarning
};
```

**Use for Phase 11:** Keep `PluginName = plugin.FileName` and final status derivation. Ensure xEdit exception-log content becomes a safe `Message`/`LogParseWarning` such as `xEdit reported an error for Plugin.esp. See the latest AutoQAC log.`; log content only if allowed by log boundary policy.

---

### `AutoQAC/Models/PluginCleaningResult.cs` and `AutoQAC/Models/CleaningSessionResult.cs` (models, transform)

**Analog:** `AutoQAC/Models/CleaningSessionResult.cs`

**Failed summary/report target** (from `PluginCleaningResult.cs`, lines 81-90):
```csharp
public string Summary
{
    get
    {
        if (Status == CleaningStatus.Skipped)
            return "Skipped";

        if (Status == CleaningStatus.Failed)
            return $"Failed: {Message}";
```

**Report generation pattern** (from `CleaningSessionResult.cs`, lines 207-215):
```csharp
if (FailedPlugins.Any())
{
    sb.AppendLine("--- Failed Plugins ---");
    foreach (var result in FailedPlugins)
    {
        sb.AppendLine($"  {result.PluginName}: {result.Message}");
    }
    sb.AppendLine();
}
```

**Use for Phase 11:** Source-created messages should already be safe. Add report disclaimer near header/summary and keep a defensive failed-row fallback if `Message` is empty/unsafe. Do not strip plugin filenames, counts, durations, or status labels.

---

### Test files (tests, request-response/file-I/O/transform)

**Analogs:** `AutoQAC.Tests/ViewModels/ErrorDialogTests.cs`, `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs`, `AutoQAC.Tests/Models/CleaningSessionResultTests.cs`, `AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs`, `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`

**ViewModel test fixture pattern** (from `ErrorDialogTests.cs`, lines 24-44):
```csharp
private readonly IConfigurationService _configServiceMock;
private readonly IStateService _stateServiceMock;
private readonly ICleaningOrchestrator _orchestratorMock;
private readonly ILoggingService _loggerMock;
private readonly IFileDialogService _fileDialogMock;
private readonly IMessageDialogService _messageDialogMock;
private readonly IPluginValidationService _pluginServiceMock;
private readonly IPluginLoadingService _pluginLoadingServiceMock;
private readonly IUiDispatcher _uiDispatcher;

public ErrorDialogTests()
{
    _configServiceMock = Substitute.For<IConfigurationService>();
    _stateServiceMock = Substitute.For<IStateService>();
    _orchestratorMock = Substitute.For<ICleaningOrchestrator>();
    _loggerMock = Substitute.For<ILoggingService>();
```

**Negative modal assertion pattern** (from `ErrorDialogTests.cs`, lines 121-130):
```csharp
vm.Commands.HasValidationErrors.Should().BeTrue("validation errors should be visible");
vm.Commands.ValidationErrors.Should().Contain(e => e.Title == "xEdit not configured",
    "should show xEdit not configured error");

// No modal dialog should be shown
await _messageDialogMock.DidNotReceive().ShowErrorAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());

// Orchestrator should NOT be called
await _orchestratorMock.DidNotReceive().StartCleaningAsync(Arg.Any<TimeoutRetryCallback>(), Arg.Any<BackupFailureCallback>(), Arg.Any<CancellationToken>());
```

**Safe restore assertion pattern** (from `RestoreViewModelTests.cs`, lines 666-694):
```csharp
/// <summary>
/// Plan 07-13 RED: when the service reports a non-validation failure (locked file, IO error),
/// the ViewModel must show the generic "technical details written to the log" sentence and
/// must not include raw exception message content in the dialog or status text.
/// </summary>
[Fact]
public async Task DeleteSessionCommand_ServiceFailedStatus_LogsAndShowsConciseError()
{
    ...
    vm.StatusText.Should().Be("Failed to delete the backup session. Technical details were written to the log.");
    await _messageDialog.Received(1).ShowErrorAsync(
        "Delete Failed",
        "Failed to delete the backup session.",
        "Technical details were written to the log.");
}
```

**Report test pattern** (from `CleaningSessionResultTests.cs`, lines 431-470):
```csharp
[Fact]
public void GenerateReport_ShouldIncludePluginDetails()
{
    var result = new CleaningSessionResult
    {
        PluginResults = new List<PluginCleaningResult>
        {
            new() { PluginName = "Clean.esp", Status = CleaningStatus.Cleaned, Duration = TimeSpan.FromSeconds(45), Statistics = new CleaningStatistics { ItemsRemoved = 10 } },
            new() { PluginName = "Skip.esp", Status = CleaningStatus.Skipped },
            new() { PluginName = "Fail.esp", Status = CleaningStatus.Failed, Message = "Timeout" }
        }
    };

    var report = result.GenerateReport();

    report.Should().Contain("Failed Plugins");
    report.Should().Contain("Fail.esp: Timeout");
}
```

**Finalizer test pattern** (from `PluginResultFinalizerTests.cs`, lines 155-188):
```csharp
[Fact]
public async Task FinalizeAsync_ExceptionLogContent_ForcesStatusFailedAndSuccessFalse()
{
    var plugin = CreatePlugin("CleanedButException.esp");
    var runnerOutput = CreateRunnerOutput();

    _logFileServiceMock.ReadLogContentAsync(...)
        .Returns(new LogReadResult
        {
            LogLines = new List<string>(),
            ExceptionContent = "EAccessViolation: invalid pointer operation at 0x00401234"
        });

    var result = await _sut.FinalizeAsync(...);

    result.Status.Should().Be(CleaningStatus.Failed);
    result.Success.Should().BeFalse();
    result.LogParseWarning.Should().Contain("EAccessViolation");
}
```

**Process log assertion pattern** (from `ProcessExecutionServiceTests.cs`, lines 83-99):
```csharp
var startInfo = new ProcessStartInfo
{
    FileName = "nonexistent_process_that_does_not_exist_12345.exe",
    Arguments = "--test"
};

var result = await service.ExecuteAsync(startInfo);

result.ExitCode.Should().Be(-1, "startup failure should return -1 exit code");

_mockLogger.Received(1).Error(
    Arg.Any<Exception>(),
    "Failed to start process: {FileName}",
    "nonexistent_process_that_does_not_exist_12345.exe");
```

**Use for Phase 11:** Add shared unsafe sentinel values (`C:\Users\...`, xEdit/MO2 path, plugin full path, `-QAC`, stack-like text). Assert every captured dialog/status/report/log message `NotContain` those sentinels while still containing plugin filename, safe category, latest AutoQAC log guidance, or argument count as appropriate.

## Shared Patterns

### Safe User-Facing Technical Failure Copy
**Source:** `AutoQAC/ViewModels/RestoreViewModel.cs` lines 216-223 and 349-357  
**Apply to:** `CleaningCommandsViewModel`, `ConfigurationViewModel`, `RestoreViewModel`, migration warning paths, cleaning result/report messages
```csharp
_logger.Error(ex, "Failed to restore plugin {Plugin}", plugin.FileName);
await _messageDialog.ShowErrorAsync(
    "Restore Failed",
    $"Failed to restore '{plugin.FileName}'.",
    "Technical details were written to the log.");
StatusText = $"Failed to restore: {plugin.FileName}";
```

### Typed Safe Category Labels
**Source:** `AutoQAC/Services/Configuration/ConfigPersistenceStatus.cs` lines 42-58; `AutoQAC/Models/BackupOperationResults.cs` lines 54-73  
**Apply to:** config persistence/migration/restore/session-load/failed-row status mapping
```csharp
public sealed record ConfigPersistenceFailure(
    ConfigPersistenceOperationKind Operation,
    ConfigPersistenceFailureKind Kind,
    string SafeSummary,
    string? LogReference,
    long Generation);

public static string? ToDisplayLabel(this BackupFailureReason reason) =>
    DisplayLabels.TryGetValue(reason, out var label) ? label : null;
```

### Structured Logging With Safe Fields
**Source:** `AutoQAC/Services/Cleaning/CleaningService.cs` lines 57-61 and 78-82  
**Apply to:** `ProcessExecutionService`, `App.axaml.cs`, result finalizer logging
```csharp
logger.Warning(
    "Failed to build {LaunchMode} launch command for {Plugin}; no process was started.",
    launchMode,
    plugin.FileName);

logger.Information(
    "Cleaning {Plugin} for {Game} with timeout {TimeoutSeconds}s...",
    plugin.FileName,
    gameDisplayName,
    timeout.TotalSeconds);
```

### MVVM Validation and Dialog Boundaries
**Source:** `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` lines 126-153; `AutoQAC/Services/UI/IMessageDialogService.cs` lines 51-61  
**Apply to:** user-facing validation errors and modal dialogs
```csharp
var errors = ValidatePreClean();
if (errors.Count > 0)
{
    foreach (var error in errors)
        ValidationErrors.Add(error);
    HasValidationErrors = true;
    return;
}

Task ShowErrorAsync(string title, string message, string? details = null);
```

### Tests With NSubstitute and FluentAssertions
**Source:** `AutoQAC.Tests/ViewModels/ErrorDialogTests.cs` lines 24-59; `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs` lines 666-694  
**Apply to:** all Phase 11 regression tests
```csharp
_messageDialogMock = Substitute.For<IMessageDialogService>();
_pluginLoadingServiceMock.IsGameSupportedByMutagen(Arg.Any<GameType>())
    .Returns(false);

vm.StatusText.Should().Be("Failed to delete the backup session. Technical details were written to the log.");
await _messageDialog.Received(1).ShowErrorAsync(
    "Delete Failed",
    "Failed to delete the backup session.",
    "Technical details were written to the log.");
```

## No Analog Found

All planned files have at least a role-match analog. Startup-log testing may require a small new seam; use `ProcessExecutionServiceTests.cs` logger assertions and `ServiceCollectionExtensions.cs` DI patterns if such a seam is extracted.

## Metadata

**Analog search scope:** `AutoQAC/Services/**/*.cs`, `AutoQAC/ViewModels/**/*.cs`, `AutoQAC/Models/**/*.cs`, `AutoQAC.Tests/**/*.cs`, `AutoQAC/App.axaml.cs`, `AutoQAC/Infrastructure/**/*.cs`  
**Files scanned:** 111 C# files from targeted globs plus Phase 11 context/research/spec and codebase docs  
**Strong analogs read:** 20 files  
**Pattern extraction date:** 2026-04-30
