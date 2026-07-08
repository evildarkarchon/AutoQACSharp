namespace AutoQAC.Tests.Helpers;

/// <summary>
/// Provides shared unsafe diagnostic fragments used by Phase 11 regression guards.
/// </summary>
public static class DiagnosticSentinels
{
    /// <summary>
    /// Gets representative path, command, exception, and stack fragments that must not cross covered user-facing boundaries.
    /// </summary>
    public static string[] UnsafeDiagnosticSentinels { get; } =
    [
        @"C:\Users\Alice",
        @"C:\Games\Skyrim Special Edition",
        @"C:\ModOrganizer\ModOrganizer.exe",
        "SSEEdit.exe -QAC",
        "-autoload",
        "System.InvalidOperationException",
        "Stack Trace",
        " at AutoQAC."
    ];

    /// <summary>
    /// Creates one unsafe payload containing every sentinel so tests exercise the shared negative-disclosure contract.
    /// </summary>
    public static string CreateUnsafePayload() => string.Join(" | ", UnsafeDiagnosticSentinels);
}
