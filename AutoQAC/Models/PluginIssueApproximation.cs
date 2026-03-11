namespace AutoQAC.Models;

public enum PluginIssueApproximationStatus
{
    Pending,
    Available,
    Unavailable
}

public sealed record PluginIssueApproximation
{
    public PluginIssueApproximationStatus Status { get; init; }
    public int ItmCount { get; init; }
    public int DeletedReferenceCount { get; init; }
    public int DeletedNavmeshCount { get; init; }

    public static PluginIssueApproximation Pending { get; } = new()
    {
        Status = PluginIssueApproximationStatus.Pending
    };

    public static PluginIssueApproximation Unavailable { get; } = new()
    {
        Status = PluginIssueApproximationStatus.Unavailable
    };

    public static PluginIssueApproximation Available(int itmCount, int deletedReferenceCount, int deletedNavmeshCount)
    {
        return new PluginIssueApproximation
        {
            Status = PluginIssueApproximationStatus.Available,
            ItmCount = itmCount,
            DeletedReferenceCount = deletedReferenceCount,
            DeletedNavmeshCount = deletedNavmeshCount
        };
    }
}

public sealed record PluginIssueApproximationResult
{
    public required string FileName { get; init; }
    public required string FullPath { get; init; }
    public required PluginIssueApproximation Approximation { get; init; }
}
