using Microsoft.UI.Xaml;

namespace AutoQAC.Services.UI;

public sealed class WinAppLifetime : IAppLifetime
{
    public void Shutdown()
    {
        Application.Current?.Exit();
    }
}
