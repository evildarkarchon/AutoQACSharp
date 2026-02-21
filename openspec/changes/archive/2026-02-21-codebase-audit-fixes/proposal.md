## Why

This codebase was originally translated from Python by an older AI agent. The translation introduced systemic concurrency bugs (Python's GIL masked threading issues that became real in C#), resource leaks (IDisposable not wired up), and thread-safety gaps (mutable shared state without synchronization). A comprehensive audit identified 43 issues -- 6 Critical, 14 High -- including a guaranteed deadlock in ConfigurationService, process handle leaks, and 93% of findings having zero test coverage. These bugs can cause data corruption, hangs, and memory leaks in production use.

## What Changes

- **Fix ConfigurationService concurrency architecture**: Replace single SemaphoreSlim with split file/state locks, implement clone-on-read pattern, remove dead code with guaranteed deadlock, fix TOCTOU races and fire-and-forget saves
- **Fix resource lifecycle management**: Wire IDisposable/IAsyncDisposable on services, dispose ServiceProvider on shutdown, flush Serilog, track Rx subscriptions in CompositeDisposable, fix View subscription leaks
- **Fix process handle management**: Replace thread-unsafe List with ConcurrentQueue for stdout/stderr capture, fix Process escaping using scope, fix GetProcessById handle leak, audit CTS linking
- **Fix thread-safety of shared data models**: Make PluginInfo.IsSelected init-only or separate mutable wrapper, make AppState collections read-only, fix volatile field TOCTOU patterns
- **Fix ConfigWatcherService**: Replace sync-over-async (.GetAwaiter().GetResult()), fix file sharing mode, add error path disposal
- **Fix cross-cutting code quality**: Serilog message templates, ConfigureAwait(false) in services, absolute log paths, dead code removal, missing CancellationToken parameters, input validation at service boundaries
- **Add test coverage for all fixes**: ConfigWatcherService (0 tests), concurrency races, process thread safety, disposal lifecycle -- following StateService test patterns

## Capabilities

### New Capabilities
- `config-concurrency`: Thread-safe configuration loading, saving, caching, and change detection with proper lock splitting, clone-on-read, and atomic state management
- `resource-lifecycle`: Application-wide IDisposable/IAsyncDisposable contracts, ServiceProvider shutdown disposal, Rx subscription tracking, and View lifecycle management
- `process-safety`: Thread-safe process output capture, proper handle lifetime management, and CancellationTokenSource linking for xEdit subprocess execution
- `thread-safe-models`: Immutability contracts for shared data models (PluginInfo, AppState) preventing cross-thread mutation

### Modified Capabilities
- `plugin-loading`: PluginInfo record changes from mutable `set` to `init` -- callers that mutate IsSelected need adaptation

## Impact

- **ConfigurationService.cs** (634 lines): Major rewrite of concurrency model -- split locks, clone-on-read, remove UpdateMultipleAsync
- **IConfigurationService.cs**: Add CancellationToken to 3 methods, remove UpdateMultipleAsync -- **BREAKING** for any external consumers (none exist currently)
- **ConfigWatcherService.cs**: Replace sync-over-async with proper async pipeline
- **ProcessExecutionService.cs**: ConcurrentQueue, restructured Process lifetime, handle disposal
- **App.axaml.cs**: ServiceProvider disposal chain on shutdown
- **PluginInfo.cs / AppState.cs**: Immutability changes -- callers must adapt
- **Multiple Views**: Subscription disposal, double-dispose guards
- **Multiple Services**: ConfigureAwait(false), Serilog templates, input validation
- **Test project**: ~50-80 new tests covering concurrency, disposal, and thread safety
- **No external API changes**: All fixes are internal to the application
- **No new dependencies**: Uses existing System.Collections.Concurrent, Interlocked, etc.
