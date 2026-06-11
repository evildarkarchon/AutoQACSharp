using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Windows.Storage.Pickers;

namespace AutoQAC.Services.UI;

public sealed class FileDialogService : IFileDialogService
{
    private readonly IWindowContextProvider _windowContextProvider;
    private readonly IUiDispatcher _uiDispatcher;

    public FileDialogService(
        IWindowContextProvider windowContextProvider,
        IUiDispatcher uiDispatcher)
    {
        _windowContextProvider = windowContextProvider;
        _uiDispatcher = uiDispatcher;
    }

    public Task<string?> OpenFileDialogAsync(
        string title,
        string filter,
        string? initialDirectory = null)
    {
        return InvokeOnUiThreadAsync(async () =>
        {
            if (!_windowContextProvider.TryGetContext(out var windowId, out _))
            {
                return null;
            }

            var picker = new FileOpenPicker(windowId)
            {
                Title = title
            };

            SetSuggestedStartFolder(picker, initialDirectory);

            foreach (var choice in FileDialogFilterMapper.BuildFileTypeChoices(filter))
            {
                picker.FileTypeChoices.Add(choice.Key, choice.Value.ToList());
            }

            var result = await picker.PickSingleFileAsync();
            return result?.Path;
        });
    }

    public Task<string?> SaveFileDialogAsync(
        string title,
        string filter,
        string? defaultFileName = null,
        string? initialDirectory = null)
    {
        return InvokeOnUiThreadAsync(async () =>
        {
            if (!_windowContextProvider.TryGetContext(out var windowId, out _))
            {
                return null;
            }

            var picker = new FileSavePicker(windowId)
            {
                Title = title,
                SuggestedFileName = defaultFileName
            };

            SetSuggestedStartFolder(picker, initialDirectory);

            foreach (var choice in FileDialogFilterMapper.BuildFileTypeChoices(filter))
            {
                picker.FileTypeChoices.Add(choice.Key, choice.Value.ToList());
            }

            var defaultExtension = FileDialogFilterMapper.BuildExtensionList(filter)
                .FirstOrDefault(extension => extension != "*");
            if (!string.IsNullOrWhiteSpace(defaultExtension))
            {
                picker.DefaultFileExtension = defaultExtension;
            }

            var result = await picker.PickSaveFileAsync();
            return result?.Path;
        });
    }

    public Task<string?> OpenFolderDialogAsync(
        string title,
        string? initialDirectory = null)
    {
        return InvokeOnUiThreadAsync(async () =>
        {
            if (!_windowContextProvider.TryGetContext(out var windowId, out _))
            {
                return null;
            }

            var picker = new FolderPicker(windowId)
            {
                Title = title
            };

            SetSuggestedStartFolder(picker, initialDirectory);

            var result = await picker.PickSingleFolderAsync();
            return result?.Path;
        });
    }

    private async Task<T> InvokeOnUiThreadAsync<T>(Func<Task<T>> action)
    {
        T result = default!;
        await _uiDispatcher.InvokeAsync(async () => result = await action());
        return result;
    }

    private static void SetSuggestedStartFolder(FileOpenPicker picker, string? initialDirectory)
    {
        var directory = GetExistingDirectory(initialDirectory);
        if (directory is not null)
        {
            picker.SuggestedStartFolder = directory;
        }
    }

    private static void SetSuggestedStartFolder(FileSavePicker picker, string? initialDirectory)
    {
        var directory = GetExistingDirectory(initialDirectory);
        if (directory is not null)
        {
            picker.SuggestedStartFolder = directory;
        }
    }

    private static void SetSuggestedStartFolder(FolderPicker picker, string? initialDirectory)
    {
        var directory = GetExistingDirectory(initialDirectory);
        if (directory is not null)
        {
            picker.SuggestedStartFolder = directory;
        }
    }

    private static string? GetExistingDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (Directory.Exists(path))
        {
            return path;
        }

        var parentDirectory = Path.GetDirectoryName(path);
        return !string.IsNullOrWhiteSpace(parentDirectory) && Directory.Exists(parentDirectory)
            ? parentDirectory
            : null;
    }
}
