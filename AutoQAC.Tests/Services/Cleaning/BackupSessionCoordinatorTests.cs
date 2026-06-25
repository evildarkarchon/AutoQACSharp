using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Backup;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.State;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services.Cleaning;

public sealed class BackupSessionCoordinatorTests : IDisposable
{
    private readonly string _testRoot;
    private readonly IBackupService _backupMock;
    private readonly IStateService _stateMock;
    private readonly IBackupSessionCoordinator _sut;

    public BackupSessionCoordinatorTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"autoqac_bsc_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
        _backupMock = Substitute.For<IBackupService>();
        _stateMock = Substitute.For<IStateService>();
        _sut = new BackupSessionCoordinator(_backupMock, _stateMock, Substitute.For<ILoggingService>());
    }

    public void Dispose()
    {
        if (!Directory.Exists(_testRoot)) return;
        try { Directory.Delete(_testRoot, recursive: true); }
        catch { /* Best-effort cleanup */ }
    }

    [Fact]
    public async Task BeginSessionAsync_NonMo2Mode_BackupEnabled_CreatesSessionDirectory_ReturnsPath()
    {
        // Arrange
        var plan = CreatePlan(backupEnabled: true, isMo2Mode: false);
        var backupRoot = Path.Combine(_testRoot, "AutoQAC Backups");
        var sessionDir = Path.Combine(backupRoot, "session");
        _backupMock.GetBackupRoot(Arg.Any<string>()).Returns(backupRoot);
        _backupMock.CreateSessionDirectory(backupRoot).Returns(sessionDir);

        // Act
        var result = await _sut.BeginSessionAsync(plan, CancellationToken.None);

        // Assert
        result.Should().Be(sessionDir);
        _backupMock.Received(1).GetBackupRoot(Path.Combine(_testRoot, "Data"));
        _backupMock.Received(1).CreateSessionDirectory(backupRoot);
    }

    [Fact]
    public async Task BeginSessionAsync_Mo2Mode_ReturnsNull_AndDoesNotCreateDirectory()
    {
        // Arrange
        var plan = CreatePlan(backupEnabled: true, isMo2Mode: true);

        // Act
        var result = await _sut.BeginSessionAsync(plan, CancellationToken.None);

        // Assert
        result.Should().BeNull();
        _backupMock.DidNotReceive().CreateSessionDirectory(Arg.Any<string>());
    }

    [Fact]
    public async Task RunPluginBackupAsync_OnCancellation_ReturnsCanceledOutcomeWithSkippedResult()
    {
        // Arrange
        var plugin = CreatePlugin("Canceled.esp");
        _backupMock.BackupPluginAsync(plugin, Arg.Any<string>(), Arg.Any<IProgress<BackupCopyProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new BackupCreateResult(BackupOperationStatus.Canceled, plugin.FileName, 10, 100, BackupFailureReason.Canceled));

        // Act
        var outcome = await _sut.RunPluginBackupAsync(plugin, _testRoot, onBackupFailure: null, CancellationToken.None);

        // Assert
        outcome.Kind.Should().Be(PluginBackupOutcomeKind.Canceled);
        outcome.SkippedResult.Should().NotBeNull();
        outcome.SkippedResult!.Status.Should().Be(CleaningStatus.Skipped);
        outcome.SkippedResult.Message.Should().Be("Backup canceled");
    }

    [Fact]
    public async Task RunPluginBackupAsync_OnFailureWithSkipPluginChoice_ReturnsUserSkippedOutcome()
    {
        // Arrange
        var plugin = CreatePlugin("Skip.esp");
        _backupMock.BackupPluginAsync(plugin, Arg.Any<string>(), Arg.Any<IProgress<BackupCopyProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new BackupCreateResult(BackupOperationStatus.Failed, plugin.FileName, 0, null, BackupFailureReason.TargetWriteFailed));
        Task<BackupFailureChoice> Callback(string s, string s1) => Task.FromResult(BackupFailureChoice.SkipPlugin);

        // Act
        var outcome = await _sut.RunPluginBackupAsync(plugin, _testRoot, Callback, CancellationToken.None);

        // Assert
        outcome.Kind.Should().Be(PluginBackupOutcomeKind.UserSkipped);
        outcome.SkippedResult.Should().NotBeNull();
        outcome.SkippedResult!.Status.Should().Be(CleaningStatus.Skipped);
        outcome.SkippedResult.Message.Should().Be("Backup failed - skipped by user");
    }

    [Fact]
    public async Task RunPluginBackupAsync_OnFailureWithAbortSessionChoice_ReturnsAbortSessionOutcome()
    {
        // Arrange
        var plugin = CreatePlugin("Abort.esp");
        _backupMock.BackupPluginAsync(plugin, Arg.Any<string>(), Arg.Any<IProgress<BackupCopyProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new BackupCreateResult(BackupOperationStatus.Failed, plugin.FileName, 0, null, BackupFailureReason.AccessDenied));
        Task<BackupFailureChoice> Callback(string s, string s1) => Task.FromResult(BackupFailureChoice.AbortSession);

        // Act
        var outcome = await _sut.RunPluginBackupAsync(plugin, _testRoot, Callback, CancellationToken.None);

        // Assert
        outcome.Kind.Should().Be(PluginBackupOutcomeKind.AbortSession);
        outcome.FailureReasonText.Should().Be("Access denied");
    }

    [Fact]
    public async Task RunPluginBackupAsync_OnFailureWithContinueWithoutBackupChoice_ReturnsContinueOutcome_AndNoEntry()
    {
        // Arrange
        var plugin = CreatePlugin("Continue.esp");
        _backupMock.BackupPluginAsync(plugin, Arg.Any<string>(), Arg.Any<IProgress<BackupCopyProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new BackupCreateResult(BackupOperationStatus.Failed, plugin.FileName, 0, null, BackupFailureReason.AccessDenied));
        Task<BackupFailureChoice> Callback(string s, string s1) => Task.FromResult(BackupFailureChoice.ContinueWithoutBackup);

        // Act
        var outcome = await _sut.RunPluginBackupAsync(plugin, _testRoot, Callback, CancellationToken.None);

        // Assert
        outcome.Kind.Should().Be(PluginBackupOutcomeKind.ContinueWithoutBackup);
        outcome.Entry.Should().BeNull();
    }

    [Fact]
    public async Task FinalizeSessionAsync_RetentionWarning_ReturnsRetentionResult_WithoutThrowing()
    {
        // Arrange
        var entries = new[] { CreateEntry("Warn.esp") };
        var retentionResult = new BackupRetentionCleanupResult(BackupOperationStatus.Warning, []);
        _backupMock.CleanupOldSessionsAsync(Arg.Any<string>(), 3, _testRoot, Arg.Any<IProgress<BackupCopyProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(retentionResult);

        // Act
        var result = await _sut.FinalizeSessionAsync(_testRoot, GameType.SkyrimSe, entries, 3, CancellationToken.None);

        // Assert
        result.Should().Be(retentionResult);
        await _backupMock.Received(1).WriteSessionMetadataAsync(
            _testRoot,
            Arg.Is<BackupSession>(session => session.Plugins.Count == 1 && session.GameType == nameof(GameType.SkyrimSe)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunPluginBackupAsync_SetsAndClearsBackupOperationState_OnSuccess()
    {
        // Arrange
        var plugin = CreatePlugin("State.esp");
        _backupMock.BackupPluginAsync(plugin, Arg.Any<string>(), Arg.Any<IProgress<BackupCopyProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new BackupCreateResult(BackupOperationStatus.Complete, plugin.FileName, 100, 100, null));

        // Act
        var outcome = await _sut.RunPluginBackupAsync(plugin, _testRoot, onBackupFailure: null, CancellationToken.None);

        // Assert
        outcome.Kind.Should().Be(PluginBackupOutcomeKind.Succeeded);
        _stateMock.Received(1).SetBackupOperation(Arg.Is<BackupOperationState>(state =>
            state.Kind == BackupOperationKind.Backup &&
            state.Label == $"Backing up: {plugin.FileName}" &&
            state.IsActive &&
            state.CanCancel));
        _stateMock.Received(1).ClearBackupOperation();
    }

    private CleaningPreflightPlan CreatePlan(bool backupEnabled, bool isMo2Mode) => new()
    {
        DetectedGameType = GameType.SkyrimSe,
        DetectedGameVariant = GameVariant.None,
        PluginRows = [new PreflightPluginRow(CreatePlugin("Plugin.esp"), PreflightDecision.Clean, SkipReason: null)],
        IsMo2ModeActive = isMo2Mode,
        BackupSkippedByPolicy = isMo2Mode,
        FileValidationSkippedByPolicy = false,
        LaunchModeLabel = "Direct",
        CleaningTimeoutSeconds = 300,
        BackupEnabled = backupEnabled,
        BackupMaxSessions = 3,
        XEditDirectory = _testRoot
    };

    private PluginInfo CreatePlugin(string fileName) => new()
    {
        FileName = fileName,
        FullPath = Path.Combine(_testRoot, "Data", fileName)
    };

    private static BackupPluginEntry CreateEntry(string fileName) => new()
    {
        FileName = fileName,
        OriginalPath = $@"C:\Games\Data\{fileName}",
        FileSizeBytes = 42
    };
}
