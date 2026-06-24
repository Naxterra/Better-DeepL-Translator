using Microsoft.Win32;

namespace DeepLTranslator;

public static class StartupHelper
{
    const string RunKey  = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string AppName = "DeepL-Translator";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppName) != null;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key == null) return;
        if (enabled)
        {
            var exe = Environment.ProcessPath
                   ?? System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
            key.SetValue(AppName, $"\"{exe}\"");
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }
}
