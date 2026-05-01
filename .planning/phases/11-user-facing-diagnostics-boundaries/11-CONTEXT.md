# Phase 11: user-facing-diagnostics-boundaries - Context

**Gathered:** 2026-04-30
**Status:** Ready for planning

<domain>
## Phase Boundary

Phase 11 hardens existing AutoQAC diagnostics so user-facing dialogs, inline validation/status text, cleaning results, exported cleaning reports, migration/restore/session-loading messages, and launch/startup logs stop leaking stack traces, raw exception text, full local paths, and command payloads. This phase preserves existing cleaning, launch, backup, restore, and plugin-refresh behavior; it clarifies safe diagnostic text and log emission boundaries only.

</domain>

<spec_lock>
## Requirements (locked via SPEC.md)

**6 requirements are locked.** See `11-SPEC.md` for full requirements, boundaries, and acceptance criteria.

Downstream agents MUST read `11-SPEC.md` before planning or implementing. Requirements are not duplicated here.

**In scope (from SPEC.md):**
- Existing user-facing error diagnostics in modal dialogs, inline validation panels, status text, cleaning result messages, and exported cleaning reports.
- Existing known leak paths found during scouting: cleaning/preview unexpected failures, configuration path browse failures, inline path validation, restore session load status, legacy migration warnings, startup diagnostics, and process-start diagnostics.
- Regression tests that prove safe user-facing diagnostics and log command boundaries.
- Preservation of existing safe category patterns from `ConfigPersistenceFailure.SafeSummary` and `BackupFailureReason` labels.
- Allowing plugin file names, game names, operation names, safe failure categories, counts, durations, PIDs, and latest AutoQAC log guidance in user-facing text when useful.

**Out of scope (from SPEC.md):**
- Adding a new diagnostics window, log viewer, telemetry, crash reporter, or issue-report exporter - this phase hardens existing surfaces only.
- Redacting xEdit-owned log files, backup file contents, user YAML file contents, or external tool output at rest - this phase controls AutoQAC UI/export/log emission.
- Removing every full path from AutoQAC logs - logs may keep a full path when it directly identifies the failing local resource needed for troubleshooting.
- Rewriting all application copy or normal progress/status text unrelated to errors - the phase is diagnostics-boundary focused.
- Changing xEdit/MO2 launch behavior, command construction, sequential cleaning, backup safety semantics, or plugin refresh behavior - adjacent behavior remains unchanged except for diagnostic text/log output.
- SEC-03 executable-name warnings - that requirement is explicitly future work.

</spec_lock>

<decisions>
## Implementation Decisions

### Error wording
- **D-01:** Unexpected cleaning and preview failures use operation-specific safe copy plus latest AutoQAC log guidance. The message should identify the failed operation and next action without raw exception text, stack traces, full paths, or command fragments.
- **D-02:** Modal dialog details for unexpected technical failures are safe details only. Details may repeat next-action/latest-log guidance, but must not contain stack traces, exception messages, paths, or command fragments.
- **D-03:** Status text after unexpected failures uses short operation status, such as cleaning/preview failed plus latest-log guidance. Do not put `ex.Message` into status text.
- **D-04:** Preserve existing safe typed labels from prior phases, especially `ConfigPersistenceFailure.SafeSummary` and `BackupFailureReason` display labels. Do not flatten those already-safe, user-actionable categories into generic text.

### Path identifiers
- **D-05:** Missing configured executable and load-order validation text should show the setting/resource plus a safe basename, such as `xEdit Path (SSEEdit.exe)` or `Load Order File (plugins.txt)`, never the directory path.
- **D-06:** Selected folder problems should show the game plus folder label, such as `Skyrim SE data folder` or `selected game data folder`, instead of a full folder path.
- **D-07:** Basenames shown in user-facing text must be sanitized display names. Remove or neutralize control characters and command-like formatting while preserving useful names such as `Plugin.esp`.
- **D-08:** Latest-log guidance appears on path-related surfaces only for technical failures such as parse/read/unexpected errors. Simple missing-path validation should provide the safe identifier and the fix action without extra log guidance.

