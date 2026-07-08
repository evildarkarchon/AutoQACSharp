# Phase 10: Configuration Persistence Hardening - Context

**Gathered:** 2026-04-30
**Status:** Ready for planning

<domain>
## Phase Boundary

Phase 10 hardens `AutoQAC Settings.yaml` persistence so app saves, forced flushes, external reloads, cleaning-time deferrals, failure recovery, and in-memory cloning resolve through deterministic behavior. This phase preserves YAML compatibility and the existing settings file location while replacing race-prone save/reload coordination and YAML-based clone round-trips.

</domain>

<spec_lock>
## Requirements (locked via SPEC.md)

**7 requirements are locked.** See `10-SPEC.md` for full requirements, boundaries, and acceptance criteria.

Downstream agents MUST read `10-SPEC.md` before planning or implementing. Requirements are not duplicated here.

**In scope (from SPEC.md):**
- User configuration persistence for `AutoQAC Settings.yaml`: save, debounced save, forced flush, reload, watcher reload, app-save hash filtering, cleaning-time reload deferral, and persistence failure state.
- App-save protected conflict policy for close-timing app saves and external YAML edits.
- Recoverable save/reload failure behavior, including the pre-cleaning forced flush path.
- Deterministic tests for debounce, deferred reload, invalid YAML, app-save hash filtering, and app-save/external-edit races.
- Replacement of YAML-based `UserConfiguration` cloning in normal in-memory paths.
- Preservation of existing public configuration behavior unless a requirement explicitly changes the failure/race outcome.

**Out of scope (from SPEC.md):**
- Centralized app-data/path-provider refactor across configuration, watcher, migration, logging, PID storage, or tests.
- Main configuration, legacy migration, and log retention behavior changes unless compile-time adjustments are required.
- Settings UI redesign or new settings screens.
- Phase 11 diagnostics redaction work.
- YAML schema, file name, or migration format changes.
- Parallel xEdit cleaning or process-launch behavior changes.
- Mutagen or QueryPlugins changes.

</spec_lock>

<decisions>
## Implementation Decisions

### Persistence Authority
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

### External Edit Races
- **D-16:** If an external edit arrives while an app save is pending, reject that racing external edit. App save wins the close-timing race.
- **D-17:** A later external reload is valid only when a new watcher/content version is observed after the app save version settles.
- **D-18:** During cleaning, apply at most one deferred external edit after cleaning ends, using the latest valid external content observed during cleaning.
- **D-19:** If the latest deferred cleaning-time edit is invalid YAML, reject the deferred reload and keep the current active config. Do not apply an earlier valid deferred snapshot.
- **D-20:** Watcher events that echo an app-written hash/version are silent skips: debug log only, no user-visible status.
- **D-21:** If an app save fails after a racing external edit was rejected, that rejected external edit stays rejected. Active config returns to last known good until a later new external edit or user action.
- **D-22:** If the settings file is deleted or temporarily missing during watcher reload, keep the current active config, record/log a recoverable reload failure, and wait for a later valid file event. Do not silently reset to defaults.
- **D-23:** Expand watcher event handling beyond `Changed`; treat `Changed`, `Created`, `Renamed`, and `Deleted`/recreate patterns as signals to re-check current settings file content.

### Failure Surfacing
- **D-24:** Expose recoverable persistence failures through a typed observable/status stream from the configuration layer. ViewModels decide concise text presentation.
- **D-25:** User-visible failures are action blockers: pre-clean flush failures, failed user-initiated saves/reloads, and invalid external YAML rejections. Harmless app-write echoes stay silent.
- **D-26:** A required pre-cleaning flush failure blocks cleaning. Do not launch xEdit; show concise configuration-save failure and keep previous good config active.
- **D-27:** The next successful save/reload/flush clears the active failure signal and any ViewModel status derived from it.
- **D-28:** Typed failure payloads carry safe summary data only: operation type, safe reason category, recovery state, and optional log-reference text. Do not expose stack traces or raw exception objects to ViewModels.
- **D-29:** Invalid external YAML rejection should be status/banner text only, not a modal dialog.
- **D-30:** After optimistic UI changes roll back because save failed, the UI/status should explicitly say settings were restored to last saved values.
- **D-31:** Do not add new retry UI in Phase 10. Recovery happens through the next valid persistence operation.

