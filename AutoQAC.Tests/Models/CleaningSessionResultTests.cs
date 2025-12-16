using System;
using System.Collections.Generic;
using AutoQAC.Models;
using FluentAssertions;
using Xunit;

namespace AutoQAC.Tests.Models;

/// <summary>
/// Unit tests for <see cref="CleaningSessionResult"/> model.
/// </summary>
public sealed class CleaningSessionResultTests
{
    private static PluginCleaningResult CreateResult(string name, CleaningStatus status, int itms = 0, int udrs = 0)
    {
        return new PluginCleaningResult
        {
            PluginName = name,
            Status = status,
            Success = status == CleaningStatus.Cleaned,
            Statistics = new CleaningStatistics
            {
                ItemsRemoved = itms,
                ItemsUndeleted = udrs
            }
        };
    }

    [Fact]
    public void Constructor_WithDefaults_ShouldSetEmptyPluginResults()
    {
        // Act
        var result = new CleaningSessionResult();

        // Assert
        result.PluginResults.Should().BeEmpty();
        result.TotalPlugins.Should().Be(0);
    }

    #region Duration Tests

    [Fact]
    public void TotalDuration_ShouldCalculateCorrectly()
    {
        // Arrange
        var start = new DateTime(2024, 1, 1, 10, 0, 0);
        var end = new DateTime(2024, 1, 1, 10, 5, 30);
        var result = new CleaningSessionResult
        {
            StartTime = start,
            EndTime = end
        };

        // Assert
        result.TotalDuration.Should().Be(TimeSpan.FromMinutes(5.5));
    }

    #endregion

    #region Plugin Filtering Tests

    [Fact]
    public void CleanedPlugins_ShouldReturnOnlyCleanedPlugins()
    {
        // Arrange
        var result = new CleaningSessionResult
        {
            PluginResults = new List<PluginCleaningResult>
            {
                CreateResult("Clean1.esp", CleaningStatus.Cleaned),
                CreateResult("Skip1.esp", CleaningStatus.Skipped),
                CreateResult("Clean2.esp", CleaningStatus.Cleaned),
                CreateResult("Fail1.esp", CleaningStatus.Failed)
            }
        };

        // Assert
        result.CleanedPlugins.Should().HaveCount(2);
        result.CleanedPlugins.Should().AllSatisfy(p => p.Status.Should().Be(CleaningStatus.Cleaned));
    }

    [Fact]
    public void FailedPlugins_ShouldReturnOnlyFailedPlugins()
    {
        // Arrange
        var result = new CleaningSessionResult
        {
            PluginResults = new List<PluginCleaningResult>
            {
                CreateResult("Clean1.esp", CleaningStatus.Cleaned),
                CreateResult("Fail1.esp", CleaningStatus.Failed),
                CreateResult("Fail2.esp", CleaningStatus.Failed)
            }
        };

        // Assert
        result.FailedPlugins.Should().HaveCount(2);
        result.FailedPlugins.Should().AllSatisfy(p => p.Status.Should().Be(CleaningStatus.Failed));
    }

    [Fact]
    public void SkippedPlugins_ShouldReturnOnlySkippedPlugins()
    {
        // Arrange
        var result = new CleaningSessionResult
        {
            PluginResults = new List<PluginCleaningResult>
            {
                CreateResult("Skip1.esp", CleaningStatus.Skipped),
                CreateResult("Clean1.esp", CleaningStatus.Cleaned),
                CreateResult("Skip2.esp", CleaningStatus.Skipped)
            }
        };

        // Assert
        result.SkippedPlugins.Should().HaveCount(2);
        result.SkippedPlugins.Should().AllSatisfy(p => p.Status.Should().Be(CleaningStatus.Skipped));
    }

    #endregion

    #region Count Tests

    [Fact]
    public void Counts_ShouldBeAccurate()
    {
        // Arrange
        var result = new CleaningSessionResult
        {
            PluginResults = new List<PluginCleaningResult>
            {
                CreateResult("Clean1.esp", CleaningStatus.Cleaned),
                CreateResult("Clean2.esp", CleaningStatus.Cleaned),
                CreateResult("Clean3.esp", CleaningStatus.Cleaned),
                CreateResult("Skip1.esp", CleaningStatus.Skipped),
                CreateResult("Skip2.esp", CleaningStatus.Skipped),
                CreateResult("Fail1.esp", CleaningStatus.Failed)
            }
        };

        // Assert
        result.TotalPlugins.Should().Be(6);
        result.CleanedCount.Should().Be(3);
        result.SkippedCount.Should().Be(2);
        result.FailedCount.Should().Be(1);
    }

    #endregion

    #region Statistics Aggregation Tests

