---
phase: 10-configuration-persistence-hardening
verified: 2026-05-01T01:23:24Z
status: gaps_found
score: 51/52 must-haves verified
overrides_applied: 0
re_verification:
  previous_status: gaps_found
  previous_score: 51/52
  gaps_closed:
    - "Explicit reload no longer masks a failed prerequisite pending-save flush; it returns the failed/rejected flush result before reading disk."
  gaps_remaining:
    - "ConfigurationService.FlushPendingSavesAsync still bypasses the coordinator barrier when the facade has no pending app save, so queued watcher reloads can remain unprocessed before cleaning/preflight continues."
  regressions: []
gaps:
  - truth: "User configuration changes save and reload deterministically when app saves and external edits occur in close timing windows."
    status: failed
    reason: "Code review CR-01 remains true: ConfigurationService.FlushPendingSavesAsync returns a facade-level NoOp when _hasPendingUserSave is false instead of enqueueing a coordinator flush barrier. A watcher event already queued in ConfigPersistenceCoordinator can therefore remain behind the pre-cleaning flush call, allowing downstream code to continue with stale active config."
    artifacts:
      - path: "AutoQAC/Services/Configuration/ConfigurationService.cs"
        issue: "Lines 214-224 short-circuit to NoOp when HasPendingUserSave() is false, bypassing _coordinator.FlushPendingSavesAsync and any queued watcher/reload operations."
      - path: "AutoQAC/Services/Configuration/ConfigWatcherService.cs"
        issue: "Lines 78-81 enqueue watcher signals through NotifySettingsFileChanged, but the facade no-pending flush path does not drain those signals as a barrier."
      - path: "AutoQAC.Tests/Services/ConfigurationServiceTests.cs"
        issue: "Existing no-pending flush test asserts NoOp behavior, but no regression proves a queued watcher reload is processed before FlushPendingSavesAsync returns."
    missing:
      - "Make ConfigurationService.FlushPendingSavesAsync always enqueue/await the coordinator flush barrier, even when the facade has no pending app save."
      - "Only clear the facade pending-save flag after coordinator Success/NoOp, but do not use the facade flag to skip the barrier."
      - "Add a regression test that queues a watcher notification/external config update, immediately calls facade FlushPendingSavesAsync with no pending app save, and asserts the external config has been applied before flush returns."
---

# Phase 10: Configuration Persistence Hardening Verification Report

**Phase Goal:** Users get reliable configuration saves/reloads under race conditions, and maintainers can reason about persistence through one serialized flow with lower in-memory clone cost.  
**Verified:** 2026-05-01T01:23:24Z  
**Status:** gaps_found  
**Re-verification:** Yes — after Wave 8 gap closure

## Goal Achievement

Wave 8 closes the prior explicit reload write-failure masking gap in the coordinator. `ApplyReloadRequestAsync` now returns a failed/rejected prerequisite flush result directly, and the deterministic regression test exists and passes.

However, Phase 10 still does not satisfy the goal because the Phase 10 code review finding CR-01 is confirmed in the current codebase. `ConfigurationService.FlushPendingSavesAsync` has a facade-level no-pending short-circuit, so the required flush barrier is not always a coordinator barrier. That breaks the single serialized flow and can let queued watcher reloads remain unprocessed before cleaning preflight continues.

### Observable Truths

