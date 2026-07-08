---
phase: 11
reviewers: [gemini, codex, opencode]
reviewed_at: 2026-05-01T04:11:47Z
plans_reviewed: [11-01-PLAN.md, 11-02-PLAN.md, 11-03-PLAN.md, 11-04-PLAN.md, 11-05-PLAN.md, 11-06-PLAN.md]
---

# Cross-AI Plan Review - Phase 11

## Gemini Review

# Phase 11: User-Facing Diagnostics Boundaries — Plan Review

The implementation plans for Phase 11 provide a comprehensive and surgically precise approach to hardening the application's diagnostic boundaries. By shifting from a "raw detail" model to a "classified safe copy" model, the plans effectively address **SEC-01** and **SEC-02** while preserving the troubleshooting utility required for a local developer tool.

## Summary
Phase 11 is structured as a TDD-driven hardening pass that establishes a centralized `DiagnosticTextFormatter` to govern all user-facing and log-bound strings. The strategy avoids the pitfalls of global post-processing (like regex scrubbing) by sanitizing data at the source—before it reaches ViewModels, Exports, or Serilog templates. The waves are logically ordered: first establishing the formatting logic (Wave 1), then applying it to UI and Export surfaces (Wave 2), and finally hardening the sensitive Process/Startup log boundaries (Wave 2) before a final verification sweep (Wave 3).

## Strengths
- **Centralized Sanitization Logic:** The creation of `DiagnosticTextFormatter` ensures that "safe copy" rules are applied consistently across Services, Models, and ViewModels.
- **Empirical Regression Testing:** The use of "Unsafe Sentinel" strings (e.g., `C:\Users\Alice`, `-QAC`) in every test wave provides a high-signal verification method that the boundaries are actually working.
- **Preservation of Troubleshooting Context:** The plans cleverly replace sensitive command lines and paths in logs with structured metadata (PID, ArgumentCount, LaunchMode), ensuring logs remain useful for debugging without being a liability.
- **Defensive Export Layer:** Plan 11-04 correctly identifies that `PluginCleaningResult` is the source for both UI and Reports, and it implements sanitization at the model level to protect both sinks simultaneously.
- **Respect for Prior Hardening:** The plans explicitly preserve the safe typed failure patterns established in Phase 07 and Phase 10 (`SafeSummary`), avoiding redundant work.

## Concerns
- **Source-Read Guards (Low):** Plan 11-06 uses source-file reading to verify the absence of forbidden log templates in `App.axaml.cs`. While effective for private/startup code that is hard to mock, this can be fragile if code formatting changes.
- **Environment Path Separators (Low):** While the app is Windows-only, `DiagnosticTextFormatter` should ensure its "Safe Basename" logic handles both forward and backslashes robustly to prevent leaks if a path is ever passed in a non-standard format. (Addressed in Plan 11-01 Task 2).
- **Log Property Safety (Medium):** In `ProcessExecutionService`, if an exception object is logged along with a safe message template, some Serilog sinks might still include the `ex.Message` property in a structured way. The plan relies on the fact that AutoQAC uses local file/console logs where the message template governs the immediate visibility, but the "Technical details were written to the log" guidance assumes the user will see the raw exception eventually. This is an acceptable trade-off for a local tool.

## Suggestions
- **Robustness in Sanitization:** Ensure `DiagnosticTextFormatter.SafeFileIdentifier` handles inputs that are already just filenames or are malformed paths without throwing.
- **Log disclaimer placement:** In Plan 11-04, ensure the `ReportDisclaimer` is placed at the top of the exported report so it is immediately visible to the user before they scroll through failed plugin lists.
- **Test Sequentiality:** As noted in the plans, ensure all verification steps run sequentially to avoid MSBuild file locks on `QueryPlugins.dll` or `AutoQAC.dll` which have hampered previous phases.

## Risk Assessment: LOW
Phase 11 carries low technical risk because it does not modify the core "sequential cleaning" state machine, backup logic, or command construction logic. The changes are strictly confined to the strings and properties passing through the UI and logging layers. The TDD approach ensures that existing functionality is not regressed while the new safety boundaries are established.

**Verdict:** The plans are ready for execution.

---

## Codex Review

## Summary

