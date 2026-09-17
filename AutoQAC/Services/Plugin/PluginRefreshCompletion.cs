namespace AutoQAC.Services.Plugin;

/// <summary>Correlated outcome of one settings-triggered refresh; rejected operations carry no unrelated snapshot.</summary>
public sealed record PluginRefreshCompletion(
    PluginRefreshCompletionStatus Status,
    PluginRefreshSnapshot? Snapshot = null,
    PluginRefreshPublication? Publication = null);

/// <summary>Terminal publication outcomes, independent of later Issue approximation work.</summary>
public enum PluginRefreshCompletionStatus
{
    Published,
    NoGame,
    Failed,
    Canceled,
    Superseded
}
