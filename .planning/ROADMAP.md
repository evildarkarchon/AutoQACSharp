# Roadmap: AutoQAC

## Milestones

- ✅ **v1.0 xEdit Log Parsing Fix** — Phases 1-4 (shipped 2026-03-31)
- [ ] **v1.0 Cleanup** — Phases 5-14

## Phases

<details>
<summary>✅ v1.0 xEdit Log Parsing Fix (Phases 1-4) — SHIPPED 2026-03-31</summary>

- [x] Phase 1: Foundation -- Game-Aware Log File Service (2/2 plans) — completed 2026-03-31
- [x] Phase 2: Process Layer -- Stop Stdout Capture (1/1 plans) — completed 2026-03-31
- [x] Phase 3: Integration -- Log-First Parsing (2/2 plans) — completed 2026-03-31
- [x] Phase 4: Cleanup -- Remove Dead Code (2/2 plans) — completed 2026-03-31

Full details: [milestones/v1.0-ROADMAP.md](milestones/v1.0-ROADMAP.md)

</details>

- [ ] **Phase 5: Process Stop & PID Safety** - Users get reliable two-stage termination behavior and maintainers can test real process cleanup.
- [x] **Phase 6: Command Launch Escaping** - Users can clean plugins with difficult paths and names through direct xEdit and MO2 launches.
- [ ] **Phase 7: Backup Restore & Retention Safety** - Users can recover from backup/restore problems with clear, cancellable outcomes.
- [ ] **Phase 8: Cleaning Orchestrator Decomposition** - Maintainers can change cleaning flow pieces without broad orchestrator rewrites.
- [ ] **Phase 9: Plugin Refresh & Approximation Performance** - Users can refresh approximations more efficiently while plugin-loading coordination moves out of the ViewModel.
- [x] **Phase 10: Configuration Persistence Hardening** - Users get deterministic configuration save/reload behavior with lower clone overhead. (gaps planned 2026-05-01) (completed 2026-05-01) (additional gap planned 2026-05-01)
- [x] **Phase 11: User-Facing Diagnostics Boundaries** - Users see concise errors while logs avoid unnecessary path and command exposure. (gaps planned 2026-05-01) (completed 2026-05-01) (additional gap planned 2026-05-01) (completed 2026-05-01) (additional gap planned 2026-05-01) (completed 2026-05-01) (additional gap planned 2026-05-01)
- [ ] **Phase 12: Process Stop Verification & Progress Flow Closure** - Users can stop cleaning from the Progress window through the confirmed two-stage termination and force-failure reporting path.
- [ ] **Phase 13: Command Launch Escaping Reverification & Safe MO2 Failures** - Users cannot accidentally launch direct xEdit when MO2 mode is enabled but MO2 configuration is missing, and command escaping safety is re-verified.
- [ ] **Phase 14: Orchestrator Decomposition Reverification** - Maintainers have current verification evidence that orchestrator gap closures preserve session guarding and detected-game preflight validation.

## Phase Details

### Phase 5: Process Stop & PID Safety
**Goal**: Users can stop cleaning through a reliable two-stage flow, and process/PID cleanup behavior is testable without reflection or private file assumptions.
**Depends on**: Phase 4
**Requirements**: SAF-01, SAF-02, REF-04, TEST-01
**Success Criteria** (what must be TRUE):
  1. User can request stop and see the confirmation path before AutoQAC force-kills an unresponsive xEdit process.
  2. User sees an accurate failure outcome if force-killing xEdit fails because of access, OS, or process-state errors.
  3. Maintainer can verify real child-process timeout, graceful stop, force-kill, and PID cleanup behavior through controlled integration tests.
  4. Maintainer can test PID tracking through injected storage/path abstractions with process-safe update behavior.
