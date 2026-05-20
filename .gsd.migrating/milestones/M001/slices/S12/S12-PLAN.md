# S12: User Facing Diagnostics Boundaries

**Goal:** Create the shared safe diagnostics text boundary required by Phase 11.
**Demo:** Create the shared safe diagnostics text boundary required by Phase 11.

## Must-Haves


## Tasks

- [x] **T01: 11-user-facing-diagnostics-boundaries 01** `est:3 min`
  - Create the shared safe diagnostics text boundary required by Phase 11.

Purpose: Later plans need one executable source of truth for safe operation messages, sanitized basenames, setting identifiers, plugin failure summaries, latest-log guidance, and export disclaimer wording.
Output: Tested formatter helpers that implement D-01 through D-08, D-09 through D-12 copy primitives, and D-13 through D-16 safe structured-field primitives without changing app behavior yet.
- [x] **T02: 11-user-facing-diagnostics-boundaries 02** `est:7 min`
  - Harden the main cleaning command UI boundary.

Purpose: SEC-01 requires the most visible cleaning and preview failures to stop surfacing `ex.Message`, `ex.StackTrace`, full configured paths, and command fragments.
Output: Tested safe status/dialog text and safe pre-clean validation rows in `CleaningCommandsViewModel`.
- [x] **T03: 11-user-facing-diagnostics-boundaries 03** `est:6 min`
  - Harden configuration browse-path and restore session-loading diagnostics.

Purpose: Phase 11 must cover inline/status/dialog surfaces beyond the cleaning command path, especially selected load-order files, selected data folders, and restore session loading.
Output: Tested safe labels and latest-log guidance for configuration technical failures and restore session load failures, preserving already-safe backup labels per D-04.
- [x] **T04: 11-user-facing-diagnostics-boundaries 04** `est:4 min`
  - Harden cleaning result rows and exported reports.

Purpose: D-09 through D-12 require failed plugin messages to be safe at source before they reach result windows or `GenerateReport()`, with report-level defensive checks and a one-line disclaimer.
Output: Tested safe plugin failure messages, xEdit exception-log handling, result summaries, and exported report text.
- [x] **T05: 11-user-facing-diagnostics-boundaries 05** `est:6 min`
  - Harden process/startup logs and migration warning copy.

Purpose: SEC-02 and D-13 through D-16 require AutoQAC logs to retain troubleshooting value without unnecessary executable path or command payload exposure, and SEC-01 requires migration warnings to avoid raw exception details.
Output: Tested safe process-start/failure logs, safe cleaning caller diagnostics, safe startup diagnostics, and safe migration warning text.
- [x] **T06: 11-user-facing-diagnostics-boundaries 06** `est:9 min`
  - Add phase-level regression guards and run final verification.

Purpose: Requirement 6 in `11-SPEC.md` requires explicit tests that fail if covered UI, export, or log surfaces reintroduce stack/path/command leaks. Earlier plans add focused tests; this plan adds a small cross-surface sentinel net and performs final automated verification.
Output: Shared sentinel helper, phase-level boundary tests, and final solution test pass.
- [x] **T07: 11-user-facing-diagnostics-boundaries 07** `est:2m 7s`
  - Close Phase 11 verification gap #1: legacy migration user warnings must use safe latest-log guidance instead of raw exception messages.

Purpose: SEC-01 and decisions D-01 through D-03 require migration warning UI to avoid raw exception/path/command details while keeping actionable guidance.
Output: Safe migration result warning copy, a defensive startup display boundary, and regression tests proving path-bearing migration warnings are not shown.
- [x] **T08: 11-user-facing-diagnostics-boundaries 08** `est:2m`
  - Close Phase 11 verification gap #2: successful process-start diagnostics must not log or persist raw legacy `Arguments` payloads.

Purpose: SEC-02 and D-13 require process/startup logs and PID tracking data to use safe structured fields and labels without raw command payload exposure.
Output: Safe PID tracking label selection and successful-start regression tests for legacy `ProcessStartInfo.Arguments` with omitted `pluginName`.
- [x] **T09: 11-user-facing-diagnostics-boundaries 09** `est:4 min`
  - Close Phase 11 verification gap #3: user-facing reports and failed result summaries must sanitize displayed plugin-name prefixes/fallbacks.

Purpose: SEC-01 and D-07/D-09 require report/result display names to preserve useful plugin filenames while stripping path separators, command-like punctuation, quotes/backticks, and control characters.
Output: Safe plugin display names in report rows and regression tests for malicious/path-like plugin names.
- [x] **T10: 11-user-facing-diagnostics-boundaries 10** `est:5 min`
  - Close the remaining Phase 11 verification gap: `CleaningService` must not create failed `CleaningResult.Message` text from raw plugin filenames that can survive into result rows, summaries, or exported reports.

