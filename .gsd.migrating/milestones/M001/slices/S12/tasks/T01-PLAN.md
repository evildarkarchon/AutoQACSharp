# T01: 11-user-facing-diagnostics-boundaries 01

**Slice:** S12 — **Milestone:** M001

## Description

Create the shared safe diagnostics text boundary required by Phase 11.

Purpose: Later plans need one executable source of truth for safe operation messages, sanitized basenames, setting identifiers, plugin failure summaries, latest-log guidance, and export disclaimer wording.
Output: Tested formatter helpers that implement D-01 through D-08, D-09 through D-12 copy primitives, and D-13 through D-16 safe structured-field primitives without changing app behavior yet.

## Must-Haves

- [ ] "User-facing diagnostic copy can be built from safe operation, setting, folder, plugin, and report helpers without raw exception text, stack traces, full paths, or command fragments."
- [ ] "Displayed basenames preserve useful names such as Plugin.esp, SSEEdit.exe, and plugins.txt while neutralizing control characters, quotes, backticks, and empty names."
- [ ] "Logs and reports have shared safe wording constants for latest AutoQAC log guidance and the export disclaimer."

## Files

- `AutoQAC/Models/Diagnostics/DiagnosticTextFormatter.cs`
- `AutoQAC.Tests/Models/DiagnosticTextFormatterTests.cs`
