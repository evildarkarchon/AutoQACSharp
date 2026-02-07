using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.State;
using FluentAssertions;
using Moq;

namespace AutoQAC.Tests.Services;

public class XEditCommandBuilderTests
{
    private readonly Mock<IStateService> _stateServiceMock;
    private readonly XEditCommandBuilder _sut;

    public XEditCommandBuilderTests()
    {
        _stateServiceMock = new Mock<IStateService>();
        _sut = new XEditCommandBuilder(_stateServiceMock.Object);
    }

    [Fact]
    public void BuildCommand_ReturnsNull_WhenXEditPathIsEmpty()
    {
        // Arrange
        _stateServiceMock.Setup(x => x.CurrentState).Returns(new AppState { XEditExecutablePath = "" });

        // Act
        var result = _sut.BuildCommand(new PluginInfo { FileName = "test.esp", FullPath = "/path/to/test.esp" }, GameType.SkyrimSe);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void BuildCommand_UsesXEditPath_InDirectMode()
    {
        // Arrange
        var xEditPath = @"C:\Games\SSE\SSEEdit.exe";
        _stateServiceMock.Setup(x => x.CurrentState).Returns(new AppState 
        { 
            XEditExecutablePath = xEditPath,
            Mo2ModeEnabled = false
        });
        var plugin = new PluginInfo { FileName = "Update.esm", FullPath = "C:\\Games\\SSE\\Data\\Update.esm" };

        // Act
        var result = _sut.BuildCommand(plugin, GameType.SkyrimSe);

        // Assert
        result.Should().NotBeNull();
        result!.FileName.Should().Be(xEditPath);
        result.Arguments.Should().Contain("-autoexit");
        result.Arguments.Should().Contain("-QAC");
        result.Arguments.Should().Contain($"-autoload \"{plugin.FileName}\"");
        // SSEEdit shouldn't usually get -SSE unless the name is generic, but logic says: if name starts with xEdit.
        // SSEEdit.exe does NOT start with xEdit.
        result.Arguments.Should().NotContain("-SSE");
    }

    [Fact]
    public void BuildCommand_AddsGameFlag_WhenUniversalXEdit()
    {
         // Arrange
        var xEditPath = @"C:\Tools\xEdit.exe";
        _stateServiceMock.Setup(x => x.CurrentState).Returns(new AppState 
        { 
            XEditExecutablePath = xEditPath
        });
        
        // Act
        var result = _sut.BuildCommand(new PluginInfo { FileName = "foo.esp", FullPath = "foo.esp" }, GameType.Fallout4);

        // Assert
        result!.Arguments.Should().Contain("-FO4");
    }

    [Fact]
    public void BuildCommand_AddsPartialFormFlags_WhenEnabled()
    {
        // Arrange
        _stateServiceMock.Setup(x => x.CurrentState).Returns(new AppState 
        { 
            XEditExecutablePath = "xEdit.exe",
            PartialFormsEnabled = true
        });

        // Act
        var result = _sut.BuildCommand(new PluginInfo { FileName = "foo.esp", FullPath = "foo.esp" }, GameType.SkyrimSe);

        // Assert
        result!.Arguments.Should().Contain("-iknowwhatimdoing");
        result.Arguments.Should().Contain("-allowmakepartial");
    }

    [Fact]
    public void BuildCommand_ReturnsNull_WhenGameTypeIsUnknown()
    {
        // Arrange
        _stateServiceMock.Setup(x => x.CurrentState).Returns(new AppState
        {
            XEditExecutablePath = @"C:\Tools\xEdit.exe",
            Mo2ModeEnabled = false
        });
        var plugin = new PluginInfo { FileName = "test.esp", FullPath = "C:\\path\\test.esp" };

        // Act
        var result = _sut.BuildCommand(plugin, GameType.Unknown);

        // Assert
        result.Should().BeNull("GameType.Unknown must be rejected to prevent building commands without game flags");
    }

    [Fact]
    public void BuildCommand_WrapsInMO2_WhenEnabled()
    {
        // Arrange
        var xEditPath = @"C:\Tools\SSEEdit.exe";
        var mo2Path = @"C:\MO2\ModOrganizer.exe";
        _stateServiceMock.Setup(x => x.CurrentState).Returns(new AppState 
        { 
            XEditExecutablePath = xEditPath,
            Mo2ExecutablePath = mo2Path,
            Mo2ModeEnabled = true
        });

        // Act
        var result = _sut.BuildCommand(new PluginInfo { FileName = "foo.esp", FullPath = "foo.esp" }, GameType.SkyrimSe);

        // Assert
        result!.FileName.Should().Be(mo2Path);
        result.Arguments.Should().StartWith("run \"");
        result.Arguments.Should().Contain(xEditPath);
    }
}
