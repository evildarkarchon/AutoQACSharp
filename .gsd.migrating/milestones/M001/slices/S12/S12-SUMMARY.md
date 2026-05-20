---
id: S12
parent: M001
milestone: M001
provides:
  - Source-safe CleaningService failed messages for command-build failures and unexpected launch exceptions
  - Regression coverage for unsafe plugin basenames through CleaningService, PluginResultFinalizer, PluginCleaningResult.Summary, and CleaningSessionResult.GenerateReport
  - Phase 11 verification gap closure for SEC-01 / CR-01
  - Safe latest-log copy for UI-bound xEdit log-read warnings
  - Regression coverage proving path-bearing main-log warnings do not reach result/report/tooltip-bound strings
  - Final Phase 11 verification update marking 31/31 must-haves verified
  - Safe timeout retry callback dialog copy
  - Safe backup failure callback dialog copy
  - Defensive backup failure dialog-service sanitization before TextBlock rendering
  - Regression coverage for unsafe callback plugin/error payloads
  - Safe Restore Selected confirmation, status, and error display names
  - Regression coverage for unsafe backup metadata filenames
  - Proof that restore service calls still receive the original BackupPluginEntry
  - Shared DiagnosticTextFormatter safe text boundary for Phase 11 consumers
  - Negative-disclosure sentinel tests for paths, commands, exceptions, and stack-like text
  - Safe cleaning and preview unexpected-failure dialog/status copy
  - Safe xEdit, MO2, and load-order pre-clean validation identifiers
  - Regression coverage for command-boundary and validation disclosure limits
  - Safe configuration browse diagnostics for selected load-order and game data folder failures
  - Safe restore backup-session load failure status with latest-log guidance
  - Settings persistence write/read regression coverage preserving ConfigPersistenceFailure.SafeSummary
  - Source sanitization for failed PluginCleaningResult messages in PluginResultFinalizer
  - Safe xEdit exception-log result rows without raw exception-log content
  - Defensive exported report disclaimer and failed-row fallback formatting
  - Failed PluginCleaningResult.Summary safe fallback behavior
  - Safe structured process-start diagnostics with argument counts and process IDs instead of executable paths or argv payloads
  - Safe CleaningService QuickAutoClean launch diagnostics with mode, game, plugin filename, argument count, status, and reason
  - Safe startup xEdit configuration diagnostics and generic legacy migration warning copy
  - Shared unsafe diagnostic sentinel helper for Phase 11 guard tests
  - Cross-surface Phase 11 ViewModel, report, and log-boundary regression tests
  - Final focused Phase 11 and full solution verification pass
  - Safe legacy migration warning copy with latest-log guidance
  - Defensive startup warning boundary before MainWindowViewModel.ShowMigrationWarning
  - Regression coverage rejecting path, command, exception, and stack sentinel disclosure
  - Safe PID tracking label selection for successful external process starts
  - Successful-start regression coverage for legacy ProcessStartInfo.Arguments with omitted pluginName
  - Phase-level log/PID-store guard for process starts that must not expose raw launch payloads
  - Safe plugin display names in generated cleaning reports
  - Regression coverage for path-like, control-character, quote/backtick, separator, and command-flag plugin names
  - Failed-row fallback formatting based on sanitized plugin prefixes
requires: []
affects: []
key_files: []
key_decisions:
  - CleaningService command-build failures use DiagnosticTextFormatter.SafePluginName(plugin.FileName) before returning failed CleaningResult.Message text.
  - CleaningService unexpected exception failures return DiagnosticTextFormatter.CleaningFailedForPlugin(plugin.FileName), keeping raw exception and unsafe basename details out of result/report boundaries.
  - PluginResultFinalizer keeps raw LogReadResult.Warning text in local logger output but replaces UI-bound LogParseWarning with stable latest-log copy.
  - ProgressWindow tooltip binding remains unchanged because the bound PluginCleaningResult.LogParseWarning value is now safe at the model boundary.
  - Timeout retry dialogs keep the existing retry behavior but display only DiagnosticTextFormatter.SafePluginName output.
  - Backup failure callbacks sanitize both plugin and failure summary before calling IMessageDialogService.
  - MessageDialogService defensively re-sanitizes backup failure dialog inputs because it renders directly to Avalonia TextBlocks.
  - BackupPluginEntry.FileName from session metadata is treated as untrusted display input.
  - Restore Selected UI copy uses DiagnosticTextFormatter.SafePluginName, but IBackupService.RestorePluginAsync still receives the original BackupPluginEntry.
  - Logger properties continue to use original metadata where local troubleshooting value is needed.
  - DiagnosticTextFormatter lives under AutoQAC.Models.Diagnostics so models, services, ViewModels, and startup code can share safe copy without a service-layer dependency.
  - Unsafe failure summaries fall back on any detected paths, command flags, exception names, stack markers, executable command markers, or control whitespace.
  - CleaningCommandsViewModel now treats unexpected cleaning and preview failures as latest-log UI copy only; raw exception and stack details stay in logs.
  - Pre-clean missing-path validation uses DiagnosticTextFormatter.SafeFileIdentifier for xEdit, MO2, and file-load-order paths, preserving basenames while hiding directories.
  - Configuration selected load-order technical failures now identify Load Order File (plugins.txt) and point to the latest AutoQAC log instead of showing selected paths or exception text.
  - Selected game data folder browse failures use game-specific safe folder copy, falling back to selected game data folder when no game context is available.
  - Settings persistence write/read banners preserve ConfigPersistenceFailure.SafeSummary instead of replacing typed safe categories with generic text.
  - xEdit exception-log content is not copied into AutoQAC result rows, reports, or structured log properties; AutoQAC records only that xEdit reported an exception log for the plugin.
  - Export report failed rows use DiagnosticTextFormatter.SafeFailureSummary defensively even though finalizer-created messages are already sanitized at source.
  - ProcessExecutionService emits only operation/status/reason/argumentCount/processId fields for launch diagnostics; executable paths and raw arguments remain out of structured log properties.
  - CleaningService owns caller-side QuickAutoClean launch context logs while preserving XEditCommandBuilder and ProcessStartInfo launch values unchanged.
  - App.axaml.cs startup diagnostics use DiagnosticTextFormatter.SafeFileIdentifier for xEdit configuration and source guards for private startup copy.
  - Phase-level disclosure guards use one shared unsafe sentinel set so UI, report, and log boundary tests fail on the same path/command/exception regressions.
  - Plan 11-06 keeps behavioral logger capture as the primary log-boundary proof and limits source guards to private startup/known bad template absence.
  - Legacy migration warnings now use fixed category copy with latest-log guidance rather than interpolated exception messages.
  - Startup migration warning display defensively applies DiagnosticTextFormatter.SafeFailureSummary before calling ShowMigrationWarning.
  - Successful process-start PID tracking uses sanitized plugin filenames when provided and ExternalProcess when pluginName is omitted; legacy Arguments are never used as labels.
  - Successful-start regression tests inspect PID-store state at the onProcessStarted boundary because normal completion intentionally untracks process entries.
  - Generated reports use sanitized plugin basenames for cleaned, already-clean, skipped, and failed row prefixes; raw PluginName remains internal model data.
  - SafePluginName removes known command-flag suffixes such as -QAC and -autoload when they appear as display-name suffixes before an extension.
