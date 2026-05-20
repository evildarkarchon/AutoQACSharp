# T08: 11-user-facing-diagnostics-boundaries 08

**Slice:** S12 — **Milestone:** M001

## Description

Close Phase 11 verification gap #2: successful process-start diagnostics must not log or persist raw legacy `Arguments` payloads.

Purpose: SEC-02 and D-13 require process/startup logs and PID tracking data to use safe structured fields and labels without raw command payload exposure.
Output: Safe PID tracking label selection and successful-start regression tests for legacy `ProcessStartInfo.Arguments` with omitted `pluginName`.

## Must-Haves

- [ ] "SEC-02 / D-13 / D-16: successful process starts never persist or log raw legacy Arguments when pluginName is omitted."
- [ ] "SEC-02 / D-15: PID tracking labels are sanitized plugin names when available and `ExternalProcess` otherwise."

## Files

- `AutoQAC/Services/Process/ProcessExecutionService.cs`
- `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`
- `AutoQAC.Tests/Services/Phase11LogBoundaryTests.cs`
