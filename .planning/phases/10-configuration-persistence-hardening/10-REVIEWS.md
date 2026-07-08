---
phase: 10
reviewers: [gemini, codex, opencode]
reviewed_at: 2026-04-30T13:00:00Z
plans_reviewed:
  - 10-01-PLAN.md
  - 10-02-PLAN.md
  - 10-03-PLAN.md
  - 10-04-PLAN.md
  - 10-05-PLAN.md
reviewer_models:
  gemini: default
  codex: default
  opencode: opencode/claude-sonnet-4-6 (separate instance, alternate model)
---

# Cross-AI Plan Review — Phase 10: Configuration Persistence Hardening

Three independent AI reviewers (Gemini, Codex, OpenCode/claude-sonnet-4-6) reviewed the
five Phase 10 plans plus the SPEC, CONTEXT, RESEARCH, and ROADMAP context. The orchestrating
agent (claude-opus-4-7 via OpenCode) did not review its own work; the OpenCode reviewer used a
distinct alternate model (sonnet-4-6) in a separate process.

## Gemini Review

# Phase 10: Configuration Persistence Hardening - Plan Review

## Summary
The implementation plans for Phase 10 are **exceptionally high quality**, providing a robust architectural shift from ad-hoc locking and ReactiveUI-based debouncing to a centralized, serialized persistence authority. The transition to a single-reader `System.Threading.Channels` coordinator (Plan 02) is a "textbook" application of the Producer/Consumer pattern to resolve complex race conditions. The plans are comprehensive, addressing performance (Plan 01), reliability (Plan 02/03), workflow safety (Plan 04), and user experience (Plan 05) with clear traceability to requirements (REF-03, TEST-03, PERF-03).

---

## Strengths
- **Robust Sync Pattern**: Using `System.Threading.Channels` with a single reader is the most reliable way to handle the "multiple producers, one writer" problem inherent in configuration persistence with file watching.
- **Performance Optimization**: Replacing YAML round-trip cloning with manual deep-copy (Plan 01) eliminates unnecessary CPU/allocation overhead in the common path of reading/mutating config state.
- **Deterministic Testing**: The transition from timing-dependent watcher tests to deterministic coordinator tests using a fake file store and `TaskCompletionSource` (Plan 02) is a significant improvement in CI stability and maintainer confidence.
- **Fail-Safe Workflow**: Blocking xEdit launch upon pre-cleaning flush failure (Plan 04) closes a critical safety gap where the app might have previously run with unpersisted settings.
- **Surgical Integration**: Plan 03 preserves the existing public surface of `IConfigurationService` while gutting its internal complexity, adhering to the boundary decisions (D-03).
- **Security Awareness**: Inclusion of STRIDE threat registers and static source guards (e.g., YAML-free clone guards) demonstrates a defensive programming mindset.

---

## Concerns

### 1. `UnboundedChannel` Growth (Severity: LOW)
- **Risk**: `Channel.CreateUnbounded` is used for the coordinator. While `UserConfiguration` saves are infrequent, a malfunctioning producer or extreme filesystem thrashing could theoretically lead to memory growth.
- **Mitigation**: `D-04` (coalescing) and the auto-flush debounce mechanism significantly limit the number of active operations. However, consider if a `BoundedChannel` with `DropOldest` would be safer for purely signal-based operations like `WatcherObserved`.

### 2. Lazy Coordinator Start-up (Severity: MEDIUM)
- **Risk**: Plan 03 suggests starting the coordinator fire-and-forget in the `ConfigurationService` constructor. If the background loop fails to start or crashes early, subsequent `await` calls on barrier operations (like `Flush`) may hang indefinitely if the channel reader isn't processing.
- **Mitigation**: The plans include `WaitAsync(TimeSpan)` on disposal and bounded waits in tests, but a "Ready" check or a more explicit lifecycle management in `App.axaml.cs` might be more resilient.

### 3. File.Replace Volume Constraints (Severity: LOW)
- **Risk**: `File.Replace` requires the source and destination to be on the same volume.
- **Mitigation**: Since the temp file is written to `path + ".tmp"` (Plan 02, Task 2), they are guaranteed to be in the same directory/volume. This is well-handled.

---

## Suggestions

- **PeriodicTimer Disposal**: Ensure that if a `PeriodicTimer` is used for the auto-flush debounce loop in `ConfigPersistenceCoordinator.RunAsync`, it is properly disposed of when the loop terminates to avoid timer leaks.
- **Expose Coordinator "Healthy" Status**: Consider exposing a simple boolean or property on `IConfigurationService` indicating if the persistence background loop is active, which could be useful for diagnostic logging.
- **Dictionary Key Comparers**: In Plan 01, `SkipLists` uses `StringComparer.Ordinal`. Verify if existing YAML files might have casing variations (e.g., `sse` vs `SSE`). If the system is case-insensitive elsewhere, `OrdinalIgnoreCase` would be safer for the `Copy()` implementation to ensure consistency with `GetSkipListAsync`.
- **Failure Mapping Fallback**: In Plan 05, the `MapFailureToBanner` switch uses a default `_ => ...` case. Ensure the `SafeSummary` provided here is truly "Safe" for users who might not understand technical terms like `write_failed`.

---

## Risk Assessment: **LOW**

### Justification:
1. **Architectural Soundness**: The use of Channels for serialization and TaskCompletionSources for results is a proven, idiomatic C# pattern for this problem space.
2. **Exhaustive Testing**: The plans mandate 20+ new deterministic tests covering the exact race matrix that was previously the source of fragility.
3. **Incremental Rollout**: The wave-based approach (Wave 1: Logic; Wave 2: Wiring; Wave 3: UI/Workflow) allows for verification of the core engine before it impacts the user-facing cleaning flow.
4. **Backward Compatibility**: The plans explicitly preserve YAML disk compatibility and existing service method signatures, minimizing the risk of regressions in dependent components like skip-list management.

