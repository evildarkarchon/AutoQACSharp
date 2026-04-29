---
phase: 06
reviewers: [gemini, claude, codex]
reviewed_at: 2026-04-28T20:50:57.7693461-07:00
plans_reviewed: [06-01-PLAN.md, 06-02-PLAN.md, 06-03-PLAN.md]
---

# Cross-AI Plan Review - Phase 06

## Gemini Review

# Phase 6: Command Launch Escaping - Plan Review

## 1. Summary
Phase 6 is a well-structured transition from fragile manual string-based command construction to the data-first `ProcessStartInfo.ArgumentList` API. The plans correctly identify the two critical boundaries: the `XEditCommandBuilder` (where the contract is defined) and the `ProcessExecutionService` (where the contract must be preserved through a cloning step). By extending the existing real-process test helper with an `argv-echo` mode, the plans ensure that verification is grounded in what the OS actually delivers to the child process, rather than just what .NET generates internally.

## 2. Strengths
*   **Correct API Selection:** Using `ArgumentList` is the industry-standard way in .NET to delegate OS-level escaping to the framework, eliminating the "guesswork" of manual quoting.
*   **Boundary Verification:** Plan 02 specifically addresses a known pitfall (cloning `ProcessStartInfo`) that would otherwise silently break the implementation even if unit tests passed.
*   **Robust Test Matrix:** The inclusion of synthetic parser cases (D-01) alongside real-world Unicode and shell-sensitive characters (D-03/D-04) provides high confidence in regression coverage.
*   **Secure Failure Handling:** Plan 03 follows the "SEC-01/SEC-02" requirements by ensuring build failures are concise and do not leak full command lines or executable paths to the user-facing UI.
*   **MO2 Contract Preservation:** The plans respect the complexity of the MO2 `-a` payload (a string-within-an-argument) while keeping the rest of the MO2 launch tokens safe.

## 3. Concerns
*   **MO2 Nested Escaping (MEDIUM):** In Plan 01, the MO2 `-a` payload remains a single string because MO2 owns a second parser boundary. While the plan mentions a "private Windows-style quoting helper," if this helper doesn't perfectly match how MO2's `run` command parses that payload, MO2 might still fail. However, the plan mitigates this by asserting the contract rather than the raw string where possible.
*   **Mutually Exclusive Arguments (LOW):** .NET throws an exception if both `Arguments` and `ArgumentList` are populated. Plan 02 correctly uses an `if/else` block in the clone logic to avoid this, but developers must be careful not to accidentally set `Arguments` in other parts of the builder.
*   **Console Encoding in Helper (LOW):** Plan 02 correctly specifies `Encoding.UTF8` for the helper's `argv-echo` output. Ensure that the `ProcessExecutionService` actually respects the `StandardOutputEncoding` property from the `startInfo` during its clone (which was a bug fixed in Phase 5).

## 4. Suggestions
*   **Document the MO2 escaping logic:** In Task 2 of Plan 01, explicitly document in a comment *why* the MO2 `-a` payload still needs manual quoting (nested parser boundary) while the direct mode does not.
*   **Verify `ArgumentList` length limits:** While unlikely for xEdit/MO2, Windows has a ~32k character limit for the full command line. Consider a "Safety" check in the builder that logs a warning if the projected command length is approaching this limit, though it's likely out of scope for this specific phase.
*   **Add a "Combined Worst Case" test:** Ensure the integration test in Plan 02 includes a single argument that contains a space, a quote, and Unicode simultaneously to prove they don't interfere with each other.

## 5. Risk Assessment
**Overall Risk: LOW**

The architectural approach is sound and leverages built-in .NET safety features. The dependency on a real-process helper for verification significantly reduces the risk of "it works in my unit test" failures. The primary remaining risk is the exact syntax expected by MO2's `-a` parser, but since the plan preserves the existing semantic contract, the risk of regression is minimized.

---
*Review completed: 2026-04-28*
*Phase: 06-command-launch-escaping*

---

## the agent Review

# Phase 6 Plan Review: Command Launch Escaping

## Summary

