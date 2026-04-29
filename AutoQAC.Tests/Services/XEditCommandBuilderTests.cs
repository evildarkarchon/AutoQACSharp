using System.Reflection;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.State;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services;

public class XEditCommandBuilderTests
{
    private const string WorstCasePluginName = "Quote\" Résumé 測試 🚀 & | ; ( ) ^ Spaces.esp";

    private readonly IStateService _stateServiceMock;
    private readonly XEditCommandBuilder _sut;

    public XEditCommandBuilderTests()
    {
        _stateServiceMock = Substitute.For<IStateService>();
        _sut = new XEditCommandBuilder(_stateServiceMock);
    }

    [Fact]
    public void BuildCommand_ReturnsNull_WhenXEditPathIsEmpty()
    {
        // Arrange
        _stateServiceMock.CurrentState.Returns(new AppState { XEditExecutablePath = "" });

        // Act
        var result = _sut.BuildCommand(new PluginInfo { FileName = "test.esp", FullPath = "/path/to/test.esp" }, GameType.SkyrimSe);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void BuildCommand_ReturnsNull_WhenGameTypeIsUnknown()
    {
        // Arrange
        _stateServiceMock.CurrentState.Returns(new AppState
        {
            XEditExecutablePath = @"C:\Tools\xEdit.exe",
            Mo2ModeEnabled = false
        });
        var plugin = CreatePlugin("test.esp");

        // Act
        var result = _sut.BuildCommand(plugin, GameType.Unknown);

        // Assert
        result.Should().BeNull("GameType.Unknown must be rejected to prevent building commands without game flags");
    }

    [Fact]
    public void BuildCommand_DirectMode_ShouldUseParsedAutoloadArgumentAndExactPluginFileName()
    {
        // Arrange
        const string xEditPath = @"C:\Tools With Spaces\Résumé 測試\SSEEdit.exe";
        _stateServiceMock.CurrentState.Returns(new AppState
        {
            XEditExecutablePath = xEditPath,
            Mo2ModeEnabled = false
        });
        var plugin = CreatePlugin(WorstCasePluginName);

        // Act
        var result = _sut.BuildCommand(plugin, GameType.SkyrimSe);

        // Assert
        result.Should().NotBeNull();
        result!.FileName.Should().Be(xEditPath);
        result.UseShellExecute.Should().BeFalse("D-04 requires shell-sensitive characters to remain literal argv data");
        result.Arguments.Should().BeEmpty("D-16 requires asserting parsed ArgumentList entries instead of raw command strings");
        result.ArgumentList.Should().Equal("-QAC", "-autoexit", "-autoload", WorstCasePluginName);
    }

    [Fact]
    public void BuildCommand_DirectUniversalXEditWithPartialForms_ShouldUseExactArgumentOrder()
    {
        // Arrange
        _stateServiceMock.CurrentState.Returns(new AppState
        {
            XEditExecutablePath = @"C:\Tools\xEdit.exe",
            PartialFormsEnabled = true,
            Mo2ModeEnabled = false
        });

        // Act
        var result = _sut.BuildCommand(CreatePlugin("Plugin.esp"), GameType.Fallout4);

        // Assert
        result.Should().NotBeNull();
        result!.Arguments.Should().BeEmpty();
        result.ArgumentList.Should().Equal("-FO4", "-QAC", "-autoexit", "-autoload", "Plugin.esp", "-iknowwhatimdoing", "-allowmakepartial");
    }

    [Theory]
    [InlineData("Simple Plugin.esp")]
    [InlineData("Quote & Parser \"Case\".esp")]
    [InlineData("Résumé 測試 🚀.esp")]
    [InlineData("Shell & | ; ( ) ^ Name.esp")]
    public void BuildCommand_DirectMode_ShouldPreserveCuratedPluginNamesAsLiteralArgv(string pluginName)
    {
        // Arrange
        _stateServiceMock.CurrentState.Returns(new AppState
        {
            XEditExecutablePath = @"C:\Tools\SSEEdit.exe",
            Mo2ModeEnabled = false
        });

        // Act
        var result = _sut.BuildCommand(CreatePlugin(pluginName), GameType.SkyrimSe);

        // Assert
        result.Should().NotBeNull();
        result!.Arguments.Should().BeEmpty("D-01/D-15 matrix entries must be verified as parsed argv data");
        result.ArgumentList.Should().Equal("-QAC", "-autoexit", "-autoload", pluginName);
    }

    [Fact]
    public void BuildCommand_Mo2Mode_ShouldUseRunXEditDashAAndOneNestedPayload()
    {
        // Arrange
        const string xEditPath = @"C:\Tools With Spaces\xEdit.exe";
        const string mo2Path = @"C:\MO2 With Spaces\Résumé 測試\ModOrganizer.exe";
        var plugin = CreatePlugin(WorstCasePluginName, @"C:\Game Data\" + WorstCasePluginName);
        _stateServiceMock.CurrentState.Returns(new AppState
        {
            XEditExecutablePath = xEditPath,
            Mo2ExecutablePath = mo2Path,
            Mo2ModeEnabled = true
        });

        // Act
        var result = _sut.BuildCommand(plugin, GameType.Fallout4);

        // Assert
        result.Should().NotBeNull();
        result!.FileName.Should().Be(mo2Path);
        result.UseShellExecute.Should().BeFalse();
        result.Arguments.Should().BeEmpty("MO2 argv should be carried through ArgumentList while only the -a value remains nested text");
        result.ArgumentList.Should().HaveCount(4, "D-05/D-06 require run, xEdit path, -a, and exactly one nested xEdit payload entry");
        result.ArgumentList.Should().Equal("run", xEditPath, "-a", result.ArgumentList[3]);

        var nestedPayload = result.ArgumentList[3];
        nestedPayload.Should().Contain("-autoload");
        nestedPayload.Should().NotContain(plugin.FullPath, "D-07 requires MO2 -autoload to use the file-name-only target");
        var parsedPayload = ParseWindowsCommandLine(nestedPayload);
        parsedPayload.Should().Contain(WorstCasePluginName);
        parsedPayload.Should().Equal("-FO4", "-QAC", "-autoexit", "-autoload", WorstCasePluginName);
    }

    [Theory]
    [InlineData("Quote \"Case\".esp")]
    [InlineData("TrailingBackslash\\")]
    [InlineData("QuoteAndTrailingBackslash\\\"")]
    public void FormatMo2NestedArgument_ShouldPreserveQuotesAndTrailingBackslashesThroughWindowsParsing(string nestedValue)
    {
        // Arrange / Act
        var formatted = InvokeFormatMo2NestedArgument(nestedValue);

        // Assert
        ParseWindowsCommandLine(formatted).Should().Equal(
            [nestedValue],
            "D-06 requires MO2 -a to remain one value while its nested tokens survive MO2's parser");
    }

    private static PluginInfo CreatePlugin(string fileName, string? fullPath = null)
    {
        return new PluginInfo
        {
            FileName = fileName,
            FullPath = fullPath ?? @"C:\Game Data\" + fileName
        };
    }

    private static string InvokeFormatMo2NestedArgument(string argument)
    {
        var method = typeof(XEditCommandBuilder).GetMethod("FormatMo2NestedArgument", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("the MO2 nested formatter is the audited second parser-boundary escaping helper");
        return ((string?)method!.Invoke(null, [argument])).Should().NotBeNull().And.Subject;
    }

    private static IReadOnlyList<string> ParseWindowsCommandLine(string commandLine)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        var backslashes = 0;

        foreach (var c in commandLine)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                current.Append('\\', backslashes / 2);
                if (backslashes % 2 == 0)
                {
                    inQuotes = !inQuotes;
                }
                else
                {
                    current.Append('"');
                }

                backslashes = 0;
                continue;
            }

            current.Append('\\', backslashes);
            backslashes = 0;

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        current.Append('\\', backslashes);
        if (current.Length > 0)
        {
            args.Add(current.ToString());
        }

        return args;
    }
}
