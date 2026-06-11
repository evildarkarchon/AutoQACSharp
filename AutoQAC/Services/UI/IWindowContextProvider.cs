using Microsoft.UI;
using Microsoft.UI.Xaml;

namespace AutoQAC.Services.UI;

public interface IWindowContextProvider
{
    bool TryGetContext(out WindowId windowId, out XamlRoot xamlRoot);

    void SetContext(WindowId windowId, XamlRoot xamlRoot);
}
