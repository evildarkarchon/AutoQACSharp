namespace AutoQAC.Services.UI;

public interface IUiFrameworkVersionProvider
{
    string DisplayName { get; }
    string Version { get; }
}