    [Fact]
    public void TotalItemsRemoved_ShouldSumAllITMs()
    {
        // Arrange
        var result = new CleaningSessionResult
        {
            PluginResults = new List<PluginCleaningResult>
            {
                CreateResult("Plugin1.esp", CleaningStatus.Cleaned, itms: 10),
                CreateResult("Plugin2.esp", CleaningStatus.Cleaned, itms: 5),
                CreateResult("Plugin3.esp", CleaningStatus.Skipped) // No ITMs
            }
        };

        // Assert
        result.TotalItemsRemoved.Should().Be(15);
    }

    [Fact]
    public void TotalItemsUndeleted_ShouldSumAllUDRs()
    {
        // Arrange
        var result = new CleaningSessionResult
        {
            PluginResults = new List<PluginCleaningResult>
            {
                CreateResult("Plugin1.esp", CleaningStatus.Cleaned, udrs: 3),
                CreateResult("Plugin2.esp", CleaningStatus.Cleaned, udrs: 7),
                CreateResult("Plugin3.esp", CleaningStatus.Failed) // No UDRs
            }
        };

        // Assert
        result.TotalItemsUndeleted.Should().Be(10);
    }

    [Fact]
    public void TotalPartialFormsCreated_ShouldSumCorrectly()
    {
        // Arrange
        var result = new CleaningSessionResult
        {
            PluginResults = new List<PluginCleaningResult>
            {
                new()
                {
                    PluginName = "Plugin1.esp",
                    Status = CleaningStatus.Cleaned,
                    Statistics = new CleaningStatistics { PartialFormsCreated = 2 }
                },
                new()
                {
                    PluginName = "Plugin2.esp",
                    Status = CleaningStatus.Cleaned,
                    Statistics = new CleaningStatistics { PartialFormsCreated = 3 }
                }
            }
        };

        // Assert
        result.TotalPartialFormsCreated.Should().Be(5);
    }

    #endregion

    #region IsSuccess Tests

