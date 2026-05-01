---
phase: 11-user-facing-diagnostics-boundaries
verified: 2026-05-01T06:51:40Z
status: gaps_found
score: 30/31 must-haves verified
overrides_applied: 0
re_verification:
  previous_status: gaps_found
  previous_score: 28/31
  gaps_closed:
    - "Legacy migration user warnings use safe latest-log guidance instead of raw exception messages."
    - "Process-start diagnostics do not log or persist raw Arguments or command payloads on successful starts."
    - "User-facing reports and result summaries sanitize displayed plugin basenames when formatting failed rows."
  gaps_remaining: []
  regressions:
    - "Review CR-01 verified: CleaningService can still create failed CleaningResult.Message values with raw plugin filenames before PluginResultFinalizer/PluginCleaningResult.Summary/report formatting."
gaps:
  - truth: "Failed PluginCleaningResult.Message values are sanitized at source before reaching result windows or CleaningSessionResult.GenerateReport()."
    status: partial
    reason: "PluginResultFinalizer sanitizes messages that contain paths, commands, exception types, or stack markers, but CleaningService still constructs failed CleaningResult.Message values with raw plugin.FileName. A plugin basename containing quotes/backticks/control-command display punctuation but no path/command sentinel is not rejected by SafeFailureSummary, so it can survive into PluginCleaningResult.Message, Summary, and report failed-row text."
    artifacts:
      - path: "AutoQAC/Services/Cleaning/CleaningService.cs"
        issue: "Build-command failure and unexpected-exception paths interpolate raw plugin.FileName into user-facing CleaningResult.Message."
      - path: "AutoQAC/Services/Cleaning/PluginResultFinalizer.cs"
        issue: "For failed results, SafeFailureSummary(resultMessage, fallback) preserves candidate text unless it matches path/command/exception heuristics; unsafe display characters in plugin basenames are not sufficient to force fallback."
      - path: "AutoQAC/Models/PluginCleaningResult.cs"
        issue: "Failed Summary uses SafeFailureSummary(Message, CleaningFailedForPlugin(PluginName)); the same raw message can be preserved when it lacks the formatter's unsafe-detail sentinels."
    missing:
      - "Use DiagnosticTextFormatter.CleaningFailedForPlugin(plugin.FileName) or a sanitized safePluginName in CleaningService failed result messages."
      - "Add a regression test where CleaningService returns a failed message for a plugin basename containing quotes/backticks/control characters and assert PluginResultFinalizer/PluginCleaningResult.Summary/report output excludes the raw basename characters."
---

# Phase 11: User-Facing Diagnostics Boundaries Verification Report

**Phase Goal:** Users receive concise, actionable error messages while logs retain local troubleshooting value without unnecessary full path or command-line exposure.  
**Verified:** 2026-05-01T06:51:40Z  
**Status:** gaps_found  
**Re-verification:** Yes — after gap-closure plans 11-07, 11-08, and 11-09

## Goal Achievement

Phase 11 closed all three previously recorded verification gaps. Legacy migration warnings now use safe latest-log copy, successful process starts no longer use legacy `Arguments` as PID labels, and generated reports sanitize plugin display prefixes in cleaned/skipped/failed rows.

However, the execute-phase review's blocker-class CR-01 is verified against the current codebase as a remaining diagnostic-boundary gap: `CleaningService` still creates failed `CleaningResult.Message` text from raw `plugin.FileName`, and the downstream safety checks only force fallback for path/command/exception/stack sentinels, not unsafe basename display characters.

### Observable Truths

