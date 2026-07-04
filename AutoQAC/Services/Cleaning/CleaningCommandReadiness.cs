using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;
using AutoQAC.Models.Diagnostics;
using AutoQAC.Services.GameCapability;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Default Cleaning command readiness projection backed by Plugin refresh publication facts.
/// </summary>
internal sealed class CleaningCommandReadiness(
    IPluginRefreshModule pluginRefreshModule,
    IStateService stateService)
    : ICleaningCommandReadiness
{
    /// <inheritdoc />
    public async Task<CleaningCommandReadinessResult> EvaluateAsync(CancellationToken ct = default)
    {
        var publication = await pluginRefreshModule.GetCurrentPublicationAsync(ct).ConfigureAwait(false);
        var state = stateService.CurrentState;
        if (state.IsCleaning)
        {
            return CleaningCommandReadinessResult.Busy;
        }

        if (TryValidatePublication(publication, out var failure) ||
            TryValidateLaunchReadiness(publication, state, out failure) ||
            TryValidateSelection(publication, out failure))
        {
            return CleaningCommandReadinessResult.Blocked(failure);
        }

        return CleaningCommandReadinessResult.Ready;
    }

    private static bool TryValidatePublication(
        PluginRefreshPublication publication,
        out CleaningPreflightFailure failure)
    {
        if (publication.GameType == GameType.Unknown)
        {
            failure = CreateFailure(
                CleaningPreflightFailureKind.NoGameSelected,
                "No game type is selected for cleaning.",
                "Select a game before cleaning.");
            return true;
        }

        if (!publication.Freshness.IsFresh)
        {
            var kind = publication.Freshness.StalenessReason == PluginRefreshStalenessReason.MissingPublication
                ? CleaningPreflightFailureKind.MissingPluginRefreshPublication
                : CleaningPreflightFailureKind.StalePluginRefreshPublication;
            failure = kind == CleaningPreflightFailureKind.MissingPluginRefreshPublication
                ? CreateFailure(
                    kind,
                    "Plugins have not been refreshed for the selected game.",
                    "Select a game and refresh plugins before cleaning.")
                : CreateFailure(
                    kind,
                    "Plugins need to be refreshed after Discovery-affecting settings changed.",
                    "Refresh plugins after changing game, load order, MO2, or Skip list settings.");
            return true;
        }

        if (publication.DiscoveryPlan is null)
        {
            failure = CreateFailure(
                CleaningPreflightFailureKind.MissingPluginRefreshPublication,
                "Plugins have not been refreshed for the selected game.",
                "Select a game and refresh plugins before cleaning.");
            return true;
        }

        failure = null!;
        return false;
    }

    private static bool TryValidateLaunchReadiness(
        PluginRefreshPublication publication,
        AppState state,
        out CleaningPreflightFailure failure)
    {
        if (string.IsNullOrWhiteSpace(state.XEditExecutablePath))
        {
            failure = CreateFailure(
                CleaningPreflightFailureKind.XEditNotConfigured,
                MissingXEditMessage(state.XEditExecutablePath),
                "Choose the correct xEdit executable in Settings.");
            return true;
        }

        if (!File.Exists(state.XEditExecutablePath))
        {
            failure = CreateFailure(
                CleaningPreflightFailureKind.XEditNotFound,
                MissingXEditMessage(state.XEditExecutablePath),
                "Choose the correct xEdit executable in Settings.");
            return true;
        }

        var plan = publication.DiscoveryPlan!;
        if (plan.Mode == PluginRefreshDiscoveryMode.DirectLoadOrderFile)
        {
            if (string.IsNullOrWhiteSpace(plan.LoadOrderPath))
            {
                failure = CreateFailure(
                    CleaningPreflightFailureKind.LoadOrderNotConfigured,
                    MissingLoadOrderMessage(plan.LoadOrderPath),
                    "Choose the current plugins.txt or loadorder.txt file.");
                return true;
            }

            if (!File.Exists(plan.LoadOrderPath))
            {
                failure = CreateFailure(
                    CleaningPreflightFailureKind.LoadOrderNotFound,
                    MissingLoadOrderMessage(plan.LoadOrderPath),
                    "Choose the current plugins.txt or loadorder.txt file.");
                return true;
            }
        }

        if (plan.Mode != PluginRefreshDiscoveryMode.Mo2LoadOrderFile)
        {
            failure = null!;
            return false;
        }

        if (string.IsNullOrWhiteSpace(state.Mo2ExecutablePath))
        {
            failure = CreateFailure(
                CleaningPreflightFailureKind.Mo2NotConfigured,
                MissingMo2Message(state.Mo2ExecutablePath),
                "Choose ModOrganizer.exe or disable MO2 Mode.");
            return true;
        }

        if (!File.Exists(state.Mo2ExecutablePath))
        {
            failure = CreateFailure(
                CleaningPreflightFailureKind.Mo2NotFound,
                MissingMo2Message(state.Mo2ExecutablePath),
                "Choose ModOrganizer.exe or disable MO2 Mode.");
            return true;
        }

        if (string.IsNullOrWhiteSpace(publication.Configuration.Mo2InstancePath) ||
            !Directory.Exists(publication.Configuration.Mo2InstancePath))
        {
            failure = CreateFailure(
                CleaningPreflightFailureKind.Mo2InstanceMissing,
                "MO2 instance is missing.",
                "Choose the MO2 instance folder or disable MO2 Mode.");
            return true;
        }

        if (string.IsNullOrWhiteSpace(publication.Configuration.SelectedProfile))
        {
            failure = CreateFailure(
                CleaningPreflightFailureKind.Mo2ProfileMissing,
                "MO2 profile is not selected.",
                "Select a game and MO2 profile before cleaning.");
            return true;
        }

        if (string.IsNullOrWhiteSpace(plan.Mo2LoadOrderPath) || !File.Exists(plan.Mo2LoadOrderPath))
        {
            failure = CreateFailure(
                CleaningPreflightFailureKind.Mo2ProfileLoadOrderMissing,
                "MO2 profile load order is missing.",
                "Select a profile with a valid loadorder.txt before cleaning.");
            return true;
        }

        failure = null!;
        return false;
    }

    private static bool TryValidateSelection(
        PluginRefreshPublication publication,
        out CleaningPreflightFailure failure)
    {
        if (publication.Rows.Count == 0)
        {
            failure = CreateFailure(
                CleaningPreflightFailureKind.NoPluginsLoaded,
                "No plugins are available for cleaning.",
                "Refresh plugins for the selected game.");
            return true;
        }

        if (!publication.Rows.Any(row => row.IsSelected && !row.IsSkippedByPolicy))
        {
            failure = CreateFailure(
                CleaningPreflightFailureKind.NoPluginsSelected,
                "No plugins are selected for cleaning.",
                "Select at least one plugin to clean, or check Skip list settings.");
            return true;
        }

        failure = null!;
        return false;
    }

    private static CleaningPreflightFailure CreateFailure(
        CleaningPreflightFailureKind kind,
        string safeMessage,
        string? actionHint = null) =>
        new(kind, safeMessage, actionHint);

    private static string MissingXEditMessage(string? path) =>
        $"{DiagnosticTextFormatter.SafeFileIdentifier("xEdit Path", path, "xEdit executable")} is missing. Choose the correct xEdit executable in Settings.";

    private static string MissingMo2Message(string? path) =>
        $"{DiagnosticTextFormatter.SafeFileIdentifier("MO2 Path", path, "ModOrganizer.exe")} is missing. Choose ModOrganizer.exe or disable MO2 Mode.";

    private static string MissingLoadOrderMessage(string? path) =>
        $"{DiagnosticTextFormatter.SafeFileIdentifier("Load Order File", path, "load order file")} is missing. Choose the current plugins.txt or loadorder.txt file.";
}
