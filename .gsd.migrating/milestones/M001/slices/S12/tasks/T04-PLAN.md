# T04: 11-user-facing-diagnostics-boundaries 04

**Slice:** S12 — **Milestone:** M001

## Description

Harden cleaning result rows and exported reports.

Purpose: D-09 through D-12 require failed plugin messages to be safe at source before they reach result windows or `GenerateReport()`, with report-level defensive checks and a one-line disclaimer.
Output: Tested safe plugin failure messages, xEdit exception-log handling, result summaries, and exported report text.

## Must-Haves

- [ ] "D-09: Failed result rows show plugin filenames and safe summaries/latest-log guidance only."
- [ ] "D-10: Failed PluginCleaningResult.Message values are sanitized at source before reaching result windows or CleaningSessionResult.GenerateReport()."
- [ ] "D-11: xEdit exception-log content is converted to a safe xEdit failure message and never appears in result rows or reports."
- [ ] "D-12: Exported cleaning reports include one short disclaimer and do not repeat technical details."

## Files

- `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs`
- `AutoQAC/Models/CleaningSessionResult.cs`
- `AutoQAC/Models/PluginCleaningResult.cs`
- `AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs`
- `AutoQAC.Tests/Models/CleaningSessionResultTests.cs`
