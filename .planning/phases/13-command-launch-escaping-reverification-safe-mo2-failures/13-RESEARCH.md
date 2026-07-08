---
phase: 13-command-launch-escaping-reverification-safe-mo2-failures
researched: 2026-05-01
status: complete
domains: [command-launch, mo2, diagnostics, verification]
---

# Phase 13 Research — Command Launch Escaping Reverification & Safe MO2 Failures

## Research Question

What must be known to plan Phase 13 so current evidence can prove `SAF-03` and `TEST-02` without broad command-launch redesign?

## Existing Implementation Findings

### Command builder contract

- `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs` is the launch-contract source of truth.
- Direct mode builds `ProcessStartInfo` with `UseShellExecute = false`, empty `Arguments`, and parsed `ArgumentList` entries for `-QAC`, `-autoexit`, `-autoload`, and the exact plugin file name.
- Universal `xEdit.exe` launches prepend the game flag and partial forms appends `-iknowwhatimdoing` and `-allowmakepartial`.
- MO2 mode is now an exclusive branch: when `Mo2ModeEnabled` is true and `Mo2ExecutablePath` is null, empty, or whitespace, `BuildCommand` returns `null` instead of falling through to direct xEdit.
- Configured MO2 mode builds exactly four wrapper arguments: `run`, xEdit path, `-a`, and one nested payload formatted with Microsoft CRT quote/backslash rules.

### Process boundary contract

- `AutoQAC/Services/Process/ProcessExecutionService.cs` clones caller-provided `ArgumentList` entries into the real launch `ProcessStartInfo` when the list is non-empty.
- It falls back to `Arguments` only for legacy callers with no `ArgumentList` entries.
- Process logs use argument counts and operation/status fields instead of raw executable paths or raw argv payloads.
- `ProcessExecutionService` still enforces a single process slot; Phase 13 must not introduce new process-start seams or parallel cleaning behavior.

### Cleaning failure diagnostics contract

- `AutoQAC/Services/Cleaning/CleaningService.cs` maps null command construction to a failed result before calling `IProcessExecutionService.ExecuteAsync`.
- Missing-command user copy includes safe plugin name, launch mode, `No process was started`, and latest-log guidance.
- Unexpected exceptions are logged with technical details but return `DiagnosticTextFormatter.CleaningFailedForPlugin(plugin.FileName)` to keep paths, command fragments, nested MO2 payloads, and raw exception text out of user-facing results.

## Existing Test Evidence Targets

| Area | Test File | Evidence Value |
|------|-----------|----------------|
| Command construction | `AutoQAC.Tests/Services/XEditCommandBuilderTests.cs` | Direct difficult-character argv, configured MO2 four-argument wrapper, nested payload parsing, null/empty/whitespace MO2 path returning `null`. |
| Process boundary | `AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs` | Helper-process proof that `ArgumentList` values with quotes, Unicode, spaces, shell-sensitive characters, and combined worst-case inputs survive real process start. |
| Failure diagnostics | `AutoQAC.Tests/Services/CleaningServiceTests.cs` | No-process-start command-build failures, MO2 failed launch log boundaries, mocked start failure copy, unexpected exception non-disclosure. |

Recommended targeted commands:

```powershell
dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~XEditCommandBuilderTests"
dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~ProcessExecutionIntegrationTests"
dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~CleaningServiceTests"
dotnet test AutoQACSharp.slnx
```

## Historical Gap Context

- `.planning/v1.0-MILESTONE-AUDIT.md` still records `SAF-03` and `TEST-02` as unsatisfied because `06-VERIFICATION.md` found two Phase 6 gaps.
- `.planning/phases/06-command-launch-escaping/06-04-SUMMARY.md` reports those gaps were fixed by making missing MO2 configuration fail command construction and by replacing unexpected exception disclosure with safe user-facing copy.
- Phase 13 should create current evidence artifacts instead of editing Phase 6 history, milestone audit markers, `REQUIREMENTS.md`, or `ROADMAP.md` status markers per D-09 through D-12.

## Validation Architecture

Phase 13 should use the existing xUnit test infrastructure and capture both targeted evidence and full-suite evidence.

| Validation Dimension | Sampling Strategy | Required Evidence |
|----------------------|-------------------|-------------------|
| Missing MO2 no-launch | Run `XEditCommandBuilderTests` and `CleaningServiceTests`; inspect failure rows if red. | Null/empty/whitespace MO2 paths produce no command; cleaning maps null command to failed no-process-start result and `ExecuteAsync` is not called. |
| Direct escaping | Run `XEditCommandBuilderTests` and `ProcessExecutionIntegrationTests`. | Direct mode preserves quotes, Unicode, spaces, shell-sensitive punctuation, combined worst-case plugin names, and `UseShellExecute=false`. |
| MO2 escaping | Run `XEditCommandBuilderTests` and `CleaningServiceTests`. | MO2 wrapper has four arguments and nested payload parses back to file-name-only `-autoload` target without exposing nested payload in user copy. |
| Launch diagnostics | Run `CleaningServiceTests`. | Command-build, mocked start, and unexpected exception paths omit configured paths, raw command fragments, `run`, `-a`, nested payload, and raw exception text. |
| Whole-solution regression | Run `dotnet test AutoQACSharp.slnx`. | Full suite passes, or unrelated failures are documented with command output and rationale per D-07. |

## Planning Implications

- This phase can be evidence-first: execute targeted tests, update `13-VALIDATION.md`, then run the full suite and write `13-VERIFICATION.md`.
- If a targeted test fails, the executor must make the smallest fix in the affected production/test file and rerun the targeted and full-suite evidence before marking the validation row passing.
- If only the full suite fails while targeted evidence passes, the executor must classify whether the failure is unrelated before concluding Phase 13 satisfaction.
- Real xEdit/MO2 execution remains a visible non-blocking external integration risk; automated evidence verifies AutoQAC command construction and process seams only.

## Out of Scope for This Phase

- Rewriting historical Phase 6 artifacts.
- Editing milestone audit markers, `REQUIREMENTS.md`, or `ROADMAP.md` status markers.
- Adding real xEdit/MO2 smoke-test automation or a new harness.
- Changing xEdit/MO2 argv contracts unless targeted evidence reveals a current gap.
- Broad diagnostics hardening outside launch-failure paths.

## Research Complete

Phase 13 can be planned as two sequential evidence plans: targeted evidence plus validation matrix, then full-suite evidence plus final verification report.
