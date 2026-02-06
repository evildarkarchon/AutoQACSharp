using System.Text;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Plugin;
using FluentAssertions;
using Moq;

namespace AutoQAC.Tests.Services;

public sealed class PluginValidationServiceTests : IDisposable
{
    private readonly PluginValidationService _sut;
    private readonly List<string> _tempFiles = new();

    public PluginValidationServiceTests()
    {
        _sut = new PluginValidationService(Mock.Of<ILoggingService>());
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch { /* cleanup best-effort */ }
        }
    }

    private string CreateTempFile(string content)
    {
        var path = Path.GetTempFileName();
        _tempFiles.Add(path);
        File.WriteAllText(path, content);
        return path;
    }

    private string CreateTempFileWithBytes(byte[] bytes)
    {
        var path = Path.GetTempFileName();
        _tempFiles.Add(path);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    #region Basic Parsing (existing behavior preserved)

    [Fact]
    public async Task GetPluginsFromLoadOrder_ShouldParseFileCorrectly()
    {
        // Arrange
        var content = "# This is a comment\n*Skyrim.esm\nUpdate.esm\n*Dawnguard.esm\nHearthFires.esm\n";
        var tempFile = CreateTempFile(content);

        // Act
        var plugins = await _sut.GetPluginsFromLoadOrderAsync(tempFile);

        // Assert
        plugins.Should().HaveCount(4);
        plugins[0].FileName.Should().Be("Skyrim.esm");
        plugins[1].FileName.Should().Be("Update.esm");
        plugins[2].FileName.Should().Be("Dawnguard.esm");
        plugins[3].FileName.Should().Be("HearthFires.esm");
    }

    [Fact]
    public void FilterSkippedPlugins_ShouldMarkSkippedCorrectly()
    {
        // Arrange
        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Skyrim.esm", FullPath = "Skyrim.esm", DetectedGameType = GameType.Unknown, IsInSkipList = false },
            new() { FileName = "Update.esm", FullPath = "Update.esm", DetectedGameType = GameType.Unknown, IsInSkipList = false },
            new() { FileName = "Mod.esp", FullPath = "Mod.esp", DetectedGameType = GameType.Unknown, IsInSkipList = false }
        };

        var skipList = new List<string> { "Skyrim.esm", "Update.esm" };

        // Act
        var result = _sut.FilterSkippedPlugins(plugins, skipList);

        // Assert
        result.Should().HaveCount(3);
        result.Single(p => p.FileName == "Skyrim.esm").IsInSkipList.Should().BeTrue();
        result.Single(p => p.FileName == "Update.esm").IsInSkipList.Should().BeTrue();
        result.Single(p => p.FileName == "Mod.esp").IsInSkipList.Should().BeFalse();
    }

    #endregion

    #region BOM Handling

    [Fact]
    public async Task GetPluginsFromLoadOrder_ShouldHandleUtf8Bom()
    {
        // Arrange: UTF-8 BOM (EF BB BF) followed by plugin name
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var content = Encoding.UTF8.GetBytes("Skyrim.esm\nUpdate.esm\n");
        var bytes = new byte[bom.Length + content.Length];
        Buffer.BlockCopy(bom, 0, bytes, 0, bom.Length);
        Buffer.BlockCopy(content, 0, bytes, bom.Length, content.Length);
        var tempFile = CreateTempFileWithBytes(bytes);

        // Act
        var plugins = await _sut.GetPluginsFromLoadOrderAsync(tempFile);

        // Assert: BOM bytes must NOT appear in the first plugin's FileName
        plugins.Should().HaveCount(2);
        plugins[0].FileName.Should().Be("Skyrim.esm", "BOM bytes should be stripped");
        plugins[1].FileName.Should().Be("Update.esm");
    }

    [Fact]
    public async Task GetPluginsFromLoadOrder_ShouldHandleUtf16LeBom()
    {
        // Arrange: Write content as UTF-16 LE (with BOM)
        var content = "Skyrim.esm\nUpdate.esm\n";
        var bytes = Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes(content))
            .ToArray();
        var tempFile = CreateTempFileWithBytes(bytes);

        // Act
        var plugins = await _sut.GetPluginsFromLoadOrderAsync(tempFile);

        // Assert
        plugins.Should().HaveCount(2);
        plugins[0].FileName.Should().Be("Skyrim.esm");
        plugins[1].FileName.Should().Be("Update.esm");
    }

    #endregion

    #region Blank Lines and Whitespace

    [Fact]
    public async Task GetPluginsFromLoadOrder_ShouldSkipBlankLinesAndWhitespace()
    {
        // Arrange
        var content = "\n\n  \n*Skyrim.esm\n\nUpdate.esm\n  \n";
        var tempFile = CreateTempFile(content);

        // Act
        var plugins = await _sut.GetPluginsFromLoadOrderAsync(tempFile);

        // Assert: Only 2 valid plugins, blanks/whitespace skipped
        plugins.Should().HaveCount(2);
        plugins[0].FileName.Should().Be("Skyrim.esm");
        plugins[1].FileName.Should().Be("Update.esm");
    }

    #endregion

    #region Comment Lines

    [Fact]
    public async Task GetPluginsFromLoadOrder_ShouldSkipCommentLines()
    {
        // Arrange
        var content = "# comment\n  # indented comment\nSkyrim.esm\n";
        var tempFile = CreateTempFile(content);

        // Act
        var plugins = await _sut.GetPluginsFromLoadOrderAsync(tempFile);

        // Assert
        plugins.Should().HaveCount(1);
        plugins[0].FileName.Should().Be("Skyrim.esm");
    }

    #endregion

    #region MO2 Separator Lines and Prefix Stripping

    [Fact]
    public async Task GetPluginsFromLoadOrder_ShouldStripPrefixesAndSkipSeparators()
    {
        // Arrange: * prefix for enabled, _separator_ has no valid extension (skip),
        // +SomeSep and -DisabledMod also lack valid extensions (skip)
        var content = "*Skyrim.esm\n*_separator_\n+SomeSep\n-DisabledMod\nUpdate.esm\n";
        var tempFile = CreateTempFile(content);

        // Act
        var plugins = await _sut.GetPluginsFromLoadOrderAsync(tempFile);

        // Assert: Only Skyrim.esm and Update.esm -- separator and non-plugin entries skipped
        plugins.Should().HaveCount(2);
        plugins[0].FileName.Should().Be("Skyrim.esm");
        plugins[1].FileName.Should().Be("Update.esm");
    }

    #endregion

    #region Invalid Extensions

    [Fact]
    public async Task GetPluginsFromLoadOrder_ShouldSkipInvalidExtensions()
    {
        // Arrange
        var content = "Skyrim.esm\nreadme.txt\nnotes.md\nMod.esp\n";
        var tempFile = CreateTempFile(content);

        // Act
        var plugins = await _sut.GetPluginsFromLoadOrderAsync(tempFile);

        // Assert: Only .esm and .esp, not .txt or .md
        plugins.Should().HaveCount(2);
        plugins[0].FileName.Should().Be("Skyrim.esm");
        plugins[1].FileName.Should().Be("Mod.esp");
    }

    [Fact]
    public async Task GetPluginsFromLoadOrder_ShouldAcceptEslExtension()
    {
        // Arrange
        var content = "Skyrim.esm\nLightMod.esl\nMod.esp\n";
        var tempFile = CreateTempFile(content);

        // Act
        var plugins = await _sut.GetPluginsFromLoadOrderAsync(tempFile);

        // Assert: .esl is valid
        plugins.Should().HaveCount(3);
        plugins[1].FileName.Should().Be("LightMod.esl");
    }

    #endregion

    #region Path Separators (Malformed Entries)

    [Fact]
    public async Task GetPluginsFromLoadOrder_ShouldSkipEntriesWithPathSeparators()
    {
        // Arrange: Entry with backslash is malformed (it's a path, not a plugin name)
        var content = "Skyrim.esm\nC:\\Data\\Mod.esp\nUpdate.esm\n";
        var tempFile = CreateTempFile(content);

        // Act
        var plugins = await _sut.GetPluginsFromLoadOrderAsync(tempFile);

        // Assert: Entry with backslash skipped as malformed
        plugins.Should().HaveCount(2);
        plugins[0].FileName.Should().Be("Skyrim.esm");
        plugins[1].FileName.Should().Be("Update.esm");
    }

    [Fact]
    public async Task GetPluginsFromLoadOrder_ShouldSkipEntriesWithForwardSlash()
    {
        // Arrange
        var content = "Skyrim.esm\nmods/Mod.esp\nUpdate.esm\n";
        var tempFile = CreateTempFile(content);

        // Act
        var plugins = await _sut.GetPluginsFromLoadOrderAsync(tempFile);

        // Assert
        plugins.Should().HaveCount(2);
        plugins[0].FileName.Should().Be("Skyrim.esm");
        plugins[1].FileName.Should().Be("Update.esm");
    }

    #endregion

    #region Control Characters

    [Fact]
    public async Task GetPluginsFromLoadOrder_ShouldSkipEntriesWithControlCharacters()
    {
        // Arrange: Line with null byte embedded
        var content = "Skyrim.esm\n\0BadPlugin.esp\nUpdate.esm\n";
        var tempFile = CreateTempFile(content);

        // Act
        var plugins = await _sut.GetPluginsFromLoadOrderAsync(tempFile);

        // Assert: Line with null byte skipped
        plugins.Should().HaveCount(2);
        plugins[0].FileName.Should().Be("Skyrim.esm");
        plugins[1].FileName.Should().Be("Update.esm");
    }

    #endregion

    #region FullPath Resolution

    [Fact]
    public async Task GetPluginsFromLoadOrder_ShouldResolveFullPath_WhenDataFolderProvided()
    {
        // Arrange
        var content = "Skyrim.esm\nUpdate.esm\n";
        var tempFile = CreateTempFile(content);
        var dataFolder = @"C:\Games\SkyrimSE\Data";

        // Act
        var plugins = await _sut.GetPluginsFromLoadOrderAsync(tempFile, dataFolder);

        // Assert: FullPath should be absolute using dataFolderPath
        plugins.Should().HaveCount(2);
        plugins[0].FullPath.Should().Be(Path.Combine(dataFolder, "Skyrim.esm"));
        plugins[1].FullPath.Should().Be(Path.Combine(dataFolder, "Update.esm"));
    }

    [Fact]
    public async Task GetPluginsFromLoadOrder_ShouldUseFileNameAsFullPath_WhenNoDataFolder()
    {
        // Arrange
        var content = "Skyrim.esm\n";
        var tempFile = CreateTempFile(content);

        // Act: No dataFolderPath
        var plugins = await _sut.GetPluginsFromLoadOrderAsync(tempFile);

        // Assert: FullPath falls back to FileName (backward compat)
        plugins.Should().HaveCount(1);
        plugins[0].FullPath.Should().Be("Skyrim.esm");
    }

    #endregion

    #region Empty/Missing File

    [Fact]
    public async Task GetPluginsFromLoadOrder_ShouldReturnEmptyList_ForEmptyPath()
    {
        // Act
        var plugins = await _sut.GetPluginsFromLoadOrderAsync("");

        // Assert
        plugins.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPluginsFromLoadOrder_ShouldReturnEmptyList_ForNullPath()
    {
        // Act
        var plugins = await _sut.GetPluginsFromLoadOrderAsync(null!);

        // Assert
        plugins.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPluginsFromLoadOrder_ShouldReturnEmptyList_ForMissingFile()
    {
        // Act
        var plugins = await _sut.GetPluginsFromLoadOrderAsync(@"C:\nonexistent\plugins.txt");

        // Assert: Should return empty, not throw
        plugins.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPluginsFromLoadOrder_ShouldReturnEmptyList_ForEmptyFile()
    {
        // Arrange
        var tempFile = CreateTempFile("");

        // Act
        var plugins = await _sut.GetPluginsFromLoadOrderAsync(tempFile);

        // Assert
        plugins.Should().BeEmpty();
    }

    #endregion

    #region ValidatePluginFile

    [Fact]
    public void ValidatePluginFile_ShouldReturnNone_ForExistingReadableFile()
    {
        // Arrange: Create a temp file with some content
        var tempFile = CreateTempFile("dummy plugin content");
        var plugin = new PluginInfo { FileName = "Test.esp", FullPath = tempFile };

        // Act
        var result = _sut.ValidatePluginFile(plugin);

        // Assert
        result.Should().Be(PluginWarningKind.None);
    }

    [Fact]
    public void ValidatePluginFile_ShouldReturnNotFound_ForMissingFile()
    {
        // Arrange
        var plugin = new PluginInfo
        {
            FileName = "Missing.esp",
            FullPath = Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid() + ".esp")
        };

        // Act
        var result = _sut.ValidatePluginFile(plugin);

        // Assert
        result.Should().Be(PluginWarningKind.NotFound);
    }

    [Fact]
    public void ValidatePluginFile_ShouldReturnZeroByte_ForEmptyFile()
    {
        // Arrange
        var tempFile = CreateTempFile(""); // zero-byte file (GetTempFileName creates empty file, WriteAllText with "" keeps it empty)
        // Need to ensure it's truly zero bytes
        File.WriteAllBytes(tempFile, Array.Empty<byte>());
        var plugin = new PluginInfo { FileName = "Empty.esp", FullPath = tempFile };

        // Act
        var result = _sut.ValidatePluginFile(plugin);

        // Assert
        result.Should().Be(PluginWarningKind.ZeroByte);
    }

    [Fact]
    public void ValidatePluginFile_ShouldReturnNotFound_ForNonRootedPath()
    {
        // Arrange: Relative path can't be validated
        var plugin = new PluginInfo { FileName = "Skyrim.esm", FullPath = "Skyrim.esm" };

        // Act
        var result = _sut.ValidatePluginFile(plugin);

        // Assert: Non-rooted path returns NotFound (can't validate without absolute path)
        result.Should().Be(PluginWarningKind.NotFound);
    }

    [Fact]
    public void ValidatePluginFile_ShouldReturnNotFound_ForEmptyFullPath()
    {
        // Arrange
        var plugin = new PluginInfo { FileName = "Test.esp", FullPath = "" };

        // Act
        var result = _sut.ValidatePluginFile(plugin);

        // Assert
        result.Should().Be(PluginWarningKind.NotFound);
    }

    #endregion

    #region Warning Property on PluginInfo

    [Fact]
    public void PluginInfo_Warning_ShouldDefaultToNone()
    {
        // Arrange & Act
        var plugin = new PluginInfo { FileName = "Test.esp", FullPath = "Test.esp" };

        // Assert
        plugin.Warning.Should().Be(PluginWarningKind.None);
    }

    [Fact]
    public void PluginInfo_Warning_ShouldBeSettableViaInit()
    {
        // Arrange & Act
        var plugin = new PluginInfo
        {
            FileName = "Test.esp",
            FullPath = "Test.esp",
            Warning = PluginWarningKind.NotFound
        };

        // Assert
        plugin.Warning.Should().Be(PluginWarningKind.NotFound);
    }

    #endregion
}