**Verdict**: The plans are ready for execution. The strategy for achieving REF-03 and TEST-03 is particularly strong.

---

## Codex Review

**Overall Assessment**
The phase direction is sound: model-owned copies, a single persistence coordinator, typed failure results, preflight blocking, and ViewModel surfacing all map to the locked requirements. The main risk is that the hardest part, race correctness, is not fully proven by the current coordinator/runtime seam. The plans also have a few compile/lifecycle mismatches with the current repo, including existing `InternalsVisibleTo`, design-time `SettingsViewModel` construction, and no XAML binding for the proposed banner.

**10-01 Manual Copy**
**Strengths**
- Good narrow scope for `PERF-03`.
- Correctly targets `SkipLists` nested list deep-copy behavior.
- Keeps YAML aliases intact and preserves disk compatibility.

**Concerns**
- MEDIUM: Plan 01 claims to deliver the normal clone-path requirement, but `ConfigurationService` still uses YAML cloning until Plan 03.
- MEDIUM: Source-regex tests around `Copy()` bodies are brittle; behavior tests are more valuable.
- LOW: The static guard must tolerate existing `using YamlDotNet.Serialization;` in `UserConfiguration.cs:2`.

**Suggestions**
- Reword success as "copy primitive delivered; clone-path replacement completes in 10-03."
- Add a behavior test for null `SkipLists` entries and source/copy mutation in both directions.
- Avoid overfitting tests to exact source shape beyond "no Serialize/Deserialize in `Copy()` bodies."

**Risk Assessment**
LOW-MEDIUM. The implementation is straightforward; the main risk is overstating completion and brittle source tests.

**10-02 Coordinator + File Store**
**Strengths**
- Correctly identifies the single-writer coordinator as the right architecture.
- Uses fake file I/O for deterministic race tests.
- Typed failure payloads are a good boundary for UI and preflight.
- Atomic temp-write intent is right.

**Concerns**
- HIGH: Plan 02 is marked independent, but it calls `UserConfiguration.Copy()` from Plan 01. As written, parallel execution can fail to compile.
- HIGH: Debounced save behavior is not actually locked. Tests mostly use `FlushPendingSavesAsync`; `D-13` requires normal debounced saves to still persist.
- HIGH: Runtime watcher API later only sends `ConfigFileSignalKind`, while tests use `WatcherObserved(currentHash, observedGeneration)`. That seam may not prove the real watcher race.
- HIGH: Startup/initial load semantics are underspecified. Current `LoadUserConfigAsync` creates defaults when missing and reads existing disk state; the coordinator must own that.
- HIGH: `StopAsync` wording conflicts: completing the channel before enqueuing a final flush/shutdown operation can make final flush impossible.
- MEDIUM: `path + ".tmp"` risks temp collisions; use a unique temp file in the same directory.
- MEDIUM: Barrier operation exceptions must always complete their `TaskCompletionSource`; the generic consumer catch could otherwise hang callers.

**Suggestions**
- Make 10-02 depend on 10-01, or move `Copy()` into 10-02's prerequisite.
- Add an injectable clock/scheduler and deterministic tests for debounce expiry without `Task.Delay(500)`.
- Define `LoadCurrentAsync` startup behavior: read existing file, default on missing only where current behavior does, and validate before publish.
- Have watcher enqueue an observation containing producer-side app generation, or otherwise prove that a queued watcher signal observed during pending app save cannot be accepted after the save settles.
- Use `Path.GetRandomFileName()` temp files and clean them best-effort.

**Risk Assessment**
HIGH. This is the core race-control plan, and the current test seam does not yet prove the production race surface.

**10-03 Wiring**
**Strengths**
- Correctly preserves non-persistence configuration helpers.
- Moves watcher policy out of `ConfigWatcherService`, which matches the spec.
- Adds a coordinator interface at the boundary where watcher/service tests need substitution.
- Recognizes watcher tests should become smoke tests once coordinator tests own race proof.

**Concerns**
- HIGH: Starting the coordinator in `ConfigurationService`'s constructor is fragile. If the watcher is resolved before the service, or construction order changes, notifications can hit an unstarted coordinator.
- HIGH: Fire-and-forget `StartAsync()` in a constructor can hide startup failures and duplicate-start bugs.
- MEDIUM: Removing `_fileLock` wholesale risks accidentally removing main-config read serialization in `ConfigurationService.cs:104`.
- MEDIUM: Config directory ownership splits between `ConfigurationService` and `UserConfigFileStore`; tests must pass the same temp dir to both.
- MEDIUM: The smoke watcher test should use a `TaskCompletionSource` callback, not just `Task.Delay(2000)` then `Received()`.

**Suggestions**
- Start the coordinator explicitly from app startup before `configWatcher.StartWatching()`, or make every public coordinator method call an idempotent `EnsureStarted`.
- Keep a small main-config lock/cache path separate from user-config persistence.
- Add an integration test that builds DI and verifies the coordinator, configuration service, and watcher all share the same settings path.
- Ensure `ReloadFromDiskAsync` returning `Task` does not hide a failure result from callers that need it later.

**Risk Assessment**
HIGH. The architecture is right, but lifecycle and actual runtime wiring need tightening.

**10-04 Preflight Flush Failure**
**Strengths**
- Small, focused change.
- Correctly blocks before game detection, validation, and later launch paths.
- Backward-compatible exception inheritance from `InvalidOperationException` is pragmatic.
- Tests target the important "no downstream calls" behavior.

**Concerns**
- MEDIUM: The test instructions say to omit `ICleaningService` assertions if preflight does not call it, but current preflight calls `ValidateEnvironmentAsync`; assert it is not called.
- LOW: Treating `Rejected` as hard failure is defensible, but document it as defensive only.
- LOW: Logger assertion with params arrays may be fiddly under NSubstitute.

