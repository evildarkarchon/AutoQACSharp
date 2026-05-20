---
id: S07
parent: M001
milestone: M001
provides:
  - Direct xEdit command construction using parsed ArgumentList tokens
  - MO2 wrapper command contract preserving run, xEdit path, -a, and one nested payload
  - Curated command escaping regression tests for quotes, Unicode, shell-sensitive punctuation, and worst-case plugin names
  - ProcessExecutionService clone preserving ArgumentList through real Process.Start
  - argv-echo helper mode emitting UTF-8 JSON argv evidence
  - Integration coverage for quotes, Unicode, shell-sensitive characters, combined worst-case argv, and legacy Arguments fallback
  - Safe command-build failure messages for direct xEdit and MO2 mode
  - Regression tests proving command-build failures do not start processes
  - Mocked launch-start failure coverage for existing concise failed flow
  - MO2 command-build failure when MO2 mode lacks a usable executable path.
  - Safe generic cleaning exception messages that keep launch details in logs.
  - Regression tests covering both Phase 6 verification gaps.
requires: []
affects: []
key_files: []
key_decisions:
  - Direct xEdit launch now uses split parsed argv tokens for -autoload and the exact plugin file name, accepting the Phase 6 A2 compatibility assumption documented in research/reviews.
  - MO2 mode keeps one nested -a payload, but only after the outer MO2 argv is built with ArgumentList.
  - ProcessExecutionService now clones ArgumentList entries when present and only falls back to Arguments for legacy callers with an empty ArgumentList.
  - Process-start debug logging reports ArgumentList entry count instead of relying on or disclosing a full argument string.
  - Command-build failures now name plugin and launch mode while stating no process was started and pointing users to logs instead of exposing configured paths or command lines.
  - Mocked launch-start failures remain in the existing `xEdit exited with code -1` failed flow without adding a new session policy for Phase 6.
  - MO2 mode now fails command construction when Mo2ExecutablePath is null, empty, or whitespace instead of falling back to direct xEdit.
  - Unexpected cleaning exceptions are logged with technical details but return a fixed plugin-scoped user message.
patterns_established:
  - Direct launch arguments are added as data tokens, never quote-decorated command fragments.
  - MO2 nested xEdit payloads use a documented formatter because MO2 owns a second parser boundary.
  - Real process-boundary tests capture redirected stdout through one reader started in onProcessStarted.
  - Helper argv evidence is serialized as UTF-8 JSON and parsed by tests rather than compared as delimiter-sensitive text.
  - Failure-flow tests assert against concrete configured xEdit/MO2 path values to prevent accidental user-facing disclosure.
  - CleaningService snapshots AppState once before command building so launch-mode failure text matches the command-build mode.
  - MO2 mode is an exclusive launch branch: missing MO2 configuration returns null rather than direct-starting xEdit.
  - Generic cleaning exceptions use concise user-facing messages and preserve details only in logger.Error.
observability_surfaces: []
drill_down_paths: []
duration: 16min
verification_result: passed
completed_at: 2026-04-29
blocker_discovered: false
---
# S07: Command Launch Escaping

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

# Phase 06 Plan 02: Process Boundary ArgumentList Preservation Summary

**ProcessExecutionService now preserves parsed ArgumentList argv through its internal clone and verifies exact helper-process receipt for quotes, Unicode, shell punctuation, and legacy Arguments fallback.**

## Performance

- **Duration:** 5 min
- **Started:** 2026-04-29T04:48:40Z
- **Completed:** 2026-04-29T04:53:47Z
- **Tasks:** 2/2 complete
- **Files modified:** 3

## Accomplishments

- Added `argv-echo` to the existing `AutoQAC.TestProcessHelper`, emitting `args.Skip(1)` as UTF-8 JSON for delimiter-safe argv proof.
- Added integration tests proving exact parsed argv receipt for embedded quotes, accented/CJK/emoji Unicode, shell-sensitive punctuation, and a combined worst-case plugin-style value.
- Updated `ProcessExecutionService` to clone `ArgumentList` entries exactly when present while preserving legacy `Arguments` behavior when `ArgumentList` is empty.

## Task Commits

Each task was committed atomically:

1. **Task 1: Add argv-echo helper mode and failing preservation tests** - `54f3f52` (test)
2. **Task 2: Preserve ArgumentList in ProcessExecutionService clone** - `51304b4` (feat)

**Plan metadata:** pending final docs commit

_Note: This was a task-level TDD flow: RED test/helper commit followed by GREEN implementation commit._

## Files Created/Modified

- `AutoQAC.Tests/TestProcessHelper/Program.cs` - Adds `argv-echo` mode with UTF-8 JSON stdout.
- `AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs` - Adds real helper-process assertions for ArgumentList preservation and legacy Arguments fallback.
- `AutoQAC/Services/Process/ProcessExecutionService.cs` - Clones `ArgumentList` entries through the launch boundary and summarizes ArgumentList starts without full command-line disclosure.

## Decisions Made

