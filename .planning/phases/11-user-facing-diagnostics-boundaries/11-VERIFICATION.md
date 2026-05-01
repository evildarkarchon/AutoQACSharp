---
phase: 11-user-facing-diagnostics-boundaries
verified: 2026-05-01T00:00:00Z
status: gaps_found
score: 28/31 must-haves verified
overrides_applied: 0
gaps:
  - truth: "Legacy migration user warnings use safe latest-log guidance instead of raw exception messages."
    status: failed
    reason: "App.axaml.cs forwards MigrationResult.WarningMessage directly to ShowMigrationWarning, while LegacyMigrationService still constructs several WarningMessage values from ex.Message. Those warnings can include local paths or raw exception details."
    artifacts:
      - path: "AutoQAC/App.axaml.cs"
        issue: "RunMigrationAsync displays result.WarningMessage directly for attempted unsuccessful migrations."
      - path: "AutoQAC/Services/Configuration/LegacyMigrationService.cs"
        issue: "Parse/write/backup/delete failure WarningMessage strings interpolate ex.Message."
    missing:
      - "Map all migration result warnings that reach MainWindowViewModel.ShowMigrationWarning to safe latest-log guidance or safe typed categories."
      - "Add a regression test where legacy migration returns or creates a path-bearing warning and assert the shown warning excludes the path and exception text."
  - truth: "Process-start diagnostics do not log or persist raw Arguments or command payloads on successful starts."
    status: failed
    reason: "ProcessExecutionService safe log templates were updated, but successful starts still call TrackProcessAsync(process, pluginName ?? arguments). When pluginName is omitted and ProcessStartInfo.Arguments is used, arguments is the raw legacy argument string; TrackProcessAsync stores it as PluginName and logs it in the orphan-tracking debug message."
    artifacts:
      - path: "AutoQAC/Services/Process/ProcessExecutionService.cs"
        issue: "ExecuteAsync line 93 passes pluginName ?? arguments into TrackProcessAsync; GetArgumentSummary returns startInfo.Arguments for legacy arguments; TrackProcessAsync stores/logs that label."
      - path: "AutoQAC.Tests/Services/Phase11LogBoundaryTests.cs"
        issue: "Phase 11 log guard covers start failure only; it does not exercise successful starts with legacy Arguments and omitted pluginName."
    missing:
      - "Use a safe tracking label such as a sanitized plugin name or ExternalProcess; never use raw legacy Arguments for PID tracking labels."
      - "Add a successful-start logger/PID-store test with ProcessStartInfo.Arguments containing path/command sentinels and no pluginName."
  - truth: "User-facing reports and result summaries sanitize displayed plugin basenames when formatting failed rows."
    status: partial
    reason: "Failed message text is sanitized, but CleaningSessionResult and PluginCleaningResult still use raw PluginName as a display prefix/fallback input. A hand-constructed or future result with a path-like/control-character PluginName can leak or duplicate unsafe text in reports."
    artifacts:
      - path: "AutoQAC/Models/CleaningSessionResult.cs"
        issue: "FormatFailedPluginReportLine uses result.PluginName directly for prefixing and comparison, and cleaned/skipped sections also print result.PluginName directly."
      - path: "AutoQAC/Models/PluginCleaningResult.cs"
        issue: "Failed Summary fallback calls CleaningFailedForPlugin(PluginName), but the surrounding model still exposes raw PluginName as the row identity."
    missing:
      - "Apply DiagnosticTextFormatter.SafePluginName to report/display plugin-name prefixes while preserving raw names only in internal data when needed."
      - "Add report/result tests with plugin names containing quotes, backticks, control characters, and path separators."
---

# Phase 11: User-Facing Diagnostics Boundaries Verification Report

**Phase Goal:** Users receive concise, actionable error messages while logs retain local troubleshooting value without unnecessary full path or command-line exposure.  
**Verified:** 2026-05-01T00:00:00Z  
**Status:** gaps_found  
**Re-verification:** No — initial verification

## Goal Achievement

