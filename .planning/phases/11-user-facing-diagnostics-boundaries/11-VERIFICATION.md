---
phase: 11-user-facing-diagnostics-boundaries
verified: 2026-05-01T07:54:26Z
status: verified
score: 31/31 must-haves verified
overrides_applied: 0
re_verification:
  previous_status: gaps_found
  previous_score: 28/31
  gaps_closed:
    - "Backup failure dialog now sanitizes callback pluginName/errorMessage values before display."
    - "Timeout retry dialog now interpolates DiagnosticTextFormatter.SafePluginName output."
    - "Restore Selected dialog/status/error text now uses sanitized backup metadata display names."
  gaps_remaining: []
  regressions: []
gaps: []
---

# Phase 11: User-Facing Diagnostics Boundaries Verification Report

**Phase Goal:** Users receive concise, actionable error messages while logs retain local troubleshooting value without unnecessary full path or command-line exposure.  
**Verified:** 2026-05-01T07:54:26Z  
**Status:** verified  
**Re-verification:** Yes - Plans 11-12 and 11-13 closed the callback/dialog gaps found by advisory review.

## Goal Achievement

Phase 11 is **fully achieved** after gap closure. The formatter, report, process-log, migration-warning, result-finalizer, xEdit log warning, backup failure, timeout retry, and restore-selected boundaries are wired with safe user-facing copy while preserving local troubleshooting detail in logs and service inputs where required.

## Observable Truths

| # | Truth | Status | Evidence |
|---|---|---|---|
| 1 | User sees concise unexpected cleaning/preview error dialogs with latest-log guidance rather than raw exceptions/stacks. | ✓ VERIFIED | `CleaningCommandsViewModel.cs:157-166` and `205-214` use `DiagnosticTextFormatter.OperationFailed(...)` plus `LatestLogDetails`. |
| 2 | Pre-clean missing xEdit/MO2/load-order validation uses safe setting identifiers and no full configured paths. | ✓ VERIFIED | `CleaningCommandsViewModel.cs:443-450` uses `DiagnosticTextFormatter.SafeFileIdentifier` for xEdit, MO2, and load-order messages. |
| 3 | Shared formatter builds safe operation, setting, folder, plugin, and report copy. | ✓ VERIFIED | `DiagnosticTextFormatter.cs:17-147` contains constants, safe basename helpers, unsafe-detail detection, and plugin command-flag stripping. |
| 4 | Generated reports sanitize plugin display prefixes and defensive failed-row summaries. | ✓ VERIFIED | `CleaningSessionResult.cs:184-205,243-251` uses `SafePluginName` and `SafeFailureSummary`; disclaimer at line 162. |
| 5 | Failed CleaningResult.Message values from CleaningService are sanitized at source. | ✓ VERIFIED | `CleaningService.cs:55-70,169-178` uses `SafePluginName` and `CleaningFailedForPlugin`; no raw `plugin.FileName` interpolation remains in those failed messages. |
| 6 | xEdit exception-log and log-read warning text does not flow raw into result/report/tooltip-bound properties. | ✓ VERIFIED | `PluginResultFinalizer.cs:39-43,67-73,92-100` logs raw log-read warning locally but exposes `SafeLogReadWarning` or `XEditReportedError`. |
| 7 | Process/startup logs avoid raw executable paths and command-line payloads while preserving useful structured fields. | ✓ VERIFIED | `ProcessExecutionService.cs:61-93,212-228` logs operation/status/PID/argument count and uses `ExternalProcess`/safe plugin labels; `CleaningService.cs:82-148` logs QuickAutoClean mode/game/plugin/count/status/reason. |
| 8 | Legacy migration warnings shown to users use safe latest-log guidance. | ✓ VERIFIED | `LegacyMigrationService` returns fixed safe warning strings; `App.axaml.cs` defensively applies `SafeFailureSummary` before `ShowMigrationWarning` per Plan 11-07. |
| 9 | Backup failure dialog avoids raw plugin/path/error details. | ✓ VERIFIED | `CleaningCommandsViewModel.HandleBackupFailureAsync` sanitizes callback plugin/error text; `MessageDialogService.ShowBackupFailureDialogAsync` defensively re-sanitizes before TextBlock rendering. |
| 10 | Timeout retry dialog avoids raw plugin/path/control/command details. | ✓ VERIFIED | `CleaningCommandsViewModel.HandleTimeoutRetryAsync` uses `DiagnosticTextFormatter.SafePluginName(pluginName)` before composing retry dialog copy. |
| 11 | Restore selected dialog/status text avoids raw plugin metadata display values. | ✓ VERIFIED | `RestoreViewModel.RestorePluginAsync` uses `DiagnosticTextFormatter.SafePluginName(plugin.FileName)` for confirmation, status, and error copy while preserving the original `BackupPluginEntry` service input. |
| 12 | Phase-level sentinel tests exercise real ViewModel, model, and logger paths. | ✓ VERIFIED | Focused Phase 11 tests now cover backup failure callback, timeout retry unsafe plugin names, restore selected unsafe metadata filenames, result/report, process/log, startup, and finalizer boundaries. |

