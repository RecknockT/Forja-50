using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MiIALocal.Desktop;

internal static class NativeWindowTheme
{
    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) => Apply(window);
        window.Activated += (_, _) => Apply(window);
    }

    private static void Apply(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var dark = 1;
        var caption = 0x00201810; // COLORREF: #101820.
        var text = 0x00F1ECE2; // COLORREF: #E2ECF1.
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        _ = DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
        _ = DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
}
