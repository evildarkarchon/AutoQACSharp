using Microsoft.UI.Xaml;

namespace AutoQAC.Services.UI;

public sealed class WinUiFrameworkVersionProvider : IUiFrameworkVersionProvider
{
    public string DisplayName => "WinUI 3";

    public string Version => typeof(Application).Assembly.GetName().Version?.ToString() ?? "Unknown";
}
