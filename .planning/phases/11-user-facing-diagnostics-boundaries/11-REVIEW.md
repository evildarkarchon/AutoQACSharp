---
phase: 11-user-facing-diagnostics-boundaries
reviewed: 2026-04-30T00:00:00Z
depth: standard
files_reviewed: 26
files_reviewed_list:
  - AutoQAC.Tests/Helpers/DiagnosticSentinels.cs
  - AutoQAC.Tests/Integration/DependencyInjectionTests.cs
  - AutoQAC.Tests/Models/CleaningSessionResultTests.cs
  - AutoQAC.Tests/Models/DiagnosticTextFormatterTests.cs
  - AutoQAC.Tests/Models/Phase11ReportBoundaryTests.cs
  - AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs
  - AutoQAC.Tests/Services/CleaningServiceTests.cs
  - AutoQAC.Tests/Services/Phase11LogBoundaryTests.cs
  - AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs
  - AutoQAC.Tests/Services/CleaningOrchestratorTests.cs
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

**Reviewed:** 2026-04-30T00:00:00Z
**Depth:** standard
**Files Reviewed:** 26
**Status:** issues_found

## Summary

Reviewed the listed production and test files for user-facing diagnostic boundaries, behavioral regressions, and test coverage. The implementation still has two ship-blocking issues: the main-window preflight can block the Disable Skip Lists workflow before the orchestrator can honor it, and `ProcessExecutionService` can persist/log raw legacy argument strings on successful process starts.

## Critical Issues

### CR-01: Disable Skip Lists can still be blocked by pre-clean validation

**File:** `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs:399-418`
**Issue:** `ValidatePreClean` computes `selectedCount` by excluding every `p.IsInSkipList` plugin, but it has no awareness of the user's `DisableSkipLists` setting. If the loaded row set contains only skip-list plugins and the user enabled Disable Skip Lists, the ViewModel reports "No plugins selected" and never calls the orchestrator, even though `CleaningPreflight` is designed and tested to clean those plugins when skip lists are disabled. This is a behavioral regression at the UI boundary and the current tests only cover the orchestrator path, not the command preflight path.
**Fix:** Carry the Disable Skip Lists setting into runtime state (or otherwise into `CleaningCommandsViewModel`) and include it in this predicate; add a command-level regression test for all-skip-list plugins with Disable Skip Lists enabled.

```csharp
var disableSkipLists = state.DisableSkipListsEnabled;
var selectedCount = state.PluginsToClean
    .Count(p => !excluded.Contains(p.FullPath) && (disableSkipLists || !p.IsInSkipList));
```

### CR-02: Successful process starts can persist and log raw command arguments

**File:** `AutoQAC/Services/Process/ProcessExecutionService.cs:51-54,93,215-218,340`
**Issue:** When callers use `ProcessStartInfo.Arguments` rather than `ArgumentList` and do not pass `pluginName`, `GetArgumentSummary` returns the raw argument string. That raw payload is passed to `TrackProcessAsync`, persisted in the PID store as `PluginName`, and logged by `TrackProcessAsync`. This reintroduces exactly the kind of raw command/path disclosure Phase 11 is trying to remove for successful launches; the added tests only cover start-failure and `ArgumentList` success cases.
**Fix:** Never use raw argument text as the tracking label. Track a caller-provided safe plugin name or a constant safe label, and make `GetArgumentSummary` count-only/safe if it remains needed.

```csharp
var trackingLabel = string.IsNullOrWhiteSpace(pluginName)
    ? "ExternalProcess"
    : DiagnosticTextFormatter.SafePluginName(pluginName);

await TrackProcessAsync(process, trackingLabel, ct).ConfigureAwait(false);

private static string GetArgumentSummary(ProcessStartInfo startInfo) =>
    startInfo.ArgumentList.Count > 0
        ? $"{startInfo.ArgumentList.Count} argument-list entries"
        : string.IsNullOrWhiteSpace(startInfo.Arguments) ? "no arguments" : "1 legacy argument string";
```

## Warnings

### WR-01: Already-clean plugins are duplicated in generated reports

**File:** `AutoQAC/Models/CleaningSessionResult.cs:179-196`
**Issue:** `CleanedPlugins` intentionally includes `CleaningStatus.AlreadyClean`, then `GenerateReport` emits both a `--- Cleaned Plugins ---` section for `CleanedPlugins` and a separate `--- Already Clean Plugins ---` section for `AlreadyCleanPlugins`. Any already-clean plugin appears twice in the exported report, which makes user-facing session accounting misleading.
**Fix:** Either remove the separate already-clean section or restrict the cleaned section to only actual `CleaningStatus.Cleaned` rows.

```csharp
var actuallyCleanedPlugins = PluginResults.Where(r => r.Status == CleaningStatus.Cleaned);
if (actuallyCleanedPlugins.Any())
{
    sb.AppendLine("--- Cleaned Plugins ---");
    foreach (var result in actuallyCleanedPlugins)
    {
        sb.AppendLine($"  {result.PluginName}: {result.Summary} ({result.Duration:mm\\:ss})");
    }
    sb.AppendLine();
}
```

### WR-02: CleaningService returns unsanitized plugin names in user-facing failure text

**File:** `AutoQAC/Services/Cleaning/CleaningService.cs:58-68,171-176`
**Issue:** Command-build failures and unexpected launch exceptions put `plugin.FileName` directly into `CleaningResult.Message`. Plugin names normally come from trusted load-order parsing, but they can still originate from external files/session data and may contain control characters or path-like content. Other Phase 11 surfaces use `DiagnosticTextFormatter.SafePluginName`, so these direct interpolations leave a disclosure/sanitization gap.
**Fix:** Sanitize once and use the safe name in every user-facing result message while preserving raw details only in structured logs.

```csharp
var safePluginName = DiagnosticTextFormatter.SafePluginName(plugin.FileName);
return new CleaningResult
{
    Success = false,
    Status = CleaningStatus.Failed,
    Message = $"Cleaning failed for {safePluginName}. See logs for technical details.",
    Duration = sw.Elapsed
};
```

---

_Reviewed: 2026-04-30T00:00:00Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: standard_
