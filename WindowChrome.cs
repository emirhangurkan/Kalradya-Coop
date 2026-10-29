using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
namespace KalradyaCoop;
public static class WindowChrome
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
    public static void Install(Window window) => window.SourceInitialized += (_, _) =>
    {
        try { int dark = 1; DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref dark, sizeof(int)); } catch { }
    };
}