This is a tightly scoped, well-researched, three-plan phase that addresses a real bug class (manual `Arguments` string escaping in `XEditCommandBuilder`) by migrating to `ProcessStartInfo.ArgumentList` and verifying preservation through the actual process-start boundary. The plans show strong adherence to the locked decisions (D-01 through D-16), reuse existing test infrastructure (Phase 5 helper process), and respect phase boundaries by deferring UI/security polish to later phases. The wave structure is sound: Wave 1 fixes builder + process layer in parallel, Wave 2 integrates failure messaging after the foundations land. Overall, the plans achieve the SAF-03 and TEST-02 phase goals with low execution risk.

## Strengths

- **Strong TDD discipline.** Every task is `tdd="true"` with read_first gates, behavior specifications, and failing-test-first ordering. Acceptance criteria use grep-able literal strings that the executor can verify cheaply.
- **Locked decision traceability.** Each plan's `must_haves.truths` cites specific D-NN identifiers, making it trivial to audit whether implementation honors discussion outcomes.
- **Correct identification of the two-boundary problem.** Plan 02 explicitly addresses the `ProcessExecutionService` clone bug (Pitfall 1 from research) - without it, Plan 01 alone would silently fail in production.
- **MO2 contract is preserved exactly.** D-05/D-06/D-07 are honored: one `-a` payload, file-name-only `-autoload`, no host path injection.
- **Helper reuse over helper proliferation.** Plan 02 extends `AutoQAC.TestProcessHelper` with `argv-echo` per D-14 instead of adding a second helper executable.
- **UTF-8 JSON over line-based stdout.** Pitfall 4 (Unicode loss through default-encoding stdout) is mitigated by `JsonSerializer` + `Encoding.UTF8`.
- **Concise failure messaging design (D-12).** Plan 03 explicitly forbids leaking executable paths or `-a` payload text into `CleaningResult.Message`, with grep-based negative assertions.
- **Wave 2 is correctly blocked.** `06-03` `depends_on: [06-01, 06-02]` because the failure-mode message branch is keyed off `Mo2ModeEnabled` after both code paths have moved to `ArgumentList`.

## Concerns

### HIGH

- **None.** No blocking issues identified.

### MEDIUM

- **Open Question 2 (xEdit `-autoload` argv shape) is not resolved by the plan.** Research Assumption A2 flags that the current production code emits `-autoload "Plugin.esp"` as a single token, while Plan 01's acceptance criteria require splitting into two `ArgumentList` entries (`-autoload`, `plugin.FileName`). The research recommendation was *"preserve the semantic payload while moving outer process escaping to ArgumentList"* - but Plan 01 chooses the split form without empirical verification that xEdit accepts it. **If xEdit requires the joined form, every direct-mode launch breaks.** Consider: (a) leave the existing single-token `-autoload "Plugin.esp"` shape (using a quote helper, since direct-mode must NOT shell-quote per the anti-pattern, but this token semantically contains the option+value xEdit expects), or (b) add a manual-validation gate in Plan 03 against a real xEdit install before declaring the phase complete.

- **Plan 02 Task 1 says "tests should fail before implementation," but the existing process-clone copies redirection/encoding fields and the test's `argv-echo` would receive an empty `argv` (`Arguments == ""`).** That's a valid red. However, the test factory must be careful: if `Arguments` is left at its default `""` while `ArgumentList` is populated and the clone drops `ArgumentList`, .NET will start the helper with no args. The helper's `args.Length == 0` branch returns exit code 2 - the test must assert on `result.ExitCode == 0` AND the JSON content, not just the exit code, otherwise it could pass the wrong way. The plan's behavior section mentions exit code implicitly but the acceptance criteria don't lock this. Worth tightening.

- **Plan 03's failure message exposes the launch mode token verbatim ("MO2" / "direct xEdit") which is fine, but the test acceptance assertion that "message does not contain `-a` or executable path text" is a substring check.** The literal string `direct xEdit` does not contain `-a` or path text, so this works - but if a future contributor changes wording to `direct-mode launch via configured xEdit path`, the negative assertion would still pass even though the word "xEdit path" is now in the message. Consider asserting against specific configured path *values* (e.g., set `XEditExecutablePath = "C:\\Tools\\SSEEdit.exe"` then assert message does not contain `SSEEdit.exe` or `C:\\Tools`).