### Result exports
- **D-09:** Failed plugin rows and exported reports show plugin filename plus a safe failure summary/status and latest-log guidance. Counts, durations, and other already-safe facts may remain.
- **D-10:** Failed `PluginCleaningResult.Message` values must be sanitized at source before they reach result windows or `CleaningSessionResult.GenerateReport()`. Report/export code may still keep defensive checks, but source-created result messages are the primary safety boundary.
- **D-11:** xEdit exception-log detection appears as a safe xEdit failure for the plugin, such as `xEdit reported an error for Plugin.esp. See the latest AutoQAC log.` Do not surface exception-log content in result rows or reports.
- **D-12:** Exported cleaning reports should include one short disclaimer that technical details are intentionally kept in AutoQAC logs, not repeated in exported reports.

### Log redaction
- **D-13:** Process-start and startup logs replace full executable paths and command payloads with structured safe fields: operation, launch mode, game, plugin filename, PID when available, argument count, counts/status, and safe reason/category.
- **D-14:** AutoQAC logs may keep a full local path only when that path is the direct file/folder resource that failed and omitting it would materially reduce local troubleshooting value.
- **D-15:** Normal workflow logs should identify plugins by filename plus game/mode context, not by full plugin path. A full plugin path is allowed only when the plugin file path itself is the direct failing resource.
- **D-16:** Phase 11 should prove log boundaries with captured logger tests or equivalent behavior/source assertions that fail on full executable paths, raw argv/nested payloads, and command fragments in startup/process-start diagnostics.

### the agent's Discretion
- Exact helper/service names, exact message strings, exact placeholder wording, and whether sanitization is implemented through a shared formatter or narrowly scoped helpers are planner discretion as long as D-01 through D-16 and `11-SPEC.md` are preserved.
- Exact test file organization is planner discretion, but tests must cover the surfaces named in `11-SPEC.md` and the log assertions in D-16.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase Scope And Requirements
- `.planning/phases/11-user-facing-diagnostics-boundaries/11-SPEC.md` - Locked Phase 11 requirements, boundaries, constraints, acceptance criteria, and interview decisions. MUST read first.
- `.planning/ROADMAP.md` - Phase 11 goal, dependency on Phase 10, SEC-01/SEC-02 mapping, and success criteria.
- `.planning/REQUIREMENTS.md` - SEC-01 and SEC-02 definitions and traceability.
- `.planning/PROJECT.md` - Cleanup milestone intent, project constraints, and carried-forward decisions such as sequential xEdit cleaning, MVVM boundaries, and prior safe diagnostics decisions.

### Prior Locked Decisions
- `.planning/phases/10-configuration-persistence-hardening/10-CONTEXT.md` - Typed persistence failure payloads, `SafeSummary`, ViewModel mapping, and pre-cleaning failure surfacing.
- `.planning/phases/09-plugin-refresh-approximation-performance/09-CONTEXT.md` - Service-owned status publication and ViewModel mapping of typed outcomes into user-facing text.
- `.planning/phases/08-cleaning-orchestrator-decomposition/08-CONTEXT.md` - Stable cleaning facade, preserved user-visible behavior, and concise user-facing messages with detailed diagnostics in logs.
- `.planning/phases/07-backup-restore-retention-safety/07-CONTEXT.md` - Restore/delete safe failure copy, `BackupFailureReason` category labels, and service-owned filesystem safety patterns.
- `.planning/phases/06-command-launch-escaping/06-CONTEXT.md` - Exact argv preservation, MO2 nested argument sensitivity, and command/payload redaction expectations.

### Codebase Constraints And Existing Patterns
- `.planning/codebase/CONCERNS.md` - Phase-driving security concerns around user-configured process launches, config/log path disclosure, and relevant fragile areas.
- `.planning/codebase/ARCHITECTURE.md` - MVVM/service boundaries, primary cleaning/dry-run paths, process execution, logging, and error handling patterns.
- `.planning/codebase/CONVENTIONS.md` - C# style, service/ViewModel conventions, logging conventions, comments, XML docs, and test naming expectations.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `AutoQAC/Services/Configuration/ConfigPersistenceStatus.cs` - Existing `ConfigPersistenceFailure.SafeSummary` payload establishes the safe typed failure pattern to preserve and reuse conceptually.
- `AutoQAC/Models/BackupOperationResults.cs` - `BackupFailureReasonExtensions.ToDisplayLabel()` provides approved concise restore/retention labels.
- `AutoQAC/Services/Cleaning/CleaningService.cs` - Existing launch/build failure messages already avoid command payloads and can guide safe per-plugin failure wording.
- `AutoQAC/Services/UI/IMessageDialogService.cs` and `MessageDialogService.cs` - Modal dialog boundary for safe title/message/details values.
- `AutoQAC/Models/ValidationError.cs` - Inline validation row model for safe title/message/fix-step path validation copy.
- `AutoQAC/Models/CleaningSessionResult.cs` and `PluginCleaningResult.cs` - Export/result boundary where failed plugin messages currently flow into reports.
- `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs` - Converts process/log results into final plugin result messages and currently carries exception-log content into warnings.
- `AutoQAC/Services/Process/ProcessExecutionService.cs` - Process-start logging currently summarizes argument-list count but still logs `FileName`; this is a primary log-boundary target.

