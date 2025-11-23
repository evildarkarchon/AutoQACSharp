using System;
using System.Threading;
using System.Threading.Tasks;

namespace AutoQAC.Services.Cleaning;

public interface ICleaningOrchestrator
{
    // Start cleaning workflow
    Task StartCleaningAsync(CancellationToken ct = default);

    // Stop current operation
    void StopCleaning();
}
