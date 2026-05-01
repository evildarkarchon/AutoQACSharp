---
phase: 11-user-facing-diagnostics-boundaries
verified: 2026-05-01T07:39:51Z
status: gaps_found
score: 28/31 must-haves verified
overrides_applied: 0
re_verification:
  previous_status: verified
  previous_score: 31/31
  gaps_closed: []
  gaps_remaining:
    - "Backup failure dialog still receives and renders raw pluginName/errorMessage values."
    - "Timeout retry dialog interpolates the callback pluginName without DiagnosticTextFormatter.SafePluginName."
    - "Restore Selected dialog/status/error text renders backup metadata FileName verbatim."
  regressions:
    - "Advisory 11-REVIEW.md critical findings CR-01 and CR-02 are confirmed in code and invalidate the previous verified status."
gaps:
  - truth: "User-facing cleaning-session dialogs avoid exposing avoidable plugin path/detail values."
    status: failed
    reason: "The backup failure callback forwards pluginName and errorMessage directly into a concrete dialog that renders both verbatim."
    artifacts:
      - path: "AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs"
        issue: "HandleBackupFailureAsync at lines 467-468 calls ShowBackupFailureDialogAsync(pluginName, errorMessage) without sanitizing either value."
      - path: "AutoQAC/Services/UI/MessageDialogService.cs"
        issue: "ShowBackupFailureDialogAsync renders pluginName in a TextBlock at line 113 and errorMessage at line 121."
    missing:
      - "Sanitize pluginName with DiagnosticTextFormatter.SafePluginName before it reaches the dialog."
      - "Pass errorMessage through DiagnosticTextFormatter.SafeFailureSummary with latest-log fallback, or enforce that dialog service sanitizes it."
      - "Add a regression test where backup callback inputs contain a path/command/exception sentinel and assert the dialog strings exclude it."
  - truth: "Timeout retry dialogs display sanitized plugin names only."
    status: failed
    reason: "The timeout retry callback interpolates the callback-provided pluginName directly into user-facing dialog copy."
    artifacts:
      - path: "AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs"
        issue: "HandleTimeoutRetryAsync at lines 452-464 builds `Cleaning of '{pluginName}'...` without safe display-name normalization."
      - path: "AutoQAC/Services/Cleaning/PluginCleaningRunner.cs"
        issue: "The callback is invoked with plugin.FileName at line 70; no caller-side contract guarantees this value is sanitized."
    missing:
      - "Normalize pluginName through DiagnosticTextFormatter.SafePluginName before composing the timeout dialog message."
      - "Add a timeout callback test using an unsafe path/control/command plugin name and assert ShowRetryAsync receives safe copy."
  - truth: "Restore dialogs/status text avoid exposing avoidable plugin path/control detail from backup metadata."
    status: partial
    reason: "Restore Selected uses BackupPluginEntry.FileName directly in confirmation, status, log, failure dialog, and failure status text. Backup metadata is loaded from session.json and can be corrupted or hand-edited."
    artifacts:
      - path: "AutoQAC/ViewModels/RestoreViewModel.cs"
        issue: "Lines 195-223 interpolate plugin.FileName directly into confirmation, StatusText, logger properties, error message, and failed status."
      - path: "AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs"
        issue: "Existing restore confirmation test asserts the literal safe sample `Dawnguard.esm`; no negative-disclosure test covers path-like/control-character metadata filenames."
    missing:
      - "Use DiagnosticTextFormatter.SafePluginName(plugin.FileName) for user-facing restore selected confirmation/status/error copy while preserving the original BackupPluginEntry for service calls."
      - "Add restore ViewModel regression coverage with a path-like/control-character FileName from backup metadata."
---

# Phase 11: User-Facing Diagnostics Boundaries Verification Report

**Phase Goal:** Users receive concise, actionable error messages while logs retain local troubleshooting value without unnecessary full path or command-line exposure.  
**Verified:** 2026-05-01T07:39:51Z  
**Status:** gaps_found  
**Re-verification:** Yes — prior verification existed, but the new advisory review findings were independently checked against production code.

## Goal Achievement