### Established Patterns
- ViewModels should map service results into user-facing text and must not expose raw exceptions directly.
- Recoverable workflow failures should use typed safe result models where possible, not raw thrown exception text.
- `ILoggingService` is the logging abstraction; structured log templates with named placeholders are the existing pattern.
- Direct and MO2 command construction must preserve exact argv intent while avoiding full command reconstruction in user-facing text or logs.
- Sequential xEdit cleaning, one process slot, stop/force-stop semantics, and log parsing behavior are fixed and not part of this phase.
- Existing tests use xUnit, FluentAssertions, NSubstitute, captured substitutes, temp files/directories, and source-level guards where behavior is hard to observe.

### Integration Points
- `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` - Unexpected `StartCleaningAsync` and `PreviewAsync` catches currently put `ex.Message` in status text and `ex.StackTrace` in dialog details; `ValidatePreClean()` exposes full xEdit/MO2/load-order paths.
- `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs` - Load-order and game-data folder browse errors currently show selected full paths and raw exception messages; migration warning display is fed from startup migration results.
- `AutoQAC/ViewModels/RestoreViewModel.cs` - `LoadSessions()` currently puts `ex.Message` into status text; restore/delete paths already show useful safe patterns.
- `AutoQAC/Services/Configuration/LegacyMigrationService.cs` and `AutoQAC/App.axaml.cs` - Legacy migration warnings and unexpected migration failures can expose raw exception messages.
- `AutoQAC/Models/CleaningSessionResult.cs` - `GenerateReport()` writes failed `result.Message` directly, so unsafe result messages become export leaks.
- `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs` - Exception log content currently flows into `LogParseWarning`; downstream agents must ensure user-facing result/report paths stay safe.
- `AutoQAC/App.axaml.cs` - Startup diagnostics currently log the full configured xEdit path.
- `AutoQAC/Services/Process/ProcessExecutionService.cs` - Process-start/failure logs currently include `startInfo.FileName`; legacy `Arguments` can still be raw if no `ArgumentList` exists.
- `AutoQAC.Tests/ViewModels/ErrorDialogTests.cs`, `MainWindowViewModelTests.cs`, `RestoreViewModelTests.cs`, `Services/CleaningServiceTests.cs`, `Services/Configuration/ConfigPersistenceCoordinatorTests.cs`, and `Services/Cleaning/CleaningPreflightTests.cs` - Existing tests to tighten or mirror for diagnostics-boundary regression coverage.

</code_context>

<specifics>
## Specific Ideas

- Use wording shaped like `Cleaning failed. See the latest AutoQAC log for technical details.` and equivalent operation-specific variants.
- Use safe identifiers shaped like `xEdit Path (SSEEdit.exe)`, `MO2 Path (ModOrganizer.exe)`, `Load Order File (plugins.txt)`, and `{Game} data folder`.
- Treat latest-log guidance as an action hint, not a full log path. Do not expose the log file path in user-facing text.
- Preserve plugin filenames in rows/reports because users need to know which plugin failed; pair filenames with game/mode context in logs when full paths are removed.
- Export reports should say technical details were kept in AutoQAC logs and should not repeat stack traces, raw exception text, full paths, command fragments, or xEdit exception-log content.
- For logs, prefer structured safe fields over placeholder-heavy templates so troubleshooting remains useful without exposing launch paths or raw payloads.

</specifics>

<deferred>
## Deferred Ideas

None - discussion stayed within phase scope. New diagnostics windows, log viewers, crash reporting, telemetry, issue-report export, and SEC-03 executable-name warnings remain out of scope per `11-SPEC.md`.

</deferred>

---

*Phase: 11-user-facing-diagnostics-boundaries*
*Context gathered: 2026-04-30*
