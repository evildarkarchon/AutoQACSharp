---
phase: 07-backup-restore-retention-safety
plan: 11
subsystem: backup
tags: [backup, restore, filesystem-safety, trusted-root, tdd, gap-closure]

# Dependency graph
requires:
  - phase: 07-backup-restore-retention-safety
    provides: restore metadata validation, atomic restore copying, and Phase 07 verification gap report from prior plans
provides:
  - trusted restore root parameter on backup restore contracts
  - normalized Data-folder containment validation before restore directory creation or copy
  - RestoreWindow trusted-root propagation and command fail-closed behavior
affects: [backup-service, restore-ui, phase-07-verification, restore-metadata-policy]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - string-level restore target containment using Path.GetFullPath plus trailing directory separator normalization
    - RestoreViewModel command gating on loaded trusted restore root
    - TDD RED/GREEN commits for restore-root containment gap closure

key-files:
  created:
    - .planning/phases/07-backup-restore-retention-safety/07-11-SUMMARY.md
  modified:
    - AutoQAC/Services/Backup/IBackupService.cs
    - AutoQAC/Services/Backup/BackupService.cs
    - AutoQAC/ViewModels/RestoreViewModel.cs
    - AutoQAC.Tests/Services/BackupServiceTests.cs
    - AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs

key-decisions:
  - "Restore services now require an explicit trusted restore root and fail closed when it is missing or invalid."
  - "Restore target containment is string-level Path.GetFullPath validation and intentionally does not resolve NTFS reparse points or symlinks."
  - "RestoreWindow disables Restore Selected and Restore All until LoadSessionsAsync receives the configured game Data folder."

patterns-established:
  - "Trusted root containment is checked before Directory.CreateDirectory or IBackupFileCopier.CopyAsync."
  - "Restore UI passes Configuration.GameDataFolder through LoadSessionsAsync into restore service calls."
  - "Sibling-prefix paths such as Data2 are rejected by comparing against a trailing-separator-normalized root."

requirements-completed: [SAF-04, TEST-04, PERF-04]

# Metrics
duration: 8 min
completed: 2026-04-29
---

# Phase 07 Plan 11: Trusted Restore Root Containment Summary

**Restore overwrites are now constrained to the configured game Data folder, with fail-closed service contracts and disabled restore commands when no trusted root is loaded.**

## Performance

- **Duration:** 8 min
- **Started:** 2026-04-29T09:55:36Z
- **Completed:** 2026-04-29T10:03:12Z
- **Tasks:** 3
- **Files modified:** 5

## Accomplishments

- Added RED regression coverage for same-name out-of-root restore targets, Data/Data2 sibling-prefix attacks, and null/empty trusted-root fail-closed behavior.
- Updated every restore service contract and call site to include `trustedRestoreRoot` before progress/cancellation parameters.
- Added `IsRestoreTargetInsideTrustedRoot` to enforce normalized trusted-root containment before target directory creation or copy.
- Stored the configured game Data folder in `RestoreViewModel`, passed it into Restore Selected/Restore All, and disabled restore commands when the trusted root is missing.
- Verified targeted BackupService/RestoreViewModel/BackupOperationResult tests, full solution tests, and the sequential xEdit source invariant.

## Task Commits

Each implementation task was committed atomically:

1. **Task 1 RED: Inventory callers and prove trusted-root behavior** - `3bcd44a` (test)
2. **Task 2 GREEN: Enforce trusted restore root before filesystem writes** - `200a327` (feat)
3. **Task 3: Verify Phase 7 gap closure invariants** - no code commit; verification-only task left the worktree clean.

**Plan metadata:** committed separately after state/roadmap updates.

## Files Created/Modified

- `AutoQAC/Services/Backup/IBackupService.cs` - Adds `string? trustedRestoreRoot` to sync and async restore contracts.
- `AutoQAC/Services/Backup/BackupService.cs` - Validates restore targets stay under the trusted root before directory creation/copy, including sibling-prefix rejection.
- `AutoQAC/ViewModels/RestoreViewModel.cs` - Stores `_trustedRestoreRoot`, gates restore command predicates, and passes the root to restore service calls.
- `AutoQAC.Tests/Services/BackupServiceTests.cs` - Adds trusted-root containment regressions and updates restore tests for explicit roots.
- `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs` - Adds Data-folder propagation tests and missing-root command disablement coverage.

