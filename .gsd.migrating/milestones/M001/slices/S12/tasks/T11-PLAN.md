# T11: 11-user-facing-diagnostics-boundaries 11

**Slice:** S12 — **Milestone:** M001

## Description

Close the remaining Phase 11 verification gap: xEdit log-read warnings that include a full main-log path must not flow into `PluginCleaningResult.LogParseWarning` and the ProgressWindow tooltip.

Purpose: SEC-01 requires user-facing progress/result diagnostics to avoid avoidable xEdit/profile-root path detail; D-14 still permits the raw main-log path in local logs because it is the direct failed local resource.
Output: Safe log-read warning copy at the finalizer/result boundary plus a regression test that proves path-bearing warning text stays out of user-facing tooltip data.

## Must-Haves

- [ ] "SEC-01 / D-09 / D-10: user-facing progress/result tooltip diagnostics replace path-bearing xEdit log-read warnings with concise latest-log copy."
- [ ] "SEC-01 / D-14: local logs may retain the raw xEdit main-log path as the direct failed local resource, but PluginCleaningResult.LogParseWarning must not expose it."

## Files

- `AutoQAC/Services/Cleaning/XEditLogFileService.cs`
- `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs`
- `AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs`
