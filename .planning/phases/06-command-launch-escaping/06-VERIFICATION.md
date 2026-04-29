---
phase: 06-command-launch-escaping
verified: 2026-04-29T05:06:49Z
status: gaps_found
score: 5/7 must-haves verified
overrides_applied: 0
gaps:
  - truth: "MO2 mode does not fall back to direct xEdit when MO2 executable configuration is missing."
    status: failed
    reason: "XEditCommandBuilder only enters the MO2 branch when Mo2ModeEnabled is true AND Mo2ExecutablePath is non-empty; otherwise it builds a direct xEdit ProcessStartInfo. This leaves the review CR-01 data-loss concern unresolved."
    artifacts:
      - path: "AutoQAC/Services/Cleaning/XEditCommandBuilder.cs"
        issue: "Lines 35-52 skip the MO2 branch when Mo2ExecutablePath is empty and then construct directStartInfo with FileName = xEditPath."
      - path: "AutoQAC.Tests/Services/XEditCommandBuilderTests.cs"
        issue: "No regression test asserts BuildCommand returns null when Mo2ModeEnabled is true and Mo2ExecutablePath is missing."
    missing:
      - "Return null from XEditCommandBuilder when Mo2ModeEnabled is true and Mo2ExecutablePath is null/empty/whitespace."
      - "Add a command-builder or cleaning-service test proving missing MO2 path in MO2 mode fails before process start instead of launching direct xEdit."
  - truth: "User-facing launch failure paths avoid configured executable paths and full command lines."
    status: partial
    reason: "Planned null-command and mocked launch-start paths are covered, but CleaningService's generic exception handler still returns ex.Message directly to the user; exceptions from launch-related code can disclose configured paths or command details."
    artifacts:
      - path: "AutoQAC/Services/Cleaning/CleaningService.cs"
        issue: "Lines 132-140 log the exception but return Message = ex.Message."
      - path: "AutoQAC.Tests/Services/CleaningServiceTests.cs"
        issue: "Lines 503-549 assert the unexpected exception message is exposed, so the current tests lock in the disclosure behavior."
    missing:
      - "Return a concise generic user-facing message for unexpected cleaning exceptions and keep detailed exception content in logs."
      - "Add a regression test where a launch-related exception message contains a configured path and assert the CleaningResult.Message omits it."
deferred:
  - truth: "Repository-wide user-facing diagnostic boundaries are hardened beyond Phase 6 launch-build and launch-start paths."
    addressed_in: "Phase 11"
    evidence: "Phase 11 goal: 'Users receive concise, actionable error messages while logs retain local troubleshooting value without unnecessary full path or command-line exposure.'"
---

# Phase 06: Command Launch Escaping Verification Report

**Phase Goal:** Users can launch direct xEdit and MO2-wrapped cleaning safely for plugin names and paths containing quotes, Unicode, spaces, and shell-sensitive characters.  
**Verified:** 2026-04-29T05:06:49Z  
**Status:** gaps_found  
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | User can clean via direct xEdit with quotes/parser cases, Unicode, spaces, shell-sensitive punctuation, and a combined worst-case plugin name preserved as argv data. | ✓ VERIFIED | `XEditCommandBuilder.cs:52-77` builds direct launches with `Arguments` empty and `ArgumentList` entries `-QAC`, `-autoexit`, `-autoload`, and exact `plugin.FileName`; tests at `XEditCommandBuilderTests.cs:54-118` cover worst-case, quote, Unicode, spaces, and shell-sensitive names. |
| 2 | User can run configured MO2 mode with `run`, xEdit path, `-a`, and one nested xEdit payload without target-plugin corruption. | ✓ VERIFIED | `XEditCommandBuilder.cs:35-49` builds `FileName = Mo2ExecutablePath`, `ArgumentList = ["run", xEditPath, "-a", BuildMo2NestedPayload(args)]`; `XEditCommandBuilderTests.cs:120-151` asserts exactly four MO2 argv entries, file-name-only `-autoload`, no `plugin.FullPath`, and parsed nested payload equal to expected tokens. |
| 3 | `ProcessExecutionService` preserves `ProcessStartInfo.ArgumentList` through cloning and real process start. | ✓ VERIFIED | `ProcessExecutionService.cs:158-193` clones every `startInfo.ArgumentList` entry and only falls back to `Arguments` when no `ArgumentList` entries exist; `ProcessExecutionIntegrationTests.cs:107-147` launches the helper with quotes/Unicode/shell punctuation/worst-case args and parses exact JSON argv. |
| 4 | Maintainer can verify direct xEdit and MO2 escaping across difficult path, plugin-name, and nested-argument cases. | ✓ VERIFIED | Targeted Phase 6 tests passed: `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~XEditCommandBuilderTests\|FullyQualifiedName~ProcessExecutionIntegrationTests\|FullyQualifiedName~CleaningServiceTests"` reported 32 passed tests. Tests include Unicode xEdit/MO2 paths, nested formatter parser checks, helper argv echo, and safe failure messages. |
| 5 | Command-build failures that return null fail before process start and use concise plugin/mode messages. | ✓ VERIFIED | `CleaningService.cs:54-70` returns failed `CleaningResult` with plugin, mode, `No process was started`, and log guidance when `BuildCommand` returns null; `CleaningServiceTests.cs:108-234` asserts direct and MO2 messages omit configured paths, `run`, `-a`, and that `ExecuteAsync` is not called. |
| 6 | MO2 mode does not fall back to direct xEdit when MO2 executable configuration is missing. | ✗ FAILED | `XEditCommandBuilder.cs:35` checks `config.Mo2ModeEnabled && !string.IsNullOrEmpty(config.Mo2ExecutablePath)`; when MO2 is enabled but path is empty, execution continues to `directStartInfo` at lines 52-77. This confirms code-review CR-01 remains present. |
| 7 | User-facing launch failure paths avoid configured executable paths and full command lines. | ✗ PARTIAL | Null-command and mocked launch-start paths are safe, but `CleaningService.cs:132-140` returns `ex.Message` directly. `CleaningServiceTests.cs:503-549` currently expects unexpected exception details to reach the user. Phase 11 covers broader diagnostics, but this is still a Phase 6 launch-failure leakage path if a launch exception includes paths. |

