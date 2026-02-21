## Context

This codebase was translated from Python by an older AI agent. The Python GIL (Global Interpreter Lock) provides implicit thread safety for in-memory state, which meant the original code needed no explicit synchronization. The C# translation added SemaphoreSlim(1,1) for file I/O serialization but left in-memory state unprotected, creating race conditions that did not exist in the Python source.

The existing StateService is the gold standard in this codebase -- it uses Lock + clone-on-read via records + emit-outside-lock pattern. The ConfigurationService predates this pattern and was never updated to match.

Current state:
- 43 audit findings (6 Critical, 14 High, 24 Medium, 14 Low)
- 93% of findings have zero test coverage
- ConfigurationService has 11 of the 43 issues (core hotspot)
- ConfigWatcherService has 0 tests

Constraint: All existing 515+ tests must continue passing throughout.

## Goals / Non-Goals

**Goals:**
- Eliminate all 6 Critical issues (deadlock, race conditions, handle leaks)
- Fix all 14 High issues (resource leaks, thread safety, sync-over-async)
- Fix Medium/Low issues where the fix is straightforward
- Add test coverage for every fix (targeting the 93% gap)
- Maintain backward compatibility -- no public API behavior changes

**Non-Goals:**
- Rewriting the entire codebase or changing the architecture beyond needed
- Adding new features or capabilities
- Changing the YAML config file format
- Migrating to a different concurrency model (e.g., actors, channels)
- UI redesign or UX changes

## Decisions

### D1: ConfigurationService concurrency model -- Split locks + clone-on-read

**Decision**: Replace single _fileLock SemaphoreSlim with two concerns:
1. _fileLock (SemaphoreSlim) -- protects file I/O only (read/write disk)
2. _stateLock (Lock) -- protects in-memory state

All public methods that return UserConfiguration will return a deep clone (serialize then deserialize round-trip via YamlDotNet).

**Alternatives considered:**
- Single ReaderWriterLockSlim: Overkill, writes are infrequent
- Immutable config with Interlocked.Exchange: YAML model classes use mutable set properties making immutability expensive
- Channel-based serialization: Too invasive, requires rewriting all callers

**Rationale**: Matches proven StateService pattern. Lock for in-memory (fast), SemaphoreSlim for file I/O (slow).

### D2: Remove UpdateMultipleAsync entirely

**Decision**: Delete UpdateMultipleAsync from both interface and implementation.

**Rationale**: Zero callers. Guaranteed deadlock -- acquires _fileLock then calls LoadUserConfigAsync which re-acquires.

### D3: Clone-on-read via YAML round-trip

**Decision**: Use serializer/deserializer round-trip for deep cloning UserConfiguration.

**Alternatives considered:**
- System.Text.Json: Does not respect YamlMember attributes
- Manual Clone(): Error-prone as properties are added
- Record with expression: Shallow copy, shared collections

**Rationale**: YAML round-trip already available, guarantees deep copy, consistent with on-disk format.

### D4: Save pipeline -- Observable.FromAsync + Switch

**Decision**: Replace fire-and-forget save with Observable.FromAsync + Switch in Rx pipeline.

**Rationale**: Properly awaits saves, cancels superseded saves, routes errors to handler.

### D5: Process output capture -- ConcurrentQueue

**Decision**: Replace List<string> with ConcurrentQueue<string> for output/error lines.

**Rationale**: OutputDataReceived fires on thread pool I/O threads. List.Add is not thread-safe.

### D6: Process handle leak -- using statement

**Decision**: Wrap Process.GetProcessById() in using in CleanOrphanedProcessesAsync.

### D7: ConfigWatcherService -- async Rx pipeline

**Decision**: Replace .GetAwaiter().GetResult() with Observable.FromAsync.

**Rationale**: Avoids blocking thread pool threads.

### D8: ServiceProvider disposal on shutdown

**Decision**: Dispose ServiceProvider in shutdown handler. Add Log.CloseAndFlush().

### D9: PluginInfo mutability -- keep mutable with docs

**Decision**: Keep IsSelected mutable, document UI-thread-only mutation contract.

**Rationale**: Only mutated by UI checkboxes, read as snapshot at cleaning start.

### D10: View subscription lifecycle

**Decision**: Use WhenActivated + CompositeDisposable for all View subscriptions.

## Risks / Trade-offs

- [Risk] YAML clone may normalize formatting -- Mitigation: clones never written to disk directly
- [Risk] Removing UpdateMultipleAsync breaks interface -- Mitigation: zero callers, internal-only
- [Risk] ConfigWatcher async rewrite changes timing -- Mitigation: Throttle(500ms) preserved
- [Risk] ConcurrentQueue changes output type -- Mitigation: .ToList() in ProcessResult builder
- [Trade-off] YAML clone slower than shallow copy -- Acceptable: configs are small
- [Trade-off] Split locks add complexity -- Acceptable: clear responsibilities, matches StateService
