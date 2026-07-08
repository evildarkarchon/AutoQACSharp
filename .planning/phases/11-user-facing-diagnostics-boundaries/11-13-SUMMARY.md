---
phase: 11-user-facing-diagnostics-boundaries
plan: 13
subsystem: diagnostics-security
tags: [csharp, dotnet, restore, diagnostics, security, tdd]

requires:
  - phase: 11-user-facing-diagnostics-boundaries
    provides: Safe diagnostics formatter and completed Phase 11 Plans 11-01 through 11-12
provides:
  - Safe Restore Selected confirmation, status, and error display names
  - Regression coverage for unsafe backup metadata filenames
  - Proof that restore service calls still receive the original BackupPluginEntry
affects: [restore-view-model, backup-restore, sec-01, phase-11-verification]

tech-stack:
  added: []
  patterns:
    - TDD RED/GREEN regression closure for untrusted backup metadata display
    - Display projection separated from service restore semantics
    - Safe plugin display names for restore dialog/status copy

key-files:
  created:
    - .planning/phases/11-user-facing-diagnostics-boundaries/11-13-SUMMARY.md
  modified:
    - AutoQAC/ViewModels/RestoreViewModel.cs
    - AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs
    - .planning/phases/11-user-facing-diagnostics-boundaries/11-VERIFICATION.md
    - .planning/ROADMAP.md

key-decisions:
  - "BackupPluginEntry.FileName from session metadata is treated as untrusted display input."
  - "Restore Selected UI copy uses DiagnosticTextFormatter.SafePluginName, but IBackupService.RestorePluginAsync still receives the original BackupPluginEntry."
  - "Logger properties continue to use original metadata where local troubleshooting value is needed."

patterns-established:
  - "Restore UI display names are projections; they do not alter restore targets or backup containment semantics."
  - "Unsafe backup metadata filenames are tested with path, command, quote, backtick, and control-character payloads."

requirements-completed: [SEC-01]

duration: 5 min
completed: 2026-05-01
---

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
