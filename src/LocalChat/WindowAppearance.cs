using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LocalChat;

internal static class WindowAppearance
{
    // Native caption colors: https://learn.microsoft.com/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute
    private const int CaptionColor = 35;
    private const int TextColor = 36;

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);

    internal static void MatchCaptionToBackground(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || SystemParameters.HighContrast) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        if (window.Background is SolidColorBrush background) SetColor(handle, CaptionColor, background.Color);
        if (window.Foreground is SolidColorBrush foreground) SetColor(handle, TextColor, foreground.Color);
    }

    private static void SetColor(IntPtr handle, int attribute, Color color)
    {
        // COLORREF is 0x00BBGGRR, not the WPF ARGB layout.
        uint value = (uint)(color.R | color.G << 8 | color.B << 16);
        _ = DwmSetWindowAttribute(handle, attribute, ref value, sizeof(uint));
    }
}