patterns_established:
  - Service-created failed result messages sanitize plugin basenames at source before downstream finalizer/model/report formatting.
  - Downstream result/report tests should include service-shaped safe messages to prove defensive boundaries remain safe end-to-end.
  - Path-bearing xEdit log-read warnings use a finalizer boundary: log raw local troubleshooting detail, expose only safe latest-log guidance.
  - Use shared DiagnosticSentinels in finalizer/report regressions to guard user-facing strings against Phase 11 disclosure fragments.
  - Callback-provided plugin names are untrusted display input until projected through DiagnosticTextFormatter.SafePluginName.
  - User-facing failure summaries use SafeFailureSummary with a latest-log fallback when callback text contains path, command, exception, or stack markers.
  - Restore UI display names are projections; they do not alter restore targets or backup containment semantics.
  - Unsafe backup metadata filenames are tested with path, command, quote, backtick, and control-character payloads.
  - Safe basename identifiers: Path.GetFileName plus display sanitization preserves filenames while dropping path directories and command-like characters.
  - Shared latest-log copy constants keep dialogs, rows, and reports aligned with the Phase 11 UI contract.
  - Command-boundary diagnostics: OperationFailed(operation) for status/message and LatestLogDetails for dialog details.
  - Validation-row diagnostics: SafeFileIdentifier(label, configuredPath, fallbackName) plus exact user fix action, without latest-log guidance for simple missing paths.
  - Use DiagnosticTextFormatter.SafeFileIdentifier for file browse diagnostics that can otherwise expose profile-root paths.
  - Use DiagnosticTextFormatter.SafeFolderIssue with a game display label for selected folder problems rather than a folder basename/path.
  - Treat ConfigPersistenceFailure.SafeSummary as the authoritative user-facing persistence category when surfacing Settings write/read failures.
  - Failed result boundaries use DiagnosticTextFormatter.CleaningFailedForPlugin or XEditReportedError as fallback text, preserving plugin filenames and latest-log guidance only.
  - Report failed-row formatting avoids duplicated plugin prefixes when the safe summary already starts with '<Plugin>:' .
  - Process log boundary: log ArgumentCount and ProcessId, not FileName or Arguments.
  - Cleaning launch boundary: log QuickAutoClean, LaunchMode, Game, Plugin filename, ArgumentCount, Status, and Reason around ExecuteAsync.
  - Startup source guard: private startup code can be protected by tests that reject forbidden literal templates and warning interpolation.
  - Use DiagnosticSentinels.UnsafeDiagnosticSentinels for future Phase 11 negative-disclosure tests instead of duplicating per-file sentinel arrays.
  - Verification-only tasks do not create empty commits; their command results are recorded in the plan summary.
  - Migration warning tests assert positive actionable copy and shared negative-disclosure sentinels.
  - UI warning boundaries sanitize service-provided summaries before ViewModel display.
  - Use onProcessStarted to assert transient PID tracking entries before the normal untrack-on-exit path runs.
  - Include legacy Arguments values in process-boundary tests without changing ProcessStartInfo launch preservation semantics.
  - Report display code should compute safePluginName once for failed rows, use it for fallback construction, duplicate-prefix comparison, and final prefix rendering.
  - Phase 11 report tests should assert both positive safe basenames and negative absence of raw paths/control/command fragments.
observability_surfaces: []
drill_down_paths: []
duration: 4 min
verification_result: passed
completed_at: 2026-05-01
blocker_discovered: false
---
# S12: User Facing Diagnostics Boundaries

**# Phase 11 Plan 10: CleaningService Failed Message Source Boundary Summary**

## What Happened

# Phase 11 Plan 10: CleaningService Failed Message Source Boundary Summary

**CleaningService failed-result messages now sanitize unsafe plugin basenames at source before finalizer, summary, and report surfaces consume them.**

## Performance

- **Duration:** 5 min
- **Started:** 2026-05-01T07:01:37Z
- **Completed:** 2026-05-01T07:06:23Z
- **Tasks:** 2 completed
- **Files modified:** 4

## Accomplishments

- Added RED tests proving command-build failures and unexpected launch exceptions leaked unsafe plugin basename characters from `CleaningService` failed messages.
- Added an end-to-end downstream regression where a service-shaped safe failed message flows through `PluginResultFinalizer`, `PluginCleaningResult.Summary`, and `CleaningSessionResult.GenerateReport()`.
- Updated `CleaningService` to compute `safePluginName` before command-build failure handling, log sanitized plugin values there, and return latest-log safe copy.
- Updated unexpected exception handling to log the sanitized plugin display value and return `DiagnosticTextFormatter.CleaningFailedForPlugin(plugin.FileName)`.
- Updated `11-VERIFICATION.md` to mark the CR-01/SEC-01 gap closed after targeted and full solution verification passed.

## Task Commits

Each task was committed atomically:

1. **Task 1: Prove CleaningService failed messages sanitize unsafe plugin basenames** - `e86508b` (test)
2. **Task 2: Sanitize CleaningService failed result messages at source** - `fe26ce3` (fix)

**Plan metadata:** pending final docs commit

_Note: This TDD plan produced the required RED and GREEN commits._

## Files Created/Modified

- `AutoQAC/Services/Cleaning/CleaningService.cs` - Uses sanitized plugin display text in command-build failure logs/messages and shared safe failed-cleaning copy for unexpected exceptions.
- `AutoQAC.Tests/Services/CleaningServiceTests.cs` - Adds unsafe basename regressions for command-build and unexpected launch exception failure paths, and aligns existing expectations with latest-log copy.
- `AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs` - Adds downstream finalizer/summary/report regression for service-shaped safe failed messages.
- `.planning/phases/11-user-facing-diagnostics-boundaries/11-VERIFICATION.md` - Records the gap closure and final verified SEC-01 status.
- `.planning/phases/11-user-facing-diagnostics-boundaries/11-10-SUMMARY.md` - Records plan execution, verification, and state handoff details.

## Decisions Made

- CleaningService command-build failures now use the same sanitized plugin display name that launch diagnostics already use, preventing unsafe basename characters from entering failed result text.
- Unexpected launch exceptions now return `DiagnosticTextFormatter.CleaningFailedForPlugin(plugin.FileName)` instead of interpolating a bespoke raw filename message, keeping the source boundary consistent with downstream report/model helpers.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- RED verification failed as expected before Task 2 because `CleaningService` still interpolated raw `plugin.FileName` in command-build and unexpected-exception failed messages.
- A first full-solution verification attempt was run concurrently with a targeted test command and hit an AutoQAC PDB file lock; rerunning the full solution test suite sequentially passed.

## TDD Gate Compliance

- RED gate: `e86508b` added failing service/finalizer regression tests before production changes.
- GREEN gate: `fe26ce3` implemented source sanitization and targeted tests passed.
- REFACTOR gate: not needed; no behavior-neutral cleanup was made after GREEN.

## Verification

- RED: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningServiceTests|FullyQualifiedName~PluginResultFinalizerTests|FullyQualifiedName~CleaningSessionResultTests" --nologo` failed before Task 2 with two expected `CleaningServiceTests` failures.
- GREEN/final targeted: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningServiceTests|FullyQualifiedName~PluginResultFinalizerTests|FullyQualifiedName~CleaningSessionResultTests" --nologo` passed, 53/53 tests.
- PASS: `dotnet test AutoQACSharp.slnx --nologo` passed, 1054/1054 tests.
- PASS: `AutoQAC/Services/Cleaning/CleaningService.cs` contains `var safePluginName = DiagnosticTextFormatter.SafePluginName(plugin.FileName);` before command-build failure handling.
- PASS: `AutoQAC/Services/Cleaning/CleaningService.cs` no longer contains raw `plugin.FileName` interpolation in command-build or unexpected exception failed messages.
- PASS: `AutoQAC/Services/Cleaning/CleaningService.cs` contains `DiagnosticTextFormatter.CleaningFailedForPlugin(plugin.FileName)` in the unexpected exception failure path.

## Known Stubs

None. Stub-pattern scans found only existing nullable/default control-flow and test capture assignments; no UI-rendered placeholders or mock data were introduced.

## Auth Gates

None.

## Threat Flags

None - this plan hardened existing failed-result trust boundaries and introduced no new network endpoints, auth paths, file access patterns, or schema boundaries.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- The remaining SEC-01/CR-01 verification gap is closed.
- Phase 11 is ready for final verification/milestone wrap-up with all 10 plans summarized.

## Self-Check: PASSED

- FOUND: `AutoQAC/Services/Cleaning/CleaningService.cs`
- FOUND: `AutoQAC.Tests/Services/CleaningServiceTests.cs`
- FOUND: `AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs`
- FOUND: `.planning/phases/11-user-facing-diagnostics-boundaries/11-VERIFICATION.md`
- FOUND: `.planning/phases/11-user-facing-diagnostics-boundaries/11-10-SUMMARY.md`
- FOUND: commit `e86508b`
- FOUND: commit `fe26ce3`

---
*Phase: 11-user-facing-diagnostics-boundaries*
*Completed: 2026-05-01*

# Phase 11 Plan 11: xEdit Log Warning Tooltip Boundary Summary

**Path-bearing xEdit main-log warnings now remain local-log-only while progress/result tooltip data uses stable latest-log copy.**

