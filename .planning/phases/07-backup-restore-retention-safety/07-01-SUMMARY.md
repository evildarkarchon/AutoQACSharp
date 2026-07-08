---
phase: 07-backup-restore-retention-safety
plan: 01
subsystem: backup
tags: [backup, restore, retention, file-copy, cancellation, progress, testing]

# Dependency graph
requires:
  - phase: 06-command-launch-escaping
    provides: concise user-facing error boundaries and safe process-launch behavior to preserve
provides:
  - cancellable async backup file copier with byte progress and safe partial-output cleanup
  - backup, restore, and retention result contracts for downstream Phase 7 plans
  - async IBackupService contract surface for backup, restore, and retention operations
affects: [backup-service, restore-ui, cleaning-orchestrator, progress-ui, retention-cleanup]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - managed FileStream copy with explicit cancellation cleanup
    - atomic restore replacement via temporary file then File.Move overwrite
    - enum-backed concise failure reasons for UI-safe rows

key-files:
  created:
    - AutoQAC/Services/Backup/IBackupFileCopier.cs
    - AutoQAC/Services/Backup/BackupFileCopier.cs
    - AutoQAC/Services/Backup/BackupCopyOptions.cs
    - AutoQAC/Models/BackupOperationResults.cs
    - AutoQAC.Tests/Services/BackupFileCopierTests.cs
    - AutoQAC.Tests/Models/BackupOperationResultTests.cs
  modified:
    - AutoQAC/Infrastructure/ServiceCollectionExtensions.cs
    - AutoQAC/Services/Backup/IBackupService.cs
    - AutoQAC/Services/Backup/BackupService.cs

key-decisions:
  - "Use managed FileStream copy behind IBackupFileCopier so later plans can swap internals without changing service/UI contracts."
  - "Use ReplaceAtomically restore semantics to preserve existing target files until the temporary copy fully succeeds."
  - "Keep SourceMissing as an operation-neutral reason and map it to Missing backup file only in restore contexts."

patterns-established:
  - "Backup copy cancellation deletes only the active output path: destination for create-new backups, temp file for atomic restore."
  - "User-facing backup/restore/retention rows use BackupFailureReason display labels instead of raw exception messages."
  - "TDD gate commits are split into RED test commits and GREEN feature commits for each task."

requirements-completed: [SAF-04, TEST-04, PERF-04]

# Metrics
duration: 6 min
completed: 2026-04-29
---

# Phase 07 Plan 01: Backup Copy Contracts and Cancellable File-Copy Foundation Summary

**Cancellable managed backup copying with atomic restore replacement, byte progress, and structured backup/restore/retention result contracts.**

## Performance

- **Duration:** 6 min
- **Started:** 2026-04-29T06:40:58Z
- **Completed:** 2026-04-29T06:46:54Z
- **Tasks:** 2
- **Files modified:** 9

## Accomplishments

- Added `IBackupFileCopier` and `BackupFileCopier` with async `FileStream` copying, cancellation-token checks inside the copy loop, progress throttling, and safe cleanup of partial outputs.
- Added `BackupCopyOptions` and `BackupCopyExistingTargetPolicy` so callers choose create-new backup behavior or atomic restore replacement explicitly.
- Added shared backup operation contracts covering complete, partial, failed, canceled, and warning aggregate outcomes plus restore/retention row statuses and count helpers.
- Extended `IBackupService` with async structured backup, restore, restore-session, and retention signatures while preserving legacy synchronous methods for current callers.
- Added targeted xUnit coverage for cancellation cleanup, restore-target preservation, progress throttling, concise labels, aggregate counts, and async service-contract presence.

## Task Commits

Each task was committed atomically:

1. **Task 1 RED: Add failing copier tests** - `67c1fcc` (test)
2. **Task 1 GREEN: Implement cancellable backup file copier** - `505fa9f` (feat)
3. **Task 2 RED: Add failing result contract tests** - `e516cd4` (test)
4. **Task 2 GREEN: Define backup operation result contracts** - `f7075ec` (feat)

**Plan metadata:** committed separately after state/roadmap updates.

## Files Created/Modified