**Score:** 31/31 must-haves verified

## Required Artifacts

| Artifact | Expected | Status | Details |
|---|---|---|---|
| `AutoQAC/Models/Diagnostics/DiagnosticTextFormatter.cs` | Shared safe diagnostics formatter | ✓ VERIFIED | Substantive and used by many surfaces. |
| `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` | Safe cleaning dialogs/status/validation/callbacks | ✓ VERIFIED | Main unexpected errors, validation, timeout retry, and backup failure callbacks use safe formatter output. |
| `AutoQAC/Services/UI/MessageDialogService.cs` | Dialog renderer that does not reintroduce unsafe detail | ✓ VERIFIED | Backup failure dialog defensively applies `SafePluginName` and `SafeFailureSummary` before TextBlock rendering. |
| `AutoQAC/ViewModels/RestoreViewModel.cs` | Safe restore/session-loading diagnostics | ✓ VERIFIED | Session loading/delete copy is safe; Restore Selected metadata display uses sanitized filenames. |
| `AutoQAC/Services/Process/ProcessExecutionService.cs` | Safe process-start logging and PID labels | ✓ VERIFIED | Structured fields; safe tracking label helper. |
| `AutoQAC/Services/Cleaning/CleaningService.cs` | Safe caller-side launch diagnostics/result messages | ✓ VERIFIED | Safe plugin display used in launch logs and failure messages. |
| `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs` | Source sanitization for result rows and warnings | ✓ VERIFIED | Safe message/log-parse boundaries present. |
| Phase 11 tests | Regression guards | ✓ VERIFIED | New focused tests cover timeout retry, backup failure, and restore selected metadata negative-disclosure regressions. |

## Key Link Verification

| From | To | Via | Status | Details |
|---|---|---|---|---|
| `CleaningCommandsViewModel` | `IMessageDialogService.ShowErrorAsync` | safe title/message/details | ✓ WIRED | Unexpected cleaning/preview catches use safe copy. |
| `CleaningCommandsViewModel.HandleTimeoutRetryAsync` | `IMessageDialogService.ShowRetryAsync` | sanitized pluginName | ✓ WIRED SAFELY | Callback value is normalized through `SafePluginName` before dialog message composition. |
| `CleaningCommandsViewModel.HandleBackupFailureAsync` | `IMessageDialogService.ShowBackupFailureDialogAsync` | sanitized pluginName/errorMessage | ✓ WIRED SAFELY | Callback plugin and failure text are normalized through `SafePluginName` and `SafeFailureSummary`. |
| `MessageDialogService.ShowBackupFailureDialogAsync` | Avalonia `TextBlock` content | defensive safe variables | ✓ WIRED SAFELY | Dialog service re-applies `SafePluginName` and `SafeFailureSummary` before assigning TextBlock text. |
| `RestoreViewModel.RestorePluginAsync` | `IMessageDialogService.ShowConfirmAsync/ShowErrorAsync` | sanitized backup metadata FileName | ✓ WIRED SAFELY | UI copy uses safe display text while the restore service still receives the original `BackupPluginEntry`. |
| `ProcessExecutionService.ExecuteAsync` | `ILoggingService` / `IPidStore` | structured fields + safe labels | ✓ WIRED | No raw FileName/Arguments logging in start templates; tracking uses `GetSafeTrackingLabel`. |
| `XEditLogFileService` | `ProgressWindow` | warning -> finalizer -> LogParseWarning binding | ✓ WIRED SAFELY | Raw warning is logged locally only; `LogParseWarning` receives safe copy. |

## Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|---|---|---|---|---|
| Backup failure dialog | `pluginName`, `errorMessage` | `BackupSessionCoordinator` callback to `CleaningCommandsViewModel` | Yes | ✓ FLOWING safely through `SafePluginName`/`SafeFailureSummary`, with dialog-service defense-in-depth. |
| Timeout retry dialog | `pluginName` | `PluginCleaningRunner` callback using `plugin.FileName` | Yes | ✓ FLOWING safely through `SafePluginName` before retry copy is composed. |
| Restore selected copy | `plugin.FileName` | `BackupPluginEntry` loaded from backup session metadata | Yes | ✓ FLOWING safely for UI copy; original entry still flows to `IBackupService.RestorePluginAsync`. |
| Report rows | `result.PluginName`, `result.Message` | `PluginResults` | Yes | ✓ FLOWING safely through `SafePluginName`/`SafeFailureSummary`. |
| Process logs/PID labels | `startInfo`, `pluginName` | caller ProcessStartInfo / optional plugin context | Yes | ✓ FLOWING safely through counts and safe labels. |

## Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|---|---|---|---|
| Focused gap-closure tests | `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~ErrorDialogTests\|FullyQualifiedName~Phase11DiagnosticsBoundaryTests\|FullyQualifiedName~RestoreViewModelTests\|FullyQualifiedName~DiagnosticTextFormatterTests" --nologo` | Passed 84/84 | ✓ PASS |
| Full solution tests | `dotnet test "AutoQACSharp.slnx" --nologo` | Passed QueryPlugins.Tests 61/61 and AutoQAC.Tests 997/997 | ✓ PASS |
| Backup failure source check | Read `CleaningCommandsViewModel.cs` and `MessageDialogService.cs` | Callback values and concrete TextBlock copy are sanitized. | ✓ PASS |
| Timeout retry source check | Read `CleaningCommandsViewModel.cs` and `PluginCleaningRunner.cs` | Callback pluginName is sanitized in the ViewModel boundary before display. | ✓ PASS |
| Restore metadata source check | Read `RestoreViewModel.cs` | `BackupPluginEntry.FileName` is sanitized for UI copy and preserved for service calls. | ✓ PASS |

## Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|---|---|---|---|---|
| SEC-01 | 11-01, 11-02, 11-03, 11-04, 11-07, 11-09, 11-10, 11-11, 11-12, 11-13 | User sees concise error dialogs with log-file references instead of stack traces or excessive internal path detail. | ✓ SATISFIED | Backup failure, timeout retry, restore selected, formatter, result/report, migration warning, result-finalizer, and xEdit log warning surfaces all use safe display boundaries. |
| SEC-02 | 11-01, 11-05, 11-06, 11-08 | User diagnostic logs avoid unnecessary full command-line/path exposure while preserving local troubleshooting value. | ✓ SATISFIED | Process and cleaning launch logs use safe structured fields; PID labels use sanitized plugin names or `ExternalProcess`; raw xEdit log-read path is local-log-only as direct failing resource. |

No orphaned Phase 11 requirement IDs were found. `.planning/REQUIREMENTS.md` maps `SEC-01` and `SEC-02` to Phase 11, and both IDs appear in Phase 11 plan frontmatter.

## Code Review Findings Adjudication

| Review Finding | Verdict | Verification Evidence |
|---|---|---|
| CR-01: Backup failure dialog receives raw plugin/error text | ✓ CLOSED | Plan 11-12 sanitizes the callback boundary and adds defensive dialog-service sanitization before TextBlock rendering. |
| CR-02: Timeout retry dialog can expose unsanitized plugin names | ✓ CLOSED | Plan 11-12 sanitizes `pluginName` with `SafePluginName` before composing retry dialog text. |
| WR-01: Already-clean plugins are listed twice in reports | ⚠️ Advisory / not Phase 11 blocker | Report accounting issue confirmed by code shape (`CleanedPlugins` includes `AlreadyClean`), but it is not a diagnostics disclosure boundary. |
| WR-02: Restore confirmation/status text renders backup metadata filenames verbatim | ✓ CLOSED | Plan 11-13 sanitizes Restore Selected confirmation/status/error copy while preserving original `BackupPluginEntry` service input. |

## Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|---|---:|---|---|---|
| `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` | 452-471 | Timeout and backup failure callbacks | ✓ Closed | Callback values are sanitized before dialog service calls. |
| `AutoQAC/Services/UI/MessageDialogService.cs` | 94-128 | Backup failure TextBlocks | ✓ Closed | Concrete UI surface defensively sanitizes plugin and failure text. |
| `AutoQAC/ViewModels/RestoreViewModel.cs` | 192-223 | Restore Selected confirmation/status/error | ✓ Closed | User-facing restore copy uses `safePluginName`; service/log inputs remain original. |

## Human Verification Required

None. The gap closures are source- and test-verifiable. No visual/manual UI judgement is needed.

## Gaps Summary

The previous `status: gaps_found` report identified three user-facing disclosure gaps. Plans 11-12 and 11-13 closed all three with RED/GREEN regression tests, ViewModel boundary sanitization, and defensive dialog-service sanitization for backup failure rendering. No Phase 11 gaps remain.

---

_Verified: 2026-05-01T07:54:26Z_  
_Verifier: the agent (gsd-verifier)_
