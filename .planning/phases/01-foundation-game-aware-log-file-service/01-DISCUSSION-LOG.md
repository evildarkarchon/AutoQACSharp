# Phase 1: Foundation -- Game-Aware Log File Service - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md -- this log preserves the alternatives considered.

**Date:** 2026-03-30
**Phase:** 01-foundation-game-aware-log-file-service
**Areas discussed:** xEdit log naming convention, Exception log exposure, Retry strategy

---

## xEdit Log Naming Convention

| Option | Description | Selected |
|--------|-------------|----------|
| I know the convention | User provides exact naming rules | |
| Research it | Let researcher verify from xEdit source/docs | ✓ |
| Use executable stem as-is | Keep current approach but fix casing | |

**User's choice:** Research it
**Notes:** The researcher should verify from xEdit source code whether log filenames are game-aware (e.g., `SSEEdit_log.txt` when running `xEdit.exe -SSE`) or based on the executable stem, and what casing rules apply.

---

## Exception Log Exposure

| Option | Description | Selected |
|--------|-------------|----------|
| Path only | Service just resolves path and checks existence | |
| Path + full content | Service reads entire exception log and returns it | |
| Path + first N lines | Service reads bounded amount to avoid edge cases | |

**User's choice:** Other (free text)
**Notes:** User clarified that the exception file doesn't get cleared between runs, so a tail-style / offset-based approach is needed — same pattern as the main log file. Capture byte offset before launch, read only new content after exit.

---

## Retry Strategy

| Option | Description | Selected |
|--------|-------------|----------|
| Light retry | 2 attempts, 200-400ms, ~600ms total | |
| Moderate retry | 3 attempts, 200-400-800ms exponential, ~1.4s total | |
| Patient retry | 4-5 attempts, up to 2-3s total | |
| You decide | Let Claude pick based on research | ✓ |

**User's choice:** You decide
**Notes:** Claude has discretion to choose retry count, delay pattern, and total timeout based on research into typical AV/indexer lock behavior on Windows.

---

## Claude's Discretion

- Retry strategy for file contention (OFF-04)

## Deferred Ideas

None -- discussion stayed within phase scope.