| # | Truth | Status | Evidence |
|---|---|---|---|
| 1 | User configuration changes save and reload deterministically when app saves and external edits occur in close timing windows. | ✗ FAILED | Wave 8 fixed explicit reload write-failure masking (`ConfigPersistenceCoordinator.cs:350-363`), but `ConfigurationService.cs:214-224` returns NoOp without entering the coordinator when no facade pending save exists. A queued watcher reload from `ConfigWatcherService.cs:78-81` is not drained by this flush call. |
| 2 | User sees or receives a recoverable failure path when configuration persistence fails instead of silent logging-only fallback. | ✓ VERIFIED | `ConfigPersistenceFailure`, `ConfigPersistenceResult`, and `ConfigPersistenceFailureException` exist; `CleaningPreflight.cs:34-56` blocks cleaning on failed/rejected flush; `SettingsViewModel.cs:172-184` subscribes to failures/results and `SettingsWindow.axaml:28-38` binds a visible banner. |
| 3 | Maintainer can verify watcher race cases deterministically for debounce, deferred reload, invalid YAML, and app-save interactions. | ⚠️ PARTIAL | Coordinator tests cover hash echo, pending app-save rejection, cleaning deferral, invalid YAML, missing file, stale observations, observer safety, and explicit reload flush failure. Missing facade-barrier regression for queued watcher reload + no-pending flush remains. |
| 4 | User configuration changes avoid YAML serialization round-trips for in-memory cloning. | ✓ VERIFIED | `UserConfiguration.Copy()` and nested `Copy()` methods perform manual deep copies; `ConfigurationService.cs` has zero `_serializer.Serialize`, `_deserializer.Deserialize<UserConfiguration>`, or `CloneConfig` matches. |
| 5 | Settings persistence failures are visible as a text-only recoverable banner. | ✓ VERIFIED | `SettingsViewModel.cs:124-128`, `172-184`, `217-233`, and `369-386` implement banner state, typed mapping, clear-on-success, and flush-before-close; `SettingsWindow.axaml:28-38` renders the banner. |
| 6 | Pre-cleaning flush failure blocks process-launch-adjacent workflow. | ⚠️ PARTIAL | `CleaningPreflight.PrepareAsync` branches on the typed result, but the result can be a facade-level NoOp that never drained queued watcher reloads. Failed pending saves block; pending external reload ordering is not guaranteed. |

**Score:** 51/52 must-haves verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|---|---|---|---|
| `AutoQAC/Models/Configuration/UserConfiguration.cs` | Public deep-copy methods on user config graph | ✓ VERIFIED | Parent and nested `Copy()` methods exist and deep-copy mutable containers. |
| `AutoQAC/Models/Configuration/BackupSettings.cs` | `BackupSettings.Copy()` | ✓ VERIFIED | Present and covered by copy tests. |
| `AutoQAC/Models/Configuration/RetentionSettings.cs` | `RetentionSettings.Copy()` | ✓ VERIFIED | Present and covered by copy tests. |
| `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs` | Single-reader channel coordinator, safe publication, explicit reload guard | ✓ VERIFIED | Single-reader channel, SafePublish helpers, pending-save explicit reload guard, and failed/rejected flush short-circuit are present. |
| `AutoQAC/Services/Configuration/ConfigPersistenceStatus.cs` | Typed status/failure contracts | ✓ VERIFIED | Result/failure enums, records, and `ConfigPersistenceFailureException` present. |
| `AutoQAC/Services/Configuration/UserConfigFileStore.cs` | Temp-file write then replace/move | ✓ VERIFIED | Same-directory temp write followed by `File.Replace`/`File.Move`; cleanup preserves original exception. |
| `AutoQAC/Services/Configuration/ConfigurationService.cs` | Coordinator-backed facade with synchronized flags and barrier flush semantics | ✗ FAILED | Persistence delegates exist and flags are locked, but `FlushPendingSavesAsync` bypasses the coordinator barrier when `HasPendingUserSave()` is false. |
| `AutoQAC/Services/Configuration/ConfigWatcherService.cs` | Event-source-only watcher | ⚠️ PARTIAL | Changed/Created/Renamed/Deleted handlers call `NotifySettingsFileChanged`; legacy policy is absent. Review WR-01 remains: Error events are logged only, not forwarded for recovery. |
| `AutoQAC/Services/Cleaning/CleaningPreflight.cs` | Typed flush barrier branch | ⚠️ PARTIAL | Failed/Rejected flush blocks preflight, but facade can return NoOp without draining queued watcher reloads. |
| `AutoQAC/ViewModels/SettingsViewModel.cs` | Failure banner and clear-on-success | ✓ VERIFIED | Failure, result, and accepted-config subscriptions use `CallbackObserver<T>` plus `IUiDispatcher.Post`; Save flushes before close. |
| `AutoQAC/Views/SettingsWindow.axaml` | Visible banner binding | ✓ VERIFIED | `Border.IsVisible` binds `HasPersistenceBanner`; `TextBlock.Text` binds `PersistenceBannerText`. |

