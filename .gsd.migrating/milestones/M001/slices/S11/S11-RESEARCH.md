# Phase 10: Configuration Persistence Hardening - Research

**Researched:** 2026-04-30  
**Domain:** .NET desktop configuration persistence, filesystem watcher coordination, deterministic async race testing  
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

#### Persistence Authority
- **D-01:** Add an internal service-owned serialized persistence coordinator behind the configuration/watcher APIs. Do not settle for lock cleanup only.
- **D-02:** Route debounced saves, forced flushes, watcher reloads, deferrals, validation, and failure handling through an explicit sequential queue.
- **D-03:** A persistence-slice rewrite of `IConfigurationService` is allowed, but the rewrite boundary is save/load/flush/reload/status. Do not reorganize skip-list/game-query helpers unless required by compile-time changes.
- **D-04:** Normal app save requests coalesce to the latest pending `UserConfiguration`; intermediate settings UI states do not need to be written.
- **D-05:** Forced flush is a queue barrier: it writes the latest app config immediately and returns a success/failure result before callers continue.
- **D-06:** The coordinator publishes recoverable failures only, not a noisy success event for every persistence operation.
- **D-07:** On shutdown/disposal, attempt a final barrier flush of the latest app config and log failure without hanging shutdown indefinitely.
- **D-08:** `ConfigWatcherService` becomes an event source only. It detects file/cleaning transitions and submits operations to the queue; it must not independently decide ordering.
- **D-09:** Use versions/generations alongside content hashes so stale saves and watcher observations can be rejected deterministically.
- **D-10:** Atomic save behavior is in scope: write to a temp file then replace/move so partial settings files are less likely after save failure.
- **D-11:** Reloads validate external YAML into a candidate before replacing in-memory config or emitting `UserConfigurationChanged`.
- **D-12:** User setting edits remain optimistic: services/UI may see the new config immediately, but save failure rolls back to last known good with a failure signal.
- **D-13:** Keep normal save debounce behavior for user edits; forced flush bypasses debounce through the queue barrier.
- **D-14:** If a queued reload is reached after a newer app save arrives, re-evaluate it by version/hash and reject it if stale or racing app intent.
- **D-15:** The persistence queue owns only `AutoQAC Settings.yaml` user config behavior. Do not turn Phase 10 into a main-config cache redesign.

#### External Edit Races
- **D-16:** If an external edit arrives while an app save is pending, reject that racing external edit. App save wins the close-timing race.
- **D-17:** A later external reload is valid only when a new watcher/content version is observed after the app save version settles.
- **D-18:** During cleaning, apply at most one deferred external edit after cleaning ends, using the latest valid external content observed during cleaning.
- **D-19:** If the latest deferred cleaning-time edit is invalid YAML, reject the deferred reload and keep the current active config. Do not apply an earlier valid deferred snapshot.
- **D-20:** Watcher events that echo an app-written hash/version are silent skips: debug log only, no user-visible status.
- **D-21:** If an app save fails after a racing external edit was rejected, that rejected external edit stays rejected. Active config returns to last known good until a later new external edit or user action.
- **D-22:** If the settings file is deleted or temporarily missing during watcher reload, keep the current active config, record/log a recoverable reload failure, and wait for a later valid file event. Do not silently reset to defaults.
- **D-23:** Expand watcher event handling beyond `Changed`; treat `Changed`, `Created`, `Renamed`, and `Deleted`/recreate patterns as signals to re-check current settings file content.

#### Failure Surfacing
- **D-24:** Expose recoverable persistence failures through a typed observable/status stream from the configuration layer. ViewModels decide concise text presentation.
- **D-25:** User-visible failures are action blockers: pre-clean flush failures, failed user-initiated saves/reloads, and invalid external YAML rejections. Harmless app-write echoes stay silent.
- **D-26:** A required pre-cleaning flush failure blocks cleaning. Do not launch xEdit; show concise configuration-save failure and keep previous good config active.
- **D-27:** The next successful save/reload/flush clears the active failure signal and any ViewModel status derived from it.
- **D-28:** Typed failure payloads carry safe summary data only: operation type, safe reason category, recovery state, and optional log-reference text. Do not expose stack traces or raw exception objects to ViewModels.
- **D-29:** Invalid external YAML rejection should be status/banner text only, not a modal dialog.
- **D-30:** After optimistic UI changes roll back because save failed, the UI/status should explicitly say settings were restored to last saved values.
- **D-31:** Do not add new retry UI in Phase 10. Recovery happens through the next valid persistence operation.

#### Deterministic Tests
- **D-32:** Primary race coverage uses the persistence coordinator seam directly by submitting save/reload/defer operations without real watcher timing.
- **D-33:** Keep real `FileSystemWatcher` coverage as smoke tests only; move race proof out of timing-dependent watcher tests.
- **D-34:** Tests should be outcome-first: assert final active config, on-disk config, failure signal, and critical ordering such as flush-before-preflight. Do not assert every internal queue step unless it is the protected behavior.
- **D-35:** Add targeted guards for YAML-free clone paths and deterministic race tests avoiding production throttle waits.
- **D-36:** Use a small internal file I/O seam/fake to deterministically inject write, replace, read, hash, missing-file, and invalid-content failures.
- **D-37:** Pre-cleaning flush failure coverage may use a mock boundary to prove the cleaning service/process launch path is not called.
- **D-38:** Race matrix scope is the SPEC acceptance matrix: save/flush/reload overlap, app-save external edit, cleaning deferral, invalid/missing YAML, and recovery.
- **D-39:** New deterministic tests must not wait on the production 500 ms watcher throttle or arbitrary sleeps. Existing real watcher smoke tests may keep bounded waits.

