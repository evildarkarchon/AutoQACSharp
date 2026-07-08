# Phase 10: Configuration Persistence Hardening - Specification

**Created:** 2026-04-30
**Ambiguity score:** 0.17 (gate: <= 0.20)
**Requirements:** 7 locked

## Goal

AutoQAC user configuration saves, reloads, watcher deferrals, and persistence failures become deterministic and user-recoverable while in-memory config cloning stops using YAML serialization round-trips.

## Background

`ConfigurationService` currently owns YAML load/save for `AutoQAC Settings.yaml`, debounces saves through an Rx `Throttle`/`Switch` pipeline, tracks `_pendingConfig`, uses `_fileLock`, computes `_lastWrittenHash`, and clones `UserConfiguration` by serializing/deserializing YAML. `ConfigWatcherService` separately watches the same YAML file with `FileSystemWatcher`, throttles changed events, compares hashes to skip app-initiated writes, rejects invalid YAML, and defers external reloads while `IStateService.CurrentState.IsCleaning` is true. Existing tests cover basic persistence, concurrent load/save, app-write hash filtering, deferred reload, invalid reload recovery, and real watcher events, but the roadmap still identifies race-prone debounce/reload behavior, silent logging-only persistence failures, timing-dependent watcher tests, and YAML clone overhead as Phase 10 gaps.

## Requirements

1. **Serialized persistence authority**: User configuration save, flush, reload, watcher deferral, and failure transitions must be reasoned about through one serialized persistence flow.
   - Current: `ConfigurationService` and `ConfigWatcherService` coordinate indirectly through `_pendingConfig`, `_lastWrittenHash`, `_fileLock`, Rx throttles, and a volatile deferred flag.
   - Target: Overlapping app saves, forced flushes, external reloads, cleaning deferrals, and persistence failures resolve through one authoritative flow with deterministic ordering semantics.
   - Acceptance: Deterministic tests drive overlapping save, flush, external reload, and cleaning-state transitions and assert the final in-memory config, on-disk config, emitted changes, and failure state for each ordering.

2. **App-save protected race policy**: App-initiated user changes must not be overwritten by stale watcher reloads when saves and external file events happen close together.
   - Current: Matching `_lastWrittenHash` values are skipped, but the required behavior for pending app saves racing distinct external edits is not locked as a product rule.
   - Target: A pending or just-written app save remains authoritative until the save settles; valid external edits reload only when they are distinct from the app write and not racing an active or pending app save.
   - Acceptance: A deterministic race test simulates an app save, a watcher event for the app write, and a distinct external edit in close succession; the app value remains authoritative during the race, and a later valid non-racing external edit can reload successfully.

3. **Cleaning-time reload deferral**: External config edits during cleaning must never change active cleaning configuration immediately and must be applied at most once after cleaning ends when valid.
   - Current: `ConfigWatcherService` sets `_hasDeferredChanges` while cleaning and applies a deferred reload after a non-cleaning state transition, with real watcher timing in tests.
   - Target: Valid external edits observed during cleaning are deferred until cleaning ends; repeated non-cleaning transitions do not reapply the same deferred change; invalid deferred YAML leaves the current configuration intact.
   - Acceptance: Deterministic tests cover valid deferred reload, multiple state transitions, multiple external changes while cleaning, and invalid deferred YAML without relying on real `FileSystemWatcher` timing.

4. **Recoverable persistence failure state**: Save and reload failures must preserve a usable configuration and surface an assertable user-observable failure path.
   - Current: Debounced save failures log and may revert to `_lastKnownGoodConfig`; watcher reload failures are logged, but there is no locked user-observable failure contract.
   - Target: Failed saves or reloads keep the last known good configuration active and emit or expose a recoverable failure state that UI or ViewModel code can surface without stack traces.
   - Acceptance: Tests simulate write failure and reload failure, then assert last known good values remain active, a failure signal/status is emitted or exposed, and subsequent valid persistence operations can recover.

5. **Pre-cleaning flush safety**: A required pre-cleaning config flush failure must not silently allow xEdit cleaning to start with stale or unpersisted user settings.
   - Current: `CleaningPreflight.PrepareAsync` calls `FlushPendingSavesAsync`, but final save failure handling in `SaveToDiskWithRetryAsync` can log/revert without a clearly locked failure result for callers.
   - Target: If pending user configuration cannot be persisted during the required pre-cleaning flush, cleaning does not launch xEdit and the user receives the recoverable persistence failure path.
   - Acceptance: A test simulates an unwritable settings file during preflight and asserts no xEdit process launch path is reached, cleaning reports configuration persistence failure, and the previous known-good config remains active.

6. **YAML-free in-memory cloning**: Normal in-memory cloning of `UserConfiguration` must not use YAML serialization or deserialization.
   - Current: `CloneConfig` serializes a `UserConfiguration` to YAML and deserializes it back to produce an independent copy.
   - Target: Save/load/reload/change-notification paths return independent mutable config copies without YAML round-trips; YAML serialization remains only for actual disk persistence.
   - Acceptance: Existing independent-clone tests pass for nested lists/dictionaries, and a source-level or executable test proves the normal clone path does not call YamlDotNet serialize/deserialize for cloning.

7. **Deterministic watcher race coverage**: Phase 10 must replace timing-dependent race assertions with deterministic tests for the persistence behaviors it changes.
   - Current: `ConfigWatcherServiceTests` exercise real `FileSystemWatcher` behavior with timeout windows around debounced events.
   - Target: Debounce, app-save hash filtering, deferred reload, invalid YAML rejection, and app-save/external-edit races are covered by controllable tests that do not depend on arbitrary sleeps or real filesystem event timing; real watcher smoke coverage may remain as supplemental coverage.
   - Acceptance: Test names and assertions make the race cases explicit, and the deterministic race suite passes without waiting for the existing 500 ms watcher throttle or real `FileSystemWatcher` event delivery.

