---
phase: 10-configuration-persistence-hardening
verified: 2026-04-30T23:47:17Z
status: gaps_found
score: 48/52 must-haves verified
overrides_applied: 0
gaps:
  - truth: "User configuration saves/reloads are deterministic when app saves and reloads occur in close timing windows."
    status: failed
    reason: "Explicit reloads bypass the pending-app-save race policy, and the facade clears its pending-save flag after any reload request. A reload during a debounced save can overwrite the optimistic app edit and mark the save as no longer pending."
    artifacts:
      - path: "AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs"
        issue: "ApplyReloadRequestAsync reads/applies disk content unconditionally (lines 348-353) instead of rejecting/flushing when _pendingApp exists."
      - path: "AutoQAC/Services/Configuration/ConfigurationService.cs"
        issue: "ReloadFromDiskAsync unconditionally sets _hasPendingUserSave = false after the reload call (lines 539-545)."
    missing:
      - "Make explicit reload obey the same pending-save protection as watcher reload, or flush first and abort reload on failed flush."
      - "Only clear facade pending-save state when reload succeeds and is accepted."
  - truth: "Forced save/flush/reload barriers always return typed results instead of hanging, even when downstream observers misbehave."
    status: failed
    reason: "Coordinator publishes Subject<T>.OnNext inline from the single-reader operation loop before completing caller TaskCompletionSources. A throwing observer can skip TrySetResult and wedge SaveUserConfigAsync, FlushPendingSavesAsync, ReloadFromDiskAsync, and the pre-cleaning flush barrier."
    artifacts:
      - path: "AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs"
        issue: "OnNext occurs before completion at save lines 240-242 and flush lines 249-250; failure publication also calls OnNext inline at lines 451-454."
    missing:
      - "Wrap observer publication in safe best-effort helpers that log and swallow observer exceptions."
      - "Complete request TaskCompletionSources in finally blocks or before best-effort publication."
  - truth: "Maintainers can reason about persistence through one serialized flow."
    status: partial
    reason: "Core coordinator state is serialized, but ConfigurationService keeps unsynchronized facade state flags outside the channel, so concurrent facade calls can disagree with coordinator state."
    artifacts:
      - path: "AutoQAC/Services/Configuration/ConfigurationService.cs"
        issue: "_hasPendingUserSave and _loadedUserConfigFromDisk are read/written from async methods without lock/volatile synchronization (e.g., lines 169-204, 207-215, 539-545)."
    missing:
      - "Move pending/loaded bookkeeping into the coordinator or protect facade flags consistently under the existing state lock."
---

# Phase 10: Configuration Persistence Hardening Verification Report

**Phase Goal:** Users get reliable configuration saves/reloads under race conditions, and maintainers can reason about persistence through one serialized flow with lower in-memory clone cost.  
**Verified:** 2026-04-30T23:47:17Z  
**Status:** gaps_found  
**Re-verification:** No — initial verification

## Goal Achievement

Phase 10 implemented substantial infrastructure: model-owned `Copy()` methods, a channel-backed coordinator, file-store seam, typed persistence results, watcher signal wiring, pre-cleaning flush failure blocking, and a settings failure banner. However, the phase goal is not fully achieved because two advisory critical findings are real in the codebase and directly affect the must-have race/reliability contract.

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | User configuration changes save and reload deterministically when app saves and external edits occur in close timing windows. | ✗ FAILED | Watcher races are guarded (`_pendingApp` check at `ConfigPersistenceCoordinator.cs:304-308`), but explicit reloads are not (`ApplyReloadRequestAsync` at `ConfigPersistenceCoordinator.cs:348-353`). `ConfigurationService.ReloadFromDiskAsync` clears `_hasPendingUserSave` unconditionally at `ConfigurationService.cs:539-545`. |
| 2 | User sees or receives a recoverable failure path when configuration persistence fails instead of silent logging-only fallback. | ⚠️ PARTIAL | Typed `ConfigPersistenceFailure`, `ConfigPersistenceResult`, settings banner, and preflight exception exist. But observer exceptions can prevent completions/results from being delivered because `Subject.OnNext` calls are inline before `TrySetResult`. |
| 3 | Maintainer can verify watcher race cases deterministically for debounce, deferred reload, invalid YAML, and app-save interactions. | ✓ VERIFIED, with coverage warning | `ConfigPersistenceCoordinatorTests` includes deterministic fake-store tests for coalescing, hash echo, pending app-save watcher rejection, cleaning deferral, missing/invalid YAML, and no production throttle sleeps. Focused test run passed 59 phase-related tests. Missing tests for observer-throw and explicit reload/pending-save races are captured as gaps. |
| 4 | User configuration changes avoid YAML serialization round-trips for in-memory cloning. | ✓ VERIFIED | `UserConfiguration.Copy()` and nested `Copy()` methods exist and deep-copy mutable containers. `ConfigurationService.cs` contains no `_serializer.Serialize` or `_deserializer.Deserialize<UserConfiguration>` clone path; YamlDotNet remains disk/reload validation only. |
| 5 | Settings persistence failures are visible as a text-only recoverable banner. | ✓ VERIFIED | `SettingsViewModel` subscribes to `Failures`/`PersistenceResults` via `CallbackObserver<T>` and `IUiDispatcher` (`SettingsViewModel.cs:172-185`); `SettingsWindow.axaml:28-38` binds `HasPersistenceBanner` and `PersistenceBannerText`. |
| 6 | Pre-cleaning flush failure blocks process-launch-adjacent workflow. | ✓ VERIFIED | `CleaningPreflight.PrepareAsync` branches on `ConfigPersistenceStatusKind.Failed/Rejection` before validation/game detection (`CleaningPreflight.cs:34-56`). |

