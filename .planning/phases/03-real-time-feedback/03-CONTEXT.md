# Phase 3: Real-Time Feedback - Context

**Gathered:** 2026-02-06
**Status:** Ready for planning

<domain>
## Phase Boundary

Give users live cleaning progress with per-plugin stats and actionable error messages when the environment is misconfigured. This phase covers the progress UI during cleaning sessions and pre-clean validation messaging. It does NOT add new cleaning capabilities, backup features, or settings UI.

</domain>

<decisions>
## Implementation Decisions

### Progress display
- Live counter badges (ITM: X, UDR: X, Nav: X) next to the current plugin name, ticking up as stats are parsed
- Focus on the current plugin with counters; show a compact summary bar ("12/87 plugins -- 3 skipped, 1 failed") rather than a full queue list
- Plugin count progress bar (fills based on plugins completed out of total)
- Completed plugin stats persist through the entire session (accumulate below current plugin) until the next cleaning run starts

### xEdit output handling
- Do NOT display raw xEdit output in the progress window -- xEdit runs visibly and shows its own output
- Parse xEdit's log file after each plugin completes (not stdout redirection)
- Stats appear immediately when xEdit exits for a plugin
- If log file can't be parsed (missing, corrupted, unexpected format): show warning icon inline with tooltip explaining the parse failure, continue cleaning

### Error message design
- Pre-clean validation errors shown as inline error panel in the main window (non-blocking, not a modal dialog)
- Validation runs only when user clicks Clean (not on startup or settings change)
- Error messages include actionable fix steps: "xEdit not found at C:\...\SSEEdit.exe. Click Settings > xEdit Path to browse for it."
- Multiple validation errors shown all at once in the panel so the user sees everything that needs fixing

### Multi-plugin session flow
- On plugin failure (crash, timeout, parse error): mark failed, log it, automatically continue to next plugin (no pause/prompt)
- No time estimates -- plugin cleaning time varies too much to be useful
- Session completion: progress window transforms into a results summary (total cleaned, skipped, failed) with persisted per-plugin stats
- On cancel (Stop): show partial summary of completed plugins + clear indication of what was cancelled/remaining

### Claude's Discretion
- Exact layout/spacing of counter badges and summary bar
- Progress bar visual style (color, animation)
- Warning icon design and tooltip formatting
- How the progress window "transforms" into the results summary (animation, layout shift)
- Log file parsing implementation details (regex patterns, error recovery)
- Throttling strategy for UI updates with 100+ plugins

</decisions>

<specifics>
## Specific Ideas

- xEdit window is always visible during cleaning -- the user watches xEdit do its work, AutoQAC just tracks stats and manages the queue
- Stats parsed from xEdit log files post-completion, not from redirected stdout
- The progress window serves double duty: live progress during cleaning, then results summary after completion (no separate results window needed)

</specifics>

<deferred>
## Deferred Ideas

None -- discussion stayed within phase scope

</deferred>

---

*Phase: 03-real-time-feedback*
*Context gathered: 2026-02-06*
