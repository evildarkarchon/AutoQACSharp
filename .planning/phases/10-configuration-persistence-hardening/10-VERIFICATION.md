---
phase: 10-configuration-persistence-hardening
verified: 2026-05-01T02:18:05Z
status: gaps_found
score: 54/55 must-haves verified
overrides_applied: 0
re_verification:
  previous_status: gaps_found
  previous_score: 54/55
  gaps_closed:
    - "Watcher hash-read failures now publish typed Watcher/ReadFailed results and do not stop later watcher reloads."
  gaps_remaining: []
  regressions:
    - "10-REVIEW CR-01 is true: production DI resolves ConfigurationService through its public logger-only constructor, creating a private coordinator that ConfigWatcherService does not notify."
gaps:
  - truth: "User configuration changes save and reload deterministically when app saves and external edits occur in close timing windows."
    status: failed
    reason: "The Plan 10-10 hash-read gap is closed, but the current code review CR-01 is a production wiring blocker: ConfigurationService ignores the DI-registered IConfigPersistenceCoordinator because its coordinator-taking constructor is internal. Microsoft.Extensions.DependencyInjection selects public constructors, so IConfigurationService is constructed through the public logger-only constructor, which creates a separate coordinator. ConfigWatcherService notifies the registered shared coordinator, while ConfigurationService starts and observes a private coordinator; external watcher reloads cannot update the facade in the running app."
    artifacts:
      - path: "AutoQAC/Infrastructure/ServiceCollectionExtensions.cs"
        issue: "Lines 30-33 register ConfigPersistenceCoordinator/IConfigPersistenceCoordinator and then AddSingleton<IConfigurationService, ConfigurationService>(), relying on constructor selection that cannot use ConfigurationService's internal coordinator constructor."
      - path: "AutoQAC/Services/Configuration/ConfigurationService.cs"
        issue: "Lines 42-66 contain the coordinator-taking constructor but it is internal; lines 68-79 expose the public DI-selected constructor that calls CreateDefaultCoordinator and constructs a separate StateService/file store/coordinator."
      - path: "AutoQAC.Tests/Integration/DependencyInjectionTests.cs"
        issue: "Existing DI test only asserts services resolve; it does not prove IConfigurationService and ConfigWatcherService share the same IConfigPersistenceCoordinator or that watcher notifications reach the facade."
    missing:
      - "Wire IConfigurationService explicitly to the registered IConfigPersistenceCoordinator (or make the coordinator-taking constructor public and remove the self-constructed DI path)."
      - "Add an integration test that builds the real ServiceCollection and proves ConfigWatcherService/coordinator notifications update the same IConfigurationService facade state/result/failure streams."
---

# Phase 10: Configuration Persistence Hardening Verification Report

**Phase Goal:** Users get reliable configuration saves/reloads under race conditions, and maintainers can reason about persistence through one serialized flow with lower in-memory clone cost.  
**Verified:** 2026-05-01T02:18:05Z  
**Status:** gaps_found  
**Re-verification:** Yes — after Plan 10-10 gap closure

## Goal Achievement

Plan 10-10 closes the previous verifier blocker: watcher hash reads are now wrapped inside `ApplyWatcherAsync`, publish typed `Watcher`/`ReadFailed` failure/result events, and have deterministic regression coverage proving a later valid watcher reload still applies.

However, the phase goal is still not achieved. The required advisory code review's critical finding is true in the actual codebase: production DI does not connect `ConfigurationService` and `ConfigWatcherService` to the same coordinator instance. That breaks the phase's primary external reload behavior in the running app even though the coordinator itself is now correct.

### Observable Truths

