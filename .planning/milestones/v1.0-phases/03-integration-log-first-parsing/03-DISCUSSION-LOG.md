# Phase 3: Integration -- Log-First Parsing - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md -- this log preserves the alternatives considered.

**Date:** 2026-03-31
**Phase:** 03-integration-log-first-parsing
**Areas discussed:** Log reading wiring, Offset capture granularity, Force-kill handling, Nothing-to-clean detection, Exception log surfacing
**Mode:** Auto (all gray areas auto-selected, recommended defaults chosen)

---

## Log Reading Wiring

| Option | Description | Selected |
|--------|-------------|----------|
| Orchestrator reads logs, passes to parser | Keep log reading in orchestrator (already has game type, xEdit path, timing context) | auto |
| CleaningService reads logs | Move log reading into CleaningService alongside process execution | |
| New dedicated service | Create a post-processing service between orchestrator and parser | |

**User's choice:** [auto] Orchestrator reads logs, passes content to parser (recommended default)
**Notes:** Orchestrator already has the enrichment pattern at lines 400-423. Minimal change path.

---

## Offset Capture Granularity

| Option | Description | Selected |
|--------|-------------|----------|
| Per-plugin | Capture offset before each plugin clean, read after each | auto |
| Per-session | Capture once at session start, parse all at end | |

**User's choice:** [auto] Per-plugin (recommended default)
**Notes:** Each xEdit launch appends to the same log file. Per-plugin offset is necessary to isolate each plugin's output.

---

## Force-Kill Handling

| Option | Description | Selected |
|--------|-------------|----------|
| Check termination status, skip log parse, return failure | If force-killed, don't attempt log read | auto |
| Always attempt log read | Read log regardless, handle empty/incomplete gracefully | |
| Check exit code only | Use non-zero exit code as the signal | |

**User's choice:** [auto] Check termination status, skip log parse, return failure (recommended default)
**Notes:** Force-killed xEdit may not flush log. TerminationResult is already tracked in orchestrator.

---

## Nothing-to-Clean Detection

| Option | Description | Selected |
|--------|-------------|----------|
| Completion line + zero stats = NothingToClean | Distinct from Failed and Skipped statuses | auto |
| Report as Cleaned with zero counts | Keep existing status, just show zeros | |
| Report as Skipped | Reuse Skipped status with different message | |

**User's choice:** [auto] Completion line + zero stats = NothingToClean (recommended default)
**Notes:** Users should see "nothing to clean" instead of misleading zeros. Distinct status improves UX clarity.

---

## Exception Log Surfacing

| Option | Description | Selected |
|--------|-------------|----------|
| Use PluginCleaningResult.LogParseWarning | Existing field, no model changes needed | auto |
| Add new ExceptionContent field | Explicit field on PluginCleaningResult | |
| Surface via CleaningResult.Message | Append exception text to the message string | |

**User's choice:** [auto] Use existing LogParseWarning field (recommended default)
**Notes:** PluginCleaningResult.LogParseWarning already exists for log parse issues. Natural fit for exception content.

---

## Claude's Discretion

- Multi-pass QAC aggregation policy (sum vs. report last)
- Exact CleaningStatus enum value for nothing-to-clean
- Whether to extend CleaningStatistics with a HasCompletionLine flag

## Deferred Ideas

- None -- discussion stayed within phase scope
