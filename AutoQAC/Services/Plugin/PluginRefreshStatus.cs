namespace AutoQAC.Services.Plugin;

/// <summary>
/// Classifies typed plugin refresh status updates emitted by the refresh coordinator.
/// </summary>
public enum PluginRefreshStatusKind
{
    Idle,
    LoadingPlugins,
    AnalyzingSelected,
    SelectedRefreshCompleted,
    SelectPlugins,
    Canceled,
    ApproximationUnavailable
}

/// <summary>
/// Explains why an active refresh generation was canceled.
/// </summary>
public enum PluginRefreshCancelReason
{
    Manual,
    Superseded,
    CleaningStarted,
    Reset,
    Disposed
}

/// <summary>
/// Typed plugin refresh progress or terminal outcome.
/// ViewModels convert these stable outcomes into user-facing status text without parsing service internals.
/// </summary>
/// <param name="Kind">Status category.</param>
/// <param name="Current">Current selected-plugin analysis index for progress statuses.</param>
/// <param name="Total">Total selected-plugin target count for progress statuses.</param>
/// <param name="UpdatedCount">Number of selected plugin approximations updated by a completed refresh.</param>
/// <param name="Message">Optional service-provided message override for future UI mapping.</param>
public sealed record PluginRefreshStatus(
    PluginRefreshStatusKind Kind,
    int Current = 0,
    int Total = 0,
    int UpdatedCount = 0,
    string? Message = null)
{
    /// <summary>
    /// Creates an analyzing-progress status for selected approximation refresh.
    /// </summary>
    /// <param name="current">One-based current selected plugin index.</param>
    /// <param name="total">Total selected plugin count.</param>
    /// <returns>A status whose display text is shaped for truthful item-count progress.</returns>
    public static PluginRefreshStatus AnalyzingSelected(int current, int total) =>
        new(PluginRefreshStatusKind.AnalyzingSelected, Current: current, Total: total);

    /// <summary>
    /// Creates a selected-refresh completion status carrying the updated target count.
    /// </summary>
    /// <param name="count">Number of selected plugin approximations updated.</param>
    /// <returns>A completed status for selected approximation refresh.</returns>
    public static PluginRefreshStatus SelectedRefreshCompleted(int count) =>
        new(PluginRefreshStatusKind.SelectedRefreshCompleted, UpdatedCount: count);

    /// <summary>
    /// Converts the typed status to the current canonical user-facing text.
    /// This keeps status wording stable until ViewModels take over localized or richer mapping.
    /// </summary>
    /// <returns>Concise text suitable for status display.</returns>
    public string ToDisplayText() => Kind switch
    {
        PluginRefreshStatusKind.AnalyzingSelected => $"Analyzing {Current} of {Total} selected plugins.",
        PluginRefreshStatusKind.SelectedRefreshCompleted => $"Updated {UpdatedCount} selected plugin approximations.",
        PluginRefreshStatusKind.SelectPlugins => "Select plugins to refresh.",
        PluginRefreshStatusKind.Canceled => "Approximation refresh canceled.",
        PluginRefreshStatusKind.ApproximationUnavailable => "Approximation refresh is not available for this game.",
        _ => Message ?? string.Empty
    };
}
