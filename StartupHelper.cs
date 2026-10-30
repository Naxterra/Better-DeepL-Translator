using Microsoft.Win32;

namespace NaxDeepL;

public static class StartupHelper
{
    const string RunKey  = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string AppName = "Nax-DeepL";

    // Versions up to 1.1.0 registered themselves under this name.
    const string LegacyName = "DeepL-Translator";

    static string ExePath => Environment.ProcessPath
                             ?? System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;

    public static bool IsEnabled() => IsEnabled(Registry.CurrentUser, RunKey);

    public static void SetEnabled(bool enabled) => SetEnabled(enabled, ExePath, Registry.CurrentUser, RunKey);

    /// <summary>
    /// Replaces an entry of an older version, which points at that version's program file, with
    /// one for this app.
    /// </summary>
    public static void MoveLegacyEntry() => MoveLegacyEntry(ExePath, Registry.CurrentUser, RunKey);

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
        key.DeleteValue(LegacyName, throwOnMissingValue: false);
    }

    internal static void MoveLegacyEntry(string exe, RegistryKey root, string runKeyPath)
    {
        bool hasLegacy;
        using (var key = root.OpenSubKey(runKeyPath))
            hasLegacy = key?.GetValue(LegacyName) != null;
        if (hasLegacy) SetEnabled(true, exe, root, runKeyPath);
    }
}
