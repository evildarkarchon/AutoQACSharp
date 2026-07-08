---
phase: 06-command-launch-escaping
reviewed: 2026-04-28T00:00:00Z
depth: standard
files_reviewed: 7
files_reviewed_list:
  - AutoQAC/Services/Cleaning/CleaningService.cs
  - AutoQAC/Services/Cleaning/XEditCommandBuilder.cs
  - AutoQAC/Services/Process/ProcessExecutionService.cs
  - AutoQAC.Tests/Services/CleaningServiceTests.cs
  - AutoQAC.Tests/Services/XEditCommandBuilderTests.cs
  - AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs
  - AutoQAC.Tests/TestProcessHelper/Program.cs
findings:
  critical: 1
  warning: 2
  info: 0
  total: 3
status: issues_found
---

# Phase 6: Code Review Report

**Reviewed:** 2026-04-28T00:00:00Z
**Depth:** standard
**Files Reviewed:** 7
**Status:** issues_found

## Summary

Reviewed the command-building, process-launch, and supporting tests for Phase 6. The move to `ArgumentList` correctly removes most shell-quoting exposure, but there is a blocker in MO2 mode: a missing MO2 executable silently falls back to direct xEdit launch. I also found two regressions around process metadata/safe failure handling that the current tests can miss.

## Critical Issues

### CR-01: MO2 mode silently falls back to direct xEdit when MO2 path is missing

**File:** `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs:35-52`
**Issue:** When `Mo2ModeEnabled` is true but `Mo2ExecutablePath` is empty, the builder skips the MO2 branch and returns a direct xEdit `ProcessStartInfo`. This violates the selected launch mode and can clean outside MO2's virtual filesystem. Because project behavior says backups are skipped in MO2 mode, this fallback creates a data-loss risk: the app can believe it is running in MO2 mode while launching direct xEdit against the real game data.
**Fix:** Treat enabled MO2 mode without a configured MO2 executable as an unbuildable command and return `null` so `CleaningService` fails safely without starting a process.

```csharp
if (config.Mo2ModeEnabled)
{
    if (string.IsNullOrWhiteSpace(config.Mo2ExecutablePath))
    {
        return null;
    }

    var startInfo = new ProcessStartInfo
    {
        FileName = config.Mo2ExecutablePath,
        WorkingDirectory = Path.GetDirectoryName(config.Mo2ExecutablePath),
        UseShellExecute = false
    };

    startInfo.ArgumentList.Add("run");
    startInfo.ArgumentList.Add(xEditPath);
    startInfo.ArgumentList.Add("-a");
    startInfo.ArgumentList.Add(BuildMo2NestedPayload(args));
    return startInfo;
}
```

## Warnings

### WR-01: CleaningService no longer passes plugin identity to process tracking

**File:** `AutoQAC/Services/Cleaning/CleaningService.cs:84`
**Issue:** `ProcessExecutionService.ExecuteAsync` accepts `pluginName` specifically so PID tracking and orphan evidence can store the real plugin name. `CleaningService` omits it, and Phase 6 changed argument logging/tracking fallback to `"N argument-list entries"` for `ArgumentList` launches (`ProcessExecutionService.cs:76`, `ProcessExecutionService.cs:198-201`). As a result, tracked PID entries for direct/MO2 ArgumentList launches lose the plugin identity and can record only an argument count. This degrades orphan diagnostics and can make user-cancellation evidence ambiguous. The current `CleaningServiceTests` also assert only the legacy optional-argument call shape, so this regression can pass falsely.
**Fix:** Pass the plugin file name explicitly and update tests to match optional parameters explicitly, including `pluginName`.

```csharp
var result = await processService.ExecuteAsync(
    command,
    timeout,
    ct,
    onProcessStarted,
    plugin.FileName).ConfigureAwait(false);
```

### WR-02: Unexpected exception messages are returned directly to the UI

**File:** `AutoQAC/Services/Cleaning/CleaningService.cs:132-140`
**Issue:** The generic exception handler returns `ex.Message` as the user-facing cleaning result. Phase 6 added safe command-build failures, but exceptions from command building or process execution can still include configured executable paths, working directories, or command-line details. That creates an inconsistent disclosure boundary: expected build failures are sanitized, while unexpected failures can leak the same sensitive launch details into the UI.
**Fix:** Log the detailed exception, but return a generic safe failure message that includes only the plugin name and directs the user to logs.

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

---

_Reviewed: 2026-04-28T00:00:00Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: standard_