**Suggestions**
- Assert `ValidateEnvironmentAsync` is not called on flush failure.
- Add one test that verifies the configured `CancellationToken` is passed to `FlushPendingSavesAsync`.
- Keep the production change limited to the initial flush block.

**Risk Assessment**
LOW-MEDIUM. This is well-scoped; most risk is test brittleness.

**10-05 Settings Banner**
**Strengths**
- Uses the existing `CallbackObserver<T>` plus `IUiDispatcher` pattern.
- The fixed banner text avoids leaking raw exception details.
- Explicitly avoids retry UI and modal dialogs.

**Concerns**
- HIGH: The plan says XAML binding is out of scope, but manual UAT expects a visible banner. Without editing `SettingsWindow.axaml:22`, the user does not see anything.
- HIGH: `SettingsViewModel()` currently passes `null!`; subscribing to `_configService.Failures` in the constructor will break design-time construction unless guarded. See `SettingsViewModel.cs:146`.
- HIGH: `SaveAsync` currently closes after `SaveUserConfigAsync`, which only queues an optimistic save. A later debounced write failure may occur after the dialog closes, so the banner may never be visible.
- HIGH: The coordinator exposes only failures, not success/clear notifications. Clearing on `UserConfigurationChanged` may clear too early because optimistic saves also emit config changes.
- MEDIUM: "Clear on next successful flush" is not implementable from the proposed ViewModel surface unless a success/status stream exists.

**Suggestions**
- Either add the XAML banner in Plan 05 or rename this to ViewModel-only and add a separate required UI-binding plan.
- For explicit Settings Save, call `SaveUserConfigAsync` followed by `FlushPendingSavesAsync`; close only on `Success`/`NoOp`.
- Add a `PersistenceStatusChanged` or `IObservable<ConfigPersistenceState>` that emits both failure and clear/success states.
- Guard design-time construction: only subscribe when `configService` and `logger` are non-null, or remove the `null!` pattern.
- Decide whether the banner belongs in `SettingsWindow`, `ConfigurationViewModel`, or both; current manual UAT is ambiguous.

**Risk Assessment**
HIGH. As written, this may not satisfy the user-visible recovery requirement even if tests pass.

**Cross-Plan Fixes**
- Update the project context to match current code: the repo is Avalonia 12.0.1 and CommunityToolkit MVVM, not Avalonia 11.3/ReactiveUI.
- Make 10-02 explicitly depend on 10-01.
- Add deterministic debounce tests with a fake clock.
- Align Plan 02's watcher-observation test API with Plan 03's runtime watcher API.
- Add a real status/clear stream, not failure-only, before relying on UI clearing behavior.
- Include the Settings XAML banner if the phase success criterion says the user must see the failure path.

---

## OpenCode Review (claude-sonnet-4-6, separate instance)

## Cross-AI Plan Review: Phase 10 — Configuration Persistence Hardening

---

## Overall Assessment

The five plans represent a well-decomposed, defensively-designed persistence hardening effort. The wave structure, TDD discipline, and decision-ID traceability are all exemplary. The architectural choice of a single-reader `Channel<T>` coordinator is correct and well-motivated. The main risks are concentrated in Plan 02 (the most complex deliverable), with secondary concerns in Plan 03's wiring complexity and Plan 05's incomplete XAML binding story. No plan has a fatal flaw, but several HIGH concerns need resolution before execution.

---

## Plan 10-01: Manual Deep-Copy for UserConfiguration Graph

### Summary
Straightforward TDD plan to replace YAML round-trip cloning with model-owned `Copy()` methods. Well-scoped, correctly isolated from Plans 02/03, and the test matrix covers all documented edge cases.

### Strengths
- Static source-level YAML-free guard (case 12) is clever and verifiable without reflection hacks
- YAML alias preservation guard (case 13) closes the regression risk on user config compatibility
- Correctly defers `ConfigurationService.CloneConfig` removal to Plan 03 — respects wave ordering
- Null normalization to defaults (D-44) is explicit in both spec and test cases
- `StringComparer.Ordinal` on the `SkipLists` dictionary key is correct

### Concerns

**MEDIUM — `using System.Linq;` assumption may be wrong:** The plan instructs adding `using System.Linq;` if not already present, but C# 10+ implicit usings in `net10.0` projects typically include `System.Linq`. If the `.csproj` has `<ImplicitUsings>enable</ImplicitUsings>` (likely given the stack), this is a no-op, but the instruction could cause confusion or a spurious duplicate using. Should verify before blindly adding.

**MEDIUM — Static YAML-free guard fragility:** The guard reads source files by walking up from `AppContext.BaseDirectory` to find `AutoQACSharp.slnx`. This path-resolution strategy can fail in CI environments where the test binary is in a non-standard output directory or the solution file is renamed. The plan should specify a fallback (e.g., skip the static guard rather than fail the test) or use an environment variable / test fixture path.

**LOW — `Copy()` on `LoadOrderConfig`, `ModOrganizerConfig`, `XEditConfig` is a one-liner but receives XML doc comments referencing "nested lists" that don't exist in these types:** The doc comment template says "deep-copy semantics" but these types only have scalar properties. Minor documentation inaccuracy.

**LOW — Wave-1 parallel dependency risk:** Plan 02 uses `UserConfiguration.Copy()` which Plan 01 adds. If Plan 01's commit lands after Plan 02's RED step, Plan 02's GREEN implementation step will fail to compile until Plan 01 is merged. The wave description says both are Wave 1, but the plan notes "Plan 02 may use a temporary local copy expression" as a fallback — this fallback should be documented more explicitly, or a soft dependency on Plan 01's GREEN commit should be stated.

### Suggestions
- Add a `#pragma warning disable CA1062` or null check in `UserConfiguration.Copy()` for the top-level `config` parameter in case it's called on a value that came from external YAML without normalization
- Consider adding an `EqualityComparer.Default` assertion test to confirm `Copy()` produces a value-equal but reference-distinct graph (catches future property additions that miss the `Copy()` update)

