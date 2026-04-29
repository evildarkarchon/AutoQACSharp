---
phase: 07-backup-restore-retention-safety
plan: 14
subsystem: backup
tags: [backup, restore, filesystem-safety, helper-extraction, refactor, tdd, gap-closure]

# Dependency graph
requires:
  - phase: 07-backup-restore-retention-safety
    provides: trusted restore root parameter and Path.GetFullPath-based containment validation from Plan 07-11
provides:
  - shared internal BackupPathContainment.IsContained(string?, string?) helper as the single source of truth for backup/restore/delete path-containment policy
  - BackupService.IsRestoreTargetInsideTrustedRoot delegating to the shared helper instead of duplicating normalization, trailing-separator, StartsWith, and try/catch logic
  - InternalsVisibleTo("AutoQAC.Tests") so the helper is exercised directly in unit tests without changing public API surface
affects: [backup-service, restore-policy, plan-07-13-prerequisite]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - shared internal static helper for canonical containment policy across multiple safety boundaries
    - InternalsVisibleTo via MSBuild ItemGroup to keep helpers internal while still unit-testable
    - TDD RED/GREEN gates committed atomically with deliberate compile-time RED on a missing symbol

key-files:
  created:
    - AutoQAC/Services/Backup/BackupPathContainment.cs
    - AutoQAC.Tests/Services/Backup/BackupPathContainmentTests.cs
    - .planning/phases/07-backup-restore-retention-safety/07-14-SUMMARY.md
  modified:
    - AutoQAC/Services/Backup/BackupService.cs
    - AutoQAC/AutoQAC.csproj

key-decisions:
  - "BackupPathContainment.IsContained is the single shared owner of string-level containment policy for backup/restore/delete safety boundaries; future changes to containment semantics happen in one file."
  - "Helper visibility is internal with explicit InternalsVisibleTo(AutoQAC.Tests) instead of public; ViewModel and service consumers stay inside AutoQAC and tests still cover the helper directly."
  - "EnsureTrailingDirectorySeparator remains in BackupService.cs because ValidateBackupDestination (line 503) and ValidateRestoreEntry (line 580) still call it; only the IsRestoreTargetInsideTrustedRoot duplication is collapsed in this plan."
  - "Plan 07-13 will consume BackupPathContainment.IsContained from RestoreViewModel.DeleteSessionAsync rather than reintroducing duplicate containment logic in the ViewModel layer."

patterns-established:
  - "Containment policy lives in BackupPathContainment.IsContained; service-level wrappers delegate via a single expression-bodied method."
  - "Internal helpers declared in AutoQAC.csproj with InternalsVisibleTo are unit-tested by AutoQAC.Tests without exposing them on the public API."

requirements-completed: [SAF-04, TEST-04]

# Metrics
duration: 4 min
completed: 2026-04-29
---

# Phase 07 Plan 14: BackupPathContainment Helper Extraction Summary

**Restore-target containment is now centralized in a single shared `BackupPathContainment.IsContained` helper, eliminating the duplicated normalize+trailing-separator+StartsWith pattern that the cross-AI review flagged as HIGH-priority duplication and Gemini flagged as a consistent-normalization risk.**

## Performance

- **Duration:** ~4 min
- **Started:** 2026-04-29T11:11:09Z
- **Completed:** 2026-04-29T11:15:18Z
- **Tasks:** 3 (RED + GREEN + REFACTOR-verification)
- **Files modified:** 5 (2 created, 2 modified, 1 SUMMARY)

## Accomplishments

