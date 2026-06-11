using System;
using System.Threading.Tasks;
using Microsoft.Windows.AppLifecycle;
using WinRT;
using WinUIApplication = Microsoft.UI.Xaml.Application;

namespace AutoQAC
{
    internal sealed class Program
    {
        private const string InstanceKey = "AutoQAC";
        private static App? _app;

        [STAThread]
        public static void Main(string[] args)
        {
            ComWrappersSupport.InitializeComWrappers();

            var instance = AppInstance.FindOrRegisterForKey(InstanceKey);
            if (!instance.IsCurrent)
            {
                RedirectActivationToAsync(instance).GetAwaiter().GetResult();
                return;
            }

            instance.Activated += OnActivated;
            WinUIApplication.Start(_ =>
            {
                _app = new App();
            });
        }

        private static async Task RedirectActivationToAsync(AppInstance instance)
        {
            var activatedEventArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            await instance.RedirectActivationToAsync(activatedEventArgs);
        }

        private static void OnActivated(object? sender, AppActivationArguments args)
        {
            if (_app == null)
            {
                return;
            }

            App.ActivateMainWindow();
        }
    }
}
