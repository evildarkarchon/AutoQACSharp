using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.GameCapability;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using AutoQAC.Tests.TestInfrastructure;
using FluentAssertions;

namespace AutoQAC.Tests.Services.Cleaning;

public sealed class CleaningCommandReadinessTests
{
    [Fact]
    public async Task EvaluateAsync_FreshPublicationWithSelectedCleanableRows_IsReady()
    {
        var xEditPath = await CreateTempFileAsync();
        try
        {
            var stateService = CreateState(xEditPath: xEditPath);
            using var refresh = new RecordingPluginRefreshModule();
            refresh.CurrentPublication = RecordingPluginRefreshModule.CreateFreshPublication(
                rows: [RecordingPluginRefreshModule.CreatePublishedRow(CreatePlugin("NeedsCleaning.esp"))]);
            var sut = new CleaningCommandReadiness(refresh, stateService);

            var result = await sut.EvaluateAsync();

            result.CanStartOrPreview.Should().BeTrue();
            result.Failure.Should().BeNull();
        }
        finally
        {
            File.Delete(xEditPath);
        }
    }

    [Fact]
    public async Task EvaluateAsync_MissingPublication_ReturnsMissingPublicationFailure()
    {
        var xEditPath = await CreateTempFileAsync();
        try
        {
            var stateService = CreateState(xEditPath: xEditPath);
            using var refresh = new RecordingPluginRefreshModule(
                RecordingPluginRefreshModule.CreateSnapshot(GameType.SkyrimSe));
            var sut = new CleaningCommandReadiness(refresh, stateService);

            var result = await sut.EvaluateAsync();

            result.CanStartOrPreview.Should().BeFalse();
            result.Failure!.Kind.Should().Be(CleaningPreflightFailureKind.MissingPluginRefreshPublication);
        }
        finally
        {
            File.Delete(xEditPath);
        }
    }

    [Fact]
    public async Task EvaluateAsync_StalePublication_ReturnsStalePublicationFailure()
    {
        var xEditPath = await CreateTempFileAsync();
        try
        {
            var stateService = CreateState(xEditPath: xEditPath);
            using var refresh = new RecordingPluginRefreshModule();
            var fresh = RecordingPluginRefreshModule.CreateFreshPublication();
            refresh.CurrentPublication = fresh with
            {
                Freshness = new PluginRefreshFreshness(false, PluginRefreshStalenessReason.SelectedGameChanged)
            };
            var sut = new CleaningCommandReadiness(refresh, stateService);

            var result = await sut.EvaluateAsync();

            result.CanStartOrPreview.Should().BeFalse();
            result.Failure!.Kind.Should().Be(CleaningPreflightFailureKind.StalePluginRefreshPublication);
        }
        finally
        {
            File.Delete(xEditPath);
        }
    }

    [Fact]
    public async Task EvaluateAsync_NoSelectedCleanableRows_ReturnsNoPluginsSelectedFailure()
    {
        var xEditPath = await CreateTempFileAsync();
        try
        {
            var stateService = CreateState(xEditPath: xEditPath);
            using var refresh = new RecordingPluginRefreshModule();
            var skipped = CreatePlugin("Skipped.esp") with { IsInSkipList = true };
            refresh.CurrentPublication = RecordingPluginRefreshModule.CreateFreshPublication(
                rows:
                [
                    RecordingPluginRefreshModule.CreatePublishedRow(
                        CreatePlugin("Deselected.esp"),
                        isSelected: false),
                    RecordingPluginRefreshModule.CreatePublishedRow(
                        skipped,
                        isVisible: false,
                        isSelected: true,
                        isSkippedByPolicy: true)
                ]);
            var sut = new CleaningCommandReadiness(refresh, stateService);

            var result = await sut.EvaluateAsync();

            result.CanStartOrPreview.Should().BeFalse();
            result.Failure!.Kind.Should().Be(CleaningPreflightFailureKind.NoPluginsSelected);
        }
        finally
        {
            File.Delete(xEditPath);
        }
    }

    [Fact]
    public async Task EvaluateAsync_MissingXEditPath_ReturnsXEditNotConfiguredFailure()
    {
        using var stateService = CreateState(xEditPath: null);
        using var refresh = new RecordingPluginRefreshModule();
        refresh.CurrentPublication = RecordingPluginRefreshModule.CreateFreshPublication();
        var sut = new CleaningCommandReadiness(refresh, stateService);

        var result = await sut.EvaluateAsync();

        result.CanStartOrPreview.Should().BeFalse();
        result.Failure!.Kind.Should().Be(CleaningPreflightFailureKind.XEditNotConfigured);
    }

    [Fact]
    public async Task EvaluateAsync_MissingXEditFile_ReturnsXEditNotFoundFailure()
    {
        using var stateService = CreateState(xEditPath: @"C:\Missing\SSEEdit.exe");
        using var refresh = new RecordingPluginRefreshModule();
        refresh.CurrentPublication = RecordingPluginRefreshModule.CreateFreshPublication();
        var sut = new CleaningCommandReadiness(refresh, stateService);

        var result = await sut.EvaluateAsync();

        result.CanStartOrPreview.Should().BeFalse();
        result.Failure!.Kind.Should().Be(CleaningPreflightFailureKind.XEditNotFound);
    }

