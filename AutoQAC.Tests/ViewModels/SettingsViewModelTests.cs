using System.Reactive.Subjects;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.UI;
using AutoQAC.ViewModels;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.ViewModels;

/// <summary>
/// Covers the Settings dialog persistence-failure banner state machine introduced for
/// Phase 10 D-24/D-27/D-29/D-30/D-31.
/// </summary>
public sealed class SettingsViewModelTests
{
    [Fact]
    public void Failure_Save_WriteFailed_PopulatesBannerWithRestoredText()
    {
        var fixture = CreateFixture();
        using var vm = fixture.CreateViewModel();

        fixture.Failures.OnNext(new ConfigPersistenceFailure(
            ConfigPersistenceOperationKind.Save,
            ConfigPersistenceFailureKind.WriteFailed,
            "Could not write settings file (write_failed)",
            null,
            1));

        vm.PersistenceBannerText.Should().Be(
            "Could not save settings. Settings were restored to last saved values.");
        vm.HasPersistenceBanner.Should().BeTrue();
    }

    [Fact]
    public void Failure_Flush_WriteFailed_PopulatesBlockedCleaningBanner()
    {
        var fixture = CreateFixture();
        using var vm = fixture.CreateViewModel();

        fixture.Failures.OnNext(new ConfigPersistenceFailure(
            ConfigPersistenceOperationKind.Flush,
            ConfigPersistenceFailureKind.WriteFailed,
            "Could not write settings file (write_failed)",
            null,
            2));

        vm.PersistenceBannerText.Should().Contain("Cleaning was blocked");
        vm.PersistenceBannerText.Should().Contain("restored to last saved values");
    }

    [Fact]
    public void Failure_Reload_InvalidExternalYaml_PopulatesYamlBanner()
    {
        var fixture = CreateFixture();
        using var vm = fixture.CreateViewModel();

        fixture.Failures.OnNext(new ConfigPersistenceFailure(
            ConfigPersistenceOperationKind.Reload,
            ConfigPersistenceFailureKind.InvalidExternalYaml,
            "Invalid settings YAML was rejected (invalid_external_yaml)",
            null,
            3));

        vm.PersistenceBannerText.Should().Contain("invalid YAML");
        vm.PersistenceBannerText.Should().Contain("Active settings were not changed");
    }

    [Fact]
    public void Failure_Reload_MissingFile_PopulatesMissingBanner()
    {
        var fixture = CreateFixture();
        using var vm = fixture.CreateViewModel();

        fixture.Failures.OnNext(new ConfigPersistenceFailure(
            ConfigPersistenceOperationKind.Reload,
            ConfigPersistenceFailureKind.MissingFile,
            "Settings file is missing (missing_file)",
            null,
            4));

        vm.PersistenceBannerText.Should().Contain("missing or unreadable");
        vm.PersistenceBannerText.Should().Contain("Active settings were not changed");
    }

