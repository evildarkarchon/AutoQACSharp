---
phase: 11-user-facing-diagnostics-boundaries
plan: 05
subsystem: diagnostics
tags: [csharp, dotnet, logging, diagnostics, security, tdd]

# Dependency graph
requires:
  - phase: 11-user-facing-diagnostics-boundaries
    provides: Shared DiagnosticTextFormatter safe file/plugin identifiers from Plan 11-01
provides:
  - Safe structured process-start diagnostics with argument counts and process IDs instead of executable paths or argv payloads
  - Safe CleaningService QuickAutoClean launch diagnostics with mode, game, plugin filename, argument count, status, and reason
  - Safe startup xEdit configuration diagnostics and generic legacy migration warning copy
affects: [process-logs, cleaning-launch-logs, startup-diagnostics, migration-warning-copy]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Logger-capture tests for injectable process and cleaning log boundaries
    - Source guard test for private App.axaml.cs startup diagnostics
    - Structured safe log fields instead of command/path payload properties

key-files:
  created:
    - .planning/phases/11-user-facing-diagnostics-boundaries/11-05-SUMMARY.md
  modified:
    - AutoQAC/Services/Process/ProcessExecutionService.cs
    - AutoQAC/Services/Cleaning/CleaningService.cs
    - AutoQAC/App.axaml.cs
    - AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs
    - AutoQAC.Tests/Services/CleaningServiceTests.cs
    - AutoQAC.Tests/Integration/DependencyInjectionTests.cs

key-decisions:
  - "ProcessExecutionService emits only operation/status/reason/argumentCount/processId fields for launch diagnostics; executable paths and raw arguments remain out of structured log properties."
  - "CleaningService owns caller-side QuickAutoClean launch context logs while preserving XEditCommandBuilder and ProcessStartInfo launch values unchanged."
  - "App.axaml.cs startup diagnostics use DiagnosticTextFormatter.SafeFileIdentifier for xEdit configuration and source guards for private startup copy."

patterns-established:
  - "Process log boundary: log ArgumentCount and ProcessId, not FileName or Arguments."
  - "Cleaning launch boundary: log QuickAutoClean, LaunchMode, Game, Plugin filename, ArgumentCount, Status, and Reason around ExecuteAsync."
  - "Startup source guard: private startup code can be protected by tests that reject forbidden literal templates and warning interpolation."

requirements-completed: [SEC-01, SEC-02]

# Metrics
duration: 6 min
completed: 2026-05-01
---

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
