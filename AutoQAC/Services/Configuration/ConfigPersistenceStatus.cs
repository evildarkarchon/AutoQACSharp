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
    RaceRejected,
    Canceled,
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