#### Clone Replacement
- **D-40:** Replace YAML-based clone with manual deep-copy behavior. Do not add a clone library or immutable model refactor in this phase.
- **D-41:** Put clone/copy behavior near the models as public methods on `UserConfiguration` and nested config models.
- **D-42:** Protect clone maintenance with behavior tests rather than reflection/source mapping guards for every property.
- **D-43:** Ban YAML serialization/deserialization from normal clone paths only. YAML remains valid for actual disk persistence.
- **D-44:** Manual clone should normalize null nested config objects/collections to default empty instances for caller safety.
- **D-45:** Manual clone must deep-copy all mutable containers, including dictionaries whose values are currently immutable strings.
- **D-46:** Proving no YAML round-trip in the normal clone path is enough for PERF-03; do not add a benchmark project or timing comparison in Phase 10.

### the agent's Discretion
- Exact coordinator/interface names, queue implementation details, result DTO names, status enum names, clone method names, and test class organization are left to research/planning as long as the decisions above and `10-SPEC.md` are preserved.

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope. Broader app-data/path-provider centralization, settings UI redesign, diagnostics redaction, and main configuration behavior changes remain excluded by `10-SPEC.md` unless a compile-time adjustment is unavoidable.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| REF-03 | Maintainer can reason about configuration saves, reloads, deferrals, and failures through one serialized persistence flow. | Use a single service-owned coordinator with a FIFO async queue and generation/hash state transitions. [VERIFIED: `.planning/REQUIREMENTS.md`; `10-CONTEXT.md`; Microsoft Channels docs] |
| TEST-03 | Maintainer can verify configuration watcher race cases deterministically. | Move race proof to coordinator tests with fake file I/O and direct operation submission; keep `FileSystemWatcher` tests as smoke only. [VERIFIED: `10-SPEC.md`; `AutoQAC.Tests/Services/ConfigWatcherServiceTests.cs`] |
| PERF-03 | User configuration changes avoid YAML serialization round-trips for in-memory cloning. | Add model-owned manual deep-copy methods and prove clone paths do not call YamlDotNet serialization/deserialization. [VERIFIED: `10-SPEC.md`; `AutoQAC/Services/Configuration/ConfigurationService.cs`; `UserConfiguration.cs`] |
</phase_requirements>

## Summary