    [Fact]
    public void IsSuccess_WhenNoFailuresAndNotCancelled_ShouldBeTrue()
    {
        // Arrange
        var result = new CleaningSessionResult
        {
            WasCancelled = false,
            PluginResults = new List<PluginCleaningResult>
            {
                CreateResult("Clean1.esp", CleaningStatus.Cleaned),
                CreateResult("Skip1.esp", CleaningStatus.Skipped)
            }
        };

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void IsSuccess_WhenCancelled_ShouldBeFalse()
    {
        // Arrange
        var result = new CleaningSessionResult
        {
            WasCancelled = true,
            PluginResults = new List<PluginCleaningResult>
            {
                CreateResult("Clean1.esp", CleaningStatus.Cleaned)
            }
        };

        // Assert
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void IsSuccess_WhenHasFailures_ShouldBeFalse()
    {
        // Arrange
        var result = new CleaningSessionResult
        {
            WasCancelled = false,
            PluginResults = new List<PluginCleaningResult>
            {
                CreateResult("Clean1.esp", CleaningStatus.Cleaned),
                CreateResult("Fail1.esp", CleaningStatus.Failed)
            }
        };

        // Assert
        result.IsSuccess.Should().BeFalse();
    }

    #endregion

    #region SessionSummary Tests

    [Fact]
    public void SessionSummary_WhenCancelled_ShouldShowCancellationMessage()
    {
        // Arrange
        var result = new CleaningSessionResult
        {
            WasCancelled = true,
            PluginResults = new List<PluginCleaningResult>
            {
                CreateResult("Clean1.esp", CleaningStatus.Cleaned),
                CreateResult("Clean2.esp", CleaningStatus.Cleaned),
                CreateResult("NotCleaned.esp", CleaningStatus.Skipped)
            }
        };

        // Assert
        result.SessionSummary.Should().Contain("Cancelled");
        result.SessionSummary.Should().Contain("2"); // 2 cleaned
        result.SessionSummary.Should().Contain("3"); // 3 total
    }

    [Fact]
    public void SessionSummary_WhenHasFailures_ShouldShowErrorMessage()
    {
        // Arrange
        var result = new CleaningSessionResult
        {
            WasCancelled = false,
            PluginResults = new List<PluginCleaningResult>
            {
                CreateResult("Clean1.esp", CleaningStatus.Cleaned),
                CreateResult("Fail1.esp", CleaningStatus.Failed),
                CreateResult("Skip1.esp", CleaningStatus.Skipped)
            }
        };

        // Assert
        result.SessionSummary.Should().Contain("errors");
        result.SessionSummary.Should().Contain("1 cleaned");
        result.SessionSummary.Should().Contain("1 failed");
        result.SessionSummary.Should().Contain("1 skipped");
    }

    [Fact]
    public void SessionSummary_WhenSuccessful_ShouldShowCompletedMessage()
    {
        // Arrange
        var result = new CleaningSessionResult
        {
            WasCancelled = false,
            PluginResults = new List<PluginCleaningResult>
            {
                CreateResult("Clean1.esp", CleaningStatus.Cleaned),
                CreateResult("Clean2.esp", CleaningStatus.Cleaned),
                CreateResult("Skip1.esp", CleaningStatus.Skipped)
            }
        };

        // Assert
        result.SessionSummary.Should().StartWith("Completed:");
        result.SessionSummary.Should().Contain("2 cleaned");
        result.SessionSummary.Should().Contain("1 skipped");
    }

    #endregion

    #region GenerateReport Tests

    [Fact]
    public void GenerateReport_ShouldIncludeHeader()
    {
        // Arrange
        var result = new CleaningSessionResult
        {
            StartTime = new DateTime(2024, 1, 15, 14, 30, 0),
            EndTime = new DateTime(2024, 1, 15, 14, 35, 0),
            GameType = GameType.SkyrimSE
        };

        // Act
        var report = result.GenerateReport();

        // Assert
        report.Should().Contain("AutoQAC Cleaning Report");
        report.Should().Contain("2024-01-15");
        report.Should().Contain("SkyrimSE");
        report.Should().Contain("00:05:00");
    }

    [Fact]
    public void GenerateReport_ShouldIncludeSummary()
    {
        // Arrange
        var result = new CleaningSessionResult
        {
            PluginResults = new List<PluginCleaningResult>
            {
                CreateResult("Plugin1.esp", CleaningStatus.Cleaned, itms: 10, udrs: 5),
                CreateResult("Plugin2.esp", CleaningStatus.Skipped),
                CreateResult("Plugin3.esp", CleaningStatus.Failed)
            }
        };

        // Act
        var report = result.GenerateReport();

        // Assert
        report.Should().Contain("Total Plugins: 3");
        report.Should().Contain("Cleaned: 1");
        report.Should().Contain("Skipped: 1");
        report.Should().Contain("Failed: 1");
        report.Should().Contain("ITMs Removed: 10");
        report.Should().Contain("UDRs Fixed: 5");
    }

    [Fact]
    public void GenerateReport_ShouldIncludePartialForms_WhenPresent()
    {
        // Arrange
        var result = new CleaningSessionResult
        {
            PluginResults = new List<PluginCleaningResult>
            {
                new()
                {
                    PluginName = "Plugin1.esp",
                    Status = CleaningStatus.Cleaned,
                    Statistics = new CleaningStatistics { PartialFormsCreated = 3 }
                }
            }
        };

        // Act
        var report = result.GenerateReport();

        // Assert
        report.Should().Contain("Partial Forms: 3");
    }

    [Fact]
    public void GenerateReport_ShouldNotIncludePartialForms_WhenZero()
    {
        // Arrange
        var result = new CleaningSessionResult
        {
            PluginResults = new List<PluginCleaningResult>
            {
                CreateResult("Plugin1.esp", CleaningStatus.Cleaned, itms: 5)
            }
        };

        // Act
        var report = result.GenerateReport();

        // Assert
        report.Should().NotContain("Partial Forms:");
    }

    [Fact]
    public void GenerateReport_ShouldIncludePluginDetails()
    {
        // Arrange
        var result = new CleaningSessionResult
        {
            PluginResults = new List<PluginCleaningResult>
            {
                new()
                {
                    PluginName = "Clean.esp",
                    Status = CleaningStatus.Cleaned,
                    Duration = TimeSpan.FromSeconds(45),
                    Statistics = new CleaningStatistics { ItemsRemoved = 10 }
                },
                new()
                {
                    PluginName = "Skip.esp",
                    Status = CleaningStatus.Skipped
                },
                new()
                {
                    PluginName = "Fail.esp",
                    Status = CleaningStatus.Failed,
                    Message = "Timeout"
                }
            }
        };

        // Act
        var report = result.GenerateReport();

        // Assert
        report.Should().Contain("Cleaned Plugins");
        report.Should().Contain("Clean.esp");
        report.Should().Contain("Skipped Plugins");
        report.Should().Contain("Skip.esp");
        report.Should().Contain("Failed Plugins");
        report.Should().Contain("Fail.esp: Timeout");
    }

    [Fact]
    public void GenerateReport_WhenCancelled_ShouldIncludeCancellationNote()
    {
        // Arrange
        var result = new CleaningSessionResult
        {
            WasCancelled = true
        };

        // Act
        var report = result.GenerateReport();

        // Assert
        report.Should().Contain("cancelled by user");
    }

    #endregion

    #region Empty Results Tests

    [Fact]
    public void WithEmptyPluginResults_ShouldHandleGracefully()
    {
        // Arrange
        var result = new CleaningSessionResult
        {
            PluginResults = Array.Empty<PluginCleaningResult>()
        };

        // Assert
        result.TotalPlugins.Should().Be(0);
        result.CleanedCount.Should().Be(0);
        result.FailedCount.Should().Be(0);
        result.SkippedCount.Should().Be(0);
        result.TotalItemsRemoved.Should().Be(0);
        result.TotalItemsUndeleted.Should().Be(0);
        result.IsSuccess.Should().BeTrue();

        // GenerateReport should not throw
        var report = result.GenerateReport();
        report.Should().NotBeNullOrEmpty();
    }

    #endregion
}