### Risk Assessment: **LOW** — Isolated model change with strong test coverage. Primary risk is the static guard's path resolution in CI.

---

## Plan 10-02: Persistence Coordinator + File Store Seam

### Summary
The most complex and highest-value plan in the phase. The `Channel<T>` coordinator design is architecturally correct. The behavior matrix (B1–B21) is comprehensive. However, several implementation details carry HIGH risk around the auto-flush timer design, the `IsCleaning` subscription threading, and the `LoadCurrentAsync` barrier semantics.

### Strengths
- `SingleReader = true` + `AllowSynchronousContinuations = false` correctly prevents the Pitfall 4 re-entry problem
- `TaskCreationOptions.RunContinuationsAsynchronously` on all TCS instances is explicitly required
- Temp-file-then-replace/move atomic save covers the partial-write risk
- `FakeUserConfigFileStore.CallLog` enables sequence assertions (WriteTemp → Replace) without real filesystem
- B21 static guard against `Task.Delay(500` / `Thread.Sleep(500` in test source is excellent
- Generation/hash dual-key rejection policy (D-09, D-14) is correctly specified
- The "PumpUntilQuiescentAsync via NoOp flush" pattern for deterministic test drainage is the right approach

### Concerns

**HIGH — Auto-flush timer design is underspecified and creates a test isolation hazard:** The plan mentions a `PeriodicTimer` for the normal debounce save and a constructor-injectable `TimeSpan? autoFlushDelay = null`. However, if `autoFlushDelay` defaults to `TimeSpan.FromMilliseconds(500)` and tests don't pass `0`, background timer fires can race with test assertions even when the test "drained" via a flush barrier. The plan says "tests use direct flush barriers and never wait on the timer" but doesn't mandate that the timer is disabled in tests. This is a concrete source of flakiness. **Recommendation:** The coordinator should expose a `_autoFlushEnabled` flag that is `false` when `autoFlushDelay` is `TimeSpan.Zero`, and all coordinator tests should pass `TimeSpan.Zero` explicitly.

**HIGH — `IStateService.StateChanged` subscription threading:** The plan says "Subscribe to `IStateService.StateChanged` and translate into `CleaningStateChanged` operations enqueued to the channel." This subscription fires from whatever thread publishes `AppState` transitions. If the coordinator enqueues a `CleaningStateChanged` operation and the consumer applies it, the state flag `_isCleaning` can be stale by the time a racing watcher signal arrives — especially during the window between `StateChanged.OnNext(cleaning=true)` and the consumer applying the `CleaningStateChanged` op. The SPEC says "External config edits during cleaning must never change active cleaning configuration immediately." This race is real and the plan doesn't fully close it. **Recommendation:** Consider using `CurrentState.IsCleaning` as a synchronous read inside `ApplyOperationAsync(WatcherObserved)` rather than relying on the channel-mediated state mirror. The coordinator already has access to `IStateService` — a direct `_stateService.CurrentState.IsCleaning` read inside the consumer is simpler and avoids the lag.

**HIGH — `LoadCurrentAsync` described as a "barrier-style request" but the implementation hints ("expose a snapshot method that internally posts a no-op barrier") leave this ambiguous:** If `LoadCurrentAsync` requires a round-trip through the channel, callers that call it on the UI thread will block the channel's consumer while waiting. If it's a direct memory read under a `volatile` read or `Interlocked`, it bypasses the consumer's ordering guarantee. The plan needs to pick one model and specify it. **Recommendation:** Since `_activeConfig` is written only by the consumer thread, a `volatile` snapshot read is safe and avoids the barrier overhead. The plan should explicitly state this choice.

**MEDIUM — B5 specifies `Status=NoOp` but B4 says `Status=Success` — these are different consumer paths:** If "no pending save" results in `NoOp`, the pre-cleaning flush in Plan 04 needs to treat `NoOp` as success. The plan does specify this in Plan 04 (B5 there). However, the coordinator test (B5) should also assert that `LastFailure` remains `null` after a NoOp flush, which isn't explicitly stated.

**MEDIUM — `UserConfigReadResult` is declared `internal sealed record` inside `IUserConfigFileStore.cs`:** Placing a record inside an interface file is unconventional in C# and some analyzers will warn. It should be in its own file or at minimum in `ConfigPersistenceStatus.cs` alongside the other result types.

**MEDIUM — B17 says "Remove `HasReadOnlyAttribute` to simulate a write failure" on Windows:** Setting `FileAttributes.ReadOnly` on a file and expecting `File.Replace` to throw `UnauthorizedAccessException` is OS-behavior-dependent. On Windows, `File.Replace` may succeed or fail depending on whether the caller has SeBackupPrivilege. The test may be flaky. **Recommendation:** Use a `WriteAsync` override in `FakeUserConfigFileStore` that throws `new IOException("locked")` instead of relying on real filesystem behavior for failure simulation.

**LOW — The plan specifies `Channel.CreateUnbounded` but the coordinator accepts watcher events from `ConfigWatcherService` which may fire at high frequency:** An unbounded channel could grow without bound under a filesystem storm. Since this is an internal coordination mechanism with a background consumer, in practice the queue will drain faster than it fills, but worth noting the assumption.

**LOW — `WatcherObserved.CurrentHash` may be `null` when signal kind is Deleted/Created:** The plan acknowledges this ("op.CurrentHash may be null") and says to re-read via `_fileStore.ReadAsync`. But the `FakeUserConfigFileStore` in tests likely doesn't simulate a `ReadAsync` call triggered by a `null`-hash `WatcherObserved` signal. The race test B15 (file missing) should explicitly exercise this path.

