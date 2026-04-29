using System.Collections.Concurrent;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Backup;
using AutoQAC.Services.UI;
using AutoQAC.Tests.TestInfrastructure;
using AutoQAC.ViewModels;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="RestoreViewModel"/> covering restore confirmations and inline structured restore outcomes.
/// </summary>
public sealed class RestoreViewModelTests
{
    private readonly IBackupService _backupService = Substitute.For<IBackupService>();
    private readonly IMessageDialogService _messageDialog = Substitute.For<IMessageDialogService>();
    private readonly ILoggingService _logger = Substitute.For<ILoggingService>();
    private readonly IUiDispatcher _uiDispatcher = new SynchronousUiDispatcher();

    private RestoreViewModel CreateViewModel() => new(_backupService, _messageDialog, _logger, _uiDispatcher);

    private RestoreViewModel CreateViewModel(IUiDispatcher uiDispatcher) => new(_backupService, _messageDialog, _logger, uiDispatcher);

    private static BackupSession CreateSession(params BackupPluginEntry[] plugins) => new()
    {
        Timestamp = new DateTime(2026, 4, 29, 7, 30, 0),
        GameType = "Skyrim Special Edition",
        SessionDirectory = @"C:\Backups\2026-04-29_07-30-00",
        Plugins = plugins.ToList()
    };

    private static BackupPluginEntry CreatePlugin(string fileName = "Update.esm", long fileSizeBytes = 1024) => new()
    {
        FileName = fileName,
        OriginalPath = $@"C:\Games\Skyrim Special Edition\Data\{fileName}",
        FileSizeBytes = fileSizeBytes
    };

    [Fact]
    public async Task RestorePluginCommand_ShouldShowRestoreSelectedConfirmationCopy()
    {
        var plugin = CreatePlugin("Dawnguard.esm");
        var session = CreateSession(plugin);
        _messageDialog.ShowConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _backupService.RestorePluginAsync(
                plugin,
                session.SessionDirectory,
                Arg.Any<string?>(),
                Arg.Any<IProgress<BackupCopyProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BackupRestoreResult(
                BackupOperationStatus.Complete,
                [new BackupRestoreRowResult(plugin.FileName, BackupRestoreRowStatus.Restored, null, 1024, 1024)]));

        var vm = CreateViewModel();
        vm.SelectedSession = session;
        vm.SelectedPlugin = plugin;

        await vm.RestorePluginCommand.ExecuteAsync(null);

        await _messageDialog.Received(1).ShowConfirmAsync(
            "Restore Selected",
            "Restore Selected: Restore Dawnguard.esm from Apr 29, 2026 7:30 AM? This overwrites the current plugin file with the backup copy.");
    }

    [Fact]
    public async Task RestoreAllCommand_ShouldShowRestoreAllConfirmationCopy()
    {
        var session = CreateSession(CreatePlugin("Update.esm"), CreatePlugin("Dawnguard.esm"));
        _messageDialog.ShowConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var vm = CreateViewModel();
        vm.SelectedSession = session;

        await vm.RestoreAllCommand.ExecuteAsync(null);

        await _messageDialog.Received(1).ShowConfirmAsync(
            "Restore All",
            "Restore All: Restore 2 plugin(s) from Apr 29, 2026 7:30 AM? Current plugin files will be overwritten by backup copies. AutoQAC will continue past individual failures and show a result list.");
    }

    [Theory]
    [InlineData(BackupOperationStatus.Complete, "Restore Complete")]
    [InlineData(BackupOperationStatus.Partial, "Restore Partial")]
    [InlineData(BackupOperationStatus.Failed, "Restore Failed")]
    [InlineData(BackupOperationStatus.Canceled, "Restore Canceled")]
    public async Task RestoreAllCommand_ShouldMapStructuredResultStatusToExactTitle(BackupOperationStatus status, string expectedTitle)
    {
        var session = CreateSession(CreatePlugin("Update.esm"));
        var rows = status switch
        {
            BackupOperationStatus.Complete => new[] { new BackupRestoreRowResult("Update.esm", BackupRestoreRowStatus.Restored, null, 1024, 1024) },
            BackupOperationStatus.Canceled => new[] { new BackupRestoreRowResult("Update.esm", BackupRestoreRowStatus.Canceled, BackupFailureReason.Canceled, 128, 1024) },
            _ => new[] { new BackupRestoreRowResult("Update.esm", BackupRestoreRowStatus.Failed, BackupFailureReason.MissingBackupFile, 0, 1024) }
        };
        _messageDialog.ShowConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _backupService.RestoreSessionAsync(
                session,
                Arg.Any<string?>(),
                Arg.Any<IProgress<BackupCopyProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BackupRestoreResult(status, rows));

        var vm = CreateViewModel();
        vm.SelectedSession = session;

        await vm.RestoreAllCommand.ExecuteAsync(null);

        vm.RestoreOutcomeTitle.Should().Be(expectedTitle);
        vm.IsRestoreResultVisible.Should().BeTrue();
    }

