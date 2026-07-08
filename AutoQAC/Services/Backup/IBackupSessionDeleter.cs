using System.Threading;
using System.Threading.Tasks;

namespace AutoQAC.Services.Backup;

/// <summary>
/// Deletes a backup session directory through an injectable seam so retention retry and cancellation behavior is testable.
/// </summary>
public interface IBackupSessionDeleter
{
    /// <summary>
    /// Deletes the session directory and all of its contents.
    /// </summary>
    /// <param name="sessionDirectory">Absolute path to the backup session directory selected for retention cleanup.</param>
    /// <param name="ct">Cancellation token observed before deletion begins.</param>
    Task DeleteAsync(string sessionDirectory, CancellationToken ct);
}