**Score:** 5/7 truths verified

### Deferred Items

Items not yet met but explicitly addressed in later milestone phases.

| # | Item | Addressed In | Evidence |
|---|------|-------------|----------|
| 1 | Repository-wide user-facing diagnostic boundaries beyond the explicit Phase 6 launch-build/start cases. | Phase 11 | Phase 11 goal covers concise errors and avoiding unnecessary path/command exposure. |

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs` | Direct and MO2 `ProcessStartInfo` construction using `ArgumentList`. | ⚠️ PARTIAL | Substantive and wired, but MO2-enabled + missing MO2 path falls through to direct xEdit (`lines 35-52`) instead of returning null. |
| `AutoQAC.Tests/Services/XEditCommandBuilderTests.cs` | Curated command escaping regression matrix. | ⚠️ PARTIAL | Covers direct/MO2 configured escaping, but lacks the CR-01 regression for missing MO2 executable in MO2 mode. |
| `AutoQAC/Services/Process/ProcessExecutionService.cs` | ArgumentList-preserving process start clone. | ✓ VERIFIED | `CloneStartInfoForLaunch` copies each argument-list entry and preserves legacy `Arguments` fallback. |
| `AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs` | Real helper-process argv preservation tests. | ✓ VERIFIED | Tests exact helper-received argv for quote, Unicode, shell-sensitive, worst-case, and legacy fallback cases. |
| `AutoQAC.Tests/TestProcessHelper/Program.cs` | `argv-echo` helper mode. | ✓ VERIFIED | Lines 25-28 serialize `args.Skip(1)` as UTF-8 JSON and return 0. |
| `AutoQAC/Services/Cleaning/CleaningService.cs` | Concise no-process-started command-build failure mapping. | ⚠️ PARTIAL | Null command path is safe; generic exception path still returns `ex.Message`. Also does not pass `plugin.FileName` to `ExecuteAsync`, preserving the review WR-01 warning. |
| `AutoQAC.Tests/Services/CleaningServiceTests.cs` | Failure-flow regression tests for direct and MO2 mode. | ⚠️ PARTIAL | Covers null command and mocked launch-start non-disclosure, but tests still expect unexpected exception message disclosure. |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `XEditCommandBuilder.cs` | `ProcessStartInfo.ArgumentList` | Direct mode adds parsed argv tokens; MO2 mode adds wrapper tokens and one nested payload. | ⚠️ PARTIAL | Correct when configured; missing MO2 executable skips MO2 branch and builds direct launch. |
| `XEditCommandBuilderTests.cs` | `XEditCommandBuilder.cs` | Tests call `BuildCommand` and assert `ArgumentList`, `FileName`, `WorkingDirectory`, `Arguments`. | ✓ VERIFIED | Tests assert direct and MO2 contracts; missing-path case absent. |
| `ProcessExecutionService.cs` | `ProcessStartInfo.ArgumentList` | `foreach (var argument in startInfo.ArgumentList) processStartInfo.ArgumentList.Add(argument)`. | ✓ VERIFIED | Clone preserves `ArgumentList` through process start and legacy `Arguments` only when list is empty. |
| `ProcessExecutionIntegrationTests.cs` | `AutoQAC.TestProcessHelper` | `argv-echo` writes JSON stdout parsed by tests. | ✓ VERIFIED | Helper process receives exact argv text under `UseShellExecute=false`. |
| `CleaningService.cs` | `IXEditCommandBuilder.BuildCommand` | Null command maps to failed cleaning result before `ExecuteAsync`. | ✓ VERIFIED | Null path does not start a process and returns concise message. |
| `CleaningServiceTests.cs` | `IProcessExecutionService.ExecuteAsync` | `DidNotReceive` proves no process on mocked command-build failure. | ✓ VERIFIED | Direct and MO2 null-command tests use 5-arg optional-parameter assertions. |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|--------------------|--------|
| `XEditCommandBuilder.cs` | `config.XEditExecutablePath`, `config.Mo2ExecutablePath`, `plugin.FileName` | `stateService.CurrentState` and `PluginInfo` | Yes, used directly as `FileName`/`ArgumentList` values without normalization. | ⚠️ PARTIAL — MO2 missing path falls back to direct xEdit. |
| `ProcessExecutionService.cs` | `startInfo.ArgumentList` | Caller-provided `ProcessStartInfo` | Yes, copied into launch clone and verified by helper process. | ✓ FLOWING |
| `CleaningService.cs` | command-build result and launch-mode message | `commandBuilder.BuildCommand`, `stateService.CurrentState` | Yes for null-command flow; exception flow can leak raw exception text. | ⚠️ PARTIAL |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Phase 6 targeted tests pass. | `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~XEditCommandBuilderTests\|FullyQualifiedName~ProcessExecutionIntegrationTests\|FullyQualifiedName~CleaningServiceTests"` | 32 AutoQAC.Tests passed; QueryPlugins had no matching tests. | ✓ PASS |
| Missing MO2 executable in MO2 mode must not produce direct xEdit launch. | Code inspection of `XEditCommandBuilder.cs:35-52`; no matching regression test found. | Builder falls through to direct launch when `Mo2ModeEnabled` is true and `Mo2ExecutablePath` is empty. | ✗ FAIL |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| SAF-03 | 06-01, 06-02, 06-03 | User can clean plugins whose paths or names contain quotes, Unicode, spaces, or shell-sensitive characters. | ✗ BLOCKED | Direct and configured MO2 argv handling is implemented and tested, but missing MO2 executable while MO2 mode is enabled can launch direct xEdit, violating safe MO2-wrapped cleaning. |
| TEST-02 | 06-01, 06-02, 06-03 | Maintainer can verify xEdit and MO2 command argument escaping across quotes, Unicode, shell-sensitive characters, and nested arguments. | ⚠️ PARTIAL | Targeted escaping tests pass, but tests do not cover the CR-01 missing-MO2 fallback and unexpected exception disclosure path. |

