---
phase: 10-configuration-persistence-hardening
verified: 2026-05-01T01:01:43Z
status: gaps_found
score: 51/52 must-haves verified
overrides_applied: 0
re_verification:
  previous_status: gaps_found
  previous_score: 48/52
  gaps_closed:
    - "Observer exceptions on Subject<T>.OnNext no longer prevent save/flush/reload caller completions."
    - "ConfigurationService facade pending/loaded flags are synchronized under _stateLock."
    - "ReloadFromDiskAsync only clears the facade pending-save flag after a successful reload result."
  gaps_remaining:
    - "Explicit reload masks a failed prerequisite pending-save flush and can report Success after dropping the queued app edit."
  regressions: []
gaps:
  - truth: "User configuration changes save and reload deterministically when app saves and reloads occur in close timing windows."
    status: failed
    reason: "ApplyReloadRequestAsync now detects _pendingApp and flushes first, but it ignores Failed/Rejected flush results and continues to read/reload disk content. If the pending save write fails and the old disk file still parses, ReloadFromDiskAsync can return Success, clear facade pending state, and leave the user edit unpersisted."
    artifacts:
      - path: "AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs"
        issue: "Lines 348-362 call FlushPendingInsideConsumerAsync and SafePublishResult(flushResult), but do not branch on failed/rejected flushResult before ReadForReloadAsync/ValidateAndApply."
      - path: "AutoQAC.Tests/Services/Configuration/ConfigPersistenceCoordinatorTests.cs"
        issue: "Explicit reload regression tests cover successful pending-save flush only; no test covers pending save WriteFailure followed by a readable old disk file."
    missing:
      - "When explicit reload flushes a pending app save, return the failed/rejected flush result immediately and do not read/apply disk content."
      - "Add a regression test for ReloadFromDiskAsync with _pendingApp and store.WriteFailure asserting result.Status=Failed and old disk content is not accepted as a successful reload."
---

# Phase 10: Configuration Persistence Hardening Verification Report

**Phase Goal:** Users get reliable configuration saves/reloads under race conditions, and maintainers can reason about persistence through one serialized flow with lower in-memory clone cost.  
**Verified:** 2026-05-01T01:01:43Z  
**Status:** gaps_found  
**Re-verification:** Yes — after gap-closure plans 10-06 and 10-07

## Goal Achievement

Phase 10 is substantially implemented, and two of the three previous blocker classes are closed in code. Safe observer publication is now present and tested, and facade pending/loaded flags are protected under `_stateLock`. However, the phase goal still is not achieved: explicit reload can still mask a failed prerequisite save flush and return a successful reload using old disk content.

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | User configuration changes save and reload deterministically when app saves and external edits occur in close timing windows. | ✗ FAILED | Watcher races are guarded, and explicit reload now checks `_pendingApp` at `ConfigPersistenceCoordinator.cs:348-357`; however, it ignores a failed/rejected `flushResult` and proceeds to `ReadForReloadAsync`/`ValidateAndApply` at lines 359-362. This can report reload Success after the queued app save failed. |
| 2 | User sees or receives a recoverable failure path when configuration persistence fails instead of silent logging-only fallback. | ✓ VERIFIED | `ConfigPersistenceFailure`, `ConfigPersistenceResult`, and `ConfigPersistenceFailureException` exist; `CleaningPreflight.cs:34-56` blocks cleaning on failed/rejected flush; `SettingsViewModel.cs:172-184` subscribes to failures/results and `SettingsWindow.axaml:28-38` binds a visible banner. |
| 3 | Maintainer can verify watcher race cases deterministically for debounce, deferred reload, invalid YAML, and app-save interactions. | ✓ VERIFIED with coverage warning | `ConfigPersistenceCoordinatorTests` covers watcher hash echo, pending app-save rejection, cleaning deferral, invalid YAML, missing file, stale observations, and safe observer tests. Missing coverage remains for explicit reload with pending-save write failure, listed as the blocker gap. |
| 4 | User configuration changes avoid YAML serialization round-trips for in-memory cloning. | ✓ VERIFIED | `UserConfiguration.Copy()` and nested `Copy()` methods perform manual deep copies; `ConfigurationService.cs` has zero `_serializer.Serialize` and zero `_deserializer.Deserialize<UserConfiguration>` matches. |
| 5 | Settings persistence failures are visible as a text-only recoverable banner. | ✓ VERIFIED | `SettingsViewModel.cs:124-128`, `172-184`, `217-233`, and `369-386` implement banner state, typed mapping, clear-on-success, and flush-before-close; `SettingsWindow.axaml:28-38` renders the banner. |
| 6 | Pre-cleaning flush failure blocks process-launch-adjacent workflow. | ✓ VERIFIED | `CleaningPreflight.PrepareAsync` inspects `flushResult.Status` before validation/game detection/plugin work and throws `ConfigPersistenceFailureException` on Failed/Rejected (`CleaningPreflight.cs:34-56`). |

