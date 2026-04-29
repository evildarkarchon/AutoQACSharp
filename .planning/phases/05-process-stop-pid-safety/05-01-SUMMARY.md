---
phase: 05-process-stop-pid-safety
plan: 01
subsystem: process-pid-store
tags: [process, pid, json, locking, dependency-injection]
requires: []
provides: [IPidStore, JsonPidStore, process-session-id]
affects: [AutoQAC/Services/Process, AutoQAC/Infrastructure]
tech_stack:
  added: []
  patterns: [constructor-injection, file-locking, corruption-preserving-json]
key_files:
  created:
    - AutoQAC/Services/Process/IPidStore.cs
    - AutoQAC/Services/Process/IPidStorePathProvider.cs
    - AutoQAC/Services/Process/IProcessSessionIdProvider.cs
    - AutoQAC/Services/Process/JsonPidStore.cs
    - AutoQAC/Services/Process/DefaultPidStorePathProvider.cs
    - AutoQAC/Services/Process/ProcessSessionIdProvider.cs
    - AutoQAC.Tests/Services/JsonPidStoreTests.cs
  modified:
    - AutoQAC/Models/TrackedProcess.cs
    - AutoQAC/Infrastructure/ServiceCollectionExtensions.cs
decisions:
  - PID entries remain JSON-backed but are now accessed only through injected store/path/session contracts.
  - Corrupt PID JSON is preserved to timestamped copies and reset only for JSON format failures.
metrics:
  completed: 2026-04-29
  tasks: 2
  commits: [0684c28, dfc0ac5]
---

# Phase 05 Plan 01: PID Store Contracts, Locked JSON Store, DI Registrations Summary

Session-aware, lock-protected JSON PID storage with injectable test seams.

## Completed Tasks

1. Defined `IPidStore`, `IPidStorePathProvider`, and `IProcessSessionIdProvider`; added `TrackedProcess.SessionId`.
2. Implemented `JsonPidStore`, production path/session providers, DI registrations, and focused PID store tests.

## Verification

- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~JsonPidStore"` — passed.
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~JsonPidStore|FullyQualifiedName~ProcessExecutionService"` — passed.

## Deviations from Plan

None - plan executed as written.

## Known Stubs

None.

## Threat Flags

None beyond planned filesystem PID-store trust boundary mitigations.

## Self-Check: PASSED

- Created files exist.
- Commits found: `0684c28`, `dfc0ac5`.
