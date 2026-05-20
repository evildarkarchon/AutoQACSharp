# T01: 05-process-stop-pid-safety 01

**Slice:** S06 — **Milestone:** M001

## Description

Create the PID storage foundation for Phase 5.

Purpose: Process/PID cleanup cannot be tested or made process-safe while the PID path and JSON read/write logic are private helpers inside `ProcessExecutionService`.
Output: Injectable PID store/path/session abstractions, locked JSON implementation, session-aware `TrackedProcess`, and direct unit tests.

## Must-Haves

- [ ] "Maintainer can test PID tracking through injected storage/path abstractions without reflection (D-10, REF-04)."
- [ ] "PID JSON read-modify-write operations are protected by an interprocess file lock (D-11)."
- [ ] "Corrupt PID JSON is preserved as a timestamped copy before a clean store is recreated (D-12)."
- [ ] "Tracked PID entries include a session ID so current-run and prior-run entries are distinguishable (D-13)."

## Files

- `AutoQAC/Models/TrackedProcess.cs`
- `AutoQAC/Services/Process/IPidStore.cs`
- `AutoQAC/Services/Process/JsonPidStore.cs`
- `AutoQAC/Services/Process/IPidStorePathProvider.cs`
- `AutoQAC/Services/Process/DefaultPidStorePathProvider.cs`
- `AutoQAC/Services/Process/IProcessSessionIdProvider.cs`
- `AutoQAC/Services/Process/ProcessSessionIdProvider.cs`
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC.Tests/Services/JsonPidStoreTests.cs`