## Performance

- **Duration:** 10 min
- **Started:** 2026-05-01T07:27:44Z
- **Completed:** 2026-05-01T07:37:44Z
- **Tasks:** 3 completed
- **Files modified:** 3

## Accomplishments

- Added a RED regression test for a missing xEdit main-log warning containing `C:\Users\Alice\AppData\Local\SSEEdit\SSEEdit_log.txt`.
- Implemented `SafeLogReadWarning` in `PluginResultFinalizer` and assigned it to `LogParseWarning` while preserving the raw warning in `_loggerMock.Warning`/local logs.
- Verified `PluginCleaningResult.LogParseWarning`, `PluginCleaningResult.Summary`, and `CleaningSessionResult.GenerateReport()` exclude the shared Phase 11 unsafe diagnostic sentinels.
- Ran focused Phase 11/finalizer tests and the full solution suite successfully.
- Updated `11-VERIFICATION.md` to `status: verified` and `31/31 must-haves verified`, with the raw xEdit main-log tooltip gap closed by Plan 11-11.

## Task Commits

Each task was committed atomically:

1. **Task 1: Prove path-bearing xEdit log warnings stay out of LogParseWarning** - `5e77323` (test)
2. **Task 2: Replace UI-bound log-read warnings with stable safe latest-log copy** - `10d214d` (fix)
3. **Task 3: Run final Phase 11 gap verification** - `3802fca` (docs)

**Plan metadata:** pending final docs commit

_Note: This TDD plan produced the required RED and GREEN commits._

## Files Created/Modified

- `AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs` - Adds the path-bearing main-log warning regression and shared sentinel assertions over result/report strings.
- `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs` - Adds `SafeLogReadWarning` and uses it for `LogParseWarning` while retaining raw warning logs.
- `.planning/phases/11-user-facing-diagnostics-boundaries/11-VERIFICATION.md` - Marks Phase 11 verified, records test evidence, and closes the remaining tooltip disclosure gap.
- `.planning/phases/11-user-facing-diagnostics-boundaries/11-11-SUMMARY.md` - Records execution, verification, and state handoff details.

## Decisions Made

- Kept `XEditLogFileService` behavior unchanged so the raw missing-main-log path remains available as the direct failed local resource permitted by D-14.
- Added the safe-copy boundary in `PluginResultFinalizer`, the seam that feeds `PluginCleaningResult.LogParseWarning` and the existing ProgressWindow tooltip binding.
- Left `ProgressWindow.axaml` unchanged because sanitizing the bound model value is the safer source boundary and avoids UI workarounds.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- RED verification failed as expected before Task 2 because `PluginResultFinalizer` copied the raw path-bearing warning into `LogParseWarning`.
- No unrelated test or build failures were encountered.

## TDD Gate Compliance

- RED gate: `5e77323` added the failing log-warning disclosure regression before production changes.
- GREEN gate: `10d214d` implemented the safe finalizer boundary and focused tests passed.
- REFACTOR gate: not needed; no behavior-neutral cleanup was made after GREEN.

## Verification

- RED: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~PluginResultFinalizerTests" --nologo` failed before Task 2 with the expected `LogParseWarning` raw-path mismatch.
- GREEN/focused: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~PluginResultFinalizerTests|FullyQualifiedName~Phase11" --nologo` passed, 16/16 tests.
- Final focused: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~PluginResultFinalizerTests|FullyQualifiedName~Phase11" --nologo` passed, 16/16 tests.
- Full solution: `dotnet test AutoQACSharp.slnx --nologo` passed: QueryPlugins.Tests 61/61, AutoQAC.Tests 994/994.
- PASS: `11-VERIFICATION.md` contains `status: verified`.
- PASS: `11-VERIFICATION.md` contains `31/31 must-haves verified`.
- PASS: `11-VERIFICATION.md` records that the raw xEdit main-log tooltip gap is closed by Plan 11-11.

## Known Stubs

None. Stub-pattern review found only existing null/empty collection patterns in model/test control flow; no UI-rendered placeholder or mock data was introduced.

## Auth Gates

None.

## Threat Flags

None - this plan hardened an existing result-to-tooltip trust boundary and introduced no new network endpoints, auth paths, file access patterns, or schema boundaries.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Phase 11 is verified with 31/31 must-haves passing.
- SEC-01 is closed for the remaining progress/result tooltip gap.
- Phase 11 is ready for milestone wrap-up or final project verification.

## Self-Check: PASSED

- FOUND: `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs`
- FOUND: `AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs`
- FOUND: `.planning/phases/11-user-facing-diagnostics-boundaries/11-VERIFICATION.md`
- FOUND: `.planning/phases/11-user-facing-diagnostics-boundaries/11-11-SUMMARY.md`
- FOUND: commit `5e77323`
- FOUND: commit `10d214d`
- FOUND: commit `3802fca`

---
*Phase: 11-user-facing-diagnostics-boundaries*
*Completed: 2026-05-01*

# Phase 11 Plan 12: Callback Dialog Boundary Summary

**Timeout retry and backup failure dialogs now sanitize callback-provided plugin/error text before it reaches user-facing copy.**

## Accomplishments

- Added RED coverage for timeout retry callbacks receiving path, command, quote, and backtick plugin-name payloads.
- Added RED coverage for backup failure callbacks receiving unsafe plugin names plus shared path/command/exception/stack sentinels.
- Updated `CleaningCommandsViewModel.HandleTimeoutRetryAsync` to use `DiagnosticTextFormatter.SafePluginName(pluginName)` in retry dialog copy.
- Updated `CleaningCommandsViewModel.HandleBackupFailureAsync` to pass safe plugin names and `SafeFailureSummary` fallback copy to the dialog service.
- Added defensive `SafePluginName` and `SafeFailureSummary` handling inside `MessageDialogService.ShowBackupFailureDialogAsync` before TextBlock creation.

## Verification

- RED: focused diagnostics tests failed before production changes for raw timeout retry and backup failure callback text.
- GREEN: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ErrorDialogTests|FullyQualifiedName~Phase11DiagnosticsBoundaryTests|FullyQualifiedName~RestoreViewModelTests|FullyQualifiedName~DiagnosticTextFormatterTests" --nologo` passed, 84/84.
- Full solution: `dotnet test AutoQACSharp.slnx --nologo` passed: QueryPlugins.Tests 61/61, AutoQAC.Tests 997/997.

## Deviations from Plan

- No task commits were created in this session because the user did not explicitly request commits.

## Next Phase Readiness

- SEC-01 callback dialog gaps for timeout retry and backup failure are closed.
- Backup failure dialog button choices and sequential cleaning behavior remain unchanged.

# Phase 11 Plan 13: Restore Metadata Display Boundary Summary

**Restore Selected now displays sanitized backup metadata filenames while preserving original restore service input.**

## Accomplishments

- Added RED coverage for a path-like/control-character `BackupPluginEntry.FileName` loaded from backup metadata.
- Verified confirmation message, error dialog message, and failure status text exclude shared unsafe diagnostic sentinels.
- Verified `IBackupService.RestorePluginAsync` still receives the same original `BackupPluginEntry` object.
- Updated `RestoreViewModel.RestorePluginAsync` to compute `safePluginName` and use it for confirmation, status, and error copy.

## Verification

- RED: focused diagnostics tests failed before production changes for raw Restore Selected metadata display text.
- GREEN: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ErrorDialogTests|FullyQualifiedName~Phase11DiagnosticsBoundaryTests|FullyQualifiedName~RestoreViewModelTests|FullyQualifiedName~DiagnosticTextFormatterTests" --nologo` passed, 84/84.
- Full solution: `dotnet test AutoQACSharp.slnx --nologo` passed: QueryPlugins.Tests 61/61, AutoQAC.Tests 997/997.

## Deviations from Plan

- No task commits were created in this session because the user did not explicitly request commits.

## Next Phase Readiness

- SEC-01 Restore Selected metadata display gap is closed.
- Restore service behavior, trusted restore root handling, and restore-all behavior remain unchanged.

# Phase 11 Plan 01: Shared Safe Diagnostics Formatter Summary

**Shared safe diagnostics formatter with sentinel-tested copy primitives for path, command, exception, plugin, and latest-log boundaries.**

## Performance