    [Fact]
    public async Task RestoreAll_PartialResult_KeepsInlineRowsVisible()
    {
        var session = CreateSession(CreatePlugin("Update.esm"), CreatePlugin("Missing.esp"));
        _messageDialog.ShowConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _backupService.RestoreSessionAsync(
                session,
                Arg.Any<string?>(),
                Arg.Any<IProgress<BackupCopyProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BackupRestoreResult(
                BackupOperationStatus.Partial,
                [
                    new BackupRestoreRowResult("Update.esm", BackupRestoreRowStatus.Restored, null, 2048, 2048),
                    new BackupRestoreRowResult("Missing.esp", BackupRestoreRowStatus.Failed, BackupFailureReason.MissingBackupFile, 0, 4096)
                ]));

        var vm = CreateViewModel();
        vm.SelectedSession = session;

        await vm.RestoreAllCommand.ExecuteAsync(null);

        vm.RestoreOutcomeTitle.Should().Be("Restore Partial");
        vm.IsRestoreResultVisible.Should().BeTrue();
        vm.RestoreResults.Should().HaveCount(2);
        vm.RestoreResults[1].DisplayReason.Should().Be("Missing backup file");
        await _messageDialog.DidNotReceive().ShowErrorAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task RestoreAllCommand_FailedThenCanceledResult_ShowsCanceledSummaryWithFailedCount()
    {
        var session = CreateSession(CreatePlugin("Failed.esp"), CreatePlugin("Canceled.esp"));
        _messageDialog.ShowConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _backupService.RestoreSessionAsync(
                session,
                Arg.Any<string?>(),
                Arg.Any<IProgress<BackupCopyProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BackupRestoreResult(
                BackupOperationStatus.Canceled,
                [
                    new BackupRestoreRowResult("Failed.esp", BackupRestoreRowStatus.Failed, BackupFailureReason.TargetWriteFailed, 0, 1024),
                    new BackupRestoreRowResult("Canceled.esp", BackupRestoreRowStatus.Canceled, BackupFailureReason.Canceled, 0, 1024)
                ]));

        var vm = CreateViewModel();
        vm.SelectedSession = session;

        await vm.RestoreAllCommand.ExecuteAsync(null);

        vm.RestoreOutcomeTitle.Should().Be("Restore Canceled");
        vm.RestoreSummaryText.Should().Contain("0 restored, 1 failed, 1 canceled");
    }

    [Fact]
    public async Task RestorePluginCommand_MissingBackupFile_ShouldShowStructuredInlineRowInsteadOfSyncException()
    {
        var plugin = CreatePlugin("Missing.esp");
        var session = CreateSession(plugin);
        _messageDialog.ShowConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _backupService.RestorePluginAsync(
                plugin,
                session.SessionDirectory,
                Arg.Any<string?>(),
                Arg.Any<IProgress<BackupCopyProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BackupRestoreResult(
                BackupOperationStatus.Failed,
                [new BackupRestoreRowResult("Missing.esp", BackupRestoreRowStatus.Failed, BackupFailureReason.MissingBackupFile, 0, 1024)]));

        var vm = CreateViewModel();
        vm.SelectedSession = session;
        vm.SelectedPlugin = plugin;

        await vm.RestorePluginCommand.ExecuteAsync(null);

        vm.RestoreOutcomeTitle.Should().Be("Restore Failed");
        vm.RestoreResults.Should().ContainSingle(row =>
            row.FileName == "Missing.esp" &&
            row.Status == BackupRestoreRowStatus.Failed &&
            row.DisplayReason == "Missing backup file");
        await _messageDialog.DidNotReceive().ShowErrorAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task RestorePluginCommand_Success_ShouldNotShowErrorOrSuccessPopup()
    {
        var plugin = CreatePlugin("Update.esm");
        var session = CreateSession(plugin);
        _messageDialog.ShowConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _backupService.RestorePluginAsync(
                plugin,
                session.SessionDirectory,
                Arg.Any<string?>(),
                Arg.Any<IProgress<BackupCopyProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BackupRestoreResult(
                BackupOperationStatus.Complete,
                [new BackupRestoreRowResult("Update.esm", BackupRestoreRowStatus.Restored, null, 1024, 1024)]));

        var vm = CreateViewModel();
        vm.SelectedSession = session;
        vm.SelectedPlugin = plugin;

        await vm.RestorePluginCommand.ExecuteAsync(null);

        vm.RestoreOutcomeTitle.Should().Be("Restore Complete");
        await _messageDialog.DidNotReceive().ShowErrorAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());
        await _messageDialog.DidNotReceive().ShowInfoAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task CancelRestoreCommand_ShouldCancelActiveRestoreToken()
    {
        var session = CreateSession(CreatePlugin("Large.esp", 120_000_000));
        var restoreStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRestoreToComplete = new TaskCompletionSource<BackupRestoreResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _messageDialog.ShowConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _backupService.RestoreSessionAsync(
                session,
                Arg.Any<string?>(),
                Arg.Any<IProgress<BackupCopyProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                restoreStarted.SetResult(callInfo.ArgAt<CancellationToken>(3));
                return allowRestoreToComplete.Task;
            });

        var vm = CreateViewModel();
        vm.SelectedSession = session;
        var restoreTask = vm.RestoreAllCommand.ExecuteAsync(null);
        var token = await restoreStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        vm.CancelRestoreCommand.Execute(null);

        token.IsCancellationRequested.Should().BeTrue("Cancel Restore should cancel the active restore CTS");
        vm.StatusText.Should().Contain("Cancel restore", "the UI should communicate cancel semantics");
        allowRestoreToComplete.SetResult(new BackupRestoreResult(
            BackupOperationStatus.Canceled,
            [new BackupRestoreRowResult("Large.esp", BackupRestoreRowStatus.Canceled, BackupFailureReason.Canceled, 0, 120_000_000)]));
        await restoreTask;
    }

    [Fact]
    public async Task CancelRestore_DisablesCommands()
    {
        var plugin = CreatePlugin("Update.esm");
        var session = CreateSession(plugin);
        var restoreStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRestoreToComplete = new TaskCompletionSource<BackupRestoreResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _messageDialog.ShowConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _backupService.RestoreSessionAsync(
                session,
                Arg.Any<string?>(),
                Arg.Any<IProgress<BackupCopyProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                restoreStarted.SetResult();
                return allowRestoreToComplete.Task;
            });

        var vm = CreateViewModel();
        vm.SelectedSession = session;
        vm.SelectedPlugin = plugin;
        var restoreTask = vm.RestoreAllCommand.ExecuteAsync(null);
        await restoreStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        vm.RestoreAllCommand.CanExecute(null).Should().BeFalse();
        vm.RestorePluginCommand.CanExecute(null).Should().BeFalse();
        vm.DeleteSessionCommand.CanExecute(null).Should().BeFalse();

        allowRestoreToComplete.SetResult(new BackupRestoreResult(BackupOperationStatus.Complete, []));
        await restoreTask;
    }

