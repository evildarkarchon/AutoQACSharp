using Avalonia;

namespace AutoQAC.Services.UI;

public sealed class AvaloniaUiFrameworkVersionProvider : IUiFrameworkVersionProvider
{
    public string DisplayName => "Avalonia";

    public string Version
    {
        get
        {
            var version = typeof(Application).Assembly.GetName().Version;
            return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "Unknown";
        }
    }
}