- `AutoQAC/Services/Backup/IBackupFileCopier.cs` - New injectable async copy service boundary.
- `AutoQAC/Services/Backup/BackupFileCopier.cs` - Managed copy implementation with throttled byte progress and safe cancellation/failure cleanup.
- `AutoQAC/Services/Backup/BackupCopyOptions.cs` - Destination overwrite policy options for create-new backup and atomic restore flows.
- `AutoQAC/Models/BackupOperationResults.cs` - Aggregate statuses, failure reasons/display labels, copy/create/restore/retention result records, and computed counts.
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` - Registers `IBackupFileCopier` as a singleton.
- `AutoQAC/Services/Backup/IBackupService.cs` - Adds async structured backup/restore/retention contracts.
- `AutoQAC/Services/Backup/BackupService.cs` - Adds compatibility async implementations and maps restore source-missing to `Missing backup file`.
- `AutoQAC.Tests/Services/BackupFileCopierTests.cs` - Tests cancellation cleanup, target preservation, progress reporting, and source-missing neutrality.
- `AutoQAC.Tests/Models/BackupOperationResultTests.cs` - Tests concise labels, aggregate counts, create result display reason, and async service methods.

## Decisions Made

- Used managed async stream copy first, matching research guidance for testability while keeping `IBackupFileCopier` swappable for a future `CopyFileEx` implementation if needed.
- Chose explicit `BackupCopyExistingTargetPolicy` values rather than a boolean overwrite flag so backup creation and restore replacement semantics are hard to mix up.
- Kept neutral `SourceMissing` out of direct UI labels; restore code maps it to `Missing backup file`, while backup callers can map it to source-plugin-specific text later.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Introduced shared copy result contracts during Task 1**
- **Found during:** Task 1 (Add cancellable copy foundation)
- **Issue:** Task 1's public `IBackupFileCopier.CopyAsync` signature returns `BackupCopyResult` and reports `BackupCopyProgress`, but the plan scheduled the shared result-contract file for Task 2. The Task 1 GREEN implementation could not compile without those types.
- **Fix:** Added the copy-related subset of `BackupOperationResults.cs` with `BackupOperationStatus`, `BackupFailureReason`, `BackupCopyProgress`, and `BackupCopyResult`, then expanded the same file during Task 2.
- **Files modified:** `AutoQAC/Models/BackupOperationResults.cs`
- **Verification:** `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupFileCopierTests`
- **Committed in:** `505fa9f`

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** No scope creep; the earlier file creation was required to satisfy Task 1's planned public API and was completed by Task 2.

## Issues Encountered

- `.gitignore` contains a broad `Backup*/` pattern, so new source files under `AutoQAC/Services/Backup/` required explicit force staging. Files were staged individually with `git add -f` and committed normally with hooks.

## Known Stubs

| File | Line | Reason |
|------|------|--------|
| `AutoQAC/Services/Backup/BackupService.cs` | `CleanupOldSessionsAsync` | Compatibility async contract delegates to existing cleanup behavior and returns an empty structured result until Plan 07-02 implements full retention row outcomes, retries, and cancellation semantics. |

## Verification

- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupFileCopierTests` — passed (4 tests).
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~BackupOperationResultTests` — passed (5 tests).
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~BackupFileCopierTests|FullyQualifiedName~BackupOperationResultTests"` — passed (9 tests).
- `dotnet test AutoQACSharp.slnx` — passed (744 tests: 685 AutoQAC.Tests, 59 QueryPlugins.Tests).

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Plan 07-02 can now implement structured async restore and retention service outcomes using the established result records, copy semantics, and progress contracts.

## Self-Check: PASSED

- Created files verified on disk: `IBackupFileCopier.cs`, `BackupFileCopier.cs`, `BackupCopyOptions.cs`, `BackupOperationResults.cs`, `BackupFileCopierTests.cs`, and `BackupOperationResultTests.cs`.
- Task commits verified in git history: `67c1fcc`, `505fa9f`, `e516cd4`, and `f7075ec`.

---
*Phase: 07-backup-restore-retention-safety*
*Completed: 2026-04-29*