Phase 10 should be implemented as a narrow persistence-slice rewrite, not as another lock-only patch. The established pattern for this problem in .NET is an explicit producer/consumer queue with one consumer that owns state transitions, while producers submit app-save, flush-barrier, watcher-signal, cleaning-transition, and reload operations. `System.Threading.Channels` is part of the .NET shared framework for .NET Core 3.0+ and provides asynchronous FIFO producer/consumer primitives with configurable single-reader/multiple-writer behavior. [CITED: https://learn.microsoft.com/dotnet/core/extensions/channels]

The current code splits responsibility between `ConfigurationService` and `ConfigWatcherService`: debounced Rx saves, `_pendingConfig`, `_lastKnownGoodConfig`, `_lastWrittenHash`, direct watcher reload decisions, and YAML-based `CloneConfig`. This is the race source Phase 10 targets. [VERIFIED: `AutoQAC/Services/Configuration/ConfigurationService.cs`; `AutoQAC/Services/Configuration/ConfigWatcherService.cs`; `.planning/codebase/CONCERNS.md`] The planner should introduce one internal persistence coordinator for `AutoQAC Settings.yaml`, one internal file I/O seam for deterministic tests, and typed result/status records for recoverable failures. [VERIFIED: `10-CONTEXT.md`; `10-SPEC.md`]

**Primary recommendation:** Use an internal single-reader `Channel<ConfigPersistenceOperation>` coordinator behind `IConfigurationService`, keep `ConfigWatcherService` as an event source, save via temp-file + `File.Replace`/`File.Move`, and replace YAML clone with public model-owned `Copy()`/`DeepClone()` methods. [CITED: https://learn.microsoft.com/dotnet/core/extensions/channels; https://learn.microsoft.com/dotnet/api/system.io.file.replace?view=net-10.0]

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|--------------|----------------|-----------|
| App user-config save/debounce/flush/reload ordering | Application Services | Filesystem | Configuration persistence is singleton service state and file I/O; ViewModels submit intent only. [VERIFIED: `AGENTS.md`; `ARCHITECTURE.md`] |
| Watcher event detection | Application Services | Filesystem | `FileSystemWatcher` should detect `Changed`/`Created`/`Deleted`/`Renamed` and submit current-file observations; it must not own ordering. [CITED: https://learn.microsoft.com/dotnet/api/system.io.filesystemwatcher?view=net-10.0; VERIFIED: `10-CONTEXT.md`] |
| Recoverable persistence failure UX | ViewModels | Application Services | Services publish typed safe status; ViewModels map to concise text/status without raw exceptions or dialogs in services. [VERIFIED: `AGENTS.md`; `10-CONTEXT.md`] |
| Pre-cleaning flush barrier | Application Services | Cleaning workflow | `CleaningPreflight.PrepareAsync` already calls `FlushPendingSavesAsync` before validation; Phase 10 must make the return path failure-aware. [VERIFIED: `CleaningPreflight.cs`; `ARCHITECTURE.md`] |
| YAML disk compatibility | Filesystem / Serialization | Domain Models | YamlDotNet remains disk serializer; model aliases must remain compatible. [VERIFIED: `UserConfiguration.cs`; CITED: `/aaubry/yamldotnet`] |
| YAML-free in-memory cloning | Domain Models | Application Services | Mutable config objects need model-owned deep copies so services can protect state without serialization round-trips. [VERIFIED: `10-CONTEXT.md`; `UserConfiguration.cs`] |

## Project Constraints (from AGENTS.md)

- AutoQAC is Windows-only and targets `net10.0-windows10.0.19041.0`; preserve Windows filesystem/process assumptions. [VERIFIED: `AGENTS.md`; `AutoQAC/AutoQAC.csproj`]
- Maintain strict MVVM boundaries; ViewModels must not manipulate controls/dialogs directly and should use service status/interactions. [VERIFIED: `AGENTS.md`]
- Use CommunityToolkit.Mvvm generators in ViewModels; do not add ReactiveUI/System.Reactive to ViewModel layer. Service `IObservable<T>` streams are acceptable when marshaled through `IUiDispatcher`. [VERIFIED: `AGENTS.md`]
- Keep I/O async; never block UI thread with `.Result` or `.Wait()`. [VERIFIED: `AGENTS.md`]
- Use constructor injection through `ServiceCollectionExtensions`; avoid static mutable state and service locators. [VERIFIED: `AGENTS.md`]
- Sequential cleaning is a hard requirement; do not parallelize plugin cleaning or xEdit launches. [VERIFIED: `AGENTS.md`; `STATE.md`]
- Do not bypass `FlushPendingSavesAsync` before launching xEdit. [VERIFIED: `AGENTS.md`; `CleaningPreflight.cs`]
- Do not modify `Mutagen/`. [VERIFIED: `AGENTS.md`]
- Use NSubstitute for mocks and match optional parameters explicitly. [VERIFIED: `AGENTS.md`; `TESTING.md`]
- There is no Avalonia.Headless test project; do not depend on one. [VERIFIED: `AGENTS.md`; `TESTING.md`]
- Never delete comments as cleanup; add XML doc comments for new/substantially rewritten methods, and explain non-obvious WHY. [VERIFIED: global `AGENTS.md`]

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET / C# | SDK 10.0.100 detected | Runtime, async I/O, `System.Threading.Channels`, `System.IO` APIs | Existing app targets .NET 10; Channels are included in shared framework on .NET Core 3.0+. [VERIFIED: `dotnet --version`; CITED: https://learn.microsoft.com/dotnet/core/extensions/channels] |
| System.Threading.Channels | Shared framework in .NET 10; NuGet latest preview 11.0.0-preview.3.26207.106 | Internal FIFO single-reader persistence queue | Official producer/consumer abstraction with async writes/reads and single-reader options; no extra package needed for .NET 10 app. [CITED: https://learn.microsoft.com/dotnet/core/extensions/channels; VERIFIED: NuGet flat-container API] |
| YamlDotNet | Existing 16.3.0; NuGet latest 17.1.0 | YAML disk serialization/deserialization only | Existing config compatibility relies on `YamlMember` aliases and `DeserializerBuilder.IgnoreUnmatchedProperties`; do not use for cloning. [VERIFIED: `dotnet list package`; VERIFIED: NuGet flat-container API; CITED: `/aaubry/yamldotnet`] |
| System.IO.FileSystemWatcher | net10.0 BCL | External settings-file signal source | Official API raises Changed/Created/Deleted/Renamed/Error events; Phase 10 should consume it as unreliable signal, not authority. [CITED: https://learn.microsoft.com/dotnet/api/system.io.filesystemwatcher?view=net-10.0] |
| System.IO.File.Replace / File.Move | net10.0 BCL | Temp-file then replace/move atomic-ish save | `File.Replace` replaces destination with source and can create backup; it requires destination to exist and same volume, so use `File.Move(temp, path, overwrite: false/true)` fallback when creating missing file. [CITED: https://learn.microsoft.com/dotnet/api/system.io.file.replace?view=net-10.0] |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.Reactive | Present via existing package graph; NuGet latest 7.0.0-preview.1 | Existing service observables and watcher debounce | Keep existing `IObservable<T>` public streams; avoid adding new Rx-based ordering logic for Phase 10 queue authority. [VERIFIED: code grep; NuGet flat-container API; `AGENTS.md`] |
| xUnit | 2.9.3 | Unit/integration tests | Existing test runner. [VERIFIED: `dotnet list package`; `TESTING.md`] |
| FluentAssertions | Existing 8.8.0; NuGet latest 8.9.0 | Behavior-first assertions | Existing dominant assertion style; use because-clauses for race outcomes. [VERIFIED: `dotnet list package`; NuGet flat-container API; `TESTING.md`] |
| NSubstitute | Existing 5.3.0; latest stable 5.3.0 (6.0.0 is RC) | Service boundary mocks | Existing mock framework; match optional `CancellationToken` parameters explicitly. [VERIFIED: `dotnet list package`; NuGet flat-container API; `AGENTS.md`] |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `Channel<T>` queue | `SemaphoreSlim` around every method | Locks serialize critical sections but do not naturally model debounce, barrier flush, stale operation rejection, and operation result completion. [VERIFIED: current code; `10-CONTEXT.md`] |
| `Channel<T>` queue | Rx `Throttle`/`Switch` pipeline | Existing Rx pipeline handles debounce but not cross-source ordering between watcher reloads, forced flushes, failures, and cleaning deferrals. [VERIFIED: `ConfigurationService.cs`; `ConfigWatcherService.cs`] |
| Manual `Copy()` methods | YAML serialize/deserialize clone | Current clone path causes YAML round-trip overhead and is explicitly banned for normal clone paths. [VERIFIED: `ConfigurationService.cs`; `10-CONTEXT.md`] |
| Manual `Copy()` methods | Reflection/deep-clone library | Phase decision forbids adding clone library or immutable model refactor; model-owned copy is simpler and testable. [VERIFIED: `10-CONTEXT.md`] |

**Installation:**
```bash
# No new production package required for the recommended queue/filesystem approach.
# Keep existing packages; do not upgrade YamlDotNet in Phase 10 unless implementation proves a compile/runtime need.
```

**Version verification:** Current package versions were verified with `dotnet list AutoQACSharp.slnx package`; NuGet latest versions were checked through the NuGet flat-container API on 2026-04-30. [VERIFIED: command output]

## Architecture Patterns

### System Architecture Diagram

```text
Settings UI / service caller
        |
        | SaveUserConfigAsync(config) / FlushPendingSavesAsync()
        v
IConfigurationService facade
        |
        | submits SaveIntent / FlushBarrier / ReloadRequest
        v
ConfigPersistenceCoordinator (single-reader queue)
        |
        +--> coalesce pending app saves by generation
        +--> validate reload candidate before state replacement
        +--> reject stale watcher generation/hash races
        +--> defer latest cleaning-time external candidate
        +--> publish typed recoverable failure only on actionable failures
        |
        v
IUserConfigFileStore seam
        |
        +--> read YAML text + hash
        +--> write temp YAML
        +--> File.Replace(existing) or File.Move(create)
        v
AutoQAC Data/AutoQAC Settings.yaml

FileSystemWatcher + StateChanged
        |
        | Changed/Created/Renamed/Deleted or cleaning transition signal
        v
ConfigWatcherService event source only
        |
        | submits WatcherObserved / CleaningStateChanged
        v
same coordinator queue
```

### Recommended Project Structure

```text
AutoQAC/
├── Models/Configuration/
│   ├── UserConfiguration.cs          # public Copy()/DeepClone() on config graph
│   ├── BackupSettings.cs             # Copy() for nested model
│   └── RetentionSettings.cs          # Copy() for nested model
├── Services/Configuration/
│   ├── IConfigurationService.cs      # facade plus typed failure/status observable/result changes
│   ├── ConfigurationService.cs       # delegates persistence slice to coordinator; retains main config/skip helpers
│   ├── ConfigPersistenceCoordinator.cs
│   ├── ConfigPersistenceOperation.cs # internal records/enums for save/flush/reload/watcher transitions
│   ├── ConfigPersistenceStatus.cs    # safe typed failure/status payloads
│   ├── IUserConfigFileStore.cs       # read/write/hash/replace seam
│   └── UserConfigFileStore.cs        # real async filesystem + YamlDotNet disk persistence
└── Infrastructure/
    └── ServiceCollectionExtensions.cs # singleton registrations

AutoQAC.Tests/
├── Services/
│   ├── ConfigPersistenceCoordinatorTests.cs
│   ├── ConfigurationServiceTests.cs
│   └── ConfigWatcherServiceTests.cs  # smoke only for real watcher delivery
└── Models/
    └── UserConfigurationCopyTests.cs
```

### Pattern 1: Single-reader async coordinator

**What:** Multiple producers submit immutable operation records to a `Channel<T>`; exactly one background consumer owns active config, last-known-good config, app-save generation, watcher generation/hash, deferred external candidate, and failure state. Channels are documented as FIFO producer/consumer synchronization structures with async producer and consumer APIs. [CITED: https://learn.microsoft.com/dotnet/core/extensions/channels]

**When to use:** Use for all save/debounce/flush/reload/watcher/defer decisions in Phase 10. [VERIFIED: `10-CONTEXT.md`]

**Example:**
```csharp
// Source: Microsoft Channels docs + Phase 10 decisions.
private readonly Channel<ConfigPersistenceOperation> _operations =
    Channel.CreateUnbounded<ConfigPersistenceOperation>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });

private async Task RunAsync(CancellationToken stoppingToken)
{
    await foreach (var operation in _operations.Reader.ReadAllAsync(stoppingToken))
    {
        await ApplyOperationAsync(operation, stoppingToken).ConfigureAwait(false);
    }
}
```

### Pattern 2: Barrier operations complete with result DTOs

**What:** `FlushPendingSavesAsync` must submit a barrier operation carrying a `TaskCompletionSource<ConfigPersistenceResult>` and await it; the consumer writes the latest pending app config immediately, then completes success/failure. [VERIFIED: `10-CONTEXT.md`; `CleaningPreflight.cs`]

**When to use:** Use for pre-cleaning flush, shutdown flush, and tests that need deterministic ordering. [VERIFIED: `10-SPEC.md`]

**Example:**
```csharp
// Source: Phase 10 D-05 and existing TaskCompletionSource test patterns.
public async Task<ConfigPersistenceResult> FlushPendingSavesAsync(CancellationToken ct = default)
{
    var completion = new TaskCompletionSource<ConfigPersistenceResult>(
        TaskCreationOptions.RunContinuationsAsynchronously);

    await _operations.Writer.WriteAsync(new FlushBarrierOperation(completion), ct)
        .ConfigureAwait(false);

    return await completion.Task.WaitAsync(ct).ConfigureAwait(false);
}
```

### Pattern 3: Watcher as signal, file content as truth

**What:** Register `Changed`, `Created`, `Deleted`, `Renamed`, and `Error` handlers, but submit only “settings file may have changed” operations; the coordinator re-reads current content/hash and decides. The official `FileSystemWatcher` API exposes those events and an error event. [CITED: https://learn.microsoft.com/dotnet/api/system.io.filesystemwatcher?view=net-10.0]

**When to use:** Use for external edits, delete/recreate saves from editors, and app-write echo filtering. [VERIFIED: `10-CONTEXT.md`]

**Example:**
```csharp
// Source: Microsoft FileSystemWatcher docs, adapted to Phase 10 event-source boundary.
watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;
watcher.Changed += (_, _) => _coordinator.NotifySettingsFileChanged(ConfigFileSignalKind.Changed);
watcher.Created += (_, _) => _coordinator.NotifySettingsFileChanged(ConfigFileSignalKind.Created);
watcher.Deleted += (_, _) => _coordinator.NotifySettingsFileChanged(ConfigFileSignalKind.Deleted);
watcher.Renamed += (_, _) => _coordinator.NotifySettingsFileChanged(ConfigFileSignalKind.Renamed);
watcher.Error += (_, e) => _coordinator.NotifyWatcherError(e.GetException());
```

### Pattern 4: Validate candidate before commit

**What:** Read YAML into a candidate `UserConfiguration`, normalize/copy it, then replace active/last-known-good state only after deserialization succeeds. YamlDotNet supports deserializing object graphs, `YamlMember` aliases, and `IgnoreUnmatchedProperties`. [CITED: `/aaubry/yamldotnet`]

**When to use:** Use for reloads, deferred reloads, startup loads, and tests injecting invalid YAML. [VERIFIED: `10-SPEC.md`]

### Pattern 5: Manual model-owned deep copy

**What:** Add `Copy()` methods to `UserConfiguration` and nested config classes; copy dictionaries/lists and normalize null nested objects/collections. [VERIFIED: `10-CONTEXT.md`; `UserConfiguration.cs`]

**When to use:** Use for every in-memory return/emission/state assignment. YAML serialization remains disk-only. [VERIFIED: `10-CONTEXT.md`]

**Example:**
```csharp
// Source: Phase 10 D-40..D-45; model shape verified in UserConfiguration.cs.
public UserConfiguration Copy() => new()
{
    SelectedGame = SelectedGame,
    LoadOrder = (LoadOrder ?? new LoadOrderConfig()).Copy(),
    LoadOrderFileOverrides = new Dictionary<string, string>(LoadOrderFileOverrides ?? []),
    ModOrganizer = (ModOrganizer ?? new ModOrganizerConfig()).Copy(),
    XEdit = (XEdit ?? new XEditConfig()).Copy(),
    Settings = (Settings ?? new AutoQacSettings()).Copy(),
    SkipLists = (SkipLists ?? [])
        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToList() ?? [], StringComparer.Ordinal),
    GameDataFolderOverrides = new Dictionary<string, string>(GameDataFolderOverrides ?? []),
    LogRetention = (LogRetention ?? new RetentionSettings()).Copy(),
    Backup = (Backup ?? new BackupSettings()).Copy()
};
```

### Anti-Patterns to Avoid

- **Adding more ad hoc locks:** Current `_fileLock`/`_stateLock` design still lets watcher reload and debounced saves make independent ordering decisions. Use one operation authority. [VERIFIED: `ConfigurationService.cs`; `10-CONTEXT.md`]
- **Using real `FileSystemWatcher` timing as race proof:** Existing tests wait for signals/timeouts; Phase 10 requires deterministic coordinator tests without production debounce sleeps. [VERIFIED: `ConfigWatcherServiceTests.cs`; `10-SPEC.md`]
- **Exposing raw exceptions to ViewModels:** Failure payloads must carry safe categories/summary only. [VERIFIED: `10-CONTEXT.md`; `AGENTS.md`]
- **Resetting defaults on missing/deleted settings file:** Requirement says keep current active config and surface recoverable reload failure. [VERIFIED: `10-CONTEXT.md`; `10-SPEC.md`]
- **Changing main config/path-provider behavior:** Out of scope unless compile-time required. [VERIFIED: `10-SPEC.md`]

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Async operation serialization | Custom thread, busy loop, `ConcurrentQueue` polling | `System.Threading.Channels` | Official async FIFO producer/consumer primitive with backpressure/completion semantics. [CITED: https://learn.microsoft.com/dotnet/core/extensions/channels] |
| YAML parser/serializer | Custom YAML parsing or string patching | Existing YamlDotNet for disk persistence | Existing aliases and `IgnoreUnmatchedProperties` preserve compatibility. [CITED: `/aaubry/yamldotnet`; VERIFIED: `UserConfiguration.cs`] |
| Atomic replacement | Direct overwrite with `File.WriteAllTextAsync(path, content)` | Temp file + `File.Replace` when destination exists, `File.Move` when creating | Direct overwrite risks partial settings files; `File.Replace` is designed to replace one file with another and optionally back up the old file. [CITED: https://learn.microsoft.com/dotnet/api/system.io.file.replace?view=net-10.0] |
| Race testing | Sleeps/timeouts around real watcher/debounce | Coordinator tests + fake file store + TCS | Product races are ordering policies, not OS event timing; tests need deterministic operation order. [VERIFIED: `10-CONTEXT.md`; `TESTING.md`] |
| Deep clone | Reflection clone library or YAML round-trip | Public model-owned `Copy()` methods | Phase explicitly forbids clone library and YAML clone; model copy is visible and behavior-testable. [VERIFIED: `10-CONTEXT.md`] |

**Key insight:** The hard part is not file I/O; it is ordering intent from competing producers. Make ordering explicit in one queue and make filesystem/watcher behavior test-doubleable. [VERIFIED: `10-SPEC.md`; current code inspection]

## Common Pitfalls

### Pitfall 1: Treating FileSystemWatcher events as authoritative
**What goes wrong:** Delete/recreate or editor save patterns produce `Created`/`Renamed`/`Deleted`, not just `Changed`; duplicate or missing signals cause stale decisions. [CITED: https://learn.microsoft.com/dotnet/api/system.io.filesystemwatcher?view=net-10.0; VERIFIED: `10-CONTEXT.md`]  
**Why it happens:** Watcher code currently listens only to `Changed` and decides reload/hash behavior itself. [VERIFIED: `ConfigWatcherService.cs`]  
**How to avoid:** Treat all relevant watcher events as “re-check current settings file” and push the decision into the coordinator. [VERIFIED: `10-CONTEXT.md`]  
**Warning signs:** Tests assert `ReloadFromDiskAsync` calls from watcher timing instead of final config/failure outcomes. [VERIFIED: `ConfigWatcherServiceTests.cs`]

### Pitfall 2: Flush returns before disk truth is known
**What goes wrong:** Pre-cleaning proceeds after logging-only save failure, launching xEdit with stale/unpersisted settings. [VERIFIED: `10-SPEC.md`; `ConfigurationService.cs`]  
**Why it happens:** Current `SaveToDiskWithRetryAsync` catches final write failure, logs/reverts, but `FlushPendingSavesAsync` does not return a success/failure result. [VERIFIED: `ConfigurationService.cs`]  
**How to avoid:** Make flush a queue barrier returning typed result; update preflight to block on failure. [VERIFIED: `10-CONTEXT.md`]  
**Warning signs:** `CleaningPreflight` still awaits `Task` only and cannot distinguish no-pending, saved, and failed. [VERIFIED: `CleaningPreflight.cs`]

### Pitfall 3: Replacing YAML clone but missing nested mutable containers
**What goes wrong:** A caller mutates a returned config and corrupts internal service state or other readers. [VERIFIED: existing clone independence tests]  
**Why it happens:** `SkipLists` is `Dictionary<string,List<string>>`; dictionaries and nested lists must be deep-copied. [VERIFIED: `UserConfiguration.cs`]  
**How to avoid:** Copy all nested models, dictionaries, and list values; add behavior tests for mutation independence and null normalization. [VERIFIED: `10-CONTEXT.md`]  
**Warning signs:** Copy test mutating `SkipLists["SSE"]` changes later reads. [VERIFIED: `ConfigurationServiceTests.cs`]

### Pitfall 4: Completing channel operations inline under locks
**What goes wrong:** Continuations run inside coordinator state mutation and can deadlock or re-enter service code unexpectedly. [ASSUMED]  
**Why it happens:** `TaskCompletionSource` defaults may run continuations synchronously. [ASSUMED]  
**How to avoid:** Use `TaskCreationOptions.RunContinuationsAsynchronously` for operation completions; existing tests already use this pattern. [VERIFIED: `TESTING.md`; `ConfigurationServiceTests.cs`]  
**Warning signs:** Deadlocks/flaky tests when a completion handler immediately calls back into configuration service. [ASSUMED]

### Pitfall 5: Updating ViewModel UI from service thread
**What goes wrong:** Avalonia binding/thread exceptions or flaky UI state. [VERIFIED: `AGENTS.md`; `ARCHITECTURE.md`]  
**Why it happens:** Service observables publish from background coordinator/watcher tasks. [VERIFIED: current service patterns]  
**How to avoid:** Services publish typed status; ViewModels subscribe via `CallbackObserver<T>` and marshal through `IUiDispatcher`. [VERIFIED: `AGENTS.md`; `ARCHITECTURE.md`]  
**Warning signs:** New ViewModel imports `System.Reactive` or updates bound properties directly in subscription callback. [VERIFIED: `AGENTS.md`]

## Code Examples

Verified patterns from official sources and project code:

### Single-reader queue setup
```csharp
// Source: https://learn.microsoft.com/dotnet/core/extensions/channels
var channel = Channel.CreateUnbounded<ConfigPersistenceOperation>(
    new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });
```

### Temp-file replacement
```csharp
// Source: https://learn.microsoft.com/dotnet/api/system.io.file.replace?view=net-10.0
await File.WriteAllTextAsync(tempPath, yaml, ct).ConfigureAwait(false);

if (File.Exists(settingsPath))
{
    File.Replace(tempPath, settingsPath, destinationBackupFileName: null);
}
else
{
    File.Move(tempPath, settingsPath);
}
```

### YamlDotNet remains disk-only
```csharp
// Source: /aaubry/yamldotnet docs; existing project pattern in ConfigurationService.cs.
var deserializer = new DeserializerBuilder()
    .WithNamingConvention(NullNamingConvention.Instance)
    .IgnoreUnmatchedProperties()
    .Build();

var candidate = deserializer.Deserialize<UserConfiguration>(yaml);
```

### Deterministic operation test shape
```csharp
// Source: existing TCS test pattern in AutoQAC.Tests plus Phase 10 D-32/D-36.
[Fact]
public async Task FlushBarrier_WhenWriteFails_CompletesFailureAndKeepsLastKnownGood()
{
    var fileStore = new FakeUserConfigFileStore
    {
        WriteFailure = new IOException("locked")
    };
    var sut = CreateCoordinator(fileStore);

    await sut.SaveUserConfigAsync(changedConfig);
    var result = await sut.FlushPendingSavesAsync();

    result.Status.Should().Be(ConfigPersistenceStatusKind.Failed);
    (await sut.LoadUserConfigAsync()).Settings.CleaningTimeout.Should().Be(123);
    sut.LastFailure.Should().NotBeNull();
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Rx `Throttle`/`Switch` plus locks for debounced save | Explicit async producer/consumer queue for all persistence intents | Phase 10 requirement; Channels docs current for .NET 10 | Queue gives deterministic cross-source ordering and barrier results. [VERIFIED: `10-SPEC.md`; CITED: Channels docs] |
| Watcher reload decides hash/deferral independently | Watcher submits signal; coordinator re-reads and validates candidate | Phase 10 requirement | Prevents stale reloads from overwriting app intent. [VERIFIED: `10-CONTEXT.md`] |
| YAML round-trip clone | Manual model-owned deep copy | Phase 10 requirement | Removes serialization cost from normal in-memory clones. [VERIFIED: `10-SPEC.md`] |
| Direct `File.WriteAllTextAsync` overwrite | Temp-file write then replace/move | Phase 10 requirement; BCL API current | Reduces partial-file risk. [VERIFIED: `ConfigurationService.cs`; CITED: File.Replace docs] |
| Timing-dependent watcher race tests | Deterministic coordinator tests with fake file I/O | Phase 10 requirement | Tests can assert race matrix without sleeping 500 ms debounce. [VERIFIED: `10-SPEC.md`] |

**Deprecated/outdated:**
- Treating YAML serialization as acceptable for cloning is outdated for this phase; PERF-03 explicitly rejects normal clone round-trips. [VERIFIED: `10-SPEC.md`]
- Treating `FileSystemWatcher.Changed` as the only edit signal is insufficient; official API includes Created/Deleted/Renamed and Phase 10 locks those patterns in scope. [CITED: FileSystemWatcher docs; VERIFIED: `10-CONTEXT.md`]

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | Channel operation completions can deadlock/re-enter if continuations run inline under locks. | Common Pitfalls | If overstated, `RunContinuationsAsynchronously` is still harmless but may be seen as unnecessary complexity. |
| A2 | Deadlocks/flaky tests can appear when a completion handler immediately calls back into the configuration service. | Common Pitfalls | Planner may over-prioritize continuation scheduling tests. |
| A3 | Prefer changing `FlushPendingSavesAsync` to return `Task<ConfigPersistenceResult>` if call-site churn is manageable; otherwise add a result-returning companion wrapper. | Open Questions | Planner may choose an interface strategy that causes more compile churn than necessary. |

## Open Questions

1. **Should `IConfigurationService.FlushPendingSavesAsync` signature change or add a parallel result-returning method?**
   - What we know: Phase requires forced flush to return success/failure before callers continue. [VERIFIED: `10-CONTEXT.md`]
   - What's unclear: Existing interface currently returns `Task`, and changing it affects callers/tests. [VERIFIED: `IConfigurationService.cs`]
   - Recommendation: Prefer `Task<ConfigPersistenceResult> FlushPendingSavesAsync(...)` if call-site churn is manageable; otherwise add `FlushPendingSavesWithResultAsync` and keep old method as compatibility wrapper only if required. [ASSUMED]

2. **How much ViewModel wiring belongs in Phase 10?**
   - What we know: typed failure stream must exist; ViewModels decide concise text presentation. [VERIFIED: `10-CONTEXT.md`]
   - What's unclear: Exact status property/banner location is discretionary. [VERIFIED: `10-CONTEXT.md`]
   - Recommendation: Add minimal status mapping in the existing configuration/settings-facing ViewModel only; do not create new UI screens. [VERIFIED: `10-SPEC.md`]

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|-------------|-----------|---------|----------|
| .NET SDK | build/test and .NET 10 shared framework APIs | ✓ | 10.0.100 | None needed. [VERIFIED: `dotnet --version`] |
| NuGet.org access | version verification only | ✓ | API reachable 2026-04-30 | Existing locked package versions are sufficient. [VERIFIED: NuGet flat-container API] |
| xUnit test infrastructure | deterministic coordinator/service tests | ✓ | xUnit 2.9.3; Microsoft.NET.Test.Sdk 18.0.1 | None. [VERIFIED: `dotnet list package`] |
| FileSystemWatcher | smoke watcher tests/runtime watcher source | ✓ | BCL net10.0 | Coordinator direct tests cover race proof if watcher events are flaky. [CITED: FileSystemWatcher docs] |

**Missing dependencies with no fallback:** None. [VERIFIED: environment probes and package list]

**Missing dependencies with fallback:** None. [VERIFIED: environment probes and package list]

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3, FluentAssertions 8.8.0, NSubstitute 5.3.0, Microsoft.NET.Test.Sdk 18.0.1 [VERIFIED: `dotnet list package`] |
| Config file | `AutoQAC.Tests/AutoQAC.Tests.csproj` (coverage and package settings embedded) [VERIFIED: file read] |
| Quick run command | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~Configuration` |
| Full suite command | `dotnet test AutoQACSharp.slnx` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|--------------|
| REF-03 | Save, flush, reload, defer, and failure pass through one serialized authority | unit/coordinator | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~ConfigPersistenceCoordinatorTests` | ❌ Wave 0 |
| TEST-03 | Debounce, deferred reload, invalid YAML, app-save hash filtering, app-save/external edit races are deterministic | unit/coordinator + watcher smoke | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ConfigPersistenceCoordinatorTests|FullyQualifiedName~ConfigWatcherServiceTests"` | ❌ coordinator; ✅ watcher smoke exists |
| PERF-03 | Normal config clone path does not serialize/deserialize YAML and deep-copies mutable containers | unit/model/service | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~UserConfigurationCopyTests|FullyQualifiedName~ConfigurationServiceTests"` | ❌ copy guard; ✅ existing clone independence exists |

### Sampling Rate
- **Per task commit:** `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~Configuration`
- **Per wave merge:** `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj`
- **Phase gate:** `dotnet test AutoQACSharp.slnx` before `/gsd-verify-work`

### Wave 0 Gaps
- [ ] `AutoQAC.Tests/Services/ConfigPersistenceCoordinatorTests.cs` — covers REF-03 and TEST-03 serialized flow/race matrix.
- [ ] `AutoQAC.Tests/Services/Fakes/FakeUserConfigFileStore.cs` or private nested fake — covers deterministic write/replace/read/hash/missing/invalid failures.
- [ ] `AutoQAC.Tests/Models/UserConfigurationCopyTests.cs` — covers PERF-03 deep copy/null normalization/YAML-free clone guard.
- [ ] Update `AutoQAC.Tests/Services/ConfigurationServiceTests.cs` — preserve existing clone/concurrent/flush behavior against facade.

## Sources

### Primary (HIGH confidence)
- `.planning/phases/10-configuration-persistence-hardening/10-SPEC.md` — locked Phase 10 requirements, boundaries, and acceptance criteria.
- `.planning/phases/10-configuration-persistence-hardening/10-CONTEXT.md` — implementation decisions D-01..D-46.
- `AutoQAC/Services/Configuration/ConfigurationService.cs` — current save/reload/hash/YAML clone implementation.
- `AutoQAC/Services/Configuration/ConfigWatcherService.cs` — current watcher/defer/hash behavior.
- `AutoQAC/Models/Configuration/UserConfiguration.cs`, `BackupSettings.cs`, `RetentionSettings.cs` — config object graph requiring manual copy.
- `AutoQAC/Services/Cleaning/CleaningPreflight.cs` — pre-clean flush integration point.
- Microsoft Learn Channels docs — `System.Threading.Channels` producer/consumer model and options: https://learn.microsoft.com/dotnet/core/extensions/channels
- Microsoft Learn FileSystemWatcher docs — events and error handling: https://learn.microsoft.com/dotnet/api/system.io.filesystemwatcher?view=net-10.0
- Microsoft Learn File.Replace docs — replace semantics and exceptions: https://learn.microsoft.com/dotnet/api/system.io.file.replace?view=net-10.0
- Context7 `/aaubry/yamldotnet` — serializer/deserializer, aliases, `IgnoreUnmatchedProperties` examples.

### Secondary (MEDIUM confidence)
- NuGet flat-container API — current latest package versions for YamlDotNet, System.Reactive, FluentAssertions, NSubstitute, xUnit, Microsoft.NET.Test.Sdk.
- `.planning/codebase/ARCHITECTURE.md`, `.planning/codebase/TESTING.md`, `.planning/codebase/CONCERNS.md` — project-specific architecture/testing/fragility mapping.

### Tertiary (LOW confidence)
- Assumptions about inline continuation deadlock risk; based on general async practice, not directly reproduced in this session.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — existing project packages verified with `dotnet list package`; .NET APIs verified with Microsoft Learn; YamlDotNet docs verified through Context7.
- Architecture: HIGH — phase decisions lock coordinator/queue approach; current code and codebase docs identify integration points.
- Pitfalls: MEDIUM-HIGH — most pitfalls are verified from code/spec; continuation pitfall is marked assumed.

**Research date:** 2026-04-30  
**Valid until:** 2026-05-30 for project architecture; 2026-05-07 for NuGet latest-version observations.