# Phase 2: Plugin Pipeline Robustness - Context

**Gathered:** 2026-02-06
**Status:** Ready for planning

<domain>
## Phase Boundary

Every plugin in the load order has a verified real file path, and edge-case inputs are handled gracefully instead of silently failing. This phase resolves plugin path tech debt and validation gaps that block safety features (Phase 5 backup needs real paths to copy files).

Scope: path resolution, load order parsing robustness, game variant detection for skip lists, and clear error reporting for pipeline failures. No new UI dialogs, no new settings screens, no new cleaning features.

</domain>

<decisions>
## Implementation Decisions

### Path resolution strategy
- Missing plugins: warn and skip (show warning per plugin, continue cleaning the rest)
- MO2 users: resolve plugin paths through MO2's virtual filesystem (query mod list/profile to find real mod folder)
- Validation depth: check both existence AND readability (try opening file briefly to verify not locked or zero-byte)
- Known extensions only: accept .esp, .esm, .esl — reject anything else as malformed

### Malformed input handling
- MO2 separator lines (lines starting with `*` or containing separator markers): strip them, log at debug level
- Encoding: auto-detect common encodings (UTF-8, UTF-16, Latin-1) rather than requiring UTF-8
- Malformed entries (path separators, control characters, missing valid extension): warn and skip — log warning with bad line content, continue with valid entries
- No best-effort parsing of bad entries — if it doesn't look like a valid plugin name, skip it

### TTW / game variant detection
- TTW detection: check for TaleOfTwoWastelands.esm (or equivalent TTW marker plugin) in the load order
- TTW skip list behavior: auto-merge FO3 skip list entries into FNV list silently — no user prompt, no log noise
- Enderal: also needs special handling — detect via Enderal-specific plugin, maintain its own separate skip list in config (not inherited from Skyrim)
- These are the only two game variants needing special skip list logic for now

### Error communication
- GameType.Unknown: block cleaning completely — refuse to proceed without skip lists (safety critical)
- Multiple path failures: aggregated summary ("5 plugins not found: [list]") rather than one-per-plugin
- MO2 errors: include actionable fix guidance in error messages (e.g., "Check MO2 profile path in Settings")
- Missing/empty load order file: hard error with clear message pointing to the expected path

### Claude's Discretion
- Path caching strategy (session cache vs re-resolve per run)
- Exact BOM handling implementation details
- Internal error codes/categories for pipeline failures
- Order of validation steps in the pipeline

</decisions>

<specifics>
## Specific Ideas

- Error messages should be actionable — tell the user what's wrong AND where to fix it
- Aggregated summaries keep the UI clean when many plugins fail (common with misconfigured MO2 setups)
- TTW and Enderal are "silent" enhancements — users with those setups benefit automatically without configuration

</specifics>

<deferred>
## Deferred Ideas

- Browse dialog for missing load order file recovery — user wanted "Offer browse" but this requires a new UI dialog, belongs in Phase 4 or 6
- Additional game variant detection beyond TTW and Enderal — future phase if needed

</deferred>

---

*Phase: 02-plugin-pipeline-robustness*
*Context gathered: 2026-02-06*
