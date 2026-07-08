using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Configuration;

/// <summary>
/// Coordinates persistence, compatibility state mirroring, and Plugin refresh publication for Discovery settings changes.
/// </summary>
public sealed class DiscoverySettingsModule(
    IConfigurationService configurationService,
    IStateService stateService,
    IPluginRefreshModule pluginRefreshModule)
    : IDiscoverySettingsModule
{
    /// <inheritdoc />
    public Task<DiscoverySettingsChangeResult> ExecuteAsync(
        DiscoverySettingsIntent intent,
        CancellationToken ct = default) =>
        intent switch
        {
            DiscoverySettingsIntent.SelectGame select => SelectGameAsync(select.GameType, ct),
            DiscoverySettingsIntent.SetMo2Mode mo2Mode => SetMo2ModeAsync(mo2Mode.Enabled, ct),
            DiscoverySettingsIntent.SetMo2Profile profile => SetMo2ProfileAsync(
                profile.GameType,
                profile.ProfileName,
                ct),
            DiscoverySettingsIntent.SetLoadOrderPath loadOrder => SetLoadOrderPathAsync(
                loadOrder.GameType,
                loadOrder.LoadOrderPath,
                ct),
            DiscoverySettingsIntent.SetGameDataFolderOverride dataFolder => SetGameDataFolderOverrideAsync(
                dataFolder.GameType,
                dataFolder.FolderPath,
                ct),
            DiscoverySettingsIntent.SetMo2InstanceOverride instance => SetMo2InstanceOverrideAsync(
                instance.GameType,
                instance.FolderPath,
                ct),
            DiscoverySettingsIntent.SetMo2ExecutablePath mo2Path => SetMo2ExecutablePathAsync(
                mo2Path.ExecutablePath,
                ct),
            DiscoverySettingsIntent.SetDisableSkipLists skipLists => SetDisableSkipListsAsync(
                skipLists.Disabled,
                ct),
            DiscoverySettingsIntent.Reset => ResetAsync(ct),
            _ => throw new ArgumentOutOfRangeException(nameof(intent), intent, "Unknown Discovery settings intent.")
        };

    private async Task<DiscoverySettingsChangeResult> SelectGameAsync(GameType gameType, CancellationToken ct)
    {
        await configurationService.SetSelectedGameAsync(gameType, ct).ConfigureAwait(false);
        return await RefreshGameAsync(gameType, selectedLoadOrderPath: null, ct).ConfigureAwait(false);
    }

    private async Task<DiscoverySettingsChangeResult> SetMo2ModeAsync(bool enabled, CancellationToken ct)
    {
        var config = await configurationService.LoadUserConfigAsync(ct).ConfigureAwait(false);
        config.Settings.Mo2Mode = enabled;
        await configurationService.SaveUserConfigAsync(config, ct).ConfigureAwait(false);

        // Preserve compatibility state even when no selected game exists and Plugin refresh publishes no runtime plan.
        stateService.UpdateState(state => state with { Mo2ModeEnabled = enabled });
        return await RefreshCurrentGameAsync(ct).ConfigureAwait(false);
    }

    private async Task<DiscoverySettingsChangeResult> SetMo2ProfileAsync(
        GameType gameType,
        string? profileName,
        CancellationToken ct)
    {
        await configurationService.SetMo2ProfileAsync(gameType, profileName, ct).ConfigureAwait(false);
        await configurationService.FlushPendingSavesAsync(ct).ConfigureAwait(false);
        return await RefreshGameAsync(gameType, selectedLoadOrderPath: null, ct).ConfigureAwait(false);
    }

    private async Task<DiscoverySettingsChangeResult> SetLoadOrderPathAsync(
        GameType gameType,
        string loadOrderPath,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(loadOrderPath) || !File.Exists(loadOrderPath))
        {
            return DiscoverySettingsChangeResult.Rejected(new DiscoverySettingsChangeFailure(
                DiscoverySettingsChangeFailureKind.InvalidLoadOrderPath,
                "The selected load order file is missing.",
                "Choose the current plugins.txt or loadorder.txt file."));
        }

        var result = await RefreshGameAsync(gameType, loadOrderPath, ct).ConfigureAwait(false);
        await configurationService.SetGameLoadOrderOverrideAsync(gameType, loadOrderPath, ct)
            .ConfigureAwait(false);
        return result;
    }

    private async Task<DiscoverySettingsChangeResult> SetGameDataFolderOverrideAsync(
        GameType gameType,
        string? folderPath,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(folderPath) && !Directory.Exists(folderPath))
        {
            return DiscoverySettingsChangeResult.Rejected(new DiscoverySettingsChangeFailure(
                DiscoverySettingsChangeFailureKind.InvalidGameDataFolder,
                "The selected game data folder is missing.",
                "Choose an existing game data folder."));
        }

        await configurationService.SetGameDataFolderOverrideAsync(gameType, folderPath, ct)
            .ConfigureAwait(false);
        return await RefreshGameAsync(gameType, selectedLoadOrderPath: null, ct).ConfigureAwait(false);
    }

    private async Task<DiscoverySettingsChangeResult> SetMo2InstanceOverrideAsync(
        GameType gameType,
        string? folderPath,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(folderPath) && !Directory.Exists(folderPath))
        {
            return DiscoverySettingsChangeResult.Rejected(new DiscoverySettingsChangeFailure(
                DiscoverySettingsChangeFailureKind.InvalidMo2InstanceFolder,
                "The selected MO2 instance folder is missing.",
                "Choose the MO2 instance folder or disable MO2 Mode."));
        }

        await configurationService.SetMo2InstanceOverrideAsync(gameType, folderPath, ct).ConfigureAwait(false);
        await configurationService.FlushPendingSavesAsync(ct).ConfigureAwait(false);
        return await RefreshGameAsync(gameType, selectedLoadOrderPath: null, ct).ConfigureAwait(false);
    }

    private async Task<DiscoverySettingsChangeResult> SetMo2ExecutablePathAsync(
        string executablePath,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(executablePath) ||
            !File.Exists(executablePath) ||
            !executablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return DiscoverySettingsChangeResult.Rejected(new DiscoverySettingsChangeFailure(
                DiscoverySettingsChangeFailureKind.InvalidMo2ExecutablePath,
                "The selected Mod Organizer executable is missing.",
                "Choose ModOrganizer.exe or disable MO2 Mode."));
        }

        var config = await configurationService.LoadUserConfigAsync(ct).ConfigureAwait(false);
        config.ModOrganizer.Binary = executablePath;
        await configurationService.SaveUserConfigAsync(config, ct).ConfigureAwait(false);
        await configurationService.FlushPendingSavesAsync(ct).ConfigureAwait(false);
        stateService.UpdateConfigurationPaths(
            stateService.CurrentState.LoadOrderPath,
            executablePath,
            stateService.CurrentState.XEditExecutablePath,
            stateService.CurrentState.Mo2Profile);
        return await RefreshCurrentGameAsync(ct).ConfigureAwait(false);
    }

    private async Task<DiscoverySettingsChangeResult> SetDisableSkipListsAsync(bool disabled, CancellationToken ct)
    {
        var config = await configurationService.LoadUserConfigAsync(ct).ConfigureAwait(false);
        config.Settings.DisableSkipLists = disabled;
        await configurationService.SaveUserConfigAsync(config, ct).ConfigureAwait(false);
        return await RefreshCurrentGameAsync(ct).ConfigureAwait(false);
    }

    private async Task<DiscoverySettingsChangeResult> ResetAsync(CancellationToken ct)
    {
        await pluginRefreshModule.ExecuteAsync(
                new PluginRefreshIntent.Cancel(PluginRefreshCancelReason.Reset),
                ct)
            .ConfigureAwait(false);
        await configurationService.ResetToDefaultsAsync(ct).ConfigureAwait(false);
        var config = await configurationService.LoadUserConfigAsync(ct).ConfigureAwait(false);

        stateService.UpdateConfigurationPaths(
            config.LoadOrder.File,
            config.ModOrganizer.Binary,
            config.XEdit.Binary,
            mo2Profile: null);
        stateService.UpdateState(state => state with
        {
            CurrentGameType = GameType.Unknown,
            Mo2ModeEnabled = config.Settings.Mo2Mode,
            CleaningTimeout = config.Settings.CleaningTimeout,
            PartialFormsEnabled = false,
            Mo2Profile = null
        });
        stateService.SetPluginsToClean([]);

        return await RefreshGameAsync(GameType.Unknown, selectedLoadOrderPath: null, ct).ConfigureAwait(false);
    }

    private Task<DiscoverySettingsChangeResult> RefreshCurrentGameAsync(CancellationToken ct) =>
        RefreshGameAsync(stateService.CurrentState.CurrentGameType, selectedLoadOrderPath: null, ct);

    private async Task<DiscoverySettingsChangeResult> RefreshGameAsync(
        GameType gameType,
        string? selectedLoadOrderPath,
        CancellationToken ct)
    {
        var snapshot = await pluginRefreshModule.ExecuteAsync(
                new PluginRefreshIntent.RefreshGame(gameType, selectedLoadOrderPath),
                ct)
            .ConfigureAwait(false);
        return DiscoverySettingsChangeResult.Accepted(snapshot);
    }
}
