# Phase 7: Backup Restore & Retention Safety - Context

**Gathered:** 2026-04-29
**Status:** Ready for planning

<domain>
## Phase Boundary

Phase 7 hardens existing backup restore, backup copy, and backup retention cleanup behavior so users can recover from backup/restore problems with clear complete, partial, failed, canceled, and warning outcomes. It is limited to restore safety, backup/restore/retention progress and cancellation, retention cleanup failure reporting, and regression coverage for those paths. It must preserve sequential xEdit cleaning, per-plugin xEdit launches, existing MO2 backup skip behavior, and the concise user-facing error direction from prior phases.

</domain>

<decisions>
## Implementation Decisions

### Restore Outcomes
- **D-01:** Restore All must continue attempting remaining plugins after an individual plugin restore failure, then report aggregate success/failure state instead of stopping on the first exception.
- **D-02:** If a backup entry's original target folder is missing, AutoQAC should recreate the folder when possible. Permission, path, or creation failures become restore failures for that plugin.
- **D-03:** Restore All user-facing outcomes must explicitly distinguish `Complete`, `Partial`, and `Failed` states. Include restored/failed counts and failed plugin names.
- **D-04:** Restore failure details visible in the restore UI should include each failed plugin plus a concise reason such as missing backup file, access denied, target folder creation failed, or target write failed. Technical exception details stay in logs.

### Progress and Cancellation
- **D-05:** Phase 7 progress and cancellation coverage includes backup copy work, Restore All work, and retention deletion work.
- **D-06:** Cleaning-session backups remain per-plugin immediately before that plugin's xEdit launch. Do not change to an all-upfront backup model.
- **D-07:** Users must be able to cancel an active backup or restore file copy rather than waiting only for the next file boundary.
- **D-08:** Partial files from a canceled copy must be deleted and must not count as restored or backed up.
- **D-09:** Cleaning backup and retention progress should appear in the existing cleaning progress surface. Restore progress should appear in the existing restore window.
- **D-10:** Copy/delete progress should show file counts and byte progress when available. Directory/session deletion can fall back to counts where byte progress is impractical.
- **D-11:** Backup, restore, and retention progress surfaces should expose a visible Cancel button. Do not overload the existing xEdit Stop button for non-xEdit file work.
- **D-12:** Retention cleanup after cleaning should complete, fail, or be canceled before the final session completion is reported, so cleanup deletion failures are visible to the user.

### Retention Cleanup
- **D-13:** If retention cleanup fails after cleaning and backup succeeded, classify the session as successful with a retention warning rather than failed or partial.
- **D-14:** Retention cleanup must never delete the current session and must keep the configured newest `MaxSessions` among the remaining sessions.
- **D-15:** When deletion of an old backup session fails, retry once for transient locks. If it still fails, keep the session and report it as not deleted.
- **D-16:** If the user cancels retention cleanup mid-run, stop remaining deletion work and report cleanup as canceled with deleted/skipped/remaining counts.

### Restore UX Safety
- **D-17:** Both Restore Selected and Restore All require confirmation before overwriting plugin files.
- **D-18:** Restore confirmation dialogs should show an overwrite summary: session timestamp, selected/all plugin count, and that current files will be overwritten by backups.
- **D-19:** After Restore All completes with any failures, keep the restore window open and show an inline result list with restored, failed, and canceled rows plus concise reasons.
- **D-20:** Successful restores should be acknowledged inline in the restore window with completion state, counts, and restored rows. Do not add extra success popups or auto-close the window.

### the agent's Discretion
- Exact result model names, progress event names, retry delay/backoff, native `CopyFileEx` versus managed stream-copy implementation, and UI layout details are left to research/planning.
- The planner may choose the smallest internal API shape that makes restore, backup, and retention operations return structured outcomes instead of relying on thrown exceptions for expected file-system failures.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Planning Scope
- `.planning/ROADMAP.md` - Phase 7 goal, dependencies, requirements, and success criteria.
- `.planning/REQUIREMENTS.md` - Requirements `SAF-04`, `TEST-04`, and `PERF-04` mapped to Phase 7.
- `.planning/PROJECT.md` - Project constraints: Windows-only app, sequential xEdit cleaning, read-only `Mutagen/`, and cleanup milestone intent.
- `.planning/STATE.md` - Current milestone state and carried-forward cleanup constraints.

### Prior Locked Decisions
- `.planning/phases/05-process-stop-pid-safety/05-CONTEXT.md` - Locks two-stage stop behavior, concise force-kill failure surfacing, and real process test harness expectations.
- `.planning/phases/06-command-launch-escaping/06-CONTEXT.md` - Locks concise user-facing failure messaging, process launch preservation, and no full command/path exposure in user-facing errors.