### Key Link Verification

| From | To | Via | Status | Details |
|---|---|---|---|---|
| `ConfigurationService` | `IConfigPersistenceCoordinator` | `_coordinator.SaveUserConfigAsync/LoadCurrentAsync/FlushPendingSavesAsync/ReloadFromDiskAsync` | ⚠️ PARTIAL | Most methods delegate, but `FlushPendingSavesAsync` returns a local NoOp at `ConfigurationService.cs:219-222` when no facade pending save exists. |
| `ConfigWatcherService` | `IConfigPersistenceCoordinator` | FSW event handlers | ✓ WIRED | Handlers at `ConfigWatcherService.cs:78-81` submit Changed/Created/Renamed/Deleted signals. |
| DI | Coordinator/file store | `AddSingleton` registrations | ✓ WIRED | `ServiceCollectionExtensions.cs:28-33` registers file store, concrete coordinator, interface, configuration service, and watcher. |
| `CleaningPreflight` | `IConfigurationService.FlushPendingSavesAsync` | typed result branch | ⚠️ PARTIAL | Branch exists, but it trusts the facade result; the facade may not have acted as a coordinator barrier. |
| `SettingsViewModel` | `IConfigurationService.Failures/PersistenceResults/UserConfigurationChanged` | `CallbackObserver<T>` + dispatcher | ✓ WIRED | `SettingsViewModel.cs:172-184`. |
| `ConfigPersistenceCoordinator.ApplySave/Flush/Reload` | Subject observers | SafePublish helpers | ✓ WIRED | Subject `OnNext` calls occur only inside `SafePublishAccepted/Result/Failure` (`ConfigPersistenceCoordinator.cs:472-519`). |
| `ApplyReloadRequestAsync` | pending app-save guard | `_pendingApp` check before disk read | ✓ WIRED | Lines 350-363 flush pending app save and return failed/rejected flush result before disk read. |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|---|---|---|---|---|
| `SettingsViewModel.cs` | `PersistenceBannerText` | `IConfigurationService.Failures`, failed save flush result | Yes — mapped from `ConfigPersistenceFailure` | ✓ FLOWING |
| `SettingsWindow.axaml` | `PersistenceBannerText`, `HasPersistenceBanner` | SettingsViewModel generated observable properties | Yes — bound into visible `Border`/`TextBlock` | ✓ FLOWING |
| `CleaningPreflight.cs` | `flushResult` | `IConfigurationService.FlushPendingSavesAsync` | Partial | ⚠️ HOLLOW for queued reload barrier semantics when facade returns local NoOp. |
| `ConfigPersistenceCoordinator.cs` | active config/reload result | file store read/write + channel operations | Yes for coordinator-level explicit reload and watcher operations | ✓ FLOWING |
| `ConfigurationService.cs` | facade pending flag | `_hasPendingUserSave` under `_stateLock` | Partial | ⚠️ HOLLOW as a barrier decision source; absence of pending app save does not imply the coordinator queue is drained. |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|---|---|---|---|
| Explicit reload gap closure tests pass. | `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~ConfigPersistenceCoordinatorTests&FullyQualifiedName~ExplicitReload" --nologo` | Passed: 3 tests. | ✓ PASS |
| Facade no-pending flush behavior is currently locked in. | `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~ConfigurationServiceTests&FullyQualifiedName~FlushPendingSavesAsync_NoPending" --nologo` | Passed: 1 test asserting NoOp. | ⚠️ WARNING — test does not include queued watcher reload and reinforces the short-circuit. |
| Parallel test run artifact. | Initial parallel targeted runs | One run hit an Avalonia generated-resource file lock; sequential rerun of the explicit reload tests passed. | ℹ️ INFO |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|---|---|---|---|---|
| REF-03 | Plans 10-02, 10-03, 10-05, 10-06, 10-07, 10-08 | Maintainer can reason about saves, reloads, deferrals, and failures through one serialized persistence flow. | ✗ BLOCKED | Coordinator internals are serialized, but the public facade still has a non-serialized fast path for `FlushPendingSavesAsync` when no app save is pending. That means maintainers cannot treat forced flush as a queue barrier across watcher reloads. |
| TEST-03 | Plans 10-02, 10-03, 10-04, 10-06, 10-07, 10-08 | Maintainer can verify configuration watcher/race cases deterministically. | ⚠️ PARTIAL | Deterministic coordinator race coverage exists, and Wave 8 adds the explicit reload write-failure regression. Missing deterministic facade regression for queued watcher reload followed by no-pending flush. |
| PERF-03 | Plans 10-01, 10-03 | User configuration changes avoid YAML serialization round-trips for in-memory cloning. | ✓ SATISFIED | Manual `Copy()` graph exists; `ConfigurationService` no longer performs YAML user-config clone round-trips. |