The Phase 11 plan set is coherent and mostly well-sequenced. It correctly treats diagnostics hardening as a boundary problem: safe formatter first, then UI/status/config/result/log call sites, then phase-level sentinels. The strongest part is the test-first shape with explicit unsafe sentinels and preservation of prior typed safe summaries. Main risks are heuristic redaction gaps, some source-guard tests that may be brittle or shallow, and Plan 11-05’s logging changes being large enough to accidentally touch launch behavior or overfit to logging templates.

## 11-01 Shared Formatter

### Strengths
- Establishes a shared safe-copy contract before dependent plans.
- TDD coverage includes C, D, and J drive-rooted path sentinels, command fragments, stack-like text, and unsafe plugin names.
- Avoids DI churn by using a static formatter available to models, services, startup, and ViewModels.
- XML doc requirement is explicit.

### Concerns
- **MEDIUM:** `SafeFailureSummary` is heuristic-based and may miss unsafe exception text that does not contain `System.`, ` at AutoQAC.`, paths, or listed flags, such as `UnauthorizedAccessException: denied profile root`.
- **MEDIUM:** The plan does not explicitly require case-insensitive matching for `-qac`, `.EXE `, `system.`, or mixed-case paths.
- **LOW:** Putting user-facing copy under `AutoQAC.Models.Diagnostics` couples model code to UI wording, though this was an accepted planning decision.
- **LOW:** Exact string tests are useful here but could become noisy if UI-SPEC copy changes later.

### Suggestions
- Require `SafeFailureSummary` unsafe checks to be `StringComparison.OrdinalIgnoreCase`.
- Treat common exception suffixes/types as unsafe too: `Exception`, `UnauthorizedAccess`, `IOException`, `Access denied`, and stack-frame markers.
- Add tests for `J:/path/file.exe`, UNC paths, lowercase flags, and empty/whitespace operation names.

### Risk Assessment
**MEDIUM.** Good foundation, but this helper becomes the phase’s safety primitive. Any false-negative redaction logic will propagate.

## 11-02 Cleaning/Preview UI Boundary

### Strengths
- Targets the highest-visibility leak path first: command-boundary catches.
- Preserves existing logging while replacing only user-facing exception/status text.
- Separates unexpected failures from `InvalidOperationException` validation branching.
- Pre-clean validation wording aligns with D-05/D-08.

### Concerns
- **MEDIUM:** The plan says not to change `InvalidOperationException` validation branching, but some `InvalidOperationException`s can be unexpected technical failures. The distinction must be verified against current code paths.
- **MEDIUM:** Validation tests focus on missing paths; they may not cover malformed path strings, empty basenames, or control-character basenames.
- **LOW:** Acceptance criteria looking for literal `xEdit Path (` in source can encourage hardcoded strings instead of formatter usage.

### Suggestions
- Add one test where the configured path basename sanitizes to fallback.
- Confirm `InvalidOperationException` is only used for safe validation messages before preserving that branch.
- Prefer acceptance criteria around formatter calls and behavior, not source literals.

### Risk Assessment
**LOW-MEDIUM.** Scope is narrow and valuable. Main risk is preserving an existing exception branch that may still leak.

## 11-03 Configuration/Restore Diagnostics

### Strengths
- Covers important non-cleaning surfaces: browse failures, selected folders, restore session loading, and persistence banners.
- Explicitly preserves `ConfigPersistenceFailure.SafeSummary` and backup failure labels.
- Good distinction between simple validation and technical failures needing latest-log guidance.

### Concerns
- **MEDIUM:** “Configuration browse failure” behavior depends heavily on current ViewModel seams. Tests may be hard to write without over-mocking or asserting implementation details.
- **MEDIUM:** Game display name fallback needs care; `SelectedGameDisplayName or "selected game"` can produce awkward or inconsistent copy if current game state is unavailable.
- **LOW:** Task 3 is coverage-only but may discover implementation leaks; plan should explicitly allow minimal source changes if needed, which it partly does.

### Suggestions
- Define a stable fallback phrase for unknown game folder issues, e.g. `selected game data folder`.
- Add tests for path-bearing exceptions from both file dialog cancellation/selection and file read/parse failure paths, if those are distinct.
- Make sure tests assert all exposed fields, not only `StatusText`.

### Risk Assessment
**MEDIUM.** Correct targets, but ViewModel setup complexity can cause fragile tests or missed surfaces.

## 11-04 Result Rows And Reports