| # | Truth | Status | Evidence |
|---|---|---|---|
| 1 | User sees concise error dialogs with a log-file reference rather than stack traces or excessive internal path detail. | ⚠️ PARTIAL | Main dialogs/status/migration warnings are safe (`CleaningCommandsViewModel.cs`, `App.axaml.cs:170-178`, `LegacyMigrationService.cs:23-26`), but failed cleaning result messages can still carry raw plugin basename display characters from `CleaningService.cs:68,176` into result/report surfaces. |
| 2 | User diagnostic logs avoid unnecessary full command-line exposure while preserving enough context for local troubleshooting. | ✓ VERIFIED | `ProcessExecutionService.cs:61-93` logs operation/status/PID/argument count only; `GetSafeTrackingLabel` returns sanitized plugin names or `ExternalProcess` (`ProcessExecutionService.cs:220-228`). |
| 3 | User-facing exports and dialogs avoid exposing avoidable profile-root, game-install, xEdit, MO2, and plugin path detail unless needed for diagnosis. | ⚠️ PARTIAL | Report plugin prefixes are sanitized (`CleaningSessionResult.cs:184,195,205,245-251`), but raw plugin filenames embedded in `CleaningService` failed messages can still survive if they contain unsafe display characters without path/command sentinels. |
| 4 | Shared formatter builds safe operation, setting, folder, plugin, and report copy without raw exception/path/command detail. | ✓ VERIFIED | `DiagnosticTextFormatter.cs` provides `OperationFailed`, safe identifiers, `SafePluginName`, `CleaningFailedForPlugin`, `XEditReportedError`, and `SafeFailureSummary`. |
| 5 | Legacy migration user warnings use safe latest-log guidance instead of raw exception messages. | ✓ VERIFIED | `LegacyMigrationService.cs:23-26,95,107,124,146,163` uses fixed warnings; `App.axaml.cs:170-172` defensively applies `SafeFailureSummary` before `ShowMigrationWarning`. |
| 6 | Successful process starts never persist or log raw legacy Arguments when pluginName is omitted. | ✓ VERIFIED | `ProcessExecutionService.cs:93` calls `TrackProcessAsync(process, GetSafeTrackingLabel(pluginName), ct)`; no `pluginName ?? arguments` remains; tests assert `ExternalProcess`. |
| 7 | Reports sanitize displayed plugin basenames before formatting cleaned, skipped, already-clean, and failed rows. | ✓ VERIFIED | `CleaningSessionResult.cs:184,195,205,245-251` uses `DiagnosticTextFormatter.SafePluginName(result.PluginName)` for every report row prefix/fallback comparison. |
| 8 | Phase-level sentinel tests exercise real ViewModel, model, and logger paths. | ✓ VERIFIED | Phase 11 guard tests run against real ViewModel/model/process-log seams and pass. |
| 9 | Full solution tests pass. | ✓ VERIFIED | `dotnet test AutoQACSharp.slnx --nologo` passed: QueryPlugins.Tests 61/61, AutoQAC.Tests 990/990. |

**Score:** 30/31 must-have truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|---|---|---|---|
| `AutoQAC/Models/Diagnostics/DiagnosticTextFormatter.cs` | Shared safe formatter | ✓ VERIFIED | Substantive and widely wired; includes safe plugin/report helpers and unsafe-detail heuristics. |
| `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` | Safe command-boundary dialogs/status/validation | ✓ VERIFIED | Unexpected cleaning/preview and pre-clean validation use formatter copy. |
| `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs` | Safe browse diagnostics and migration display state | ✓ VERIFIED | Uses safe load-order and folder issue copy; migration warning text is assigned after startup sanitization. |
| `AutoQAC/ViewModels/RestoreViewModel.cs` | Safe restore session loading status | ✓ VERIFIED | No raw load-session `ex.Message` path remains in the checked restore load boundary. |
| `AutoQAC/ViewModels/SettingsViewModel.cs` | Safe persistence banner | ✓ VERIFIED | Persistence text maps from `ConfigPersistenceFailure.SafeSummary`. |
| `AutoQAC/Services/Configuration/LegacyMigrationService.cs` | Safe migration warning categories | ✓ VERIFIED | Parse/write/backup/delete warning messages are fixed latest-log categories. |
| `AutoQAC/App.axaml.cs` | Safe startup log and migration warning boundary | ✓ VERIFIED | Startup xEdit log uses safe identifier; migration warning is passed through `SafeFailureSummary`. |
| `AutoQAC/Services/Process/ProcessExecutionService.cs` | Safe process-start/failure logging and PID labels | ✓ VERIFIED | Uses safe structured logs and `GetSafeTrackingLabel`; no raw legacy argument label fallback remains. |
| `AutoQAC/Models/CleaningSessionResult.cs` | Defensive report disclaimer and sanitized plugin prefixes | ✓ VERIFIED | Report disclaimer present; plugin row prefixes and failed fallback use sanitized display names. |
| `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs` | Source sanitization for failed result rows | ⚠️ PARTIAL | Path/command/exception unsafe messages are sanitized, but raw display-unsafe plugin basenames from `CleaningService` messages can pass through. |
| `AutoQAC/Services/Cleaning/CleaningService.cs` | Caller-side safe launch diagnostics/result messages | ✗ FAILED | Logs use sanitized plugin names, but failed `CleaningResult.Message` values still interpolate raw `plugin.FileName` at lines 68 and 176. |
| Phase 11 tests | Regression guards | ⚠️ PARTIAL | Existing tests pass and cover previous gaps, but no test covers `CleaningService` failed messages with unsafe basename characters flowing through finalizer/summary/report. |

### Key Link Verification