- `ArgumentList` entries are authoritative when present; `Arguments` is copied only for legacy callers with an empty `ArgumentList`, matching Microsoft guidance that the two APIs should not be mixed.
- Debug process-start logging now reports an ArgumentList entry count for parsed-argv launches, avoiding stale reliance on `startInfo.Arguments` and avoiding broader full-command disclosure.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- The RED verification failed as expected before Task 2: the helper exited with code 2 because the process-service clone dropped `ArgumentList`, so `argv-echo` never reached the helper.

## Known Stubs

None. Stub scan produced only nullable/default parameter false positives in existing process/test code; no placeholder UI/data stubs were added.

## User Setup Required

None - no external service configuration required.

## Threat Flags

None - changes stay within the planned local process-start and test-helper stdout trust boundaries.

## TDD Gate Compliance

- RED gate: `54f3f52` added helper-process argv preservation tests that failed before the service clone copied `ArgumentList`.
- GREEN gate: `51304b4` implemented `ArgumentList` preservation and made the targeted tests pass.
- REFACTOR gate: Not needed; no behavior-preserving cleanup commit was required after GREEN.

## Verification

- `dotnet test AutoQACSharp.slnx --filter FullyQualifiedName~ProcessExecutionIntegrationTests` — RED expected failure after Task 1, then PASS after Task 2 (7 AutoQAC process integration tests; QueryPlugins project had no matching tests).
- `dotnet test AutoQACSharp.slnx` — PASS (59 QueryPlugins.Tests, 671 AutoQAC.Tests).

## Self-Check: PASSED

- Found summary file: `.planning/phases/06-command-launch-escaping/06-02-SUMMARY.md`.
- Found task commits: `54f3f52`, `51304b4`.
- Found modified files: `AutoQAC/Services/Process/ProcessExecutionService.cs`, `AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs`, `AutoQAC.Tests/TestProcessHelper/Program.cs`.

## Next Phase Readiness

Plan 06-02 is ready for Plan 06-03 to integrate safe command-build failure messaging and final Phase 6 verification.

---
*Phase: 06-command-launch-escaping*
*Completed: 2026-04-29*

# Phase 06 Plan 03: Safe Command-Build Failure Flow Summary

**CleaningService now fails direct and MO2 command-build errors before process start with concise plugin/mode messages, structured logs, and regression coverage for non-disclosure.**

## Performance

- **Duration:** 2 min
- **Started:** 2026-04-29T04:56:09Z
- **Completed:** 2026-04-29T04:58:03Z
- **Tasks:** 2/2 complete
- **Files modified:** 3

## Accomplishments

- Added TDD coverage for direct xEdit and MO2 command-build failures, including concrete negative assertions for configured executable paths, nested payload text, `run `, and `-a`.
- Added a mocked launch-start failure regression that keeps `ProcessResult { ExitCode = -1 }` on the existing failed flow with `xEdit exited with code -1` and no path/command disclosure.
- Updated `CleaningService` to snapshot `AppState` once, derive launch mode from the same snapshot used for command building, log a structured warning, and return a concise no-process-started failure message.
- Completed Phase 6 targeted regression verification and full solution verification.

## Task Commits

Each task was committed atomically:

1. **Task 1: Lock command-build failure behavior in CleaningService tests** - `fc66b18` (test)
2. **Task 2: Implement concise command-build failure messaging and final verification** - `86fda99` (feat)

**Plan metadata:** pending final docs commit

_Note: This plan followed TDD gates: RED test commit followed by GREEN implementation commit._

## Files Created/Modified

- `AutoQAC.Tests/Services/CleaningServiceTests.cs` - Adds direct and MO2 command-build failure tests plus mocked launch-start failure disclosure assertions.
- `AutoQAC/Services/Cleaning/CleaningService.cs` - Snapshots state once and maps null command builds to concise plugin/mode/no-process failure results with structured warning logs.
- `.planning/phases/06-command-launch-escaping/06-03-SUMMARY.md` - Documents completed work, verification, and phase readiness.

## Decisions Made

- Command-build failures stay in the existing `CleaningStatus.Failed` result flow and do not introduce new per-plugin or session policy.
- User-facing command-build failure text includes plugin filename, `direct xEdit` or `MO2`, `No process was started`, and `See logs for technical details`, but omits configured paths and full command-line fragments.
- Real xEdit `-autoload` split-argv compatibility remains the accepted Phase 6 assumption from Plan 06-01; no real xEdit/MO2 manual smoke test was available during automated execution.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- RED verification failed as expected after Task 1: direct and MO2 command-build failure tests received the old generic `Failed to build xEdit command.` message.

## Known Stubs

None.

## User Setup Required

None - no external service configuration required.

## Threat Flags

None - changes stay within the planned local command-build failure and process-launch failure result boundaries.

## TDD Gate Compliance

- RED gate: `fc66b18` added failing CleaningService failure-flow tests.
- GREEN gate: `86fda99` implemented safe failure messaging and made targeted/full-suite tests pass.
- REFACTOR gate: Not needed; no behavior-preserving cleanup commit was required after GREEN.