- Added 11 RED test cases in `BackupPathContainmentTests` that lock the canonical containment policy: null/empty/whitespace candidate (3), null/empty/whitespace root (3), valid containment, traversal-normalized escape, sibling-prefix collision, case-insensitive containment, malformed-path exception swallowing.
- Created `AutoQAC/Services/Backup/BackupPathContainment.cs` with the shared `IsContained(string? candidatePath, string? rootPath)` helper as `internal static`, encapsulating `Path.GetFullPath`, `EnsureTrailingDirectorySeparator`, `StringComparison.OrdinalIgnoreCase` `StartsWith`, and the four-exception fail-closed catch filter.
- Refactored `BackupService.IsRestoreTargetInsideTrustedRoot` (lines 654-671 pre-refactor) into a single expression-bodied delegation to `BackupPathContainment.IsContained`.
- Added `<InternalsVisibleTo Include="AutoQAC.Tests" />` to `AutoQAC.csproj` so the new helper can be exercised directly without changing its public API surface.
- Verified all 11 new helper tests pass; all 48 existing `BackupServiceTests` (including Plan 07-11's `RestorePluginAsync_SiblingPrefixTrustedRoot_*`, `RestorePluginAsync_MissingTrustedRestoreRoot_*`, and the Data2 sibling-prefix regression) continue to pass.
- Verified the Phase 7 targeted test cluster (155 tests) and the full solution suite (764 AutoQAC.Tests + 59 QueryPlugins.Tests) all pass.
- Verified `AutoQAC/Services/Cleaning` contains zero `Task.WhenAll | Parallel.ForEachAsync | Task.Run` matches — sequential xEdit invariant preserved.

## Task Commits

Each task was committed atomically using TDD RED/GREEN gates:

1. **Task 1 RED: failing BackupPathContainment helper tests** — `4419990` (test)
2. **Task 2 GREEN: extract shared BackupPathContainment helper** — `a3f285d` (feat)
3. **Task 3 REFACTOR: verification only** — no code commit; verification confirmed helper extraction is complete and the duplication pattern is gone.

**Plan metadata:** committed separately after state/roadmap updates.

## Files Created/Modified

- `AutoQAC/Services/Backup/BackupPathContainment.cs` (created) — Shared internal static helper class with `IsContained(string?, string?)` returning `bool`.
- `AutoQAC.Tests/Services/Backup/BackupPathContainmentTests.cs` (created) — 11 RED-then-GREEN test cases locking the canonical containment policy.
- `AutoQAC/Services/Backup/BackupService.cs` (modified) — `IsRestoreTargetInsideTrustedRoot` collapsed to a one-line expression-bodied delegation.
- `AutoQAC/AutoQAC.csproj` (modified) — Added `InternalsVisibleTo("AutoQAC.Tests")` MSBuild ItemGroup.
- `.planning/phases/07-backup-restore-retention-safety/07-14-SUMMARY.md` (created) — This summary.

## Grep Evidence (single-source-of-truth verification)

- `BackupService.cs` zero matches for the restore-root inline pattern:
  ```
  rg -n 'Path\.GetFullPath\([^)]*\)\s*\.StartsWith' AutoQAC/Services/Backup/BackupService.cs  # 0 matches
  ```
- `BackupService.IsRestoreTargetInsideTrustedRoot` exactly one delegation call:
  ```
  rg -n 'BackupPathContainment\.IsContained' AutoQAC/Services/Backup/BackupService.cs
  649:    /// Delegates to <see cref="BackupPathContainment.IsContained"/> ...
  658:        BackupPathContainment.IsContained(targetPath, trustedRestoreRoot);
  ```
- `BackupPathContainmentTests` declares 11 IsContained_* test methods (one per RED case).
- `AutoQAC/Services/Cleaning` zero matches for parallel-xEdit constructs:
  ```
  rg -n 'Task\.WhenAll|Parallel\.ForEachAsync|Task\.Run' AutoQAC/Services/Cleaning/*.cs  # 0 matches
  ```

## Decisions Made

- The helper is `internal static` rather than `public static`. The two known consumers — `BackupService` and (in Plan 07-13) `RestoreViewModel` — both live in the `AutoQAC` assembly, so internal visibility is sufficient and keeps the policy off the public API surface.
- `EnsureTrailingDirectorySeparator` was kept in both `BackupService.cs` and `BackupPathContainment.cs` because two unrelated session-root containment call sites in `BackupService` (`ValidateBackupDestination` line 503, `ValidateRestoreEntry` line 580) still rely on the local copy. Those call sites are out of scope for Plan 07-14 and validate session-relative file names against the *backup session root*, not the trusted restore root.
- Trusted-restore-root residual risk from Plan 07-11 is unchanged: `IsContained` does not resolve NTFS reparse points or symlinks. This is documented on the helper's `<remarks>` block.

## Deviations from Plan

None — plan executed exactly as written. The two remaining `StartsWith` call sites at `BackupService.cs` lines 505 and 582 are inside `ValidateBackupDestination` and `ValidateRestoreEntry` respectively; they validate *session-root* containment (Plan 07-09 scope), not trusted-restore-root containment, and are explicitly out of scope for 07-14's restore-target duplication closure. They are a candidate for a future plan that consolidates *all* containment scopes (session-root + trusted-restore-root) behind `BackupPathContainment.IsContained`.

## TDD Gate Compliance

- RED gate present: `4419990` (`test(07-14): add failing BackupPathContainment helper tests`). Verified failing build with 11 `CS0103: The name 'BackupPathContainment' does not exist in the current context` errors before Task 2 production changes.
- GREEN gate present after RED: `a3f285d` (`feat(07-14): extract shared BackupPathContainment helper`). All 11 new helper tests + 48 existing `BackupServiceTests` pass.
- REFACTOR gate intentionally produced no code commit because Task 3 is verification-only; no behavior-neutral cleanup was needed and the GREEN code is already in its final shape.

## Issues Encountered

- The `.gitignore` `Backup*/` pattern from earlier Phase 7 work hides both `AutoQAC/Services/Backup/` and `AutoQAC.Tests/Services/Backup/`. All staging used `git add -f` for files under those paths. Plan 07-11's summary called out the same workaround. No untracked generated files remained.
- Initial RED build failed with the expected 11 `CS0103` errors confirming the helper symbol does not exist before Task 2 — exactly the desired RED state.

## Known Stubs

None.

## Threat Flags

None — no new network endpoints, auth paths, schema boundaries, or additional filesystem trust boundaries were introduced. This plan consolidates an existing trust boundary (the restore-target containment check) into a single helper file, mitigating threats T-07-14-01 through T-07-14-04 from the plan's threat register.

## Verification

- RED build: `dotnet build AutoQAC.Tests/AutoQAC.Tests.csproj` — 11 `CS0103` errors before Task 2 (expected).
- Full build after GREEN: `dotnet build AutoQACSharp.slnx` — 0 warnings, 0 errors.
- GREEN targeted: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --no-build --filter "FullyQualifiedName~BackupPathContainmentTests|FullyQualifiedName~BackupServiceTests"` — passed (59 tests: 11 new helper + 48 existing service).
- Phase 7 targeted cluster: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --no-build --filter "FullyQualifiedName~BackupFileCopierTests|FullyQualifiedName~BackupServiceTests|FullyQualifiedName~BackupPathContainmentTests|FullyQualifiedName~RestoreViewModelTests|FullyQualifiedName~CleaningOrchestratorTests|FullyQualifiedName~ProgressViewModelTests|FullyQualifiedName~ViewSubscriptionLifecycleTests"` — passed (155 tests).
- Full solution: `dotnet test AutoQACSharp.slnx --no-build` — passed (764 AutoQAC.Tests + 59 QueryPlugins.Tests).
- Sequential xEdit invariant: `rg -n 'Task\.WhenAll|Parallel\.ForEachAsync|Task\.Run' AutoQAC/Services/Cleaning/*.cs` — no matches.
- Containment-pattern duplication scan: `rg -nE 'Path\.GetFullPath\([^)]*\)\s*\.StartsWith' AutoQAC/Services/Backup/BackupService.cs` — no matches.

## User Setup Required

None.

## Next Phase Readiness

Plan 07-13 (next executor in this wave) can consume `BackupPathContainment.IsContained` directly from `RestoreViewModel.DeleteSessionAsync` without reintroducing duplicate containment logic in the ViewModel layer. The cross-AI review consensus HIGH on duplication (the agent + Codex) and Gemini's consistent-normalization finding are now closed. Remaining documented limitations are the unchanged Plan 07-11 string-level scope (no reparse-point/symlink resolution) and the deferred consolidation of session-root containment in `ValidateBackupDestination` / `ValidateRestoreEntry`.

## Self-Check: PASSED

- Created files verified on disk: `AutoQAC/Services/Backup/BackupPathContainment.cs`, `AutoQAC.Tests/Services/Backup/BackupPathContainmentTests.cs`, this summary.
- Modified files verified on disk: `AutoQAC/Services/Backup/BackupService.cs`, `AutoQAC/AutoQAC.csproj`.
- Task commits verified in git history: `4419990` (RED test), `a3f285d` (GREEN feat).
- Build clean, full solution tests green.

---
*Phase: 07-backup-restore-retention-safety*
*Completed: 2026-04-29*
