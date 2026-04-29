# Phase 6: Command Launch Escaping - Context

**Gathered:** 2026-04-28
**Status:** Ready for planning

<domain>
## Phase Boundary

Phase 6 makes direct xEdit and MO2-wrapped cleaning launch the intended plugin safely when executable paths, plugin names, or nested xEdit arguments contain difficult characters such as spaces, quotes, Unicode, and shell-sensitive punctuation. It is limited to command construction, process-start argument preservation, and regression coverage for launch escaping. It must preserve sequential cleaning, Phase 5 stop/force-kill semantics, and existing cleaning session flow.

</domain>

<decisions>
## Implementation Decisions

### Coverage Matrix
- **D-01:** Quote cases should be covered with synthetic command/argv parser tests when Windows cannot create equivalent real file names; real file/path tests should be used for characters Windows actually permits.
- **D-02:** The difficult-character matrix must cover all launch-bound inputs: xEdit executable path, MO2 executable path, plugin file name, and nested xEdit arguments.
- **D-03:** Unicode coverage should be representative rather than exhaustive: include accented text, at least one non-Latin case such as CJK or Cyrillic, and one supplementary/emoji-style case if the test harness can represent it reliably.
- **D-04:** Shell-sensitive characters should be proven as literal argv text under `UseShellExecute=false`; include cases such as `&`, `|`, `;`, `(`, `)`, `^`, and spaces rather than treating them as shell syntax.

### MO2 Contract
- **D-05:** Preserve the current MO2 launch shape if possible: `ModOrganizer.exe run <xEdit> -a <xEdit args>`. The goal is to replace fragile escaping, not to change the MO2 integration contract.
- **D-06:** Keep one MO2 `-a` payload containing the xEdit flags as a single nested xEdit argument string unless research proves the current contract cannot be made safe.
- **D-07:** In MO2 mode, keep `-autoload` file-name-only. Do not switch it to a full host filesystem path because MO2 VFS and xEdit load-order resolution should own plugin lookup.
- **D-08:** MO2-specific verification should assert the final MO2 argv contract (`run`, xEdit path, `-a`, one intact xEdit argument string) without requiring real MO2 to be installed or launched in tests.

### Failure Behavior
- **D-09:** If AutoQAC cannot safely build a direct or MO2 launch command, fail before starting any process. Return a clear command-build failure for the plugin and log technical detail.
- **D-10:** Do not normalize, strip, sanitize, or rewrite configured paths or plugin names to make launching easier. Inputs are authoritative; pass them exactly as argv text or fail.
- **D-11:** A command-build or launch-start failure for one plugin should use the existing cleaning failure flow. Do not invent a new session policy for this phase.
- **D-12:** User-facing launch-escaping failure messages should be concise: name the plugin and launch mode, state that no process was started, and point to logs for technical details. Do not show a full command line in the user-facing message.

### Verification Depth
- **D-13:** Phase 6 must explicitly verify that `ProcessExecutionService` preserves `ProcessStartInfo.ArgumentList` through launch; a command-builder fix is incomplete if the process layer drops `ArgumentList` while cloning `ProcessStartInfo`.
- **D-14:** Extend the existing Phase 5 `AutoQAC.TestProcessHelper` with an argv-echo mode rather than adding a separate helper process or avoiding helper-based process tests.
- **D-15:** Use a curated regression matrix: cover each locked category plus one combined worst-case input. Do not expand this phase into broad fuzzing.
- **D-16:** Prefer parsed argv assertions over raw generated command-string assertions. Assert raw string shape only where MO2's single `-a` payload contract requires it.

### the agent's Discretion
- Exact helper method names, data structures for representing xEdit/MO2 argument payloads, and test case naming are left to downstream research and planning.
- The planner may choose the smallest internal API changes that satisfy the decisions above, as long as all launch-bound `ArgumentList` entries survive to the actual process start boundary.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Planning Scope
- `.planning/ROADMAP.md` - Phase 6 goal, dependencies, requirements, and success criteria.
- `.planning/REQUIREMENTS.md` - Requirements `SAF-03` and `TEST-02` mapped to Phase 6.
- `.planning/PROJECT.md` - Project constraints: Windows-only app, sequential xEdit cleaning, read-only `Mutagen/`, and cleanup milestone intent.
- `.planning/STATE.md` - Current milestone state and carried-forward process/cleaning constraints.