| # | Truth | Status | Evidence |
|---|---|---|---|
| 1 | User configuration changes save and reload deterministically when app saves and external edits occur in close timing windows. | ✗ FAILED | Coordinator internals now handle hash failures, but production DI is unwired. `ServiceCollectionExtensions.cs:30-33` registers a shared coordinator and `AddSingleton<IConfigurationService, ConfigurationService>()`; `ConfigurationService.cs:42-66` has the shared-coordinator constructor as `internal`, while `ConfigurationService.cs:68-79` public constructor creates a private coordinator. `ConfigWatcherService.cs:25-32` receives the DI coordinator, so watcher reloads go to a different coordinator than the facade observes. |
| 2 | User sees or receives a recoverable failure path when configuration persistence fails instead of silent logging-only fallback. | ⚠️ PARTIAL | Hash-read failures and FileSystemWatcher error signals now emit typed `Watcher/ReadFailed` results (`ConfigPersistenceCoordinator.cs:288-312`, `ConfigWatcherService.cs:82-85`). But because production watcher signals go to the wrong coordinator, those results/failures are not visible through the `IConfigurationService` facade used by Settings UI. |
| 3 | Maintainer can verify watcher race cases deterministically for debounce, deferred reload, invalid YAML, and app-save interactions. | ⚠️ PARTIAL | `Watcher_HashFailure_EmitsReadFailedAndProcessesLaterReload` exists and passed; `DependencyInjectionTests` passed but only asserts service resolution and does not cover the shared-coordinator wiring regression. |
| 4 | User configuration changes avoid YAML serialization round-trips for in-memory cloning. | ✓ VERIFIED | `UserConfiguration.Copy()` and nested copy methods remain model-owned; `ConfigurationService` normal in-memory paths use `.Copy()` rather than YAML clone round-trips. |
| 5 | Forced configuration flush is always a coordinator queue barrier, even when the facade has no pending app save. | ✓ VERIFIED | `ConfigurationService.cs:214-230` always awaits `_coordinator.FlushPendingSavesAsync(ct)` and clears `_hasPendingUserSave` only after `Success`/`NoOp`. |
| 6 | Queued watcher reload work cannot remain behind a pre-cleaning/no-pending facade flush. | ⚠️ PARTIAL | The facade no-pending bypass is fixed for its own coordinator, but production watcher work is queued to a different coordinator, so the facade flush cannot drain watcher work submitted by `ConfigWatcherService`. |
| 7 | Explicit reload cannot mask a failed pending app-save flush as a successful disk reload. | ✓ VERIFIED | `ConfigPersistenceCoordinator.cs:371-392` returns failed/rejected prerequisite flush results before reading disk. |
| 8 | Settings persistence failures are visible as a text-only recoverable banner. | ⚠️ PARTIAL | Banner wiring exists, but watcher-originated failures in production can be published on the DI coordinator rather than the facade coordinator exposed through `IConfigurationService.Failures`. |
| 9 | Pre-cleaning flush failure blocks process-launch-adjacent workflow. | ✓ VERIFIED | `CleaningPreflight` branches on failed/rejected flush results before downstream validation/game detection/plugin/MO2 work. |

**Score:** 54/55 must-haves verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|---|---|---|---|
| `AutoQAC/Models/Configuration/UserConfiguration.cs` | Public manual deep-copy methods on user config graph | ✓ VERIFIED | Parent and nested copy methods remain substantive and YAML-free for clone paths. |
| `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs` | Single-reader coordinator with typed watcher failure handling | ✓ VERIFIED | `ApplyWatcherAsync` handles `ConfigFileSignalKind.Error` and catches non-cancellation `ComputeHashAsync` exceptions, publishing `Watcher/ReadFailed` failed results. |
| `AutoQAC/Services/Configuration/ConfigurationService.cs` | Coordinator-backed facade using shared production coordinator | ✗ FAILED | The coordinator-backed constructor is `internal`; the public constructor creates a private default coordinator, so DI facade wiring is not shared with watcher. |
| `AutoQAC/Services/Configuration/ConfigWatcherService.cs` | Event-source-only watcher forwarding signals to persistence coordinator | ⚠️ PARTIAL | Changed/Created/Renamed/Deleted/Error are forwarded to its injected coordinator, but DI injects a different coordinator than the facade uses. Shutdown callback `ObjectDisposedException` can still escape per WR-01. |
| `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` | Shared coordinator registration for facade and watcher | ✗ FAILED | Registers the shared coordinator but does not construct `ConfigurationService` with it. |
| `AutoQAC.Tests/Services/Configuration/ConfigPersistenceCoordinatorTests.cs` | Deterministic race coverage | ✓ VERIFIED | Contains `Watcher_HashFailure_EmitsReadFailedAndProcessesLaterReload` and `Watcher_ErrorSignal_EmitsReadFailedWithoutReadingFile`. |
| `AutoQAC.Tests/Services/Configuration/Fakes/FakeUserConfigFileStore.cs` | Deterministic hash-failure injection seam | ✓ VERIFIED | `HashFailure` is thrown from `ComputeHashAsync` after logging `Hash`. |
| `AutoQAC.Tests/Integration/DependencyInjectionTests.cs` | Production DI wiring coverage | ⚠️ PARTIAL | Resolves services successfully, but no assertion proves shared coordinator identity or watcher-to-facade data flow. |

