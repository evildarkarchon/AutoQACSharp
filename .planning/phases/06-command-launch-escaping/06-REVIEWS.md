---
phase: 06
reviewers: [gemini, claude, codex]
reviewed_at: 2026-04-28T21:12:40.5422285-07:00
plans_reviewed: [06-01-PLAN.md, 06-02-PLAN.md, 06-03-PLAN.md]
---

# Cross-AI Plan Review - Phase 06

## Gemini Review

# Phase 6: Command Launch Escaping - Plan Review

## 1. Summary
Phase 6 is a critical robustness update that migrates the application from fragile manual command-line string construction to the structured `ProcessStartInfo.ArgumentList` API. The plans are well-organized into a clear progression: establishing the contract at the builder level, ensuring preservation at the process-start boundary, and refining user-facing failure paths. By leveraging the existing real-process test helper for verification, the plans provide a high degree of confidence that the implementation will handle the complex escaping rules required by Windows and MO2.

## 2. Strengths
*   **API Modernization:** Shifting to `ArgumentList` is the correct technical choice for .NET 10, delegating the complexities of Windows command-line quoting to the framework.
*   **End-to-End Verification:** The use of an `argv-echo` mode in the `TestProcessHelper` ensures that the tests verify what the OS actually delivers to the child process, bridging the gap between unit tests and reality.
*   **Robust Character Coverage:** The curated matrix (quotes, Unicode, shell-sensitive, combined worst-case) specifically targets the safety bugs identified in `CONCERNS.md`.
*   **Security-First Failure Flow:** Plan 03 correctly prioritizes information disclosure prevention by ensuring failure messages do not leak executable paths or full command strings.
*   **Preservation of Existing MO2 Semantics:** The plans respect the complexity of the MO2 `-a` payload (a string-within-an-argument) while keeping the rest of the MO2 launch tokens safe.

## 3. Concerns
*   **xEdit `-autoload` Token Shape (HIGH):** Plan 01 moves from a single command-line token `-autoload "Plugin.esp"` to two separate argv entries: `-autoload` and `Plugin.esp`. While standard for many modern CLIs, it is unverified if xEdit accepts the split form. If xEdit requires the joined form (option and value in one token), all direct-mode cleaning will break.
*   **MO2 Nested Parser Complexity (MEDIUM):** The MO2 `-a` payload remains a nested string because it's a "string-within-an-argument" interpreted by MO2's own parser. While the plan mentions a "private Windows-style quoting helper," this logic is prone to subtle bugs (e.g., trailing backslashes before quotes) if not perfectly aligned with Microsoft C-Runtime (CRT) parsing rules.
*   **Incomplete Launch Failure Hardening (MEDIUM):** Plan 03 focuses heavily on command-build failures (`BuildCommand == null`) but does not explicitly verify the "concise/safe" requirements for cases where `Process.Start` itself fails (e.g., `Win32Exception` for File Not Found or Access Denied).
*   **Test Assertion Fragility (LOW):** Some planned assertions in Plan 03 use generic substrings (e.g., "not containing `-a`"). These could yield false positives if the plugin name itself happens to contain those characters.

