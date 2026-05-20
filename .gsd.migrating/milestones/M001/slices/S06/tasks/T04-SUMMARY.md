---
id: T04
parent: S06
milestone: M001
provides:
  - ISingleInstanceGuard
  - TestProcessHelper
  - real-process-integration-tests
requires: []
affects: []
key_files: []
key_decisions: []
patterns_established: []
observability_surfaces: []
drill_down_paths: []
duration: 
verification_result: passed
completed_at: 
blocker_discovered: false
---
# T04: 05-process-stop-pid-safety 04

**# Phase 05 Plan 04: Single-Instance Guard and Real-Process Integration Tests Summary**

## What Happened

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
