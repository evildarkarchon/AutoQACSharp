---
phase: 11-user-facing-diagnostics-boundaries
reviewed: 2026-05-01T06:46:55Z
depth: standard
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
  warning: 2
  info: 0
  total: 3
status: issues_found
---

# Phase 11: Code Review Report

**Reviewed:** 2026-05-01T06:46:55Z
**Depth:** standard
**Files Reviewed:** 27
**Status:** issues_found

## Summary

Reviewed the Phase 11 diagnostics-boundary implementation and tests at standard depth. The main concern is that one failed-launch path still constructs a user-facing result from an unsanitized plugin name. Two additional correctness/robustness issues affect generated reports and load-order selection state consistency.

## Critical Issues

### CR-01: Raw plugin filename can cross the failed-launch diagnostic boundary

**Classification:** BLOCKER
**File:** `AutoQAC/Services/Cleaning/CleaningService.cs:64-69,171-177`
**Issue:** The command-build failure and unexpected-exception paths build `CleaningResult.Message` with raw `plugin.FileName`. Phase 11 is explicitly hardening user-facing diagnostic boundaries, but this method is public and returns a result message that can be displayed directly or passed downstream. A path-like or sentinel-laden plugin filename can therefore leak local path/command fragments or unsafe display characters before `PluginResultFinalizer` has a chance to replace the text.
**Fix:** Use a sanitized plugin display name for returned user-facing messages, or preferably use the shared formatter fallback for failed plugin copy.
```csharp
var safePluginName = DiagnosticTextFormatter.SafePluginName(plugin.FileName);

return new CleaningResult
{
    Success = false,
    Status = CleaningStatus.Failed,
    Message = $"Could not build {buildFailureLaunchMode} launch command for {safePluginName}. No process was started. See logs for technical details.",
    Duration = sw.Elapsed
};

// In the exception path:
Message = DiagnosticTextFormatter.CleaningFailedForPlugin(plugin.FileName),
```

## Warnings

### WR-01: Already-clean plugins are duplicated in generated reports

**Classification:** WARNING
**File:** `AutoQAC/Models/CleaningSessionResult.cs:51-52,179-197`
**Issue:** `CleanedPlugins` intentionally includes `CleaningStatus.AlreadyClean`, but `GenerateReport()` prints `CleanedPlugins` in the `--- Cleaned Plugins ---` section and then prints `AlreadyCleanPlugins` again in `--- Already Clean Plugins ---`. Any already-clean plugin appears twice in exported reports, which makes the report misleading and can inflate perceived work done.
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
**Issue:** `ConfigureLoadOrderAsync()` calls `_stateService.UpdateConfigurationPaths(path, Mo2Path, XEditPath)` and sets `LoadOrderPath = path` before `RefreshForGameAsync()` proves the selected file can be loaded. If parsing/loading fails, the catch blocks show an error but leave the rejected path in runtime state and the ViewModel. That can mislead later validation and UI state even though the override is not persisted.
**Fix:** Defer state mutation until after refresh succeeds, or roll back the previous path in every failure path.
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
catch (Exception ex)
{
    LoadOrderPath = previousPath;
    _stateService.UpdateConfigurationPaths(previousPath, Mo2Path, XEditPath);
    // existing safe error handling...
}
```

---

_Reviewed: 2026-05-01T06:46:55Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: standard_