Purpose: SEC-01 and D-10 require failed plugin messages to be sanitized at source before result-window/report boundaries consume them; Plan 11-09 sanitized report prefixes, but verification found service-created failed messages can still carry unsafe basename display characters.
Output: Safe `CleaningService` failed-result messages plus regression coverage through service, finalizer, summary, and report surfaces.
- [x] **T11: 11-user-facing-diagnostics-boundaries 11** `est:10 min`
  - Close the remaining Phase 11 verification gap: xEdit log-read warnings that include a full main-log path must not flow into `PluginCleaningResult.LogParseWarning` and the ProgressWindow tooltip.

Purpose: SEC-01 requires user-facing progress/result diagnostics to avoid avoidable xEdit/profile-root path detail; D-14 still permits the raw main-log path in local logs because it is the direct failed local resource.
Output: Safe log-read warning copy at the finalizer/result boundary plus a regression test that proves path-bearing warning text stays out of user-facing tooltip data.
- [x] **T12: 11-user-facing-diagnostics-boundaries 12** `est:8 min`
  - Close the Phase 11 SEC-01 cleaning-session dialog gaps where timeout retry and backup failure callbacks can expose raw plugin paths, command fragments, exception text, stack markers, or unsafe backup error strings.

Purpose: callback inputs cross a user-facing trust boundary and must be sanitized even when upstream services usually provide safe values.
Output: executable regression tests plus safe callback/dialog formatting in the existing ViewModel and dialog service surfaces.
- [x] **T13: 11-user-facing-diagnostics-boundaries 13** `est:5 min`
  - Close the Phase 11 SEC-01 restore metadata display gap where Restore Selected confirmation, status, and error text can render raw `BackupPluginEntry.FileName` from editable backup metadata.

Purpose: backup session metadata is untrusted display input; restore operations must preserve original metadata for service safety while projecting safe copy to users.
Output: executable regression tests plus safe restore-selected display formatting in `RestoreViewModel`.

## Files Likely Touched

- `AutoQAC/Models/Diagnostics/DiagnosticTextFormatter.cs`
- `AutoQAC.Tests/Models/DiagnosticTextFormatterTests.cs`
- `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`
- `AutoQAC.Tests/ViewModels/ErrorDialogTests.cs`
- `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`
- `AutoQAC/ViewModels/RestoreViewModel.cs`
- `AutoQAC/ViewModels/SettingsViewModel.cs`
- `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs`
- `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs`
- `AutoQAC.Tests/ViewModels/SettingsViewModelTests.cs`
- `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs`
- `AutoQAC/Models/CleaningSessionResult.cs`
- `AutoQAC/Models/PluginCleaningResult.cs`
- `AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs`
- `AutoQAC.Tests/Models/CleaningSessionResultTests.cs`
- `AutoQAC/Services/Process/ProcessExecutionService.cs`
- `AutoQAC/Services/Cleaning/CleaningService.cs`
- `AutoQAC/App.axaml.cs`
- `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`
- `AutoQAC.Tests/Services/CleaningServiceTests.cs`
- `AutoQAC.Tests/Integration/DependencyInjectionTests.cs`
- `AutoQAC.Tests/Helpers/DiagnosticSentinels.cs`
- `AutoQAC.Tests/ViewModels/Phase11DiagnosticsBoundaryTests.cs`
- `AutoQAC.Tests/Models/Phase11ReportBoundaryTests.cs`
- `AutoQAC.Tests/Services/Phase11LogBoundaryTests.cs`
- `AutoQAC/App.axaml.cs`
- `AutoQAC/Services/Configuration/LegacyMigrationService.cs`
- `AutoQAC.Tests/Services/LegacyMigrationServiceTests.cs`
- `AutoQAC.Tests/Integration/DependencyInjectionTests.cs`
- `AutoQAC/Services/Process/ProcessExecutionService.cs`
- `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`
- `AutoQAC.Tests/Services/Phase11LogBoundaryTests.cs`
- `AutoQAC/Models/CleaningSessionResult.cs`
- `AutoQAC/Models/PluginCleaningResult.cs`
- `AutoQAC.Tests/Models/CleaningSessionResultTests.cs`
- `AutoQAC.Tests/Models/Phase11ReportBoundaryTests.cs`
- `AutoQAC/Services/Cleaning/CleaningService.cs`
- `AutoQAC.Tests/Services/CleaningServiceTests.cs`
- `AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs`
- `AutoQAC.Tests/Models/CleaningSessionResultTests.cs`
- `AutoQAC/Services/Cleaning/XEditLogFileService.cs`
- `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs`
- `AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs`
- `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`
- `AutoQAC/Services/UI/MessageDialogService.cs`
- `AutoQAC.Tests/ViewModels/ErrorDialogTests.cs`
- `AutoQAC.Tests/ViewModels/Phase11DiagnosticsBoundaryTests.cs`
- `AutoQAC/ViewModels/RestoreViewModel.cs`
- `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs`
