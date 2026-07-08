using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models.Configuration;

namespace AutoQAC.Services.Configuration;

internal interface IUserConfigFileStore
{
    Task<UserConfigReadResult> ReadAsync(CancellationToken ct);

    Task<string> WriteAsync(UserConfiguration config, CancellationToken ct);

    Task<string?> ComputeHashAsync(CancellationToken ct);

    string SettingsFilePath { get; }
}

internal sealed record UserConfigReadResult(bool Exists, string? Content, string? Hash);
