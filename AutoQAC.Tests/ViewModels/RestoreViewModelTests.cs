using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Backup;
using AutoQAC.Services.UI;
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

    private RestoreViewModel CreateViewModel() => new(_backupService, _messageDialog, _logger);

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
    public async Task RestoreAllCommand_ShouldKeepPartialRestoreRowsVisibleInline()
    {
        var session = CreateSession(CreatePlugin("Update.esm"), CreatePlugin("Missing.esp"));
        _messageDialog.ShowConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _backupService.RestoreSessionAsync(
                session,
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
    public async Task RestorePluginCommand_MissingBackupFile_ShouldShowStructuredInlineRowInsteadOfSyncException()
    {
        var plugin = CreatePlugin("Missing.esp");
        var session = CreateSession(plugin);
        _messageDialog.ShowConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _backupService.RestorePluginAsync(
                plugin,
                session.SessionDirectory,
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
}
