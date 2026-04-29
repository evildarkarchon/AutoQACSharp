---
phase: 05-process-stop-pid-safety
plan: 03
subsystem: cleaning-stop-flow
tags: [orchestrator, viewmodel, mvvm, stop-flow, user-safety]
requires: [05-02]
provides: [StopCleaningResult, force-terminate-confirmation, unsafe-log-read-guard]
affects: [AutoQAC/Services/Cleaning, AutoQAC/ViewModels/MainWindow]
tech_stack:
  added: []
  patterns: [structured-result-snapshot, mvvm-dialog-service, conservative-log-parsing]
key_files:
  created:
    - AutoQAC/Services/Cleaning/StopCleaningResult.cs
  modified:
    - AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs
    - AutoQAC/Services/Cleaning/CleaningOrchestrator.cs
    - AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs
    - AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs
    - AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs
decisions:
  - ViewModel stop UI branches on returned `StopCleaningResult` snapshots, using `LastTerminationResult` only as compatibility fallback.
  - Declining force termination records `LeftRunningByUser` so log parsing remains blocked when xEdit may still be writing.
metrics:
  completed: 2026-04-29
  tasks: 2
  commits: [869749c]
---

# Phase 05 Plan 03: Orchestrator/ViewModel Stop Flow and User-Visible Outcomes Summary

Stop behavior now prompts before force termination, records decline/failure outcomes, and shows UI-SPEC copy through MVVM-safe dialog services.

## Completed Tasks

1. Added structured stop outcomes and unsafe-log-read guards for may-still-be-running termination states.
2. Updated stop command UI to show force termination confirmation, leave-running warning, and force-kill failure error copy.

## Verification

- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningOrchestrator|FullyQualifiedName~CleaningCommandsViewModel"` — passed.
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~MainWindowViewModel|FullyQualifiedName~ProgressViewModel|FullyQualifiedName~CleaningOrchestrator"` — passed.

## Deviations from Plan

None - plan executed as written.

## Known Stubs

None.

## Threat Flags

None beyond planned user-intent and process-outcome trust boundary mitigations.

## Self-Check: PASSED

- Created/modified files exist.
- Commit found: `869749c`.
