using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AutoQAC.Services.Backup;

/// <summary>
/// Deletes backup session directories using the local filesystem.
/// </summary>
public sealed class DirectoryBackupSessionDeleter : IBackupSessionDeleter
{
    /// <inheritdoc />
    public Task DeleteAsync(string sessionDirectory, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Directory.Delete(sessionDirectory, recursive: true);
        return Task.CompletedTask;
    }
}
