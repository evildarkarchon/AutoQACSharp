using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.State;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AutoQAC.Services.Configuration;

internal sealed class ConfigPersistenceCoordinator : IConfigPersistenceCoordinator
{
    private readonly IUserConfigFileStore _fileStore;
    private readonly IStateService _stateService;
    private readonly ILoggingService _logger;
    private readonly IDeserializer _yamlValidator;
    private readonly TimeSpan _autoFlushDelay;
    private readonly bool _autoFlushEnabled;
    private readonly Lock _snapshotLock = new();
    private readonly Subject<ConfigPersistenceFailure> _failures = new();
    private readonly Subject<ConfigPersistenceResult> _persistenceResults = new();
    private readonly Subject<UserConfiguration> _configurationAccepted = new();

    private readonly Channel<ConfigPersistenceOperation> _operations =
        Channel.CreateUnbounded<ConfigPersistenceOperation>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    private UserConfiguration _activeConfig = new();
    private UserConfiguration _lastKnownGood = new();
    private UserConfiguration? _pendingApp;
    private UserConfigReadResult? _deferredCandidate;
    private IDisposable? _cleaningSubscription;
    private CancellationTokenSource? _autoFlushCts;
    private CancellationTokenSource? _consumerCts;
    private Task? _consumerTask;
    private ConfigPersistenceFailure? _lastFailure;
    private string? _lastWrittenHash;
    private string? _lastKnownExternalHash;
    private long _appGeneration;
    private long _appGenerationProducer;
    private int _started;
    private int _disposed;

    public ConfigPersistenceCoordinator(
        IUserConfigFileStore fileStore,
        IStateService stateService,
        ILoggingService logger,
        TimeSpan? autoFlushDelay = null)
    {
        _fileStore = fileStore;
        _stateService = stateService;
        _logger = logger;
        _autoFlushDelay = autoFlushDelay ?? TimeSpan.FromMilliseconds(500);
        _autoFlushEnabled = _autoFlushDelay > TimeSpan.Zero;
        _yamlValidator = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public IObservable<ConfigPersistenceFailure> Failures => _failures;

    public IObservable<ConfigPersistenceResult> PersistenceResults => _persistenceResults;

    public IObservable<UserConfiguration> ConfigurationAccepted => _configurationAccepted;

    public ConfigPersistenceFailure? LastFailure => Volatile.Read(ref _lastFailure);

    /// <summary>
    /// Starts the single-reader coordinator loop and the cleaning transition subscription.
    /// </summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return Task.CompletedTask;
        }

