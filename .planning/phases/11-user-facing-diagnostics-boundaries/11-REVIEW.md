---
phase: 11-user-facing-diagnostics-boundaries
reviewed: 2026-05-01T12:00:00Z
depth: deep
files_reviewed: 27
files_reviewed_list:
  - AutoQAC.Tests/Helpers/DiagnosticSentinels.cs
  - AutoQAC.Tests/Integration/DependencyInjectionTests.cs
  - AutoQAC.Tests/Models/CleaningSessionResultTests.cs
  - AutoQAC.Tests/Models/DiagnosticTextFormatterTests.cs
  - AutoQAC.Tests/Models/Phase11ReportBoundaryTests.cs
  - AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs
  - AutoQAC.Tests/Services/CleaningServiceTests.cs
  - AutoQAC.Tests/Services/LegacyMigrationServiceTests.cs
  - AutoQAC.Tests/Services/Phase11LogBoundaryTests.cs
  - AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs
  - AutoQAC.Tests/ViewModels/ErrorDialogTests.cs
  - AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs
  - AutoQAC.Tests/ViewModels/Phase11DiagnosticsBoundaryTests.cs
  - AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs
  - AutoQAC.Tests/ViewModels/SettingsViewModelTests.cs
  - AutoQAC/App.axaml.cs
  - AutoQAC/Models/CleaningSessionResult.cs
  - AutoQAC/Models/Diagnostics/DiagnosticTextFormatter.cs
  - AutoQAC/Models/PluginCleaningResult.cs
  - AutoQAC/Services/Cleaning/CleaningService.cs
  - AutoQAC/Services/Cleaning/PluginResultFinalizer.cs
  - AutoQAC/Services/Configuration/LegacyMigrationService.cs
  - AutoQAC/Services/Process/ProcessExecutionService.cs
  - AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs
  - AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs
  - AutoQAC/ViewModels/RestoreViewModel.cs
  - AutoQAC/ViewModels/SettingsViewModel.cs
findings:
  critical: 1
  warning: 4
  info: 0
  total: 5
status: issues_found
---

# Phase 11: Code Review Report

**Reviewed:** 2026-05-01T12:00:00Z
**Depth:** deep
**Files Reviewed:** 27
**Status:** issues_found

## Summary

Reviewed the listed Phase 11 diagnostics-boundary files at deep depth, including cross-file flows from cleaning finalization into progress UI tooltips and reports. The earlier raw-plugin-filename failed-result issue in `CleaningService` is resolved in the current code; failed command-build and launch-exception messages now route through sanitized plugin display names. One disclosure boundary remains open through log-parse warnings, and prior report/state-consistency warnings are still valid.

## Critical Issues

### CR-01: Raw log-file paths are returned into user-facing progress tooltips

**Classification:** BLOCKER
**File:** `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs:37-40`
**Issue:** `PluginResultFinalizer` copies `logResult.Warning` directly into `PluginCleaningResult.LogParseWarning`. `XEditLogFileService.ReadLogContentAsync()` can produce `Warning = $"Main log file not found: {mainLogPath}"`, including the full local xEdit log path. `ProgressWindow.axaml` binds `LogParseWarning` to `ToolTip.Tip`, so a missing-log condition leaks raw local paths through the user-facing UI despite Phase 11's safe diagnostic boundary.
**Fix:** Do not propagate service warnings verbatim into `LogParseWarning`; log the raw warning for troubleshooting and return stable safe copy.
```csharp
if (logResult.Warning != null)
{
    logger.Warning("Log read warning for {Plugin}: {Warning}", plugin.FileName, logResult.Warning);
    logParseWarning = "xEdit log could not be read. See the latest AutoQAC log.";
}
```

## Warnings

### WR-01: Already-clean plugins are duplicated in generated reports