### Strengths
- Correctly sanitizes at source in `PluginResultFinalizer`, not only at export time.
- Adds defensive checks in `CleaningSessionResult.GenerateReport()` and `PluginCleaningResult.Summary`.
- Handles xEdit exception-log content explicitly, which is a critical SEC-01 surface.
- Preserves plugin filenames and useful counts/durations.

### Concerns
- **HIGH:** The proposed finalizer rule “failed result messages are assigned through `CleaningFailedForPlugin` when `result.Success` is false or `finalStatus` is failed” may flatten already-safe, actionable failure categories unless carefully scoped.
- **MEDIUM:** Logging change says not to include exception-log content as a property. That satisfies disclosure, but may reduce local troubleshooting value unless the raw detail is logged elsewhere through an allowed technical channel.
- **MEDIUM:** Report fallback example could duplicate plugin name: `Fail.esp: Fail.esp: Cleaning failed...`.

### Suggestions
- Preserve known safe typed messages where `SafeFailureSummary` accepts them; only fallback when unsafe or unknown.
- Decide whether xEdit exception-log content should be logged at debug/error boundary with clear local-only intent, or intentionally omitted. The current plan may undercut troubleshooting.
- Normalize report formatting to avoid duplicated plugin names.

### Risk Assessment
**MEDIUM-HIGH.** This plan touches user-visible cleaning outcomes and could accidentally reduce useful result specificity.

## 11-05 Process/Startup Logs

### Strengths
- Directly addresses SEC-02 and D-13 through D-16.
- Keeps launch argv construction out of scope and focuses on log call sites.
- Adds PID, launch mode, game, plugin filename, argument count, status, and reason fields.
- Explicitly forbids raw `FileName`, `Arguments`, `ArgumentList`, `-QAC`, and `-autoload` logging.

### Concerns
- **HIGH:** This is the riskiest plan. It changes logging in both `CleaningService` and `ProcessExecutionService`, and tests may require substantial setup. There is risk of accidentally changing process construction or launch flow.
- **MEDIUM:** “Do not include raw `-QAC` anywhere in captured log calls” can fail if exception objects or lower layers log command payloads legitimately. The boundary should be clear about message template/properties versus exception text.
- **MEDIUM:** Source guard tests in `DependencyInjectionTests` are brittle and may pass while equivalent unsafe logging is reintroduced under a different template.
- **LOW:** `SafePluginName(state.XEditExecutablePath, "not configured")` for executable basename works, but a `SafeFileIdentifier("xEdit Path", ...)` shape may be clearer.

### Suggestions
- Introduce a small launch diagnostics context object only if it avoids duplicating operation/mode/plugin/count computation.
- Keep behavior tests for injectable services; use source guards only for private startup code as a last resort.
- Add explicit tests that `ProcessStartInfo.FileName`, `Arguments`, and `ArgumentList` values are unchanged after logging changes.
- Clarify whether exception object rendering is in scope for log redaction, since Serilog may serialize exception messages separately.

### Risk Assessment
**HIGH.** Necessary plan, but it has the largest blast radius and the highest chance of fragile tests or accidental behavior drift.

## 11-06 Phase-Level Sentinels

### Strengths
- Adds final cross-surface regression coverage after focused plans land.
- Requires real production APIs or source reads, avoiding pure hardcoded string assertions.
- Includes final focused and full solution test commands.
- Explicitly avoids new UI infrastructure and Avalonia.Headless scope creep.

### Concerns
- **MEDIUM:** The phase-level tests may duplicate earlier focused tests and become maintenance-heavy.
- **MEDIUM:** Source-read assertions for forbidden tokens can be brittle and can miss semantically equivalent unsafe logging.
- **LOW:** Defining sentinel arrays independently in each file risks drift.

### Suggestions
- Prefer a shared test helper for sentinel assertions unless project conventions discourage it.
- Keep phase-level tests small and focused on integration boundaries not already covered.
- For source guards, assert both absence of known bad templates and behavior-level logger output where feasible.

### Risk Assessment
**LOW-MEDIUM.** Good final safety net, but should avoid becoming a brittle duplicate of the focused suite.

## Overall Risk Assessment

