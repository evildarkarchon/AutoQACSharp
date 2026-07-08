---
phase: 07-backup-restore-retention-safety
plan: 02
subsystem: backup
tags: [backup, restore, retention, cancellation, file-system, testing]

# Dependency graph
requires:
  - phase: 07-backup-restore-retention-safety
    provides: cancellable backup file copier and structured operation result contracts from Plan 07-01
provides:
  - structured async restore outcomes for single-plugin and session restores
  - atomic restore overwrite usage through IBackupFileCopier
  - structured async retention cleanup with current-session protection, valid-session eligibility, retry, warning, and cancellation outcomes
affects: [restore-ui, cleaning-orchestrator, progress-ui, backup-retention]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - injectable filesystem deletion seam for retention cleanup tests
    - concise failure reason mapping for restore and retention rows
    - TDD RED/GREEN commits for restore and retention service behavior

key-files:
  created:
    - AutoQAC/Services/Backup/IBackupSessionDeleter.cs
    - AutoQAC/Services/Backup/DirectoryBackupSessionDeleter.cs
  modified:
    - AutoQAC/Services/Backup/BackupService.cs
    - AutoQAC/Infrastructure/ServiceCollectionExtensions.cs
    - AutoQAC.Tests/Services/BackupServiceTests.cs

key-decisions:
  - "Keep legacy synchronous restore and cleanup APIs on direct synchronous filesystem paths while async callers use structured outcomes."
  - "Retention cleanup deletes only directories with readable backup session metadata and reports malformed directories as kept."
  - "Current session protection is applied before retention counting, so maxSessionCount controls non-current sessions only."

patterns-established:
  - "Restore failures are row results with approved labels rather than thrown exceptions for async callers."
  - "Retention deletion uses an injectable deleter, retries IOException/UnauthorizedAccessException once, and returns Warning if old sessions remain because deletion failed."
  - "Cancellation during retention cleanup returns Canceled with remaining directories represented as kept rows."

requirements-completed: [SAF-04, TEST-04, PERF-04]

# Metrics
duration: 8 min
completed: 2026-04-29
---

# Phase 07 Plan 02: Structured Restore and Retention Service Outcomes Summary

**Async backup restore and retention services now return recoverable complete, partial, failed, canceled, and warning outcomes with concise per-row reasons.**

## Performance

- **Duration:** 8 min
- **Started:** 2026-04-29T06:48:24Z
- **Completed:** 2026-04-29T06:56:24Z
- **Tasks:** 2
- **Files modified:** 5

## Accomplishments

- Implemented async restore behavior that continues after individual plugin failures, recreates target directories when possible, maps expected filesystem problems to concise row reasons, and preserves legacy exception-throwing sync restore methods.
- Updated `BackupService` constructor injection so async restore uses `IBackupFileCopier` directly without sync-over-async blocking.
- Added retention cleanup with valid-session filtering, current-session protection, newest non-current retention semantics, one retry after deletion lock failures, warning rows for undeleted sessions, and cancellation reporting.
- Added `IBackupSessionDeleter` and `DirectoryBackupSessionDeleter` as a deterministic seam for retention tests and registered it in DI.
- Expanded `BackupServiceTests` to cover restore partials, atomic cancellation preservation, target-folder failure mapping, retention current protection, newest retention, malformed directory skips, retry-delay cancellation, and pre-canceled cleanup reporting.

## Task Commits

Each task was committed atomically:

1. **Task 1 RED: Add failing restore service tests** - `e0bcc6f` (test)
2. **Task 1 RED: Add failing restore target-folder test** - `3c011d2` (test)
3. **Task 1 GREEN: Implement structured restore outcomes** - `1559ce2` (feat)
4. **Task 2 RED: Add failing retention cleanup tests** - `5d2824a` (test)
5. **Task 2 GREEN: Implement structured retention cleanup** - `4ed0e91` (feat)

**Plan metadata:** committed separately after state/roadmap updates.

## Files Created/Modified

- `AutoQAC/Services/Backup/IBackupSessionDeleter.cs` - New async deletion seam for retention cleanup.
- `AutoQAC/Services/Backup/DirectoryBackupSessionDeleter.cs` - Filesystem-backed deleter using recursive `Directory.Delete`.
- `AutoQAC/Services/Backup/BackupService.cs` - Async restore and retention implementations, constructor injection, restore failure mapping, retention retry/cancel helpers.
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` - Registers `IBackupSessionDeleter` with the concrete directory deleter.
- `AutoQAC.Tests/Services/BackupServiceTests.cs` - Restore and retention regression coverage for Plan 07-02 behavior.

## Decisions Made

- Kept sync compatibility methods direct and synchronous because Plan 07-03/07-04 migrate known callers; this avoids `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` deadlock risk.
- Used valid `session.json` metadata as the retention eligibility boundary so unrelated directories under the backup root are never deleted by cleanup.
- Treated `maxSessionCount <= 0` as keeping no non-current valid sessions while still protecting the current session.

## Deviations from Plan

None - plan executed as specified.

## Issues Encountered

- `.gitignore` contains a broad `Backup*/` pattern, so new source files under `AutoQAC/Services/Backup/` required explicit force staging with `git add -f`.
- The first retention test helper parsed timestamp directory names with `DateTime.Parse`, which rejected the `yyyy-MM-dd_HH-mm-ss` format. It was corrected to `DateTime.ParseExact` before the Task 2 GREEN commit.

## Known Stubs

None.

## Verification

- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupServiceTests` — passed (25 tests).

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Plan 07-03 can wire structured backup and retention outcomes into the sequential cleaning orchestrator using the async service APIs and retention warning/cancel result model established here.

## Self-Check: PASSED

- Created/modified files verified on disk: `IBackupSessionDeleter.cs`, `DirectoryBackupSessionDeleter.cs`, `BackupService.cs`, `ServiceCollectionExtensions.cs`, `BackupServiceTests.cs`, and this summary.
- Task commits verified in git history: `e0bcc6f`, `3c011d2`, `1559ce2`, `5d2824a`, and `4ed0e91`.

---
*Phase: 07-backup-restore-retention-safety*
*Completed: 2026-04-29*
