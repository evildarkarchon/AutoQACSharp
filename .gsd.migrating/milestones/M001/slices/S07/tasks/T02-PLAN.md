# T02: 06-command-launch-escaping 02

**Slice:** S07 — **Milestone:** M001

## Description

Prove and implement preservation of `ProcessStartInfo.ArgumentList` through the actual process-start boundary.

Purpose: A command-builder fix is incomplete if `ProcessExecutionService` drops `ArgumentList` while cloning `ProcessStartInfo`; users need the actual launched process to receive the intended argv.
Output: UTF-8 JSON argv echo helper, integration tests, and process-layer clone changes.

## Must-Haves

- [ ] "D-13: ProcessExecutionService preserves ProcessStartInfo.ArgumentList through its internal clone and real process launch."
- [ ] "D-14: AutoQAC.TestProcessHelper exposes an argv-echo mode instead of adding a second helper process."
- [ ] "D-03/D-04: Integration tests prove Unicode and shell-sensitive characters arrive as literal argv text under UseShellExecute=false."
- [ ] "D-01/D-15/D-16: Quote/parser and combined worst-case cases are verified through parsed argv rather than raw direct command strings."
- [ ] "Reviews: Process-boundary tests assert helper exit code 0 plus exact parsed JSON argv, and they preserve legacy `Arguments` fallback when `ArgumentList` is empty."

## Files

- `AutoQAC/Services/Process/ProcessExecutionService.cs`
- `AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs`
- `AutoQAC.Tests/TestProcessHelper/Program.cs`
