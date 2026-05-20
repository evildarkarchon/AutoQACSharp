---
id: T12
parent: S12
milestone: M001
provides:
  - Safe timeout retry callback dialog copy
  - Safe backup failure callback dialog copy
  - Defensive backup failure dialog-service sanitization before TextBlock rendering
  - Regression coverage for unsafe callback plugin/error payloads
requires: []
affects: []
key_files: []
key_decisions: []
patterns_established: []
observability_surfaces: []
drill_down_paths: []
duration: 8 min
verification_result: passed
completed_at: 2026-05-01
blocker_discovered: false
---
# T12: 11-user-facing-diagnostics-boundaries 12

**# Phase 11 Plan 12: Callback Dialog Boundary Summary**

## What Happened

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
