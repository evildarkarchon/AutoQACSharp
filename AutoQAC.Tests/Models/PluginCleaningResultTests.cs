using System;
using AutoQAC.Models;
using FluentAssertions;
using Xunit;

namespace AutoQAC.Tests.Models;

/// <summary>
/// Unit tests for <see cref="PluginCleaningResult"/> model.
/// </summary>
public sealed class PluginCleaningResultTests
{
    [Fact]
    public void Constructor_WithRequiredProperties_ShouldSetCorrectDefaults()
    {
        // Act
        var result = new PluginCleaningResult { PluginName = "Test.esp" };

        // Assert
        result.PluginName.Should().Be("Test.esp");
        result.Status.Should().Be(CleaningStatus.Cleaned); // Default enum value
        result.Success.Should().BeFalse();
        result.Message.Should().BeEmpty();
        result.Duration.Should().Be(TimeSpan.Zero);
        result.Statistics.Should().BeNull();
    }

    [Theory]
    [InlineData(CleaningStatus.Cleaned)]
    [InlineData(CleaningStatus.Skipped)]
    [InlineData(CleaningStatus.Failed)]
    public void Status_ShouldBeSettable_ToAllValues(CleaningStatus expectedStatus)
    {
        // Act
        var result = new PluginCleaningResult
        {
            PluginName = "Test.esp",
            Status = expectedStatus
        };

        // Assert
        result.Status.Should().Be(expectedStatus);
    }

    #region Statistics Properties Tests

    [Fact]
    public void StatisticsProperties_WhenStatisticsIsNull_ShouldReturnZero()
    {
        // Arrange
        var result = new PluginCleaningResult
        {
            PluginName = "Test.esp",
            Statistics = null
        };

        // Assert
        result.ItemsRemoved.Should().Be(0);
        result.ItemsUndeleted.Should().Be(0);
        result.ItemsSkipped.Should().Be(0);
        result.PartialFormsCreated.Should().Be(0);
        result.TotalProcessed.Should().Be(0);
    }

    [Fact]
    public void StatisticsProperties_WhenStatisticsIsPresent_ShouldReturnCorrectValues()
    {
        // Arrange
        var result = new PluginCleaningResult
        {
            PluginName = "Test.esp",
            Statistics = new CleaningStatistics
            {
                ItemsRemoved = 10,
                ItemsUndeleted = 5,
                ItemsSkipped = 2,
                PartialFormsCreated = 3
            }
        };

        // Assert
        result.ItemsRemoved.Should().Be(10);
        result.ItemsUndeleted.Should().Be(5);
        result.ItemsSkipped.Should().Be(2);
        result.PartialFormsCreated.Should().Be(3);
        result.TotalProcessed.Should().Be(20); // 10 + 5 + 2 + 3
    }

    #endregion

    #region Summary Tests

    [Fact]
    public void Summary_WhenSkipped_ShouldReturnSkipped()
    {
        // Arrange
        var result = new PluginCleaningResult
        {
            PluginName = "Test.esp",
            Status = CleaningStatus.Skipped
        };

        // Assert
        result.Summary.Should().Be("Skipped");
    }

    [Fact]
    public void Summary_WhenFailed_ShouldIncludeMessage()
    {
        // Arrange
        var result = new PluginCleaningResult
        {
            PluginName = "Test.esp",
            Status = CleaningStatus.Failed,
            Message = "File locked"
        };

        // Assert
        result.Summary.Should().Be("Failed: File locked");
    }

    [Fact]
    public void Summary_WhenCleanedWithNoChanges_ShouldReturnNoChanges()
    {
        // Arrange
        var result = new PluginCleaningResult
        {
            PluginName = "Test.esp",
            Status = CleaningStatus.Cleaned,
            Statistics = new CleaningStatistics
            {
                ItemsRemoved = 0,
                ItemsUndeleted = 0,
                ItemsSkipped = 0,
                PartialFormsCreated = 0
            }
        };

        // Assert
        result.Summary.Should().Be("No changes");
    }

    [Fact]
    public void Summary_WhenCleanedWithOnlyITMs_ShouldShowITMs()
    {
        // Arrange
        var result = new PluginCleaningResult
        {
            PluginName = "Test.esp",
            Status = CleaningStatus.Cleaned,
            Statistics = new CleaningStatistics { ItemsRemoved = 15 }
        };

        // Assert
        result.Summary.Should().Be("15 ITMs");
    }

    [Fact]
    public void Summary_WhenCleanedWithOnlyUDRs_ShouldShowUDRs()
    {
        // Arrange
        var result = new PluginCleaningResult
        {
            PluginName = "Test.esp",
            Status = CleaningStatus.Cleaned,
            Statistics = new CleaningStatistics { ItemsUndeleted = 7 }
        };

        // Assert
        result.Summary.Should().Be("7 UDRs");
    }

    [Fact]
    public void Summary_WhenCleanedWithMixedStats_ShouldShowAll()
    {
        // Arrange
        var result = new PluginCleaningResult
        {
            PluginName = "Test.esp",
            Status = CleaningStatus.Cleaned,
            Statistics = new CleaningStatistics
            {
                ItemsRemoved = 10,
                ItemsUndeleted = 5,
                PartialFormsCreated = 2
            }
        };

        // Assert
        result.Summary.Should().Be("10 ITMs, 5 UDRs, 2 partial");
    }

    [Fact]
    public void Summary_WhenCleanedWithNullStatisticsAndZeroTotal_ShouldReturnNoChanges()
    {
        // Arrange
        var result = new PluginCleaningResult
        {
            PluginName = "Test.esp",
            Status = CleaningStatus.Cleaned,
            Statistics = null
        };

        // Assert
        result.Summary.Should().Be("No changes");
    }

    #endregion

    #region Record Behavior Tests

    [Fact]
    public void Record_ShouldBeImmutable()
    {
        // Arrange
        var original = new PluginCleaningResult
        {
            PluginName = "Original.esp",
            Status = CleaningStatus.Cleaned,
            Message = "Done"
        };

        // Act
        var modified = original with { PluginName = "Modified.esp" };

        // Assert
        original.PluginName.Should().Be("Original.esp");
        modified.PluginName.Should().Be("Modified.esp");
    }

    [Fact]
    public void Record_WithSameValues_ShouldBeEqual()
    {
        // Arrange
        var stats = new CleaningStatistics { ItemsRemoved = 5 };
        var result1 = new PluginCleaningResult
        {
            PluginName = "Test.esp",
            Status = CleaningStatus.Cleaned,
            Success = true,
            Statistics = stats
        };
        var result2 = new PluginCleaningResult
        {
            PluginName = "Test.esp",
            Status = CleaningStatus.Cleaned,
            Success = true,
            Statistics = stats
        };

        // Assert
        result1.Should().Be(result2);
    }

    #endregion
}