**Overall risk: MEDIUM.** The plans are well-aligned with SEC-01/SEC-02 and the wave ordering is sound: formatter first, dependent surfaces second, final sentinels last. The main risks are Plan 11-05’s logging blast radius, over-reliance on heuristic redaction, and possible loss of useful safe failure specificity in Plan 11-04. Tightening `SafeFailureSummary`, preserving known safe typed summaries, and verifying process command objects remain unchanged would make the set substantially safer.

---

## OpenCode Review

## Phase 11 Plan Review: User-Facing Diagnostics Boundaries

### Summary

The six plans form a coherent, well-scoped hardening pass. The wave structure is sensible, the TDD discipline is consistently applied, and the formatter-first approach (11-01) creates a clean dependency chain. The plans stay tightly within the declared scope—no new UI infrastructure, no behavior changes—and the decision traceability from D-01 through D-16 is solid throughout. The main risks are a few concrete implementation gaps in the process-log task, some test fixture ambiguities that could make the red phase fragile, and the cross-surface sentinel tests in 11-06 straddling the line between real regression guards and source-read assertions that could pass trivially.

---

### Plan 11-01 — Shared Formatter

**Strengths**
- Static class in `AutoQAC.Models.Diagnostics` avoids DI churn while remaining accessible to all layers.
- Drive-rooted detection specified generically (`letter + colon + slash`) rather than just `C:\` — catches the non-C-drive sentinels already in the test suite.
- `SafeFailureSummary` acts as a defensive last-resort filter at report boundaries, not as the primary strategy.
- XML doc mandate is explicit and tied to repo instructions.

**Concerns**
- **MEDIUM** — The sanitization character blocklist in Task 2 (`"`, `'`, `` ` ``, `|`, `&`, `;`, `<`, `>`) differs from the character set in the research's `SafeBasename` example (`Path.GetInvalidFileNameChars()` + `` ` ``). The spec is the plan, so the plan wins, but the mismatch could surprise an implementer who reads the research first. Should clarify which set governs.
- **LOW** — `SafeFailureSummary` checks for `.exe ` (with trailing space) to detect command fragments. A path ending in `.exe"` or `.exe\n` without a trailing space would slip through. Consider checking `.exe` without the trailing-space constraint, or add additional checks.
- **LOW** — `SafeFolderIssue` returns a hardcoded action phrase ("Choose a valid Data folder or reset the override.") baked into the formatter. If the action wording needs to differ per call site (e.g., restore vs. configuration), callers have no override path. Not a blocker now, but worth noting.

**Suggestions**
- Add one test specifically covering a path with no file extension (e.g., a bare executable without `.exe`) to confirm `SafeFileIdentifier` still produces a useful label.
- Add a `SafeFailureSummary` test for `.exe"` without trailing space to lock down the boundary.

---

### Plan 11-02 — Cleaning/Preview Dialogs and Validation

**Strengths**
- Three focused tasks cleanly separate unexpected-exception copy (Tasks 1–2) from validation-row identifiers (Task 3).
- Acceptance criteria check for absence of `Stack Trace:` in the source file — a concrete, grep-able guard.
- Task 3 correctly restricts latest-log guidance to unexpected failures only (D-08).

**Concerns**
- **MEDIUM** — Task 1 specifies RED tests that depend on the `ICleaningOrchestrator` substitute throwing — but the existing `CleaningCommandsViewModel` test fixture setup is not described. If the existing `ErrorDialogTests` fixture does not already wire `ICleaningOrchestrator` as a substitute, the RED tests may fail to compile (not fail at runtime as expected from a red TDD cycle). The plan should mention verifying/updating fixture wiring.
- **MEDIUM** — Task 3 validation behavior mentions three specific messages but `ValidatePreClean()` may produce different message formats depending on load-order game type vs. Mutagen game type. The plan doesn't confirm whether load-order validation is only emitted for file-load-order games; if the Mutagen path also hits this code, tests may have false positives.
- **LOW** — The acceptance criteria check for `xEdit Path (` in source but not for the absence of the raw path like `state.XEditExecutablePath` being directly concatenated into a message string. A source check for the negative would be stronger.

**Suggestions**
- Clarify which validation branches apply to which game types, or make the test parametric over game type to prevent silent misses.
- Add an acceptance criterion asserting `CleaningCommandsViewModel.cs` does not contain `ex.StackTrace` (parallel to the `Stack Trace:` check).

---

### Plan 11-03 — Configuration Browse and Restore Diagnostics

