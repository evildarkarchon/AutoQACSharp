# T06: 10-configuration-persistence-hardening 06

**Slice:** S11 — **Milestone:** M001

## Description

Close two coordinator-level verification gaps: (1) inline Subject<T>.OnNext calls can throw from misbehaving observers before TrySetResult completes request TaskCompletionSources, hanging callers; (2) explicit reload requests bypass the pending-save guard that watcher reloads already respect.

Purpose: Make the single-reader coordinator loop resilient to observer exceptions and prevent explicit reloads from silently overwriting queued app saves.
Output: Hardened coordinator with safe publication helpers and pending-save reload guard, plus deterministic regression tests.

## Must-Haves

- [ ] "Observer exceptions on Subject<T>.OnNext cannot prevent TrySetResult from completing caller TCSs"
- [ ] "Explicit reload flushes or rejects when a pending app save exists instead of overwriting it"
- [ ] "Forced save/flush/reload barriers always return typed results even when downstream observers throw"

## Files

- `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs`
- `AutoQAC.Tests/Services/Configuration/ConfigPersistenceCoordinatorTests.cs`
