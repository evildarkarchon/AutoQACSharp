using AutoQAC.Services.UI;
using FluentAssertions;

namespace AutoQAC.Tests.Services.UI;

/// <summary>
/// Unit tests for framework-neutral file dialog filter parsing.
/// The UI-specific file dialog services adapt these entries to their picker APIs.
/// </summary>
public sealed class FileDialogServiceTests
{
    #region Filter Parsing Tests

    /// <summary>
    /// Verifies that standard file dialog filter format is parsed correctly.
    /// Format: "DisplayName (*.ext)|*.ext|DisplayName2 (*.*)|*.*"
    /// </summary>
    [Fact]
    public void ParseFilter_ShouldParseStandardFormat()
    {
        // Arrange
        const string filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*";

        // Act
        var result = InvokeParseFilter(filter);

        // Assert
        result.Should().HaveCount(2, "should have two file type filters");

        result[0].Name.Should().Be("Text Files (*.txt)");
        result[0].Patterns.Should().Contain("*.txt");

        result[1].Name.Should().Be("All Files (*.*)");
        result[1].Patterns.Should().Contain("*.*");
    }

    /// <summary>
    /// Verifies that multiple patterns in a single filter entry are parsed correctly.
    /// </summary>
    [Fact]
    public void ParseFilter_ShouldHandleMultiplePatterns()
    {
        // Arrange
        const string filter = "Image Files (*.jpg;*.png;*.gif)|*.jpg;*.png;*.gif";

        // Act
        var result = InvokeParseFilter(filter);

        // Assert
        result.Should().ContainSingle();
        result[0].Patterns.Should().HaveCount(3);
        result[0].Patterns.Should().Contain("*.jpg");
        result[0].Patterns.Should().Contain("*.png");
        result[0].Patterns.Should().Contain("*.gif");
    }

    /// <summary>
    /// Verifies that empty filter string returns empty list.
    /// </summary>
    [Fact]
    public void ParseFilter_ShouldHandleEmptyString()
    {
        // Arrange
        const string filter = "";

        // Act
        var result = InvokeParseFilter(filter);

        // Assert
        result.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that malformed filter with odd number of parts is handled gracefully.
    /// </summary>
    [Fact]
    public void ParseFilter_ShouldHandleMalformedFilter_OddParts()
    {
        // Arrange
        const string filter = "Text Files|*.txt|Orphan Part";

        // Act
        var result = InvokeParseFilter(filter);

        // Assert
        // Should parse the complete pair and ignore the orphan
        result.Should().ContainSingle();
        result[0].Name.Should().Be("Text Files");
        result[0].Patterns.Should().Contain("*.txt");
    }

    /// <summary>
    /// Verifies that filter with single entry is parsed correctly.
    /// </summary>
    [Fact]
    public void ParseFilter_ShouldHandleSingleFilter()
    {
        // Arrange
        const string filter = "Executables (*.exe)|*.exe";

        // Act
        var result = InvokeParseFilter(filter);

        // Assert
        result.Should().ContainSingle();
        result[0].Name.Should().Be("Executables (*.exe)");
        result[0].Patterns.Should().ContainSingle().Which.Should().Be("*.exe");
    }

    /// <summary>
    /// Verifies parsing with special characters in display name.
    /// </summary>
    [Fact]
    public void ParseFilter_ShouldHandleSpecialCharactersInName()
    {
        // Arrange
        const string filter = "C# Source (*.cs)|*.cs";

        // Act
        var result = InvokeParseFilter(filter);

        // Assert
        result.Should().ContainSingle();
        result[0].Name.Should().Be("C# Source (*.cs)");
    }

    /// <summary>
    /// Verifies that the standard xEdit executable filter is parsed correctly.
    /// This is the actual filter used in the application.
    /// </summary>
    [Fact]
    public void ParseFilter_ShouldHandleXEditFilter()
    {
        // Arrange
        const string filter = "Executables (*.exe)|*.exe|All Files (*.*)|*.*";

        // Act
        var result = InvokeParseFilter(filter);

        // Assert
        result.Should().HaveCount(2);
        result[0].Patterns.Should().Contain("*.exe");
        result[1].Patterns.Should().Contain("*.*");
    }

    [Fact]
    public void BuildExtensionList_ShouldNormalizePickerExtensions()
    {
        // Arrange
        const string filter = "Executables (*.exe)|*.exe|All Files (*.*)|*.*";

        // Act
        var result = FileDialogFilterMapper.BuildExtensionList(filter);

        // Assert
        result.Should().Equal(".exe", "*");
    }

    [Fact]
    public void BuildFileTypeChoices_ShouldPreserveFilterNamesAndNormalizePatterns()
    {
        // Arrange
        const string filter = "Images (*.jpg;*.png)|*.jpg;*.png|All Files (*.*)|*.*";

        // Act
        var result = FileDialogFilterMapper.BuildFileTypeChoices(filter);

        // Assert
        result.Should().ContainKey("Images (*.jpg;*.png)");
        result["Images (*.jpg;*.png)"].Should().Equal(".jpg", ".png");
        result["All Files (*.*)"].Should().Equal("*");
    }

    [Fact]
    public void BuildFileTypeChoices_ShouldFallbackToAllFilesForEmptyFilter()
    {
        // Act
        var result = FileDialogFilterMapper.BuildFileTypeChoices(string.Empty);

        // Assert
        result.Should().ContainSingle();
        result["All Files (*.*)"].Should().Equal("*");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Parses the filter string through the framework-neutral helper used by
    /// UI-specific file dialog service implementations.
    /// </summary>
    private static IReadOnlyList<FileDialogFilterEntry> InvokeParseFilter(string filter) =>
        FileDialogFilterParser.Parse(filter);

    #endregion
}