**Score:** 51/52 must-haves verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|---|---|---|---|
| `AutoQAC/Models/Configuration/UserConfiguration.cs` | Public deep-copy methods on user config graph | ✓ VERIFIED | Parent and nested `Copy()` methods exist and deep-copy mutable containers. |
| `AutoQAC/Models/Configuration/BackupSettings.cs` | `BackupSettings.Copy()` | ✓ VERIFIED | Present and covered by copy tests. |
| `AutoQAC/Models/Configuration/RetentionSettings.cs` | `RetentionSettings.Copy()` | ✓ VERIFIED | Present and covered by copy tests. |
| `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs` | Single-reader channel coordinator, safe publication, reload guard | ⚠️ PARTIAL | Channel and SafePublish helpers exist; explicit reload guard flushes pending saves but does not abort on failed flush. |
| `AutoQAC/Services/Configuration/ConfigPersistenceStatus.cs` | Typed status/failure contracts | ✓ VERIFIED | Result/failure enums, records, and `ConfigPersistenceFailureException` present. |
| `AutoQAC/Services/Configuration/UserConfigFileStore.cs` | Temp-file write then replace/move | ✓ VERIFIED | Same-directory temp write followed by injected `File.Replace`/`File.Move`; cleanup preserves original exception. |
| `AutoQAC/Services/Configuration/ConfigurationService.cs` | Coordinator-backed facade with synchronized flags | ✓ VERIFIED | Persistence delegates to coordinator; `_hasPendingUserSave`/`_loadedUserConfigFromDisk` are accessed through locked helpers/blocks; reload clears pending only on Success. |
| `AutoQAC/Services/Configuration/ConfigWatcherService.cs` | Event-source-only watcher | ✓ VERIFIED | Changed/Created/Renamed/Deleted handlers call `NotifySettingsFileChanged`; legacy throttle/YAML/deferral policy is absent. |
| `AutoQAC/Services/Cleaning/CleaningPreflight.cs` | Typed flush barrier branch | ✓ VERIFIED | Failed/Rejected flush blocks preflight before downstream work. |
| `AutoQAC/ViewModels/SettingsViewModel.cs` | Failure banner and clear-on-success | ✓ VERIFIED | Failure, result, and accepted-config subscriptions use `CallbackObserver<T>` plus `IUiDispatcher.Post`; Save flushes before close. |
| `AutoQAC/Views/SettingsWindow.axaml` | Visible banner binding | ✓ VERIFIED | `Border.IsVisible` binds `HasPersistenceBanner`; `TextBlock.Text` binds `PersistenceBannerText`. |

### Key Link Verification