**Plans**: 4 plans
Plans:
- [x] 05-01-PLAN.md — Build injectable, file-lock-protected PID storage with session-aware entries.
- [x] 05-02-PLAN.md — Correct process-service cancellation intent and force-kill result semantics.
- [x] 05-03-PLAN.md — Wire orchestrator/ViewModel stop confirmation, decline, failure, and log-safety behavior.
- [x] 05-04-PLAN.md — Add single-instance protection and controlled real-process integration tests.
**UI hint**: yes

### Phase 6: Command Launch Escaping
**Goal**: Users can launch direct xEdit and MO2-wrapped cleaning safely for plugin names and paths containing quotes, Unicode, spaces, and shell-sensitive characters.
**Depends on**: Phase 5
**Requirements**: SAF-03, TEST-02
**Success Criteria** (what must be TRUE):
  1. User can clean a plugin whose path or file name contains embedded quotes, Unicode, spaces, or shell-sensitive characters.
  2. User can run MO2 mode with nested xEdit arguments without argument splitting or target-plugin corruption.
  3. Maintainer can verify direct xEdit and MO2 command escaping across difficult path, plugin-name, and nested-argument cases.
**Plans**: 4 plans
Plans:
**Wave 1**
- [x] 06-01-PLAN.md — Build direct xEdit and MO2 `ArgumentList` command contracts with difficult-character regression tests.
- [x] 06-02-PLAN.md — Preserve `ArgumentList` through the real process-start boundary using the existing helper process.

**Wave 2 (blocked on Wave 1 completion)**
- [x] 06-03-PLAN.md — Integrate safe command-build failure messaging and final phase verification.

**Wave 3 (gap closure; blocked on Wave 2 completion)**
- [x] 06-04-PLAN.md — Close MO2 missing-path fallback and unexpected exception disclosure verification gaps.

Cross-cutting constraints:
- Difficult-character coverage spans quotes/parser cases, Unicode, spaces, shell-sensitive punctuation, and one combined worst-case input.
- Direct and MO2 launch paths must preserve exact argv intent without normalizing, stripping, sanitizing, or exposing full command lines to users.

### Phase 7: Backup Restore & Retention Safety
**Goal**: Users can restore backups and run backup/retention work with clear failure reporting, cancellation, and progress while preserving sequential xEdit cleaning.
**Depends on**: Phase 6
**Requirements**: SAF-04, TEST-04, PERF-04
**Success Criteria** (what must be TRUE):
  1. User can restore backups and receive clear failure reporting when target directories are missing or permissions fail.
  2. User can distinguish complete restore, partial restore, and cleanup-deletion failure outcomes for a session.
  3. User can cancel long backup or retention work and see visible progress without AutoQAC parallelizing xEdit launches.
  4. Maintainer can verify restore safety across missing targets, permission failures, partial failures, and cleanup deletion failures.
**Plans**: 14 plans
Plans:
**Wave 1**
- [x] 07-01-PLAN.md — Create backup/restore/retention result contracts and cancellable copy foundation.

**Wave 2 (blocked on Wave 1 completion)**
- [x] 07-02-PLAN.md — Implement structured async restore and retention service outcomes.

**Wave 3 (blocked on Wave 2 completion)**
- [x] 07-03-PLAN.md — Wire backup/retention progress and outcomes into sequential cleaning orchestration.
- [x] 07-04-PLAN.md — Add restore-window confirmations, progress, cancellation, and inline result reporting.

**Wave 4 (blocked on Wave 3 completion)**
- [x] 07-05-PLAN.md — Complete cleaning progress UI cancel affordances and final Phase 7 verification.

**Wave 5 (gap closure; blocked on Wave 4 completion)**
- [x] 07-06-PLAN.md — Close backup failure SkipPlugin/AbortSession session accounting and finalization gaps.
- [x] 07-07-PLAN.md — Close restore metadata validation, retention progress, and TEST-04 coverage gaps.

**Wave 6 (gap closure; blocked on Wave 5 completion)**
- [x] 07-08-PLAN.md — Close restore progress dispatcher, backup directory failure, and retention cancellation verification gaps.