**Strengths**
- Task 3 (settings persistence) is correctly scoped as coverage-only: it verifies the existing `ConfigPersistenceFailure.SafeSummary` path and only modifies implementation if a real leak is discovered, preserving D-04.
- Using non-C-drive sentinels (`J:\Users\Alice`, `D:\Profiles`) in Task 3 tests directly validates the generic drive-rooted detection from 11-01.
- Existing `BackupFailureReason.ToDisplayLabel()` is explicitly preserved.

**Concerns**
- **HIGH** — Task 1 says "update `ConfigurationViewModel` catch/status/dialog text" but does not read `ConfigurationViewModel` as a required read-first file in its own right — only in Task 1's read-first list. More critically, the plan's `files_modified` header lists `ConfigurationViewModel.cs` but Task 1's `<files>` tag specifies it. If the task agent only reads listed files, the lack of a second read of the actual current catch block structure could lead to incorrect surgery on a method that has already-safe branches from prior phases.
- **MEDIUM** — The plan lists `AutoQAC.Tests/ViewModels/SettingsViewModelTests.cs` in `files_modified` but Task 3 creates tests there with no corresponding implementation change in `SettingsViewModel.cs` listed as modified. If the existing ViewModel already leaks (which the plan acknowledges is possible), there's no task that explicitly updates `SettingsViewModel.cs` — only a conditional "if tests expose an actual raw-detail leak." This conditionality could leave a real leak unfixed if the agent's test is subtly wrong.
- **LOW** — Task 2 hardcodes the status text `Backup sessions could not be loaded. See the latest AutoQAC log for technical details.` in the acceptance criteria but does not check for the absence of `Error loading sessions: ` in the test (only in source). A test-level negative assertion would be more durable.

**Suggestions**
- Make Task 3's implementation step unconditional: always map persistence failures through `SafeSummary` in user-visible text, even if current code happens to be safe, to prevent future regression.
- Add `RestoreViewModel.cs` to `files_modified` explicitly alongside the source check, rather than only in the task action.

---

### Plan 11-04 — Cleaning Results and Reports

**Strengths**
- Two-task structure cleanly separates source sanitization (finalizer) from defensive export check (report).
- D-10 is correctly implemented as a source-boundary principle, not a post-processing filter.
- The acceptance criterion prohibiting `xEdit exception log for {Plugin}: {Content}` as a log template is specific and grep-able.
- Disclaimer appears exactly once — the `CleaningSessionResultTests` test for exact-once behavior is a good regression guard.

**Concerns**
- **HIGH** — Task 1 says to change xEdit exception-log logging to avoid `{Content}` as a structured property, but it does NOT explicitly say to log exception content anywhere else for local troubleshooting (e.g., via `logger.Warning(ex, ...)` where `ex` wraps the content). D-14 allows full paths/content when "the path is the direct failing local resource." If the intent is that exception-log content is never logged at all, this should be stated; if the intent is it may be logged as an exception object or safe string, the plan should say so. Currently ambiguous.
- **MEDIUM** — `PluginCleaningResult.Summary` is modified in Task 2, but `PluginCleaningResult.cs` does not appear in the plan's `must_haves.artifacts` — only in `files_modified`. This makes it easy for an executor to miss the `Summary` projection change.
- **LOW** — The acceptance criteria for Task 2 check for `System.InvalidOperationException`, `C:\Users\Alice`, and `-QAC` in report output, but not for stack trace content (`at AutoQAC.`). Should include ` at AutoQAC.` in the report-level sentinel set to match 11-06's sentinel definition.

**Suggestions**
- Add explicit guidance on whether xEdit exception-log content should be captured in AutoQAC logs at all, and if so, via what mechanism (e.g., the content as an exception message wrapped in a local exception, or omitted entirely).
- Add `PluginCleaningResult.cs` to `must_haves.artifacts`.

---

### Plan 11-05 — Process/Startup Logs and Migration Warnings

**Strengths**
- The split between `CleaningService` (caller-side safe context) and `ProcessExecutionService` (transport-level safe fields) is architecturally correct — the service that knows about game/plugin/mode provides the meaningful context.
- PID inclusion in post-start logs is correctly scoped to "when available" to avoid crashes on `Process.Id` access failures.
- Source-guard approach for `App.axaml.cs` is appropriate given the private helper structure.