    [Fact]
    public async Task LoadSessionsDisabledWhileRestoreActive_ShouldPreventRefreshRace()
    {
        var session = CreateSession(CreatePlugin("Update.esm"));
        var restoreStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRestoreToComplete = new TaskCompletionSource<BackupRestoreResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _messageDialog.ShowConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _backupService.RestoreSessionAsync(
                session,
                Arg.Any<string?>(),
                Arg.Any<IProgress<BackupCopyProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                restoreStarted.SetResult();
                return allowRestoreToComplete.Task;
            });

        var vm = CreateViewModel();
        vm.SelectedSession = session;
        var restoreTask = vm.RestoreAllCommand.ExecuteAsync(null);
        await restoreStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        vm.LoadSessionsCommand.CanExecute(null).Should().BeFalse();

        allowRestoreToComplete.SetResult(new BackupRestoreResult(BackupOperationStatus.Complete, []));
        await restoreTask;
    }

    [Fact]
    public async Task FormatBytes_ShouldUseDecimalMegabytesWithOneFractionalDigitInProgressText()
    {
        var session = CreateSession(CreatePlugin("Large.esp", 120_000_000));
        _messageDialog.ShowConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _backupService.RestoreSessionAsync(
                session,
                Arg.Any<string?>(),
                Arg.Any<IProgress<BackupCopyProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var progress = callInfo.ArgAt<IProgress<BackupCopyProgress>?>(2);
                progress?.Report(new BackupCopyProgress("Large.esp", 38_400_000, 120_000_000));
                return Task.FromResult(new BackupRestoreResult(
                    BackupOperationStatus.Complete,
                    [new BackupRestoreRowResult("Large.esp", BackupRestoreRowStatus.Restored,  null, 120_000_000, 120_000_000)]));
            });

        var vm = CreateViewModel();
        vm.SelectedSession = session;

        await vm.RestoreAllCommand.ExecuteAsync(null);

        vm.RestoreProgressText.Should().Be("Restoring 1 / 1 plugins — 38.4 MB / 120.0 MB");
    }

    [Fact]
    public async Task RestoreProgressReportedFromWorkerThread_ShouldPostBindableUpdatesToDispatcher()
    {
        var session = CreateSession(CreatePlugin("Large.esp", 120_000_000));
        var dispatcher = new DeferredUiDispatcher();
        _messageDialog.ShowConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _backupService.RestoreSessionAsync(
                session,
                Arg.Any<string?>(),
                Arg.Any<IProgress<BackupCopyProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var progress = callInfo.ArgAt<IProgress<BackupCopyProgress>?>(2);
                await Task.Run(() => progress?.Report(new BackupCopyProgress("Large.esp", 38_400_000, 120_000_000)));
                await dispatcher.Posted.Task.WaitAsync(TimeSpan.FromSeconds(2));
                return new BackupRestoreResult(
                    BackupOperationStatus.Complete,
                    [new BackupRestoreRowResult("Large.esp", BackupRestoreRowStatus.Restored, null, 120_000_000, 120_000_000)]);
            });

        var vm = CreateViewModel(dispatcher);
        vm.SelectedSession = session;

        await vm.RestoreAllCommand.ExecuteAsync(null);
        vm.RestoreProgressText.Should().Be("Restoring 0 / 1 plugins", "worker-thread progress must not mutate UI-bound state before dispatcher execution");

        dispatcher.Drain();

        vm.RestoreProgressText.Should().Be("Restoring 1 / 1 plugins — 38.4 MB / 120.0 MB");
    }

    [Fact]
    public async Task DisposeClearsRestoreCancellationSource_ShouldCancelActiveRestoreToken()
    {
        var session = CreateSession(CreatePlugin("Large.esp", 120_000_000));
        var restoreStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRestoreToComplete = new TaskCompletionSource<BackupRestoreResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _messageDialog.ShowConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _backupService.RestoreSessionAsync(
                session,
                Arg.Any<string?>(),
                Arg.Any<IProgress<BackupCopyProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                restoreStarted.SetResult(callInfo.ArgAt<CancellationToken>(3));
                return allowRestoreToComplete.Task;
            });

        var vm = CreateViewModel();
        vm.SelectedSession = session;
        var restoreTask = vm.RestoreAllCommand.ExecuteAsync(null);
        var token = await restoreStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        vm.Dispose();

        token.IsCancellationRequested.Should().BeTrue("disposing the ViewModel should clear the active restore cancellation source");
        allowRestoreToComplete.SetResult(new BackupRestoreResult(BackupOperationStatus.Canceled, []));
        await restoreTask;
    }

    /// <summary>
    /// Verifies the configured game Data folder is retained as the trusted restore root for selected restores.
    /// </summary>
    [Fact]
    public async Task RestorePluginCommand_PassesLoadedDataFolderAsTrustedRestoreRoot()
    {
        var dataFolderPath = @"C:\Games\Skyrim Special Edition\Data";
        var backupRoot = @"C:\Games\Skyrim Special Edition\AutoQAC Backups";
        var plugin = CreatePlugin("Update.esm");
        var session = CreateSession(plugin);
        _backupService.GetBackupRoot(dataFolderPath).Returns(backupRoot);
        _backupService.GetBackupSessionsAsync(backupRoot, Arg.Any<CancellationToken>()).Returns([session]);
        _messageDialog.ShowConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _backupService.RestorePluginAsync(
                plugin,
                session.SessionDirectory,
                dataFolderPath,
                Arg.Any<IProgress<BackupCopyProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BackupRestoreResult(
                BackupOperationStatus.Complete,
                [new BackupRestoreRowResult(plugin.FileName, BackupRestoreRowStatus.Restored, null, 1024, 1024)]));

        var vm = CreateViewModel();
        await vm.LoadSessionsAsync(dataFolderPath);
        vm.SelectedSession = session;
        vm.SelectedPlugin = plugin;

        await vm.RestorePluginCommand.ExecuteAsync(null);

        await _backupService.Received(1).RestorePluginAsync(
            plugin,
            session.SessionDirectory,
            dataFolderPath,
            Arg.Any<IProgress<BackupCopyProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies the configured game Data folder is retained as the trusted restore root for Restore All.
    /// </summary>
    [Fact]
    public async Task RestoreAllCommand_PassesLoadedDataFolderAsTrustedRestoreRoot()
    {
        var dataFolderPath = @"C:\Games\Skyrim Special Edition\Data";
        var backupRoot = @"C:\Games\Skyrim Special Edition\AutoQAC Backups";
        var session = CreateSession(CreatePlugin("Update.esm"));
        _backupService.GetBackupRoot(dataFolderPath).Returns(backupRoot);
        _backupService.GetBackupSessionsAsync(backupRoot, Arg.Any<CancellationToken>()).Returns([session]);
        _messageDialog.ShowConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _backupService.RestoreSessionAsync(
                session,
                dataFolderPath,
                Arg.Any<IProgress<BackupCopyProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BackupRestoreResult(
                BackupOperationStatus.Complete,
                [new BackupRestoreRowResult("Update.esm", BackupRestoreRowStatus.Restored, null, 1024, 1024)]));

        var vm = CreateViewModel();
        await vm.LoadSessionsAsync(dataFolderPath);
        vm.SelectedSession = session;

        await vm.RestoreAllCommand.ExecuteAsync(null);

        await _backupService.Received(1).RestoreSessionAsync(
            session,
            dataFolderPath,
            Arg.Any<IProgress<BackupCopyProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies restore commands fail closed in the UI when no trusted restore root has been loaded.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task RestoreCommands_DisabledWhenTrustedRestoreRootMissing(string? dataFolderPath)
    {
        var plugin = CreatePlugin("Update.esm");
        var session = CreateSession(plugin);
        var vm = CreateViewModel();
        vm.SelectedSession = session;
        vm.SelectedPlugin = plugin;

        await vm.LoadSessionsAsync(dataFolderPath);

        vm.RestorePluginCommand.CanExecute(null).Should().BeFalse();
        vm.RestoreAllCommand.CanExecute(null).Should().BeFalse();
        vm.DeleteSessionCommand.CanExecute(null).Should().BeFalse(
            "DeleteSessionCommand must also be disabled when no trusted restore root is loaded -- unifies the safety story with Restore Selected/All gating from Plan 07-11");
    }

    /// <summary>
    /// Helper that primes the backup service substitute with a backup root and one session,
    /// then loads the ViewModel and selects the session. Used by the Plan 07-13 Delete Session
    /// containment tests so they share a single setup pattern instead of duplicating arrange logic.
    /// </summary>
    private async Task<RestoreViewModel> CreateLoadedViewModelAsync(string dataFolderPath, string backupRoot, BackupSession session)
    {
        _backupService.GetBackupRoot(dataFolderPath).Returns(backupRoot);
        _backupService.GetBackupSessionsAsync(backupRoot, Arg.Any<CancellationToken>()).Returns([session]);
        var vm = CreateViewModel();
        await vm.LoadSessionsAsync(dataFolderPath);
        vm.SelectedSession = session;
        return vm;
    }

    /// <summary>
    /// Plan 07-13 RED: confirmed delete with a session directory clearly outside the loaded backup
    /// root must fail closed. The ViewModel must surface the canonical out-of-root sentence in both
    /// StatusText and the dialog details copy and must not invoke recursive filesystem deletion.
    /// </summary>
    [Fact]
    public async Task DeleteSessionCommand_UnsafeSessionDirectoryOutsideBackupRoot_FailsClosed()
    {
        var dataFolder = @"C:\Games\Skyrim Special Edition\Data";
        var backupRoot = @"C:\Games\Skyrim Special Edition\AutoQAC Backups";
        var session = new BackupSession
        {
            Timestamp = new DateTime(2026, 4, 29, 7, 30, 0),
            GameType = "Skyrim Special Edition",
            SessionDirectory = @"C:\Outside\session",
            Plugins = new List<BackupPluginEntry>()
        };
        var vm = await CreateLoadedViewModelAsync(dataFolder, backupRoot, session);
        _messageDialog.ShowConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _backupService.DeleteSessionAsync(session, backupRoot, Arg.Any<CancellationToken>())
            .Returns(new BackupSessionDeleteResult(BackupSessionDeleteStatus.RejectedOutsideBackupRoot, session.SessionDirectory));

        await vm.DeleteSessionCommand.ExecuteAsync(null);

        vm.Sessions.Should().Contain(session, "rejected sessions must remain in the list");
        vm.SelectedSession.Should().Be(session);
        vm.StatusText.Should().Be("The selected backup session is outside the configured backup folder.");
        await _messageDialog.Received(1).ShowErrorAsync(
            "Delete Failed",
            "Failed to delete the backup session.",
            "The selected backup session is outside the configured backup folder.");
    }

    /// <summary>
    /// Plan 07-13 RED: a sibling-prefix path such as <c>"AutoQAC Backups 2"</c> next to
    /// <c>"AutoQAC Backups"</c> must fail closed even though the prefix string would naively match.
    /// </summary>
    [Fact]
    public async Task DeleteSessionCommand_SiblingPrefixBackupRoot_FailsClosed()
    {
        var dataFolder = @"C:\Games\Data";
        var backupRoot = @"C:\Games\AutoQAC Backups";
        var session = new BackupSession
        {
            Timestamp = new DateTime(2026, 4, 29, 7, 30, 0),
            GameType = "Skyrim Special Edition",
            SessionDirectory = @"C:\Games\AutoQAC Backups 2\2026-04-29_07-30-00",
            Plugins = new List<BackupPluginEntry>()
        };
        var vm = await CreateLoadedViewModelAsync(dataFolder, backupRoot, session);
        _messageDialog.ShowConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _backupService.DeleteSessionAsync(session, backupRoot, Arg.Any<CancellationToken>())
            .Returns(new BackupSessionDeleteResult(BackupSessionDeleteStatus.RejectedOutsideBackupRoot, session.SessionDirectory));

        await vm.DeleteSessionCommand.ExecuteAsync(null);

        vm.StatusText.Should().Be("The selected backup session is outside the configured backup folder.");
        await _messageDialog.Received(1).ShowErrorAsync(
            "Delete Failed",
            "Failed to delete the backup session.",
            "The selected backup session is outside the configured backup folder.");
    }

    /// <summary>
    /// Plan 07-13 RED: a session path that escapes the backup root through <c>..</c> traversal
    /// must fail closed even when the unnormalized path string starts with the root.
    /// </summary>
    [Fact]
    public async Task DeleteSessionCommand_ParentTraversalEscapesBackupRoot_FailsClosed()
    {
        var dataFolder = @"C:\Games\Data";
        var backupRoot = @"C:\Games\AutoQAC Backups";
        var session = new BackupSession
        {
            Timestamp = new DateTime(2026, 4, 29, 7, 30, 0),
            GameType = "Skyrim Special Edition",
            SessionDirectory = @"C:\Games\AutoQAC Backups\..\OutsideSession",
            Plugins = new List<BackupPluginEntry>()
        };
        var vm = await CreateLoadedViewModelAsync(dataFolder, backupRoot, session);
        _messageDialog.ShowConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _backupService.DeleteSessionAsync(session, backupRoot, Arg.Any<CancellationToken>())
            .Returns(new BackupSessionDeleteResult(BackupSessionDeleteStatus.RejectedOutsideBackupRoot, session.SessionDirectory));

        await vm.DeleteSessionCommand.ExecuteAsync(null);

        vm.StatusText.Should().Be("The selected backup session is outside the configured backup folder.");
    }

    /// <summary>
    /// Plan 07-13 RED: when no trusted restore root is loaded (data folder null/empty),
    /// <c>_backupRoot</c> stays null and DeleteSessionCommand must be disabled. Even direct
    /// invocation must short-circuit before calling the backup service.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task DeleteSessionCommand_NullBackupRoot_FailsClosed(string? dataFolderPath)
    {
        var session = CreateSession(CreatePlugin("Update.esm"));
        var vm = CreateViewModel();
        // _backupRoot stays null because LoadSessionsAsync receives a null/empty data folder.
        await vm.LoadSessionsAsync(dataFolderPath);
        vm.SelectedSession = session;

        vm.DeleteSessionCommand.CanExecute(null).Should().BeFalse(
            "DeleteSessionCommand must be disabled when _backupRoot is null/empty/whitespace");

        // Direct execution must remain a no-op even if a caller bypasses CanExecute.
        await vm.DeleteSessionCommand.ExecuteAsync(null);

        await _backupService.DidNotReceive().DeleteSessionAsync(
            Arg.Any<BackupSession>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Plan 07-13 RED: a contained session whose deletion succeeds must remove the row from
    /// Sessions, clear SelectedSession, raise HasSessions, and set StatusText to the canonical
    /// success copy.
    /// </summary>
    [Fact]
    public async Task DeleteSessionCommand_NormalContainedSession_DeletesAndUpdatesViewModelState()
    {
        var dataFolder = @"C:\Games\Data";
        var backupRoot = @"C:\Games\AutoQAC Backups";
        var session = new BackupSession
        {
            Timestamp = new DateTime(2026, 4, 29, 7, 30, 0),
            GameType = "Skyrim Special Edition",
            SessionDirectory = @"C:\Games\AutoQAC Backups\2026-04-29_07-30-00",
            Plugins = new List<BackupPluginEntry>()
        };
        var vm = await CreateLoadedViewModelAsync(dataFolder, backupRoot, session);
        _messageDialog.ShowConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _backupService.DeleteSessionAsync(session, backupRoot, Arg.Any<CancellationToken>())
            .Returns(new BackupSessionDeleteResult(BackupSessionDeleteStatus.Deleted, session.SessionDirectory));

        await vm.DeleteSessionCommand.ExecuteAsync(null);

        vm.Sessions.Should().NotContain(session);
        vm.SelectedSession.Should().BeNull();
        vm.HasSessions.Should().BeFalse();
        vm.StatusText.Should().Be("Session deleted");
    }

    /// <summary>
    /// Plan 07-13 RED: when the service reports a non-validation failure (locked file, IO error),
    /// the ViewModel must show the generic "technical details written to the log" sentence and
    /// must not include raw exception message content in the dialog or status text.
    /// </summary>
    [Fact]
    public async Task DeleteSessionCommand_ServiceFailedStatus_LogsAndShowsConciseError()
    {
        var dataFolder = @"C:\Games\Data";
        var backupRoot = @"C:\Games\AutoQAC Backups";
        var session = new BackupSession
        {
            Timestamp = new DateTime(2026, 4, 29, 7, 30, 0),
            GameType = "Skyrim Special Edition",
            SessionDirectory = @"C:\Games\AutoQAC Backups\2026-04-29_07-30-00",
            Plugins = new List<BackupPluginEntry>()
        };
        var vm = await CreateLoadedViewModelAsync(dataFolder, backupRoot, session);
        _messageDialog.ShowConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _backupService.DeleteSessionAsync(session, backupRoot, Arg.Any<CancellationToken>())
            .Returns(new BackupSessionDeleteResult(BackupSessionDeleteStatus.Failed, session.SessionDirectory));

        await vm.DeleteSessionCommand.ExecuteAsync(null);

        vm.StatusText.Should().Be("Failed to delete the backup session. Technical details were written to the log.");
        await _messageDialog.Received(1).ShowErrorAsync(
            "Delete Failed",
            "Failed to delete the backup session.",
            "Technical details were written to the log.");
    }

    /// <summary>
    /// Test dispatcher that records posted callbacks and runs them only when explicitly drained.
    /// </summary>
    private sealed class DeferredUiDispatcher : IUiDispatcher
    {
        private readonly ConcurrentQueue<Action> _actions = new();

        public TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <inheritdoc />
        public void Post(Action action)
        {
            _actions.Enqueue(action);
            Posted.TrySetResult();
        }

        /// <inheritdoc />
        public Task InvokeAsync(Func<Task> action) => action();

        /// <summary>
        /// Executes all queued UI callbacks in FIFO order to simulate the UI thread draining work.
        /// </summary>
        public void Drain()
        {
            while (_actions.TryDequeue(out var action))
            {
                action();
            }
        }
    }
}
