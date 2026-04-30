---
phase: 08
slug: cleaning-orchestrator-decomposition
status: verified
threats_open: 0
asvs_level: 1
created: 2026-04-30
---

# Phase 08 — Security

> Per-phase security contract: threat register, accepted risks, and audit trail.

---

## Trust Boundaries

| Boundary | Description | Data Crossing |
|----------|-------------|---------------|
| Test ↔ production code | Characterization and source-guard tests verify refactor invariants without changing public contracts. | Public API shape, source text, regression behavior |
| Filesystem/configuration ↔ preflight | AppState/config paths are validated before preflight produces rows that can reach xEdit launch. | xEdit path, load-order path, plugin paths, skip lists |
| Backup coordinator ↔ filesystem | Backup copies, metadata, and retention cleanup flow only through IBackupService. | Plugin files, backup metadata, cleanup status |
| Termination coordinator ↔ live process handle | xEdit process references stay behind the coordinator lock and are exposed only as stop/force-stop state. | Process handle, PID, termination result, hang state |
| Runner/finalizer ↔ xEdit/log files | Runner launches through ICleaningService and captures log offsets; finalizer reads logs via IXEditLogFileService with termination guards. | xEdit process lifecycle, main/exception log slices |
| UI/ViewModel → ICleaningOrchestrator | Public service calls can start, stop, force-stop, dry-run, or cancel backup operations. | Session CTS ownership, active-session state, cancellation requests |

---

## Threat Register

| Threat ID | Category | Component | Disposition | Mitigation | Status | Evidence |
|-----------|----------|-----------|-------------|------------|--------|----------|
| T-08-01 | Tampering | Test file edits during refactor | mitigate | Wave 0 characterization and public-surface guard remain present after refactor. | closed | `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs:2217`, `:2525` |
| T-08-02 | Tampering | Preflight reason mapping divergence between dry-run and real run | mitigate | Shared preflight plus dry-run/start equivalence and idempotence tests. | closed | `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs:2347`; `AutoQAC.Tests/Services/Cleaning/CleaningPreflightTests.cs:61` |
| T-08-03 | Information Disclosure | Validation failure messages exposing full file paths | accept | Accepted D-12 behavior; disclosure bounded by later Phase 11 SEC-01. | closed | Accepted Risks Log `AR-08-03`; existing path-detail behavior retained in `CleaningOrchestrator.cs:341` |
| T-08-14 | Tampering | PreflightSkipReason mapping divergence due to enum guesswork | mitigate | Mapper uses actual `PluginWarningKind` members. | closed | `AutoQAC/Services/Cleaning/CleaningPreflight.cs:201-209` |
| T-08-04 | Denial of Service | CTS leak if ClearBackupOperationCts is skipped on exception | mitigate | Backup/retention operation CTS cleared from `finally`; cancellation tolerates disposed CTS. | closed | `AutoQAC/Services/Cleaning/BackupSessionCoordinator.cs:296-300`, `:352-356`, `:239-246` |
| T-08-05 | Tampering | Backup metadata written for plugins that did not actually back up | mitigate | Only `Succeeded` outcome adds backup entries; ContinueWithoutBackup has no add path. | closed | `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:209-215` |
| T-08-15 | Tampering | AbortSession branch silently drops LogSessionSummary or FinishCleaningWithResults | mitigate | Abort branch writes partial metadata then calls `FinishSession`, which publishes and logs summary, before returning early. | closed | `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:197-207`, `:311-320` |
| T-08-06 | Elevation of Privilege | Self-termination of AutoQAC process | mitigate | Stop and force-stop refuse current process PID. | closed | `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs:129-133`, `:178-182` |
| T-08-07 | Denial of Service | Termination cleanup abandoned by caller cancellation | mitigate | Stop/force-stop termination calls pass `CancellationToken.None`. | closed | `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs:137`, `:186` |
| T-08-08 | Tampering | Hang Subject lifetime mismatch with coordinator | mitigate | Coordinator implements `IDisposable`; hang observable is owned by coordinator and forwarded through facade. | closed | `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs:18`, `:60-61`, `:240-247` |
| T-08-13 | Tampering | Stale UI state leaks across sessions if ResetForNewSession is incomplete | mitigate | Reset clears stop flag, terminating state, last result, hang state, and process reference. | closed | `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs:215-238` |
| T-08-09 | Information Disclosure | Log content read after process killed | mitigate | Finalizer only reads logs when process is not still running and stop was not requested. | closed | `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs:29-33`, `:71-74` |
| T-08-10 | Tampering | Runner skips offset capture between attempts | mitigate | Log offsets captured inside retry loop before every `CleanPluginAsync` call. | closed | `AutoQAC/Services/Cleaning/PluginCleaningRunner.cs:41-65` |
| T-08-11 | Tampering | Future maintainer adds Parallel.ForEach to collaborator | mitigate | Source-level guard scans all six cleaning files for parallel constructs. | closed | `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs:2636-2671` |
| T-08-12 | Tampering | Public surface drift introduced after Phase 8 | mitigate | Public-surface snapshot locks ICleaningOrchestrator members. | closed | `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs:2525-2553` |
| T-08-13 | Tampering | StartCleaningAsync session CTS ordering | mitigate | Session CTS is created before orphan cleanup/preflight; cancellation checked before plugin loop. | closed | `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:58-80`; tests at `CleaningOrchestratorTests.cs:704`, `:765` |
| T-08-14 | Denial of Service | Async regression tests blocking startup awaits | mitigate | Startup-window stop tests release blocked TCS instances in `finally`. | closed | `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs:751-755`, `:806-810` |
| T-08-15 | Elevation of Privilege | Accidental xEdit launch after Stop | mitigate | Cancellation checked before plugin loop; no-cleaning regression tests cover preflight/orphan stop. | closed | `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:78-83`; `CleaningOrchestratorTests.cs:757-762`, `:812-817` |
| T-08-16 | Tampering | PluginResultFinalizer AlreadyClean promotion | mitigate | AlreadyClean promotion requires `result.Success && result.Status == CleaningStatus.Cleaned`. | closed | `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs:48-59` |
| T-08-17 | Repudiation | Final status/success mismatch | mitigate | `Success` is derived from final status after log overrides. | closed | `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs:76-87` |
| T-08-18 | Information Disclosure | Exception log surfacing | accept | Existing exception-log surfacing intentionally preserved as LogParseWarning. | closed | Accepted Risks Log `AR-08-18`; `PluginResultFinalizer.cs:62-69` |
| T-08-09-01 | Denial of Service | CleaningOrchestrator.StartCleaningAsync | mitigate | Concurrent starts fail fast with `Interlocked.CompareExchange`. | closed | `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:45`, `:269-279` |
| T-08-09-02 | Tampering | CleaningOrchestrator session state | mitigate | Regression test proves second start cannot overwrite first session token and Stop cancels original active session. | closed | `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs:820-874` |
| T-08-09-03 | Elevation of Privilege | xEdit process launch | accept | Existing runner/process-service launch boundary unchanged; no new permission path introduced. | closed | Accepted Risks Log `AR-08-09-03`; `PluginCleaningRunner.cs:57-65` |
| T-08-10-01 | Tampering | CleaningPreflight.PrepareAsync | mitigate | Revalidates LoadOrderPath after final game detection. | closed | `AutoQAC/Services/Cleaning/CleaningPreflight.cs:76-80`, `:260-272` |
| T-08-10-02 | Denial of Service | file-load-order games | mitigate | Invalid detected file-load-order state throws before variant/skip-list/plugin row construction. | closed | `AutoQAC/Services/Cleaning/CleaningPreflight.cs:76-83`; tests at `CleaningPreflightTests.cs:157-182` |
| T-08-10-03 | Information Disclosure | path validation error | accept | Generic invalid-configuration message retained without path details. | closed | Accepted Risks Log `AR-08-10-03`; `CleaningPreflight.cs:78-79` |