- **Duration:** 3 min
- **Started:** 2026-05-01T04:23:00Z
- **Completed:** 2026-05-01T04:25:39Z
- **Tasks:** 2 completed
- **Files modified:** 2

## Accomplishments

- Added focused RED tests covering exact UI-SPEC copy, sanitized basenames, plugin failure rows, xEdit error rows, and unsafe failure-summary sentinels.
- Implemented `DiagnosticTextFormatter` with public constants and helpers for safe operation, file, folder, plugin, and failure-summary text.
- Verified focused formatter tests pass and the full solution builds without warnings or errors.

## Task Commits

Each task was committed atomically:

1. **Task 1: RED — lock shared diagnostic text behavior** - `0f691bd` (test)
2. **Task 2: GREEN — implement shared diagnostic text formatter** - `2dffa1f` (feat)

**Plan metadata:** `047f760` (docs)

_Note: This TDD plan produced the required RED and GREEN commits._

## Files Created/Modified

- `AutoQAC.Tests/Models/DiagnosticTextFormatterTests.cs` - Locks expected safe diagnostic copy and negative-disclosure sentinel behavior.
- `AutoQAC/Models/Diagnostics/DiagnosticTextFormatter.cs` - Provides shared safe formatting helpers and constants for later Phase 11 consumers.

## Decisions Made

