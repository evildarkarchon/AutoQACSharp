using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace AutoQAC.Services.UI;

public sealed class AvaloniaAppLifetime : IAppLifetime
{
    public void Shutdown()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
