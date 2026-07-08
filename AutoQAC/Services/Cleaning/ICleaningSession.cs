using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Owns a full Cleaning session from preflight through final state publication.
/// </summary>
public interface ICleaningSession
{
    /// <summary>
    /// Emits xEdit hang state during an active Cleaning session. Emits false when the warning should clear.
    /// </summary>
    IObservable<bool> HangDetected { get; }

    /// <summary>
    /// Runs a full Cleaning session from preflight through final state publication.
    /// </summary>
    /// <param name="ct">Cancellation token for startup and session work.</param>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Runs the same preflight and plugin-decision logic without backing up, launching xEdit, or mutating cleaning state.
    /// </summary>
    /// <param name="ct">Cancellation token for preview work.</param>
    /// <returns>Preview rows showing which plugins would be cleaned or skipped.</returns>
    Task<IReadOnlyList<DryRunResult>> PreviewAsync(CancellationToken ct = default);

    /// <summary>
    /// Applies a live control intent to the active Cleaning session.
    /// </summary>
    /// <param name="control">Requested control action.</param>
    /// <param name="ct">Cancellation token for any user-decision work caused by the control request.</param>
    /// <returns>A structured result suitable for projecting user-facing status.</returns>
    Task<CleaningSessionControlResult> ControlAsync(
        CleaningSessionControl control,
        CancellationToken ct = default);
}
