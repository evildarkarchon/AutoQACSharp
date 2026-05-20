---
id: T01
parent: S07
milestone: M001
provides:
  - Direct xEdit command construction using parsed ArgumentList tokens
  - MO2 wrapper command contract preserving run, xEdit path, -a, and one nested payload
  - Curated command escaping regression tests for quotes, Unicode, shell-sensitive punctuation, and worst-case plugin names
requires: []
affects: []
key_files: []
key_decisions: []
patterns_established: []
observability_surfaces: []
drill_down_paths: []
duration: 23 min
verification_result: passed
completed_at: 2026-04-29
blocker_discovered: false
---
# T01: 06-command-launch-escaping 01

**# Phase 06 Plan 01: Command Builder ArgumentList Contracts Summary**

## What Happened

# Phase 06 Plan 01: Command Builder ArgumentList Contracts Summary

**Direct xEdit and MO2 command builders now use parsed argv contracts with targeted quote, Unicode, shell-punctuation, and nested-payload regression tests.**

## Performance

- **Duration:** 23 min
- **Started:** 2026-04-29T04:23:00Z
- **Completed:** 2026-04-29T04:46:37Z
- **Tasks:** 2/2 complete
- **Files modified:** 2

## Accomplishments

- Replaced direct-mode `Arguments` command-string construction with `ProcessStartInfo.ArgumentList` tokens for game flags, `-QAC`, `-autoexit`, `-autoload`, exact plugin filename, and partial-form flags.
- Preserved MO2's locked `run <xEdit> -a <payload>` shape while moving the outer MO2 launch to `ArgumentList` and adding a CRT-style nested formatter for the single `-a` value.
- Added TDD regression coverage for direct and MO2 difficult-character cases: spaces, embedded quotes, accented/CJK/emoji Unicode, shell-sensitive punctuation, and a combined worst-case plugin name.

## Task Commits

Each task was committed atomically:

1. **Task 1: Add command-builder argv contract tests** - `5b57346` (test)
2. **Task 2: Implement ArgumentList-based direct and MO2 command construction** - `220e885` (feat)

**Plan metadata:** pending final docs commit

_Note: This was a TDD plan segment: RED test commit followed by GREEN implementation commit._

## Files Created/Modified

- `AutoQAC.Tests/Services/XEditCommandBuilderTests.cs` - Replaced string-fragment command assertions with `ArgumentList` contract tests and nested formatter parser checks.
- `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs` - Builds direct and MO2 process launches using `ArgumentList`; adds nested MO2 quote/backslash formatter.

## Decisions Made

- Direct `-autoload` is intentionally represented as separate parsed argv tokens (`-autoload`, exact plugin filename), matching the plan's locked review resolution while retaining the documented xEdit compatibility risk for later smoke validation.
- MO2's nested `-a` payload remains one string because MO2 owns a second parser boundary; direct xEdit mode does not use this formatter.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- An initial targeted verification run overlapped with the full-suite run and hit a transient build output file lock. Re-running the targeted command after the full suite completed passed.

## Known Stubs

None.

## User Setup Required

None - no external service configuration required.

## Threat Flags

None - changes stay within the planned local process argv boundary and do not add new network, auth, file-access, or schema trust surfaces.

## TDD Gate Compliance

- RED gate: `5b57346` added failing command-builder argv contract tests.
- GREEN gate: `220e885` implemented the command builder changes and made the tests pass.
- REFACTOR gate: Not needed; no behavior-preserving cleanup commit was required after GREEN.

## Verification

- `dotnet test AutoQACSharp.slnx --filter FullyQualifiedName~XEditCommandBuilderTests` — PASS (12 AutoQAC command-builder tests; QueryPlugins project had no matching tests).
- `dotnet test AutoQACSharp.slnx` — PASS (59 QueryPlugins.Tests, 669 AutoQAC.Tests).

## Self-Check: PASSED

- Found modified files: `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`, `AutoQAC.Tests/Services/XEditCommandBuilderTests.cs`.
- Found summary file: `.planning/phases/06-command-launch-escaping/06-01-SUMMARY.md`.
- Found task commits: `5b57346`, `220e885`.

## Next Phase Readiness

Plan 06-01 is ready for Plan 06-02 to preserve `ArgumentList` through `ProcessExecutionService`'s real process-start boundary.

---
*Phase: 06-command-launch-escaping*
*Completed: 2026-04-29*
