using AutoQAC.Models.Diagnostics;
using FluentAssertions;

namespace AutoQAC.Tests.Models;

/// <summary>
/// Locks the Phase 11 shared diagnostic text boundary before the formatter implementation exists.
/// </summary>
public sealed class DiagnosticTextFormatterTests
{
    private const string CleaningFallback = "Cleaning failed. See the latest AutoQAC log.";

    private static readonly string[] UnsafeDetails =
    [
        @"C:\Users\Alice\Tools\SSEEdit.exe",
        @"D:\Games\Skyrim Special Edition\Data\Plugin.esp",
        @"J:\ModOrganizer\ModOrganizer.exe",
        "J:/Tools/SSEEdit.exe",
        @"\\server\share\xEdit.exe",
        "-QAC",
        "-qac",
        "-autoload",
        ".exe\"",
        "UnauthorizedAccessException: access denied",
        "System.InvalidOperationException: boom at AutoQAC.Services.Cleaning"
    ];

    [Fact]
    public void OperationFailed_Cleaning_ReturnsUiSpecCopy()
    {
        var message = DiagnosticTextFormatter.OperationFailed("Cleaning");

        message.Should().Be("Cleaning failed. See the latest AutoQAC log for technical details.");
    }

    [Fact]
    public void OperationFailed_WhenOperationIsBlank_UsesFallbackOperation()
    {
        var message = DiagnosticTextFormatter.OperationFailed("  ");

        message.Should().Be("Operation failed. See the latest AutoQAC log for technical details.");
    }

    [Fact]
    public void SafeFileIdentifier_XEditPath_ReturnsLabelAndSanitizedBasename()
    {
        var identifier = DiagnosticTextFormatter.SafeFileIdentifier(
            "xEdit Path",
            @"C:\Users\Alice\Tools\SSEEdit.exe",
            "xEdit executable");

        identifier.Should().Be("xEdit Path (SSEEdit.exe)");
    }

    [Fact]
    public void SafeFileIdentifier_LoadOrderFile_ReturnsLabelAndSanitizedBasename()
    {
        var identifier = DiagnosticTextFormatter.SafeFileIdentifier(
            "Load Order File",
            @"C:\Users\Alice\AppData\plugins.txt",
            "load order file");

        identifier.Should().Be("Load Order File (plugins.txt)");
    }

    [Fact]
    public void SafeFolderIssue_GameDisplayName_ReturnsUiSpecCopy()
    {
        var message = DiagnosticTextFormatter.SafeFolderIssue("Skyrim SE");

        message.Should().Be("Skyrim SE data folder is unavailable. Choose a valid Data folder or reset the override.");
    }

    [Fact]
    public void SafePluginName_WhenNameContainsControlAndQuoteCharacters_RemovesUnsafeCharacters()
    {
        var pluginName = DiagnosticTextFormatter.SafePluginName("Bad\n\"Plugin`.esp");

        pluginName.Should().Be("BadPlugin.esp");
        pluginName.Should().EndWith(".esp");
        pluginName.Should().NotContain("\n");
        pluginName.Should().NotContain("\"");
        pluginName.Should().NotContain("`");
    }

    [Fact]
    public void CleaningFailedForPlugin_PluginName_ReturnsUiSpecCopy()
    {
        var message = DiagnosticTextFormatter.CleaningFailedForPlugin("Plugin.esp");

        message.Should().Be("Plugin.esp: Cleaning failed. See the latest AutoQAC log.");
    }

    [Fact]
    public void XEditReportedError_PluginName_ReturnsUiSpecCopy()
    {
        var message = DiagnosticTextFormatter.XEditReportedError("Plugin.esp");

        message.Should().Be("xEdit reported an error for Plugin.esp. See the latest AutoQAC log.");
    }