**Wave 7 (gap closure; blocked on Wave 6 completion)**
- [x] 07-09-PLAN.md — Close remaining backup destination containment, restore target metadata, and mixed cancellation status gaps.

**Wave 8 (gap closure; blocked on Wave 7 completion)**
- [x] 07-10-PLAN.md — Close create-new backup copy existing-destination preservation gap.
- [x] 07-11-PLAN.md — Close trusted restore-root containment gap for restore metadata overwrites.

**Wave 9 (gap closure; blocked on Wave 8 completion)**
- [x] 07-12-PLAN.md — Close normal progress window result close and ViewModel disposal lifecycle gap (regex-loosened tests + defense-in-depth comment).
- [x] 07-14-PLAN.md — Extract shared `BackupPathContainment` helper consumed by `BackupService` and Plan 07-13 (closes cross-AI review duplication finding).
- [x] 07-13-PLAN.md — Close RestoreWindow Delete Session backup-root containment gap via service-layer `IBackupService.DeleteSessionAsync` (depends on 07-14).
**UI hint**: yes

### Phase 8: Cleaning Orchestrator Decomposition
**Goal**: Maintainers can reduce regression risk by changing cleaning preflight, backup, execution, result finalization, or termination coordination in focused collaborators instead of one monolithic orchestrator.
**Depends on**: Phase 7
**Requirements**: REF-01
**Success Criteria** (what must be TRUE):
  1. Maintainer can change cleaning preflight selection without editing backup, xEdit execution, or result finalization code.
  2. Maintainer can change backup-session handling without editing plugin execution or termination coordination code.
  3. Maintainer can change per-plugin execution and result finalization without changing session-level sequential coordination.
  4. User-observable cleaning behavior remains sequential and unchanged across successful, skipped, failed, stopped, and already-clean plugin outcomes.
**Plans**: 10 plans (6 original + 4 gap-closure)
Plans:
**Wave 0**
- [x] 08-01-PLAN.md — Add Wave 0 characterization tests (left-running, retention warning/canceled, dry-run/preflight equivalence, ContinueWithoutBackup, last-termination-result reset) and ICleaningOrchestrator public-surface snapshot test.

**Wave 1 (blocked on Wave 0 completion)**
- [x] 08-02-PLAN.md — Extract ICleaningPreflight collaborator (D-13–D-16); shared preflight/selection plan consumed by both StartCleaningAsync and RunDryRunAsync.

**Wave 2 (blocked on Wave 1 completion)**
- [x] 08-03-PLAN.md — Extract IBackupSessionCoordinator (D-06); coordinator owns backup CTS lifecycle, BackupPluginAsync/CleanupOldSessionsAsync helpers, and PluginBackupOutcome dispatch model.

**Wave 3 (blocked on Wave 2 completion; HIGHEST RISK — Phase 5 lock surface)**
- [x] 08-04-PLAN.md — Extract ICleaningTerminationCoordinator (D-08); coordinator owns _currentProcess, _processLock, _isStopRequested, _lastTerminationResult, hang-monitor, and Phase 5 stop/force-stop semantics.

**Wave 4 (blocked on Wave 3 completion)**
- [x] 08-05-PLAN.md — Extract IPluginCleaningRunner + IPluginResultFinalizer (D-07); runner owns retry/launch/offset capture, finalizer owns log read + result construction.

**Wave 5 (blocked on Wave 4 completion)**
- [x] 08-06-PLAN.md — Final facade integration + cross-file source-level parallelization guard; verify REF-01 satisfied.

**Wave 6 (review-informed gap closure; blocked on Wave 5 completion)**
- [x] 08-07-PLAN.md — Fix CR-01: publish session CTS before orphan cleanup/preflight so Stop during preflight cancels the session before any xEdit launch (TDD).
- [x] 08-08-PLAN.md — Fix CR-02 + WR-01 in PluginResultFinalizer: gate AlreadyClean on success/cleaned; derive Success from finalStatus (TDD).