- Diagnostic text primitives were implemented as a public static model-layer helper to avoid introducing a DI/service dependency into models and report generation.
- Unsafe failure summary detection is intentionally conservative and case-insensitive: if a candidate includes path, command, exception, executable, stack-frame, or control-whitespace markers, callers get the safe fallback.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Replaced unsupported FluentAssertions string-comparison overload**
- **Found during:** Task 2 (GREEN — implement shared diagnostic text formatter)
- **Issue:** The RED tests used `NotContain(string, StringComparison)`, which is unavailable in the installed FluentAssertions version and blocked the GREEN compile.
- **Fix:** Switched the assertion to `IndexOf(..., StringComparison.OrdinalIgnoreCase).Should().Be(-1)` while preserving the same case-insensitive negative-disclosure behavior.
- **Files modified:** `AutoQAC.Tests/Models/DiagnosticTextFormatterTests.cs`
- **Verification:** `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~DiagnosticTextFormatterTests --nologo` passed.
- **Committed in:** `2dffa1f` (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** The auto-fix preserved the intended test behavior and removed a test-framework compatibility blocker. No product scope changed.

## Issues Encountered

- Focused RED test command failed before Task 2 because `AutoQAC.Models.Diagnostics.DiagnosticTextFormatter` did not exist, as required by the TDD gate.

## TDD Gate Compliance

- RED commit present: `0f691bd` (`test(11-01): add failing diagnostic formatter contract tests`)
- GREEN commit present after RED: `2dffa1f` (`feat(11-01): implement safe diagnostic text formatter`)
- REFACTOR commit: not needed; no behavior-neutral cleanup was made after GREEN.

## Verification

- PASS: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~DiagnosticTextFormatterTests --nologo`
- PASS: `dotnet build AutoQACSharp.slnx --nologo`

## Known Stubs

None.

## Threat Flags

None - no new network endpoints, auth paths, file access patterns, or schema trust boundaries were introduced.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Ready for Plan 11-02 to consume `DiagnosticTextFormatter` in cleaning/preview dialogs, status text, and pre-clean validation identifiers.

## Self-Check: PASSED

- FOUND: `AutoQAC/Models/Diagnostics/DiagnosticTextFormatter.cs`
- FOUND: `AutoQAC.Tests/Models/DiagnosticTextFormatterTests.cs`
- FOUND: commit `0f691bd`
- FOUND: commit `2dffa1f`

---
*Phase: 11-user-facing-diagnostics-boundaries*
*Completed: 2026-05-01*

# Phase 11 Plan 02: Main Cleaning Command Diagnostics Boundary Summary

**Cleaning/preview command failures and pre-clean path validation now use safe latest-log copy and sanitized basename identifiers instead of raw exception, stack, command, or local path details.**

## Performance

- **Duration:** 7 min
- **Started:** 2026-05-01T04:26:30Z
- **Completed:** 2026-05-01T04:33:08Z
- **Tasks:** 3 completed
- **Files modified:** 2

## Accomplishments

- Added failing command-boundary tests proving unexpected cleaning and preview exceptions with path/command/stack sentinels do not cross into dialog/status text.
- Replaced raw `ex.Message`, `ex.StackTrace`, `Error:`, and `Stack Trace:` UI copy in cleaning/preview catches with `DiagnosticTextFormatter.OperationFailed(...)` and `LatestLogDetails`.
- Added and passed validation-row tests for xEdit, MO2, non-Mutagen load-order files, Mutagen false-positive prevention, and unsafe basename fallback behavior.
- Updated pre-clean validation rows to show safe setting identifiers such as `xEdit Path (SSEEdit.exe)`, `MO2 Path (ModOrganizer.exe)`, and `Load Order File (plugins.txt)` with direct fix guidance.

## Task Commits

Each task was committed atomically, with additional RED/GREEN commits where the TDD gate required them:

1. **Task 1: RED — prove cleaning and preview failures hide unsafe exception details** - `d72253d` (test)
2. **Task 2: GREEN — replace raw exception dialog/status copy** - `431b218` (feat)
3. **Task 3: Safe pre-clean validation identifiers for configured paths** - `2377a42` (test RED), `d3364d1` (feat GREEN)

**Plan metadata:** `e1cee8b` (docs)

_Note: This TDD plan produced RED and GREEN commits for command failure copy and validation identifier behavior._

## Files Created/Modified

- `AutoQAC.Tests/ViewModels/ErrorDialogTests.cs` - Adds disclosure-boundary tests for unexpected cleaning/preview errors and xEdit/MO2/load-order validation rows.
- `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` - Uses shared safe diagnostic formatter copy for cleaning/preview failures and pre-clean path validation identifiers.

## Decisions Made

- Unexpected cleaning and preview exceptions are still logged through `ILoggingService`, but every user-facing dialog/status value is now deterministic safe copy from `DiagnosticTextFormatter`.
- Invalid operation command-boundary failures now avoid displaying raw exception text in validation rows, preserving SEC-01 even when validation exceptions carry path or command details.
- Simple missing-path validation intentionally omits latest-log guidance; rows identify the setting and safe basename plus the exact corrective action.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- RED command-boundary tests failed because `StatusText` and dialog details used raw exception text/stack content, as expected for the TDD gate.
- RED validation tests failed because xEdit, MO2, and load-order rows displayed full configured paths, as expected before the GREEN implementation.

## TDD Gate Compliance

- RED commit present: `d72253d` (`test(11-02): add failing cleaning diagnostics tests`)
- GREEN commit present after RED: `431b218` (`feat(11-02): use safe command failure diagnostics`)
- Additional RED commit present: `2377a42` (`test(11-02): add failing validation disclosure tests`)
- Additional GREEN commit present after validation RED: `d3364d1` (`feat(11-02): sanitize pre-clean validation paths`)
- REFACTOR commit: not needed; no behavior-neutral cleanup was made after GREEN.

## Verification

- PASS: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~ErrorDialogTests --nologo`
- PASS: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ErrorDialogTests|FullyQualifiedName~CleaningCommandsViewModel" --nologo`

## Known Stubs

None. Null/captured-callback matches found during the stub scan are existing test setup values, not UI stubs or placeholder data flows.

## Threat Flags

None - no new network endpoints, auth paths, file access patterns, or schema trust boundaries were introduced.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Ready for Plan 11-03 to continue applying the shared diagnostics formatter to the next user-facing boundary without changing cleaning workflow behavior.

## Self-Check: PASSED

- FOUND: `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`
- FOUND: `AutoQAC.Tests/ViewModels/ErrorDialogTests.cs`
- FOUND: `.planning/phases/11-user-facing-diagnostics-boundaries/11-02-SUMMARY.md`
- FOUND: commit `d72253d`
- FOUND: commit `431b218`
- FOUND: commit `2377a42`
- FOUND: commit `d3364d1`

---
*Phase: 11-user-facing-diagnostics-boundaries*
*Completed: 2026-05-01*

# Phase 11 Plan 03: Configuration, Restore, and Settings Diagnostics Summary

**Configuration browse, restore session loading, and Settings persistence failures now use safe identifiers, typed safe summaries, and latest-log guidance without raw path-bearing exception text.**

## Performance

- **Duration:** 6 min
- **Started:** 2026-05-01T04:37:26Z
- **Completed:** 2026-05-01T04:43:27Z
- **Tasks:** 3 completed
- **Files modified:** 6

## Accomplishments

- Added TDD regression tests for configuration load-order parse failures and missing selected game data folders, proving user-facing text excludes selected paths and raw exception messages.
- Updated `ConfigurationViewModel` to use shared safe file/folder diagnostics for browse failures while preserving plugin refresh, skip-list, selection, and persistence behavior.
- Added and implemented restore session loading coverage so backup session enumeration failures show exact latest-log guidance instead of `ex.Message`.
- Added Settings persistence write/read tests with unsafe path-bearing detail and preserved `ConfigPersistenceFailure.SafeSummary` in the visible banner.

## Task Commits

Each TDD task was committed atomically:

1. **Task 1 RED: Safe configuration browse failure copy tests** - `b829d5a` (test)
2. **Task 1 GREEN: Safe configuration browse diagnostics** - `a922145` (feat)
3. **Task 2 RED: Safe restore session loading failure test** - `d9659dd` (test)
4. **Task 2 GREEN: Safe restore session load status** - `e4267c8` (feat)
5. **Task 3 RED: Settings safe-summary disclosure tests** - `a6f3f96` (test)
6. **Task 3 GREEN: Settings persistence SafeSummary mapping** - `0e46c2a` (feat)

**Plan metadata:** final docs commit for this summary/state update.

## Files Created/Modified

- `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs` - Maps selected load-order and game-data folder browse failures through safe diagnostic formatter copy.
- `AutoQAC/ViewModels/RestoreViewModel.cs` - Replaces backup session load exception status text with exact latest-log guidance.
- `AutoQAC/ViewModels/SettingsViewModel.cs` - Preserves `ConfigPersistenceFailure.SafeSummary` for Settings save write failures and reload read failures.
- `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs` - Adds negative-disclosure coverage for load-order and game-data folder browse failures.
- `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs` - Adds backup session loading failure disclosure regression coverage.
- `AutoQAC.Tests/ViewModels/SettingsViewModelTests.cs` - Adds Settings write/read unsafe-detail tests and updates save write-failure expectation to SafeSummary.

## Decisions Made

- Used a private game-display helper in `ConfigurationViewModel` rather than adding a new service dependency, keeping the change scoped to browse diagnostics.
- Kept simple missing selected game-data folder copy free of latest-log guidance, matching D-08's distinction between missing-path validation and technical failures.
- Preserved cleaning-specific flush failure copy in Settings while switching direct Settings save write failures and reload read failures to `SafeSummary`.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- Existing configuration parsing error test expected the literal word `Error` in status text; it was tightened to accept the new safe `failed` latest-log wording required by the plan.
- The load-order disclosure test uses a temporary `plugins.txt` file while injecting the `C:\Users\Alice` sentinel through the thrown exception, avoiding fragile assumptions about creating files under a real user profile path.

## TDD Gate Compliance

- RED commits present: `b829d5a`, `d9659dd`, `a6f3f96`.
- GREEN commits present after their RED commits: `a922145`, `e4267c8`, `0e46c2a`.
- REFACTOR commit: not needed; no behavior-neutral cleanup was made after GREEN.

## Verification

- PASS: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~Configuration" --nologo`
- PASS: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~RestoreViewModelTests --nologo`
- PASS: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~SettingsViewModelTests --nologo`
- PASS: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~RestoreViewModelTests|FullyQualifiedName~SettingsViewModelTests|FullyQualifiedName~Configuration" --nologo`

## Known Stubs

None. Stub-pattern scan matches were existing nullable field/test variables, designed null-clearing assignments, or collection initializers; no UI-facing placeholder/mock data was introduced.

## Threat Flags

None - no new network endpoints, auth paths, file access patterns, schema changes, or new trust-boundary surfaces were introduced. Existing file/folder browse and persistence failure surfaces were hardened.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Ready for Plan 11-04 to continue diagnostics-boundary hardening on migration/startup or result/export surfaces using the same safe formatter patterns.

## Self-Check: PASSED

- FOUND: `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`
- FOUND: `AutoQAC/ViewModels/RestoreViewModel.cs`
- FOUND: `AutoQAC/ViewModels/SettingsViewModel.cs`
- FOUND: `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs`
- FOUND: `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs`
- FOUND: `AutoQAC.Tests/ViewModels/SettingsViewModelTests.cs`
- FOUND: commit `b829d5a`
- FOUND: commit `a922145`
- FOUND: commit `d9659dd`
- FOUND: commit `e4267c8`
- FOUND: commit `a6f3f96`
- FOUND: commit `0e46c2a`

---
*Phase: 11-user-facing-diagnostics-boundaries*
*Completed: 2026-05-01*

# Phase 11 Plan 04: Cleaning Result and Report Boundary Summary

**Failed cleaning result rows and exported reports now preserve plugin filenames with safe latest-log guidance while suppressing paths, commands, stacks, and raw xEdit exception-log content.**

## Performance

- **Duration:** 4 min
- **Started:** 2026-05-01T04:45:11Z
- **Completed:** 2026-05-01T04:48:59Z
- **Tasks:** 2 completed
- **Files modified:** 5

## Accomplishments

- Added TDD coverage proving unsafe runner failure messages fall back to `Plugin.esp: Cleaning failed. See the latest AutoQAC log.`.
- Converted xEdit exception-log detections into safe result `Message` and `LogParseWarning` text while logging only the safe boundary trigger.
- Added report disclaimer exact-once coverage and defensive failed-row/report summary sanitization.
- Updated failed `PluginCleaningResult.Summary` to avoid raw exception/path/command details.

## Task Commits

Each task was committed atomically:

1. **Task 1 RED: finalizer boundary tests** - `51225aa` (test)
2. **Task 1 GREEN: finalizer source sanitization** - `8e048fa` (feat)
3. **Task 2 RED: report boundary tests** - `4340019` (test)
4. **Task 2 GREEN: report and summary defensive fallback** - `3248555` (feat)

**Plan metadata:** pending final docs commit

_Note: This TDD plan produced RED and GREEN commits for each planned safety boundary._

## Files Created/Modified

- `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs` - Sanitizes failed result messages and xEdit exception-log outcomes before creating `PluginCleaningResult`.
- `AutoQAC/Models/CleaningSessionResult.cs` - Adds the report disclaimer and safe failed-row formatting with duplicate-plugin-prefix protection.
- `AutoQAC/Models/PluginCleaningResult.cs` - Uses safe failure-summary fallback for failed display summaries.
- `AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs` - Locks source-boundary sanitization and xEdit exception-log suppression.
- `AutoQAC.Tests/Models/CleaningSessionResultTests.cs` - Locks disclaimer exact-once behavior, safe failed report rows, and safe failed summaries.

## Decisions Made

- xEdit exception-log content remains in the xEdit-owned troubleshooting source, not in AutoQAC result rows/reports or AutoQAC log properties, matching Plan 11-04's boundary scope.
- Report generation defensively sanitizes failed messages even though `PluginResultFinalizer` now sanitizes at source, ensuring hand-constructed or future failed results cannot leak unsafe details into exports.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- RED tests failed as expected before each GREEN implementation: finalizer messages still exposed unsafe runner/xEdit content, and reports/summaries lacked disclaimer/fallback behavior.

## TDD Gate Compliance

- RED commits present: `51225aa` and `4340019`.
- GREEN commits present after corresponding RED commits: `8e048fa` and `3248555`.
- REFACTOR commit: not needed; no behavior-neutral cleanup was made after GREEN.

## Verification

- PASS: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~PluginResultFinalizerTests --nologo`
- PASS: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~CleaningSessionResultTests --nologo`
- PASS: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~PluginResultFinalizerTests|FullyQualifiedName~CleaningSessionResultTests" --nologo`

## Known Stubs

None. Stub-pattern scan found only nullable local variables/null checks in existing control flow, not UI/mock placeholders.

## Threat Flags

None - no new network endpoints, auth paths, file access patterns, schema changes, or trust boundaries were introduced.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Ready for Plan 11-05 to continue diagnostics-boundary hardening on process/startup log surfaces.

## Self-Check: PASSED

- FOUND: `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs`
- FOUND: `AutoQAC/Models/CleaningSessionResult.cs`
- FOUND: `AutoQAC/Models/PluginCleaningResult.cs`
- FOUND: `AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs`
- FOUND: `AutoQAC.Tests/Models/CleaningSessionResultTests.cs`
- FOUND: commit `51225aa`
- FOUND: commit `8e048fa`
- FOUND: commit `4340019`
- FOUND: commit `3248555`

---
*Phase: 11-user-facing-diagnostics-boundaries*
*Completed: 2026-05-01*

# Phase 11 Plan 05: Process, Cleaning, and Startup Diagnostics Boundary Summary

**Safe structured launch/startup diagnostics that preserve process construction while removing executable paths, raw argv payloads, and raw migration exception text from AutoQAC-owned logs and warnings.**

## Performance

- **Duration:** 6 min
- **Started:** 2026-05-01T04:51:12Z
- **Completed:** 2026-05-01T04:57:07Z
- **Tasks:** 3 completed
- **Files modified:** 6

## Accomplishments

- Replaced `ProcessExecutionService` process-start/failure log properties with safe operation, status, reason, argument-count, and process-ID fields.
- Added CleaningService launch-boundary diagnostics for direct xEdit and MO2 paths without reconstructing or logging command payloads.
- Replaced startup configured xEdit path logging with a safe configured/identifier event and made unexpected legacy migration warnings use latest-log guidance.
- Added TDD regression coverage for process logger capture, cleaning logger capture plus ProcessStartInfo preservation, and private startup source guards.

## Task Commits

Each TDD task was committed atomically:

1. **Task 1 RED: ProcessExecutionService log boundary tests** - `78342f6` (test)
2. **Task 1 GREEN: ProcessExecutionService safe diagnostics** - `4e60449` (feat)
3. **Task 2 RED: CleaningService launch log tests** - `3265f0d` (test)
4. **Task 2 GREEN: CleaningService safe launch diagnostics** - `feb0516` (feat)
5. **Task 3 RED: Startup diagnostics source guard** - `7dd465b` (test)
6. **Task 3 GREEN: Startup diagnostics and migration warning** - `4a40997` (feat)

**Plan metadata:** recorded in final docs commit.

## Files Created/Modified

- `AutoQAC/Services/Process/ProcessExecutionService.cs` - Logs process start/failure/success with safe structured fields and preserves launch cloning behavior.
- `AutoQAC/Services/Cleaning/CleaningService.cs` - Logs QuickAutoClean launch context around `ExecuteAsync` with mode/game/plugin filename/count/status/reason fields.
- `AutoQAC/App.axaml.cs` - Uses `DiagnosticTextFormatter.SafeFileIdentifier` for startup xEdit configuration and safe migration warning copy.
- `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs` - Captures process logger calls and verifies no executable path, raw arguments, or command fragments are emitted as log template/argument values.
- `AutoQAC.Tests/Services/CleaningServiceTests.cs` - Separately asserts safe cleaning logger calls and unchanged `ProcessStartInfo` direct/MO2 argv values.
- `AutoQAC.Tests/Integration/DependencyInjectionTests.cs` - Adds source guard coverage for private startup diagnostics and migration warning literals.

## Decisions Made

- Process-start logs intentionally use `ExternalProcess` as the generic operation because `ProcessExecutionService` is launch-mode agnostic; richer QuickAutoClean mode/game/plugin context is emitted by `CleaningService` at the caller boundary.
- `CleaningService` computes argument count from `ArgumentList.Count` when populated and otherwise treats a non-empty legacy `Arguments` string as one opaque argument payload, avoiding command reconstruction.
- Startup diagnostics use a safe xEdit identifier and configured boolean instead of the raw configured executable path.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- RED gates failed as expected for missing safe process, cleaning, and startup diagnostics behavior before each GREEN implementation.
- During Task 3 GREEN, the startup guard required the `DiagnosticTextFormatter.SafeFileIdentifier("xEdit Path"` call to appear in source as a direct literal substring matching the plan acceptance criterion; the implementation was adjusted before committing.

## TDD Gate Compliance

- RED commits present: `78342f6`, `3265f0d`, `7dd465b`.
- GREEN commits present after their corresponding RED commits: `4e60449`, `feb0516`, `4a40997`.
- REFACTOR commit: not needed; no behavior-neutral cleanup was made after GREEN.

## Verification

- PASS: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~ProcessExecutionServiceTests --nologo`
- PASS: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~CleaningServiceTests --nologo`
- PASS: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~DependencyInjectionTests|FullyQualifiedName~Startup" --nologo`
- PASS: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ProcessExecutionServiceTests|FullyQualifiedName~CleaningServiceTests|FullyQualifiedName~DependencyInjectionTests" --nologo`

## Known Stubs

None - no stubs were introduced. New `null`/empty values are test captures or existing optional parameter defaults, not user-facing placeholders.

## Threat Flags

None - no new network endpoints, auth paths, file access patterns, or schema trust boundaries were introduced.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Ready for Plan 11-06 to add phase-level disclosure sentinels and run the final verification sweep across Phase 11 surfaces.

## Self-Check: PASSED

- FOUND: `AutoQAC/Services/Process/ProcessExecutionService.cs`
- FOUND: `AutoQAC/Services/Cleaning/CleaningService.cs`
- FOUND: `AutoQAC/App.axaml.cs`
- FOUND: `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`
- FOUND: `AutoQAC.Tests/Services/CleaningServiceTests.cs`
- FOUND: `AutoQAC.Tests/Integration/DependencyInjectionTests.cs`
- FOUND: `.planning/phases/11-user-facing-diagnostics-boundaries/11-05-SUMMARY.md`
- FOUND: commit `78342f6`
- FOUND: commit `4e60449`
- FOUND: commit `3265f0d`
- FOUND: commit `feb0516`
- FOUND: commit `7dd465b`
- FOUND: commit `4a40997`

---
*Phase: 11-user-facing-diagnostics-boundaries*
*Completed: 2026-05-01*

# Phase 11 Plan 06: Phase-Level Diagnostics Boundary Guard Summary

**Shared Phase 11 sentinel tests now cover real ViewModel, report, and process/startup log boundaries, with focused and full-solution verification green.**

## Performance

- **Duration:** 9 min
- **Started:** 2026-05-01T04:57:00Z
- **Completed:** 2026-05-01T05:05:59Z
- **Tasks:** 2 completed
- **Files modified:** 5

## Accomplishments

- Added `DiagnosticSentinels.UnsafeDiagnosticSentinels` with the exact shared unsafe fragments required by the plan.
- Added a ViewModel boundary guard that drives `StartCleaningCommand` through a real unexpected-orchestrator-failure catch path before asserting no shared sentinel reaches status, validation, or dialog text.
- Added a report boundary guard that creates real failed `PluginCleaningResult` data, calls `CleaningSessionResult.GenerateReport()`, verifies the report disclaimer, and excludes every shared sentinel.
- Added log-boundary guards using captured `ProcessExecutionService` logger calls plus source checks for private startup/known forbidden diagnostic templates.
- Ran the focused Phase 11 verification command and the full solution test suite sequentially; both passed.

## Task Commits

Each task was handled atomically:

1. **Task 1: Add cross-surface Phase 11 disclosure sentinels** - `05614d6` (test)
2. **Task 2: Final Phase 11 verification sweep** - no code commit; verification-only task produced no file changes after both commands passed.

**Plan metadata:** pending final docs commit.

## Files Created/Modified

- `AutoQAC.Tests/Helpers/DiagnosticSentinels.cs` - Provides the shared Phase 11 unsafe sentinel array and payload helper.
- `AutoQAC.Tests/ViewModels/Phase11DiagnosticsBoundaryTests.cs` - Exercises a real `StartCleaningCommand` catch path and checks status/dialog/validation text against the shared sentinels.
- `AutoQAC.Tests/Models/Phase11ReportBoundaryTests.cs` - Exercises real `CleaningSessionResult.GenerateReport()` output with unsafe failed-result input.
- `AutoQAC.Tests/Services/Phase11LogBoundaryTests.cs` - Captures process logger calls and source-guards known bad process/startup diagnostic templates.
- `.planning/phases/11-user-facing-diagnostics-boundaries/11-06-SUMMARY.md` - Records execution, verification, and state handoff details.

## Decisions Made

- Phase-level guard tests use one reusable sentinel helper rather than duplicating local arrays in each test file.
- Verification-only Task 2 was not represented by an empty git commit because it made no file changes; its evidence is the command output recorded below.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed Phase 11 test compile/runtime issues before Task 1 verification**
- **Found during:** Task 1 (Add cross-surface Phase 11 disclosure sentinels)
- **Issue:** The initial log-boundary test missed the `AutoQAC.Models` namespace for `TrackedProcess`, the source guard read paths relative to the test output directory, and the ViewModel test used `InvalidOperationException`, which exercises the configuration-validation catch rather than the unexpected-error dialog catch.
- **Fix:** Added the missing namespace import, made the source guard locate the repository root from `AppContext.BaseDirectory`, and changed the orchestrator throw to a generic `Exception` so the test exercises the intended unexpected command catch path.
- **Files modified:** `AutoQAC.Tests/ViewModels/Phase11DiagnosticsBoundaryTests.cs`, `AutoQAC.Tests/Services/Phase11LogBoundaryTests.cs`
- **Verification:** `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~Phase11 --nologo` passed.
- **Committed in:** `05614d6` (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** The fixes made the new tests exercise the required production seams and did not expand product scope.

## Issues Encountered

- The first focused Phase 11 test run failed during Task 1 due to test implementation issues described in the deviation above; the issues were fixed before committing.
- No unrelated failures were encountered during the final verification sweep.

## Verification

- PASS: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~Phase11 --nologo` — 4 passed.
- PASS: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Phase11|FullyQualifiedName~ErrorDialogTests|FullyQualifiedName~RestoreViewModelTests|FullyQualifiedName~CleaningSessionResultTests|FullyQualifiedName~PluginResultFinalizerTests|FullyQualifiedName~ProcessExecutionServiceTests" --nologo` — 95 passed.
- PASS: `dotnet test AutoQACSharp.slnx --nologo` — QueryPlugins.Tests 61 passed; AutoQAC.Tests 982 passed.

## Known Stubs

None. Stub-pattern scanning found only an in-memory test collection initialized to `[]` in `Phase11LogBoundaryTests`; it is not UI-rendered placeholder/mock data.

## Threat Flags

None - this plan added tests only and did not introduce new network endpoints, auth paths, file access patterns at runtime, or schema trust boundaries.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Phase 11 is ready for verification and milestone completion. SEC-01 and SEC-02 are covered by focused behavioral/source guards, and the full solution suite is green.

## Self-Check: PASSED

- FOUND: `AutoQAC.Tests/Helpers/DiagnosticSentinels.cs`
- FOUND: `AutoQAC.Tests/ViewModels/Phase11DiagnosticsBoundaryTests.cs`
- FOUND: `AutoQAC.Tests/Models/Phase11ReportBoundaryTests.cs`
- FOUND: `AutoQAC.Tests/Services/Phase11LogBoundaryTests.cs`
- FOUND: commit `05614d6`

---
*Phase: 11-user-facing-diagnostics-boundaries*
*Completed: 2026-05-01*

# Phase 11 Plan 07: Legacy Migration Warning Boundary Summary

**Legacy migration warnings now use safe category copy with latest-log guidance plus a defensive startup sanitization boundary.**

## Performance

- **Duration:** 2m 7s
- **Started:** 2026-05-01T06:25:28Z
- **Completed:** 2026-05-01T06:27:34Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments

- Added RED regression tests proving legacy migration warnings reject path, command, exception, and stack-frame sentinel fragments.
- Replaced parse, write, backup, and delete migration warning messages with fixed safe latest-log guidance.
- Wrapped startup migration warning display with `DiagnosticTextFormatter.SafeFailureSummary` before calling `ShowMigrationWarning`.

## Task Commits

1. **Task 1: Prove legacy migration warnings exclude raw exception details** - `6469171` (test)
2. **Task 2: Replace migration warning UI copy with safe categories** - `a8ad466` (fix)

**Plan metadata:** pending final docs commit

## Files Created/Modified

- `AutoQAC/App.axaml.cs` - Sanitizes migration warning text before forwarding to `MainWindowViewModel.ShowMigrationWarning`.
- `AutoQAC/Services/Configuration/LegacyMigrationService.cs` - Uses safe fixed warning copy for parse, write, backup, and delete outcomes while preserving local logging.
- `AutoQAC.Tests/Services/LegacyMigrationServiceTests.cs` - Adds migration warning safety assertions and updates empty-file warning expectations to the safe parse category.
- `AutoQAC.Tests/Integration/DependencyInjectionTests.cs` - Guards startup source so direct `result.WarningMessage` forwarding does not regress.
- `.planning/phases/11-user-facing-diagnostics-boundaries/11-07-SUMMARY.md` - Records plan execution results.

## Decisions Made

- Legacy migration warnings now use fixed category copy with latest-log guidance rather than interpolated exception messages.
- Startup migration warning display defensively applies `DiagnosticTextFormatter.SafeFailureSummary` before calling `ShowMigrationWarning`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical] Aligned empty legacy file warnings with safe parse copy**
- **Found during:** Task 2 (Replace migration warning UI copy with safe categories)
- **Issue:** The existing empty-file warning was safe from raw exception details but lacked the required latest-log guidance for migration warning UI.
- **Fix:** Reused the same safe parse-failure copy for null/empty deserialization results and updated the existing unit test to assert the shared safety contract.
- **Files modified:** `AutoQAC/Services/Configuration/LegacyMigrationService.cs`, `AutoQAC.Tests/Services/LegacyMigrationServiceTests.cs`
- **Verification:** Targeted migration/DI test command passed.
- **Committed in:** `a8ad466`

---

**Total deviations:** 1 auto-fixed (1 missing critical)
**Impact on plan:** Required for consistent SEC-01 latest-log guidance across legacy migration warnings; no scope creep.

## Issues Encountered

- The source guard initially required `DiagnosticTextFormatter.SafeFailureSummary(result.WarningMessage` as a contiguous substring, so the production call was formatted to keep the first argument on the same line.

## User Setup Required

None - no external service configuration required.

## Known Stubs

None.

## Auth Gates

None.

## Threat Flags

None.

## Verification

- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~LegacyMigrationServiceTests|FullyQualifiedName~DependencyInjectionTests" --nologo` — passed, 13/13 tests.

## TDD Gate Compliance

- RED gate: `6469171` added failing regression tests before production changes.
- GREEN gate: `a8ad466` implemented the safe warning copy and boundary; targeted tests passed.

## Next Phase Readiness

- Verification gap #1 is closed for SEC-01.
- Plans 11-08 and 11-09 can proceed without depending on raw migration warning behavior.

## Self-Check: PASSED

- Confirmed all modified/source files and this summary exist.
- Confirmed task commits `6469171` and `a8ad466` exist in git history.

---
*Phase: 11-user-facing-diagnostics-boundaries*
*Completed: 2026-05-01*

# Phase 11 Plan 08: Successful Process Tracking Boundary Summary

**Successful external process starts now track only sanitized plugin labels or `ExternalProcess`, closing the raw legacy `Arguments` PID/log disclosure gap.**

## Performance

- **Duration:** 2m
- **Started:** 2026-05-01T06:33:57Z
- **Completed:** 2026-05-01T06:35:53Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments

- Added RED regression coverage proving successful legacy `ProcessStartInfo.Arguments` launches without `pluginName` must track `ExternalProcess` instead of launch text.
- Added a Phase 11 guard that checks both captured process logs and transient PID-store entries for successful starts.
- Replaced raw legacy argument fallback in `ExecuteAsync` with `GetSafeTrackingLabel`, using `DiagnosticTextFormatter.SafePluginName` for plugin names and `ExternalProcess` when omitted.

## Task Commits

1. **Task 1: Prove successful legacy-Arguments starts keep PID tracking safe** - `8681334` (test)
2. **Task 2: Use safe tracking labels for successful process starts** - `111cb68` (fix)

**Plan metadata:** pending final docs commit

## Files Created/Modified

- `AutoQAC/Services/Process/ProcessExecutionService.cs` - Uses safe PID tracking label selection and no longer derives labels from raw legacy arguments.
- `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs` - Adds successful legacy-Arguments process regression coverage with omitted plugin context.
- `AutoQAC.Tests/Services/Phase11LogBoundaryTests.cs` - Adds phase-level successful-start log and PID-store sentinel guard.
- `.planning/phases/11-user-facing-diagnostics-boundaries/11-08-SUMMARY.md` - Records execution results and verification evidence.

## Decisions Made

- Successful process-start PID tracking uses sanitized plugin filenames when provided and `ExternalProcess` when `pluginName` is omitted; legacy `Arguments` are never used as labels.
- Successful-start tests inspect PID-store entries from the `onProcessStarted` callback because `ProcessExecutionService` correctly untracks normal successful exits.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- The first RED test draft asserted PID-store contents after process exit, but successful completion intentionally untracks the PID. The tests were corrected before the RED commit to assert the transient tracking entry at the `onProcessStarted` boundary.

## Verification

- RED: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ProcessExecutionServiceTests|FullyQualifiedName~Phase11LogBoundaryTests" --nologo` failed before Task 2 because successful starts tracked `--info` instead of `ExternalProcess`.
- GREEN: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ProcessExecutionServiceTests|FullyQualifiedName~Phase11LogBoundaryTests" --nologo` passed, 16/16 tests.

## TDD Gate Compliance

- RED gate: `8681334` added failing regression tests before production changes.
- GREEN gate: `111cb68` implemented the safe label fix and targeted tests passed.
- REFACTOR gate: not needed; no behavior-neutral cleanup was made after GREEN.

## Known Stubs

None. New `=[]` initializers are in-memory test captures only and are not UI-rendered placeholder data.

## Auth Gates

None.

## Threat Flags

None - this plan changed an existing process/PID tracking boundary already covered by the plan threat model and introduced no new network endpoints, auth paths, file access patterns, or schema trust boundaries.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Verification gap #2 is closed for SEC-02.
- Plan 11-09 can proceed to the remaining report/plugin-name display gap without depending on raw process tracking behavior.

## Self-Check: PASSED

- FOUND: `AutoQAC/Services/Process/ProcessExecutionService.cs`
- FOUND: `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`
- FOUND: `AutoQAC.Tests/Services/Phase11LogBoundaryTests.cs`
- FOUND: `.planning/phases/11-user-facing-diagnostics-boundaries/11-08-SUMMARY.md`
- FOUND: commit `8681334`
- FOUND: commit `111cb68`

---
*Phase: 11-user-facing-diagnostics-boundaries*
*Completed: 2026-05-01*

# Phase 11 Plan 09: Report Plugin Display Boundary Summary

**Generated cleaning reports now render sanitized plugin basenames in every row section, including failed fallback summaries, without exposing raw path-like plugin names or command flags.**

## Performance

- **Duration:** 4 min
- **Started:** 2026-05-01T06:37:00Z
- **Completed:** 2026-05-01T06:41:28Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments

- Added RED regression tests for cleaned, already-clean, skipped, and failed report rows with unsafe plugin-name characters and path-like values.
- Added a Phase 11 report guard proving the shared report disclaimer remains present exactly once while unsafe plugin-name sentinels stay out of report text.
- Updated report generation to use `DiagnosticTextFormatter.SafePluginName(result.PluginName)` for every plugin display prefix.
- Tightened plugin display sanitization for known command-flag suffixes so `-QAC` and `-autoload` cannot survive in user-facing plugin basenames.

## Task Commits

Each task was committed atomically:

1. **Task 1: Prove report plugin-name prefixes are sanitized** - `1280401` (test)
2. **Task 2: Sanitize plugin display names across report rows** - `128f309` (fix)

**Plan metadata:** pending final docs commit

## Files Created/Modified

- `AutoQAC.Tests/Models/CleaningSessionResultTests.cs` - Adds unsafe plugin-name report regressions and negative assertions for paths, quotes, controls, separators, `-QAC`, and `-autoload`.
- `AutoQAC.Tests/Models/Phase11ReportBoundaryTests.cs` - Adds a phase-level unsafe plugin-name guard with disclaimer exact-once verification.
- `AutoQAC/Models/CleaningSessionResult.cs` - Uses sanitized plugin display names for cleaned, already-clean, skipped, and failed report rows.
- `AutoQAC/Models/Diagnostics/DiagnosticTextFormatter.cs` - Removes known command-flag suffixes from plugin display names before sanitizing user-facing basenames.

## Decisions Made

- Generated reports sanitize plugin display names at the report/export boundary rather than mutating `PluginCleaningResult.PluginName`, preserving raw model data for internal callers.
- Failed report rows build fallback text from the same sanitized prefix used in duplicate-prefix comparison and final rendering.
- `SafePluginName` strips only known command-flag suffixes (`-QAC`, `-autoload`) before the extension/end of string to avoid broadly removing useful dashes from normal plugin filenames.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical] Removed command-flag suffixes in SafePluginName**
- **Found during:** Task 2 (Sanitize plugin display names across report rows)
- **Issue:** Existing `SafePluginName` removed quotes, backticks, controls, separators, and invalid filename characters but left suffix-style `-QAC`/`-autoload` fragments in display names, while the plan acceptance criteria required those fragments to be absent from report output.
- **Fix:** Added a narrow `PluginCommandFlagPattern` that removes known command-flag suffixes only when they appear before a file extension or end of string.
- **Files modified:** `AutoQAC/Models/Diagnostics/DiagnosticTextFormatter.cs`
- **Verification:** `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningSessionResultTests|FullyQualifiedName~Phase11ReportBoundaryTests" --nologo` passed.
- **Committed in:** `128f309`

