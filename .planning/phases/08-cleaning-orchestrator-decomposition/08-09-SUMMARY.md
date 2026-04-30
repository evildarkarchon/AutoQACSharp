---
phase: 08-cleaning-orchestrator-decomposition
plan: 09
subsystem: cleaning-orchestrator
tags: [csharp, dotnet, tdd, concurrency, cancellation]
requires:
  - phase: 08-cleaning-orchestrator-decomposition
    provides: thin CleaningOrchestrator facade with startup cancellation fixes
provides:
  - Concurrent StartCleaningAsync rejection guard
  - Regression coverage for active CTS ownership during overlapping starts
affects: [cleaning-orchestrator, cancellation, session-state]
tech-stack:
  added: []
  patterns: [Interlocked active-session guard, TDD regression]
key-files:
  created: []
  modified:
    - AutoQAC/Services/Cleaning/CleaningOrchestrator.cs
    - AutoQAC.Tests/Services/CleaningOrchestratorTests.cs
key-decisions:
  - "Concurrent StartCleaningAsync calls fail fast with InvalidOperationException instead of waiting or replacing session state."
patterns-established:
  - "Use Interlocked.CompareExchange plus Volatile.Write for non-blocking facade session guards."
requirements-completed: [REF-01]
duration: 8min
completed: 2026-04-30
---

# Phase 08 Plan 09: Concurrent Start Guard Summary

**Fail-fast StartCleaningAsync guard preserving first-session CTS ownership under concurrent public calls**

## Performance

- **Duration:** ~8 min
- **Started:** 2026-04-30T03:30:00Z
- **Completed:** 2026-04-30T03:38:00Z
- **Tasks:** 3
- **Files modified:** 2

## Accomplishments

- Added a TDD RED regression proving an overlapping second `StartCleaningAsync` call was previously allowed.
- Added a non-blocking `_sessionActive` guard using `Interlocked.CompareExchange` and `Volatile.Write`.
- Verified the guard preserves Plan 08-07 startup stop behavior and keeps the orchestrator suite green.

## Task Commits

1. **Task 1: RED - add concurrent start rejection regression** - `46d618b` (test)
2. **Task 2: GREEN - guard active cleaning session before mutable session setup** - `39dff15` (feat)
3. **Task 3: Regression sweep and source guard** - no code changes; verification only

## Files Created/Modified

- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs` - Adds concurrent-start regression covering rejection, single StartCleaning publication, single xEdit launch, and first-token cancellation.
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` - Adds fail-fast active-session entry/exit helpers around the full cleaning workflow.

## Decisions Made

- Concurrent starts reject immediately instead of queuing, preserving sequential cleaning and active CTS ownership.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- The first RED draft could fail from the overwritten first CTS timing out inside the mock; the test was tightened so the RED failure is specifically "no exception was thrown" for the second start.

## User Setup Required

None - no external service configuration required.

## Known Stubs

None.

## Threat Flags

None.

## Verification

- `dotnet test AutoQACSharp.slnx --nologo --filter "FullyQualifiedName~StartCleaningAsync_WhenSessionAlreadyActive_ShouldRejectSecondStartAndKeepFirstSessionCancellable"` — failed during RED as expected, then passed after GREEN.
- `dotnet test AutoQACSharp.slnx --nologo --filter "FullyQualifiedName~StartCleaningAsync_WhenSessionAlreadyActive_ShouldRejectSecondStartAndKeepFirstSessionCancellable|FullyQualifiedName~StopCleaningAsync_DuringPreflight_CancelsSessionAndDoesNotInvokeCleaningService|FullyQualifiedName~StopCleaningAsync_DuringOrphanCleanup_CancelsSessionBeforePreflightAndDoesNotInvokeCleaningService"` — passed.
- `dotnet test AutoQACSharp.slnx --nologo --filter "FullyQualifiedName~CleaningOrchestratorTests"` — passed, 50 tests.
- Source guard found no `Task.WhenAll`, `Task.WhenAny`, `Task.Run`, `Parallel.ForEach`, or `Parallel.For` tokens in `CleaningOrchestrator.cs`.

## Next Phase Readiness

- Cleaning session entry is protected against overlapping public calls.
- Plan 08-10 can close the remaining preflight load-order validation gap.

## Self-Check: PASSED

- Verified modified files exist.
- Verified task commits `46d618b` and `39dff15` exist in git history.
- Verified regression commands passed after implementation.

---
*Phase: 08-cleaning-orchestrator-decomposition*
*Completed: 2026-04-30*
