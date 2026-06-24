using System;

namespace AutoQAC.Services.Configuration;

/// <summary>
/// Describes the terminal status for a configuration persistence operation.
/// </summary>
public enum ConfigPersistenceStatusKind
{
    Success,
    NoOp,
    Failed,
    Rejected
}

/// <summary>
/// Identifies the configuration persistence operation that produced a result or failure.
/// </summary>
public enum ConfigPersistenceOperationKind
{
    Save,
    Flush,
    Reload,
    DeferredReload,
    Watcher
}

/// <summary>
/// Classifies recoverable configuration persistence failures without exposing raw exceptions.
/// </summary>
public enum ConfigPersistenceFailureKind
{
    WriteFailed,
    ReadFailed,
    InvalidExternalYaml,
    MissingFile,
    Unknown
}

/// <summary>
/// Safe failure payload for ViewModel-facing configuration persistence status. Per D-28,
/// <paramref name="SafeSummary" /> is category-shaped human text such as
/// "Could not write settings file (write_failed)" and must not contain raw exception types,
/// stack traces, or full internal paths.
/// </summary>
/// <param name="Operation">The operation that failed.</param>
/// <param name="Kind">The recoverable failure category.</param>
/// <param name="SafeSummary">Concise safe text for UI/status mapping.</param>
/// <param name="LogReference">Optional opaque log reference text.</param>
/// <param name="Generation">The coordinator generation associated with the failure.</param>
public sealed record ConfigPersistenceFailure(
    ConfigPersistenceOperationKind Operation,
    ConfigPersistenceFailureKind Kind,
    string SafeSummary,
    string? LogReference,
    long Generation);

/// <summary>
/// Typed result returned by barrier configuration persistence operations so callers can branch on
/// success, no-op, failure, or rejection before continuing workflow steps.
/// </summary>
/// <param name="Status">The operation status.</param>
/// <param name="Operation">The operation that completed.</param>
/// <param name="Generation">The coordinator generation associated with the result.</param>
/// <param name="Failure">The safe failure payload when <paramref name="Status" /> is Failed.</param>
public sealed record ConfigPersistenceResult(
    ConfigPersistenceStatusKind Status,
    ConfigPersistenceOperationKind Operation,
    long Generation,
    ConfigPersistenceFailure? Failure);

/// <summary>
/// Thrown when a required persistence operation fails in a context that must abort
/// the calling workflow. Phase 10 D-26: pre-cleaning flush failures must block xEdit
/// launch and surface the typed payload to ViewModel mapping (D-28).
/// </summary>
/// <remarks>
/// Derives from <see cref="InvalidOperationException" /> so existing cleaning callers'
/// catch sites continue to handle it as an actionable configuration failure without
/// requiring additional catch clauses (Phase 10 backwards-compatibility).
/// </remarks>
public sealed class ConfigPersistenceFailureException : InvalidOperationException
{
    /// <summary>
    /// Gets the typed safe failure payload that callers such as ViewModels may map.
    /// </summary>
    public ConfigPersistenceFailure Failure { get; }

    /// <summary>
    /// Creates an exception carrying the safe persistence failure payload and user-safe summary.
    /// </summary>
    /// <param name="failure">The typed failure payload produced by the persistence coordinator.</param>
    /// <param name="safeSummary">The safe exception message; must not contain raw exception details or stack traces.</param>
    public ConfigPersistenceFailureException(ConfigPersistenceFailure failure, string safeSummary)
        : base(safeSummary)
    {
        Failure = failure;
    }
}
