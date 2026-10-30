using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NaxDeepL;

/// <summary>
/// Paints the Windows title bar and window border in the window's own black, so the title bar
/// and the content read as one surface instead of a grey strip above a black window.
/// </summary>
static class TitleBar
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int sz);

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_BORDER_COLOR            = 34;
    const int DWMWA_CAPTION_COLOR           = 35;
    const int DWMWA_TEXT_COLOR              = 36;

    // Colours are COLORREF values, which store the channels as 0x00BBGGRR.
    const int CaptionBlack = 0x000000;
    const int BorderGrey   = 0x282323;
    const int TextWhite    = 0xFFFFFF;

    /// <summary>
    /// Call once the window has a handle. Windows 10 understands only the dark mode attribute
    /// and ignores the colour attributes, so the call is safe on every supported version.
    /// </summary>
    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        Set(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, 1);
        Set(hwnd, DWMWA_CAPTION_COLOR, CaptionBlack);
        Set(hwnd, DWMWA_BORDER_COLOR, BorderGrey);
        Set(hwnd, DWMWA_TEXT_COLOR, TextWhite);
    }

    static void Set(IntPtr hwnd, int attr, int value) =>
        DwmSetWindowAttribute(hwnd, attr, ref value, sizeof(int));
}
