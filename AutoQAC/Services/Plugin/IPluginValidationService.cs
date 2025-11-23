using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.Plugin;

public interface IPluginValidationService
{
    // Extract plugins from load order file
    Task<List<PluginInfo>> GetPluginsFromLoadOrderAsync(
        string loadOrderPath,
        CancellationToken ct = default);

    // Filter plugins by skip list
    List<PluginInfo> FilterSkippedPlugins(
        List<PluginInfo> plugins,
        List<string> skipList);

    // Validate plugin file exists
    bool ValidatePluginExists(PluginInfo plugin);
}
