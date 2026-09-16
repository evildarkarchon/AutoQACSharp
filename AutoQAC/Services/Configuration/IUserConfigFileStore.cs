using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models.Configuration;

namespace AutoQAC.Services.Configuration;

internal interface IUserConfigFileStore
{
    string SettingsFilePath { get; }
    Task<UserConfigReadResult> ReadAsync(CancellationToken ct);

    Task<string> WriteAsync(UserConfiguration config, CancellationToken ct);

    Task<string?> ComputeHashAsync(CancellationToken ct);
}

internal sealed record UserConfigReadResult(bool Exists, string? Content, string? Hash);