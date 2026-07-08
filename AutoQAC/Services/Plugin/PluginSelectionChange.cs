namespace AutoQAC.Services.Plugin;

/// <summary>
/// Describes a plugin selection mutation over the current visible Plugin refresh snapshot.
/// </summary>
public abstract record PluginSelectionChange
{
    /// <summary>
    /// Selects every visible plugin row.
    /// </summary>
    public sealed record SelectAllVisible : PluginSelectionChange;

    /// <summary>
    /// Deselects every visible plugin row.
    /// </summary>
    public sealed record DeselectAllVisible : PluginSelectionChange;

    /// <summary>
    /// Sets selection for one visible plugin row.
    /// </summary>
    /// <param name="Row">Identity of the row to mutate.</param>
    /// <param name="IsSelected">Whether the row should be selected.</param>
    public sealed record SetOne(
        PluginRefreshRowKey Row,
        bool IsSelected) : PluginSelectionChange;
}