    [Fact]
    public async Task EvaluateAsync_DirectLoadOrderModeWithoutPath_ReturnsLoadOrderNotConfiguredFailure()
    {
        var xEditPath = await CreateTempFileAsync();
        try
        {
            var configuration = RecordingPluginRefreshModule.CreateConfiguration(xEditPath: xEditPath);
            var plan = RecordingPluginRefreshModule.CreateDiscoveryPlan(
                mode: PluginRefreshDiscoveryMode.DirectLoadOrderFile,
                configuration: configuration,
                loadOrderPath: null);
            var stateService = CreateState(xEditPath: xEditPath);
            using var refresh = new RecordingPluginRefreshModule();
            refresh.CurrentPublication = RecordingPluginRefreshModule.CreateFreshPublication(
                discoveryPlan: plan,
                configuration: configuration);
            var sut = new CleaningCommandReadiness(refresh, stateService);

            var result = await sut.EvaluateAsync();

            result.CanStartOrPreview.Should().BeFalse();
            result.Failure!.Kind.Should().Be(CleaningPreflightFailureKind.LoadOrderNotConfigured);
        }
        finally
        {
            File.Delete(xEditPath);
        }
    }

    [Fact]
    public async Task EvaluateAsync_DirectLoadOrderModeWithMissingFile_ReturnsLoadOrderNotFoundFailure()
    {
        var xEditPath = await CreateTempFileAsync();
        try
        {
            var loadOrderPath = @"C:\Missing\plugins.txt";
            var configuration = RecordingPluginRefreshModule.CreateConfiguration(
                loadOrderPath: loadOrderPath,
                xEditPath: xEditPath);
            var plan = RecordingPluginRefreshModule.CreateDiscoveryPlan(
                mode: PluginRefreshDiscoveryMode.DirectLoadOrderFile,
                configuration: configuration,
                loadOrderPath: loadOrderPath);
            var stateService = CreateState(xEditPath: xEditPath, loadOrderPath: loadOrderPath);
            using var refresh = new RecordingPluginRefreshModule();
            refresh.CurrentPublication = RecordingPluginRefreshModule.CreateFreshPublication(
                discoveryPlan: plan,
                configuration: configuration);
            var sut = new CleaningCommandReadiness(refresh, stateService);

            var result = await sut.EvaluateAsync();

            result.CanStartOrPreview.Should().BeFalse();
            result.Failure!.Kind.Should().Be(CleaningPreflightFailureKind.LoadOrderNotFound);
        }
        finally
        {
            File.Delete(xEditPath);
        }
    }

