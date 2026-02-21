## 1. One-Liner Fixes (Cross-Cutting)

- [x] 1.1 Fix Serilog string interpolation: replace interpolated strings with structured message templates across all services
- [x] 1.2 Add ConfigureAwait(false) to all awaits in non-UI service code (ConfigurationService, ConfigWatcherService, ProcessExecutionService)
- [x] 1.3 Fix relative log path in LoggingService: use AppContext.BaseDirectory instead of relative "logs" path
- [x] 1.4 Remove dead else branch in LoggingService (unreachable Log.CloseAndFlush at lines 62-65)
- [x] 1.5 Change MainWindowViewModel registration from AddTransient to AddSingleton in ServiceCollectionExtensions
- [x] 1.6 Fix _pendingConfig TOCTOU in LoadUserConfigAsync: read volatile field into local variable before null check
- [x] 1.7 Fix _mainConfigCache read outside lock: move cache check inside _stateLock
- [x] 1.8 Fix ReferenceEquals + null assign: use Interlocked.CompareExchange for _pendingConfig clear in SaveToDiskWithRetryAsync

## 2. ConfigurationService Concurrency Rewrite

- [x] 2.1 Add Lock _stateLock field alongside existing SemaphoreSlim _fileLock
- [x] 2.2 Implement CloneConfig helper method using YAML serialize/deserialize round-trip
- [x] 2.3 Refactor LoadUserConfigAsync to use _stateLock for cache access and return cloned config
- [x] 2.4 Refactor LoadMainConfigAsync to use _stateLock for cache access
- [x] 2.5 Remove UpdateMultipleAsync from IConfigurationService and ConfigurationService
- [x] 2.6 Replace fire-and-forget save with Observable.FromAsync + Switch in debounce pipeline
- [x] 2.7 Add CancellationToken parameter to GetSkipListAsync, GetDefaultSkipListAsync, GetXEditExecutableNamesAsync
- [x] 2.8 Update all callers of modified interface methods to pass CancellationToken (or use default)
- [x] 2.9 Implement IAsyncDisposable on ConfigurationService with FlushPendingSavesAsync in DisposeAsync

## 3. Resource Lifecycle Management

- [x] 3.1 Add ServiceProvider disposal in App.axaml.cs shutdown handler (cast to IDisposable, call Dispose)
- [x] 3.2 Add Log.CloseAndFlush() call in shutdown handler after ServiceProvider disposal
- [x] 3.3 Fix SkipListWindow: add CompositeDisposable, track bare Subscribe() calls, dispose on close
- [x] 3.4 Fix ProgressWindow DataContextChanged: unsubscribe previous VM subscriptions before subscribing new
- [x] 3.5 Fix ProgressWindow double-dispose: add boolean guard to ensure disposal runs only once
- [x] 3.6 Fix ConfigWatcherService FileSystemWatcher disposal on error paths in StartWatching

## 4. Process Safety

- [x] 4.1 Replace List<string> with ConcurrentQueue<string> for outputLines/errorLines in ExecuteAsync
- [x] 4.2 Convert ConcurrentQueue to List via .ToList() when building ProcessResult (preserve API)
- [x] 4.3 Wrap Process.GetProcessById() in using statement in CleanOrphanedProcessesAsync
- [x] 4.4 Audit all CancellationTokenSource creation in ProcessExecutionService for proper linking and disposal

## 5. Thread-Safe Models

- [x] 5.1 Add XML doc comment to PluginInfo.IsSelected documenting UI-thread-only mutation contract
- [x] 5.2 Change AppState PluginsToClean to IReadOnlyList<PluginInfo>
- [x] 5.3 Change AppState CleanedPlugins/FailedPlugins/SkippedPlugins to IReadOnlySet<string>
- [x] 5.4 Update all AppState construction sites to use immutable collection wrappers

## 6. ConfigWatcherService Fixes

- [x] 6.1 Replace .GetAwaiter().GetResult() in HandleFileChanged with async method + Observable.FromAsync
- [x] 6.2 Replace .GetAwaiter().GetResult() in ApplyDeferredChanges with async method + Observable.FromAsync
- [x] 6.3 Fix File.ReadAllText in TryValidateYaml to use FileShare.ReadWrite for concurrent access

## 7. Test Coverage

- [x] 7.1 Add ConfigurationService clone-on-read tests (verify returned config is independent copy)
- [x] 7.2 Add ConfigurationService concurrent read-write race condition tests
- [x] 7.3 Add ConfigurationService debounce pipeline error handling tests
- [x] 7.4 Add ConfigWatcherService basic tests (FSW events, hash comparison, reload trigger)
- [x] 7.5 Add ConfigWatcherService deferred change tests (changes during cleaning deferred until end)
- [x] 7.6 Add ProcessExecutionService concurrent output capture tests (simulate concurrent events)
- [x] 7.7 Add ProcessExecutionService handle disposal verification tests
- [x] 7.8 Add App shutdown disposal integration test (verify ServiceProvider.Dispose cascade)
- [x] 7.9 Add View subscription lifecycle tests (CompositeDisposable disposal verification)
- [x] 7.10 Verify all existing 515+ tests still pass after all changes
