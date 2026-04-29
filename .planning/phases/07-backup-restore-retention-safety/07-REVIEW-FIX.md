---
phase: 07-backup-restore-retention-safety
fixed_at: 2026-04-29T11:55:01Z
review_path: .planning/phases/07-backup-restore-retention-safety/07-REVIEW.md
iteration: 1
findings_in_scope: 7
fixed: 4
skipped: 3
status: partial
---

# Phase 07: Code Review Fix Report

**Fixed at:** 2026-04-29T11:55:01Z
**Source review:** .planning/phases/07-backup-restore-retention-safety/07-REVIEW.md
**Iteration:** 1

**Summary:**
- Findings in scope: 7
- Fixed: 4
- Skipped: 3

## Fixed Issues

### WR-01: WARNING — `BackupService(ILoggingService)` convenience constructor bypasses the deleter seam

**Files modified:** `AutoQAC/Services/Backup/BackupService.cs`, `AutoQAC.Tests/Services/BackupServiceTests.cs`  
**Commit:** 916e87d  
**Applied fix:** Forwarded the convenience constructor with an explicit `sessionDeleter: null` named argument and added a regression test covering `DeleteSessionAsync` through the single-argument constructor.

### WR-02: WARNING — `DeleteSessionAsync` does not call `ct.ThrowIfCancellationRequested()` before validation

**Files modified:** `AutoQAC/Services/Backup/BackupService.cs`, `AutoQAC.Tests/Services/BackupServiceTests.cs`  
**Commit:** cc2d951  
**Applied fix:** Added an upfront cancellation check before validation/logging and added a regression test proving an already-canceled token throws before validation and warning logging.

### WR-03: WARNING — Stale XML doc reference in `BackupService.IsRestoreTargetInsideTrustedRoot`

**Files modified:** `AutoQAC/Services/Backup/BackupService.cs`  
**Commit:** b6b3211  
**Applied fix:** Rewrote the XML documentation to reference the service-layer `DeleteSessionAsync` containment consumer instead of the ViewModel.

### IN-02: INFO — `ToString("MMM d, yyyy h:mm tt")` is repeated three times in `RestoreViewModel`

**Files modified:** `AutoQAC/ViewModels/RestoreViewModel.cs`  
**Commit:** 5b37a44  
**Applied fix:** Replaced the two inline timestamp format strings with `FormatSessionTimestamp(...)` so restore and delete session copy share one formatter.

## Skipped Issues

### IN-01: INFO — `EnsureTrailingDirectorySeparator` exists in two places

**File:** `AutoQAC/Services/Backup/BackupPathContainment.cs:61-62`, `AutoQAC/Services/Backup/BackupService.cs:723-724`  
**Reason:** Skipped as requested; REVIEW.md says this should be tracked in a future plan with no immediate change.  
**Original issue:** The trailing-separator helper remains duplicated between backup session containment and trusted restore root containment.

### IN-03: INFO — `RestoreViewModel.DeleteSessionAsync` does not log the canonical out-of-root rejection in `Information` form

**File:** `AutoQAC/ViewModels/RestoreViewModel.cs:338-347`  
**Reason:** Skipped as requested; REVIEW.md marks this as an intentional design choice with no code change.  
**Original issue:** The ViewModel relies on the service-layer warning log and does not add a second information-level UI audit log.

### IN-04: INFO — `MainWindowValidationPanel_ShouldRenderValidationErrorMessage` is unrelated to the rest of the file

**File:** `AutoQAC.Tests/Views/ViewSubscriptionLifecycleTests.cs:8-17`  
**Reason:** Skipped as requested; REVIEW.md says no change is required and the suggestion is cosmetic.  
**Original issue:** A validation rendering test lives in a subscription lifecycle test class.

## Verification

- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "DeleteSessionAsync_SingleArgConstructor_UsesDirectoryBackupSessionDeleter"` — passed (1/1).
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "DeleteSessionAsync_AlreadyCanceledToken_ThrowsBeforeValidation"` — passed (1/1).
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~RestoreViewModelTests"` — passed (27/27).
- Initial `--no-restore` attempt for the first targeted test failed because the isolated worktree had no restored `project.assets.json`; reran with restore and it passed.

---

_Fixed: 2026-04-29T11:55:01Z_  
_Fixer: the agent (gsd-code-fixer)_  
_Iteration: 1_
