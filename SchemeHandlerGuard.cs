using Microsoft.Win32;

namespace NaxDeepL;

/// <summary>
/// Temporarily points the deepl:// link handler at this app during login and puts the previous
/// state back afterwards. The previous state is also written to a backup key first, so that a
/// crash during login is undone on the next start instead of leaving the handler behind.
/// </summary>
public sealed class SchemeHandlerGuard(RegistryKey root, string schemeKeyPath, string backupKeyPath)
{
    const string CommandSubKey = @"shell\open\command";
    const string BackupExisted = "ExistedLevels";
    const string BackupValue   = "SchemeCommand";
    const string BackupHadUrlProtocol = "HadUrlProtocol";

    // Without this value Windows does not treat the key as a link scheme, so browsers report
    // that no app can open deepl:// links.
    const string UrlProtocol = "URL Protocol";

    public static SchemeHandlerGuard ForCurrentUser() => new(
        Registry.CurrentUser, @"Software\Classes\deepl", @"Software\Nax-DeepL\LoginBackup");

    /// <summary>
    /// The guard with the backup key of versions up to 1.1.0, so that a login those versions
    /// left unfinished is still undone.
    /// </summary>
    public static SchemeHandlerGuard LegacyForCurrentUser() => new(
        Registry.CurrentUser, @"Software\Classes\deepl", @"Software\DeepL-Translator\LoginBackup");

    string CommandKeyPath => $@"{schemeKeyPath}\{CommandSubKey}";

    // The scheme key and each key below it down to "command", shallowest first.
    string[] Levels => [schemeKeyPath, $@"{schemeKeyPath}\shell", $@"{schemeKeyPath}\shell\open", CommandKeyPath];

    /// <summary>Points deepl:// at <paramref name="exePath"/> after saving the current state.</summary>
    public void Take(string exePath)
    {
        // A backup that is still there belongs to an earlier, unfinished login. Restoring it
        // first keeps the real original state instead of saving our own handler as "original".
        RestoreLeftover();

        // Bit i is set when Levels[i] already existed, so restore can delete exactly what we created.
        var existed = 0;
        var levels  = Levels;
        for (var i = 0; i < levels.Length; i++)
        {
            using var level = root.OpenSubKey(levels[i]);
            if (level != null) existed |= 1 << i;
        }
        string? previous;
        using (var cmd = root.OpenSubKey(CommandKeyPath))
            previous = cmd?.GetValue(null) as string;
        bool hadUrlProtocol;
        using (var scheme = root.OpenSubKey(schemeKeyPath))
            hadUrlProtocol = scheme?.GetValue(UrlProtocol) != null;

        using (var backup = root.CreateSubKey(backupKeyPath))
        {
            backup.SetValue(BackupExisted, existed, RegistryValueKind.DWord);
            backup.SetValue(BackupHadUrlProtocol, hadUrlProtocol ? 1 : 0, RegistryValueKind.DWord);
            if (previous != null) backup.SetValue(BackupValue, previous);
        }

        using (var scheme = root.CreateSubKey(schemeKeyPath))
            if (!hadUrlProtocol) scheme.SetValue(UrlProtocol, "");
        using var key = root.CreateSubKey(CommandKeyPath);
        key.SetValue(null, $"\"{exePath}\" \"%1\"");
    }

    /// <summary>Puts back the state saved by <see cref="Take"/>, if a backup exists.</summary>
    public void RestoreLeftover()
    {
        int existed;
        string? previous;
        bool hadUrlProtocol;
        using (var backup = root.OpenSubKey(backupKeyPath))
        {
            if (backup == null) return;
            existed        = backup.GetValue(BackupExisted) is int i ? i : 0;
            previous       = backup.GetValue(BackupValue) as string;
            hadUrlProtocol = backup.GetValue(BackupHadUrlProtocol) is int u && u == 1;
        }

        if ((existed & 1) != 0 && !hadUrlProtocol)
        {
            using var scheme = root.OpenSubKey(schemeKeyPath, writable: true);
            scheme?.DeleteValue(UrlProtocol, throwOnMissingValue: false);
        }

        var levels = Levels;
        if ((existed & (1 << (levels.Length - 1))) != 0)
        {
            using var cmd = root.CreateSubKey(CommandKeyPath);
            if (previous != null) cmd.SetValue(null, previous);
            else                  cmd.DeleteValue("", throwOnMissingValue: false);
        }
        // Remove every key the login created, deepest first, so nothing of it stays behind.
        for (var i = levels.Length - 1; i >= 0; i--)
            if ((existed & (1 << i)) == 0)
                root.DeleteSubKeyTree(levels[i], throwOnMissingSubKey: false);

        root.DeleteSubKeyTree(backupKeyPath, throwOnMissingSubKey: false);
        DeleteParentIfEmpty(backupKeyPath);
    }

    void DeleteParentIfEmpty(string path)
    {
        var cut = path.LastIndexOf('\\');
        if (cut <= 0) return;
        var parent = path[..cut];
        using (var key = root.OpenSubKey(parent))
        {
            if (key == null || key.SubKeyCount > 0 || key.ValueCount > 0) return;
        }
        root.DeleteSubKey(parent, throwOnMissingSubKey: false);
    }
}