- **`PartialFormsEnabled` flags ordering is unspecified for direct-mode argv.** The current code order is `[gameFlag], -QAC, -autoexit, -autoload "plugin", -iknowwhatimdoing, -allowmakepartial`. Plan 01's behavior says "optional partial-form flags" come last. xEdit may or may not care about flag ordering relative to `-autoload`. Worth adding an explicit ordering acceptance assertion to prevent regression.

### LOW

- **Plan 01 Task 1 acceptance criterion mentions `Тест` as alternative to `測試`,** but the research example uses `測試 🚀` and the plan's behavior says "CJK or Cyrillic." The "or" makes this fine, but pick one and lock it to keep test data consistent across plans.

- **Combined worst-case input is described but not specified.** D-15 calls for "one combined worst-case input" but neither plan defines what that string is. Two plans could pick different worst-case strings. Recommend specifying once (e.g., `"Quote\" Résumé 測試 🚀 & | ; ( ) ^ Spaces.esp"`) and reusing across builder unit tests and integration tests.

- **No test for `ArgumentList`-vs-`Arguments` mutual exclusion.** Microsoft documents that mixing the two is undefined. Plan 02's clone logic uses an `else` branch (Arguments only when ArgumentList is empty), which is correct, but no test verifies this branch exists. A test that constructs a `ProcessStartInfo` with `Arguments = "test"` and empty `ArgumentList` and asserts the helper receives `["test"]` would close the gap.

- **`AutoQAC.TestProcessHelper`'s default exit code is 2 for "unknown command,"** but `argv-echo` mode adds a new public surface. If a future test passes `argv-echo` with no payload args (`args.Length == 1`), the existing code happily writes `[]`. That's probably fine, but worth noting in a comment.

- **Plan 01 leaves the MO2 nested payload's quoting helper as planner discretion** ("may use a private Windows-style quoting helper because MO2 owns a second parser boundary"). The CRT-style backslash-and-quote rules from `CommandLineToArgvW` are subtle. Consider citing or linking to a reference implementation (e.g., the algorithm in Microsoft's `ProcessStartInfo.ArgumentList` source code) so the executor doesn't roll a buggy variant.

## Suggestions

1. **Resolve Open Question 2 before Plan 01 implementation.** Either:
   - Verify on a real xEdit install that `-autoload` + plugin filename as two argv tokens works, or
   - Keep `-autoload "plugin"` semantically as one token (build it as `-autoload <space> <plugin.FileName>` with the quote helper that MO2 mode also uses) and mark this as a single-token argv entry. Add a code comment explaining why direct mode uses one token while the rest of argv uses split tokens.

2. **Tighten Plan 02 Task 1's red assertion.** Add `result.ExitCode.Should().Be(0)` AND `JsonSerializer.Deserialize<string[]>(stdout).Should().Equal(expectedArgs)` to the failing test. Without both, a clone that drops `ArgumentList` could produce a zero-exit "no args echoed" run that silently passes if assertions are loose.

3. **Lock the combined worst-case string in Plan 01,** then have Plan 02 reference it. E.g., add a const in `XEditCommandBuilderTests`: `private const string WorstCase = "Quote\" Résumé 測試 🚀 & | ; ( ) ^ Spaces.esp";` and reuse from integration tests.

4. **Add an `Arguments`-fallback test in Plan 02** to prove the `else` branch in the clone keeps working for non-Phase-6 callers (if any). If no callers use `Arguments` after Phase 6, document that and consider deleting the fallback in a later cleanup.

5. **Plan 03: replace generic substring negative assertions with concrete value assertions.** Set realistic `XEditExecutablePath` and `Mo2ExecutablePath` in the failure-flow tests, then assert `Message.Should().NotContain(xEditPath)` and `Message.Should().NotContain(mo2Path)`. This catches regressions where someone substitutes `state.XEditExecutablePath` into the message string.

6. **Add an explicit argv-ordering assertion** to Plan 01 Task 1 acceptance: `result!.ArgumentList.Should().Equal("-FO4", "-QAC", "-autoexit", "-autoload", "Plugin.esp", "-iknowwhatimdoing", "-allowmakepartial");` for the partial-forms-enabled universal-xEdit case. Equality (not contains) prevents both ordering regressions and accidental added flags.