| From | To | Via | Status | Details |
|---|---|---|---|---|
| `LegacyMigrationService` | `App.RunMigrationAsync` | `MigrationResult.WarningMessage` | ✓ WIRED | Service emits safe warnings; `App.axaml.cs` applies `SafeFailureSummary` before `ShowMigrationWarning`. |
| `ProcessExecutionService.ExecuteAsync` | `IPidStore.UpdateAsync` | `TrackProcessAsync` label | ✓ WIRED | Safe label selection is used for successful starts. |
| `ProcessExecutionService.ExecuteAsync` | `ILoggingService` | Structured safe fields | ✓ WIRED | Logs operation/status/PID/argument count, not filename/arguments. |
| `CleaningSessionResult.GenerateReport` | `DiagnosticTextFormatter.SafePluginName` | Report row display prefixes | ✓ WIRED | Cleaned, already-clean, skipped, and failed sections use sanitized names. |
| `CleaningService.CleanPluginAsync` | `PluginResultFinalizer` | `CleaningResult.Message` | ⚠️ PARTIAL | Finalizer catches path/command/exception unsafe strings, but does not force fallback for raw display-unsafe plugin basenames inside otherwise plain messages. |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|---|---|---|---|---|
| `App.axaml.cs` migration warning | `warningMessage` | `MigrationResult.WarningMessage` → `SafeFailureSummary` | Yes | ✓ FLOWING safely. |
| `ProcessExecutionService` PID label | `pluginName` | caller parameter → `GetSafeTrackingLabel` | Yes | ✓ FLOWING safely or `ExternalProcess`. |
| `CleaningSessionResult` report plugin name | `result.PluginName` | `PluginResults` | Yes | ✓ FLOWING through `SafePluginName`. |
| `PluginResultFinalizer` failed message | `resultMessage` | `CleaningResult.Message` from runner/service | Yes | ⚠️ UNSAFE EDGE — raw plugin filename text can remain when it lacks path/command/exception sentinel patterns. |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|---|---|---|---|
| Gap-closure focused tests | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~LegacyMigrationServiceTests|FullyQualifiedName~DependencyInjectionTests|FullyQualifiedName~ProcessExecutionServiceTests|FullyQualifiedName~Phase11LogBoundaryTests|FullyQualifiedName~CleaningSessionResultTests|FullyQualifiedName~Phase11ReportBoundaryTests" --nologo` | Passed: 58/58 | ✓ PASS |
| Phase 11 guard tests | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Phase11" --nologo` | Passed: 6/6 on sequential rerun | ✓ PASS |
| Full solution suite | `dotnet test AutoQACSharp.slnx --nologo` | Passed: 1051/1051 total | ✓ PASS |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|---|---|---|---|---|
| SEC-01 | 11-01, 11-02, 11-03, 11-04, 11-05, 11-06, 11-07, 11-09 | User sees concise error dialogs with log-file references instead of stack traces or excessive internal path detail. | ✗ BLOCKED | Previously failing migration/report-prefix paths are fixed, but verified CR-01 leaves failed cleaning result messages capable of raw plugin basename disclosure. |
| SEC-02 | 11-01, 11-05, 11-06, 11-08 | User diagnostic logs avoid unnecessary full command-line/path exposure while preserving local troubleshooting value. | ✓ SATISFIED | Process/startup logs and PID tracking use safe structured fields and labels; targeted and full tests pass. |

No orphaned Phase 11 requirement IDs were found. `.planning/REQUIREMENTS.md` maps `SEC-01` and `SEC-02` to Phase 11, and both IDs appear in Phase 11 plan frontmatter.

### Code Review Findings Adjudication

| Review Finding | Verdict | Verification Evidence |
|---|---|---|
| CR-01: Raw plugin filename can cross the failed-launch diagnostic boundary | 🛑 TRUE GAP | `CleaningService.cs:68,176` still interpolates raw `plugin.FileName`; downstream `SafeFailureSummary` does not reject quotes/backticks/control-display issues in otherwise plain messages. |
| WR-01: Already-clean plugins duplicated in generated reports | ⚠️ Warning / outside diagnostics disclosure goal | Still a report accounting issue, but it does not determine path/command/error-message boundary achievement. |
| WR-02: Failed load-order selection leaves invalid runtime state behind | ⚠️ Warning / outside diagnostics disclosure goal | Important state-consistency issue, but not a diagnostic disclosure blocker for this phase. |

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|---|---:|---|---|---|
| `AutoQAC/Services/Cleaning/CleaningService.cs` | 68, 176 | Raw `plugin.FileName` in failed `CleaningResult.Message` | 🛑 Blocker | Can become user-facing result/report text for unsafe basenames that do not trigger `SafeFailureSummary` fallback. |

Other stub-pattern matches (`return null`, empty arrays) are existing legitimate control-flow/default values and not Phase 11 placeholders.

### Human Verification Required

None. The remaining gap is source-verifiable and should be covered with a focused automated regression test.

### Gaps Summary

The three prior verification gaps are closed, but Phase 11 still cannot pass because review CR-01 is a real diagnostic-boundary hole. Fix `CleaningService` failed result messages to use `DiagnosticTextFormatter` safe copy/sanitized plugin names, then add a regression that follows the message through `PluginResultFinalizer`, `PluginCleaningResult.Summary`, and `CleaningSessionResult.GenerateReport()`.

---

_Verified: 2026-05-01T06:51:40Z_  
_Verifier: the agent (gsd-verifier)_