    [Fact]
    public async Task EvaluateAsync_Mo2ModeWithConfiguredPathsAndProfileLoadOrder_IsReady()
    {
        var xEditPath = await CreateTempFileAsync();
        var mo2Path = await CreateTempFileAsync();
        var loadOrderPath = await CreateTempFileAsync();
        var instanceDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var configuration = RecordingPluginRefreshModule.CreateConfiguration(
                xEditPath: xEditPath,
                mo2Path: mo2Path,
                mo2ModeEnabled: true,
                mo2InstancePath: instanceDirectory.FullName,
                selectedProfile: "Default");
            var plan = RecordingPluginRefreshModule.CreateDiscoveryPlan(
                mode: PluginRefreshDiscoveryMode.Mo2LoadOrderFile,
                configuration: configuration,
                mo2LoadOrderPath: loadOrderPath);
            var stateService = CreateState(
                xEditPath: xEditPath,
                mo2Mode: true,
                mo2Path: mo2Path,
                mo2Profile: "Default");
            using var refresh = new RecordingPluginRefreshModule();
            refresh.CurrentPublication = RecordingPluginRefreshModule.CreateFreshPublication(
                discoveryPlan: plan,
                configuration: configuration);
            var sut = new CleaningCommandReadiness(refresh, stateService);

            var result = await sut.EvaluateAsync();

            result.CanStartOrPreview.Should().BeTrue();
            result.Failure.Should().BeNull();
        }
        finally
        {
            File.Delete(xEditPath);
            File.Delete(mo2Path);
            File.Delete(loadOrderPath);
            instanceDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(null, CleaningPreflightFailureKind.Mo2NotConfigured)]
    [InlineData(@"C:\Missing\ModOrganizer.exe", CleaningPreflightFailureKind.Mo2NotFound)]
    public async Task EvaluateAsync_Mo2ExecutableFailures_ReturnExpectedFailure(
        string? mo2Path,
        CleaningPreflightFailureKind expectedKind)
    {
        var xEditPath = await CreateTempFileAsync();
        var loadOrderPath = await CreateTempFileAsync();
        var instanceDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var configuration = RecordingPluginRefreshModule.CreateConfiguration(
                xEditPath: xEditPath,
                mo2Path: mo2Path,
                mo2ModeEnabled: true,
                mo2InstancePath: instanceDirectory.FullName,
                selectedProfile: "Default");
            var plan = RecordingPluginRefreshModule.CreateDiscoveryPlan(
                mode: PluginRefreshDiscoveryMode.Mo2LoadOrderFile,
                configuration: configuration,
                mo2LoadOrderPath: loadOrderPath);
            var stateService = CreateState(
                xEditPath: xEditPath,
                mo2Mode: true,
                mo2Path: mo2Path,
                mo2Profile: "Default");
            using var refresh = new RecordingPluginRefreshModule();
            refresh.CurrentPublication = RecordingPluginRefreshModule.CreateFreshPublication(
                discoveryPlan: plan,
                configuration: configuration);
            var sut = new CleaningCommandReadiness(refresh, stateService);

            var result = await sut.EvaluateAsync();

            result.CanStartOrPreview.Should().BeFalse();
            result.Failure!.Kind.Should().Be(expectedKind);
        }
        finally
        {
            File.Delete(xEditPath);
            File.Delete(loadOrderPath);
            instanceDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task EvaluateAsync_Mo2ModeWithoutSelectedProfile_ReturnsProfileMissingFailure()
    {
        var xEditPath = await CreateTempFileAsync();
        var mo2Path = await CreateTempFileAsync();
        var loadOrderPath = await CreateTempFileAsync();
        var instanceDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var configuration = RecordingPluginRefreshModule.CreateConfiguration(
                xEditPath: xEditPath,
                mo2Path: mo2Path,
                mo2ModeEnabled: true,
                mo2InstancePath: instanceDirectory.FullName,
                selectedProfile: null);
            var plan = RecordingPluginRefreshModule.CreateDiscoveryPlan(
                mode: PluginRefreshDiscoveryMode.Mo2LoadOrderFile,
                configuration: configuration,
                mo2LoadOrderPath: loadOrderPath);
            var stateService = CreateState(
                xEditPath: xEditPath,
                mo2Mode: true,
                mo2Path: mo2Path,
                mo2Profile: null);
            using var refresh = new RecordingPluginRefreshModule();
            refresh.CurrentPublication = RecordingPluginRefreshModule.CreateFreshPublication(
                discoveryPlan: plan,
                configuration: configuration);
            var sut = new CleaningCommandReadiness(refresh, stateService);

            var result = await sut.EvaluateAsync();

            result.CanStartOrPreview.Should().BeFalse();
            result.Failure!.Kind.Should().Be(CleaningPreflightFailureKind.Mo2ProfileMissing);
        }
        finally
        {
            File.Delete(xEditPath);
            File.Delete(mo2Path);
            File.Delete(loadOrderPath);
            instanceDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task EvaluateAsync_Mo2ModeWithMissingProfileLoadOrder_ReturnsProfileLoadOrderMissingFailure()
    {
        var xEditPath = await CreateTempFileAsync();
        var mo2Path = await CreateTempFileAsync();
        var instanceDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var configuration = RecordingPluginRefreshModule.CreateConfiguration(
                xEditPath: xEditPath,
                mo2Path: mo2Path,
                mo2ModeEnabled: true,
                mo2InstancePath: instanceDirectory.FullName,
                selectedProfile: "Default");
            var plan = RecordingPluginRefreshModule.CreateDiscoveryPlan(
                mode: PluginRefreshDiscoveryMode.Mo2LoadOrderFile,
                configuration: configuration,
                mo2LoadOrderPath: @"C:\Missing\loadorder.txt");
            var stateService = CreateState(
                xEditPath: xEditPath,
                mo2Mode: true,
                mo2Path: mo2Path,
                mo2Profile: "Default");
            using var refresh = new RecordingPluginRefreshModule();
            refresh.CurrentPublication = RecordingPluginRefreshModule.CreateFreshPublication(
                discoveryPlan: plan,
                configuration: configuration);
            var sut = new CleaningCommandReadiness(refresh, stateService);

            var result = await sut.EvaluateAsync();

            result.CanStartOrPreview.Should().BeFalse();
            result.Failure!.Kind.Should().Be(CleaningPreflightFailureKind.Mo2ProfileLoadOrderMissing);
        }
        finally
        {
            File.Delete(xEditPath);
            File.Delete(mo2Path);
            instanceDirectory.Delete(recursive: true);
        }
    }

    private static StateService CreateState(
        string? xEditPath,
        bool mo2Mode = false,
        string? mo2Path = null,
        string? mo2Profile = null,
        string? loadOrderPath = null)
    {
        var stateService = new StateService();
        stateService.UpdateState(state => state with
        {
            CurrentGameType = GameType.SkyrimSe,
            XEditExecutablePath = xEditPath,
            LoadOrderPath = loadOrderPath,
            Mo2ModeEnabled = mo2Mode,
            Mo2ExecutablePath = mo2Path,
            Mo2Profile = mo2Profile
        });
        return stateService;
    }

    private static PluginInfo CreatePlugin(string fileName) =>
        new()
        {
            FileName = fileName,
            FullPath = $@"C:\Game\Data\{fileName}",
            DetectedGameType = GameType.SkyrimSe
        };

    private static async Task<string> CreateTempFileAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"AutoQAC-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(path, string.Empty);
        return path;
    }
}
