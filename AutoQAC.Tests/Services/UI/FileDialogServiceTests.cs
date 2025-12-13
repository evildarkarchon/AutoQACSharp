using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Platform.Storage;
using FluentAssertions;
using Xunit;

namespace AutoQAC.Tests.Services.UI;

/// <summary>
/// Unit tests for <see cref="AutoQAC.Services.UI.FileDialogService"/>.
///
/// NOTE: The FileDialogService is tightly coupled to Avalonia UI and cannot be
/// fully unit tested without the Avalonia.Headless infrastructure. These tests
/// focus on the testable ParseFilter method via reflection.
///
/// For full UI testing, consider using Avalonia.Headless for integration tests.
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
        var filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*";

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
        var filter = "Image Files (*.jpg;*.png;*.gif)|*.jpg;*.png;*.gif";

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
        var filter = "";

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
        var filter = "Text Files|*.txt|Orphan Part";

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
        var filter = "Executables (*.exe)|*.exe";

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
        var filter = "C# Source (*.cs)|*.cs";

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
        var filter = "Executables (*.exe)|*.exe|All Files (*.*)|*.*";

        // Act
        var result = InvokeParseFilter(filter);

        // Assert
        result.Should().HaveCount(2);
        result[0].Patterns.Should().Contain("*.exe");
        result[1].Patterns.Should().Contain("*.*");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Invokes the private ParseFilter method via reflection for testing.
    /// </summary>
    private static List<FilePickerFileType> InvokeParseFilter(string filter)
    {
        var serviceType = typeof(AutoQAC.Services.UI.FileDialogService);
        var method = serviceType.GetMethod("ParseFilter",
            BindingFlags.NonPublic | BindingFlags.Static);

        if (method == null)
        {
            throw new InvalidOperationException("ParseFilter method not found");
        }

        var result = method.Invoke(null, new object[] { filter });
        return result as List<FilePickerFileType>
               ?? throw new InvalidOperationException("ParseFilter returned unexpected type");
    }

    #endregion
}
