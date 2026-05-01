---
phase: 11-user-facing-diagnostics-boundaries
plan: 12
subsystem: diagnostics-security
tags: [csharp, dotnet, dialogs, diagnostics, security, tdd]

requires:
  - phase: 11-user-facing-diagnostics-boundaries
    provides: Safe diagnostics formatter and completed Phase 11 Plans 11-01 through 11-11
provides:
  - Safe timeout retry callback dialog copy
  - Safe backup failure callback dialog copy
  - Defensive backup failure dialog-service sanitization before TextBlock rendering
  - Regression coverage for unsafe callback plugin/error payloads
affects: [cleaning-commands, message-dialog-service, sec-01, phase-11-verification]

tech-stack:
  added: []
  patterns:
    - TDD RED/GREEN regression closure for user-facing callback trust boundaries
    - ViewModel boundary sanitization plus dialog-service defense-in-depth
    - Safe fallback summaries route raw technical detail to latest-log guidance

key-files:
  created:
    - .planning/phases/11-user-facing-diagnostics-boundaries/11-12-SUMMARY.md
  modified:
    - AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs
    - AutoQAC/Services/UI/MessageDialogService.cs
    - AutoQAC.Tests/ViewModels/ErrorDialogTests.cs
    - AutoQAC.Tests/ViewModels/Phase11DiagnosticsBoundaryTests.cs
    - .planning/phases/11-user-facing-diagnostics-boundaries/11-VERIFICATION.md
    - .planning/ROADMAP.md

key-decisions:
  - "Timeout retry dialogs keep the existing retry behavior but display only DiagnosticTextFormatter.SafePluginName output."
  - "Backup failure callbacks sanitize both plugin and failure summary before calling IMessageDialogService."
  - "MessageDialogService defensively re-sanitizes backup failure dialog inputs because it renders directly to Avalonia TextBlocks."

patterns-established:
  - "Callback-provided plugin names are untrusted display input until projected through DiagnosticTextFormatter.SafePluginName."
  - "User-facing failure summaries use SafeFailureSummary with a latest-log fallback when callback text contains path, command, exception, or stack markers."

requirements-completed: [SEC-01]

duration: 8 min
completed: 2026-05-01
---

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
