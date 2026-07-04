using System.Threading;
using System.Threading.Tasks;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Projects whether Start and Preview may be offered from the current Plugin refresh publication.
/// </summary>
public interface ICleaningCommandReadiness
{
    /// <summary>
    /// Evaluates the current Cleaning command readiness facts without mutating cleaning state or refreshing plugins.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current command readiness and the first user-safe blocking reason, if any.</returns>
    Task<CleaningCommandReadinessResult> EvaluateAsync(CancellationToken ct = default);
}

/// <summary>
/// User-visible readiness projection shared by Start and Preview commands.
/// </summary>
/// <param name="CanStartOrPreview">True when Start and Preview can be offered.</param>
/// <param name="Failure">First typed blocking reason, or null when disabled only because cleaning is active.</param>
public sealed record CleaningCommandReadinessResult(
    bool CanStartOrPreview,
    CleaningPreflightFailure? Failure)
{
    /// <summary>Ready result.</summary>
    public static CleaningCommandReadinessResult Ready { get; } = new(true, null);

    /// <summary>Disabled result without a validation banner, used while cleaning is already active.</summary>
    public static CleaningCommandReadinessResult Busy { get; } = new(false, null);

    /// <summary>
    /// Creates a disabled result with a typed preflight-compatible validation failure.
    /// </summary>
    /// <param name="failure">User-safe failure payload.</param>
    /// <returns>Disabled readiness with the provided failure.</returns>
    public static CleaningCommandReadinessResult Blocked(CleaningPreflightFailure failure) => new(false, failure);
}