**Score:** 48/52 must-haves verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|---|---|---|---|
| `AutoQAC/Models/Configuration/UserConfiguration.cs` | Public deep-copy methods on user config graph | ✓ VERIFIED | Parent and nested `Copy()` methods present; SkipLists inner lists are copied. |
| `AutoQAC/Models/Configuration/BackupSettings.cs` | `BackupSettings.Copy()` | ✓ VERIFIED | Method present and tested by `UserConfigurationCopyTests`. |
| `AutoQAC/Models/Configuration/RetentionSettings.cs` | `RetentionSettings.Copy()` | ✓ VERIFIED | Method present and tested by `UserConfigurationCopyTests`. |
| `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs` | Single-reader channel coordinator | ⚠️ SUBSTANTIVE BUT DEFECTIVE | Channel exists with `SingleReader = true`, but critical observer-completion and explicit reload race gaps remain. |
| `AutoQAC/Services/Configuration/ConfigPersistenceStatus.cs` | Typed status/failure contracts | ✓ VERIFIED | `ConfigPersistenceResult`, `ConfigPersistenceFailure`, and `ConfigPersistenceFailureException` present. |
| `AutoQAC/Services/Configuration/UserConfigFileStore.cs` | Temp-file write then replace/move | ✓ VERIFIED | `WriteAsync` writes same-directory temp, then calls injected `File.Replace`/`File.Move` delegates. |
| `AutoQAC/Services/Configuration/ConfigurationService.cs` | Coordinator-backed facade | ⚠️ PARTIAL | Persistence methods delegate to coordinator, but unsynchronized `_hasPendingUserSave`/`_loadedUserConfigFromDisk` flags remain outside serialized flow. |
| `AutoQAC/Services/Configuration/ConfigWatcherService.cs` | Event-source-only watcher | ✓ VERIFIED | Changed/Created/Renamed/Deleted handlers call `_coordinator.NotifySettingsFileChanged`; no throttle/YAML/deferral policy remains. |
| `AutoQAC/Services/Cleaning/CleaningPreflight.cs` | Typed flush barrier branch | ✓ VERIFIED | Branch on `ConfigPersistenceResult.Status` exists before downstream preflight. |
| `AutoQAC/ViewModels/SettingsViewModel.cs` | Failure banner and clear-on-success | ✓ VERIFIED | Banner state, subscriptions, mapping, flush-before-close, and disposal exist. |
| `AutoQAC/Views/SettingsWindow.axaml` | Visible banner binding | ✓ VERIFIED | Banner bound to `HasPersistenceBanner` and `PersistenceBannerText`. |

### Key Link Verification

