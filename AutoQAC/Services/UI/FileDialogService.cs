using System.Threading.Tasks;

namespace AutoQAC.Services.UI;

public sealed class FileDialogService : IFileDialogService
{
    public Task<string?> OpenFileDialogAsync(
        string title,
        string filter,
        string? initialDirectory = null)
    {
        return Task.FromResult<string?>(null);
    }

    public Task<string?> SaveFileDialogAsync(
        string title,
        string filter,
        string? defaultFileName = null,
        string? initialDirectory = null)
    {
        return Task.FromResult<string?>(null);
    }

    public Task<string?> OpenFolderDialogAsync(
        string title,
        string? initialDirectory = null)
    {
        return Task.FromResult<string?>(null);
    }
}