**Wave 7 (verification gap closure; blocked on Wave 6 completion)**
- [x] 08-09-PLAN.md — Close concurrent StartCleaningAsync session-overlap gap with a fail-fast in-flight guard (TDD).
- [x] 08-10-PLAN.md — Close post-detection file-load-order validation gap in CleaningPreflight (TDD).

### Phase 9: Plugin Refresh & Approximation Performance
**Goal**: Users can refresh plugin issue approximations with better cancellation and less redundant work while plugin loading and approximation refresh behavior moves out of the configuration ViewModel.
**Depends on**: Phase 8
**Requirements**: REF-02, PERF-01, PERF-02
**Success Criteria** (what must be TRUE):
  1. User can refresh plugin issue approximations with responsive cancellation and narrower refresh scope when only selected or visible plugins need updates.
  2. User can run ITM approximation on large plugins without the detector materializing every override context for each record.
  3. Maintainer can change plugin loading or issue approximation refresh behavior outside `ConfigurationViewModel`.
  4. User-visible plugin lists and approximation results remain consistent with the selected game, data folder, skip lists, and current cancellation generation.
**Plans**: 9 plans
Plans:
**Wave 1**
- [x] 09-01-PLAN.md — Optimize QueryPlugins ITM analysis with streaming context traversal and cancellation propagation.
- [x] 09-02-PLAN.md — Define refresh coordinator contracts and Wave 0 behavior tests for row-first selected approximation refresh.

**Wave 2 (blocked on 09-02 completion)**
- [x] 09-03-PLAN.md — Implement the refresh coordinator/capability policy and move plugin refresh workflow out of ConfigurationViewModel.

**Wave 3 (blocked on 09-03 completion)**
- [x] 09-04-PLAN.md — Add selected approximation refresh and cancel controls near the plugin list.

**Wave 4 (blocked on 09-01 and 09-04 completion)**
- [x] 09-05-PLAN.md — Wire cleaning-start/lifecycle refresh cancellation and complete full phase verification.

**Wave 5 (gap closure; blocked on Wave 4 completion)**
- [x] 09-06-PLAN.md — Close selected approximation refresh non-targeted row preservation verification gaps.

**Wave 6 (gap closure; blocked on Wave 5 completion)**
- [x] 09-07-PLAN.md — Plumb DisableSkipLists from ConfigurationViewModel through PluginRefreshRequest into the coordinator so refreshed rows respect the user's Disable Skip Lists setting.

**Wave 7 (gap closure; blocked on 09-07 completion — same coordinator file)**
- [x] 09-08-PLAN.md — Publish a FullRefreshCompleted terminal status after successful full-list approximation analysis so the cancel-refresh UI clears.

**Wave 8 (gap closure; blocked on 09-08 completion — same coordinator file)**
- [x] 09-09-PLAN.md — Close cancellation CTS lifetime, failure terminal status, and variant-specific skip-list refresh gaps.
**UI hint**: yes

### Phase 10: Configuration Persistence Hardening
**Goal**: Users get reliable configuration saves/reloads under race conditions, and maintainers can reason about persistence through one serialized flow with lower in-memory clone cost.
**Depends on**: Phase 9
**Requirements**: REF-03, TEST-03, PERF-03
**Success Criteria** (what must be TRUE):
  1. User configuration changes save and reload deterministically when app saves and external edits occur in close timing windows.
  2. User sees or receives a recoverable failure path when configuration persistence fails instead of silent logging-only fallback.
  3. Maintainer can verify watcher race cases deterministically for debounce, deferred reload, invalid YAML, and app-save interactions.
  4. User configuration changes avoid YAML serialization round-trips for in-memory cloning.
**Plans**: 11 plans (5 original + 6 gap-closure)
Plans:
**Wave 1**
- [x] 10-01-PLAN.md — Manual deep-copy on UserConfiguration graph (PERF-03)

