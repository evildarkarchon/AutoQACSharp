using System.Security.Cryptography;
using System.Text;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Configuration;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AutoQAC.Tests.Services.Configuration.Fakes;

internal sealed class FakeUserConfigFileStore : IUserConfigFileStore
{
    private readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .Build();

    public Exception? WriteFailure { get; set; }

    public Exception? ReadFailure { get; set; }

    public bool FileMissing { get; set; }

    public string? CurrentContent { get; set; }

    public string? CurrentHash { get; set; }

    public List<string> CallLog { get; } = [];

    public int WriteCount => CallLog.Count(c => c == "Write");

    public string SettingsFilePath { get; } = Path.Combine(Path.GetTempPath(), "AutoQAC-tests", Guid.NewGuid().ToString(), "AutoQAC Settings.yaml");

    public Task<UserConfigReadResult> ReadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        CallLog.Add("Read");
        if (ReadFailure != null)
        {
            throw ReadFailure;
        }

        if (FileMissing)
        {
            return Task.FromResult(new UserConfigReadResult(false, null, null));
        }

        var content = CurrentContent ?? _serializer.Serialize(new UserConfiguration());
        var hash = CurrentHash ?? ComputeHash(content);
        return Task.FromResult(new UserConfigReadResult(true, content, hash));
    }

    public Task<string> WriteAsync(UserConfiguration config, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        CallLog.Add("Write");
        if (WriteFailure != null)
        {
            throw WriteFailure;
        }

        CurrentContent = _serializer.Serialize(config);
        CurrentHash = ComputeHash(CurrentContent);
        return Task.FromResult(CurrentHash);
    }

    public Task<string?> ComputeHashAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        CallLog.Add("Hash");
        return Task.FromResult(FileMissing ? null : CurrentHash ?? (CurrentContent == null ? null : ComputeHash(CurrentContent)));
    }

    public void SimulateWriteSequence(params string[] entries) => CallLog.AddRange(entries);

    public static string ComputeHash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
