# Phase 8: Cleaning Orchestrator Decomposition - Context

**Gathered:** 2026-04-29
**Status:** Ready for planning

<domain>
## Phase Boundary

Phase 8 decomposes the existing cleaning session flow so maintainers can change preflight selection, backup session handling, per-plugin xEdit execution/result finalization, and termination coordination in focused collaborators instead of broad `CleaningOrchestrator` rewrites. This is a behavior-preserving refactor phase: user-visible cleaning behavior, sequential one-plugin-at-a-time execution, stop/force-stop semantics, backup behavior, dry-run outcomes, and the public ViewModel-facing orchestrator contract must remain stable.

</domain>

<decisions>
## Implementation Decisions

### Extraction Boundaries
- **D-01:** Use focused collaborators around the Phase 8 success criteria, not a minimal helper-only cleanup and not a pipeline/stage rewrite.
- **D-02:** `CleaningOrchestrator` remains the public sequential shell/facade. It owns high-level session ordering and delegates detailed policies to collaborators.
- **D-03:** Keep the public `ICleaningOrchestrator` API stable for ViewModels: start-cleaning overloads, `RunDryRunAsync`, stop/force-stop, backup-operation cancellation, `LastTerminationResult`, and `HangDetected` remain available through the same interface.
- **D-04:** Place new extraction interfaces and implementations under `AutoQAC/Services/Cleaning` unless research finds a compelling narrower reason to use an existing owner folder. Avoid broad cross-folder churn.
- **D-05:** Migration order is characterize-then-extract: lock current outcomes with tests before moving each seam, then extract one collaborator at a time.

### Collaborator Responsibilities
- **D-06:** Add a backup-session lifecycle collaborator for cleaning-session backup policy: create session, run per-plugin backup, shape backup-failure choices into cleaning outcomes, write metadata, and coordinate retention cleanup/progress/cancellation. `CleaningOrchestrator` should call it before/after plugin/session boundaries rather than owning backup branches inline.
- **D-07:** Split backup-independent per-plugin work into a runner plus finalizer. The runner handles attempt/retry, xEdit launch, and log-offset capture. The finalizer builds `PluginCleaningResult` from process results, log content, parser output, and termination state.
- **D-08:** Add a dedicated termination coordinator behind the orchestrator facade. It owns current-process tracking, stop/force-stop escalation state, `LastTerminationResult`, hang-monitor lifecycle, and `MayProcessStillBeRunning` decisions while preserving Phase 5 semantics.

### Behavior Preservation
- **D-09:** Phase 8 allows zero intentional user-visible behavior changes. Preserve successful, skipped, failed, stopped, force-stopped, left-running, already-clean, backup-canceled, backup-failed-choice, retention-warning, retention-canceled, and dry-run behavior.
- **D-10:** Prior phase locks are non-negotiable: sequential xEdit cleaning, two-stage stop/force-stop behavior, no log parsing after unsafe termination, exact launch argv intent, concise user-facing launch/error messages, backup cancellation semantics, restore/retention safety, and MO2 backup skip behavior.
- **D-11:** If decomposition reveals an edge-case bug that is not required to satisfy `REF-01`, downstream agents must stop and ask before fixing it. Do not silently expand this refactor into behavior repair.
- **D-12:** User-facing messages and result meanings stay stable. Internal log line placement, collaborator names in logs, and implementation-detail diagnostics may change if tests do not assert exact log text.

### Dry-Run and Preflight Sharing
- **D-13:** Create one preview-safe preflight/selection plan consumed by both `StartCleaningAsync` and `RunDryRunAsync`. It should handle config flush, game detection, variant detection, skip lists, exclusions, MO2 validation, and file validation without starting cleaning, creating cleaning CTS/processes, or running backups.
- **D-14:** State mutation is mode-specific. The shared preflight plan returns detected game/variant and selected-plugin facts; real cleaning may apply detected game state as current behavior does, while dry-run remains non-mutating.
- **D-15:** The preflight plan includes full clean/skip reason rows, not only a clean list. Reasons should preserve current dry-run semantics such as not selected, in skip list, file not found, unreadable, zero-byte, malformed entry, invalid extension, and ready for cleaning.
- **D-16:** The preflight plan carries explicit MO2 policy facts such as MO2 mode active, backup skipped, file validation skipped, and launch mode. Later collaborators should not rediscover these rules independently.

### Test Proof Expectations
- **D-17:** Minimum proof is characterization plus seams: add or preserve characterization tests for current session outcomes, then add focused collaborator tests showing each extracted responsibility can change independently.
- **D-18:** Prioritize characterization coverage for roadmap outcomes: successful, skipped, failed, stopped/left-running, already-clean, backup-canceled/failed choices, and retention warning/canceled paths where current tests are thin.
- **D-19:** Use source-level or structural guard tests sparingly for hard-to-observe invariants such as no parallel xEdit constructs and stable public orchestrator surface. Prefer behavior tests for normal logic.
- **D-20:** Test collaborator contracts by inputs, outputs, state/result effects, and preserved behavior. Assert call order only where order is itself the protected behavior, such as backup before xEdit, log offset before xEdit launch, and one-plugin-at-a-time sequencing.

### the agent's Discretion
- Exact collaborator type names, interface names, internal preflight model shape, test method names, and plan grouping are left to research and planning.
- Downstream agents may choose the smallest internal API that satisfies the locked boundaries above while preserving comments that explain safety, threading, cancellation, and termination constraints.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Planning Scope
- `.planning/ROADMAP.md` - Phase 8 goal, dependency on Phase 7, `REF-01`, and success criteria.
- `.planning/REQUIREMENTS.md` - Requirement `REF-01`: maintainers can change cleaning preflight, backup, execution, result finalization, or termination logic without editing one monolithic orchestrator.
- `.planning/PROJECT.md` - Project constraints: Windows-only app, sequential xEdit cleaning, read-only `Mutagen/`, MVVM boundaries, and cleanup milestone intent.
- `.planning/STATE.md` - Current milestone state and accumulated locked decisions from Phases 5-7.

