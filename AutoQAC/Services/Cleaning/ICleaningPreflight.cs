using System.Threading;
using System.Threading.Tasks;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Runs the shared preflight pipeline used by both StartCleaningAsync and RunDryRunAsync (D-13).
/// Flushes pending config, validates environment, detects game/variant, applies skip lists and
/// exclusions, validates MO2 path (when active), and validates plugin files (when not in MO2 mode).
/// </summary>
/// <remarks>
/// Does NOT mutate cleaning state, start processes, create cancellation tokens, or run backups (D-14).
/// State mutation is the caller's responsibility. Note: PrepareAsync DOES perform persistence
/// flushing (FlushPendingSavesAsync) and environment validation (ValidateEnvironmentAsync) — those
/// are environment plumbing, not cleaning state mutation.
/// </remarks>
public interface ICleaningPreflight
{
    /// <summary>
    /// Prepares the cleaning plan: detected game/variant, per-plugin clean/skip decisions
    /// with reasons (D-15), and MO2 policy facts (D-16).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Immutable plan describing the cleaning session inputs, including XEditDirectory (R-08).</returns>
    /// <exception cref="System.InvalidOperationException">
    /// Thrown when configuration is missing, the game type cannot be detected, or the MO2
    /// executable path is required but missing/invalid. Messages match the current orchestrator
    /// verbatim (D-12: user-facing messages stay stable).
    /// </exception>
    Task<CleaningPreflightPlan> PrepareAsync(CancellationToken ct = default);
}