---

**Total deviations:** 1 auto-fixed (1 missing critical).
**Impact on plan:** The change was required to satisfy SEC-01/D-07 display-boundary requirements and remained limited to display-name sanitization.

## Issues Encountered

- RED verification failed as expected before implementation because reports still printed raw `result.PluginName` prefixes and because command-flag suffixes remained in safe plugin display names.

## Verification

- RED: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningSessionResultTests|FullyQualifiedName~Phase11ReportBoundaryTests" --nologo` failed before Task 2 with raw plugin paths/prefixes in generated report output.
- GREEN/final: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningSessionResultTests|FullyQualifiedName~Phase11ReportBoundaryTests" --nologo` passed, 29/29 tests.
- PASS: `AutoQAC/Models/CleaningSessionResult.cs` contains `DiagnosticTextFormatter.SafePluginName(result.PluginName)`.
- PASS: `AutoQAC/Models/CleaningSessionResult.cs` no longer contains `sb.AppendLine($"  {result.PluginName}`.
- PASS: `AutoQAC/Models/CleaningSessionResult.cs` no longer contains `var prefix = $"{result.PluginName}:"`.

## TDD Gate Compliance

- RED gate: `1280401` added failing report/display-name regression tests before production changes.
- GREEN gate: `128f309` implemented sanitized report display names and targeted tests passed.
- REFACTOR gate: not needed; no behavior-neutral cleanup was made after GREEN.

## Known Stubs

None. Stub-pattern scans found only existing null/empty collection patterns in model/test code; no UI-rendered placeholders or mock data were introduced.

## Auth Gates

None.

## Threat Flags

None - this plan modified an existing report/export trust boundary already covered by the plan threat model and introduced no new network endpoints, auth paths, file access patterns, or schema boundaries.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Verification gap #3 is closed for SEC-01 report/result display boundaries.
- Phase 11 gap-closure execution is complete and ready for phase verification/milestone wrap-up.

## Self-Check: PASSED

- FOUND: `AutoQAC/Models/CleaningSessionResult.cs`
- FOUND: `AutoQAC/Models/Diagnostics/DiagnosticTextFormatter.cs`
- FOUND: `AutoQAC.Tests/Models/CleaningSessionResultTests.cs`
- FOUND: `AutoQAC.Tests/Models/Phase11ReportBoundaryTests.cs`
- FOUND: `.planning/phases/11-user-facing-diagnostics-boundaries/11-09-SUMMARY.md`
- FOUND: commit `1280401`
- FOUND: commit `128f309`

---
*Phase: 11-user-facing-diagnostics-boundaries*
*Completed: 2026-05-01*
