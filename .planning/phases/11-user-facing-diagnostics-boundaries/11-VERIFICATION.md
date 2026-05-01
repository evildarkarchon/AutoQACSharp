---
phase: 11-user-facing-diagnostics-boundaries
verified: 2026-05-01T07:18:24Z
status: gaps_found
score: 30/31 must-haves verified
overrides_applied: 0
re_verification:
  previous_status: verified
  previous_score: 31/31
  gaps_closed: []
  gaps_remaining:
    - "Raw xEdit main-log paths still flow into user-facing progress/result tooltips through LogParseWarning."
  regressions:
    - "Latest code review CR-01 is confirmed in code and was missed by the prior verification."
gaps:
  - truth: "User-facing progress/result diagnostics avoid exposing avoidable xEdit/profile-root path detail."
    status: failed
    reason: "XEditLogFileService returns a warning containing the full xEdit main-log path; PluginResultFinalizer copies it directly into PluginCleaningResult.LogParseWarning; ProgressWindow binds LogParseWarning to ToolTip.Tip, making the raw path user-facing."
    artifacts:
      - path: "AutoQAC/Services/Cleaning/XEditLogFileService.cs"
        issue: "ReadLogContentAsync returns Warning = $\"Main log file not found: {mainLogPath}\"."
      - path: "AutoQAC/Services/Cleaning/PluginResultFinalizer.cs"
        issue: "Lines 37-40 log the warning and assign logParseWarning = logResult.Warning without sanitizing or replacing it with safe latest-log copy."
      - path: "AutoQAC/Views/ProgressWindow.axaml"
        issue: "Lines 199-200 and 365-366 bind ToolTip.Tip directly to LogParseWarning."
    missing:
      - "Replace user-facing LogParseWarning text for log-read warnings with stable safe copy such as 'xEdit log could not be read. See the latest AutoQAC log.' while retaining raw warning/path only in local logs if useful."
      - "Add a regression test covering LogReadResult.Warning with a path-bearing main-log warning through PluginResultFinalizer and the bound result value."
---

# Phase 11: User-Facing Diagnostics Boundaries Verification Report

**Phase Goal:** Users receive concise, actionable error messages while logs retain local troubleshooting value without unnecessary full path or command-line exposure.  
**Verified:** 2026-05-01T07:18:24Z  
**Status:** gaps_found  
**Re-verification:** Yes — rechecked after existing `11-VERIFICATION.md` and latest `11-REVIEW.md`.

## Goal Achievement

Phase 11 is **not fully achieved**. Most dialog/status/report/process-log boundaries are implemented and tested, but the latest review's CR-01 is a real in-scope blocker: a missing xEdit main-log warning still carries a full local path through `PluginCleaningResult.LogParseWarning` into `ProgressWindow` tooltips.

### Observable Truths

| # | Truth | Status | Evidence |
|---|---|---|---|
| 1 | User sees concise error dialogs with a log-file reference rather than stack traces or excessive internal path detail. | ✓ VERIFIED | `CleaningCommandsViewModel.cs:157-166,205-214` uses `DiagnosticTextFormatter.OperationFailed(...)` plus `LatestLogDetails`; legacy migration warning display is passed through `SafeFailureSummary` in `App.axaml.cs:167-172`. |
| 2 | User diagnostic logs avoid unnecessary full command-line exposure while preserving enough context for local troubleshooting. | ✓ VERIFIED | `ProcessExecutionService.cs:61-93` logs operation/status/PID/argument count; `CleaningService.cs:82-148` logs QuickAutoClean mode/game/sanitized plugin/argument count/status/reason rather than raw argv. |
| 3 | User-facing exports and dialogs avoid exposing avoidable profile-root, game-install, xEdit, MO2, and plugin path detail unless needed for diagnosis. | ✗ FAILED | `XEditLogFileService.cs:79-84` creates `Warning = "Main log file not found: {mainLogPath}"`; `PluginResultFinalizer.cs:37-40` copies it to `LogParseWarning`; `ProgressWindow.axaml:199-200,365-366` exposes it in `ToolTip.Tip`. |
| 4 | Shared formatter builds safe operation, setting, folder, plugin, and report copy without raw exception/path/command detail. | ✓ VERIFIED | `DiagnosticTextFormatter.cs:17-117` implements latest-log/report constants, safe identifiers, safe plugin names, and unsafe-detail fallback checks. |
| 5 | Legacy migration user warnings use safe latest-log guidance instead of raw exception messages. | ✓ VERIFIED | `LegacyMigrationService.cs:23-26,92-108,121-126,143-147,160-164` returns fixed safe warning strings; `App.axaml.cs:170-172` defensively sanitizes warning text before display. |
| 6 | Successful process starts never persist or log raw legacy Arguments when pluginName is omitted. | ✓ VERIFIED | `ProcessExecutionService.cs:93,220-228` uses `GetSafeTrackingLabel(pluginName)` and `ExternalProcess`; grep found no `pluginName ?? arguments`. |
| 7 | Reports sanitize displayed plugin basenames before formatting cleaned, skipped, already-clean, and failed rows. | ✓ VERIFIED | `CleaningSessionResult.cs:184-185,195,205,243-251` uses `DiagnosticTextFormatter.SafePluginName`; `PluginCleaningResult.cs:89-90` uses safe fallback. |
| 8 | Failed CleaningResult.Message values created by CleaningService sanitize plugin display names before finalizer/summary/report consumption. | ✓ VERIFIED | `CleaningService.cs:55-70,169-178` uses `SafePluginName` and `CleaningFailedForPlugin`; downstream finalizer/report paths preserve safe summaries. |
| 9 | Phase-level sentinel tests exercise real ViewModel, model, and logger paths. | ✓ VERIFIED | `Phase11DiagnosticsBoundaryTests`, `Phase11ReportBoundaryTests`, and `Phase11LogBoundaryTests` exercise real production seams, though they do not cover the failing `LogReadResult.Warning` path. |
| 10 | Full/focused automated tests pass. | ✓ VERIFIED | Spot-check command passed: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Phase11|FullyQualifiedName~PluginResultFinalizerTests" --nologo` — 15/15 passed. Passing tests do not cover CR-01. |

