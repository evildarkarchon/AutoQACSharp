using System.IO;
using System.Linq;
using AutoQAC.Models;
using AutoQAC.Models.Diagnostics;
using AutoQAC.Services.GameCapability;
using AutoQAC.Services.MO2;
using AutoQAC.Services.Plugin;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Evaluates the cheap launch blockers shared by command readiness and full Cleaning preflight.
/// </summary>
internal static class CleaningLaunchBlockerValidator
{
    private const string XEditActionHint = "Choose the correct xEdit executable in Settings.";
    private const string LoadOrderActionHint = "Choose the current plugins.txt or loadorder.txt file.";
    private const string Mo2ActionHint = "Choose ModOrganizer.exe or disable MO2 Mode.";

    internal static bool TryValidateSharedBlockers(
        PluginRefreshPublication publication,
        AppState state,
        out CleaningPreflightFailure failure)
    {
        if (TryValidatePublication(publication, out failure) ||
            TryValidateLaunchReadiness(publication, state, out failure) ||
            TryValidateSelection(publication, out failure))
        {
            return true;
        }

        failure = null!;
        return false;
    }

    internal static bool TryValidatePublication(
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

    internal static bool TryValidateLaunchReadiness(
        PluginRefreshPublication publication,
        AppState state,
        out CleaningPreflightFailure failure)
    {
        if (string.IsNullOrWhiteSpace(state.XEditExecutablePath))
        {
            failure = CreateFailure(
                CleaningPreflightFailureKind.XEditNotConfigured,
                MissingXEditMessage(state.XEditExecutablePath),
                XEditActionHint);
            return true;
        }

        if (!File.Exists(state.XEditExecutablePath))
        {
            failure = CreateFailure(
                CleaningPreflightFailureKind.XEditNotFound,
                MissingXEditMessage(state.XEditExecutablePath),
                XEditActionHint);
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
                    LoadOrderActionHint);
                return true;
            }

            if (!File.Exists(plan.LoadOrderPath))
            {
                failure = CreateFailure(
                    CleaningPreflightFailureKind.LoadOrderNotFound,
                    MissingLoadOrderMessage(plan.LoadOrderPath),
                    LoadOrderActionHint);
                return true;
            }
        }

        if (plan.Mode != PluginRefreshDiscoveryMode.Mo2LoadOrderFile)
        {
            failure = null!;
            return false;
        }

        var mo2Path = state.Mo2ExecutablePath;
        if (string.IsNullOrWhiteSpace(mo2Path))
        {
            failure = CreateFailure(
                CleaningPreflightFailureKind.Mo2NotConfigured,
                MissingMo2Message(mo2Path),
                Mo2ActionHint);
            return true;
        }

        if (!Mo2ExecutablePathValidator.IsValidExecutablePath(mo2Path))
        {
            failure = CreateMo2NotFoundFailure(mo2Path);
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

    internal static bool TryValidateSelection(
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

    internal static CleaningPreflightFailure CreateMo2NotFoundFailure(string? path) =>
        CreateFailure(
            CleaningPreflightFailureKind.Mo2NotFound,
            MissingMo2Message(path),
            Mo2ActionHint);

    private static CleaningPreflightFailure CreateFailure(
        CleaningPreflightFailureKind kind,
        string safeMessage,
        string? actionHint = null) =>
        new(kind, safeMessage, actionHint);

    private static string MissingXEditMessage(string? path) =>
        $"{DiagnosticTextFormatter.SafeFileIdentifier("xEdit Path", path, "xEdit executable")} is missing. {XEditActionHint}";

    private static string MissingMo2Message(string? path) =>
        $"{DiagnosticTextFormatter.SafeFileIdentifier("MO2 Path", path, "ModOrganizer.exe")} is missing. {Mo2ActionHint}";

    private static string MissingLoadOrderMessage(string? path) =>
        $"{DiagnosticTextFormatter.SafeFileIdentifier("Load Order File", path, "load order file")} is missing. {LoadOrderActionHint}";
}