    [Fact]
    public void SafeFailureSummary_WhenCandidateContainsUnsafeDetails_ReturnsFallback()
    {
        var message = DiagnosticTextFormatter.SafeFailureSummary(
            @"System.InvalidOperationException at C:\Users\Alice\xEdit.exe -QAC",
            CleaningFallback);

        message.Should().Be(CleaningFallback);
        message.Should().NotContain("System.InvalidOperationException");
        message.Should().NotContain(@"C:\Users\Alice");
        message.Should().NotContain("-QAC");
    }

    [Fact]
    public void SafeFailureSummary_WhenCandidateContainsNonCDriveWindowsPath_ReturnsFallback()
    {
        var message = DiagnosticTextFormatter.SafeFailureSummary(
            @"IOException at J:\ModOrganizer\mods\Plugin.esp -autoload",
            CleaningFallback);

        message.Should().Be(CleaningFallback);
        message.Should().NotContain(@"J:\ModOrganizer");
        message.Should().NotContain("Plugin.esp -autoload");
        message.Should().NotContain("-autoload");
    }

    [Fact]
    public void SafeFailureSummary_WhenCandidateContainsForwardSlashWindowsPath_ReturnsFallback()
    {
        var message = DiagnosticTextFormatter.SafeFailureSummary(
            "IOException at J:/Tools/SSEEdit.exe",
            CleaningFallback);

        message.Should().Be(CleaningFallback);
        message.Should().NotContain("J:/Tools");
        message.Should().NotContain("SSEEdit.exe");
    }

    [Fact]
    public void SafeFailureSummary_WhenCandidateContainsLowercaseCommandFlag_ReturnsFallback()
    {
        var message = DiagnosticTextFormatter.SafeFailureSummary(
            "xedit exited after -qac command processing",
            CleaningFallback);

        message.Should().Be(CleaningFallback);
        message.Should().NotContain("-qac");
    }

    [Theory]
    [MemberData(nameof(UnsafeDetailCases))]
    public void SafeFailureSummary_WhenCandidateContainsKnownUnsafeSentinel_ReturnsFallback(string unsafeDetail)
    {
        var message = DiagnosticTextFormatter.SafeFailureSummary($"Failure detail: {unsafeDetail}", CleaningFallback);

        message.Should().Be(CleaningFallback);
        message.IndexOf(unsafeDetail, StringComparison.OrdinalIgnoreCase).Should().Be(-1);
    }

    [Theory]
    [InlineData(@"\\server\share\xEdit.exe failed")]
    [InlineData("J:/Tools/SSEEdit.exe failed")]
    [InlineData("xEdit exited from SSEEdit.exe\"")]
    [InlineData("xEdit exited from SSEEdit.exe\n")]
    [InlineData("xEdit exited from SSEEdit.exe")]
    [InlineData("UnauthorizedAccessException: access denied")]
    [InlineData("IOException: access denied")]
    [InlineData("Exception: boom")]
    [InlineData("boom at AutoQAC.Services.Cleaning.CleaningService")]
    [InlineData("   at AutoQAC.Services.Cleaning.CleaningService.Start()")]
    public void SafeFailureSummary_WhenCandidateMatchesUnsafeCaseInsensitivePatterns_ReturnsFallback(
        string unsafeCandidate)
    {
        var message = DiagnosticTextFormatter.SafeFailureSummary(unsafeCandidate, CleaningFallback);

        message.Should().Be(CleaningFallback);
    }

    [Fact]
    public void SafeFailureSummary_WhenCandidateIsSafe_ReturnsTrimmedCandidate()
    {
        var message =
            DiagnosticTextFormatter.SafeFailureSummary("  Cleaning timed out after 2 attempts.  ", CleaningFallback);

        message.Should().Be("Cleaning timed out after 2 attempts.");
    }

    public static TheoryData<string> UnsafeDetailCases()
    {
        var data = new TheoryData<string>();
        foreach (var unsafeDetail in UnsafeDetails)
        {
            data.Add(unsafeDetail);
        }

        return data;
    }
}