*Status: open · closed*
*Disposition: mitigate (implementation required) · accept (documented risk) · transfer (third-party)*

---

## Accepted Risks Log

| Risk ID | Threat Ref | Rationale | Accepted By | Date |
|---------|------------|-----------|-------------|------|
| AR-08-03 | T-08-03 | D-12 lock keeps existing validation failure text verbatim; disclosure is bounded by planned Phase 11 SEC-01. | gsd-security-auditor | 2026-04-30 |
| AR-08-18 | T-08-18 | Existing behavior intentionally surfaces xEdit exception-log content as `LogParseWarning`; Phase 8 only fixed status/success consistency. | gsd-security-auditor | 2026-04-30 |
| AR-08-09-03 | T-08-09-03 | Phase 8 introduced no new xEdit launch path or permissions; launch remains delegated to the existing `ICleaningService`/process-service boundary. | gsd-security-auditor | 2026-04-30 |
| AR-08-10-03 | T-08-10-03 | Post-detection load-order validation preserves the generic `Configuration is invalid` message and does not add path details. | gsd-security-auditor | 2026-04-30 |

---

## Unregistered Flags

None. SUMMARY threat flags for 08-03 through 08-10 state no new network endpoints, auth paths, file-access trust boundaries, schema changes, or external service setup. 08-01 and 08-02 had no Threat Flags section and reported test-only / planned refactor execution.

---

## Security Audit Trail

| Audit Date | Threats Total | Closed | Open | Run By |
|------------|---------------|--------|------|--------|
| 2026-04-30 | 27 | 27 | 0 | gsd-security-auditor |

---

## Sign-Off

- [x] All threats have a disposition (mitigate / accept / transfer)
- [x] Accepted risks documented in Accepted Risks Log
- [x] `threats_open: 0` confirmed
- [x] `status: verified` set in frontmatter

**Approval:** verified 2026-04-30
