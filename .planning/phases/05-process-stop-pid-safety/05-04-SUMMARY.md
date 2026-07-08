---
phase: 05-process-stop-pid-safety
plan: 04
subsystem: process-safety-tests
tags: [single-instance, mutex, integration-tests, helper-process]
requires: [05-02, 05-03]
provides: [ISingleInstanceGuard, TestProcessHelper, real-process-integration-tests]
affects: [AutoQAC/App.axaml.cs, AutoQAC/Services/Process, AutoQAC.Tests]
tech_stack:
  added: []
  patterns: [named-mutex, bounded-helper-processes, finally-cleanup]
key_files:
  created:
    - AutoQAC/Services/Process/ISingleInstanceGuard.cs
    - AutoQAC/Services/Process/SingleInstanceGuard.cs
    - AutoQAC.Tests/Services/SingleInstanceGuardTests.cs
    - AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs
    - AutoQAC.Tests/TestProcessHelper/AutoQAC.TestProcessHelper.csproj
    - AutoQAC.Tests/TestProcessHelper/Program.cs
  modified:
    - AutoQAC/App.axaml.cs
    - AutoQAC/Infrastructure/ServiceCollectionExtensions.cs
    - AutoQAC/Services/Process/ProcessExecutionService.cs
    - AutoQAC.Tests/AutoQAC.Tests.csproj
    - AutoQACSharp.slnx
decisions:
  - Duplicate startup is blocked with the per-user `Local\AutoQAC` named mutex before watchers, migration, or retention work starts.
  - Real-process tests use a local helper executable instead of xEdit, cmd scripts, or PowerShell.
metrics:
  completed: 2026-04-29
  tasks: 2
  commits: [c0788ef, 274ce15]
---

# Phase 05 Plan 04: Single-Instance Guard and Real-Process Integration Tests Summary

Duplicate app instances are blocked with a named mutex, and controlled helper-process tests now verify timeout, graceful exit, process-tree kill, and PID cleanup behavior.

## Completed Tasks

1. Added `ISingleInstanceGuard`/`SingleInstanceGuard`, DI registration, startup duplicate handling, and guard tests.
2. Added `AutoQAC.TestProcessHelper` plus default-running integration tests for real child-process lifecycle safety.

## Verification

- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~SingleInstanceGuard"` — passed.
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ProcessExecutionIntegration"` — passed.
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~SingleInstanceGuard|FullyQualifiedName~ProcessExecutionIntegration"` — passed after rerunning sequentially; an earlier parallel test invocation hit a transient build-file lock.
- `dotnet test AutoQACSharp.slnx` — passed: QueryPlugins.Tests 59/59, AutoQAC.Tests 661/661.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Preserved redirected process streams in process start copy**
- **Found during:** Plan 04 integration testing
- **Issue:** `ProcessExecutionService` copied only a subset of `ProcessStartInfo`, so helper tests using stdin/stdout redirection could not exercise graceful signaling.
- **Fix:** Copied redirect and encoding properties into the internal `ProcessStartInfo`.
- **Files modified:** `AutoQAC/Services/Process/ProcessExecutionService.cs`
- **Commit:** `c0788ef`

**2. [Rule 3 - Blocking] Excluded helper source from test project compilation and copied helper DLL**
- **Found during:** Plan 04 integration testing
- **Issue:** The test SDK compiled helper global-code source into `AutoQAC.Tests`, and the copied helper apphost lacked its DLL at test runtime.
- **Fix:** Removed helper sources from test compilation and copied `AutoQAC.TestProcessHelper.dll` beside the helper executable.
- **Files modified:** `AutoQAC.Tests/AutoQAC.Tests.csproj`
- **Commit:** `274ce15`

## Known Stubs

None.

## Threat Flags

None beyond planned duplicate-process and test-helper process trust boundary mitigations.

## Self-Check: PASSED

- Created/modified files exist.
- Commits found: `c0788ef`, `274ce15`.
