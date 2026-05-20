# T04: 10-configuration-persistence-hardening 04

**Slice:** S11 — **Milestone:** M001

## Description

Surface persistence flush failure into the cleaning workflow so a pre-cleaning save failure prevents xEdit launch (SPEC requirement #5, decision D-26, success criterion #2). Today, `CleaningPreflight.PrepareAsync` calls `await configService.FlushPendingSavesAsync(ct)` and proceeds regardless of the outcome; after Plan 03, `FlushPendingSavesAsync` returns a typed `ConfigPersistenceResult`, and Plan 04 inspects it and aborts preflight on `Status=Failed`.

Purpose: closes the SPEC race-policy and acceptance gap "Pre-cleaning flush failure prevents xEdit launch and surfaces the persistence failure path." Wave 3 work; depends only on the public-boundary contracts already wired by Plans 02 and 03.

Output: a small change to `CleaningPreflight.cs` (one new branch + one new typed throw) plus a focused test addition to `CleaningPreflightTests.cs` covering the failure path and the no-regression happy path.

## Must-Haves

- [ ] "When ConfigurationService.FlushPendingSavesAsync returns ConfigPersistenceResult.Status=Failed, CleaningPreflight.PrepareAsync throws a typed exception BEFORE any game detection, plugin validation, MO2 validation, or process launch path is reached (D-26, success criterion #2)."
- [ ] "D-37: Pre-cleaning flush failure coverage uses a mocked configuration boundary to prove the cleaning service/process launch path is not called."
- [ ] "The thrown exception carries the typed ConfigPersistenceFailure payload (or a derived safe summary string) so cleaning callers can map it to user-facing status (D-28, D-30)."
- [ ] "When FlushPendingSavesAsync returns Status=Success or Status=NoOp, preflight proceeds exactly as before (no behavior change for the happy path)."
- [ ] "Plan 04 does not touch CleaningCommandsViewModel or other cleaning consumers; the cleaning workflow's existing exception handling already converts InvalidOperationException to user-facing failure UI; the new exception type is either InvalidOperationException (typed via inner data) or a new ConfigPersistenceFailureException that derives from InvalidOperationException for backwards-compatible catch sites."

## Files

- `AutoQAC/Services/Configuration/ConfigPersistenceStatus.cs`
- `AutoQAC/Services/Cleaning/CleaningPreflight.cs`
- `AutoQAC.Tests/Services/Cleaning/CleaningPreflightTests.cs`