### Deterministic Tests
- **D-32:** Primary race coverage uses the persistence coordinator seam directly by submitting save/reload/defer operations without real watcher timing.
- **D-33:** Keep real `FileSystemWatcher` coverage as smoke tests only; move race proof out of timing-dependent watcher tests.
- **D-34:** Tests should be outcome-first: assert final active config, on-disk config, failure signal, and critical ordering such as flush-before-preflight. Do not assert every internal queue step unless it is the protected behavior.
- **D-35:** Add targeted guards for YAML-free clone paths and deterministic race tests avoiding production throttle waits.
- **D-36:** Use a small internal file I/O seam/fake to deterministically inject write, replace, read, hash, missing-file, and invalid-content failures.
- **D-37:** Pre-cleaning flush failure coverage may use a mock boundary to prove the cleaning service/process launch path is not called.
- **D-38:** Race matrix scope is the SPEC acceptance matrix: save/flush/reload overlap, app-save external edit, cleaning deferral, invalid/missing YAML, and recovery.
- **D-39:** New deterministic tests must not wait on the production 500 ms watcher throttle or arbitrary sleeps. Existing real watcher smoke tests may keep bounded waits.

### Clone Replacement
- **D-40:** Replace YAML-based clone with manual deep-copy behavior. Do not add a clone library or immutable model refactor in this phase.
- **D-41:** Put clone/copy behavior near the models as public methods on `UserConfiguration` and nested config models.
- **D-42:** Protect clone maintenance with behavior tests rather than reflection/source mapping guards for every property.
- **D-43:** Ban YAML serialization/deserialization from normal clone paths only. YAML remains valid for actual disk persistence.
- **D-44:** Manual clone should normalize null nested config objects/collections to default empty instances for caller safety.
- **D-45:** Manual clone must deep-copy all mutable containers, including dictionaries whose values are currently immutable strings.
- **D-46:** Proving no YAML round-trip in the normal clone path is enough for PERF-03; do not add a benchmark project or timing comparison in Phase 10.

### the agent's Discretion
- Exact coordinator/interface names, queue implementation details, result DTO names, status enum names, clone method names, and test class organization are left to research/planning as long as the decisions above and `10-SPEC.md` are preserved.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase Scope And Requirements
- `.planning/phases/10-configuration-persistence-hardening/10-SPEC.md` — Locked Phase 10 requirements, boundaries, constraints, acceptance criteria, and interview decisions. MUST read first.
- `.planning/ROADMAP.md` — Phase 10 goal, dependencies, mapped requirements, and success criteria.
- `.planning/REQUIREMENTS.md` — REF-03, TEST-03, and PERF-03 definitions and traceability.
- `.planning/PROJECT.md` — Project constraints, cleanup milestone intent, and carried-forward decisions such as sequential xEdit cleaning and MVVM boundaries.

### Prior Locked Decisions
- `.planning/phases/07-backup-restore-retention-safety/07-CONTEXT.md` — Structured recoverable outcomes, concise user-facing failure text, and service-owned filesystem safety patterns.
- `.planning/phases/08-cleaning-orchestrator-decomposition/08-CONTEXT.md` — Shared preflight contract, required config flush before cleaning, behavior-preserving service extraction, and no process launch on invalid preflight.
- `.planning/phases/09-plugin-refresh-approximation-performance/09-CONTEXT.md` — Service-owned coordinator pattern, generation/cancellation semantics, and ViewModel mapping of typed service statuses.

