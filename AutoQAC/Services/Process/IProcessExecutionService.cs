using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AutoQAC.Services.Process;

public interface IProcessExecutionService
{
    // Execute process with real-time output
    Task<ProcessResult> ExecuteAsync(
        ProcessStartInfo startInfo,
        IProgress<string>? outputProgress = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default);

    // Resource limit management
    Task<IDisposable> AcquireProcessSlotAsync(
        CancellationToken ct = default);
}

public sealed record ProcessResult
{
    public int ExitCode { get; init; }
    public List<string> OutputLines { get; init; } = new();
    public List<string> ErrorLines { get; init; } = new();
    public bool TimedOut { get; init; }
}
