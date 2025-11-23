using System.Threading.Tasks;

namespace AutoQAC.Services.UI;

public interface IFileDialogService
{
    Task<string?> OpenFileDialogAsync(
        string title,
        string filter,
        string? initialDirectory = null);
}
