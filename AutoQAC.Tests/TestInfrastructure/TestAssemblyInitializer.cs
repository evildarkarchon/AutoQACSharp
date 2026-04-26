using System.Runtime.CompilerServices;
using ReactiveUI.Builder;

namespace AutoQAC.Tests.TestInfrastructure;

internal static class TestAssemblyInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithPlatformServices()
            .WithCoreServices()
            .BuildApp();
    }
}
