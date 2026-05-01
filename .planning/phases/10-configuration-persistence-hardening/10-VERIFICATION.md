---
phase: 10-configuration-persistence-hardening
verified: 2026-05-01T00:00:00Z
status: gaps_found
score: 54/55 must-haves verified
overrides_applied: 0
re_verification:
  previous_status: gaps_found
  previous_score: 51/52
  gaps_closed:
    - "Explicit reload no longer masks a failed prerequisite pending-save flush; it returns the failed/rejected flush result before reading disk."
    - "ConfigurationService.FlushPendingSavesAsync no longer bypasses the coordinator barrier when the facade has no pending app save; it always awaits the coordinator flush barrier."
  gaps_remaining:
    - "Watcher reloads can still be silently dropped when ComputeHashAsync throws while an external writer holds/locks the settings file."
  regressions: []
gaps:
  - truth: "User configuration changes save and reload deterministically when app saves and external edits occur in close timing windows."
    status: failed
    reason: "The code review CR-01 finding is true in the current codebase: ApplyWatcherAsync computes the current file hash before entering the reload read/failure path. If ComputeHashAsync throws during a normal FileSystemWatcher race with an external writer lock, the operation-loop catch only logs and drops the watcher operation; no ReadFailed failure/result is published and no retry is scheduled."
    artifacts:
      - path: "AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs"
        issue: "Lines 288-290 call _fileStore.ComputeHashAsync before ReadForReloadAsync; exceptions escape to RunAsync lines 217-220, which log only and drop the operation."
      - path: "AutoQAC/Services/Configuration/UserConfigFileStore.cs"
        issue: "Lines 106-115 read the settings file directly for hash computation and can throw IOException/UnauthorizedAccessException while another process is writing."
      - path: "AutoQAC.Tests/Services/Configuration/ConfigPersistenceCoordinatorTests.cs"
        issue: "No regression covers ComputeHashAsync throwing for watcher signals; FakeUserConfigFileStore has no hash-failure knob."
    missing:
      - "Catch hash/read exceptions inside ApplyWatcherAsync (or route hashing through ReadForReloadAsync) and publish a typed Watcher/ReadFailed result/failure instead of dropping the operation."
      - "Add a deterministic coordinator test where ComputeHashAsync throws once and assert a ReadFailed failure/result is emitted and the coordinator remains able to process a later valid watcher reload."
---

# Phase 10: Configuration Persistence Hardening Verification Report

**Phase Goal:** Users get reliable configuration saves/reloads under race conditions, and maintainers can reason about persistence through one serialized flow with lower in-memory clone cost.  
**Verified:** 2026-05-01T00:00:00Z  
**Status:** gaps_found  
**Re-verification:** Yes — after Plan 10-09 gap closure

## Goal Achievement

Plan 10-09 closes the prior verifier gap: `ConfigurationService.FlushPendingSavesAsync` now always awaits `_coordinator.FlushPendingSavesAsync(ct)`, including no-pending facade calls, so forced flush is again a public coordinator barrier.

However, the phase goal is still not achieved. The required `10-REVIEW.md` critical finding CR-01 remains present in the codebase. Watcher hash computation is outside the typed reload failure path, so a close-timing external edit can be dropped under a file-lock race with only a log entry and no recoverable failure or retry. That violates the core Phase 10 outcome: reliable save/reload behavior under race conditions through one serialized, reason-about-able flow.

### Observable Truths