## Boundaries

**In scope:**
- User configuration persistence for `AutoQAC Settings.yaml`: save, debounced save, forced flush, reload, watcher reload, app-save hash filtering, cleaning-time reload deferral, and persistence failure state.
- App-save protected conflict policy for close-timing app saves and external YAML edits.
- Recoverable save/reload failure behavior, including the pre-cleaning forced flush path.
- Deterministic tests for debounce, deferred reload, invalid YAML, app-save hash filtering, and app-save/external-edit races.
- Replacement of YAML-based `UserConfiguration` cloning in normal in-memory paths.
- Preservation of existing public configuration behavior unless a requirement above explicitly changes the failure/race outcome.

**Out of scope:**
- Centralized app-data/path-provider refactor across configuration, watcher, migration, logging, PID storage, or tests - identified in codebase concerns but not the minimum viable Phase 10 boundary.
- Main configuration, legacy migration, and log retention behavior changes - adjacent configuration services are not part of the persistence race target unless compile-time adjustments are required.
- Redesigning settings UI or adding new settings screens - Phase 10 may expose a minimal failure signal/status, but broad UI design is not required.
- Phase 11 diagnostics redaction work - concise error/log path exposure boundaries are handled by the next phase.
- YAML schema, file name, or migration format changes - Phase 10 hardens behavior without changing user config file compatibility.
- Parallel xEdit cleaning or process-launch behavior changes - sequential cleaning remains a hard project constraint.
- Mutagen or QueryPlugins changes - this phase is limited to app-side configuration persistence.

## Constraints

- Preserve Windows-only app assumptions and the existing `AutoQAC Data/AutoQAC Settings.yaml` user configuration file name/location.
- Preserve existing YAML field aliases and user config compatibility.
- Keep I/O async and avoid blocking the UI thread with `.Result` or `.Wait()`.
- Maintain app-initiated save hash filtering so app saves do not create save/reload loops.
- Continue deferring external reloads during active cleaning; never mutate active cleaning configuration from a watcher event.
- Do not parallelize xEdit cleaning or introduce background behavior that can launch multiple xEdit processes.
- Keep ViewModels on existing MVVM boundaries; services expose state/results, and UI code decides how to present them.
- Deterministic race tests must not depend on arbitrary sleeps, the current 500 ms throttle delay, or real `FileSystemWatcher` event delivery.

## Acceptance Criteria

- [ ] User config save, flush, reload, watcher deferral, and failure transitions are covered by one serialized persistence flow with deterministic ordering semantics.
- [ ] A close-timing app save plus watcher reload cannot overwrite the app's pending or just-written value with stale external content.
- [ ] A later valid non-racing external YAML edit can still reload after app-save protection has settled.
- [ ] External YAML changes detected during cleaning do not reload immediately and apply at most once after cleaning ends when valid.
- [ ] Invalid external or deferred YAML leaves the current configuration active and records a recoverable failure/rejection path.
- [ ] Save or reload failure preserves last known good configuration and emits or exposes an assertable user-observable recoverable failure state.
- [ ] A required pre-cleaning flush failure prevents xEdit launch/cleaning start and surfaces the persistence failure path.
- [ ] Normal in-memory `UserConfiguration` cloning no longer serializes/deserializes YAML while preserving independent nested mutable collections.
- [ ] Deterministic tests cover debounce, app-save hash filtering, deferred reload, invalid YAML, and app-save/external-edit race cases without relying on real watcher timing.
- [ ] Existing `ConfigurationServiceTests`, `ConfigWatcherServiceTests`, and the full solution test suite pass after implementation.

## Ambiguity Report

| Dimension           | Score | Min   | Status | Notes |
|---------------------|-------|-------|--------|-------|
| Goal Clarity        | 0.90  | 0.75  | met    | Goal locks config persistence determinism, recoverable failures, and clone overhead. |
| Boundary Clarity    | 0.78  | 0.70  | met    | Scope is persistence flow only; path-provider and diagnostics phases are excluded. |
| Constraint Clarity  | 0.82  | 0.65  | met    | Race policy, no-timing tests, YAML compatibility, and cleaning deferral are explicit. |
| Acceptance Criteria | 0.80  | 0.70  | met    | Ten pass/fail checks cover user behavior, maintainer tests, and clone overhead. |
| **Ambiguity**       | 0.17  | <=0.20| met    | Gate passed after round 2. |

Status: met = dimension meets the minimum; below minimum = planner treats as assumption.

## Interview Log

| Round | Perspective | Question summary | Decision locked |
|-------|-------------|------------------|-----------------|
| 1 | Researcher | Required behavior when app save and external YAML edit happen close together | App save protected: pending/just-written app changes must not be overwritten by stale watcher reloads. |
| 1 | Researcher | What counts as recoverable persistence failure | Failures must keep last known good config and surface a visible/assertable recoverable state. |
| 1 | Researcher | Highest-priority Phase 10 acceptance anchor | Watcher races are the primary proof point. |
| 2 | Researcher + Simplifier | Minimum viable Phase 10 boundary | Persistence flow only; broader app-data path-provider centralization is out of scope. |
| 2 | Researcher + Simplifier | Meaning of deterministic watcher race tests | Race tests must avoid arbitrary timing and real `FileSystemWatcher` dependence. |
| 2 | Researcher + Simplifier | Acceptance target for clone overhead | Normal in-memory config cloning must not use YAML serialization/deserialization. |

---

*Phase: 10-configuration-persistence-hardening*
*Spec created: 2026-04-30*
*Next step: /gsd-discuss-phase 10 - implementation decisions only*
