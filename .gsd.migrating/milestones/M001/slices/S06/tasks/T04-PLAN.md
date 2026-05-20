# T04: 05-process-stop-pid-safety 04

**Slice:** S06 — **Milestone:** M001

## Description

Add the single-instance guard and real-process validation harness required to prove Phase 5 safety behavior.

Purpose: PID/process safety cannot be trusted if duplicate AutoQAC instances can race PID writes, and TEST-01 requires controlled real child-process coverage that runs by default.
Output: Named mutex guard, startup wiring, helper executable, integration tests, and test project wiring.

## Must-Haves

- [ ] "Multiple AutoQAC instances are blocked by a single-instance app lock (D-14)."
- [ ] "Real child-process timeout, graceful stop signal, force kill, and PID cleanup behavior run under `dotnet test` (D-15, D-16, D-17, D-18, TEST-01)."
- [ ] "Helper process tests are bounded and clean up helper processes in `finally` paths (D-18)."

## Files

- `AutoQAC/Services/Process/ISingleInstanceGuard.cs`
- `AutoQAC/Services/Process/SingleInstanceGuard.cs`
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC/App.axaml.cs`
- `AutoQAC.Tests/Services/SingleInstanceGuardTests.cs`
- `AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs`
- `AutoQAC.Tests/TestProcessHelper/AutoQAC.TestProcessHelper.csproj`
- `AutoQAC.Tests/TestProcessHelper/Program.cs`
- `AutoQAC.Tests/AutoQAC.Tests.csproj`
- `AutoQACSharp.slnx`