**Wave 2 (blocked on 10-01 completion)**
- [x] 10-02-PLAN.md — Persistence coordinator + file store seam + race-matrix tests (REF-03, TEST-03)

**Wave 3 (blocked on 10-02 completion)**
- [x] 10-03-PLAN.md — Wire IConfigurationService + ConfigWatcherService through coordinator; remove YAML clone (REF-03, TEST-03, PERF-03)

**Wave 4 (blocked on 10-03 completion)**
- [x] 10-04-PLAN.md — Pre-cleaning flush failure blocks xEdit launch (TEST-03)
- [x] 10-05-PLAN.md — Settings ViewModel + SettingsWindow typed-failure banner (REF-03)

**Wave 6 (gap closure; blocked on Wave 4 completion)**
- [x] 10-06-PLAN.md — Safe observer publication + explicit reload pending-save guard (REF-03, TEST-03)

**Wave 7 (gap closure; blocked on 10-06 completion)**
- [x] 10-07-PLAN.md — Facade state synchronization and conditional reload flag clearing (REF-03, TEST-03)

**Wave 8 (gap closure; blocked on 10-07 completion)**
- [x] 10-08-PLAN.md — Close explicit reload pending-save write-failure masking gap (REF-03, TEST-03)

**Wave 9 (gap closure; blocked on 10-08 completion)**
- [x] 10-09-PLAN.md — Close no-pending facade flush coordinator-barrier bypass (REF-03, TEST-03)

**Wave 10 (gap closure; blocked on 10-09 completion)**
- [x] 10-10-PLAN.md — Close watcher hash-read exception typed failure gap (REF-03, TEST-03)

**Wave 11 (gap closure; blocked on 10-10 completion)**
- [x] 10-11-PLAN.md — Close production DI shared-coordinator wiring regression (REF-03, TEST-03)

### Phase 11: User-Facing Diagnostics Boundaries
**Goal**: Users receive concise, actionable error messages while logs retain local troubleshooting value without unnecessary full path or command-line exposure.
**Depends on**: Phase 10
**Requirements**: SEC-01, SEC-02
**Success Criteria** (what must be TRUE):
  1. User sees concise error dialogs with a log-file reference rather than stack traces or excessive internal path detail.
  2. User diagnostic logs avoid unnecessary full command-line exposure while preserving enough context for local troubleshooting.
  3. User-facing exports and dialogs avoid exposing avoidable profile-root, game-install, xEdit, MO2, and plugin path detail unless needed for diagnosis.
**Plans**: 13 plans (6 original + 7 gap-closure)
Plans:
**Wave 1**
- [x] 11-01-PLAN.md — Create the shared safe diagnostics formatter and sentinel tests (SEC-01, SEC-02)

**Wave 2 (blocked on 11-01 completion)**
- [x] 11-02-PLAN.md — Harden cleaning/preview dialogs, status text, and pre-clean validation identifiers (SEC-01)
- [x] 11-03-PLAN.md — Harden configuration browse and restore session diagnostics (SEC-01)
- [x] 11-04-PLAN.md — Sanitize cleaning result rows and exported reports (SEC-01)
- [x] 11-05-PLAN.md — Redact process/startup log boundaries and migration warning copy (SEC-01, SEC-02)

**Wave 3 (blocked on Wave 2 completion)**
- [x] 11-06-PLAN.md — Add phase-level disclosure sentinels and final verification sweep (SEC-01, SEC-02)

**Wave 4 (gap closure; blocked on 11-06 completion)**
- [x] 11-07-PLAN.md — Close legacy migration warning raw exception disclosure gap (SEC-01)
- [x] 11-08-PLAN.md — Close successful process-start legacy Arguments PID/log disclosure gap (SEC-02)
- [x] 11-09-PLAN.md — Close report plugin-name display sanitization gap (SEC-01)

**Wave 5 (gap closure; blocked on 11-09 completion)**
- [x] 11-10-PLAN.md — Close CleaningService raw plugin-filename failed-message source boundary gap (SEC-01)

