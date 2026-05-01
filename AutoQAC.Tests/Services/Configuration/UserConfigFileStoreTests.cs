using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Configuration;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services.Configuration;

public sealed class UserConfigFileStoreTests : IDisposable
{
    private readonly string _testDirectory;

    public UserConfigFileStoreTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "AutoQACUserConfigFileStoreTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_testDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(_testDirectory, true);
        }
        catch
        {
            // Test cleanup is best-effort because Windows file handles can briefly lag after assertions.
        }
    }

    [Fact]
    public async Task WriteAsync_NewFile_CreatesViaTempThenMove()
    {
        var moved = false;
        var store = new UserConfigFileStore(
            Substitute.For<ILoggingService>(),
            _testDirectory,
            File.Replace,
            (source, destination) =>
            {
                moved = true;
                File.Move(source, destination, overwrite: false);
            });

        await store.WriteAsync(NewConfig(123), CancellationToken.None);

        File.Exists(store.SettingsFilePath).Should().BeTrue();
        File.ReadAllText(store.SettingsFilePath).Should().Contain("Cleaning_Timeout: 123");
        Directory.EnumerateFiles(_testDirectory, "*.tmp").Should().BeEmpty();
        moved.Should().BeTrue("new settings files should be created by moving the temp file into place");
    }

    [Fact]
    public async Task WriteAsync_ExistingFile_UsesReplace()
    {
        var replaced = false;
        var path = Path.Combine(_testDirectory, "AutoQAC Settings.yaml");
        await File.WriteAllTextAsync(path, "Selected_Game: SkyrimSe");
        var store = new UserConfigFileStore(
            Substitute.For<ILoggingService>(),
            _testDirectory,
            (source, destination, backup) =>
            {
                replaced = true;
                File.Replace(source, destination, backup);
            },
            (source, destination) => File.Move(source, destination, overwrite: false));

        await store.WriteAsync(NewConfig(456), CancellationToken.None);

        File.ReadAllText(path).Should().Contain("Cleaning_Timeout: 456");
        Directory.EnumerateFiles(_testDirectory, "*.tmp").Should().BeEmpty();
        replaced.Should().BeTrue("existing settings files should use File.Replace semantics");
    }

    [Fact]
    public async Task WriteAsync_ReplaceThrows_RethrowsAndCleansTemp()
    {
        var expected = new IOException("locked");
        var path = Path.Combine(_testDirectory, "AutoQAC Settings.yaml");
        await File.WriteAllTextAsync(path, "Selected_Game: SkyrimSe");
        var store = new UserConfigFileStore(
            Substitute.For<ILoggingService>(),
            _testDirectory,
            (_, _, _) => throw expected,
            (source, destination) => File.Move(source, destination, overwrite: false));

        var thrown = await FluentActions.Awaiting(() => store.WriteAsync(NewConfig(789), CancellationToken.None))
            .Should().ThrowAsync<IOException>();

        thrown.Which.Should().BeSameAs(expected);
        Directory.EnumerateFiles(_testDirectory, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAsync_CanceledWrite_DoesNotLeaveTempFile()
    {
        var store = new UserConfigFileStore(Substitute.For<ILoggingService>(), _testDirectory);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await FluentActions.Awaiting(() => store.WriteAsync(NewConfig(321), cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        Directory.EnumerateFiles(_testDirectory, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_FileMissing_ReturnsExistsFalse()
    {
        var store = new UserConfigFileStore(Substitute.For<ILoggingService>(), _testDirectory);

        var result = await store.ReadAsync(CancellationToken.None);

        result.Exists.Should().BeFalse();
        result.Content.Should().BeNull();
        result.Hash.Should().BeNull();
    }

    [Fact]
    public async Task ReadAsync_FileExists_ReturnsContentAndHash()
    {
        var path = Path.Combine(_testDirectory, "AutoQAC Settings.yaml");
        await File.WriteAllTextAsync(path, "Selected_Game: SkyrimSe");
        var store = new UserConfigFileStore(Substitute.For<ILoggingService>(), _testDirectory);

        var result = await store.ReadAsync(CancellationToken.None);

        result.Exists.Should().BeTrue();
        result.Content.Should().Be("Selected_Game: SkyrimSe");
        result.Hash.Should().MatchRegex("^[0-9A-F]{64}$");
    }

    private static UserConfiguration NewConfig(int timeout) => new()
    {
        SelectedGame = "SkyrimSe",
        Settings = new AutoQacSettings { CleaningTimeout = timeout }
    };
}