### Key Link Verification

| From | To | Via | Status | Details |
|---|---|---|---|---|
| `ConfigPersistenceCoordinator.ApplyWatcherAsync` | `IUserConfigFileStore.ComputeHashAsync` | try/catch around hash acquisition | ✓ WIRED | `ConfigPersistenceCoordinator.cs:298-312` catches non-cancellation hash exceptions and publishes typed failed watcher results. |
| `ConfigWatcherService` | `IConfigPersistenceCoordinator` | FSW Changed/Created/Renamed/Deleted/Error handlers | ✓ WIRED locally | `ConfigWatcherService.cs:78-85` forwards signals to the injected coordinator. |
| DI `IConfigWatcherService` | DI `IConfigPersistenceCoordinator` | constructor injection | ✓ WIRED | The watcher receives the registered coordinator. |
| DI `IConfigurationService` | DI `IConfigPersistenceCoordinator` | constructor selection | ✗ NOT_WIRED | `AddSingleton<IConfigurationService, ConfigurationService>()` cannot use the internal coordinator constructor; public constructor creates a separate default coordinator. |
| `ConfigurationService.Failures/PersistenceResults/UserConfigurationChanged` | watcher-originated events | shared coordinator observable streams | ✗ NOT_WIRED | Facade observable properties read from its private coordinator, not the coordinator that watcher notifies in production DI. |
| `CleaningPreflight` | `IConfigurationService.FlushPendingSavesAsync` | typed result branch | ✓ WIRED | Preflight receives facade flush results and blocks on failed/rejected status. |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|---|---|---|---|---|
| `ConfigPersistenceCoordinator.cs` | watcher reload candidate | hash/read via `IUserConfigFileStore` | Yes | ✓ FLOWING inside the coordinator; hash exceptions now produce typed failures. |
| `ConfigWatcherService.cs` | watcher signal | `FileSystemWatcher` events | Yes | ⚠️ HOLLOW at app level — signal flows to the registered coordinator, but not to the facade's private coordinator. |
| `ConfigurationService.cs` | `UserConfigurationChanged`, `Failures`, `PersistenceResults` | private `_coordinator` | Partial | ✗ DISCONNECTED from watcher in production DI. |
| `SettingsViewModel.cs` | persistence banner state | `IConfigurationService.Failures/PersistenceResults` | Partial | ⚠️ Watcher-originated failures can miss the facade stream due to DI mismatch. |
| `UserConfiguration.Copy()` | copied config graph | mutable `UserConfiguration` instance | Yes | ✓ FLOWING — manual deep copies remain substantive. |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|---|---|---|---|
| Watcher hash-read regression closes prior gap. | `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~ConfigPersistenceCoordinatorTests&FullyQualifiedName~Watcher_HashFailure_EmitsReadFailedAndProcessesLaterReload" --nologo` | Passed: 1 test after sequential rerun. Initial parallel run hit a build file lock while another test command was compiling. | ✓ PASS |
| Existing DI smoke tests. | `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~DependencyInjectionTests" --nologo` | Passed: 1 test. | ⚠️ PASS BUT INSUFFICIENT — test only resolves services; it does not detect the shared-coordinator wiring break. |
| Production DI watcher-to-facade data flow. | Source inspection of `ServiceCollectionExtensions.cs`, `ConfigurationService.cs`, `ConfigWatcherService.cs`. | DI constructs facade through public logger-only constructor, creating a private coordinator. | ✗ FAIL |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|---|---|---|---|---|
| REF-03 | Plans 10-02, 10-03, 10-05, 10-06, 10-07, 10-08, 10-09, 10-10 | Maintainer can reason about saves, reloads, deferrals, and failures through one serialized persistence flow. | ✗ BLOCKED | The coordinator implementation is reason-about-able, but production has two coordinators: watcher signals go to one, facade state/result/failure observers use another. |
| TEST-03 | Plans 10-02, 10-03, 10-04, 10-06, 10-07, 10-08, 10-09, 10-10 | Maintainer can verify configuration watcher race cases deterministically. | ⚠️ PARTIAL | Deterministic coordinator tests cover the race matrix including hash failures; missing deterministic DI integration test lets the production watcher/facade disconnect pass. |
| PERF-03 | Plans 10-01, 10-03 | User configuration changes avoid YAML serialization round-trips for in-memory cloning. | ✓ SATISFIED | Manual copy graph remains in use for in-memory snapshots; YAML remains disk persistence only. |