| From | To | Via | Status | Details |
|---|---|---|---|---|
| `ConfigurationService` | `IConfigPersistenceCoordinator` | `_coordinator.SaveUserConfigAsync/LoadCurrentAsync/FlushPendingSavesAsync/ReloadFromDiskAsync` | ✓ WIRED | Delegation present in `ConfigurationService.cs:164-224` and `555-580`. |
| `ConfigWatcherService` | `IConfigPersistenceCoordinator` | FSW event handlers | ✓ WIRED | Handlers at `ConfigWatcherService.cs:78-81`. |
| DI | Coordinator/file store | `AddSingleton` registrations | ✓ WIRED | `ServiceCollectionExtensions.cs:28-33`. |
| `CleaningPreflight` | `IConfigurationService.FlushPendingSavesAsync` | typed result branch | ✓ WIRED | `CleaningPreflight.cs:34-56`. |
| `SettingsViewModel` | `IConfigurationService.Failures/PersistenceResults/UserConfigurationChanged` | `CallbackObserver<T>` + dispatcher | ✓ WIRED | `SettingsViewModel.cs:172-184`. |
| `ConfigPersistenceCoordinator.ApplySave/Flush/Reload` | Subject observers | SafePublish helpers | ✓ WIRED | Subject `OnNext` calls only occur inside `SafePublishAccepted/Result/Failure` (`ConfigPersistenceCoordinator.cs:469-512`). |
| `ApplyReloadRequestAsync` | pending app-save guard | `_pendingApp` check before disk read | ⚠️ PARTIAL | Guard exists at `ConfigPersistenceCoordinator.cs:350-357`, but failure result is ignored before disk read. |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|---|---|---|---|---|
| `SettingsViewModel.cs` | `PersistenceBannerText` | `IConfigurationService.Failures`, failed save flush result | Yes — mapped from `ConfigPersistenceFailure` | ✓ FLOWING |
| `SettingsWindow.axaml` | `PersistenceBannerText`, `HasPersistenceBanner` | SettingsViewModel generated observable properties | Yes — bound into visible `Border`/`TextBlock` | ✓ FLOWING |
| `CleaningPreflight.cs` | `flushResult` | `IConfigurationService.FlushPendingSavesAsync` | Yes — typed coordinator/facade result | ✓ FLOWING |
| `ConfigPersistenceCoordinator.cs` | active config/reload result | file store read/write + channel operations | Partial — explicit reload can replace failed flush result with later reload result | ⚠️ PARTIAL |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|---|---|---|---|
| Phase 10 focused tests pass | `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~ConfigPersistenceCoordinatorTests|FullyQualifiedName~ConfigurationServiceTests|FullyQualifiedName~CleaningPreflightTests|FullyQualifiedName~SettingsViewModelTests|FullyQualifiedName~UserConfigurationCopyTests|FullyQualifiedName~UserConfigFileStoreTests" --nologo` | 102 passed, 0 failed | ✓ PASS |
| Observer-exception completion safety | Code inspection + tests `Flush_WithThrowingObserver_StillReturnsTypedResult`, `Save_WithThrowingAcceptedObserver_StillCompletesAndActiveUpdates`, `Reload_WithThrowingObserver_StillReturnsTypedResult` | SafePublish helpers wrap all subject `OnNext` calls and tests exist | ✓ PASS |
| Explicit reload after pending-save write failure | Code inspection | `ApplyReloadRequestAsync` ignores failed `flushResult` and continues to reload disk | ✗ FAIL |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|---|---|---|---|---|
| REF-03 | Plans 10-02, 10-03, 10-05, 10-06, 10-07 | Maintainer can reason about saves, reloads, deferrals, and failures through one serialized persistence flow. | ✗ BLOCKED | Core flow is serialized and facade flags are synchronized, but explicit reload result semantics are not reasoned through one barrier: a failed pending-save flush can be overwritten by a later reload success in the same operation. |
| TEST-03 | Plans 10-02, 10-03, 10-04, 10-06, 10-07 | Maintainer can verify configuration watcher/race cases deterministically. | ⚠️ PARTIAL | Deterministic race coverage exists, but no regression test covers explicit reload with pending-save `WriteFailure` and readable old disk content. |
| PERF-03 | Plans 10-01, 10-03 | User configuration changes avoid YAML serialization round-trips for in-memory cloning. | ✓ SATISFIED | Manual `Copy()` graph exists; `ConfigurationService` no longer performs YAML user-config clone round-trips. |

No orphaned Phase 10 requirements were found. `.planning/REQUIREMENTS.md` maps only `REF-03`, `TEST-03`, and `PERF-03` to Phase 10, and all three appear in plan frontmatter.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|---|---:|---|---|---|
| `ConfigPersistenceCoordinator.cs` | 348-362 | Failed prerequisite flush result is published but ignored | 🛑 Blocker | Explicit reload can mask failed persistence and accept old disk content. |
| `ConfigPersistenceCoordinatorTests.cs` | 345-358 | Positive explicit reload test only covers successful flush | ⚠️ Warning | The remaining blocker lacks deterministic regression coverage. |
| `ConfigurationService.cs` | 184-195 | Initial load marks `_loadedUserConfigFromDisk = true` even when reload failed | ⚠️ Warning | Review WR-01 remains valid: transient initial reload failures will not be retried automatically by later `LoadUserConfigAsync` calls. Not counted as a blocker because it is not one of the previous must-have gaps and is outside the close-timing race truth, but should be considered follow-up. |
| `ConfigPersistenceCoordinator.cs` | 217-220 | Unexpected operation exceptions are logged without operation-specific TCS completion | ⚠️ Warning | Review WR-02 remains valid for unexpected exceptions outside safe observer publication; the tested observer path is fixed. |

### Human Verification Required

None for this gate decision. Plan 10-05 already recorded manual UAT approval for the settings banner, and the remaining issue is a code-level blocker.

### Gaps Summary

Plans 10-06 and 10-07 closed the observer-hang and facade synchronization gaps from the prior verification. The remaining blocker is narrower but still goal-critical: explicit reload must not report success when the pending app save it depends on failed. Phase 11 is about diagnostics boundaries and does not explicitly cover persistence race correctness, so this gap is not deferred.

---

_Verified: 2026-05-01T01:01:43Z_  
_Verifier: the agent (gsd-verifier)_