### Codebase Analysis
- `.planning/codebase/CONCERNS.md` - Identifies manual command-line construction in `XEditCommandBuilder` and missing command-line escaping tests as high-priority concerns.
- `.planning/codebase/ARCHITECTURE.md` - Documents the cleaning request path from `CleaningService` to `XEditCommandBuilder` to `ProcessExecutionService`.
- `.planning/codebase/TESTING.md` - Existing test organization, helper patterns, and process-test constraints.
- `.planning/phases/05-process-stop-pid-safety/05-CONTEXT.md` - Locks Phase 5 process semantics and the existing real child-process test helper pattern that Phase 6 should reuse.

### External API and Parser Semantics
- `https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.argumentlist` - `ArgumentList` escapes provided arguments and should be preferred over manual `Arguments` escaping.
- `https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.start` - Process start failure modes and launch API cautions.
- `https://learn.microsoft.com/windows/win32/api/shellapi/nf-shellapi-commandlinetoargvw#remarks` - Windows quote/backslash parsing behavior for command lines.
- `https://learn.microsoft.com/cpp/c-language/parsing-c-command-line-arguments?view=msvc-170` - Microsoft C runtime argv parsing rules for quotes, whitespace, and backslashes.

### MO2 and xEdit Launch Behavior
- `https://github-wiki-see.page/m/ModOrganizer2/modorganizer/wiki/Executables-window` - MO2 executable and argument-field behavior under its virtual filesystem.
- `https://wiki.step-project.com/Guide:XEdit` - xEdit launch argument guidance and MO2 usage context.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs` - Primary command-construction target; currently builds direct xEdit and MO2 commands through manually escaped strings.
- `AutoQAC/Services/Cleaning/CleaningService.cs` - Calls `IXEditCommandBuilder.BuildCommand` and passes the resulting `ProcessStartInfo` to process execution.
- `AutoQAC/Services/Process/ProcessExecutionService.cs` - Primary process-start boundary; currently clones `ProcessStartInfo` fields and must preserve `ArgumentList` when the builder starts using it.
- `AutoQAC.Tests/Services/XEditCommandBuilderTests.cs` - Existing command-builder unit tests to expand with direct and MO2 argument cases.
- `AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs` and `AutoQAC.Tests/TestProcessHelper/Program.cs` - Phase 5 helper-process pattern to extend with argv echo coverage.

### Established Patterns
- `ProcessExecutionService` enforces one process slot with `SemaphoreSlim(1, 1)`; Phase 6 must not parallelize launch testing or runtime cleaning.
- Process launches use `UseShellExecute=false`, so shell-sensitive characters should remain literal process arguments rather than shell syntax.
- Existing service tests use xUnit, FluentAssertions, NSubstitute, temp directories, `TaskCompletionSource`, and `try/finally` cleanup for helper processes.
- ViewModel/UI changes are not the center of this phase; user-facing failure text should flow through existing cleaning failure pathways.

### Integration Points
- `XEditCommandBuilder.BuildCommand` returns `ProcessStartInfo?`; any richer build failure information must still integrate with `CleaningService.CleanPluginAsync` and existing cleaning result handling.
- `CleaningService.CleanPluginAsync` currently maps `null` command builds to `CleaningStatus.Failed` with a generic message; this is the likely boundary for concise plugin/mode launch failure reporting.
- `ProcessExecutionService.ExecuteAsync` currently copies `FileName`, `Arguments`, `WorkingDirectory`, redirection, encoding, and window flags into a new `ProcessStartInfo`; this copy step is a required verification target for `ArgumentList` preservation.
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` should only need changes if the planner introduces a new escaping/helper service rather than keeping logic in `XEditCommandBuilder`.

</code_context>

<specifics>
## Specific Ideas

- Preserve behavior rather than broaden it: current MO2 mode should still launch through `ModOrganizer.exe run` with a single `-a` payload.
- The safest implementation should pass argv entries as data, not quote-decorated fragments. Tests should prove what the launched helper receives.
- Quote-related cases are still required as parser regression coverage even when the exact character cannot exist in a Windows file name.
- Full command lines should not be shown in user-facing launch failure messages; detailed command/argument diagnostics belong in logs.

</specifics>

<deferred>
## Deferred Ideas

None - discussion stayed within phase scope.

</deferred>

---

*Phase: 06-command-launch-escaping*
*Context gathered: 2026-04-28*
