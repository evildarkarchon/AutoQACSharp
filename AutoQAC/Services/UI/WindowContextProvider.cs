using System.Threading;
using Microsoft.UI;
using Microsoft.UI.Xaml;

namespace AutoQAC.Services.UI;

public sealed class WindowContextProvider : IWindowContextProvider
{
    private readonly Lock _gate = new();
    private WindowId? _windowId;
    private XamlRoot? _xamlRoot;

    public bool TryGetContext(out WindowId windowId, out XamlRoot xamlRoot)
    {
        lock (_gate)
        {
            if (_windowId.HasValue && _xamlRoot is not null)
            {
                windowId = _windowId.Value;
                xamlRoot = _xamlRoot;
                return true;
            }
        }

        windowId = default;
        xamlRoot = null!;
        return false;
    }

    public void SetContext(WindowId windowId, XamlRoot xamlRoot)
    {
        lock (_gate)
        {
            _windowId = windowId;
            _xamlRoot = xamlRoot;
        }
    }
}