| # | Truth | Status | Evidence |
|---|---|---|---|
| 1 | User configuration changes save and reload deterministically when app saves and external edits occur in close timing windows. | ✗ FAILED | `ConfigPersistenceCoordinator.ApplyWatcherAsync` computes `_fileStore.ComputeHashAsync` at lines 288-290 before entering `ReadForReloadAsync`. `UserConfigFileStore.ComputeHashAsync` reads the file directly at lines 106-115. If that read throws during an external writer lock, `RunAsync` lines 217-220 logs and drops the operation; no `ReadFailed` result/failure is emitted. |
| 2 | User sees or receives a recoverable failure path when configuration persistence fails instead of silent logging-only fallback. | ⚠️ PARTIAL | Write, invalid YAML, missing file, explicit reload, and pre-cleaning flush failures have typed paths. Watcher hash read failures do not: they are log-only through the top-level operation-loop catch. |
| 3 | Maintainer can verify watcher race cases deterministically for debounce, deferred reload, invalid YAML, and app-save interactions. | ⚠️ PARTIAL | Deterministic coordinator tests cover hash echo, pending app-save rejection, cleaning deferral, invalid YAML, missing file, observer safety, explicit reload flush failure, and facade no-pending flush. No deterministic test covers the review CR-01 hash-read exception race; grep found no ComputeHash failure regression. |
| 4 | User configuration changes avoid YAML serialization round-trips for in-memory cloning. | ✓ VERIFIED | `UserConfiguration.Copy()` and nested `Copy()` methods perform manual deep copies. `ConfigurationService.cs` contains no `_serializer.Serialize`, `_deserializer.Deserialize<UserConfiguration>`, or `CloneConfig` matches; YamlDotNet remains only for disk persistence/main config. |
| 5 | Forced configuration flush is always a coordinator queue barrier, even when the facade has no pending app save. | ✓ VERIFIED | `ConfigurationService.cs:214-230` always awaits `_coordinator.FlushPendingSavesAsync(ct)` and only clears `_hasPendingUserSave` after `Success`/`NoOp`. `FlushPendingSavesAsync_NoPending_DrainsCoordinatorBarrier` passed. |
| 6 | Queued watcher reload work cannot remain behind a pre-cleaning/no-pending facade flush. | ✓ VERIFIED | The facade no-pending shortcut is gone; no-pending flush returns the coordinator barrier result rather than a local generation-0 NoOp. |
| 7 | Explicit reload cannot mask a failed pending app-save flush as a successful disk reload. | ✓ VERIFIED | `ConfigPersistenceCoordinator.cs:348-370` returns failed/rejected prerequisite flush results before `ReadForReloadAsync`; explicit reload regression tests passed. |
| 8 | Settings persistence failures are visible as a text-only recoverable banner. | ✓ VERIFIED | `SettingsViewModel.cs:124-128`, `172-184`, `217-233`, `369-386`, and `SettingsWindow.axaml:28-38` implement and bind the banner; manual UAT was approved in `10-05-SUMMARY.md`. |
| 9 | Pre-cleaning flush failure blocks process-launch-adjacent workflow. | ✓ VERIFIED | `CleaningPreflight.cs:34-56` branches on failed/rejected flush results before validation/game detection/plugin/MO2 work and throws `ConfigPersistenceFailureException`. |

**Score:** 54/55 must-haves verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|---|---|---|---|
| `AutoQAC/Models/Configuration/UserConfiguration.cs` | Public manual deep-copy methods on user config graph | ✓ VERIFIED | Parent and nested `Copy()` methods exist and deep-copy dictionaries/SkipLists; null normalization is implemented. |
| `AutoQAC/Models/Configuration/BackupSettings.cs` | `BackupSettings.Copy()` | ✓ VERIFIED | Present at line 23. |
| `AutoQAC/Models/Configuration/RetentionSettings.cs` | `RetentionSettings.Copy()` | ✓ VERIFIED | Present at line 36. |
| `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs` | Single-reader channel coordinator with safe publication and race policies | ✗ FAILED | Substantive and wired, but watcher hash exceptions escape before the typed failure/reload path and are dropped by the top-level catch. |
| `AutoQAC/Services/Configuration/ConfigPersistenceStatus.cs` | Typed result/failure/exception contracts | ✓ VERIFIED | `ConfigPersistenceResult`, `ConfigPersistenceFailure`, enums, and `ConfigPersistenceFailureException` are present with safe-summary documentation. |
| `AutoQAC/Services/Configuration/UserConfigFileStore.cs` | Temp-file write then replace/move and hash/read seam | ⚠️ PARTIAL | Temp+replace/move exists. Review WR-04 remains: temp file write at line 72 is outside the cleanup `try`, so cancellation/write failure during temp write can leave `*.tmp`. |
| `AutoQAC/Services/Configuration/ConfigurationService.cs` | Coordinator-backed facade with synchronized flags and barrier flush semantics | ✓ VERIFIED | Persistence delegates to coordinator; facade flags use `_stateLock`; flush no longer has the local no-pending NoOp bypass. |
| `AutoQAC/Services/Configuration/ConfigWatcherService.cs` | Event-source-only watcher | ⚠️ PARTIAL | Changed/Created/Renamed/Deleted signals are forwarded; Error events are still logged only at line 82 and do not enter the recoverable failure path. |
| `AutoQAC/Services/Cleaning/CleaningPreflight.cs` | Typed flush barrier branch | ✓ VERIFIED | Failed/rejected flush blocks before downstream workflow steps. |
| `AutoQAC/ViewModels/SettingsViewModel.cs` | Failure banner and clear-on-success | ✓ VERIFIED | Failure/result/reload subscriptions use `CallbackObserver<T>` plus `IUiDispatcher.Post`; Save flushes before close. |
| `AutoQAC/Views/SettingsWindow.axaml` | Visible banner binding | ✓ VERIFIED | `Border.IsVisible` binds `HasPersistenceBanner`; `TextBlock.Text` binds `PersistenceBannerText`. |

