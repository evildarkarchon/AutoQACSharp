---
phase: 11-user-facing-diagnostics-boundaries
reviewed: 2026-05-01T00:00:00Z
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
  critical: 2
  warning: 2
  info: 0
  total: 4
status: issues_found
---

# Phase 11: Code Review Report

**Reviewed:** 2026-05-01T00:00:00Z
**Depth:** deep
**Files Reviewed:** 27
**Status:** issues_found

## Summary

Deep review covered the listed production files and their Phase 11 regression tests, including cross-file tracing from view models into dialog services and result formatting. The implementation still has user-facing diagnostic boundary leaks on backup/timeout flows, and the generated cleaning report double-lists `AlreadyClean` plugins.

## Critical Issues

### CR-01: Backup failure dialog receives raw plugin/error text

**File:** `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs:467-468`

**Issue:** `HandleBackupFailureAsync` forwards `pluginName` and `errorMessage` directly to `IMessageDialogService.ShowBackupFailureDialogAsync`. The concrete dialog service renders both values verbatim in text blocks, and backup failures commonly originate from filesystem exceptions that can include absolute paths, access-denied details, or exception text. This bypasses the Phase 11 diagnostic boundary and can expose local paths/technical details in a modal dialog.

**Fix:** Sanitize the plugin name and replace raw error detail with safe latest-log copy before invoking the dialog (or move the same sanitization into `MessageDialogService`).

```csharp
private async Task<BackupFailureChoice> HandleBackupFailureAsync(string pluginName, string errorMessage)
{
    var safePluginName = DiagnosticTextFormatter.SafePluginName(pluginName);
    var safeMessage = DiagnosticTextFormatter.SafeFailureSummary(
        errorMessage,
        "Backup could not be created. See the latest AutoQAC log for technical details.");

    return await _messageDialog.ShowBackupFailureDialogAsync(safePluginName, safeMessage);
}
```

### CR-02: Timeout retry dialog can expose unsanitized plugin names

**File:** `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs:452-464`

**Issue:** `HandleTimeoutRetryAsync` interpolates the callback-provided `pluginName` directly into the retry dialog. The callback boundary is not constrained to a basename, and a malformed/plugin-sourced value containing a path, command fragment, quote/control character, or `-QAC` payload would be shown to the user. This is the same disclosure class Phase 11 is intended to prevent.

**Fix:** Normalize the callback value through `DiagnosticTextFormatter.SafePluginName` before building dialog text.

```csharp
private async Task<bool> HandleTimeoutRetryAsync(string pluginName, int timeoutSeconds, int attemptNumber)
{
    var safePluginName = DiagnosticTextFormatter.SafePluginName(pluginName);
    var message = $"Cleaning of '{safePluginName}' timed out after {timeoutSeconds} seconds.\n\n" +
                  $"Attempt {attemptNumber} of 3 failed.\n\n" +
                  "Would you like to retry cleaning this plugin?";

    // unchanged details...
    return await _messageDialog.ShowRetryAsync("Plugin Timeout", message, details);
}
```

## Warnings

### WR-01: Already-clean plugins are listed twice in exported reports

**File:** `AutoQAC/Models/CleaningSessionResult.cs:179-198`

**Issue:** `CleanedPlugins` intentionally includes both `Cleaned` and `AlreadyClean`, but `GenerateReport()` uses `CleanedPlugins` for the `--- Cleaned Plugins ---` section and then separately emits an `--- Already Clean Plugins ---` section. Any `AlreadyClean` result therefore appears in both report sections, making exports misleading.

**Fix:** Use a strict cleaned-only filter for the cleaned section, or remove the separate already-clean section. For example:

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

### WR-02: Restore confirmation/status text renders backup metadata filenames verbatim

**File:** `AutoQAC/ViewModels/RestoreViewModel.cs:195-223,244-263`

**Issue:** Restore UI text interpolates `BackupPluginEntry.FileName` directly in confirmation dialogs, status text, log message templates, and failure dialogs. `BackupPluginEntry` is loaded from `session.json` metadata, so a corrupted or hand-edited backup session can place a path-like or control-character-bearing value in `file_name` and have it displayed to the user. This is less severe than the active cleaning failure paths because it requires bad backup metadata, but it is still a user-facing diagnostic boundary gap.

**Fix:** Sanitize backup display names before rendering them in dialogs/status text, while continuing to pass the original `BackupPluginEntry` object to `IBackupService` for restore behavior.

```csharp
var safePluginName = DiagnosticTextFormatter.SafePluginName(plugin.FileName);
var confirmed = await _messageDialog.ShowConfirmAsync(
    "Restore Selected",
    $"Restore Selected: Restore {safePluginName} from {timestamp}? This overwrites the current plugin file with the backup copy.");

StatusText = $"Restoring: {safePluginName}";
RestoreProgressText = "Restoring 1 / 1 plugins";
```

---

_Reviewed: 2026-05-01T00:00:00Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: deep_