### Prior Locked Decisions
- `.planning/phases/05-process-stop-pid-safety/05-CONTEXT.md` - Locks two-stage stop/force-stop behavior, left-running semantics, force-kill failure handling, and process-test expectations.
- `.planning/phases/06-command-launch-escaping/06-CONTEXT.md` - Locks command construction, exact argv preservation, MO2 launch contract, and concise user-facing launch failure messaging.
- `.planning/phases/07-backup-restore-retention-safety/07-CONTEXT.md` - Locks backup cancellation, restore/retention outcomes, progress/cancel behavior, retention warning semantics, and backup safety boundaries.

### Codebase Analysis
- `.planning/codebase/CONCERNS.md` - Identifies cleaning orchestration concentration and recommends narrow collaborators for session preparation, plugin filtering, backup session lifecycle, log-stat collection, dry-run planning, and termination state.
- `.planning/codebase/ARCHITECTURE.md` - Documents the cleaning request path, state service, process service, backup integration, dry-run path, and architectural constraints.
- `.planning/codebase/STRUCTURE.md` - Documents service folder layout, where to add new cleaning workflow code, and test locations.
- `.planning/codebase/CONVENTIONS.md` - Documents C# style, service/interface patterns, async cancellation conventions, logging conventions, and XML doc/comment expectations.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs` - Public ViewModel-facing contract to preserve while adding internal collaborators behind it.
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` - Current monolithic coordinator. It contains config flush, game/variant detection, skip-list filtering, MO2/file validation, cleaning state start, backup session setup, per-plugin backup choices, sequential xEdit loop, log-offset capture, log parsing, result construction, metadata/retention cleanup, stop/force-stop, dry-run, validation helpers, hang monitoring, and disposal.
- `AutoQAC/Services/Cleaning/CleaningService.cs` and `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs` - Existing single-plugin cleaning and command-build boundaries that should remain the process-launch path.
- `AutoQAC/Services/Backup/IBackupService.cs` and `AutoQAC/Services/Backup/BackupService.cs` - Existing backup, metadata, retention, restore/delete, progress, and safety APIs that the backup-session lifecycle collaborator should orchestrate rather than duplicate.
- `AutoQAC/Services/Process/IProcessExecutionService.cs` and `AutoQAC/Services/Process/ProcessExecutionService.cs` - Existing process launch/termination boundary and single process slot; Phase 8 must not bypass it.
- `AutoQAC/Services/Monitoring/IHangDetectionService.cs` - Existing CPU-based hang detection stream used by the current orchestrator and a likely dependency of the termination coordinator.
- `AutoQAC/Services/State/IStateService.cs` - Runtime state hub for cleaning progress, detailed results, termination state, backup operation state, and final session results.
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs` - Primary existing orchestrator test file to expand or use as a safety net for characterization coverage.

### Established Patterns
- Cleaning is sequential in two places: `CleaningOrchestrator` loops one plugin at a time and `ProcessExecutionService` enforces one process slot. Both protections must survive decomposition.
- ViewModels interact with cleaning through `ICleaningOrchestrator`; do not push new preflight, backup, execution, or termination services directly into ViewModels for this phase.
- Service-layer work is async and cancellation-aware. Keep cancellation tokens flowing through preflight, backup, process, and retention collaborators without blocking the UI thread.
- Recoverable workflow outcomes should use domain result models and state-service updates instead of relying on thrown exceptions for normal partial/canceled/failed cases.
- User-facing error/result text is concise; detailed exception/path/command diagnostics belong in logs unless a prior phase explicitly exposes them.

### Integration Points
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` - Register new cleaning collaborators through DI using narrow interfaces.
- `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` - Existing start, preview, stop, retry, and backup-failure callback caller should continue depending on `ICleaningOrchestrator`.
- `AutoQAC/ViewModels/ProgressViewModel.cs` - Existing progress stop, force-stop, and backup-operation cancel commands should continue calling the orchestrator facade.
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` lines around `StartCleaningAsync`, `RunDryRunAsync`, `StopCleaningAsync`, `ForceStopCleaningAsync`, and backup operation helpers are the main extraction targets.
- `AutoQAC.Tests/Services/`, `AutoQAC.Tests/ViewModels/`, and `AutoQAC.Tests/Integration/` - Use existing xUnit, FluentAssertions, NSubstitute, temp-directory, and helper-process patterns. Do not create a separate Avalonia.Headless project unless a later phase explicitly scopes it.

</code_context>

<specifics>
## Specific Ideas

- The target shape is a stable `ICleaningOrchestrator` facade with focused internal collaborators, not a public API reshuffle.
- The shared preflight plan is the key anti-drift mechanism between dry-run and real cleaning.
- The per-plugin runner/finalizer split should make log parsing/result finalization changeable without touching session-level sequencing.
- The termination coordinator should preserve Phase 5 semantics exactly while moving current-process, hang, and termination-result state out of the monolithic orchestrator.
- If a new bug is discovered while extracting, ask before fixing unless it blocks the refactor itself.

</specifics>

<deferred>
## Deferred Ideas

None - discussion stayed within phase scope.

</deferred>

---

*Phase: 08-cleaning-orchestrator-decomposition*
*Context gathered: 2026-04-29*
