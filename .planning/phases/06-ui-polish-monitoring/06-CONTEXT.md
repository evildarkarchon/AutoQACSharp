# Phase 6: UI Polish & Monitoring - Context

**Gathered:** 2026-02-07
**Status:** Ready for planning

<domain>
## Phase Boundary

Decompose the bloated MainWindowViewModel into focused sub-ViewModels, add an About dialog with version/build/update info, improve application logging at startup and session completion, and detect hung xEdit processes with inline user notification. No in-app log viewer -- users access xEdit log files on disk directly.

</domain>

<decisions>
## Implementation Decisions

### About dialog
- Classic centered layout: app icon at top, version info stacked below, links at bottom
- Info shown: app version, build date, .NET runtime version, key library versions (Avalonia, ReactiveUI)
- Links: GitHub repository, GitHub issues (bug report), xEdit project
- GitHub release check: button that hits GitHub API to compare current vs latest release tag

### Hang detection
- Threshold: 60 seconds of near-zero CPU usage before flagging xEdit as potentially hung
- Notification: inline warning banner in the progress window with action buttons (not a modal dialog)
- Actions offered: Wait or Kill (two simple choices)
- Auto-dismiss: if xEdit resumes CPU activity, automatically clear the warning

### Log viewer
- No in-app log viewer -- explicitly removed from scope
- xEdit log files remain accessible on disk for manual inspection
- Application logging improvements (startup info, session summary) still in scope

### ViewModel decomposition
- Split strategy: functional grouping by feature area (cleaning commands, settings state, plugin list management, etc.)
- Behavior changes: minor polish acceptable if obvious UX improvements surface during the split
- Test migration: existing tests migrated to target new sub-ViewModels directly

### Claude's Discretion
- Sub-ViewModel communication pattern (parent mediation vs shared reactive state)
- Exact decomposition boundaries (which properties/commands go where)
- Logging format and verbosity levels for startup/session summary
- About dialog visual polish (spacing, typography, icon size)
- Update check error handling and UI feedback

</decisions>

<specifics>
## Specific Ideas

- About dialog layout inspired by Visual Studio / Notepad++ style (classic centered)
- Hang detection warning should feel non-intrusive -- inline in progress window, not a popup that interrupts the user
- Auto-dismiss of hang warning when xEdit resumes provides a smooth experience without user needing to manually clear false positives

</specifics>

<deferred>
## Deferred Ideas

None -- discussion stayed within phase scope

</deferred>

---

*Phase: 06-ui-polish-monitoring*
*Context gathered: 2026-02-07*