## 4. Suggestions
*   **Explicitly Validate `-autoload` Shape:** Before finalizing Plan 01, verify if xEdit accepts `-autoload` and the plugin name as separate argv entries. If xEdit requires a single token, the builder should emit them as a single entry to `ArgumentList` (e.g., `"-autoload Plugin.esp"`) using the same quoting helper designed for MO2.
*   **Strengthen MO2 Formatter Tests:** Add exhaustive unit tests for the private MO2 nested-argument formatter, specifically covering cases where the plugin name or xEdit path ends with a backslash (e.g., `C:\Path\`).
*   **Use Concrete Path Values for Negative Assertions:** In Plan 03, configure the test state with unique, recognizable paths (e.g., `C:\FAILURE_TEST\xEdit.exe`) and assert that the result message does not contain that specific path value.
*   **Add a "Combined Worst Case" Constant:** Define the `WorstCasePluginName` as a shared constant across the test projects to ensure the unit and integration tests are exercising the exact same edge-case interactions.
*   **Add Legacy Fallback Test:** In Plan 02, include a test case that uses the legacy `Arguments` property to ensure the fallback logic in `ProcessExecutionService` remains functional for any non-migrated callers.

## 5. Risk Assessment
**Overall Risk: LOW to MEDIUM**

The architectural approach is sound and leverages built-in .NET safety features. The primary remaining risk is the "functional assumption" about xEdit and MO2's internal argument parsers. However, the inclusion of the real-process helper integration tests for `ArgumentList` preservation mitigates the vast majority of execution risk, as it proves AutoQAC is no longer the source of the corruption.

---

## the agent Review

# Phase 6 Plan Review: Command Launch Escaping

## Overall Summary

This is a tightly-scoped, well-researched three-plan phase that correctly identifies and addresses the two-boundary problem in command construction: `XEditCommandBuilder` (where argv contracts are defined) and `ProcessExecutionService` (where they're preserved through `Process.Start`). The plans incorporate prior cross-AI review feedback substantively - locking the `-autoload` argv shape, requiring concrete-value negative assertions for failure messages, demanding both exit code AND parsed JSON in red tests, and adding a legacy `Arguments` fallback regression. Wave structure is sound (06-01/06-02 touch independent files in parallel; 06-03 integrates after both land). The plans achieve SAF-03/TEST-02 with low-to-medium execution risk; the only meaningful residual risk is the unverified xEdit `-autoload` argv-shape assumption (A2 from research).

---

## Plan 06-01: Builder ArgumentList Contracts

### Strengths
- **Resolves Open Question A2 explicitly** by locking direct mode to two argv tokens (`-autoload`, `plugin.FileName`) with a code comment explaining the parsed-argv intent. Previous reviewers flagged this as ambiguous; the plan now picks a side and documents why.
- **Concrete worst-case constant** (`WorstCasePluginName`) is locked in this plan and referenced by 06-02, addressing the prior "two plans could pick different worst cases" concern.
- **Exact argv ordering assertion** (`.Should().Equal("-FO4", "-QAC", "-autoexit", "-autoload", "Plugin.esp", "-iknowwhatimdoing", "-allowmakepartial")`) prevents both ordering regressions and accidental flag additions - a direct response to prior review.
- **MO2 nested formatter has direct unit tests** for embedded quotes and trailing backslashes, addressing the HIGH concern that MO2 `-a` was the remaining manual-escape surface.
- **D-02 coverage is now explicit**: tests vary xEdit path, MO2 path, plugin filename, and nested arguments rather than only varying plugin filename.

### Concerns

**MEDIUM** - The plan still does not empirically verify that real xEdit accepts `-autoload` as two argv tokens. The code comment captures intent, but if xEdit treats `-autoload` and the plugin name as a single space-delimited token (as the old code emitted), every direct-mode launch breaks at runtime. The unit tests prove the contract AutoQAC produces, not the contract xEdit consumes. Phase 5's helper-process tests can't catch this either - they test argv preservation, not xEdit-specific parsing.

**LOW** - The MO2 nested formatter is described as "Windows CRT quote/backslash escaping" but the plan does not cite the exact algorithm (e.g., the C runtime parsing rules from `parsing-c-command-line-arguments`). A future maintainer "simplifying" this helper without that reference is a real risk. The acceptance criterion checks for a comment string but not for algorithmic correctness against the parser.

**LOW** - Acceptance criterion for `Arguments` empty in direct mode is implicit (test name `result.Arguments.Should().BeEmpty()` is in `<behavior>` but not in `<acceptance_criteria>`). A grep-able criterion would strengthen the gate.

### Suggestions
1. **Add a Phase 6 closing manual smoke check**: a one-line note in `06-03-SUMMARY.md` confirming a real xEdit launch on a test plugin succeeded, or explicit acceptance of the A2 risk. This is cheap insurance against the only HIGH-impact unverified assumption.
2. **Cite the CRT parser algorithm** in the MO2 nested formatter's XML doc comment (link to `https://learn.microsoft.com/cpp/c-language/parsing-c-command-line-arguments`) so future maintainers can validate any change.
3. **Add a parser round-trip test for the MO2 nested formatter**: format a string, then run it through `CommandLineToArgvW` (or a port of the CRT algorithm) and assert the original tokens come back. This proves the formatter works for MO2's parser without requiring real MO2.

### Risk Assessment: **MEDIUM**
The argv-shape change for `-autoload` is intentional and well-tested at AutoQAC's boundary, but unverified at xEdit's. If wrong, this is detectable on first real run and fully reversible.

---

## Plan 06-02: Process Boundary Preservation

### Strengths
- **Tightened red assertions** require both `ExitCode.Should().Be(0)` AND `JsonSerializer.Deserialize<string[]>(stdout).Should().Equal(expectedArgs)`, addressing the prior concern that a clone dropping `ArgumentList` could silently produce a "no args echoed" zero-exit pass.
- **Legacy `Arguments` fallback regression** explicitly added (`legacy-token` test), closing the gap where the `else` branch of the new clone logic had no test coverage.
- **Single stdout reader** via `ReadHelperJsonAsync` in `onProcessStarted` avoids the stream-read race Codex flagged where two consumers could read redirected stdout.
- **Helper extension only**, not a new helper executable, honoring D-14 and avoiding copy/build-surface growth.
- **All Phase 5-copied `ProcessStartInfo` settings preserved** (FileName, WorkingDirectory, redirection, encodings, CreateNoWindow), with explicit reminder in the `<action>` block.

### Concerns

**LOW** - The plan documents that `ArgumentList` "takes precedence" in the clone when both are populated. .NET docs say these are mutually exclusive and behavior is undefined when both are set. The acceptance criterion checks for the `else` branch but not for an explicit test where both `Arguments` and `ArgumentList` are non-empty. A future contributor could set both and rely on whichever wins. Worth either (a) failing fast when both are populated, or (b) adding a test asserting the documented precedence.

**LOW** - Debug log change is mentioned (`do not rely on startInfo.Arguments as the only argument source`) but not specified concretely. If the new log emits joined `ArgumentList` tokens, that's a quasi command-line in logs - fine for local diagnostics but a future SEC-02 boundary. Worth a one-line comment in code clarifying intent.

**LOW** - The integration test's combined worst case (`Quote" Résumé 測試 🚀 & | ; ( ) ^ Spaces.esp`) differs slightly from 06-01's `WorstCasePluginName` (`Quote\" Résumé 測試 🚀 & | ; ( ) ^ Spaces.esp`). The escape difference (`Quote"` vs `Quote\"`) is C# source escaping vs raw value - they should resolve to the same runtime string, but the plan is inconsistent in how it shows them. Worth a single shared `const` referenced from both files.

### Suggestions
1. **Add a "both APIs populated" test** that either asserts `ArgumentList` precedence or asserts the service throws / fails fast. The current plan documents intent but doesn't lock it.
2. **Reference 06-01's `WorstCasePluginName` constant** from the integration test (via `internal const` or test data sharing) rather than restating the literal. Prevents drift.
3. **Confirm debug log content**: add an explicit acceptance criterion that the debug log does not include configured xEdit/MO2 paths verbatim if the plan intends to keep paths out of logs (this is a Phase 11 boundary, but worth not regressing now).

### Risk Assessment: **LOW**
The change is narrow, highly testable, and the `else` fallback preserves all non-Phase-6 callers. Risk comes from accidentally altering existing process semantics, but acceptance criteria explicitly preserve every prior copied field.

---

## Plan 06-03: Failure Flow Integration

### Strengths
- **Concrete-value negative assertions** (`C:\Tools With Spaces\SSEEdit.exe`, `C:\MO2 With Spaces\ModOrganizer.exe`) replace the prior brittle generic substring check (`xEdit.exe`, `-a`). This addresses the MEDIUM concern that a plugin name containing those substrings could falsely satisfy negative assertions.
- **Mocked launch-start failure test added** (`ProcessResult { ExitCode = -1 }`), addressing the HIGH concern that the plan previously claimed launch-start coverage while only testing the build-null branch. The plan now explicitly limits the new coverage to mocked launch-start without expanding to real `Process.Start` failures (which is appropriate scoping).
- **Single state snapshot** (`var state = stateService.CurrentState`) captured before mode determination, addressing the race where state changes between build-time and message-time.
- **Structured warning log** with plugin/launch mode placeholders preserves diagnostic value without leaking configured paths to user-facing text.
- **Explicit launch-mode branching** (`Mo2ModeEnabled ? "MO2" : "direct xEdit"`) is grep-able and asserts user-facing wording at the cleaning-service boundary, not the UI.

### Concerns

**MEDIUM** - The mocked launch-start test asserts the existing `xEdit exited with code -1` message remains concise, but the assertion that paths aren't disclosed is implicit. If `CleaningService` adds path detail to that branch later (e.g., for SEC-02), the test won't catch the regression because the criterion only requires the message contain `xEdit exited with code -1` - not that it omits path values. Add a `.Should().NotContain(xEditPath)` and `.Should().NotContain(mo2Path)` assertion explicitly to the launch-start failure test.

**LOW** - The plan logs structured warnings on build failure but does not specify what to log on launch-start failure. The existing code path emits `Message = "xEdit exited with code -1"` without a structured warning. If "technical detail in logs" is the user-facing promise, the launch-start branch should also log structured detail. Worth either documenting that existing process-layer logging covers this, or adding a thin warning at the cleaning service boundary.

**LOW** - `Message` references `plugin.FileName`. If the plugin filename itself contains the literal text `direct xEdit` or `MO2` (extremely unlikely but possible per D-10's "inputs are authoritative"), tests asserting `Message.Contains("direct xEdit")` could pass for the wrong reason. Defensive but probably over-engineered for this phase.

### Suggestions
1. **Tighten the launch-start failure test** with explicit `Message.Should().NotContain(xEditPath)` and `Message.Should().NotContain(mo2Path)` assertions, mirroring the build-failure test's negative-assertion style.
2. **Document log behavior for launch-start failures**: add a one-line note that `ProcessExecutionService` already logs detail (it does - see `logger.Error(ex, "Failed to start process: {FileName}", ...)`) so `CleaningService` doesn't need to duplicate. This closes Codex's prior concern that "technical detail" might be empty.
3. **Consider asserting `Message.Length`** is below some bound (e.g., `< 200`) as a guard against accidental command-line embedding. Concise messages are a SEC requirement; a length cap is a cheap regression gate.

### Risk Assessment: **LOW**
Implementation is simple, well-tested, and bounded to the existing `CleaningResult` flow. The plan correctly avoids expanding into real launch-start hardening (a future SEC-01/SEC-02 boundary) while still adding meaningful mocked coverage.

---

## Phase-Level Assessment

### Strengths

- **Prior review feedback substantively incorporated.** Almost every MEDIUM concern from 06-REVIEWS.md (argv shape, worst-case consistency, exit-code-AND-JSON assertions, concrete negative assertions, launch-start coverage) has corresponding plan changes with grep-able acceptance criteria.
- **Wave structure is correct.** 06-01 and 06-02 touch disjoint production files; 06-03 depends on both because the failure-mode branch keys off `Mo2ModeEnabled` after both code paths land.
- **TDD discipline is consistent.** Every task is `tdd="true"` with `<read_first>` gates and acceptance criteria the executor can verify with grep - no hand-wavy "tests should pass."
- **Locked-decision traceability** via D-NN identifiers in `must_haves.truths` makes audit cheap.
- **No new dependencies, no DI changes, no UI changes.** Phase 5 process semantics preserved by construction.

### Concerns

- **MEDIUM** - Real xEdit `-autoload` argv-shape assumption (A2) remains unverified. Reversible, but is the only HIGH-impact residual risk in the phase.
- **LOW** - MO2 nested formatter algorithmic correctness depends on a documented CRT parser; without an algorithm citation in the helper's doc comment, a "simplification" PR could silently break MO2 mode.
- **LOW** - `WorstCasePluginName` should be a single shared constant referenced from both 06-01 and 06-02 tests rather than restated literally.

### Final Risk Assessment

**Overall risk: LOW-to-MEDIUM**

Justification:
- Scope is tight: three plans, six tasks, all in files Phase 5 already validated.
- Strong test-first design with both unit-level argv contracts and integration-level OS-boundary verification.
- The `ProcessExecutionService` clone fix is the highest-leverage change in the phase and has the best test coverage (red-green TDD with parsed JSON assertions plus legacy fallback).
- Single residual technical risk (xEdit `-autoload` shape) is detectable on first real run, reversible (revert to single-token form), and could be cheaply mitigated by a manual smoke check in 06-03's summary.

**Recommended before execution**: address Suggestion 1 from 06-01 (manual xEdit smoke check), Suggestion 1 from 06-03 (explicit path-disclosure negative assertions on launch-start failure test), and consider Suggestion 2 from 06-02 (shared worst-case constant). All three are minor edits; none block execution.

---

## Codex Review

**Overall Summary**

The three plans are generally strong and aligned with Phase 6: they target the right boundaries, split builder/process/failure-flow work cleanly, and emphasize parsed argv verification over brittle command-string snapshots. The main risks are around locking behavior that is still partly assumed: direct xEdit `-autoload` token shape and MO2 `-a` nested parsing. Those need either stronger evidence, explicit accepted-risk notes, or extra targeted tests so Phase 6 does not replace one escaping bug with a semantic launch regression.

## 06-01-PLAN.md

**Summary**

Strong builder-focused plan. It correctly moves command construction toward `ProcessStartInfo.ArgumentList`, preserves the MO2 wrapper contract, and requires curated difficult-character coverage. The biggest concern is that it explicitly locks direct `-autoload` as two parsed argv tokens despite research noting uncertainty about xEdit’s exact expected shape.

**Strengths**

- Uses `ArgumentList` for AutoQAC-controlled argv instead of manual quoting.
- Keeps direct xEdit and MO2 behavior separated, which matches the real trust boundaries.
- Preserves MO2 shape: `run`, xEdit path, `-a`, one nested payload.
- Includes direct coverage for Unicode, shell-sensitive characters, quote/parser cases, and a combined worst-case input.
- Requires tests before implementation and updates existing builder tests instead of adding disconnected coverage.

**Concerns**

- **HIGH:** Direct `-autoload` shape is being locked as `"-autoload", plugin.FileName` even though research flags this as an open xEdit compatibility question. If xEdit expects the old effective command-line shape, this could break actual cleaning.
- **MEDIUM:** MO2 nested formatter tests may accidentally test the formatter against its own parser assumptions. The plan should avoid “round-trip through the same helper” as the only proof.
- **MEDIUM:** “Plugin path” coverage is ambiguous. The launch contract appears to use `plugin.FileName`, not `plugin.FullPath`, especially in MO2 mode. The plan should be precise about which plugin fields are launch-bound.
- **LOW:** Acceptance criteria like literal `ArgumentList.Add(plugin.FileName)` are somewhat implementation-shape sensitive; a helper method could satisfy the behavior without matching the string.
- **LOW:** Quote characters cannot exist in Windows file names. The plan acknowledges synthetic parser cases, but the tests should clearly distinguish impossible filesystem names from argv/parser regression inputs.

**Suggestions**

- Add an explicit decision note: either “we accept the risk that xEdit accepts split `-autoload` argv” or “we validated this against xEdit/manual parser behavior.”
- Make MO2 formatter tests table-driven with expected quoted strings for hard cases such as embedded quote, trailing backslash, whitespace, and quote plus trailing backslash.
- Add negative assertions that MO2 `-autoload` does not use `plugin.FullPath`.
- Consider exposing a small internal helper for nested MO2 payload construction if tests need to target it directly.
- If adding nontrivial private formatter methods, use XML doc comments or a tight explanatory comment per repo instructions.

**Risk Assessment**

**MEDIUM.** The plan is structurally good, but the direct xEdit `-autoload` argv contract and MO2 nested parser assumptions are meaningful compatibility risks.

## 06-02-PLAN.md

**Summary**

This is the strongest of the three plans. It targets the exact process-boundary risk: `XEditCommandBuilder` can be correct while `ProcessExecutionService` still drops `ArgumentList`. The helper-process JSON echo approach is practical and gives high-confidence verification.

**Strengths**

- Correctly verifies the real process launch boundary, not just builder state.
- Reuses `AutoQAC.TestProcessHelper` instead of adding another helper project.
- Uses UTF-8 JSON for robust Unicode and delimiter-safe argv assertions.
- Preserves legacy `Arguments` fallback when `ArgumentList` is empty.
- Keeps Phase 5 stop/PID/timeout behavior explicitly out of scope for change.

**Concerns**

- **MEDIUM:** Tests that populate both `Arguments` and `ArgumentList` should be careful. The clone may choose `ArgumentList`, but production callers should not treat mixed input as a supported public contract.
- **MEDIUM:** Capturing stdout via `onProcessStarted` needs careful task lifetime handling so the test cannot pass before the stdout read completes or hang if process startup fails.
- **LOW:** Updating debug logging to handle `ArgumentList` could accidentally disclose more command detail than before. Keep any expanded command detail at debug level and avoid user-facing propagation.
- **LOW:** The helper’s `Console.OutputEncoding = UTF8` should be scoped to `argv-echo` mode so it does not alter behavior of other helper modes unexpectedly.

**Suggestions**

- Add a helper in tests that awaits both process completion and stdout read with a bounded timeout.
- Name the legacy fallback test clearly as “when ArgumentList is empty” rather than implying mixed APIs are generally supported.
- Add one assertion that `Arguments` is not copied when `ArgumentList.Count > 0`, if the code exposes enough behavior to verify this safely.
- Keep the process clone logic explicit rather than clever; preserving all existing copied fields matters more than abstraction here.

**Risk Assessment**

**LOW.** The plan is well-scoped, testable, and directly addresses the most important process-layer regression risk.

## 06-03-PLAN.md

**Summary**

Good final integration plan. It verifies that command-build failures stop before process launch and that user-facing messages avoid full command/path disclosure. The main gap is that `BuildCommand` still returns only `null`, so “technical detail in logs” may remain weak unless the builder or service can distinguish why command construction failed.

**Strengths**

- Correctly depends on 06-01 and 06-02.
- Tests both direct and MO2 command-build failure messages.
- Requires `DidNotReceive().ExecuteAsync`, which proves no process start on build failure.
- Keeps failure handling inside the existing `CleaningResult` flow.
- Adds final targeted and full-suite verification.

**Concerns**

- **MEDIUM:** `BuildCommand` returning `null` provides no structured failure reason. The user-facing message is improved, but logs may still lack the technical detail promised by D-09/D-12.
- **MEDIUM:** The mocked “launch-start failure” as `ProcessResult { ExitCode = -1 }` may not exercise the actual `Process.Start` exception path if `ProcessExecutionService` records errors differently.
- **MEDIUM:** `autonomous: true` plus dependency on generated summaries from 06-01/06-02 is fine only if execution tooling enforces wave order. Otherwise this plan can start with missing context files.
- **LOW:** Capturing `stateService.CurrentState` once is a good consistency improvement, but it should be checked against any existing behavior that intentionally re-reads state after config flushing.
- **LOW:** Negative message assertions should check exact configured paths and nested payload values, not just generic fragments like `-a`.

**Suggestions**

- If feasible, introduce a small command-build failure result later, such as reason/mode/plugin, instead of only `ProcessStartInfo?`. If not, explicitly document that detailed failure cause is not available in Phase 6.
- Add or adjust a test to match the real process-start failure shape returned by `ProcessExecutionService`, including any error/message field if present.
- Make the dependency gate explicit: 06-03 should not run until both summaries and targeted tests from 06-01/06-02 exist.
- Log structured technical context sufficient for diagnostics, while keeping full command lines out of `CleaningResult.Message`.

**Risk Assessment**

**MEDIUM.** The user-facing behavior is well covered, but the logging/detail story and mocked launch-start failure may under-test the real failure path.

## Cross-Plan Assessment

**Strengths**

- Clean division of responsibility: builder, process boundary, service failure flow.
- The phase avoids parallelizing cleaning and does not touch Mutagen.
- Testing strategy is appropriately layered: builder unit tests plus real helper-process integration tests.
- Security posture is directionally correct: `UseShellExecute=false`, no shell escaping, no full command line in user-facing messages.

**Main Risks**

- **HIGH:** xEdit `-autoload` argv shape compatibility remains the largest product risk.
- **MEDIUM:** MO2 `-a` nested parsing is still partly assumption-based.
- **MEDIUM:** Failure diagnostics may be less useful than promised unless `null` command builds carry more reason detail.
- **LOW:** Planning context has stack inconsistencies elsewhere, such as Avalonia/ReactiveUI details, but Phase 6 does not materially depend on them.

**Overall Risk Assessment**

**MEDIUM.** The plans are high quality and likely achieve the Phase 6 goals, but two launch semantics are still assumption-heavy. Resolve or explicitly accept the `-autoload` and MO2 `-a` risks before implementation, and the phase risk drops close to low.

---

## Consensus Summary

The reviewers agree that the plans are structurally strong and address the right Phase 6 failure surfaces: command construction in `XEditCommandBuilder`, argument preservation in `ProcessExecutionService`, and safe user-facing failure behavior in `CleaningService`. The dominant unresolved risk is not the choice of `ArgumentList`; all reviewers agree that is the right primary mitigation. The dominant risk is semantic compatibility with the downstream parsers that still interpret the resulting arguments, especially xEdit's `-autoload` shape and MO2's nested `-a` payload.

### Agreed Strengths

- `ProcessStartInfo.ArgumentList` is the correct API for AutoQAC-controlled argv boundaries.
- The three plans have a clean builder/process/failure-flow split and an appropriate wave dependency structure.
- Real helper-process `argv-echo` verification is a strong way to prove arguments survive the process-start boundary.
- The plans preserve sequential cleaning, avoid Mutagen changes, and keep MO2 wrapping scoped to the existing `run <xEdit> -a <payload>` contract.
- The failure-flow plan correctly avoids exposing full executable paths or command lines in `CleaningResult.Message`.

### Agreed Concerns

- Direct xEdit `-autoload` shape remains the most important compatibility assumption. All three reviewers flagged that split argv entries (`-autoload`, `plugin.FileName`) should be validated, explicitly accepted as a risk, or adjusted if xEdit requires the old effective shape.
- MO2 `-a` remains the one manual nested parsing surface. Reviewers want stricter table-driven formatter tests, quote/backslash/trailing-backslash coverage, and a comment or XML doc citing the exact CRT parsing rules being modeled.
- Plan 03 should be stricter about launch-start failure disclosure. Reviewers recommend explicit negative assertions against configured path values for the mocked launch-start failure branch, not just build-failure branches.
- Failure diagnostics may be weak while `BuildCommand` returns only `null`; if Phase 6 does not add a richer build result, the plans should explicitly document that detailed cause reporting remains limited.

### Divergent Views

- Gemini and the agent rate the overall risk as low-to-medium, while Codex rates it medium because xEdit and MO2 parser assumptions remain product-level risks.
- The agent views the mocked launch-start failure coverage as an appropriate Phase 6 boundary with minor assertion tightening; Gemini and Codex are more concerned that real `Process.Start` exception behavior remains under-verified.
- Codex is more skeptical of parser round-trip tests that use the same formatter/parser assumptions; the agent recommends a parser round-trip test using `CommandLineToArgvW` or a CRT parser port.

### Highest-Priority Feedback For Planning

1. Add a manual or documented validation gate for real xEdit `-autoload` split-argv behavior, or explicitly record accepted risk in `06-03-SUMMARY.md`.
2. Tighten MO2 nested formatter requirements with table-driven expected-output tests for embedded quotes, whitespace, trailing backslashes, and quote-plus-trailing-backslash cases.
3. Add explicit launch-start failure negative assertions that `CleaningResult.Message` does not contain configured xEdit/MO2 path values or nested payload text.
4. Document the CRT parsing rule source beside the MO2 nested formatter and ensure any nontrivial helper gets an XML doc comment or concise explanatory comment.
5. Clarify that `plugin.FileName`, not `plugin.FullPath`, is the launch-bound plugin value for MO2, and add a negative assertion that MO2 `-autoload` does not use `plugin.FullPath`.
