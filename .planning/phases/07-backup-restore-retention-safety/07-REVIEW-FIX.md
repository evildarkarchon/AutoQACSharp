---
phase: 07-backup-restore-retention-safety
fixed_at: 2026-04-29T01:13:32.3670975-07:00
review_path: .planning/phases/07-backup-restore-retention-safety/07-REVIEW.md
iteration: 1
findings_in_scope: 6
fixed: 6
skipped: 0
status: all_fixed
---

# Phase 07: Code Review Fix Report

**Fixed at:** 2026-04-29T01:13:32.3670975-07:00
**Source review:** .planning/phases/07-backup-restore-retention-safety/07-REVIEW.md
**Iteration:** 1

**Summary:**
- Findings in scope: 6
- Fixed: 6
- Skipped: 0

## Fixed Issues

### CR-01: [BLOCKER] Restore progress mutates UI-bound ViewModel state from the copy worker thread

**Files modified:** `AutoQAC/ViewModels/RestoreViewModel.cs`, `AutoQAC/Views/MainWindow.axaml.cs`, `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs`  
**Commit:** 71b9d33  
**Applied fix:** Injected `IUiDispatcher` into restore progress flow, updated manual construction, and added an asynchronous worker-thread progress test proving updates are posted before UI-bound state changes.

### WR-01: [WARNING] Source length is read outside the copier error-handling boundary

**Files modified:** `AutoQAC/Services/Backup/BackupFileCopier.cs`  
**Commit:** d4e3c03  
**Applied fix:** Moved output path and source length probing inside the structured copy error boundary and mapped disappearing source paths to structured failure results.

### WR-02: [WARNING] Backup session directory creation failures bypass structured backup results

**Files modified:** `AutoQAC/Services/Backup/BackupService.cs`, `AutoQAC.Tests/Services/BackupServiceTests.cs`  
**Commit:** 3e6e1f4  
**Applied fix:** Wrapped async backup session directory creation in targeted exception handling and added coverage that directory creation failures return `BackupCreateResult` without invoking the copier.

### WR-03: [WARNING] Timestamp-only session directory names can collide within one second

**Files modified:** `AutoQAC/Services/Backup/BackupService.cs`, `AutoQAC.Tests/Services/BackupServiceTests.cs`  
**Commit:** 9aa30f3  
**Applied fix:** Preserved timestamp directory names while adding numeric suffixes on collision, with coverage for distinct same-second session directory creation.

### WR-04: [WARNING] Retention cancellation can throw instead of returning a canceled cleanup result

**Files modified:** `AutoQAC/Services/Backup/BackupService.cs`  
**Commit:** 8a58349  
**Applied fix:** Caught expected retention cleanup cancellation inside `CleanupOldSessionsAsync`, added unreported directories as kept/canceled rows, and returned a structured canceled cleanup result.

### WR-05: [WARNING] Restore and backup byte units disagree for the same copy progress model

**Files modified:** `AutoQAC/ViewModels/BackupProgressTextFormatter.cs`, `AutoQAC/ViewModels/ProgressViewModel.cs`, `AutoQAC/ViewModels/RestoreViewModel.cs`, `AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs`  
**Commit:** 16dae93  
**Applied fix:** Added a shared decimal byte formatter for backup and restore progress text and updated progress ViewModel expectations to the shared convention.

---

_Fixed: 2026-04-29T01:13:32.3670975-07:00_  
_Fixer: the agent (gsd-code-fixer)_  
_Iteration: 1_
