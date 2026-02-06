using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.Plugin;

public interface IPluginValidationService
{
    /// <summary>
    /// Extract plugins from a load order file with encoding auto-detection and line validation.
    /// </summary>
    /// <param name="loadOrderPath">Path to the load order file (e.g., plugins.txt).</param>
    /// <param name="dataFolderPath">Optional game data folder path for resolving FullPath. If null, FullPath equals FileName.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of validated plugins parsed from the file.</returns>
    Task<List<PluginInfo>> GetPluginsFromLoadOrderAsync(
        string loadOrderPath,
        string? dataFolderPath = null,
        CancellationToken ct = default);

    /// <summary>
    /// Filter plugins by skip list, marking IsInSkipList on matching entries.
    /// </summary>
    List<PluginInfo> FilterSkippedPlugins(
        List<PluginInfo> plugins,
        List<string> skipList);

    /// <summary>
    /// Validate that a plugin file exists on disk, is non-zero, and is readable.
    /// Replaces the old ValidatePluginExists which had a dual code path for rooted vs non-rooted paths.
    /// </summary>
    /// <param name="plugin">The plugin to validate.</param>
    /// <returns>A PluginWarningKind indicating the validation result.</returns>
    PluginWarningKind ValidatePluginFile(PluginInfo plugin);
}