**Score:** 30/31 must-have truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|---|---|---|---|
| `AutoQAC/Models/Diagnostics/DiagnosticTextFormatter.cs` | Shared safe diagnostics formatter | ✓ VERIFIED | Substantive helper with safe copy and unsafe-detail heuristics. |
| `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` | Safe cleaning/preview dialogs and validation identifiers | ✓ VERIFIED | Unexpected catches use safe copy; validation rows use safe identifiers. |
| `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs` | Safe browse diagnostics and migration warning display state | ✓ VERIFIED | Load-order/data-folder user text uses formatter copy; WR-02 state rollback issue is outside this phase goal. |
| `AutoQAC/ViewModels/RestoreViewModel.cs` | Safe restore session loading status | ✓ VERIFIED | Load session catch uses latest-log copy; WR-04 stale sessions is outside diagnostics disclosure scope. |
| `AutoQAC/ViewModels/SettingsViewModel.cs` | Safe persistence banner | ✓ VERIFIED | Banner maps typed `ConfigPersistenceFailure.SafeSummary`. |
| `AutoQAC/Services/Configuration/LegacyMigrationService.cs` | Safe migration warning categories | ✓ VERIFIED | Warning strings are fixed category copy with latest-log guidance. |
| `AutoQAC/App.axaml.cs` | Safe startup diagnostics/migration boundary | ✓ VERIFIED | Startup xEdit identifier is safe; migration warnings are sanitized. |
| `AutoQAC/Services/Process/ProcessExecutionService.cs` | Safe process-start/failure logging and PID labels | ✓ VERIFIED for diagnostics | Logs/PID labels are safe. WR-03 launch-clone semantics is a follow-up behavior concern, not proof the Phase 11 diagnostics goal is false. |
| `AutoQAC/Services/Cleaning/CleaningService.cs` | Safe caller-side launch diagnostics/result messages | ✓ VERIFIED | Command-build and unexpected-exception failed messages use sanitized plugin display text. |
| `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs` | Source sanitization for result rows and warnings | ✗ FAILED | Failed messages and xEdit exception-log content are safe, but generic `logResult.Warning` is copied verbatim to `LogParseWarning`. |
| `AutoQAC/Services/Cleaning/XEditLogFileService.cs` | Log-read warnings remain safe before user-facing flow | ✗ FAILED | Missing main log warning embeds full `mainLogPath`. Raw path is acceptable in local logs, not in UI-bound `LogParseWarning`. |
| Phase 11 tests | Regression guards | ⚠️ PARTIAL | Existing Phase 11 tests pass but omit the `LogReadResult.Warning` → tooltip flow. |

### Key Link Verification

