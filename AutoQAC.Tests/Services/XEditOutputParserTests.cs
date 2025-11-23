using System.Collections.Generic;
using AutoQAC.Services.Cleaning;
using FluentAssertions;
using Xunit;

namespace AutoQAC.Tests.Services;

public class XEditOutputParserTests
{
    private readonly XEditOutputParser _sut;

    public XEditOutputParserTests()
    {
        _sut = new XEditOutputParser();
    }

    [Fact]
    public void ParseOutput_CountsCorrectly()
    {
        // Arrange
        var lines = new List<string>
        {
            "Some random log line",
            "Undeleting: Some record",
            "Removing: Bad record",
            "Removing: Another bad record",
            "Skipping: Safe record",
            "Making Partial Form: NavMesh",
            "Done."
        };

        // Act
        var result = _sut.ParseOutput(lines);

        // Assert
        result.ItemsUndeleted.Should().Be(1);
        result.ItemsRemoved.Should().Be(2);
        result.ItemsSkipped.Should().Be(1);
        result.PartialFormsCreated.Should().Be(1);
    }

    [Theory]
    [InlineData("Done.", true)]
    [InlineData("Cleaning completed", true)]
    [InlineData("Processing...", false)]
    [InlineData("", false)]
    public void IsCompletionLine_ReturnsExpectedResult(string line, bool expected)
    {
        // Act
        var result = _sut.IsCompletionLine(line);

        // Assert
        result.Should().Be(expected);
    }
    
    [Fact]
    public void ParseOutput_HandlesEmptyInput()
    {
        // Arrange
        var lines = new List<string>();

        // Act
        var result = _sut.ParseOutput(lines);

        // Assert
        result.ItemsRemoved.Should().Be(0);
        result.ItemsUndeleted.Should().Be(0);
    }
}