The phase substantially hardens the main cleaning/preview, configuration, restore, report, process-start failure, and startup diagnostics boundaries. However, the goal is **not fully achieved**. Code review CR-02 remains true for successful process starts using legacy `Arguments`, and legacy migration warnings still expose raw `ex.Message` through a user-facing warning path. A defensive report/name sanitization gap also remains for displayed plugin names.

### Observable Truths

| # | Truth | Status | Evidence |
|---|---|---|---|
| 1 | User sees concise error dialogs with a log-file reference rather than stack traces or excessive internal path detail. | ✗ FAILED | Main cleaning/preview dialogs are safe (`CleaningCommandsViewModel.cs:157-165`, `205-213`), but migration warnings are still raw on result-warning paths (`App.axaml.cs:167-170`; `LegacyMigrationService.cs:97-104`, `114-121`, `135-143`, `152-160`). |
| 2 | User diagnostic logs avoid unnecessary full command-line exposure while preserving enough context for local troubleshooting. | ✗ FAILED | Process start/failure templates use safe fields, but successful starts still use `pluginName ?? arguments` for PID tracking (`ProcessExecutionService.cs:51-53`, `93`, `213-218`, `321-340`), so raw legacy `Arguments` can be persisted/logged. |
| 3 | User-facing exports and dialogs avoid exposing avoidable profile-root, game-install, xEdit, MO2, and plugin path detail unless needed for diagnosis. | ⚠️ PARTIAL | Failed messages are defensively sanitized (`CleaningSessionResult.cs:244-249`), but report/display prefixes still use raw `PluginName` (`CleaningSessionResult.cs:182-194`, `242-249`; `PluginCleaningResult.cs:90`). |
| 4 | Shared formatter builds safe operation, setting, folder, plugin, and report copy without raw exception/path/command detail. | ✓ VERIFIED | `DiagnosticTextFormatter` defines latest-log/report constants, sanitized file/folder/plugin helpers, and conservative `SafeFailureSummary` checks for paths, commands, exceptions, stacks, and control whitespace. |
| 5 | Displayed basenames preserve useful names while neutralizing unsafe display characters. | ✓ VERIFIED | Formatter removes control characters, invalid filename characters, quotes/backticks and command-like punctuation in `SanitizeDisplayName` (`DiagnosticTextFormatter.cs:26-31`, `133-144`). Integration remains incomplete for raw `PluginName` report prefixes (gap #3). |
| 6 | Unexpected cleaning failures show operation-specific safe copy and latest-log guidance. | ✓ VERIFIED | `StartCleaningAsync` logs the exception but sets `StatusText` and dialog message/details from `DiagnosticTextFormatter.OperationFailed("Cleaning")` and `LatestLogDetails` (`CleaningCommandsViewModel.cs:157-165`). |
| 7 | Unexpected preview failures show concise safe copy and latest-log guidance. | ✓ VERIFIED | `PreviewAsync` catch uses `OperationFailed("Preview")` plus `LatestLogDetails` and does not display stack traces (`CleaningCommandsViewModel.cs:205-213`). |
| 8 | Pre-clean validation identifies xEdit, MO2, and load-order settings by safe basename only and omits extra log guidance for simple missing paths. | ✓ VERIFIED | Validation messages use `SafeFileIdentifier` for xEdit, MO2, and load-order paths with direct fix actions (`CleaningCommandsViewModel.cs:444-450`). |
| 9 | Configuration browse failures use safe file/folder labels and latest-log guidance for technical failures. | ✓ VERIFIED | `ConfigurationViewModel` uses `SafeFileIdentifier("Load Order File", ...)`, `OperationFailed("Load order selection")`, `LatestLogDetails`, and `SafeFolderIssue` (`ConfigurationViewModel.cs:270-314`, `369`). |
| 10 | Restore session load failures use concise latest-log guidance rather than raw exception messages. | ✓ VERIFIED | `RestoreViewModel.cs` no longer contains `Error loading sessions: {ex.Message}`; plan tests exist and artifact verification passed. |
| 11 | Settings persistence failures preserve `ConfigPersistenceFailure.SafeSummary`. | ✓ VERIFIED | `SettingsViewModel.cs:221-232` maps persistence banner text from `failure.SafeSummary`. |
| 12 | Failed `PluginCleaningResult.Message` values are sanitized at finalizer source. | ✓ VERIFIED | `PluginResultFinalizer` converts unsafe failed messages with `SafeFailureSummary` and xEdit exception logs with `XEditReportedError` (`PluginResultFinalizer.cs:64-72`, `86-98`). |
| 13 | xEdit exception-log content is not copied into result rows/reports. | ✓ VERIFIED | Finalizer sets safe `logParseWarning` and logs only that xEdit reported an exception log, without `{Content}` (`PluginResultFinalizer.cs:64-72`). |
| 14 | Exported reports include one short disclaimer and defensive failed-row fallback. | ⚠️ PARTIAL | Disclaimer and failed-message fallback exist (`CleaningSessionResult.cs:158-163`, `209-215`, `242-249`), but raw `PluginName` remains a display prefix gap. |
| 15 | Cleaning-layer launch diagnostics preserve safe operation/mode/game/plugin/count/status/reason fields without changing command construction. | ✓ VERIFIED | `CleaningService` logs `QuickAutoClean`, launch mode, game display, sanitized plugin name, argument count, status, and reason around `ExecuteAsync` (`CleaningService.cs:77-91`, `97-148`). |
| 16 | Startup diagnostics replace configured executable paths with safe structured fields. | ✓ VERIFIED | `LogStartupInfo` logs `configured={XEditConfigured}` and `identifier={XEditIdentifier}` from `SafeFileIdentifier`, not `xEdit Path: {XEditPath}` (`App.axaml.cs:125-135`). |
| 17 | Phase-level sentinel tests exercise real ViewModel, model, and logger paths. | ✓ VERIFIED | Phase 11 guard tests use `UnsafeDiagnosticSentinels`, a real `StartCleaningCommand` catch path, real `GenerateReport()`, and captured `ProcessExecutionService` start-failure logger calls (`Phase11*Tests.cs`). |
| 18 | Full Phase 11 guard tests pass. | ✓ VERIFIED | Sequential rerun passed: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Phase11" --nologo` — 4 passed. |
| 19 | ProcessExecutionService focused tests pass. | ✓ VERIFIED | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ProcessExecutionServiceTests" --nologo` — 12 passed. Tests do not cover the successful legacy-Arguments tracking gap. |

**Score:** 28/31 must-have truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|---|---|---|---|
| `AutoQAC/Models/Diagnostics/DiagnosticTextFormatter.cs` | Shared safe formatter | ✓ VERIFIED | Exists, substantive, and used by ViewModels, models, services, and startup code. |
| `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` | Safe command-boundary dialogs/status/validation | ✓ VERIFIED | Cleaning/preview catches and pre-clean validation use formatter helpers. |
| `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs` | Safe browse diagnostics | ✓ VERIFIED | Uses safe load-order and folder copy. |
| `AutoQAC/ViewModels/RestoreViewModel.cs` | Safe restore session loading status | ✓ VERIFIED | Artifact passed; no raw load-session `ex.Message` pattern found. |
| `AutoQAC/ViewModels/SettingsViewModel.cs` | Safe persistence banner | ✓ VERIFIED | Uses `SafeSummary`. |
| `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs` | Source sanitization for failed result rows | ✓ VERIFIED | Sanitizes failed messages and xEdit exception-log outcomes. |
| `AutoQAC/Models/CleaningSessionResult.cs` | Defensive report disclaimer and failed-row fallback | ⚠️ PARTIAL | Disclaimer/message fallback exist, but raw `PluginName` display remains. |
| `AutoQAC/Models/PluginCleaningResult.cs` | Safe failed summary projection | ⚠️ PARTIAL | Failed `Summary` uses `SafeFailureSummary`, but raw `PluginName` remains the row identity. |
| `AutoQAC/Services/Process/ProcessExecutionService.cs` | Safe process-start/failure structured logging boundary | ✗ FAILED | Log templates are safe, but successful-start PID tracking can store/log raw legacy `Arguments`. |
| `AutoQAC/Services/Cleaning/CleaningService.cs` | Caller-side safe launch diagnostics | ⚠️ WARNING | Launch logs use sanitized plugin names; generic exception result still interpolates raw `plugin.FileName` before finalizer sanitization. |
| `AutoQAC/App.axaml.cs` | Safe startup log and migration warning boundary | ✗ FAILED | Startup xEdit log is safe, but migration result warnings are forwarded raw. |
| Phase 11 test files | Regression guards | ⚠️ PARTIAL | Existing guards pass but miss successful legacy-Arguments process tracking and migration result warning paths. |

### Key Link Verification

| From | To | Via | Status | Details |
|---|---|---|---|---|
| `DiagnosticTextFormatter` | Phase 11 UI/error/log consumers | Static helper calls | ✓ WIRED | 26 production `DiagnosticTextFormatter.` matches across App, services, models, and ViewModels. |
| `CleaningCommandsViewModel` | `IMessageDialogService.ShowErrorAsync` | Safe title/message/details values | ✓ WIRED | Cleaning and preview unexpected catches call `ShowErrorAsync` with safe operation copy and `LatestLogDetails`. |
| `ConfigurationViewModel` | `DiagnosticTextFormatter.SafeFileIdentifier/SafeFolderIssue` | Browse failure paths | ✓ WIRED | Load-order and folder failure paths use formatter helpers. |
| `PluginResultFinalizer` | `CleaningSessionResult.GenerateReport` | `PluginCleaningResult.Message` | ✓ WIRED | Finalizer populates safe failed `Message`; report defensively rechecks failed `Message`. |
| `ProcessExecutionService.ExecuteAsync` | `ILoggingService` | Structured safe fields | ⚠️ PARTIAL | Start/failure/success templates use `ArgumentCount`/`ProcessId`, but `TrackProcessAsync` logs the unsafe tracking label when the label came from raw legacy arguments. |
| `App.axaml.cs` migration | `MainWindowViewModel.ShowMigrationWarning` | `MigrationResult.WarningMessage` | ✗ NOT_WIRED | Safe unexpected catch exists, but normal failed migration results pass raw warning strings through. |
| Phase 11 diagnostics tests | Acceptance criteria | Shared unsafe sentinels | ⚠️ PARTIAL | Tests exercise real seams, but do not cover the two failing paths above. |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|---|---|---|---|---|
| `CleaningCommandsViewModel` | `StatusText`, dialog args, `ValidationErrors` | Orchestrator exceptions and pre-clean state | Yes | ✓ FLOWING with safe formatter copy. |
| `ConfigurationViewModel` | load-order/data-folder status/dialog text | File selection and browse/read exceptions | Yes | ✓ FLOWING with safe identifiers. |
| `SettingsViewModel` | persistence banner text | `IConfigurationService` failure streams | Yes | ✓ FLOWING via `SafeSummary`. |
| `PluginResultFinalizer` → `CleaningSessionResult` | failed plugin `Message` | Runner output and xEdit log parse result | Yes | ✓ FLOWING for message text; ⚠️ raw `PluginName` display remains. |
| `ProcessExecutionService` | PID tracking label | `pluginName ?? GetArgumentSummary(startInfo)` | Yes | ✗ HOLLOW/UNSAFE — when `pluginName` is null and legacy `Arguments` is populated, raw command text flows to PID store and orphan log. |
| `App.axaml.cs` migration warning | warning text | `LegacyMigrationService.MigrationResult.WarningMessage` | Yes | ✗ UNSAFE — raw service warning strings flow to `ShowMigrationWarning`. |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|---|---|---|---|
| Phase 11 guard tests | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Phase11" --nologo` | Passed: 4/4 | ✓ PASS |
| Process execution focused tests | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ProcessExecutionServiceTests" --nologo` | Passed: 12/12 | ✓ PASS |
| Artifact frontmatter checks | `gsd-sdk query verify.artifacts` for plans 11-01..11-06 | 22/22 artifacts passed | ✓ PASS |
| Key-link SDK checks | `gsd-sdk query verify.key-links` for plans 11-01..11-06 | 0/6 due non-path source names; manually verified above | ⚠️ TOOL LIMITATION |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|---|---|---|---|---|
| SEC-01 | 11-01, 11-02, 11-03, 11-04, 11-05, 11-06 | User sees concise error dialogs with log-file references instead of stack traces or excessive internal path detail. | ✗ BLOCKED | Most UI/report paths are safe, but legacy migration result warnings can still display raw `ex.Message`, and defensive report plugin-name display is incomplete. |
| SEC-02 | 11-01, 11-05, 11-06 | User diagnostic logs avoid unnecessary full command-line/path exposure while preserving local troubleshooting value. | ✗ BLOCKED | Process-start log templates are safe, but successful starts can persist/log raw legacy argument strings through PID tracking. |

No orphaned Phase 11 requirement IDs were found. `.planning/REQUIREMENTS.md` maps `SEC-01` and `SEC-02` to Phase 11, and both IDs appear in Phase 11 plan frontmatter.

### Code Review Findings Adjudication

| Review Finding | Verdict | Verification Evidence |
|---|---|---|
| CR-01: Disable Skip Lists can still be blocked by pre-clean validation | ⚠️ Advisory / outside diagnostics goal | The finding is behavioral and important, but it does not directly determine whether Phase 11 achieved diagnostics boundaries. Track separately; not counted as a Phase 11 diagnostics blocker here. |
| CR-02: Successful process starts can persist/log raw legacy argument strings | 🛑 TRUE GAP | `ProcessExecutionService.cs:93` still passes `pluginName ?? arguments`; `GetArgumentSummary` returns raw `startInfo.Arguments` when no `ArgumentList` exists. |
| WR-01: Already-clean plugins duplicated in generated reports | ⚠️ Warning | `CleanedPlugins` includes `AlreadyClean` and report separately prints already-clean. User-facing accounting issue, but not a direct path/command/diagnostic disclosure blocker. |
| WR-02: CleaningService returns unsanitized plugin names in failure text | ⚠️ Warning | `CleaningService.cs:176` interpolates raw `plugin.FileName`; downstream finalizer generally sanitizes failed `PluginCleaningResult.Message`, but this remains a fragile source-boundary gap. |

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|---|---:|---|---|---|
| `AutoQAC/Services/Configuration/LegacyMigrationService.cs` | 103, 120, 142, 159 | User-facing `WarningMessage` interpolates `ex.Message` | 🛑 Blocker | Raw exception/path detail can reach migration warning UI. |
| `AutoQAC/Services/Process/ProcessExecutionService.cs` | 93, 216-218, 321-340 | Raw legacy `Arguments` used as PID tracking label | 🛑 Blocker | Raw command/path payload can be persisted and logged after successful start. |
| `AutoQAC/Models/CleaningSessionResult.cs` | 184, 194, 246-249 | Raw `PluginName` used in report display | 🛑 Blocker | Defensive report boundary can still display unsafe plugin/path-like names. |
| `AutoQAC/Services/Cleaning/CleaningService.cs` | 176 | Raw `plugin.FileName` in failure message | ⚠️ Warning | Usually sanitized downstream by finalizer, but source result remains fragile. |

Stub-pattern scans found only legitimate null/empty returns or test setup values; no placeholder implementation was identified in Phase 11 production artifacts.

### Human Verification Required

None. The blocking issues are source-verifiable and testable with focused automated regression tests.

### Gaps Summary

Phase 11 is close, but not complete. The main implemented diagnostic formatter and most UI/report/log call sites are substantive and wired. The remaining blockers are hidden boundary paths that existing tests do not cover: migration result warnings still carry raw exception text into UI, and successful process starts can still persist/log legacy raw argument strings through PID tracking. Additionally, defensive report display sanitizes failed messages but not the plugin-name prefix itself.

---

_Verified: 2026-05-01T00:00:00Z_  
_Verifier: the agent (gsd-verifier)_
