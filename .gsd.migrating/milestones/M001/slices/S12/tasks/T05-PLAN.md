# T05: 11-user-facing-diagnostics-boundaries 05

**Slice:** S12 — **Milestone:** M001

## Description

Harden process/startup logs and migration warning copy.

Purpose: SEC-02 and D-13 through D-16 require AutoQAC logs to retain troubleshooting value without unnecessary executable path or command payload exposure, and SEC-01 requires migration warnings to avoid raw exception details.
Output: Tested safe process-start/failure logs, safe cleaning caller diagnostics, safe startup diagnostics, and safe migration warning text.

## Must-Haves

- [ ] "D-13: Process-start diagnostics do not log full executable paths, raw Arguments, raw ArgumentList entries, or nested MO2 payloads."
- [ ] "D-13: Cleaning-layer launch diagnostics preserve operation, launch mode, game, plugin filename, argument count, status, and safe reason/category without changing ProcessStartInfo construction."
- [ ] "D-13: Startup diagnostics replace configured executable paths with structured safe configured/basename fields."
- [ ] "D-14: Logs may keep full local paths only when the path is the direct failed resource and omitting it would materially reduce local troubleshooting value."
- [ ] "D-15: Normal workflow logs identify plugins by filename plus game/mode context, not full plugin path unless the plugin file path itself is the direct failing resource."
- [ ] "D-16: Behavioral logger-capture tests prove injectable process/cleaning log boundaries; source guards are reserved for private startup code only."
- [ ] "Legacy migration user warnings use safe latest-log guidance instead of raw exception messages."

## Files

- `AutoQAC/Services/Process/ProcessExecutionService.cs`
- `AutoQAC/Services/Cleaning/CleaningService.cs`
- `AutoQAC/App.axaml.cs`
- `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`
- `AutoQAC.Tests/Services/CleaningServiceTests.cs`
- `AutoQAC.Tests/Integration/DependencyInjectionTests.cs`