No orphaned Phase 10 requirements were found. `.planning/REQUIREMENTS.md` maps only `REF-03`, `TEST-03`, and `PERF-03` to Phase 10, and all three appear in plan frontmatter across Phase 10 plans.

### Code Review Findings Adjudication

| Review Finding | Verdict | Verification Evidence |
|---|---|---|
| CR-01: ConfigurationService ignores the DI coordinator, so watcher reloads go to an unstarted/separate coordinator | 🛑 TRUE GAP | `ConfigurationService` shared-coordinator constructor is internal; public constructor creates `CreateDefaultCoordinator`. DI registration uses `AddSingleton<IConfigurationService, ConfigurationService>()`, so production facade and watcher do not share coordinator state. |
| WR-01: FileSystemWatcher callbacks can throw during shutdown races | ⚠️ TRUE WARNING | `ConfigWatcherService.cs:78-85` directly calls `_coordinator.NotifySettingsFileChanged`; `ConfigPersistenceCoordinator.NotifySettingsFileChanged` throws after disposal. Non-blocking robustness issue unless it masks shutdown. |

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|---|---:|---|---|---|
| `AutoQAC/Services/Configuration/ConfigurationService.cs` | 42-79 | DI-relevant constructor is internal; public constructor creates service-local coordinator | 🛑 Blocker | Watcher reloads/failures are not observed by the production facade/UI/preflight path. |
| `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` | 30-33 | Registers shared coordinator but uses type registration for facade | 🛑 Blocker | The intended shared coordinator is bypassed by DI constructor selection. |
| `AutoQAC.Tests/Integration/DependencyInjectionTests.cs` | 34-39 | Resolve-only DI smoke test | ⚠️ Warning | Allows broken shared-instance/data-flow wiring to pass. |
| `AutoQAC/Services/Configuration/ConfigWatcherService.cs` | 78-85 | Watcher callbacks do not swallow disposal races | ⚠️ Warning | Queued FSW callback during shutdown can throw `ObjectDisposedException`. |

No placeholder or stub implementation was identified in the Plan 10-10 coordinator fix or fake-store test seam.

### Human Verification Required

None for this gate decision. The blocking issue is source-verifiable and should be covered by a deterministic DI integration test.

### Gaps Summary

The previous watcher hash-read blocker is closed by Plan 10-10. The phase remains blocked by a new, true code-review critical issue: the production DI graph registers a shared coordinator for `ConfigWatcherService`, but `ConfigurationService` is constructed through a public constructor that creates a private coordinator. This means external settings file changes and watcher-originated failures are processed by the wrong coordinator and do not update the facade/UI streams that Phase 10 was meant to harden. Fix the DI wiring and add an integration regression proving watcher signals reach the same `IConfigurationService` facade.

---

_Verified: 2026-05-01T02:18:05Z_  
_Verifier: the agent (gsd-verifier)_