## Restore Caller Inventory

Task 1 audited restore call sites with:

`Select-String -Path AutoQAC/**/*.cs,AutoQAC.Tests/**/*.cs -Pattern 'RestorePluginAsync\(|RestoreSessionAsync\(|RestorePlugin\(|RestoreSession\('`

| Call-site category | Files | Trusted root supplied |
|--------------------|-------|-----------------------|
| RestoreWindow selected/all runtime calls | `AutoQAC/ViewModels/RestoreViewModel.cs`; launched from `AutoQAC/Views/MainWindow.axaml.cs` via `LoadSessionsAsync(dataFolderPath)` | `_trustedRestoreRoot`, populated from current `Configuration.GameDataFolder` |
| Backup service sync compatibility tests | `AutoQAC.Tests/Services/BackupServiceTests.cs` | Parent directory/root containing the intended target, or explicit unsafe root for rejection cases |
| Backup service async restore/session tests | `AutoQAC.Tests/Services/BackupServiceTests.cs` | Parent Data/target directory for safe tests; `_testRoot`, `Data`, or null/empty roots for unsafe/fail-closed tests |
| Restore ViewModel substitute setups/assertions | `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs` | `Arg.Any<string?>()` for existing behavior tests; exact `dataFolderPath` for propagation regressions |

## Decisions Made

- Retention cleanup remains outside the `trustedRestoreRoot` policy because it deletes backup session directories under the backup root, not plugin targets under the game Data folder.
- Ghosted plugin extensions remain rejected by the existing active-plugin extension allow-list (`.esm`, `.esp`, `.esl`).
- UNC/device/out-of-root/non-plugin metadata rejection remains intentional for Phase 7 safety and compatibility impact is accepted.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- Task 1 RED failed at compile time, as expected, because the tests used the new trusted-root signatures before production contracts were updated.
- `.gitignore` contains a broad `Backup*/` pattern, so tracked files under `AutoQAC/Services/Backup/` required explicit staging handling; no untracked generated files remained.

## Known Stubs

None. Stub scan matches were nullable state resets or test data, not UI/data-source placeholders.

## Threat Flags

None - no new network endpoints, auth paths, schema boundaries, or additional filesystem trust boundaries were introduced. This plan mitigated the existing backup metadata-to-filesystem overwrite trust boundary.

## TDD Gate Compliance

- RED gate present: `3bcd44a` (`test(07-11): add failing trusted restore root tests`).
- GREEN gate present after RED: `200a327` (`feat(07-11): enforce trusted restore root`).
- REFACTOR gate not needed; no behavior-neutral cleanup commit was produced.

## Verification

- RED: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~RestorePluginAsync_OriginalPathOutsideTrustedRoot_ReturnsTargetFolderCreationFailedAndDoesNotCopy|FullyQualifiedName~RestoreViewModelTests"` failed before production changes due to missing trusted-root signatures.
- Build after GREEN: `dotnet build AutoQACSharp.slnx` — passed with 0 warnings and 0 errors.
- Targeted restore safety: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~BackupServiceTests|FullyQualifiedName~RestoreViewModelTests|FullyQualifiedName~BackupOperationResultTests"` — passed (73 tests).
- Full solution: `dotnet test AutoQACSharp.slnx` — passed (752 AutoQAC.Tests, 59 QueryPlugins.Tests).
- Sequential xEdit invariant: `Select-String -Path AutoQAC/Services/Cleaning/*.cs -Pattern 'Task\.WhenAll|Parallel\.ForEachAsync|Task\.Run'` — no matches.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Phase 07 backup/restore/retention safety gap closure is complete and ready for phase verification or milestone completion. Remaining documented limitations are string-level restore-root containment (not reparse-point/symlink aware), intentional active-plugin extension rejection, and future hardening outside this trusted-root plan.

## Self-Check: PASSED

- Modified files verified on disk: `IBackupService.cs`, `BackupService.cs`, `RestoreViewModel.cs`, `BackupServiceTests.cs`, `RestoreViewModelTests.cs`, and this summary.
- Task commits verified in git history: `3bcd44a` and `200a327`.

---
*Phase: 07-backup-restore-retention-safety*
*Completed: 2026-04-29*
