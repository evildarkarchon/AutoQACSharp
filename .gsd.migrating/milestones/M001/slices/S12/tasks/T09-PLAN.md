# T09: 11-user-facing-diagnostics-boundaries 09

**Slice:** S12 — **Milestone:** M001

## Description

Close Phase 11 verification gap #3: user-facing reports and failed result summaries must sanitize displayed plugin-name prefixes/fallbacks.

Purpose: SEC-01 and D-07/D-09 require report/result display names to preserve useful plugin filenames while stripping path separators, command-like punctuation, quotes/backticks, and control characters.
Output: Safe plugin display names in report rows and regression tests for malicious/path-like plugin names.

## Must-Haves

- [ ] "SEC-01 / D-07 / D-09: user-facing reports and result summaries sanitize displayed plugin basenames before formatting cleaned, skipped, already-clean, and failed rows."
- [ ] "SEC-01 / D-10 / D-12: reports keep the existing disclaimer and safe failed-message fallback while preserving raw plugin names only in internal model data."

## Files

- `AutoQAC/Models/CleaningSessionResult.cs`
- `AutoQAC/Models/PluginCleaningResult.cs`
- `AutoQAC.Tests/Models/CleaningSessionResultTests.cs`
- `AutoQAC.Tests/Models/Phase11ReportBoundaryTests.cs`