**Concerns**
- **HIGH** — Task 1's action is extremely long and covers two separate files (`CleaningService.cs` and `ProcessExecutionService.cs`) with complex interleaved requirements. The risk of partial implementation or conflicts between the two call sites is significant. These should be two separate tasks or at minimum two separate `<files>` subtasks with independent acceptance criteria checks.
- **HIGH** — The plan requires `CleaningServiceTests` to use a substitute `IProcessExecutionService` that "records the received `ProcessStartInfo` but does not launch." However, `ProcessStartInfo` is a concrete type, not an interface — the substitute captures only the call to `IProcessExecutionService.ExecuteAsync(command, ...)` where `command` is the `ProcessStartInfo`. If the plan means to assert on logger calls from `CleaningService`, those assertions require a captured `ILoggingService`, not a `ProcessStartInfo` substitute. The plan conflates two different capture targets. This needs to be unambiguous.
- **MEDIUM** — The `argumentCount` calculation in Task 1 (`ArgumentList.Count > 0 ? ArgumentList.Count : ...`) is a loose heuristic. If `ArgumentList` is populated, this is correct; but MO2 mode may use both `FileName` and `ArgumentList` differently. The plan doesn't confirm that `CleaningService` always uses `ArgumentList` (vs. `Arguments`) post-Phase 06. If there's a mixed path, argument count could log 0 when arguments exist.
- **MEDIUM** — Task 2 proposes `DiagnosticTextFormatter.SafePluginName(state.XEditExecutablePath, "not configured")` for the xEdit startup log. `SafePluginName` is named for plugins — using it for an executable path is semantically odd. `SafeFileIdentifier` would be more appropriate and already handles the label + basename pattern.
- **LOW** — The DependencyInjectionTests source-read guard asserting `xEdit Path: {XEditPath}` is absent is fragile: if the template string is reformatted or split across lines, the string comparison fails. A regex-based search would be more durable.

**Suggestions**
- Split Task 1 into two tasks: one for `ProcessExecutionService` log changes with `ProcessExecutionServiceTests`, and one for `CleaningService` caller-side diagnostics with `CleaningServiceTests`.
- Clarify that logger-capture assertions target a captured `ILoggingService` substitute (not a `ProcessStartInfo` substitute).
- Change the `SafePluginName` usage for xEdit path in Task 2 to `SafeFileIdentifier`.

---

### Plan 11-06 — Phase-Level Sentinels and Final Verification

**Strengths**
- The three-file split (ViewModel/Model/Service) mirrors the architectural layers, making future maintenance obvious.
- Explicit rule that tests must call real production APIs, not assert against hardcoded safe literals only — this is the right constraint to prevent hollow tests.
- Sequential (not parallel) final verification run acknowledges the documented MSBuild file-lock issue from prior phases.

**Concerns**
- **HIGH** — `Phase11LogBoundaryTests` is permitted to source-read production files and assert forbidden template strings are absent. This is a source-text assertion, not a behavioral test. If `ProcessExecutionService.cs` is refactored to split a long method but retains the same safe log calls, the source guard might accidentally fail on intermediate states. More critically, source-read tests do not catch runtime composition issues (e.g., a helper that injects the path via a variable). Behavioral capture tests are strongly preferred where the seam exists.
- **MEDIUM** — The shared `UnsafeDiagnosticSentinels` set is defined locally in each test file (or a private helper), not in a shared test utility class. This means if a sentinel needs to be updated, it must be changed in three places. A shared constant class in `AutoQAC.Tests/Helpers/` would be more maintainable.
- **MEDIUM** — `Phase11DiagnosticsBoundaryTests` is described as invoking "existing ViewModel/test fixture paths or calling production diagnostics formatter/validation helpers." This is vague enough that an executor could satisfy it by calling `DiagnosticTextFormatter.OperationFailed("Cleaning")` directly — which would pass trivially without exercising any ViewModel catch block. The acceptance criteria should require that at least one test exercises a real ViewModel command path.
- **LOW** — Task 2's conditional "if failure is caused by a Phase 11 regression, fix it" is reasonable guidance, but leaves undefined what the executor should do if a pre-existing failure was silently masked by a prior test arrangement. Given the 838-test baseline, this is low risk.