### Codebase Analysis
- `.planning/codebase/CONCERNS.md` - Identifies synchronous backup/retention operations, missing backup restore safety coverage, and the need for cancellable visible backup/retention work.
- `.planning/codebase/ARCHITECTURE.md` - Documents cleaning, backup, state, ViewModel, process, and UI service integration points.
- `.planning/codebase/TESTING.md` - Existing xUnit, FluentAssertions, NSubstitute, temp directory, async coordination, and integration test patterns.

### External Behavior References
- `https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-copyfileexa` - Windows copy progress/cancellation semantics, including partial destination cleanup behavior.
- `https://docs.percona.com/percona-backup-mongodb/troubleshoot/restore-partial.html` - Example of explicit `Done`, `Error`, and `Partly-Done` restore status modeling.
- `https://docs.aws.amazon.com/prescriptive-guidance/latest/backup-recovery/clean-up.html` - Backup cleanup strategy guidance: clean up only backups no longer required for recovery/retention.
- `https://blog.logrocket.com/ux-design/double-check-user-actions-confirmation-dialog` - Confirmation dialog guidance: state action, consequences, and clear choices.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `AutoQAC/Services/Backup/BackupService.cs` - Primary implementation target for backup copy, restore, session enumeration, and retention cleanup. Current restore/cleanup methods are synchronous and throw/log instead of returning structured outcomes.
- `AutoQAC/Services/Backup/IBackupService.cs` - Service contract that likely needs async, cancellable, progress-aware restore and cleanup APIs.
- `AutoQAC/Models/BackupSession.cs` and `AutoQAC/Models/BackupResult.cs` - Existing backup metadata and single-plugin backup result models to extend or parallel with restore/retention result models.
- `AutoQAC/ViewModels/RestoreViewModel.cs` and `AutoQAC/Views/RestoreWindow.axaml` - Existing restore UI surface for sessions, selected plugins, restore selected/all, delete session, and status text.
- `AutoQAC/ViewModels/ProgressViewModel.cs` and `AutoQAC/Views/ProgressWindow.axaml` - Existing cleaning progress surface that can expose backup/retention phases without creating a separate cleaning progress window.
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` - Current per-plugin backup and end-of-session retention cleanup integration point. Backups are invoked before each xEdit launch and retention runs before session results are emitted.
- `AutoQAC/Services/UI/MessageDialogService.cs` - Existing confirmation/error/backup-failure dialog service. Restore UX changes should keep ViewModels dialog-agnostic.
- `AutoQAC.Tests/Services/BackupServiceTests.cs` - Existing temp-directory backup, restore, session enumeration, and cleanup tests to expand with missing target, permission, partial restore, cancellation, and cleanup-deletion failure cases.

### Established Patterns
- xEdit cleaning stays sequential through `CleaningOrchestrator` and the single process slot in `ProcessExecutionService`; Phase 7 must not overlap xEdit launches.
- MO2 mode skips backups because MO2 uses a virtual filesystem; Phase 7 should preserve that runtime behavior unless requirements explicitly change later.
- ViewModels request dialogs through services/interactions rather than creating windows directly. Restore UI changes should stay in `RestoreViewModel`/`RestoreWindow` boundaries.
- Tests use xUnit, FluentAssertions, NSubstitute, temp directories, `TaskCompletionSource`, and `try/finally`/`IDisposable` cleanup for file-system and async cases.
- Prior phases prefer concise user-facing messages and logs for technical details. Do not surface raw stack traces or full configured paths in restore dialogs.

### Integration Points
- `CleaningOrchestrator` backup loop: preserve per-plugin backup before `CleaningService.CleanPluginAsync`; add progress/cancellation without allowing xEdit launch for a plugin whose backup was canceled or failed without user approval.
- `CleaningOrchestrator` retention block: replace synchronous `CleanupOldSessions` call with structured, progress-aware cleanup that can return success-with-warning or canceled cleanup outcome before final session completion.
- `RestoreViewModel.RestorePluginAsync` and `RestoreViewModel.RestoreAllAsync`: move from direct throwing restore calls to structured async restore results with confirmation, inline progress, cancellation, and result rows.
- `IBackupService`/DI registration in `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`: register any new file-copy/progress abstractions needed for testability.
- `IMessageDialogService`: restore confirmations and concise error summaries should use existing dialog service patterns unless the planner finds a smaller MVVM-safe interaction.

</code_context>

<specifics>
## Specific Ideas

- Treat restore like a recoverable batch operation: attempt all requested plugins, then report exactly what restored and what did not.
- Active copy cancellation is desired. Partial outputs should be deleted and never counted as successful.
- Retention cleanup is a separate reportable step: it can warn or cancel without invalidating a successful cleaning backup session.
- Restore UI should favor inline review over modal-only summaries so users can inspect partial failures without losing context.

</specifics>

<deferred>
## Deferred Ideas

None - discussion stayed within phase scope.

</deferred>

---

*Phase: 07-backup-restore-retention-safety*
*Context gathered: 2026-04-29*