### Codebase Constraints And Existing Patterns
- `.planning/codebase/CONCERNS.md` — Phase-driving concerns for debounced config persistence, external reload races, YAML clone overhead, and test gaps.
- `.planning/codebase/ARCHITECTURE.md` — Configuration persistence, watcher, state service, preflight, DI, threading, and MVVM boundaries.
- `.planning/codebase/TESTING.md` — Existing xUnit, FluentAssertions, NSubstitute, temp-directory, async synchronization, and smoke/integration test patterns.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `AutoQAC/Services/Configuration/ConfigurationService.cs`: current YAML load/save service, split `_fileLock`/`_stateLock`, `_pendingConfig`, `_lastKnownGoodConfig`, `_lastWrittenHash`, debounced Rx save pipeline, `FlushPendingSavesAsync`, `ReloadFromDiskAsync`, and YAML-based `CloneConfig` target.
- `AutoQAC/Services/Configuration/ConfigWatcherService.cs`: current FileSystemWatcher wrapper, `_lastKnownExternalHash`, `_hasDeferredChanges`, hash echo skip, cleaning-time deferral, invalid YAML validation, and watcher event pipeline target.
- `AutoQAC/Services/Configuration/IConfigurationService.cs`: public configuration surface that can be rewritten for the persistence slice and typed failure exposure.
- `AutoQAC/Services/Cleaning/CleaningPreflight.cs`: required pre-cleaning `FlushPendingSavesAsync` call before validation/xEdit launch; primary preflight failure integration point.
- `AutoQAC/Models/Configuration/UserConfiguration.cs`: mutable YAML-backed config object graph that needs public manual deep-copy methods and null/default normalization.
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`: DI registration point for any internal persistence coordinator, file I/O seam, or related interfaces.
- `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`: existing clone independence, concurrent load/save, debounced failure rollback, and flush tests to preserve/extend.
- `AutoQAC.Tests/Services/ConfigWatcherServiceTests.cs`: existing watcher tests that should be reduced to smoke coverage while deterministic race proof moves to coordinator/file-I/O seam tests.

### Established Patterns
- Configuration services are singletons registered by interface through DI.
- ViewModels should not receive raw exceptions or manipulate dialogs directly; services expose typed state/results and ViewModels map them to UI text or interactions.
- `CleaningPreflight` is the shared start-cleaning/dry-run preflight seam and already documents the required flush-before-xEdit behavior.
- Existing tests use temp directories, NSubstitute, `TaskCompletionSource`, and `FluentAssertions`; deterministic tests should follow these patterns but avoid arbitrary sleeps or production throttle waits.
- Prior Phase 9 coordinator/generation semantics are a strong analog for Phase 10 stale operation rejection.

### Integration Points
- `ConfigurationViewModel` and `SettingsViewModel` call `SaveUserConfigAsync`; their optimistic UI behavior should remain, with rollback/failure status when save fails.
- `App.axaml.cs` starts `ConfigWatcherService`; watcher startup can remain, but watcher decisions should route into the persistence authority.
- `CleaningPreflight.PrepareAsync` must receive a flush failure outcome and prevent xEdit launch through the existing preflight failure path.
- `IConfigurationService.UserConfigurationChanged` remains the main config-change notification path, but emitted values must come from validated authoritative config changes.
- Any new persistence/file-I/O abstractions must be registered in `ServiceCollectionExtensions.AddConfiguration` or a narrowly related extension.

</code_context>

<specifics>
## Specific Ideas

- Treat the persistence coordinator like a single-writer authority for `AutoQAC Settings.yaml`.
- App save intent wins close-timing races; rejected external edits do not become eligible later just because the app save failed.
- Missing/deleted settings files during watcher reload keep current active config and surface a recoverable reload failure rather than resetting settings.
- Invalid external YAML rejection is visible as status/banner text only.
- Failed optimistic saves should explain that settings were restored to the last saved values.
- Deterministic race tests should submit operations directly to the coordinator and use a fake file I/O seam, not real watcher timing.
- Clone replacement should be explicit, public, model-owned, YAML-free, null-normalizing, and deep for all mutable containers.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope. Broader app-data/path-provider centralization, settings UI redesign, diagnostics redaction, and main configuration behavior changes remain excluded by `10-SPEC.md` unless a compile-time adjustment is unavoidable.

</deferred>

---

*Phase: 10-configuration-persistence-hardening*
*Context gathered: 2026-04-30*