### Suggestions
- Explicitly document the `LoadCurrentAsync` implementation choice (volatile read vs. barrier) in the SUMMARY
- Add a test for B9 that verifies the coordinator emits on `ConfigurationAccepted` (not just updates internal state)
- The `Shutdown` operation should have a bounded timeout built into the consumer (`await _fileStore.WriteAsync(...).WaitAsync(TimeSpan.FromSeconds(4))`), not just in `StopAsync` — otherwise a hung write blocks the consumer indefinitely
- Consider naming `ShutdownOperation` as something other than `Shutdown` to avoid conflicting with `IHostedService.StopAsync` naming conventions in future refactors

### Risk Assessment: **HIGH** — The auto-flush timer race (concern 1), IsCleaning lag (concern 2), and LoadCurrentAsync threading model (concern 3) are all concrete sources of test flakiness or correctness failure. These must be resolved in the plan before execution.

---

## Plan 10-03: Wire Services Through Coordinator

### Summary
Plan 03 is a large wiring plan that touches five production files simultaneously. The decomposition is logical but the Task 1 RED step is fragile — it tries to make tests RED by referencing `IConfigPersistenceCoordinator` before it exists, which means the build fails rather than tests failing at runtime. This is an acceptable RED indicator per the plan, but it means a build failure rather than a test runtime failure.

### Strengths
- Correctly identifies that `ConfigWatcherService` should lose its `IConfigurationService` and `IStateService` dependencies (the coordinator owns those now)
- Explicit list of fields/methods to remove from `ConfigurationService` and `ConfigWatcherService` prevents accidental survival of dead code
- The `IConfigPersistenceCoordinator` interface introduction is correctly placed here (first external coupling point)
- Preservation of `LoadMainConfigAsync` behavior (out of scope per D-15) is explicitly noted
- Wave 2 ordering correctly depends on Plans 01 and 02 completing first

### Concerns

**HIGH — `ConfigurationService` constructor fires `_coordinator.StartAsync()` as fire-and-forget:** The plan states `_coordinator.StartAsync(); // fires consumer loop without awaiting per service-singleton lifecycle`. In .NET, `async Task` methods that are not awaited cause unobserved exceptions to be swallowed silently. If `StartAsync` fails (e.g., the channel is already completed, or an internal state error), the failure is lost. **Recommendation:** Store the returned `Task` in a field and observe it with a continuation that logs the failure: `_consumerTask = _coordinator.StartAsync(); _ = _consumerTask.ContinueWith(t => _logger.Error(t.Exception, "[Config] Coordinator consumer crashed"), TaskContinuationOptions.OnlyOnFaulted);`

**HIGH — `DisposeAsync` disposes `_configChanges` and `_skipListChanges` subjects, but these may not exist after the rewrite:** The plan's `DisposeAsync` code sample references `_configChanges.Dispose()` and `_skipListChanges.Dispose()`. After the coordinator rewrite, `UserConfigurationChanged` is proxied from `_coordinator.ConfigurationAccepted`. If the service no longer owns a `Subject<UserConfiguration>`, disposing a non-existent subject will throw. The implementation must reconcile what Subjects (if any) `ConfigurationService` still owns after the rewrite.

**MEDIUM — The smoke test in `ConfigWatcherServiceTests` uses `NSubstitute.For<IConfigPersistenceCoordinator>()` but `IConfigPersistenceCoordinator` is `internal`:** NSubstitute cannot substitute `internal` interfaces unless `InternalsVisibleTo` is also applied to `DynamicProxyGenAssembly2` (the Castle proxy assembly NSubstitute uses internally). The plan adds `InternalsVisibleTo("AutoQAC.Tests")` in Plan 02, but this is insufficient for NSubstitute substitution of internal interfaces. **Recommendation:** Add `[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]` alongside the `AutoQAC.Tests` entry, OR make `IConfigPersistenceCoordinator` `public` (with `internal` on the coordinator class). This is a concrete build failure risk.

**MEDIUM — `UserConfigurationChanged` observable proxying:** The plan says proxy `_coordinator.ConfigurationAccepted`. But `ConfigurationAccepted` is an `IObservable<UserConfiguration>` on the coordinator. If `ConfigurationService` subscribes to it to forward to a local `Subject<UserConfiguration>`, that subscription must also be disposed. If it returns the coordinator's observable directly, the public API changes semantics (downstream subscribers now observe coordinator lifecycle, not service lifecycle). The plan doesn't specify which approach to use.

**MEDIUM — `App.axaml.cs` verification step "grep for StartWatching":** The plan says "VERIFY by grepping `App.axaml.cs` for `StartWatching`". If the app starts the watcher via DI hosted service or `IHostApplicationLifetime`, this grep may miss indirect watcher starts. A more robust check would be to grep the entire codebase for `StartWatching` calls.

**LOW — The `main config deserializer` concern:** The plan says "KEEP the deserializer for `LoadMainConfigAsync` only if needed; else extract a tiny private `MainConfigYamlSupport` helper." The "else" branch is ambiguous — if the deserializer is removed and then the plan discovers `LoadMainConfigAsync` needs it, there's a compile error mid-implementation. Should be "VERIFY `LoadMainConfigAsync` uses `_mainDeserializer` before removing any deserializer field."

**LOW — Task 2 runs `dotnet test AutoQACSharp.slnx --nologo` as the final verification but notes "If `CleaningPreflightTests` or other tests fail because of the typed-flush signature, that signals Plan 04 work is needed; fix only TRIVIAL compile-level issues here."** This is ambiguous guidance. If pre-cleaning tests fail, Plan 03's verification step will exit non-zero, which could be misinterpreted as a regression. **Recommendation:** Be explicit: if the only failure is a `CleaningPreflight` test whose substitute call doesn't match the new `Task<ConfigPersistenceResult>` return type, that's a Plan 04 responsibility and not a Plan 03 regression. The plan should either fix `CleaningPreflightTests` minimally here (update the substitute configuration) or explicitly mark those tests as `[Trait("Phase", "10-04")]` to filter them.

