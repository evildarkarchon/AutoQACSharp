# T10: 11-user-facing-diagnostics-boundaries 10

**Slice:** S12 — **Milestone:** M001

## Description

Close the remaining Phase 11 verification gap: `CleaningService` must not create failed `CleaningResult.Message` text from raw plugin filenames that can survive into result rows, summaries, or exported reports.

Purpose: SEC-01 and D-10 require failed plugin messages to be sanitized at source before result-window/report boundaries consume them; Plan 11-09 sanitized report prefixes, but verification found service-created failed messages can still carry unsafe basename display characters.
Output: Safe `CleaningService` failed-result messages plus regression coverage through service, finalizer, summary, and report surfaces.

## Must-Haves

- [ ] "SEC-01 / D-07 / D-09 / D-10: failed CleaningResult.Message values created by CleaningService sanitize plugin display names before PluginResultFinalizer, PluginCleaningResult.Summary, or CleaningSessionResult.GenerateReport can show them."
- [ ] "SEC-01 / D-01 / D-02 / D-03: build-command and unexpected cleaning failures preserve concise latest-log guidance without raw plugin basename control characters, quotes, backticks, command fragments, or path detail."

## Files

- `AutoQAC/Services/Cleaning/CleaningService.cs`
- `AutoQAC.Tests/Services/CleaningServiceTests.cs`
- `AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs`
- `AutoQAC.Tests/Models/CleaningSessionResultTests.cs`