| From | To | Via | Status | Details |
|---|---|---|---|---|
| `CleaningCommandsViewModel` | `IMessageDialogService.ShowErrorAsync` | safe title/message/details | ✓ WIRED | Dialog args use `OperationFailed`/`LatestLogDetails`. |
| `LegacyMigrationService` | `App.RunMigrationAsync` | `MigrationResult.WarningMessage` | ✓ WIRED | Safe service warning plus defensive `SafeFailureSummary`. |
| `ProcessExecutionService.ExecuteAsync` | `IPidStore.UpdateAsync` | `TrackProcessAsync` label | ✓ WIRED | Uses sanitized plugin name or `ExternalProcess`. |
| `ProcessExecutionService.ExecuteAsync` | `ILoggingService` | structured safe fields | ✓ WIRED | No raw `FileName`/`Arguments` process-start templates found. |
| `CleaningSessionResult.GenerateReport` | `DiagnosticTextFormatter.SafePluginName` | report row display prefixes | ✓ WIRED | Cleaned/already-clean/skipped/failed prefixes sanitized. |
| `CleaningService.CleanPluginAsync` | `PluginResultFinalizer` | `CleaningResult.Message` | ✓ WIRED | Service-created failed messages are safe. |
| `XEditLogFileService.ReadLogContentAsync` | `ProgressWindow` | `LogReadResult.Warning` → `PluginResultFinalizer.LogParseWarning` → `ToolTip.Tip` | ✗ NOT_WIRED SAFELY | Raw `mainLogPath` warning is user-facing; no formatter/safe-copy boundary. |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|---|---|---|---|---|
| `ProgressWindow` log warning tooltip | `LogParseWarning` | `XEditLogFileService.Warning` via `PluginResultFinalizer` | Yes, but unsafe | ✗ HOLLOW/UNSAFE — dynamic data flows, but raw path-bearing warning reaches UI. |
| `CleaningSessionResult` report plugin name | `result.PluginName` | `PluginResults` | Yes | ✓ FLOWING safely through `SafePluginName`. |
| `ProcessExecutionService` PID label | `pluginName` | caller parameter | Yes | ✓ FLOWING safely or `ExternalProcess`. |
| `App.axaml.cs` migration warning | `result.WarningMessage` | `LegacyMigrationService` | Yes | ✓ FLOWING safely through `SafeFailureSummary`. |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|---|---|---|---|
| Existing Phase 11/finalizer tests | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Phase11|FullyQualifiedName~PluginResultFinalizerTests" --nologo` | Passed 15/15 | ✓ PASS, but coverage gap remains |
| CR-01 source/data-flow check | Read `XEditLogFileService.cs`, `PluginResultFinalizer.cs`, and `ProgressWindow.axaml` | Raw warning path flows to tooltip | ✗ FAIL |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|---|---|---|---|---|
| SEC-01 | 11-01, 11-02, 11-03, 11-04, 11-05, 11-06, 11-07, 11-09, 11-10 | User sees concise error dialogs with log-file references instead of stack traces or excessive internal path detail. | ✗ BLOCKED / PARTIAL | Dialogs/reports are mostly safe, but user-facing progress tooltips can still expose a full xEdit main-log path via `LogParseWarning`. |
| SEC-02 | 11-01, 11-05, 11-06, 11-08 | User diagnostic logs avoid unnecessary full command-line/path exposure while preserving enough information for local troubleshooting. | ✓ SATISFIED | Process/startup logs use safe structured fields and PID labels; raw warning path may remain in local logs as troubleshooting context but must not be forwarded to UI. |

No orphaned Phase 11 requirement IDs were found. `.planning/REQUIREMENTS.md` maps `SEC-01` and `SEC-02` to Phase 11, and both IDs appear in Phase 11 plan frontmatter.

### Code Review Findings Adjudication

| Review Finding | Verdict | Verification Evidence |
|---|---|---|
| CR-01: Raw log-file paths are returned into user-facing progress tooltips | **BLOCKER / in-scope gap** | Confirmed by `XEditLogFileService.cs:79-84`, `PluginResultFinalizer.cs:37-40`, and `ProgressWindow.axaml:199-200,365-366`. This violates the phase goal and SC3. |
| WR-01: Already-clean plugins duplicated in generated reports | Follow-up / out of Phase 11 blocker scope | It is a report-accounting/usability issue, not a path/command/error-detail disclosure boundary failure. |
| WR-02: Failed load-order selection leaves invalid runtime state behind | Follow-up / out of Phase 11 blocker scope | State consistency is important, but user-facing diagnostic text at this boundary is safe. |
| WR-03: ProcessStartInfo cloning silently changes launch semantics | Follow-up / out of Phase 11 blocker scope | Potential launch-contract regression, but not a remaining path/command exposure issue. It should be routed separately because changing launch semantics is explicitly adjacent to, not the diagnostics goal. |
| WR-04: Loading restore sessions without a trusted root leaves stale sessions visible | Follow-up / out of Phase 11 blocker scope | Restore UI state issue; not evidence of unsafe diagnostic disclosure. |

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|---|---:|---|---|---|
| `AutoQAC/Services/Cleaning/XEditLogFileService.cs` | 84 | `Warning = $"Main log file not found: {mainLogPath}"` | 🛑 Blocker | Creates path-bearing warning text that is later user-facing. |
| `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs` | 40 | `logParseWarning = logResult.Warning` | 🛑 Blocker | Missing safe-copy boundary before `LogParseWarning` reaches UI tooltip. |
| `AutoQAC/Views/ProgressWindow.axaml` | 200, 366 | `ToolTip.Tip="{Binding LogParseWarning}"` | 🛑 Blocker | Confirms the unsafe warning is displayed to users. |

Other `ex.Message`, `return []`, and null/default matches reviewed in this sweep are local logging, typed service internals, tests, or ordinary control flow rather than Phase 11 blocking UI/export disclosure paths.

### Human Verification Required

None. The remaining gap is source-verifiable and should be closed with a focused automated regression test.

### Gaps Summary

The phase cannot be marked passed because one user-facing diagnostic path remains unsafe. When xEdit's main log is missing, `XEditLogFileService` produces a warning containing the full local log path, `PluginResultFinalizer` forwards that warning unchanged as `LogParseWarning`, and `ProgressWindow` displays it as a tooltip. The fix should keep the raw path in local logs if desired but replace the UI-bound warning with concise latest-log guidance.

---

_Verified: 2026-05-01T07:18:24Z_  
_Verifier: the agent (gsd-verifier)_
