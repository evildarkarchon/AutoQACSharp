---
id: T13
parent: S12
milestone: M001
provides:
  - Safe Restore Selected confirmation, status, and error display names
  - Regression coverage for unsafe backup metadata filenames
  - Proof that restore service calls still receive the original BackupPluginEntry
requires: []
affects: []
key_files: []
key_decisions: []
patterns_established: []
observability_surfaces: []
drill_down_paths: []
duration: 5 min
verification_result: passed
completed_at: 2026-05-01
blocker_discovered: false
---
# T13: 11-user-facing-diagnostics-boundaries 13

**# Phase 11 Plan 13: Restore Metadata Display Boundary Summary**

## What Happened

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