## Verification

- `dotnet test AutoQACSharp.slnx --filter FullyQualifiedName~CleaningServiceTests` — RED expected failure after Task 1, then covered by targeted Phase 6 pass after Task 2.
- `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~XEditCommandBuilderTests|FullyQualifiedName~ProcessExecutionIntegrationTests|FullyQualifiedName~CleaningServiceTests"` — PASS (32 AutoQAC targeted tests; QueryPlugins project had no matching tests).
- `dotnet test AutoQACSharp.slnx` — PASS (59 QueryPlugins.Tests, 673 AutoQAC.Tests).

## Self-Check: PASSED

- Found modified files: `AutoQAC/Services/Cleaning/CleaningService.cs`, `AutoQAC.Tests/Services/CleaningServiceTests.cs`.
- Found summary file: `.planning/phases/06-command-launch-escaping/06-03-SUMMARY.md`.
- Found task commits: `fc66b18`, `86fda99`.

## Next Phase Readiness

Phase 6 is complete and ready for `/gsd-verify-work` or Phase 7 planning. The only documented residual product risk is the accepted Plan 06-01 assumption that real xEdit accepts split parsed `-autoload` argv tokens.

---
*Phase: 06-command-launch-escaping*
*Completed: 2026-04-29*

# Phase 06 Plan 04: Gap Closure Summary

**MO2 missing-path launches now fail before process start, and unexpected launch exceptions no longer disclose paths or command fragments to users.**

## Performance

- **Duration:** 16 min
- **Started:** 2026-04-29T04:59:00Z
- **Completed:** 2026-04-29T05:15:18Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments

- Added a regression theory proving MO2 mode returns `null` for null, empty, and whitespace `Mo2ExecutablePath` values.
- Changed `XEditCommandBuilder` so MO2 mode never silently falls through to direct xEdit when MO2 configuration is unusable.
- Replaced the unexpected cleaning exception disclosure test with a safe-failure regression that blocks xEdit/MO2 paths, `run`, `-a`, and raw exception text from `CleaningResult.Message`.
- Changed `CleaningService` to log unexpected exceptions while returning `Cleaning failed for {plugin.FileName}. See logs for technical details.`.

## Task Commits

Each task was committed atomically:

1. **Task 1 RED:** `fcfb0b4` test(06-04): add failing MO2 missing path regression
2. **Task 1 GREEN:** `fd0a13f` fix(06-04): fail MO2 launch without executable path
3. **Task 2 RED:** `982514d` test(06-04): add failing safe launch exception regression
4. **Task 2 GREEN:** `baf5e46` fix(06-04): hide unexpected cleaning exception details

**Plan metadata:** final `docs(06-04)` commit for this summary and state updates

## Files Created/Modified

- `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs` - Treats MO2 mode as exclusive and returns `null` when the MO2 executable path is null, empty, or whitespace.
- `AutoQAC.Tests/Services/XEditCommandBuilderTests.cs` - Adds missing-path MO2 regression coverage for null, empty, and whitespace path values.
- `AutoQAC/Services/Cleaning/CleaningService.cs` - Returns a concise generic failure message for unexpected exceptions while preserving technical details in logs.
- `AutoQAC.Tests/Services/CleaningServiceTests.cs` - Verifies unexpected launch exceptions do not expose configured paths, MO2 command fragments, or raw exception text.

## Decisions Made

- MO2 mode is now an exclusive branch: if enabled, a usable `Mo2ExecutablePath` is required and direct xEdit fallback is not allowed.
- Generic unexpected cleaning exceptions are intentionally less specific in the UI because launch-related exception messages can contain local paths or command fragments.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## Known Stubs

None - stub scan only found intentional null/empty test inputs and existing optional parameters, not UI-facing placeholder data.

## Verification

- `dotnet test AutoQACSharp.slnx --filter FullyQualifiedName~XEditCommandBuilderTests` — passed, 15 AutoQAC tests.
- `dotnet test AutoQACSharp.slnx --filter FullyQualifiedName~CleaningServiceTests` — passed, 13 AutoQAC tests.
- `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~XEditCommandBuilderTests|FullyQualifiedName~CleaningServiceTests"` — passed, 28 AutoQAC tests.
- `dotnet test AutoQACSharp.slnx` — passed, 59 QueryPlugins tests and 676 AutoQAC tests.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Phase 6 gaps from verification are closed and the full solution test suite passes.
- Phase 7 can proceed with backup restore and retention safety while preserving sequential xEdit cleaning.
- Broader diagnostics boundary hardening remains appropriately deferred to Phase 11.

## Self-Check: PASSED

- Verified modified code and test files exist.
- Verified summary file exists.
- Verified task commits exist: `fcfb0b4`, `fd0a13f`, `982514d`, `baf5e46`.

---
*Phase: 06-command-launch-escaping*
*Completed: 2026-04-29*
