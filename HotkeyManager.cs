using System.Runtime.InteropServices;
using System.Windows;
using Application = System.Windows.Application;

namespace DeepLTranslator;

/// <summary>Detects Ctrl+C+C (two Ctrl+C within 500 ms) via a low-level keyboard hook.</summary>
public sealed class HotkeyManager : IDisposable
{
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN     = 0x0100;
    const int VK_C           = 0x43;
    const int VK_CONTROL     = 0x11;

    delegate IntPtr LlKbProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, LlKbProc proc, IntPtr mod, uint tid);
    [DllImport("user32.dll")] static extern bool   UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int n, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern short  GetAsyncKeyState(int vk);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string? name);

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr extra; }

    readonly LlKbProc _proc;   // must hold reference to prevent GC
    readonly IntPtr   _hook;
    DateTime          _lastCtrlC = DateTime.MinValue;

    public event Action? CtrlCCPressed;

    public HotkeyManager()
    {
        _proc = HookCallback;
        using var proc = System.Diagnostics.Process.GetCurrentProcess();
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc,
                    GetModuleHandle(proc.MainModule!.ModuleName), 0);
    }

    IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == WM_KEYDOWN)
        {
            var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (kb.vkCode == VK_C && (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0)
            {
                var now = DateTime.UtcNow;
                if ((now - _lastCtrlC).TotalMilliseconds < 500)
                {
                    _lastCtrlC = DateTime.MinValue;
                    // Small delay so the clipboard has the new content
                    Task.Delay(80).ContinueWith(_ =>
                        Application.Current?.Dispatcher.Invoke(
                            () => CtrlCCPressed?.Invoke()));
                }
                else
                {
                    _lastCtrlC = now;
                }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => UnhookWindowsHookEx(_hook);
}