| From | To | Via | Status | Details |
|---|---|---|---|---|
| `ConfigurationService` | `ConfigPersistenceCoordinator` | `_coordinator.SaveUserConfigAsync/LoadCurrentAsync/FlushPendingSavesAsync/ReloadFromDiskAsync` | ✓ WIRED | Delegation present at `ConfigurationService.cs:197-217`, `539-543`. |
| `ConfigWatcherService` | `IConfigPersistenceCoordinator` | FSW event handlers | ✓ WIRED | Handlers at `ConfigWatcherService.cs:77-82`. |
| DI | Coordinator/file store | `AddSingleton` registrations | ✓ WIRED | `ServiceCollectionExtensions.cs:28-33`. |
| `CleaningPreflight` | `IConfigurationService.FlushPendingSavesAsync` | typed result branch | ✓ WIRED | `CleaningPreflight.cs:34-56`. |
| `SettingsViewModel` | `IConfigurationService.Failures/PersistenceResults/UserConfigurationChanged` | `CallbackObserver<T>` + dispatcher | ✓ WIRED | `SettingsViewModel.cs:172-185`. |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|---|---|---|---|---|
| `SettingsViewModel.cs` | `PersistenceBannerText` | `IConfigurationService.Failures` / flush failure result | Yes — mapped from `ConfigPersistenceFailure` | ✓ FLOWING |
| `SettingsWindow.axaml` | `PersistenceBannerText`, `HasPersistenceBanner` | SettingsViewModel generated observable properties | Yes — bound into visible `Border`/`TextBlock` | ✓ FLOWING |
| `CleaningPreflight.cs` | `flushResult` | `IConfigurationService.FlushPendingSavesAsync` | Yes — typed coordinator result | ✓ FLOWING |
| `ConfigurationService.cs` | active user config | coordinator snapshot | Partial — coordinator data flows, but facade pending flags can diverge | ⚠️ PARTIAL |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|---|---|---|---|
| Phase 10 focused tests pass | `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~ConfigPersistenceCoordinatorTests|FullyQualifiedName~SettingsViewModelTests|FullyQualifiedName~CleaningPreflightTests|FullyQualifiedName~UserConfigurationCopyTests" --nologo` | 59 passed, 0 failed | ✓ PASS |
| Observer-exception completion safety | Code inspection | No safe publication or completion-finally pattern; inline `Subject.OnNext` before completions | ✗ FAIL |
| Explicit reload vs pending app save | Code inspection | `ApplyReloadRequestAsync` lacks `_pendingApp` guard; facade clears pending flag unconditionally | ✗ FAIL |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|---|---|---|---|---|
| REF-03 | Plans 10-02, 10-03, 10-05 | Maintainer can reason about saves, reloads, deferrals, and failures through one serialized persistence flow. | ✗ BLOCKED | Coordinator exists, but observer calls can wedge operations and facade state flags remain outside serialized/synchronized flow. |
| TEST-03 | Plans 10-02, 10-03, 10-04 | Maintainer can verify configuration watcher race cases deterministically. | ⚠️ PARTIAL | Watcher race tests are deterministic and pass, but critical explicit reload/pending-save and observer-exception race/failure cases are untested. |
| PERF-03 | Plans 10-01, 10-03 | User configuration changes avoid YAML serialization round-trips for in-memory cloning. | ✓ SATISFIED | Manual `Copy()` graph exists; user-config clone path no longer uses YAML serialization. |

No orphaned Phase 10 requirements were found: `.planning/REQUIREMENTS.md` maps only `REF-03`, `TEST-03`, and `PERF-03` to Phase 10, and all three appear in plan frontmatter.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|---|---:|---|---|---|
| `ConfigPersistenceCoordinator.cs` | 240, 249, 281, 319, 344, 352, 431, 454 | Inline `Subject<T>.OnNext` from operation loop | 🛑 Blocker | Observer exceptions can escape before task completions are signaled, causing hangs. |
| `ConfigPersistenceCoordinator.cs` | 348-353 | Reload request lacks pending-save guard | 🛑 Blocker | Explicit reload can overwrite queued app save. |
| `ConfigurationService.cs` | 539-545 | Reload clears pending save flag unconditionally | 🛑 Blocker | Facade can report no pending save after rejected/failed reload or while save is pending. |
| `ConfigurationService.cs` | 169-215 | Unsynchronized facade flags outside coordinator | ⚠️ Warning | Concurrent facade calls can diverge from coordinator state. |
| `ConfigurationService.cs` | 248-254 | MO2 mode accepts blank binary in path validation | ⚠️ Warning | Not a Phase 10 persistence blocker, but review warning remains valid. |
| `ConfigurationServiceTests.cs` | 459-473 (per review) | Empty-file test has no assertion | ⚠️ Warning | Weakens regression coverage for invalid/empty YAML path. |

### Human Verification Required

None for the automated gate decision. Plan 10-05 records manual UAT approval for the banner UX, but the phase still has blocker code gaps, so the overall status remains `gaps_found`.

### Gaps Summary

The implementation delivers most planned artifacts and many tests, but the phase goal requires reliable save/reload behavior under race conditions. That reliability is not proven because explicit reloads can still race pending app saves, and observer exceptions can hang the serialized persistence loop's callers before typed results are completed. These are not deferred to Phase 11, whose roadmap scope is diagnostics boundaries rather than persistence correctness.

---

_Verified: 2026-04-30T23:47:17Z_  
_Verifier: the agent (gsd-verifier)_
