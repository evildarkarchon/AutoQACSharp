using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.MO2;

public interface IMo2InstanceService
{
    Task<Mo2InstanceInfo?> ResolveInstanceAsync(
        GameType game,
        string? mo2BinaryPath,
        string? overrideBaseDir,
        CancellationToken ct = default);

    IReadOnlyList<string> GetProfiles(Mo2InstanceInfo instance);

    string? ChooseProfile(Mo2InstanceInfo instance, IReadOnlyList<string> profiles, string? persistedProfile);

    string? GetLoadOrderPath(Mo2InstanceInfo instance, string profile);

    IReadOnlyDictionary<string, string> BuildPluginPathMap(
        Mo2InstanceInfo instance,
        string profile,
        string? gameDataFolder);
}
