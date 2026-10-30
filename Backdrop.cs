using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace NaxDeepL;

/// <summary>
/// Gives a window the dark Windows title bar and, where Windows supports it, the Mica backdrop
/// that tints the window with the colours of the desktop wallpaper.
/// </summary>
static class Backdrop
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int sz);

    [DllImport("dwmapi.dll")]
    static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    struct Margins { public int Left, Right, Top, Bottom; }

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_SYSTEMBACKDROP_TYPE     = 38;
    const int DWMSBT_MAINWINDOW             = 2;   // Mica

    // The backdrop attribute exists from Windows 11 22H2 on.
    const int FirstMicaBuild = 22621;

    /// <summary>
    /// Call once the window has a handle. Returns true when Mica is active, and the caller then
    /// makes the window background transparent. Otherwise the window keeps its solid background.
    /// </summary>
    public static bool Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var dark = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

        if (Environment.OSVersion.Version.Build < FirstMicaBuild) return false;

        var type = DWMSBT_MAINWINDOW;
        if (DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int)) != 0) return false;

        // WPF paints its own background over the whole client area. Letting the frame extend
        // into it and clearing WPF's background makes the backdrop visible behind the content.
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target })
            target.BackgroundColor = Colors.Transparent;
        return true;
    }
}
