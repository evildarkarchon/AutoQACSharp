# Phase 4: Configuration Enhancement - Context

**Gathered:** 2026-02-07
**Status:** Ready for planning

<domain>
## Phase Boundary

Configuration management is efficient, validated, and gives users confidence their settings are correct before they start cleaning. Covers: real-time path validation in Settings, legacy Python config migration, YAML cache invalidation, and log/journal retention. No new Settings fields or new configuration capabilities — just making existing config robust and user-friendly.

</domain>

<decisions>
## Implementation Decisions

### Path validation feedback
- Red border + icon indicator style: text fields get red border with X icon when invalid, green border with checkmark when valid
- Validation triggers on debounced input (300-500ms after user pauses typing)
- Browse button selections validate immediately (no debounce — deliberate user action)
- All path fields in Settings are validated: xEdit path, MO2 path, load order path, data directory

### Legacy config migration
- Auto-detect legacy Python config files on every app startup
- Migration only runs when no C# config exists yet — one-time bootstrap, not a merge
- After successful migration: copy old files to a backup folder, then delete originals
- Migration failures show a non-modal warning banner at the top of the main window, persists until dismissed, with details on what failed

### Log/journal retention
- Users choose between age-based (days) OR count-based (keep last N) retention model — both options available in Settings
- Retention covers both app logs (Serilog output) and cleaning session journals
- Cleanup runs on app startup
- Ships with sensible defaults active out of the box (e.g., 30 days / 100 files) — user can adjust in Settings

### Config edit detection
- Silent reload when YAML config files change on disk — no user prompt, new values take effect automatically
- Invalid external edits rejected: keep previous known-good config, log a warning
- Config changes during an active cleaning session are deferred until the session ends
- Change detection uses content hashing (not file timestamps) for reliability

### Claude's Discretion
- Exact hash algorithm for config change detection
- Default retention values (suggested ~30 days / ~100 files but Claude can tune)
- Cache invalidation polling interval or FileSystemWatcher approach
- Migration backup folder location and naming convention
- Warning banner styling and dismiss behavior

</decisions>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 04-configuration-enhancement*
*Context gathered: 2026-02-07*
