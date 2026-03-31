# Phase 4: Cleanup -- Remove Dead Code - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md -- this log preserves the alternatives considered.

**Date:** 2026-03-31
**Phase:** 04-cleanup-remove-dead-code
**Areas discussed:** Obsolete method removal, OutputParser dependency cleanup, IProgress removal scope, ProcessResult field handling

---

## Obsolete Method Removal

| Option | Description | Selected |
|--------|-------------|----------|
| Delete everything | Remove ReadLogFileAsync, GetLogFilePath(string) from service + interface, delete all 3 legacy tests. Clean cut. | ✓ |
| Keep as deprecated longer | Leave the [Obsolete] methods for one more milestone in case something was missed. | |

**User's choice:** Delete everything (Recommended)
**Notes:** No hesitation -- clean cut preferred.

---

## OutputParser Dependency Cleanup

| Option | Description | Selected |
|--------|-------------|----------|
| CleaningService only | Remove IXEditOutputParser from CleaningService constructor, its test mock, and DI wiring. Orchestrator keeps it -- it's actively used there. | ✓ |
| Also refactor orchestrator | Additionally restructure how the orchestrator gets the parser (e.g., method injection instead of constructor). More churn, debatable value. | |
| You decide | Claude picks the approach that matches the codebase patterns best. | |

**User's choice:** CleaningService only (Recommended)
**Notes:** Minimal scope -- don't touch what's actively working.

---

## IProgress Removal Scope

| Option | Description | Selected |
|--------|-------------|----------|
| Remove entirely | Delete IProgress<string> param from IProcessExecutionService, ProcessExecutionService, ICleaningService, CleaningService, and all test call sites. No stdout means no progress to report. | ✓ |
| Keep on ProcessExecutionService | Remove from CleaningService layer but keep on ProcessExecutionService in case future process types need it. | |
| You decide | Claude picks based on whether any caller ever passes a non-null progress. | |

**User's choice:** Remove entirely (Recommended)
**Notes:** No future use case identified -- clean removal.

---

## ProcessResult Field Handling

| Option | Description | Selected |
|--------|-------------|----------|
| Remove both fields | Startup failure already throws/logs the exception. ErrorLines is redundant -- the caller catches the exception. Remove both fields from the record. | ✓ |
| Remove OutputLines, keep ErrorLines | ErrorLines on startup failure is a useful diagnostic even if redundant. Keep it as a safety net. | |
| You decide | Claude checks whether anything reads ErrorLines and decides. | |

**User's choice:** Remove both fields (Recommended)
**Notes:** Both fields are redundant given exception propagation.

---

## Claude's Discretion

- Order of removals within the plan (dependency-safe sequencing)
- Cleanup of unused imports after removals
- DI registration updates in ServiceCollectionExtensions.cs

## Deferred Ideas

None -- discussion stayed within phase scope.