    [Fact]
    public async Task BannerText_ClearsAfter_NextSuccessfulSave()
    {
        var fixture = CreateFixture();
        using var vm = fixture.CreateViewModel();
        ApplyValidEditableValues(vm);
        bool? closeResult = null;
        vm.CloseRequested += result => closeResult = result;

        fixture.Failures.OnNext(new ConfigPersistenceFailure(
            ConfigPersistenceOperationKind.Save,
            ConfigPersistenceFailureKind.WriteFailed,
            "Could not write settings file (write_failed)",
            null,
            5));
        fixture.ConfigService.FlushPendingSavesAsync(Arg.Any<CancellationToken>()).Returns(
            new ConfigPersistenceResult(ConfigPersistenceStatusKind.Success, ConfigPersistenceOperationKind.Flush, 6, null));

        await vm.SaveCommand.ExecuteAsync(null);

        vm.PersistenceBannerText.Should().BeNull("D-27 clears the banner after a successful explicit save flush");
        vm.HasPersistenceBanner.Should().BeFalse();
        closeResult.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_FlushFailure_KeepsDialogOpenAndShowsBanner()
    {
        var fixture = CreateFixture();
        using var vm = fixture.CreateViewModel();
        ApplyValidEditableValues(vm);
        bool? closeResult = null;
        vm.CloseRequested += result => closeResult = result;
        var failure = new ConfigPersistenceFailure(
            ConfigPersistenceOperationKind.Flush,
            ConfigPersistenceFailureKind.WriteFailed,
            "Could not write settings file (write_failed)",
            null,
            7);
        fixture.ConfigService.FlushPendingSavesAsync(Arg.Any<CancellationToken>()).Returns(
            new ConfigPersistenceResult(ConfigPersistenceStatusKind.Failed, ConfigPersistenceOperationKind.Flush, 7, failure));

        await vm.SaveCommand.ExecuteAsync(null);

        vm.PersistenceBannerText.Should().Contain("restored to last saved values");
        vm.PersistenceBannerText.Should().NotContain("Cleaning was blocked");
        closeResult.Should().BeFalse("the settings dialog must stay open when the flush barrier fails");
    }

    [Fact]
    public void BannerText_ClearsAfter_AcceptedExternalReload()
    {
        var fixture = CreateFixture();
        using var vm = fixture.CreateViewModel();
        fixture.Failures.OnNext(new ConfigPersistenceFailure(
            ConfigPersistenceOperationKind.Reload,
            ConfigPersistenceFailureKind.InvalidExternalYaml,
            "Invalid settings YAML was rejected (invalid_external_yaml)",
            null,
            8));

        fixture.UserConfigurationChanged.OnNext(new UserConfiguration());

        vm.PersistenceBannerText.Should().BeNull("D-27 clears on the next accepted external reload");
    }

    [Fact]
    public void Failure_Subscription_DisposedOnDispose()
    {
        var fixture = CreateFixture(useRecordingObservables: true);
        using var vm = fixture.CreateViewModel();

        vm.Dispose();

        fixture.RecordingFailures!.DisposeCount.Should().Be(1);
        fixture.RecordingResults!.DisposeCount.Should().Be(1);
        fixture.RecordingUserConfigurationChanged!.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void SettingsWindow_BindsPersistenceBannerText_StaticGuard()
    {
        var source = File.ReadAllText(ProjectPath("AutoQAC", "Views", "SettingsWindow.axaml"));

        source.Should().Contain("PersistenceBannerText");
        source.Should().Contain("HasPersistenceBanner");
    }

    [Fact]
    public void BannerText_NoModalDialog_StaysAsTextOnly_StaticGuard()
    {
        var source = File.ReadAllText(ProjectPath("AutoQAC", "ViewModels", "SettingsViewModel.cs"));

        source.Should().NotContain("MessageBox");
        source.Should().NotContain("IDialogService.ShowError");
        source.Should().NotContain("ShowAlert");
    }

    [Fact]
    public void Failure_Save_WriteFailed_DoesNotInvokeRetryUi_StaticGuard()
    {
        var source = File.ReadAllText(ProjectPath("AutoQAC", "ViewModels", "SettingsViewModel.cs"));

        source.Should().NotContain("RetryCommand");
        source.Should().NotContain("RetryButton");
        source.Should().NotContain("RetryAttempt");
    }

    private static Fixture CreateFixture(bool useRecordingObservables = false)
    {
        var configService = Substitute.For<IConfigurationService>();
        var logger = Substitute.For<ILoggingService>();
        var dispatcher = new SynchronousUiDispatcher();
        var failures = new Subject<ConfigPersistenceFailure>();
        var results = new Subject<ConfigPersistenceResult>();
        var userConfigurationChanged = new Subject<UserConfiguration>();
        RecordingObservable<ConfigPersistenceFailure>? recordingFailures = null;
        RecordingObservable<ConfigPersistenceResult>? recordingResults = null;
        RecordingObservable<UserConfiguration>? recordingUserConfigurationChanged = null;

        if (useRecordingObservables)
        {
            recordingFailures = new RecordingObservable<ConfigPersistenceFailure>();
            recordingResults = new RecordingObservable<ConfigPersistenceResult>();
            recordingUserConfigurationChanged = new RecordingObservable<UserConfiguration>();
            configService.Failures.Returns(recordingFailures);
            configService.PersistenceResults.Returns(recordingResults);
            configService.UserConfigurationChanged.Returns(recordingUserConfigurationChanged);
        }
        else
        {
            configService.Failures.Returns(failures);
            configService.PersistenceResults.Returns(results);
            configService.UserConfigurationChanged.Returns(userConfigurationChanged);
        }

        configService.LoadUserConfigAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(CreateConfig()));
        configService.SaveUserConfigAsync(Arg.Any<UserConfiguration>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        configService.FlushPendingSavesAsync(Arg.Any<CancellationToken>()).Returns(
            new ConfigPersistenceResult(ConfigPersistenceStatusKind.Success, ConfigPersistenceOperationKind.Flush, 1, null));

        return new Fixture(
            configService,
            logger,
            dispatcher,
            failures,
            results,
            userConfigurationChanged,
            recordingFailures,
            recordingResults,
            recordingUserConfigurationChanged);
    }

    private static UserConfiguration CreateConfig() => new()
    {
        Settings = new AutoQacSettings
        {
            JournalExpiration = 30,
            CleaningTimeout = 300,
            CpuThreshold = 50,
            Mo2Mode = false
        },
        XEdit = new XEditConfig(),
        ModOrganizer = new ModOrganizerConfig(),
        LoadOrder = new LoadOrderConfig(),
        LogRetention = new RetentionSettings(),
        Backup = new BackupSettings()
    };

    private static void ApplyValidEditableValues(SettingsViewModel vm)
    {
        vm.JournalExpiration = 30;
        vm.CleaningTimeout = 300;
        vm.CpuThreshold = 50;
        vm.MaxAgeDays = 14;
        vm.MaxFileCount = 100;
        vm.BackupMaxSessions = 10;
    }

    private static string ProjectPath(params string[] segments)
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(new[] { directory }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(directory);
            if (parent is null)
            {
                break;
            }

            directory = parent.FullName;
        }

        return Path.Combine(segments);
    }

    private sealed record Fixture(
        IConfigurationService ConfigService,
        ILoggingService Logger,
        IUiDispatcher Dispatcher,
        Subject<ConfigPersistenceFailure> Failures,
        Subject<ConfigPersistenceResult> Results,
        Subject<UserConfiguration> UserConfigurationChanged,
        RecordingObservable<ConfigPersistenceFailure>? RecordingFailures,
        RecordingObservable<ConfigPersistenceResult>? RecordingResults,
        RecordingObservable<UserConfiguration>? RecordingUserConfigurationChanged)
    {
        public SettingsViewModel CreateViewModel() => new(ConfigService, Logger, Dispatcher);
    }

    private sealed class SynchronousUiDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();

        public Task InvokeAsync(Func<Task> action) => action();
    }

    private sealed class RecordingObservable<T> : IObservable<T>
    {
        public int DisposeCount { get; private set; }

        public IDisposable Subscribe(IObserver<T> observer) => new RecordingDisposable(this);

        private sealed class RecordingDisposable : IDisposable
        {
            private readonly RecordingObservable<T> _owner;
            private bool _disposed;

            public RecordingDisposable(RecordingObservable<T> owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _owner.DisposeCount++;
            }
        }
    }
}
