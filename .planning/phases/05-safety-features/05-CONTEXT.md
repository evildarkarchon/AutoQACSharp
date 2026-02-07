# Phase 5: Safety Features - Context

**Gathered:** 2026-02-07
**Status:** Ready for planning

<domain>
## Phase Boundary

Dry-run preview and plugin backup/rollback so users can trust the tool with their load order. Dry-run shows what would happen without invoking xEdit. Backup copies plugins before cleaning and allows restoration. Creating new cleaning modes, adding plugin analysis, or expanding xEdit integration are out of scope.

</domain>

<decisions>
## Implementation Decisions

### Dry-run presentation
- Reuse the existing progress window with a "Preview" banner — no separate window
- Each plugin shows status + reason: "Will clean" or "Will skip: [reason]" (skip list, file not found, master file, etc.)
- Triggered via a separate "Preview" / "Dry Run" button on the main window alongside the existing Clean button
- Inline disclaimer at the top of results: "Preview only — does not detect ITMs/UDRs (requires xEdit)"
- No xEdit invocation during dry-run — purely local validation logic

### Backup organization
- Session-based folders: each cleaning session gets a timestamped directory (e.g., `Backups/2026-02-07_14-30/`)
- Backup root lives next to the game's Data directory (sibling folder), not inside AutoQAC Data/
- Each plugin is backed up individually, right before xEdit opens it for cleaning (not all at once at session start)
- If backup copy fails (disk full, permissions), show a dialog asking the user whether to skip that plugin, abort the session, or continue without backup

### Restore experience
- Accessed via a "Restore Backups" button/menu on the main window (not buried in Settings)
- Two-level browser: first shows list of backup sessions (date + plugin count), then drill into a session to see individual plugins
- Restoring overwrites the current file in place with the backed-up version
- Both "Restore All" (whole session) and individual plugin restore supported
- Confirmation dialog required for "Restore All" session-wide restore; individual plugin restores proceed without confirmation

### Settings integration
- Backup enabled by default for new users
- Retention configured by session count (keep last N sessions, delete older ones)
- Fixed backup location (next to game Data/) — no user-configurable path
- No disk usage display — just the enable/disable toggle and retention count

### Claude's Discretion
- Default retention count (e.g., 5 or 10 sessions)
- Exact backup folder naming convention details
- Session metadata format (how to record which plugins were in a session)
- Progress window adaptations for preview mode vs cleaning mode
- Error handling for restore failures

</decisions>

<specifics>
## Specific Ideas

- Per-plugin backup timing: back up right before xEdit processes that plugin, not at session start — if session is interrupted partway, only relevant plugins have backups
- Backup failure dialog should offer three choices: skip plugin, abort session, continue without backup
- Dry-run button should be visually distinct from Clean but equally accessible

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 05-safety-features*
*Context gathered: 2026-02-07*
