using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using AutoQAC.Models;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Plugin;

/// <summary>
/// Compatibility adapter between Plugin refresh publications and the legacy AppState row surface.
/// </summary>
internal sealed class PluginRefreshAppStateMirror
{
    private readonly IStateService _stateService;

    /// <summary>
    /// Initializes an AppState mirror backed by the shared state service.
    /// </summary>
    /// <param name="stateService">Shared runtime state service.</param>
    internal PluginRefreshAppStateMirror(IStateService stateService)
    {
        _stateService = stateService;
    }

    /// <summary>
    /// Gets the latest shared AppState.
    /// </summary>
    internal AppState CurrentState => _stateService.CurrentState;

    /// <summary>
    /// Creates a configuration projection from compatibility AppState fields.
    /// </summary>
    /// <param name="state">State to project.</param>
    /// <returns>A Plugin refresh configuration projection.</returns>
    internal static PluginRefreshConfigurationProjection CreateConfigurationProjection(AppState state) =>
        new(
            LoadOrderPath: state.LoadOrderPath,
            GameDataFolder: null,
            HasGameDataFolderOverride: false,
            XEditPath: state.XEditExecutablePath,
            Mo2Path: state.Mo2ExecutablePath,
            Mo2ModeEnabled: state.Mo2ModeEnabled,
            Mo2InstancePath: null,
            IsMo2InstanceOverride: false,
            IsMo2InstanceValid: null,
            AvailableProfiles: [],
            SelectedProfile: state.Mo2Profile,
            CleaningTimeout: state.CleaningTimeout);

    /// <summary>
    /// Projects visible rows from legacy AppState facts.
    /// </summary>
    /// <param name="state">State containing compatibility plugin rows and exclusions.</param>
    /// <returns>Visible Plugin refresh rows.</returns>
    internal static IReadOnlyList<PluginRefreshRow> ProjectVisibleRows(AppState state) =>
        PluginRefreshPublicationRows.ProjectStateVisibleRows(
            state.PluginsToClean,
            state.ExcludedPluginPaths);

    /// <summary>
    /// Clears compatibility rows when a refresh switches to a different game.
    /// </summary>
    /// <param name="gameType">Game that is about to become current.</param>
    internal void ClearRowsForRefreshStart(GameType gameType)
    {
        // Start/Preview gates read AppState, so clear stale rows before async discovery can be canceled.
        _stateService.UpdateState(state => state with
        {
            CurrentGameType = gameType,
            PluginsToClean = Array.Empty<PluginInfo>().ToList().AsReadOnly(),
            ExcludedPluginPaths = Array.Empty<string>().ToFrozenSet(StringComparer.OrdinalIgnoreCase)
        });
    }

    /// <summary>
    /// Clears compatibility plugin rows without changing other runtime state.
    /// </summary>
    internal void ClearRows()
    {
        _stateService.SetPluginsToClean([]);
    }

    /// <summary>
    /// Mirrors accepted publication rows into AppState for legacy cleaning compatibility.
    /// </summary>
    /// <param name="mirror">Rows and selection exclusions derived from a publication.</param>
    internal void MirrorRows(PluginRefreshPublicationRowsMirror mirror)
    {
        if (_stateService.CurrentState.IsCleaning)
        {
            // Cleaning sessions run from their own accepted row copy; mirroring during cleaning could
            // rewrite the in-flight session's AppState rows underneath the sequential runner.
            return;
        }

        _stateService.SetPluginsToClean(mirror.PluginsToClean.ToList());
        _stateService.UpdateExcludedPlugins(_ => mirror.ExcludedPluginPaths);
    }

    /// <summary>
    /// Applies selection changes to AppState when no accepted publication is available.
    /// </summary>
    /// <param name="visibleRows">Visible rows from the current snapshot.</param>
    /// <param name="change">Requested selection change.</param>
    /// <returns>True when the selection request addressed at least one visible row.</returns>
    internal bool ApplyStateSelectionChange(
        IReadOnlyList<PluginRefreshRow> visibleRows,
        PluginSelectionChange change)
    {
        var targetFound = false;
        _stateService.UpdateExcludedPlugins(current =>
        {
            var result = PluginRefreshPublicationRows.ApplyStateSelectionChange(
                visibleRows,
                current,
                change);
            targetFound = result.WasTargetFound;
            return result.ExcludedPluginPaths;
        });

        return targetFound;
    }

}