Phase 11 is **not fully achieved**. The formatter, report, process-log, migration-warning, result-finalizer, and xEdit log warning boundaries are substantive and wired. However, goal-backward verification confirms the latest advisory review's two critical UI-disclosure findings: backup failure and timeout retry dialogs still render unsanitized plugin/error values. A restore selected metadata display gap is also confirmed.

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
| 9 | Backup failure dialog avoids raw plugin/path/error details. | ✗ FAILED | `CleaningCommandsViewModel.cs:467-468` forwards raw values; `MessageDialogService.cs:111-124` renders both verbatim. |
| 10 | Timeout retry dialog avoids raw plugin/path/control/command details. | ✗ FAILED | `CleaningCommandsViewModel.cs:452-464` interpolates `pluginName` directly into dialog text. |
| 11 | Restore selected dialog/status text avoids raw plugin metadata display values. | ✗ PARTIAL | `RestoreViewModel.cs:195-223` renders `plugin.FileName` from backup metadata directly in confirmation/status/error copy. |
| 12 | Phase-level sentinel tests exercise real ViewModel, model, and logger paths. | ⚠️ PARTIAL | Focused Phase 11 tests pass, but grep/read shows no sentinel tests for backup failure callback, timeout retry unsafe plugin names, or restore selected unsafe metadata filenames. |

**Score:** 28/31 must-haves verified

## Required Artifacts

| Artifact | Expected | Status | Details |
|---|---|---|---|
| `AutoQAC/Models/Diagnostics/DiagnosticTextFormatter.cs` | Shared safe diagnostics formatter | ✓ VERIFIED | Substantive and used by many surfaces. |
| `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` | Safe cleaning dialogs/status/validation/callbacks | ✗ FAILED | Main unexpected errors and validation are safe, but backup failure and timeout retry callbacks are unsanitized. |
| `AutoQAC/Services/UI/MessageDialogService.cs` | Dialog renderer that does not reintroduce unsafe detail | ✗ FAILED | Backup failure dialog renders caller-provided pluginName/errorMessage verbatim. |
| `AutoQAC/ViewModels/RestoreViewModel.cs` | Safe restore/session-loading diagnostics | ⚠️ PARTIAL | Session loading/delete failure copy is safe; Restore Selected metadata display uses raw `plugin.FileName`. |
| `AutoQAC/Services/Process/ProcessExecutionService.cs` | Safe process-start logging and PID labels | ✓ VERIFIED | Structured fields; safe tracking label helper. |
| `AutoQAC/Services/Cleaning/CleaningService.cs` | Safe caller-side launch diagnostics/result messages | ✓ VERIFIED | Safe plugin display used in launch logs and failure messages. |
| `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs` | Source sanitization for result rows and warnings | ✓ VERIFIED | Safe message/log-parse boundaries present. |
| Phase 11 tests | Regression guards | ⚠️ PARTIAL | Existing focused tests pass, but critical callback/dialog gaps are untested. |

## Key Link Verification

| From | To | Via | Status | Details |
|---|---|---|---|---|
| `CleaningCommandsViewModel` | `IMessageDialogService.ShowErrorAsync` | safe title/message/details | ✓ WIRED | Unexpected cleaning/preview catches use safe copy. |
| `CleaningCommandsViewModel.HandleTimeoutRetryAsync` | `IMessageDialogService.ShowRetryAsync` | interpolated pluginName | ✗ NOT SAFE | Callback value is used raw in dialog message. |
| `CleaningCommandsViewModel.HandleBackupFailureAsync` | `IMessageDialogService.ShowBackupFailureDialogAsync` | raw pluginName/errorMessage | ✗ NOT SAFE | No formatter call before concrete dialog renders values. |
| `MessageDialogService.ShowBackupFailureDialogAsync` | Avalonia `TextBlock` content | direct text assignment | ✗ NOT SAFE | Lines 113 and 121 render raw caller values. |
| `RestoreViewModel.RestorePluginAsync` | `IMessageDialogService.ShowConfirmAsync/ShowErrorAsync` | raw backup metadata FileName | ⚠️ PARTIAL | Service calls should keep raw entry, but UI copy should use safe display value. |
| `ProcessExecutionService.ExecuteAsync` | `ILoggingService` / `IPidStore` | structured fields + safe labels | ✓ WIRED | No raw FileName/Arguments logging in start templates; tracking uses `GetSafeTrackingLabel`. |
| `XEditLogFileService` | `ProgressWindow` | warning -> finalizer -> LogParseWarning binding | ✓ WIRED SAFELY | Raw warning is logged locally only; `LogParseWarning` receives safe copy. |

## Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|---|---|---|---|---|
| Backup failure dialog | `pluginName`, `errorMessage` | `BackupSessionCoordinator` callback to `CleaningCommandsViewModel` | Yes | ✗ HOLLOW SAFETY — real dynamic data flows to user without safe boundary. |
| Timeout retry dialog | `pluginName` | `PluginCleaningRunner` callback using `plugin.FileName` | Yes | ✗ HOLLOW SAFETY — dynamic plugin display value flows raw into dialog message. |
| Restore selected copy | `plugin.FileName` | `BackupPluginEntry` loaded from backup session metadata | Yes | ⚠️ PARTIAL — restore service receives real entry; UI display lacks safe projection. |
| Report rows | `result.PluginName`, `result.Message` | `PluginResults` | Yes | ✓ FLOWING safely through `SafePluginName`/`SafeFailureSummary`. |
| Process logs/PID labels | `startInfo`, `pluginName` | caller ProcessStartInfo / optional plugin context | Yes | ✓ FLOWING safely through counts and safe labels. |

## Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|---|---|---|---|
| Existing Phase 11/finalizer tests | `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~Phase11\|FullyQualifiedName~PluginResultFinalizerTests" --nologo` | Passed 16/16 | ✓ PASS |
| Backup failure source check | Read `CleaningCommandsViewModel.cs` and `MessageDialogService.cs` | Raw callback values are rendered verbatim | ✗ FAIL |
| Timeout retry source check | Read `CleaningCommandsViewModel.cs` and `PluginCleaningRunner.cs` | Raw callback pluginName is interpolated | ✗ FAIL |
| Restore metadata source check | Read `RestoreViewModel.cs` | `BackupPluginEntry.FileName` is displayed verbatim | ⚠️ PARTIAL |

## Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|---|---|---|---|---|
| SEC-01 | 11-01, 11-02, 11-03, 11-04, 11-07, 11-09, 11-10, 11-11 | User sees concise error dialogs with log-file references instead of stack traces or excessive internal path detail. | ✗ BLOCKED | Major surfaces are safe, but backup failure and timeout retry dialogs still render unsanitized plugin/error values; restore selected metadata copy is also raw. |
| SEC-02 | 11-01, 11-05, 11-06, 11-08 | User diagnostic logs avoid unnecessary full command-line/path exposure while preserving local troubleshooting value. | ✓ SATISFIED | Process and cleaning launch logs use safe structured fields; PID labels use sanitized plugin names or `ExternalProcess`; raw xEdit log-read path is local-log-only as direct failing resource. |

No orphaned Phase 11 requirement IDs were found. `.planning/REQUIREMENTS.md` maps `SEC-01` and `SEC-02` to Phase 11, and both IDs appear in Phase 11 plan frontmatter.

## Code Review Findings Adjudication

| Review Finding | Verdict | Verification Evidence |
|---|---|---|
| CR-01: Backup failure dialog receives raw plugin/error text | 🛑 TRUE GAP | `HandleBackupFailureAsync` passes raw values; concrete dialog renders both verbatim. Even though current `BackupSessionCoordinator` usually supplies a typed `DisplayReason`, neither the callback nor dialog enforces this boundary and pluginName remains raw. |
| CR-02: Timeout retry dialog can expose unsanitized plugin names | 🛑 TRUE GAP | `HandleTimeoutRetryAsync` interpolates `pluginName` without `SafePluginName`. |
| WR-01: Already-clean plugins are listed twice in reports | ⚠️ Advisory / not Phase 11 blocker | Report accounting issue confirmed by code shape (`CleanedPlugins` includes `AlreadyClean`), but it is not a diagnostics disclosure boundary. |
| WR-02: Restore confirmation/status text renders backup metadata filenames verbatim | ⚠️ TRUE GAP | Confirmed in `RestoreViewModel.cs:195-223`; classified partial because it requires bad backup metadata but still violates the user-facing display boundary. |

## Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|---|---:|---|---|---|
| `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` | 454 | `$"Cleaning of '{pluginName}'...` | 🛑 Blocker | Unsanitized callback plugin display value reaches retry dialog. |
| `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` | 467-468 | `ShowBackupFailureDialogAsync(pluginName, errorMessage)` | 🛑 Blocker | Unsanitized backup failure data crosses into dialog. |
| `AutoQAC/Services/UI/MessageDialogService.cs` | 113, 121 | TextBlocks render caller strings verbatim | 🛑 Blocker | Concrete UI surface has no defensive sanitization. |
| `AutoQAC/ViewModels/RestoreViewModel.cs` | 195-223 | Raw `plugin.FileName` in confirmation/status/error | ⚠️ Warning | Corrupt backup metadata can display path/control detail. |

## Human Verification Required

None. The blocking gaps are source-verifiable. No visual/manual UI judgement is needed to determine that raw values are interpolated into dialog text.

## Gaps Summary

The previous `status: verified` report was too soft. It trusted the then-current Phase 11 surfaces but missed callback-driven dialog paths. The phase goal explicitly covers user-facing dialogs and avoidable plugin/path detail; backup failure and timeout retry dialogs are part of the cleaning user flow and currently bypass the shared formatter. These are BLOCKER gaps for SEC-01. Restore selected metadata display is a lower-likelihood but real user-facing boundary gap that should be fixed in the same closure pass or explicitly deferred/overridden.

---

_Verified: 2026-05-01T07:39:51Z_  
_Verifier: the agent (gsd-verifier)_
