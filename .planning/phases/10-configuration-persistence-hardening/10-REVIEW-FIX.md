---
phase: 10-configuration-persistence-hardening
fixed_at: 2026-04-30T18:58:40.7230086-07:00
review_path: .planning/phases/10-configuration-persistence-hardening/10-REVIEW.md
iteration: 1
findings_in_scope: 5
fixed: 5
skipped: 0
status: all_fixed
---

# Phase 10: Code Review Fix Report

**Fixed at:** 2026-04-30T18:58:40.7230086-07:00
**Source review:** .planning/phases/10-configuration-persistence-hardening/10-REVIEW.md
**Iteration:** 1

**Summary:**
- Findings in scope: 5
- Fixed: 5
- Skipped: 0

## Fixed Issues

### CR-01: BLOCKER - Watcher reloads are silently dropped if hashing races a writer lock

**Files modified:** `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs`, `AutoQAC.Tests/Services/Configuration/Fakes/FakeUserConfigFileStore.cs`, `AutoQAC.Tests/Services/Configuration/ConfigPersistenceCoordinatorTests.cs`
**Commit:** 5f5b9d2
**Applied fix:** Wrapped watcher hash computation in the typed watcher failure path, publishing `ReadFailed` failures/results and adding a regression that verifies subsequent watcher reloads still process.

### WR-01: WARNING - Settings Save failures are reported as cleaning-blocked failures

**Files modified:** `AutoQAC/ViewModels/SettingsViewModel.cs`, `AutoQAC.Tests/ViewModels/SettingsViewModelTests.cs`
**Commit:** 1596d2a
**Applied fix:** Added Settings Save-specific flush failure banner mapping and strengthened the test to reject “Cleaning was blocked” copy for explicit Settings saves.

### WR-02: WARNING - Public settings snapshot omits the new Backup settings

**Files modified:** `AutoQAC/Services/Configuration/ConfigurationService.cs`, `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`
**Commit:** 5c0f41e
**Applied fix:** Included `Backup.Enabled` and `Backup.MaxSessions` in `GetAllSettingsAsync` and added a regression test for the public snapshot.

### WR-03: WARNING - FileSystemWatcher internal errors never reach the persistence failure pipeline

**Files modified:** `AutoQAC/Services/Configuration/ConfigWatcherService.cs`, `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs`, `AutoQAC.Tests/Services/Configuration/ConfigPersistenceCoordinatorTests.cs`
**Commit:** 98c0446
**Applied fix:** Forwarded `FileSystemWatcher.Error` as `ConfigFileSignalKind.Error` and handled it as a safe watcher `ReadFailed` failure/result without attempting normal hash/read processing.

### WR-04: WARNING - Canceled writes can leave temp settings files behind

**Files modified:** `AutoQAC/Services/Configuration/UserConfigFileStore.cs`, `AutoQAC.Tests/Services/Configuration/UserConfigFileStoreTests.cs`
**Commit:** ea562a2
**Applied fix:** Moved temp file writing inside the cleanup-protected `try` block and added a cancellation regression asserting no `*.tmp` files remain.

## Skipped Issues

None.

## Verification

- `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~ConfigPersistenceCoordinatorTests&FullyQualifiedName~Watcher_HashFailure" --nologo` — passed.
- `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~SettingsViewModelTests&FullyQualifiedName~SaveAsync_FlushFailure" --nologo` — passed.
- `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~ConfigurationServiceTests&FullyQualifiedName~GetAllSettingsAsync_IncludesBackupSettings" --nologo` — passed.
- `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~ConfigPersistenceCoordinatorTests&(FullyQualifiedName~Watcher_ErrorSignal|FullyQualifiedName~ConfigWatcherService_ForwardsFileSystemWatcherErrors)" --nologo` — passed.
- `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~UserConfigFileStoreTests&FullyQualifiedName~WriteAsync_CanceledWrite" --nologo` — passed.
- `dotnet test "AutoQACSharp.slnx" --nologo` — passed (983 total tests).

---

_Fixed: 2026-04-30T18:58:40.7230086-07:00_
_Fixer: the agent (gsd-code-fixer)_
_Iteration: 1_