### Key Link Verification

| From | To | Via | Status | Details |
|---|---|---|---|---|
| `ConfigurationService` | `IConfigPersistenceCoordinator` | Save/load/flush/reload/status delegation | ✓ WIRED | `SaveUserConfigAsync`, `LoadUserConfigAsync`, `FlushPendingSavesAsync`, `ReloadFromDiskAsync`, `Failures`, `PersistenceResults`, and `UserConfigurationChanged` route to the coordinator. |
| `ConfigurationService.FlushPendingSavesAsync` | coordinator flush barrier | unconditional `await _coordinator.FlushPendingSavesAsync(ct)` | ✓ WIRED | Plan 10-09 gap closure verified by code and targeted test pass. |
| `ConfigWatcherService` | `IConfigPersistenceCoordinator` | FSW Changed/Created/Renamed/Deleted handlers | ✓ WIRED | Lines 78-81 call `NotifySettingsFileChanged`. |
| `ConfigWatcherService.Error` | recoverable failure pipeline | Error handler | ⚠️ PARTIAL | Line 82 logs only; no coordinator signal/error operation is sent. |
| `ConfigPersistenceCoordinator.ApplyWatcherAsync` | typed read failure handling | hash/read-before-reload flow | ✗ NOT_WIRED | `ComputeHashAsync` exceptions occur before `ReadForReloadAsync`; top-level catch logs and drops. |
| DI | coordinator/file store/facade/watcher | `AddSingleton` registrations | ✓ WIRED | `ServiceCollectionExtensions.cs:28-33`. |
| `CleaningPreflight` | `IConfigurationService.FlushPendingSavesAsync` | typed result branch | ✓ WIRED | Failed/rejected flush throws before process-launch-adjacent work. |
| `SettingsViewModel` | `IConfigurationService.Failures/PersistenceResults/UserConfigurationChanged` | `CallbackObserver<T>` + dispatcher | ✓ WIRED | `SettingsViewModel.cs:172-184`. |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|---|---|---|---|---|
| `ConfigPersistenceCoordinator.cs` | watcher reload candidate | `IUserConfigFileStore.ComputeHashAsync` then `ReadAsync` | Partial | ⚠️ HOLLOW for hash-read failure: exception bypasses typed result/failure publication. |
| `ConfigurationService.cs` | forced flush result | `_coordinator.FlushPendingSavesAsync` | Yes | ✓ FLOWING — no-pending facade flush returns coordinator result. |
| `CleaningPreflight.cs` | `flushResult` | `IConfigurationService.FlushPendingSavesAsync` | Yes | ✓ FLOWING — failed/rejected blocks before downstream work. |
| `SettingsViewModel.cs` | `PersistenceBannerText` | `IConfigurationService.Failures`, failed Save flush result | Yes | ✓ FLOWING — mapped to visible banner text. |
| `SettingsWindow.axaml` | `PersistenceBannerText`, `HasPersistenceBanner` | SettingsViewModel generated properties | Yes | ✓ FLOWING — visible `Border`/`TextBlock` bindings. |
| `UserConfiguration.Copy()` | copied config graph | mutable `UserConfiguration` instance | Yes | ✓ FLOWING — manual deep copies, no YAML clone. |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|---|---|---|---|
| No-pending facade flush drains coordinator barrier. | `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~ConfigurationServiceTests&FullyQualifiedName~FlushPendingSavesAsync_NoPending_DrainsCoordinatorBarrier" --nologo` | Passed: 1 test. | ✓ PASS |
| Explicit reload regressions remain green. | `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~ConfigPersistenceCoordinatorTests&FullyQualifiedName~ExplicitReload" --nologo` | Sequential rerun passed: 3 tests. | ✓ PASS |
| Parallel targeted run artifact. | Initial parallel targeted test run | One run hit an Avalonia generated-resource file lock; sequential rerun passed. | ℹ️ INFO |
| Watcher hash exception coverage exists. | Grep for `ComputeHash.*throw`, `ComputeHashAsync.*ReadFailed`, watcher hash failure tests. | No regression found; fake lacks a hash-failure knob. | ✗ FAIL |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|---|---|---|---|---|
| REF-03 | Plans 10-02, 10-03, 10-05, 10-06, 10-07, 10-08, 10-09 | Maintainer can reason about saves, reloads, deferrals, and failures through one serialized persistence flow. | ✗ BLOCKED | The facade barrier bypass is closed, but watcher hash failures still bypass the typed coordinator failure flow and are log-only at the top-level operation-loop catch. That is not one reason-about-able serialized failure path. |
| TEST-03 | Plans 10-02, 10-03, 10-04, 10-06, 10-07, 10-08, 10-09 | Maintainer can verify configuration watcher race cases deterministically. | ⚠️ PARTIAL | Many deterministic race tests exist and targeted regressions passed, but no test covers the writer-lock/hash-read race identified by code review CR-01. |
| PERF-03 | Plans 10-01, 10-03 | User configuration changes avoid YAML serialization round-trips for in-memory cloning. | ✓ SATISFIED | Manual `Copy()` graph exists; `ConfigurationService` no longer performs user-config YAML clone round-trips. |