7. **Document the MO2 `-a` quoting helper choice.** If the planner introduces a helper, add a brief XML doc comment citing the specific CRT parsing rules being matched (per the research's `CommandLineToArgvW` reference). This protects future maintainers from "simplifying" the helper.

8. **Consider a Wave 2 manual smoke check** for the open question A2: a one-line note in 06-03-SUMMARY.md confirming the user has run a real xEdit launch on a test plugin (or explicitly accepting the risk). This is cheap insurance against the only remaining HIGH-impact assumption.

## Risk Assessment

**Overall risk: LOW**

Justification:
- **Scope is tight and well-bounded.** Three plans, six tasks total, all touching files already analyzed in CONCERNS.md. No greenfield architecture work.
- **Strong test-first design.** All implementation tasks have failing tests committed first, with grep-based acceptance criteria the executor can verify.
- **Phase 5 foundation is solid.** Helper process, integration test patterns, and process-clone scaffolding already exist and were validated last phase.
- **Locked decisions remove the largest risk class.** No re-litigation of MO2 contract, sanitization, or shell escaping during execution.
- **Single residual technical risk** is the xEdit `-autoload` argv-shape question (A2). If this assumption is wrong, Wave 1 ships a regression that breaks every direct-mode launch - but that risk is detectable on first run with a real xEdit install and reversible (revert to single-token form).
- **No new dependencies, no new services, no DI changes, no UI changes.** The phase preserves all Phase 5 stop/timeout/PID semantics by construction.

Recommend proceeding to execution after addressing Suggestion 1 (resolve `-autoload` argv shape) and ideally Suggestions 2, 3, 5, 6 before Wave 1 starts.

---

## Codex Review

## Overall Summary

The Phase 6 plan set is strong and mostly execution-ready. It correctly identifies the two real failure surfaces: `XEditCommandBuilder` manual escaping and `ProcessExecutionService` dropping `ArgumentList` during cloning. The split into builder contract tests, real process-boundary tests, and final failure-flow polish is coherent. Main risks are around the remaining manual MO2 `-a` nested payload, the still-open `-autoload` argv contract assumption, and 06-03 slightly overclaiming launch-start failure handling while only testing command-build null behavior.

## 06-01-PLAN.md

### Summary

Good plan. It targets the right production file and moves the primary command-construction boundary from fragile string concatenation to `ProcessStartInfo.ArgumentList`. The tests are appropriately contract-focused, but the plan should tighten the exact direct `-autoload` contract and the MO2 nested payload quoting strategy before implementation.

### Strengths

- Uses TDD against `XEditCommandBuilderTests`, which is the right first boundary.
- Separates direct xEdit argv from MO2 wrapper argv instead of flattening the MO2 contract.
- Preserves locked MO2 shape: `run`, xEdit path, `-a`, one nested payload.
- Explicitly checks `Arguments` is empty, reducing accidental mixed API usage.
- Covers quotes/parser cases, Unicode, shell-sensitive punctuation, and combined inputs.

### Concerns

- **HIGH:** The direct-mode contract changes from a raw command string containing `-autoload "plugin"` to argv entries `-autoload`, `plugin.FileName`. That is probably the intended parsed argv, but the plan should explicitly state this is preserving parsed argv behavior and call out the xEdit assumption.
- **HIGH:** MO2 `-a` remains a manual nested command-line string. The plan says it “may use a private Windows-style quoting helper” but does not require a precise quoting algorithm or parser round-trip tests for quotes/backslashes.
- **MEDIUM:** D-02 requires coverage of xEdit executable path, MO2 executable path, plugin filename, and nested xEdit arguments. The plan says this, but the task examples focus mostly on plugin filename. Add an explicit matrix so coverage is not accidental.
- **MEDIUM:** Quote characters cannot exist in real Windows filenames. The plan handles synthetic cases, but should distinguish synthetic builder/parser tests from real path tests so implementers do not create impossible filesystem cases.
- **LOW:** The threat model says “Phase 03 covers user-facing failure copy”; this should say plan 06-03 or Phase 6 Wave 2.

### Suggestions

- Add a required test that documents direct xEdit argv as parsed tokens: `-autoload` and exact plugin filename as separate entries.
- Extract a small private MO2 nested-argument formatter and test it directly with quote/backslash cases.
- Add a table-style test matrix covering each launch-bound input: xEdit path, MO2 path, plugin filename, nested payload.
- Include partial-forms and game-flag cases in at least one direct and one MO2 nested-payload assertion.

### Risk Assessment

**MEDIUM.** The main direct-mode fix is straightforward, but MO2’s nested `-a` payload keeps one manual escaping surface. Without stricter tests around that formatter, the plan could satisfy `ArgumentList` usage while still corrupting nested xEdit args.

## 06-02-PLAN.md

### Summary

This is the most important plan in the set and is well scoped. It closes the gap where builder tests could pass but the real process launch still loses arguments. The helper-process JSON echo approach is the right validation level.

### Strengths

- Directly addresses D-13 with a real launched process.
- Reuses `AutoQAC.TestProcessHelper`, matching D-14.
- UTF-8 JSON output is a good choice for Unicode and delimiter safety.
- Preserves existing process semantics as an explicit constraint.
- Verifies shell-sensitive characters as literal argv text under `UseShellExecute=false`.

### Concerns

- **MEDIUM:** The plan should warn against two consumers reading redirected stdout. If `ProcessExecutionService` already captures stdout, the test should assert through the returned `ProcessResult` rather than reading the stream separately in `onProcessStarted`.
- **MEDIUM:** The clone update must preserve every existing copied `ProcessStartInfo` setting, not only the ones listed. This is especially important around redirection, encodings, environment, and window settings if present.
- **MEDIUM:** The plan chooses `ArgumentList` precedence when both `Arguments` and `ArgumentList` are populated. That is reasonable, but it should either test the precedence or fail fast, because .NET documents these as mutually exclusive concepts.
- **LOW:** Keep an existing fallback test proving old `Arguments`-based callers still work when `ArgumentList.Count == 0`.

### Suggestions

- Add one regression test for legacy `Arguments` fallback.
- Add one test or assertion for “ArgumentList wins and Arguments is ignored” only if that behavior is intentional; otherwise fail fast when both are populated.
- Prefer a helper method in `ProcessExecutionService` for cloning start info so future fields are less likely to drift.
- Make stdout collection follow the existing service pattern to avoid stream-read races.

### Risk Assessment

**LOW to MEDIUM.** The change is narrow and highly testable. Risk mainly comes from accidentally altering existing process execution behavior while touching the clone path.

## 06-03-PLAN.md

### Summary

Useful final integration plan, but it is weaker than the first two because it claims coverage for both command-build and launch-start failures while mostly testing only the `BuildCommand == null` branch. The concise user-facing message is good, but the plan should be clearer about what is handled now versus what remains for SEC-01/SEC-02.

### Strengths

- Correctly keeps failures in the existing `CleaningResult` flow.
- Verifies no process starts when command building fails.
- Avoids exposing full command lines in user-facing messages.
- Separates direct xEdit and MO2 wording.
- Includes final targeted and full-suite verification.

### Concerns

- **HIGH:** D-11 mentions command-build and launch-start failures, but the tests only cover command-builder null. If `Process.Start` fails because the executable path is invalid or impossible, this plan may not improve or verify the user-facing boundary.
- **MEDIUM:** The message checks should avoid brittle assertions like not containing `xEdit.exe` or `-a` if the plugin filename itself could contain those substrings. Assert against configured executable paths/full command text instead.
- **MEDIUM:** `launchMode` should be captured from state before command building if state can change, so the logged/messaged mode matches the mode used by the builder.
- **MEDIUM:** Logging only plugin and launch mode may not provide the “technical detail” promised by the message. If the builder only returns null, there may be no actionable reason logged.
- **LOW:** This plan slightly overlaps future SEC-01/SEC-02. Keep the change limited to the null-command branch unless adding explicit launch-start sanitization tests.

### Suggestions

- Either add launch-start failure tests or narrow the plan language to command-build failures only.
- If launch-start failures are included, mock `IProcessExecutionService.ExecuteAsync` returning a failed `ProcessResult` with path-heavy detail and verify the user-facing message remains safe.
- Consider introducing a small build-result type later if null lacks enough diagnostic detail, but do not expand Phase 6 unless necessary.
- Capture `var state = stateService.CurrentState` once before build/messaging.

### Risk Assessment

**MEDIUM.** The implementation is simple, but the plan overstates its coverage. It is safe if scoped to command-build null failures; risk rises if reviewers expect it to fully satisfy launch-start error-boundary behavior.

## Phase-Level Assessment

### Strengths

- The three plans are ordered well: builder contract, process boundary, then failure messaging.
- Wave 1 can proceed in parallel with low conflict because 06-01 and 06-02 touch different production files.
- The phase directly addresses SAF-03 and TEST-02 without touching Mutagen, UI, or cleaning parallelism.
- The validation strategy is realistic: unit tests for command contracts, helper integration tests for actual argv receipt, full suite at phase close.

### Concerns

- **HIGH:** The only remaining manual escaping surface is MO2 `-a`; it needs the most precise tests.
- **MEDIUM:** The `-autoload` contract should be explicitly documented as parsed argv behavior.
- **MEDIUM:** 06-03 should not imply complete launch-start failure hardening unless it tests that path.
- **LOW:** Keep comments/docstrings aligned with repo policy if adding non-trivial helper methods.

### Final Risk Assessment

**Overall risk: MEDIUM.** The core approach is correct and appropriately scoped. The biggest risk is not `ArgumentList` itself; it is the nested MO2 parser boundary and any unverified assumptions about xEdit/MO2 argument parsing. Tightening those tests would make the phase much closer to low risk.

---

## Consensus Summary

All reviewers agree that the plan set targets the right bug class and uses the right primary mitigation: move AutoQAC-controlled process arguments to `ProcessStartInfo.ArgumentList`, then prove those arguments survive the real `ProcessExecutionService` launch boundary. The strongest recurring caution is that the plan still contains two parser-boundary assumptions that should be tightened before execution: xEdit's direct `-autoload` argv shape and MO2's nested `-a` payload quoting.

### Agreed Strengths

- Correctly identifies `XEditCommandBuilder` manual escaping and `ProcessExecutionService` cloning as the two key failure surfaces.
- Uses `ArgumentList` for AutoQAC-controlled argv boundaries instead of continuing manual shell-style escaping.
- Includes real helper-process verification, not just builder unit tests, for Unicode, quotes, spaces, and shell-sensitive punctuation.
- Preserves the locked MO2 wrapper contract (`run`, xEdit path, `-a`, one nested payload) rather than broadening scope.
- Keeps user-facing command-build failures concise and avoids exposing full command lines or configured executable paths.

### Agreed Concerns

- `-autoload` direct-mode shape remains a meaningful assumption. Two reviewers flagged that changing from the current `-autoload "Plugin.esp"` command-string shape to separate `ArgumentList` entries (`-autoload`, `Plugin.esp`) should be explicitly documented, validated against real xEdit, or revised to preserve existing semantics.
- MO2 `-a` remains the one manual nested command-line surface. Multiple reviewers recommend requiring a precise quote/backslash formatter, direct formatter tests, parser round-trip checks where practical, and a comment/docstring explaining why this boundary is intentionally different from direct mode.
- Process-boundary tests need to be strict enough to fail for the right reason. Reviewers recommend asserting both helper exit code and parsed JSON argv, preserving/covering legacy `Arguments` fallback or explicitly failing mixed `Arguments`/`ArgumentList`, and avoiding stdout stream-read races.
- Plan 03 should either narrow its language to command-build failures or add explicit launch-start failure coverage. Codex specifically called out that `BuildCommand == null` tests do not prove user-facing boundaries for `Process.Start` failures.

### Divergent Views

- Overall risk rating differed: Gemini and the agent rated the phase LOW, while Codex rated it MEDIUM because of the MO2 nested parser and launch-start coverage gaps.
- Gemini suggested considering Windows command-line length warnings, while the other reviewers did not treat that as necessary for this phase.
- The agent recommended a manual real-xEdit smoke check for the `-autoload` shape before declaring the phase complete; Codex framed the same issue as plan text/test-contract tightening, while Gemini did not flag `-autoload` as a major risk.