### Suggestions
- Add explicit grep verification for `_serializer.Serialize` in the whole `AutoQAC/Services/Configuration/` directory, not just `ConfigurationService.cs`, to catch any helper that may have been accidentally left behind
- The `IConfigWatcherService` interface may need a version bump to reflect the new constructor (it likely doesn't, since the interface exposes `StartWatching`/`StopWatching` only, but verify)
- Consider whether `ConfigurationService.ReloadFromDiskAsync` should return `Task<ConfigPersistenceResult>` now that the coordinator returns it — the plan discards the result, but exposing it through the interface would be consistent with `FlushPendingSavesAsync`

### Risk Assessment: **MEDIUM-HIGH** — The NSubstitute internal interface substitution issue (concern 3) is a concrete build failure risk that will block the watcher smoke test. The fire-and-forget `StartAsync` (concern 1) is a correctness risk. Both must be addressed.

---

## Plan 10-04: Pre-Cleaning Flush Failure Blocks Cleaning

### Summary
The smallest and most focused plan in the phase. Correctly adds the typed failure check in `CleaningPreflight.PrepareAsync` and introduces `ConfigPersistenceFailureException` as an `InvalidOperationException` subclass. Well-targeted, with a clear test matrix.

### Strengths
- `ConfigPersistenceFailureException : InvalidOperationException` is the correct backwards-compatible derivation choice
- Defensive handling of `Status=Rejected` (treat as Failed) with an explicit code comment is correct
- `DidNotReceive()` assertions on all downstream collaborators make the "no xEdit launch" guarantee machine-verifiable
- Placing `ConfigPersistenceFailureException` in `ConfigPersistenceStatus.cs` keeps all persistence-status types co-located

### Concerns

**MEDIUM — The `ConfigPersistenceFailure` null fallback in the throw site:** The plan has `flushResult.Failure ?? new ConfigPersistenceFailure(Flush, Unknown, safeSummary, null, flushResult.Generation)`. However, per Plan 02's design, if `Status=Failed` then `Failure` is always non-null. The `??` guard is defensive but could mask a Plan 02 contract violation silently. **Recommendation:** Log a warning if `Failure` is null despite `Status=Failed` before using the fallback, so contract violations are visible.

**MEDIUM — Test B3 uses `DidNotReceiveWithAnyArgs().LoadUserConfigAsync(default)` but `LoadUserConfigAsync(default)` has signature `LoadUserConfigAsync(CancellationToken ct = default)`:** NSubstitute's `DidNotReceiveWithAnyArgs()` ignores argument matching, so this should work, but the plan should be consistent about using `Arg.Any<CancellationToken>()` vs. `default` matching throughout, per AGENTS.md ("match optional parameters explicitly in substitute setups and assertions"). Using `DidNotReceiveWithAnyArgs` is fine, but mixing conventions within a single test is a maintenance hazard.

**LOW — The test for B4 (`PrepareAsync_FlushSuccess_ProceedsAsBefore`) instructs "configure other substitutes minimally so PrepareAsync can complete."** The existing happy-path test in `CleaningPreflightTests` likely already does this, but the plan should explicitly reference the existing fixture setup method to avoid duplicate configuration code being written from scratch.

**LOW — `CleaningOrchestrator` catch-site verification is done via "read the file; confirm by reading":** The plan notes "confirm by reading" without specifying what to look for. A concrete grep (e.g., `Select-String -Path AutoQAC/Services/Cleaning/CleaningOrchestrator.cs -Pattern "catch.*Exception|catch.*InvalidOperationException"`) would make this verifiable.

### Suggestions
- Add a test asserting `flushResult.Failure.SafeSummary` is NOT empty in the exception's message (i.e., `exception.Message.Should().NotBeNullOrEmpty()`) to guard against the fallback path producing an empty message
- Consider adding a test that specifically simulates `Status=Rejected` (despite the plan's note that it's not currently produced) to verify the defensive branch works — this is low-cost and proves future-safety

### Risk Assessment: **LOW** — Small, well-specified change with strong test coverage. The concerns are minor.

---

## Plan 10-05: SettingsViewModel Typed-Failure Banner

### Summary
Plan 05 correctly scopes the ViewModel-side failure surfacing to a single `SettingsViewModel` property. The decision to defer XAML binding to a future maintainer is pragmatic but creates an incomplete user story. The `autonomous: false` flag correctly signals this plan needs human oversight for the UAT checkpoint.

### Strengths
- `CallbackObserver<T>` + `IUiDispatcher.Post()` threading pattern correctly follows AGENTS.md
- Static guards for modal dialog (`MessageBox`/`ShowError`) and retry UI (`RetryCommand`) are directly verifiable
- Banner mapping table in `<interfaces>` is unambiguous and covers all `ConfigPersistenceFailureKind` values
- The fallback `_ => $"Could not persist settings: {failure.SafeSummary}"` case correctly uses `SafeSummary` (not raw exception)
- Clear-on-success via both `SaveAsync` and `UserConfigurationChanged` subscription covers the D-27 requirement from both angles

### Concerns

**HIGH — XAML binding to `PersistenceBannerText` is explicitly OUT OF SCOPE:** The plan ships a ViewModel property with no corresponding View binding. The phase success criterion says "User sees or receives a recoverable failure path." If the banner is never wired to a visible UI control, this criterion is not met. The manual UAT in Task 2 asks the user to observe the banner text "in the Settings dialog or its hosting view," but if no XAML binding exists, the banner will never be visible. **Recommendation:** Either include the XAML binding in Plan 05 scope (it's a small addition), or explicitly accept that the success criterion is partially deferred and update the SPEC acceptance item accordingly. Leaving this as "the maintainer's responsibility" is a gap.

**MEDIUM — `SaveAsync` clears the banner proactively (`ClearPersistenceBanner()`) after the `await SaveUserConfigAsync` returns, but `SaveUserConfigAsync` is now optimistic (D-12 — coordinator updates `_activeConfig` immediately):** There's a window where the coordinator has accepted the save intent but hasn't flushed to disk yet. If the disk write later fails (async, after `SaveUserConfigAsync` returns), the coordinator emits on `Failures`, which sets the banner. But the plan's `SaveAsync` flow already called `ClearPersistenceBanner()` optimistically. The `Failures` subscription will then re-set the banner, but only after the coordinator's flush completes (which is async from the UI perspective). The net result is: banner clears, then re-appears after the disk failure — which is actually the correct behavior. But the plan should document this as intentional rather than leaving it implicit.

**MEDIUM — `_failuresSubscription` disposal in `Dispose` is mentioned but the plan says "match the file's existing dispose pattern (other observers/subscriptions in the file)."** Without reading the existing file, it's unclear whether `SettingsViewModel` implements `IDisposable` today and whether there's an existing `Dispose` method. If it doesn't implement `IDisposable`, the plan needs to add the interface. The instruction "match the existing dispose pattern" could lead to the subscription being silently not disposed if no pattern exists.

**MEDIUM — The `UserConfigurationChanged` subscription for banner-clear-on-reload is added without specifying where its `IDisposable` is stored:** The plan mentions "Keep the new subscription's IDisposable field and dispose it in Dispose" but doesn't name the field. If an implementer adds it without a field (e.g., as a lambda without storing the IDisposable), it will be a subscription leak.

**LOW — The mapping for `{ Operation: Watcher }` returns `null`/falls through silently:** The plan says "Watcher events are silent — coordinator already filters." But the coordinator only filters watcher echoes. If a future coordinator change emits a `Watcher` operation failure on the `Failures` stream, the switch fall-through produces `$"Could not persist settings: {failure.SafeSummary}"`. This is acceptable behavior but should be explicitly documented in the code comment.

**LOW — The static guards in tests (B8, B9) read source files at runtime using solution-root resolution:** Same concern as Plan 01 — path resolution in CI can be fragile.

### Suggestions
- Add XAML binding for `PersistenceBannerText` in Plan 05 scope, even as a minimal `<TextBlock Text="{Binding PersistenceBannerText}" IsVisible="{Binding PersistenceBannerText, Converter={...IsNullOrEmpty}}" />` — this is a 2-line XAML change that completes the user story
- Consider using a `[NotifyPropertyChangedFor(nameof(HasPersistenceError))]` boolean alongside `PersistenceBannerText` to make XAML `IsVisible` binding simpler
- Explicitly name the `UserConfigurationChanged` subscription field (e.g., `_configChangedClearSubscription`) in the plan

### Risk Assessment: **MEDIUM** — The missing XAML binding is a concrete gap in the success criterion. The async-clear/re-set behavior is correct but undocumented. Implementer guidance needs strengthening.

---

## Cross-Cutting Concerns (OpenCode)

**Wave 1 parallel execution risk:** Plans 01 and 02 are both Wave 1 and independent, but Plan 02's GREEN step calls `UserConfiguration.Copy()`. If an executor starts Plan 02 before Plan 01 commits, they'll hit a compile error. The plans acknowledge this but the fallback ("temporary local copy expression") is under-specified. A concrete fallback implementation should be documented.

**`InternalsVisibleTo("DynamicProxyGenAssembly2")` omission:** Plans 02 and 03 both introduce `internal` types that are tested via NSubstitute substitution. Neither plan mentions the NSubstitute assembly-visibility requirement. This will cause NSubstitute to throw `Can not create proxy for type AutoQAC.Services.Configuration.IConfigPersistenceCoordinator because it is not accessible` at test runtime. **This must be fixed before execution.**

**SHA256 hashing:** Both `UserConfigFileStore` and the coordinator reference SHA256 for hash computation. The plan doesn't specify whether the hash is computed over the raw bytes or the UTF-8 string. Inconsistency between these two would cause hash-echo detection to fail. Should specify `SHA256.HashData(Encoding.UTF8.GetBytes(content))` as the canonical form in Plan 02.

**Acceptance of `Flush` returning `NoOp` vs `Success` when no pending changes:** Plan 02 specifies `NoOp` for "no pending changes." Plan 04 B5 treats `NoOp` as "proceed." This is consistent but worth verifying that `CleaningPreflight` after Plan 04 handles `NoOp` explicitly (it does — the `if (flushResult.Status is Failed or Rejected)` guard passes through `NoOp`).

---

## Summary Risk Table (per-plan, by reviewer)

| Plan  | Gemini | Codex     | OpenCode    |
|-------|--------|-----------|-------------|
| 10-01 | LOW    | LOW-MED   | LOW         |
| 10-02 | LOW    | HIGH      | HIGH        |
| 10-03 | MED    | HIGH      | MED-HIGH    |
| 10-04 | LOW    | LOW-MED   | LOW         |
| 10-05 | LOW    | HIGH      | MED         |

---

## Consensus Summary

### Agreed Strengths (raised by 2+ reviewers)

- **Channel<T> + single-reader is the right architecture.** All three reviewers explicitly endorse the producer/consumer queue choice as the textbook solution to the multi-source ordering problem.
- **TDD discipline and decision-ID traceability are exemplary.** Codex and OpenCode both highlight the wave structure, RED/GREEN/REFACTOR cycles, and `because:` clauses tied to D-XX decisions.
- **Atomic temp-file save and YAML-free deep clone are correctly scoped.** Gemini and OpenCode both call out `File.Replace`/`File.Move` and the model-owned `Copy()` methods as correctly designed.
- **Pre-cleaning flush blocking (Plan 04) is small, focused, and correctly closes a real safety gap.** All three rate Plan 04 as low-risk and well-targeted.
- **Static source guards for YAML-free clone bodies and `Task.Delay(500)` in tests** are praised as creative and verifiable (OpenCode B21 callout, Codex implicit endorsement).

### Agreed Concerns (raised by 2+ reviewers — highest priority to address)

1. **HIGH — Fire-and-forget `_coordinator.StartAsync()` in `ConfigurationService` constructor (Plan 03).**
   Both Gemini ("Lazy Coordinator Start-up", MEDIUM) and Codex (HIGH) and OpenCode (HIGH) flag this. Unobserved exceptions are swallowed silently; barrier `Flush` calls can hang indefinitely if the consumer never started. **Required fix:** Store `_consumerTask` and observe it with a fault continuation that logs to `_logger`, OR start the coordinator explicitly from `App.axaml.cs` before `configWatcher.StartWatching()`.

2. **HIGH — Plan 02 uses `UserConfiguration.Copy()` from Plan 01, but both are marked Wave 1 / independent.**
   Codex and OpenCode both flag this as a concrete compile-failure risk if executors run them in parallel. **Required fix:** Make Plan 02 explicitly `depends_on: [10-01]`, OR document a concrete inline fallback expression for Plan 02's RED-to-GREEN gap.

3. **HIGH — XAML binding for `PersistenceBannerText` is excluded from Plan 05 scope, breaking the phase success criterion "User sees or receives a recoverable failure path."**
   Codex and OpenCode both flag this. The ViewModel property exists but no view is bound to it, so the user never sees the banner. **Required fix:** Either add a minimal `<TextBlock Text="{Binding PersistenceBannerText}" IsVisible="{Binding HasPersistenceError}" />` to `SettingsWindow.axaml` in Plan 05 scope, OR explicitly downgrade the SPEC acceptance criterion and add a follow-up plan.

4. **MEDIUM — `SettingsViewModel()` design-time constructor passes `null!` (Plan 05).**
   Codex flags this as HIGH because subscribing to `_configService.Failures` in the constructor will NRE during XAML preview. OpenCode notes the disposal lifecycle gap as MEDIUM. **Required fix:** Guard the subscription with a null check in the constructor, OR remove the design-time `null!` constructor.

5. **MEDIUM — Watcher signal seam mismatch (Plan 02 vs Plan 03).**
   Codex flags HIGH: tests in Plan 02 use `WatcherObserved(currentHash, observedGeneration)` but Plan 03's runtime watcher only emits `ConfigFileSignalKind`. OpenCode flags the related `IsCleaning` subscription threading lag as HIGH. **Required fix:** Align the watcher operation API between the two plans before execution; document whether `IsCleaning` is read synchronously from `IStateService.CurrentState` inside the consumer (preferred) or mirrored via a queued `CleaningStateChanged` op.

6. **MEDIUM — `SaveAsync` in `SettingsViewModel` closes the dialog before the disk write completes (Plan 05).**
   Codex flags this as HIGH. The optimistic save returns immediately; a later debounced disk failure produces a banner that the user never sees because the dialog is closed. **Required fix:** In Plan 05's `SaveAsync`, call `SaveUserConfigAsync` followed by `await FlushPendingSavesAsync()`; close only when `Status ∈ {Success, NoOp}`.

### Divergent Views (worth investigating)

- **Plan 02 risk level diverges sharply.** Gemini rates it LOW; Codex and OpenCode both rate it HIGH. The divergence is because Gemini focused on the architectural choice of Channels, while Codex and OpenCode dug into the actual coordinator implementation details (auto-flush timer, IsCleaning lag, LoadCurrentAsync threading model). **Suggested action:** Treat Codex and OpenCode's per-detail concerns as the actionable list for replan.

- **Plan 05 risk level diverges.** Gemini rates it LOW (only mentions banner text safety); Codex rates HIGH (multiple concerns); OpenCode rates MEDIUM. **Suggested action:** Codex and OpenCode agree on the missing-XAML and SaveAsync-closes-too-early concerns; address those even if Gemini did not flag them.

- **`_fileLock` removal in Plan 03.** Codex flags MEDIUM risk that removing `_fileLock` wholesale could accidentally remove main-config read serialization. OpenCode does not flag this explicitly. **Suggested action:** Verify that `LoadMainConfigAsync` does not depend on `_fileLock` for thread-safety before removing it.

- **`InternalsVisibleTo("DynamicProxyGenAssembly2")`.** OpenCode flags this as a concrete build-failure risk for NSubstitute substitution of `internal` interfaces; the other reviewers don't mention it. **Suggested action:** Verify the existing test project's `[assembly: InternalsVisibleTo]` attributes — if `DynamicProxyGenAssembly2` is missing, add it; if `IConfigPersistenceCoordinator` and related types are public, this concern doesn't apply.

### Highest-Priority Action List for Replan

1. Add `depends_on: [10-01]` to Plan 02 (or document an inline `Copy()` fallback).
2. Replace fire-and-forget `_coordinator.StartAsync()` with task-tracked startup + logged fault continuation, OR move startup to `App.axaml.cs`.
3. Add the XAML banner binding to `SettingsWindow.axaml` in Plan 05 scope (or downgrade the success criterion).
4. Guard `SettingsViewModel()` design-time constructor against `null` `IConfigurationService` before subscribing to `Failures`.
5. Align Plan 02's test-time `WatcherObserved` API with Plan 03's runtime `ConfigFileSignalKind` (pick one); decide whether `_isCleaning` is mirrored or read synchronously from `IStateService.CurrentState`.
6. Change Plan 05's `SaveAsync` to await a flush barrier before closing the dialog.
7. Add `[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]` to `AutoQAC.csproj` if internal types will be NSubstitute-mocked, OR make `IConfigPersistenceCoordinator` public.
8. Specify SHA256 input form (`Encoding.UTF8.GetBytes(content)`) canonically in Plan 02.

---

*Reviewers: Gemini (default model), Codex (default model), OpenCode (claude-sonnet-4-6 in a separate process distinct from the orchestrating claude-opus-4-7 instance).*
*To incorporate feedback into planning: `/gsd-plan-phase 10 --reviews`*
