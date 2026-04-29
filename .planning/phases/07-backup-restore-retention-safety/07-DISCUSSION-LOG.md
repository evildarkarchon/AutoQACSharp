# Phase 7: Backup Restore & Retention Safety - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md - this log preserves the alternatives considered.

**Date:** 2026-04-29
**Phase:** 07-backup-restore-retention-safety
**Areas discussed:** Restore outcomes, Progress cancellation, Retention cleanup, Restore UX safety

---

## Restore Outcomes

| Question | Options Presented | Selected | Notes |
|----------|-------------------|----------|-------|
| For Restore All, what should happen after one plugin fails to restore? | Continue remaining; Stop immediately; You decide | Continue remaining | Attempt every plugin and report aggregate complete/partial/failed state. |
| When a backup entry's original target folder is missing, how should restore handle it? | Recreate folder; Fail as missing; Ask each time | Recreate folder | Recreate when possible; report creation failures clearly. |
| What restore outcome labels should users see after Restore All finishes? | Complete partial failed; Counts only; Existing wording | Complete partial failed | Explicit labels are required so partial restore is not vague. |
| For restore failures, what detail should be visible immediately in the restore UI? | Plugin plus reason; Plugin names only; Full exception text | Plugin plus reason | Keep user-facing detail concise and plugin-scoped; logs keep technical exceptions. |

---

## Progress Cancellation

| Question | Options Presented | Selected | Notes |
|----------|-------------------|----------|-------|
| Which backup-related operations should get visible progress and cancellation in Phase 7? | Backup restore retention; Backup retention only; You decide | Backup restore retention | Includes backup copy, Restore All, and retention deletion work. |
| When should AutoQAC perform backups for a cleaning session? | Per plugin; All upfront; You decide | Per plugin | Preserve current model: back up immediately before the plugin's xEdit launch. |
| If the user cancels while a large backup or restore file is actively copying, what should happen? | Cancel active copy; Finish current file; Hybrid safety | Cancel active copy | Users should not have to wait for huge copies to finish. |
| After an active copy is canceled, how should AutoQAC treat partial files? | Delete partials; Leave partials; You decide | Delete partials | Partial copy outputs are cleanup artifacts, not valid backups/restores. |
| Where should backup, restore, and retention progress be shown? | Reuse windows; One progress window; Status text only | Reuse windows | Cleaning backup/retention uses progress window; restore uses restore window. |
| What progress units should users see for copy/delete work? | Files and bytes; File count only; Bytes only | Files and bytes | Use bytes when available, counts as fallback. |
| How should users cancel backup/restore/retention work from the UI? | Visible cancel button; Reuse Stop; Confirm cancel | Visible cancel button | Keep xEdit Stop semantics separate from file-operation cancellation. |
| When retention cleanup runs after cleaning, should session completion wait for it? | Wait and report; Background cleanup; Ask user | Wait and report | Session completion should include retention warnings/cancellation. |

---

## Retention Cleanup

| Question | Options Presented | Selected | Notes |
|----------|-------------------|----------|-------|
| If old-session cleanup fails after a successful cleaning backup session, how should the outcome be classified? | Success with warning; Session partial; Session failed | Success with warning | Retention failure should not imply plugin cleaning/backup failed. |
| What sessions should retention cleanup protect from deletion? | Current plus newest; Current only; All valid metadata | Current plus newest | Never delete current session; preserve configured newest sessions. |
| When deletion of an old backup session fails, what should AutoQAC do next? | Retry then warn; Warn only; Delete more old | Retry then warn | Retry transient locks once, then keep and report the session. |
| If the user cancels retention cleanup mid-run, what should happen? | Stop remaining; Finish cleanup; Delete current target | Stop remaining | Stop deletion work and report cleanup canceled with counts. |

---

## Restore UX Safety

| Question | Options Presented | Selected | Notes |
|----------|-------------------|----------|-------|
| Which restore actions should require confirmation before overwriting files? | All restore actions; Restore All only; Only existing targets | All restore actions | Restore Selected and Restore All both overwrite plugin files. |
| What should restore confirmation dialogs explicitly say? | Overwrite summary; Minimal warning; Detailed file list | Overwrite summary | Show session timestamp, plugin count, and overwrite consequence. |
| After Restore All completes with any failures, where should the user review the result? | Inline result list; Modal summary; Status bar only | Inline result list | Keep window open for failure review. |
| How should successful restores be acknowledged? | Inline completion; Success dialog; Close window | Inline completion | Avoid extra success popups and do not auto-close. |

---

## the agent's Discretion

- Exact result model names, progress event names, retry delay/backoff, native `CopyFileEx` versus managed stream-copy implementation, and UI layout details.

## Deferred Ideas

None.