**Wave 6 (gap closure; blocked on 11-10 completion)**
- [x] 11-11-PLAN.md — Close xEdit main-log path LogParseWarning tooltip disclosure gap (SEC-01)

**Wave 7 (gap closure; blocked on 11-11 completion)**
- [x] 11-12-PLAN.md — Close backup failure and timeout retry callback dialog disclosure gaps (SEC-01)
- [x] 11-13-PLAN.md — Close Restore Selected backup metadata display disclosure gap (SEC-01)

Cross-cutting constraints:
- User-facing diagnostics must preserve concise safe copy and latest-log guidance while excluding raw exception text, stack traces, full local paths, and command fragments.
- Diagnostic identifiers should use sanitized basenames, game/folder labels, plugin filenames, and safe typed summaries instead of avoidable profile-root, game-install, xEdit, MO2, or plugin path detail.
- Logs must preserve local troubleshooting value through safe structured fields such as operation, launch mode, game/mode, plugin filename, PID when available, argument count, counts/status, and safe reason/category.
**UI hint**: yes

### Phase 12: Process Stop Verification & Progress Flow Closure
**Goal**: Users can stop cleaning from the Progress window through the same confirmed two-stage termination path as the main cleaning command, and maintainers have current verification evidence for stop/PID safety requirements.
**Depends on**: Phase 11
**Requirements**: SAF-01, SAF-02, REF-04, TEST-01
**Gap Closure**: Closes `v1.0-MILESTONE-AUDIT.md` orphaned Phase 5 requirements plus integration gap INT-01 and flow gap FLOW-01.
**Success Criteria** (what must be TRUE):
  1. User can click Stop in the Progress window and see the grace-expired force-termination confirmation before AutoQAC force-kills xEdit.
  2. User sees an accurate force-kill failure outcome from the Progress-window Stop path.
  3. Maintainer can verify Progress-window Stop, force-failure reporting, PID evidence, and process cleanup behavior through automated tests.
  4. Current verification artifacts prove SAF-01, SAF-02, REF-04, and TEST-01 are satisfied.
**Plans**: 3 plans
Plans:
**Wave 1**
- [x] 12-01-PLAN.md — Create shared stop outcome text and explicit dialog-label contract for main/Progress Stop parity.

**Wave 2 (blocked on 12-01 completion)**
- [x] 12-02-PLAN.md — Wire Progress Stop and Hang Kill through shared two-stage outcome handling and persistent summary warnings.

**Wave 3 (blocked on 12-02 completion)**
- [ ] 12-03-PLAN.md — Run current stop/process/PID evidence and write Phase 12 requirement verification artifact.

Cross-cutting constraints:
- Stop outcome copy must be exact, shared, and Phase 11-safe across main Stop, Progress Stop, and Hang Kill force-failure reporting.
- User-initiated Stop must never force-kill xEdit until `GracePeriodExpired` has been surfaced through the explicit `Force Terminate` confirmation.
- Phase 12 verification must close `INT-01` and `FLOW-01` without updating Phase 5 artifacts, `REQUIREMENTS.md`, or milestone completion markers.
**UI hint**: yes

### Phase 13: Command Launch Escaping Reverification & Safe MO2 Failures
**Goal**: Users can rely on MO2 mode failing safely when MO2 configuration is missing, while existing direct and MO2 command escaping guarantees remain verified.
**Depends on**: Phase 11
**Requirements**: SAF-03, TEST-02
**Gap Closure**: Closes `v1.0-MILESTONE-AUDIT.md` Phase 6 SAF-03/TEST-02 unsatisfied verification gaps.
**Success Criteria** (what must be TRUE):
  1. MO2-enabled cleaning with a missing MO2 executable path fails before any direct xEdit launch can start.
  2. Direct xEdit and configured MO2 launches still preserve quotes, Unicode, spaces, shell-sensitive characters, and nested xEdit arguments.
  3. Unexpected launch failures use safe user-facing diagnostics after Phase 11 boundaries.
  4. Current verification and validation artifacts prove SAF-03 and TEST-02 are satisfied.
