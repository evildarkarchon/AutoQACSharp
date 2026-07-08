using System;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace AutoQAC.Views.Helpers;

public static partial class WindowSizing
{
    public static void Resize(Window window, int width, int height)
    {
        var scale = GetScale(window);
        window.AppWindow.Resize(new SizeInt32(
            (int)Math.Round(width * scale),
            (int)Math.Round(height * scale)));
    }

    private static double GetScale(Window window)
    {
        try
        {
            var hwnd = Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
            return hwnd == IntPtr.Zero ? 1.0 : GetDpiForWindow(hwnd) / 96.0;
        }
        catch
        {
            return 1.0;
        }
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr hwnd);
}
