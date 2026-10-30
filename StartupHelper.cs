using Microsoft.Win32;

namespace DeepLTranslator;

public static class StartupHelper
{
    const string RunKey  = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string AppName = "DeepL-Translator";

    public static bool IsEnabled() => IsEnabled(Registry.CurrentUser, RunKey);

    public static void SetEnabled(bool enabled) =>
        SetEnabled(enabled, Environment.ProcessPath
                            ?? System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName,
                   Registry.CurrentUser, RunKey);

    internal static bool IsEnabled(RegistryKey root, string runKeyPath)
    {
        using var key = root.OpenSubKey(runKeyPath);
        return key?.GetValue(AppName) != null;
    }

    internal static void SetEnabled(bool enabled, string exe, RegistryKey root, string runKeyPath)
    {
        using var key = enabled ? root.CreateSubKey(runKeyPath) : root.OpenSubKey(runKeyPath, writable: true);
        if (key == null) return;
        if (enabled)
            key.SetValue(AppName, $"\"{exe}\"");
        else
            key.DeleteValue(AppName, throwOnMissingValue: false);
    }
}