No additional Phase 6 requirement IDs were found in `.planning/REQUIREMENTS.md` beyond SAF-03 and TEST-02.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs` | 35-52 | Conditional MO2 branch falls through to direct launch when MO2 path is missing. | 🛑 Blocker | MO2 mode can launch direct xEdit instead of failing safely. |
| `AutoQAC/Services/Cleaning/CleaningService.cs` | 84 | `ExecuteAsync(command, timeout, ct, onProcessStarted)` omits `pluginName`. | ⚠️ Warning | Code-review WR-01 remains: PID tracking can record argument-count summary instead of plugin identity for ArgumentList launches. |
| `AutoQAC/Services/Cleaning/CleaningService.cs` | 132-140 | `Message = ex.Message`. | ⚠️ Warning | Unexpected launch-related exceptions can disclose configured paths/full command details to users. |
| `AutoQAC.Tests/Services/CleaningServiceTests.cs` | 503-549 | Test expects unexpected exception details in user-facing result. | ⚠️ Warning | Current tests lock in disclosure behavior instead of protecting the launch failure boundary. |

### Human Verification Required

Automated checks do not launch real xEdit or real MO2. After blockers are fixed, a human smoke test should verify that real xEdit accepts the split parsed `-autoload`, `Plugin.esp` argv contract and that real MO2 accepts the `run <xEdit> -a <nested payload>` contract for difficult plugin names. This is especially important because `06-RESEARCH.md` records both xEdit autoload shape and MO2 `run -a` parser behavior as assumptions.

### Gaps Summary

Phase 6 is substantially implemented for the happy path: direct `ArgumentList`, configured MO2 nested payloads, process-layer cloning, helper-process argv preservation, and targeted tests all exist and pass. However, the phase goal is not fully achieved because the critical MO2 failure path identified by code review is still present in production code. If MO2 mode is enabled but the MO2 executable path is missing, `XEditCommandBuilder` constructs a direct xEdit launch instead of failing safely before process start. That violates the MO2-wrapped safety goal and SAF-03. A secondary disclosure gap remains in the generic exception path, with broader diagnostics hardening explicitly scheduled for Phase 11.

---

_Verified: 2026-04-29T05:06:49Z_  
_Verifier: the agent (gsd-verifier)_
