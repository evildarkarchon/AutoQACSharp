namespace AutoQAC.ViewModels;

/// <summary>
/// Formats backup and restore copy progress using one shared decimal byte-unit convention.
/// </summary>
internal static class BackupProgressTextFormatter
{
    /// <summary>
    /// Converts byte counts to concise decimal units for user-facing copy progress text.
    /// </summary>
    /// <param name="bytes">The byte count reported by backup or restore copy operations.</param>
    /// <returns>A one-decimal decimal byte string such as <c>38.4 MB</c>.</returns>
    public static string FormatBytes(long bytes)
    {
        const decimal kb = 1_000m;
        const decimal mb = kb * 1_000m;
        const decimal gb = mb * 1_000m;

        return bytes switch
        {
            >= 1_000_000_000 => $"{bytes / gb:F1} GB",
            >= 1_000_000 => $"{bytes / mb:F1} MB",
            >= 1_000 => $"{bytes / kb:F1} KB",
            _ => $"{bytes} B"
        };
    }
}