**Plans**: 0 plans (gap closure planning pending)

### Phase 14: Orchestrator Decomposition Reverification
**Goal**: Maintainers can trust current verification evidence that the decomposed cleaning orchestrator preserves session guarding, final detected-game preflight validation, and sequential behavior.
**Depends on**: Phase 11
**Requirements**: REF-01
**Gap Closure**: Closes `v1.0-MILESTONE-AUDIT.md` Phase 8 REF-01 unsatisfied verification gap.
**Success Criteria** (what must be TRUE):
  1. Concurrent `StartCleaningAsync` calls are rejected or no-op safely without overwriting the active session CTS.
  2. File-load-order validation runs after Unknown-game detection resolves to Fallout3, FalloutNewVegas, or Oblivion.
  3. Cleaning remains sequential and collaborator boundaries remain focused across preflight, backup, runner, finalizer, and termination responsibilities.
  4. Current verification artifacts prove REF-01 is satisfied after the Phase 8 gap closures.
**Plans**: 0 plans (gap closure planning pending)

## Progress

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 1. Foundation -- Game-Aware Log File Service | v1.0 xEdit Log Parsing Fix | 2/2 | Complete | 2026-03-31 |
| 2. Process Layer -- Stop Stdout Capture | v1.0 xEdit Log Parsing Fix | 1/1 | Complete | 2026-03-31 |
| 3. Integration -- Log-First Parsing | v1.0 xEdit Log Parsing Fix | 2/2 | Complete | 2026-03-31 |
| 4. Cleanup -- Remove Dead Code | v1.0 xEdit Log Parsing Fix | 2/2 | Complete | 2026-03-31 |
| 5. Process Stop & PID Safety | v1.0 Cleanup | 0/0 | Not started | - |
| 6. Command Launch Escaping | v1.0 Cleanup | 4/4 | Complete | 2026-04-29 |
| 7. Backup Restore & Retention Safety | v1.0 Cleanup | 11/14 | Gaps planned | - |
| 8. Cleaning Orchestrator Decomposition | v1.0 Cleanup | 10/10 | Complete | 2026-04-30 |
| 9. Plugin Refresh & Approximation Performance | v1.0 Cleanup | 8/9 | Gaps planned | - |
| 10. Configuration Persistence Hardening | v1.0 Cleanup | 11/11 | Complete    | 2026-05-01 |
| 11. User-Facing Diagnostics Boundaries | v1.0 Cleanup | 13/13 | Complete   | 2026-05-01 |
| 12. Process Stop Verification & Progress Flow Closure | v1.0 Cleanup | 2/3 | In Progress|  |
| 13. Command Launch Escaping Reverification & Safe MO2 Failures | v1.0 Cleanup | 0/0 | Not started | - |
| 14. Orchestrator Decomposition Reverification | v1.0 Cleanup | 0/0 | Not started | - |

## Coverage

| Requirement | Phase |
|-------------|-------|
| SAF-01 | Phase 12 |
| SAF-02 | Phase 12 |
| SAF-03 | Phase 13 |
| SAF-04 | Phase 7 |
| REF-01 | Phase 14 |
| REF-02 | Phase 9 |
| REF-03 | Phase 10 |
| REF-04 | Phase 12 |
| TEST-01 | Phase 12 |
| TEST-02 | Phase 13 |
| TEST-03 | Phase 10 |
| TEST-04 | Phase 7 |
| SEC-01 | Phase 11 |
| SEC-02 | Phase 11 |
| PERF-01 | Phase 9 |
| PERF-02 | Phase 9 |
| PERF-03 | Phase 10 |
| PERF-04 | Phase 7 |

**Coverage:** 18/18 v1.0 Cleanup requirements mapped.