No orphaned Phase 10 requirements were found. `.planning/REQUIREMENTS.md` maps only `REF-03`, `TEST-03`, and `PERF-03` to Phase 10, and all three appear in plan frontmatter.

### Code Review Findings Adjudication

| Review Finding | Verdict | Verification Evidence |
|---|---|---|
| CR-01: Watcher reloads are silently dropped if hashing races a writer lock | 🛑 TRUE GAP | `ApplyWatcherAsync` line 290 computes hash outside any catch/retry/failure publication. `RunAsync` catches the escaped exception and logs only. |
| WR-01: Settings Save failures are reported as cleaning-blocked failures | ⚠️ TRUE WARNING | `SettingsViewModel.SaveAsync` receives a `Flush` + `WriteFailed` result and maps all Flush write failures to “Could not save settings before cleaning. Cleaning was blocked...” at lines 222-223, even for the Settings dialog Save button. |
| WR-02: Public settings snapshot omits new Backup settings | ⚠️ TRUE WARNING | `ConfigurationService.GetAllSettingsAsync` lines 534-549 includes retention/settings fields but not `Backup.Enabled` or `Backup.MaxSessions`. |
| WR-03: FileSystemWatcher internal errors never reach the persistence failure pipeline | ⚠️ TRUE WARNING | `ConfigWatcherService.cs:82` logs `watcher.Error` only; `ConfigFileSignalKind.Error` is not forwarded. |
| WR-04: Canceled writes can leave temp settings files behind | ⚠️ TRUE WARNING | `UserConfigFileStore.WriteAsync` writes the temp file at line 72 before the cleanup-protected `try` starts at line 73. |

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|---|---:|---|---|---|
| `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs` | 288-290 | Hash read before typed reload failure handling | 🛑 Blocker | External edit watcher operation can be dropped during writer-lock races, leaving stale active config with no recoverable failure. |
| `AutoQAC/Services/Configuration/ConfigWatcherService.cs` | 82 | FSW error logged only | ⚠️ Warning | Dropped file events may not trigger recovery until another file event happens. |
| `AutoQAC/ViewModels/SettingsViewModel.cs` | 222-223 | Settings Save flush failure uses cleaning-blocked banner copy | ⚠️ Warning | Users saving Settings can see misleading cleaning-specific text. |
| `AutoQAC/Services/Configuration/ConfigurationService.cs` | 534-549 | Backup settings omitted from public settings snapshot | ⚠️ Warning | Diagnostics/import/export-style callers may miss user-facing backup policy. |
| `AutoQAC/Services/Configuration/UserConfigFileStore.cs` | 72-73 | Temp write outside cleanup-protected region | ⚠️ Warning | Cancellation or write failure during temp write may leave same-directory `*.tmp` files. |

Stub-pattern scan found only legitimate empty collection/default/null-return cases in configuration helpers and file-store missing-file handling. No placeholder implementation was identified.

### Human Verification Required

None for this gate decision. The remaining blocker is source-verifiable and testable with a deterministic file-store fake; Plan 10-05 already recorded manual UAT approval for the settings banner.

### Gaps Summary

Plan 10-09 correctly closes the previous no-pending facade flush barrier bypass. The current blocking gap is the required code review CR-01: watcher hash computation can fail before typed reload/read failure handling and the coordinator drops the operation through the top-level catch. This is not deferred to Phase 11, whose roadmap scope is user-facing diagnostics boundaries; it is a Phase 10 race-correctness and TEST-03 coverage gap. Fixing it requires moving hash failure handling into the watcher/reload failure pipeline and adding deterministic regression coverage for a thrown `ComputeHashAsync` followed by a later successful watcher reload.

---

_Verified: 2026-05-01T00:00:00Z_  
_Verifier: the agent (gsd-verifier)_
