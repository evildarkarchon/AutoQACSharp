namespace AutoQAC.Models;

/// <summary>
/// Represents a pre-clean validation error with actionable fix guidance.
/// </summary>
public sealed record ValidationError(string Title, string Message, string FixStep);