No orphaned Phase 10 requirements were found. `.planning/REQUIREMENTS.md` maps only `REF-03`, `TEST-03`, and `PERF-03` to Phase 10, and all three appear in plan frontmatter.

### Code Review Findings Adjudication

| Review Finding | Verdict | Verification Evidence |
|---|---|---|
| CR-01: `ConfigurationService.FlushPendingSavesAsync` bypasses the coordinator barrier when `_hasPendingUserSave` is false | 🛑 TRUE GAP | `ConfigurationService.cs:219-222` returns a local `ConfigPersistenceResult(NoOp, Flush, 0, null)` without awaiting `_coordinator.FlushPendingSavesAsync`. `ConfigWatcherService.cs:78-81` can queue reload signals that this path does not drain. |
| WR-01: FileSystemWatcher error events are logged but never trigger recovery | ⚠️ TRUE WARNING | `ConfigWatcherService.cs:82` only logs `watcher.Error`; `ConfigFileSignalKind.Error` is not forwarded. This can miss changes after FSW buffer errors, but is not the primary Phase 10 blocker. |
| WR-02: Failed initial user-config reload is marked as loaded, preventing automatic retry | ⚠️ TRUE WARNING | `ConfigurationService.cs:184-195` sets `_loadedUserConfigFromDisk = true` even when `result.Status == Failed`. This makes transient initial reload failure sticky until watcher/explicit reload. |

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|---|---:|---|---|---|
| `AutoQAC/Services/Configuration/ConfigurationService.cs` | 219-222 | Facade-level NoOp bypasses coordinator flush barrier | 🛑 Blocker | Queued watcher reloads are not guaranteed to apply before pre-cleaning flush returns. |
| `AutoQAC/Services/Configuration/ConfigWatcherService.cs` | 82 | FSW error logged only | ⚠️ Warning | Dropped file events may not trigger recovery until another file event happens. |
| `AutoQAC/Services/Configuration/ConfigurationService.cs` | 184-195 | Initial load failure still marks loaded | ⚠️ Warning | Transient initial reload failures may not retry through normal `LoadUserConfigAsync`. |

Stub-pattern scan found only legitimate empty collection/null-return cases in config helpers and file-store missing-file handling. No placeholder implementation was identified.

### Human Verification Required

None for this gate decision. The remaining issue is code-level and testable without manual UI validation. Plan 10-05 already recorded manual UAT approval for the settings banner.

### Gaps Summary

Wave 8 successfully closed the previously documented explicit reload write-failure masking blocker. The current Phase 10 blocker is the review CR-01 facade barrier bypass: a forced flush is not always a coordinator barrier. Fixing it requires removing the no-pending facade short-circuit, always awaiting the coordinator barrier, and adding deterministic regression coverage for queued watcher reloads before no-pending flush returns. Phase 11 is diagnostics-focused and does not clearly defer persistence race correctness, so this is an actionable Phase 10 gap.

---

_Verified: 2026-05-01T01:23:24Z_  
_Verifier: the agent (gsd-verifier)_