**Classification:** WARNING
**File:** `AutoQAC/Models/CleaningSessionResult.cs:51-52,179-197`
**Issue:** `CleanedPlugins` includes `CleaningStatus.AlreadyClean`, but `GenerateReport()` prints `CleanedPlugins` under `--- Cleaned Plugins ---` and then prints `AlreadyCleanPlugins` again under `--- Already Clean Plugins ---`. Already-clean plugins appear twice, making exported reports misleading.
**Fix:** Use a cleaned-only sequence for the cleaned section, or remove the separate already-clean section.
```csharp
var actuallyCleanedPlugins = PluginResults.Where(r => r.Status == CleaningStatus.Cleaned);
if (actuallyCleanedPlugins.Any())
{
    sb.AppendLine("--- Cleaned Plugins ---");
    foreach (var result in actuallyCleanedPlugins)
    {
        var safePluginName = DiagnosticTextFormatter.SafePluginName(result.PluginName);
        sb.AppendLine($"  {safePluginName}: {result.Summary} ({result.Duration:mm\\:ss})");
    }
    sb.AppendLine();
}
```

### WR-02: Failed load-order selection leaves invalid runtime state behind

**Classification:** WARNING
**File:** `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs:277-284,285-317`
**Issue:** `ConfigureLoadOrderAsync()` updates `_stateService` and `LoadOrderPath` before `RefreshForGameAsync()` proves the selected file can be loaded. If parsing/loading fails, the catch blocks show safe error copy but leave the rejected path in runtime state and the ViewModel even though the override was not persisted.
**Fix:** Defer state mutation until refresh succeeds, or roll back the previous path in every failure path.
```csharp
var previousPath = LoadOrderPath;
try
{
    await _pluginRefreshCoordinator.RefreshForGameAsync(
        new PluginRefreshRequest(SelectedGame, GameDataFolder, path));

    LoadOrderPath = path;
    _stateService.UpdateConfigurationPaths(path, Mo2Path, XEditPath);
    await _configService.SetGameLoadOrderOverrideAsync(SelectedGame, path);
}
catch
{
    LoadOrderPath = previousPath;
    _stateService.UpdateConfigurationPaths(previousPath, Mo2Path, XEditPath);
    throw;
}
```

### WR-03: ProcessStartInfo cloning silently changes launch semantics

**Classification:** WARNING
**File:** `AutoQAC/Services/Process/ProcessExecutionService.cs:175-209`
**Issue:** `CloneStartInfoForLaunch()` forces `UseShellExecute = false` and copies only a subset of `ProcessStartInfo` properties. Any caller-provided shell semantics, environment variables, verb/window settings, credentials, or other start options are silently dropped. Even if current xEdit commands use `UseShellExecute = false`, `IProcessExecutionService.ExecuteAsync()` accepts a fully-formed `ProcessStartInfo` and should not change the launch contract while trying to sanitize logging.
**Fix:** Either launch the caller's `startInfo` directly without mutating it, or clone all launch-affecting properties and preserve `UseShellExecute` unless there is an explicit documented reason to override it.
```csharp
var processStartInfo = new ProcessStartInfo
{
    FileName = startInfo.FileName,
    WorkingDirectory = startInfo.WorkingDirectory,
    UseShellExecute = startInfo.UseShellExecute,
    CreateNoWindow = startInfo.CreateNoWindow,
    Verb = startInfo.Verb,
    WindowStyle = startInfo.WindowStyle,
};
foreach (var pair in startInfo.Environment)
{
    processStartInfo.Environment[pair.Key] = pair.Value;
}
```

### WR-04: Loading restore sessions without a trusted root leaves stale sessions visible

**Classification:** WARNING
**File:** `AutoQAC/ViewModels/RestoreViewModel.cs:120-133`
**Issue:** When `LoadSessionsAsync()` is called with a null/empty data folder, it clears `_backupRoot` and disables commands but does not clear `Sessions`, `SelectedSession`, or `SelectedSessionPlugins`. If the restore window previously loaded a real backup root, the UI can continue displaying stale sessions/plugins under the new “cannot locate backups” state.
**Fix:** Clear the selected session and session collections when the trusted restore root is missing, and raise `HasSessions` after clearing.
```csharp
if (!HasTrustedRestoreRoot)
{
    _backupRoot = null;
    SelectedSession = null;
    Sessions.Clear();
    SelectedSessionPlugins.Clear();
    OnPropertyChanged(nameof(HasSessions));
    DeleteSessionCommand.NotifyCanExecuteChanged();
    StatusText = "No game data folder configured -- cannot locate backups";
    return;
}
```

---

_Reviewed: 2026-05-01T12:00:00Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: deep_
