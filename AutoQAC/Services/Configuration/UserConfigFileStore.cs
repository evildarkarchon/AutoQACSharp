using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models.Configuration;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AutoQAC.Services.Configuration;

internal sealed class UserConfigFileStore : IUserConfigFileStore
{
    private const string UserConfigFile = "AutoQAC Settings.yaml";

    private readonly ILoggingService _logger;
    private readonly string _configDirectory;
    private readonly ISerializer _serializer;
    private readonly Action<string, string, string?> _replace;
    private readonly Action<string, string> _move;

    public UserConfigFileStore(ILoggingService logger, string? configDirectory = null)
        : this(logger, configDirectory, File.Replace, (source, dest) => File.Move(source, dest, overwrite: false))
    {
    }

    internal UserConfigFileStore(
        ILoggingService logger,
        string? configDirectory,
        Action<string, string, string?> replace,
        Action<string, string> move)
    {
        _logger = logger;
        _configDirectory = configDirectory ?? ResolveConfigDirectory(logger);
        _replace = replace;
        _move = move;
        _serializer = new SerializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();
    }

    public string SettingsFilePath => Path.Combine(_configDirectory, UserConfigFile);

    /// <summary>
    /// Reads the settings YAML text and canonical content hash, returning Exists=false for a missing file.
    /// </summary>
    public async Task<UserConfigReadResult> ReadAsync(CancellationToken ct)
    {
        var path = SettingsFilePath;
        if (!File.Exists(path))
        {
            return new UserConfigReadResult(false, null, null);
        }

        var content = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return new UserConfigReadResult(true, content, ComputeContentHash(content));
    }

    /// <summary>
    /// Writes settings through a same-directory temp file before replacing or moving into place.
    /// </summary>
    public async Task<string> WriteAsync(UserConfiguration config, CancellationToken ct)
    {
        Directory.CreateDirectory(_configDirectory);
        var settingsPath = SettingsFilePath;
        var directory = Path.GetDirectoryName(settingsPath) ?? _configDirectory;
        var tempPath = Path.Combine(directory, Path.GetRandomFileName() + ".tmp");
        var yaml = _serializer.Serialize(config);

        try
        {
            await File.WriteAllTextAsync(tempPath, yaml, ct).ConfigureAwait(false);
            if (File.Exists(settingsPath))
            {
                _replace(tempPath, settingsPath, null);
            }
            else
            {
                _move(tempPath, settingsPath);
            }
        }
        catch
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (Exception cleanupEx)
            {
                // Temp cleanup is best-effort; preserving the original replace/move exception is more important.
                _logger.Debug("[ConfigPersistence] Failed to delete temp settings file after write failure: {Message}", cleanupEx.Message);
            }

            throw;
        }

        var finalContent = await File.ReadAllTextAsync(settingsPath, ct).ConfigureAwait(false);
        return ComputeContentHash(finalContent);
    }

    /// <summary>
    /// Computes the canonical SHA256 hash for the current settings file, or null when it is missing.
    /// </summary>
    public async Task<string?> ComputeHashAsync(CancellationToken ct)
    {
        var path = SettingsFilePath;
        if (!File.Exists(path))
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return ComputeContentHash(content);
    }

    internal static string ComputeContentHash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static string ResolveConfigDirectory(ILoggingService logger)
    {
        var baseDir = AppContext.BaseDirectory;

#if DEBUG
        var current = new DirectoryInfo(baseDir);
        for (var i = 0; i < 6 && current != null; i++)
        {
            var candidate = Path.Combine(current.FullName, "AutoQAC Data");
            if (Directory.Exists(candidate))
            {
                logger.Information("[Debug] Resolved configuration directory to source: {Candidate}", candidate);
                return candidate;
            }

            current = current.Parent;
        }
#endif

        return Path.Combine(baseDir, "AutoQAC Data");
    }
}