**Suggestions**
- Extract `UnsafeDiagnosticSentinels` into a shared `AutoQAC.Tests/Helpers/DiagnosticSentinels.cs` static class rather than duplicating it in three test files.
- Require `Phase11DiagnosticsBoundaryTests` to exercise at least one ViewModel command catch path (e.g., `CleaningCommandsViewModel.StartCleaningCommand` with a throwing orchestrator) rather than allowing formatter-only calls.
- Where `ProcessExecutionService` already has test seams (captured logger substitute), require behavioral capture over source-read for the log boundary assertions.

---

### Overall Risk Assessment: **MEDIUM**

The plan set is well-structured and the phase goal is achievable. The risks are concentrated in two areas:

1. **11-05 Task 1** is the highest single-task risk — it's too large, conflates two separate files and two separate capture targets, and has an ambiguous `argumentCount` calculation. If this task executes incorrectly, it could produce tests that pass for the wrong reasons (logger assertions targeting the wrong substitute).

2. **11-06's source-read guards** are a structural weakness: they can pass without proving behavioral safety. Combined with the vague `Phase11DiagnosticsBoundaryTests` scope, the phase-level sentinel net could be thinner than it appears.

Neither risk is a blocker — both are correctable with targeted plan amendments before execution. The core architecture (formatter-first, source-boundary classification, no post-hoc scrubbing) is sound and aligns with the research findings and locked decisions.

---

## Consensus Summary

All three reviewers found the Phase 11 plan set coherent, properly scoped, and aligned with SEC-01/SEC-02. Gemini rated the overall risk low; Codex and OpenCode rated it medium because implementation risk is concentrated in a few plans rather than spread across the phase.

### Agreed Strengths
- The formatter-first dependency chain is sound: `11-01` creates a shared safe-copy boundary before dependent UI, report, and log plans execute.
- The phase preserves core cleaning, launch, backup, restore, and plugin-refresh behavior while hardening only diagnostic boundaries.
- The TDD/sentinel approach is high-signal and directly targets stack traces, paths, exception text, and command fragments.
- Structured log fields such as operation, launch mode, game, plugin filename, PID, argument count, status, and reason preserve troubleshooting value without reconstructing raw commands.
- Source sanitization before report/export boundaries is the right approach; reviewers especially agreed that `PluginCleaningResult` and `CleaningSessionResult.GenerateReport()` must both be protected.

### Agreed Concerns
- **HIGH: Plan 11-05 is the main execution risk.** Codex and OpenCode both flagged its blast radius across `CleaningService` and `ProcessExecutionService`, and Gemini separately noted log-property safety concerns. Tighten this before execution by splitting or clarifying tasks, preserving `ProcessStartInfo` behavior, and making logger-capture targets explicit.
- **HIGH/MEDIUM: Source-read guards should not be the primary proof where behavioral seams exist.** All reviewers mentioned brittleness or shallowness around source guards. Use behavioral logger/dialog/model capture tests for injectable services and reserve source guards for private startup code only.
- **MEDIUM: `SafeFailureSummary` needs stronger unsafe-detail detection.** Codex and OpenCode both found heuristic gaps, including case sensitivity, `.exe` variants, UNC/forward-slash paths, lowercase flags, and common exception names not starting with `System.`.
- **MEDIUM: Plan 11-04 may flatten useful failure specificity or lose xEdit troubleshooting detail.** Codex and OpenCode both asked to preserve already-safe typed/actionable messages and explicitly decide whether xEdit exception-log content should be logged locally, omitted, or wrapped safely.
- **MEDIUM: Phase-level sentinels risk becoming duplicate or hollow tests.** Codex and OpenCode both warned that `11-06` should exercise real ViewModel/model/logger paths, not only formatter calls or source-text checks.

### Divergent Views
- Gemini considered the plan set ready with low overall risk, while Codex and OpenCode recommended targeted plan amendments before execution.
- Gemini treated exception-object logging as an acceptable local-tool trade-off; Codex and OpenCode asked the plan to explicitly define whether exception text and xEdit exception-log content are in scope for log redaction.
- Gemini accepted source-read guards as pragmatic for startup/private code; Codex and OpenCode were more skeptical and recommended behavioral capture wherever seams already exist.
- Codex suggested a shared sentinel helper may be useful, while OpenCode was stronger about extracting `UnsafeDiagnosticSentinels`; the existing plan currently allows local private helpers.
