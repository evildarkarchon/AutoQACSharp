using AutoQAC.Models;
using AutoQAC.Models.Diagnostics;
using AutoQAC.Tests.Helpers;
using FluentAssertions;

namespace AutoQAC.Tests.Models;

/// <summary>
/// Phase 11 regression guards for cleaning result export/report diagnostic boundaries.
/// </summary>
public sealed class Phase11ReportBoundaryTests
{
    /// <summary>
    /// Verifies generated reports defensively replace unsafe failed-plugin messages with safe latest-log copy.
    /// </summary>
    [Fact]
    public void GenerateReport_WithUnsafeFailedPluginMessage_ShouldIncludeDisclaimerAndExcludeSharedSentinels()
    {
        // Arrange
        var result = new CleaningSessionResult
        {
            StartTime = new DateTime(2026, 5, 1, 4, 0, 0),
            EndTime = new DateTime(2026, 5, 1, 4, 1, 0),
            GameType = GameType.SkyrimSe,
            PluginResults =
            [
                new PluginCleaningResult
                {
                    PluginName = "Sentinel.esp",
                    Status = CleaningStatus.Failed,
                    Success = false,
                    Message = DiagnosticSentinels.CreateUnsafePayload()
                }
            ]
        };

        // Act
        var report = result.GenerateReport();

        // Assert
        report.Should().Contain(DiagnosticTextFormatter.ReportDisclaimer);
        report.Should().Contain("Technical details are intentionally kept in AutoQAC logs and are not repeated in this report.");
        report.Should().Contain("Sentinel.esp: Cleaning failed. See the latest AutoQAC log.");
        foreach (var sentinel in DiagnosticSentinels.UnsafeDiagnosticSentinels)
        {
            report.Should().NotContain(sentinel);
        }
    }
}
