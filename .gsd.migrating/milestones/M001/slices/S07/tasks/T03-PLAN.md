# T03: 06-command-launch-escaping 03

**Slice:** S07 — **Milestone:** M001

## Description

Integrate launch-build failures into existing cleaning failure flow with concise, safe user-facing text and final phase verification.

Purpose: If AutoQAC cannot build a safe direct or MO2 launch command, it must fail before starting any process while avoiding full command-line exposure in the user-facing result.
Output: CleaningService failure-flow tests and implementation plus final targeted/full-suite verification.

## Must-Haves

- [ ] "D-09: Command-build failures return before any process starts."
- [ ] "D-11: Command-build failures and mocked launch-start failures continue through the existing CleaningResult failed flow without new session policy."
- [ ] "D-12: User-facing launch-escaping failure messages name the plugin and mode, state that no process was started, and avoid full command lines."
- [ ] "Reviews: Failure-message tests assert against concrete configured path values, not generic substrings such as `xEdit.exe` or `-a`."
- [ ] "SAF-03/TEST-02: Full targeted and solution tests pass after builder, process, and failure-flow changes are integrated."

## Files

- `AutoQAC/Services/Cleaning/CleaningService.cs`
- `AutoQAC.Tests/Services/CleaningServiceTests.cs`