        _consumerCts = new CancellationTokenSource();
        _cleaningSubscription = _stateService.StateChanged
            .Select(state => state.IsCleaning)
            .DistinctUntilChanged()
            .Subscribe(
                isCleaning => TryWrite(new CleaningStateChanged(isCleaning)),
                ex => _logger.Error(ex, "[ConfigPersistence] Cleaning state subscription failed"));
        _consumerTask = Task.Run(() => RunAsync(_consumerCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Attempts one final bounded flush, then completes the operation channel and waits for drain.
    /// </summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _autoFlushCts?.Cancel();
        _cleaningSubscription?.Dispose();

        if (_consumerTask == null)
        {
            _operations.Writer.TryComplete();
            DisposeSubjects();
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_operations.Writer.TryWrite(new ShutdownOperation(completion)))
        {
            try
            {
                await completion.Task.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
            {
                _logger.Warning("[ConfigPersistence] Shutdown flush did not complete within the bounded wait");
            }
        }

        _operations.Writer.TryComplete();
        try
        {
            await _consumerTask.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _consumerCts?.Cancel();
            _logger.Warning("[ConfigPersistence] Coordinator consumer did not drain before shutdown timeout");
        }

        DisposeSubjects();
    }

    /// <summary>
    /// Returns a deep snapshot of the currently active configuration without blocking the consumer loop.
    /// </summary>
    public Task<UserConfiguration> LoadCurrentAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        lock (_snapshotLock)
        {
            return Task.FromResult(_activeConfig.Copy());
        }
    }

    /// <summary>
    /// Queues an optimistic app-save intent. Intermediate pending saves coalesce in the consumer.
    /// </summary>
    public async Task SaveUserConfigAsync(UserConfiguration config, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var generation = Interlocked.Increment(ref _appGenerationProducer);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _operations.Writer.WriteAsync(new SaveIntent(config.Copy(), generation, completion), ct).ConfigureAwait(false);
        await completion.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Queues a flush barrier and returns the typed persistence result after the consumer drains it.
    /// </summary>
    public async Task<ConfigPersistenceResult> FlushPendingSavesAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var completion = CreateResultCompletion();
        await _operations.Writer.WriteAsync(new FlushBarrier(completion), ct).ConfigureAwait(false);
        return await completion.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Queues a reload barrier that validates disk YAML before mutating active state.
    /// </summary>
    public async Task<ConfigPersistenceResult> ReloadFromDiskAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var completion = CreateResultCompletion();
        await _operations.Writer.WriteAsync(new ReloadRequest(completion), ct).ConfigureAwait(false);
        return await completion.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Submits a watcher signal; current file content remains the authority when the consumer runs.
    /// </summary>
    public void NotifySettingsFileChanged(ConfigFileSignalKind kind)
    {
        ThrowIfDisposed();
        TryWrite(new WatcherObserved(null, Volatile.Read(ref _appGenerationProducer), kind));
    }

    public string? GetLastWrittenHash() => Volatile.Read(ref _lastWrittenHash);

    private async Task RunAsync(CancellationToken ct)
    {
        await foreach (var op in _operations.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            try
            {
                await ApplyOperationAsync(op, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[ConfigPersistence] Operation handler threw");
            }
        }
    }

    private Task ApplyOperationAsync(ConfigPersistenceOperation operation, CancellationToken ct) => operation switch
    {
        SaveIntent save => ApplySaveAsync(save),
        FlushBarrier flush => ApplyFlushAsync(flush, ct),
        WatcherObserved watcher => ApplyWatcherAsync(watcher, ct),
        CleaningStateChanged cleaning => ApplyCleaningStateAsync(cleaning, ct),
        ReloadRequest reload => ApplyReloadRequestAsync(reload, ct),
        ShutdownOperation shutdown => ApplyShutdownAsync(shutdown, ct),
        _ => Task.CompletedTask
    };

    private Task ApplySaveAsync(SaveIntent intent)
    {
        _appGeneration = Math.Max(_appGeneration, intent.Generation);
        _pendingApp = intent.Config.Copy();
        SetActive(intent.Config);
        SafePublishAccepted(intent.Config.Copy());
        ScheduleAutoFlush();
        intent.Completion.TrySetResult();
        return Task.CompletedTask;
    }

    private async Task ApplyFlushAsync(FlushBarrier op, CancellationToken ct)
    {
        var result = await FlushPendingInsideConsumerAsync(ct).ConfigureAwait(false);
        SafePublishResult(result);
        op.Completion.TrySetResult(result);
    }

    private async Task<ConfigPersistenceResult> FlushPendingInsideConsumerAsync(CancellationToken ct)
    {
        if (_pendingApp == null)
        {
            return new ConfigPersistenceResult(ConfigPersistenceStatusKind.NoOp, ConfigPersistenceOperationKind.Flush, _appGeneration, null);
        }

        try
        {
            var pending = _pendingApp.Copy();
            var hash = await _fileStore.WriteAsync(pending, ct).ConfigureAwait(false);
            _lastKnownGood = pending.Copy();
            SetActive(pending);
            _lastWrittenHash = hash;
            _lastKnownExternalHash = hash;
            _pendingApp = null;
            _lastFailure = null;
            return new ConfigPersistenceResult(ConfigPersistenceStatusKind.Success, ConfigPersistenceOperationKind.Flush, _appGeneration, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[ConfigPersistence] Could not write settings file");
            _pendingApp = null;
            SetActive(_lastKnownGood);
            SafePublishAccepted(_lastKnownGood.Copy());
            var failure = CreateFailure(ConfigPersistenceOperationKind.Flush, ConfigPersistenceFailureKind.WriteFailed, "Could not write settings file (write_failed)");
            PublishFailure(failure);
            return new ConfigPersistenceResult(ConfigPersistenceStatusKind.Failed, ConfigPersistenceOperationKind.Flush, _appGeneration, failure);
        }
    }

    private async Task ApplyWatcherAsync(WatcherObserved op, CancellationToken ct)
    {
        if (op.Kind == ConfigFileSignalKind.Error)
        {
            var failure = CreateFailure(ConfigPersistenceOperationKind.Watcher, ConfigPersistenceFailureKind.ReadFailed, "Could not read settings file (read_failed)");
            PublishFailure(failure);
            SafePublishResult(new ConfigPersistenceResult(ConfigPersistenceStatusKind.Failed, ConfigPersistenceOperationKind.Watcher, _appGeneration, failure));
            return;
        }

        string? currentHash;
        try
        {
            currentHash = op.CurrentHash ?? await _fileStore.ComputeHashAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Hashing reads the live settings file, so file-lock races must surface
            // through the typed watcher failure path instead of dropping the signal.
            _logger.Error(ex, "[ConfigPersistence] Could not hash settings file for watcher reload");
            var failure = CreateFailure(ConfigPersistenceOperationKind.Watcher, ConfigPersistenceFailureKind.ReadFailed, "Could not read settings file (read_failed)");
            PublishFailure(failure);
            SafePublishResult(new ConfigPersistenceResult(ConfigPersistenceStatusKind.Failed, ConfigPersistenceOperationKind.Watcher, _appGeneration, failure));
            return;
        }

        if (_lastWrittenHash != null && string.Equals(currentHash, _lastWrittenHash, StringComparison.OrdinalIgnoreCase))
        {
            _logger.Debug("[ConfigPersistence] Watcher echo matched last app-written hash; skipping reload");
            _lastKnownExternalHash = currentHash;
            return;
        }

        if (_lastKnownExternalHash != null && string.Equals(currentHash, _lastKnownExternalHash, StringComparison.OrdinalIgnoreCase))
        {
            _logger.Debug("[ConfigPersistence] Watcher hash has already been observed; skipping reload");
            return;
        }

        if (_pendingApp != null)
        {
            _logger.Information("[ConfigPersistence] Rejected watcher reload because an app save is pending");
            return;
        }

        if (op.ObservedAtGeneration < _appGeneration)
        {
            _logger.Information("[ConfigPersistence] Rejected stale watcher reload for generation {Generation}", op.ObservedAtGeneration);
            return;
        }

        var read = await ReadForReloadAsync(ConfigPersistenceOperationKind.Watcher, ct).ConfigureAwait(false);
        if (read.Result != null)
        {
            SafePublishResult(read.Result);
            return;
        }

        if (_stateService.CurrentState.IsCleaning)
        {
            _deferredCandidate = read.ReadResult;
            _lastKnownExternalHash = read.ReadResult.Hash ?? currentHash;
            _logger.Warning("[ConfigPersistence] Config changed externally during cleaning; deferring latest candidate");
            return;
        }

        ApplyCandidate(read.ReadResult, ConfigPersistenceOperationKind.Watcher);
    }

    private async Task ApplyCleaningStateAsync(CleaningStateChanged cleaning, CancellationToken ct)
    {
        if (cleaning.IsCleaning || _deferredCandidate == null)
        {
            return;
        }

        var candidate = _deferredCandidate;
        _deferredCandidate = null;
        var result = ValidateAndApply(candidate, ConfigPersistenceOperationKind.DeferredReload);
        SafePublishResult(result);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task ApplyReloadRequestAsync(ReloadRequest reload, CancellationToken ct)
    {
        if (_pendingApp != null)
        {
            // Explicit reloads must not silently overwrite a queued app save; flush first
            // so the reload reads the latest user edits back from disk.
            _logger.Information("[ConfigPersistence] Flushing pending app save before explicit reload");
            var flushResult = await FlushPendingInsideConsumerAsync(ct).ConfigureAwait(false);
            SafePublishResult(flushResult);
            // Explicit reload depends on the pending app save reaching disk; otherwise
            // reading older disk content would mask the failure and drop queued edits.
            if (flushResult.Status is ConfigPersistenceStatusKind.Failed or ConfigPersistenceStatusKind.Rejected)
            {
                reload.Completion.TrySetResult(flushResult);
                return;
            }
        }

        var read = await ReadForReloadAsync(ConfigPersistenceOperationKind.Reload, ct).ConfigureAwait(false);
        var result = read.Result ?? ValidateAndApply(read.ReadResult, ConfigPersistenceOperationKind.Reload);
        SafePublishResult(result);
        reload.Completion.TrySetResult(result);
    }

    private async Task ApplyShutdownAsync(ShutdownOperation shutdown, CancellationToken ct)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            var result = await FlushPendingInsideConsumerAsync(linked.Token).ConfigureAwait(false);
            if (result.Status == ConfigPersistenceStatusKind.Failed)
            {
                _logger.Warning("[ConfigPersistence] Final shutdown flush failed: {SafeSummary}", result.Failure?.SafeSummary ?? "unknown");
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            _logger.Warning("[ConfigPersistence] Final shutdown flush timed out or was canceled");
        }
        finally
        {
            shutdown.Completion.TrySetResult();
        }
    }

    private async Task<(UserConfigReadResult ReadResult, ConfigPersistenceResult? Result)> ReadForReloadAsync(
        ConfigPersistenceOperationKind operation,
        CancellationToken ct)
    {
        try
        {
            var read = await _fileStore.ReadAsync(ct).ConfigureAwait(false);
            if (!read.Exists)
            {
                var failure = CreateFailure(operation, ConfigPersistenceFailureKind.MissingFile, "Settings file is missing (missing_file)");
                PublishFailure(failure);
                return (read, new ConfigPersistenceResult(ConfigPersistenceStatusKind.Failed, operation, _appGeneration, failure));
            }

            return (read, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error(ex, "[ConfigPersistence] Could not read settings file");
            var failure = CreateFailure(operation, ConfigPersistenceFailureKind.ReadFailed, "Could not read settings file (read_failed)");
            PublishFailure(failure);
            return (new UserConfigReadResult(false, null, null), new ConfigPersistenceResult(ConfigPersistenceStatusKind.Failed, operation, _appGeneration, failure));
        }
    }

    private void ApplyCandidate(UserConfigReadResult readResult, ConfigPersistenceOperationKind operation)
    {
        var result = ValidateAndApply(readResult, operation);
        SafePublishResult(result);
    }

    private ConfigPersistenceResult ValidateAndApply(UserConfigReadResult readResult, ConfigPersistenceOperationKind operation)
    {
        if (!readResult.Exists || string.IsNullOrWhiteSpace(readResult.Content))
        {
            var missing = CreateFailure(operation, ConfigPersistenceFailureKind.MissingFile, "Settings file is missing (missing_file)");
            PublishFailure(missing);
            return new ConfigPersistenceResult(ConfigPersistenceStatusKind.Failed, operation, _appGeneration, missing);
        }

        try
        {
            var candidate = _yamlValidator.Deserialize<UserConfiguration>(readResult.Content);
            if (candidate == null)
            {
                throw new InvalidOperationException("YAML deserialized to null");
            }

            var copy = candidate.Copy();
            SetActive(copy);
            _lastKnownGood = copy.Copy();
            _lastKnownExternalHash = readResult.Hash;
            _lastFailure = null;
            SafePublishAccepted(copy.Copy());
            return new ConfigPersistenceResult(ConfigPersistenceStatusKind.Success, operation, _appGeneration, null);
        }
        catch (Exception ex)
        {
            _logger.Warning("[ConfigPersistence] Invalid external config edit rejected: {Message}", ex.Message);
            var failure = CreateFailure(operation, ConfigPersistenceFailureKind.InvalidExternalYaml, "Invalid settings YAML was rejected (invalid_external_yaml)");
            PublishFailure(failure);
            return new ConfigPersistenceResult(ConfigPersistenceStatusKind.Failed, operation, _appGeneration, failure);
        }
    }

    private void SetActive(UserConfiguration config)
    {
        lock (_snapshotLock)
        {
            _activeConfig = config.Copy();
        }
    }

    private void PublishFailure(ConfigPersistenceFailure failure)
    {
        SafePublishFailure(failure);
    }

    /// <summary>
    /// Publishes to the configuration-accepted subject without letting observer exceptions escape into the single-reader operation loop.
    /// </summary>
    /// <param name="config">The accepted configuration snapshot to publish to observers.</param>
    private void SafePublishAccepted(UserConfiguration config)
    {
        try
        {
            _configurationAccepted.OnNext(config); // SafePublishAccepted owns the subject boundary.
        }
        catch (Exception ex)
        {
            _logger.Warning("[ConfigPersistence] Observer exception on ConfigurationAccepted: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Publishes to the persistence-results subject without letting observer exceptions escape into the single-reader operation loop.
    /// </summary>
    /// <param name="result">The typed persistence result to publish to observers.</param>
    private void SafePublishResult(ConfigPersistenceResult result)
    {
        try
        {
            _persistenceResults.OnNext(result); // SafePublishResult owns the subject boundary.
        }
        catch (Exception ex)
        {
            _logger.Warning("[ConfigPersistence] Observer exception on PersistenceResults: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Records and publishes a failure without letting observer exceptions escape into the single-reader operation loop.
    /// </summary>
    /// <param name="failure">The safe typed persistence failure to record and publish.</param>
    private void SafePublishFailure(ConfigPersistenceFailure failure)
    {
        _lastFailure = failure;
        try
        {
            _failures.OnNext(failure); // SafePublishFailure owns the subject boundary.
        }
        catch (Exception ex)
        {
            _logger.Warning("[ConfigPersistence] Observer exception on Failures: {Message}", ex.Message);
        }
    }

    private ConfigPersistenceFailure CreateFailure(
        ConfigPersistenceOperationKind operation,
        ConfigPersistenceFailureKind kind,
        string safeSummary) => new(operation, kind, safeSummary, null, _appGeneration);

    private static TaskCompletionSource<ConfigPersistenceResult> CreateResultCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void ScheduleAutoFlush()
    {
        if (!_autoFlushEnabled)
        {
            return;
        }

        _autoFlushCts?.Cancel();
        _autoFlushCts?.Dispose();
        _autoFlushCts = new CancellationTokenSource();
        var token = _autoFlushCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_autoFlushDelay, token).ConfigureAwait(false);
                var completion = CreateResultCompletion();
                TryWrite(new FlushBarrier(completion));
            }
            catch (OperationCanceledException)
            {
                // The timer is canceled whenever a newer save intent supersedes the pending auto-flush.
            }
        }, CancellationToken.None);
    }

    private void TryWrite(ConfigPersistenceOperation operation)
    {
        if (!_operations.Writer.TryWrite(operation) && Volatile.Read(ref _disposed) == 0)
        {
            throw new ObjectDisposedException(nameof(ConfigPersistenceCoordinator));
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(ConfigPersistenceCoordinator));
        }
    }

    private void DisposeSubjects()
    {
        _autoFlushCts?.Dispose();
        _consumerCts?.Dispose();
        _failures.Dispose();
        _persistenceResults.Dispose();
        _configurationAccepted.Dispose();
    }
}
