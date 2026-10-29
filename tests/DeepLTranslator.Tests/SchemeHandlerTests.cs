using Microsoft.Win32;

namespace DeepLTranslator.Tests;

/// <summary>
/// Runs the deepl:// handler guard against a throw-away key under HKCU, never against the real
/// Software\Classes\deepl key.
/// </summary>
public sealed class SchemeHandlerTests : IDisposable
{
    readonly string _base = $@"Software\DeepLTranslator.Tests\{Guid.NewGuid():N}";
    string Scheme  => $@"{_base}\Classes\deepl";
    string Command => $@"{Scheme}\shell\open\command";
    string Backup  => $@"{_base}\App\LoginBackup";

    SchemeHandlerGuard Guard() => new(Registry.CurrentUser, Scheme, Backup);

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_base, throwOnMissingSubKey: false);
        using var parent = Registry.CurrentUser.OpenSubKey(@"Software\DeepLTranslator.Tests");
        if (parent is { SubKeyCount: 0, ValueCount: 0 })
            Registry.CurrentUser.DeleteSubKey(@"Software\DeepLTranslator.Tests", throwOnMissingSubKey: false);
    }

    bool Exists(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key != null;
    }

    string? CommandValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Command);
        return key?.GetValue(null) as string;
    }

    [Fact]
    public void login_without_existing_handler_leaves_no_registry_keys_behind()
    {
        Guard().Take(@"C:\App\app.exe");
        Assert.Equal("\"C:\\App\\app.exe\" \"%1\"", CommandValue());

        Guard().RestoreLeftover();

        Assert.False(Exists(Scheme));
        Assert.False(Exists(Backup));
        Assert.False(Exists($@"{_base}\App"));
    }

    [Fact]
    public void login_with_existing_handler_restores_original_command()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(Command))
            key.SetValue(null, "\"C:\\DeepL\\DeepL.exe\" \"%1\"");

        Guard().Take(@"C:\App\app.exe");
        Guard().RestoreLeftover();

        Assert.Equal("\"C:\\DeepL\\DeepL.exe\" \"%1\"", CommandValue());
        Assert.False(Exists(Backup));
    }

    [Fact]
    public void existing_scheme_key_without_command_gets_only_created_keys_removed()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(Scheme))
            key.SetValue("URL Protocol", "");

        Guard().Take(@"C:\App\app.exe");
        Guard().RestoreLeftover();

        Assert.True(Exists(Scheme));
        Assert.False(Exists($@"{Scheme}\shell"));
        using var scheme = Registry.CurrentUser.OpenSubKey(Scheme)!;
        Assert.Equal("", scheme.GetValue("URL Protocol"));
    }

    [Fact]
    public void second_login_after_crash_still_restores_real_original_handler()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(Command))
            key.SetValue(null, "\"C:\\DeepL\\DeepL.exe\" \"%1\"");

        Guard().Take(@"C:\App\app.exe");   // the app crashes here, restore never runs
        Guard().Take(@"C:\App\app.exe");   // next login after a restart
        Guard().RestoreLeftover();

        Assert.Equal("\"C:\\DeepL\\DeepL.exe\" \"%1\"", CommandValue());
    }

    [Fact]
    public void start_without_unfinished_login_changes_nothing()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(Command))
            key.SetValue(null, "\"C:\\DeepL\\DeepL.exe\" \"%1\"");

        Guard().RestoreLeftover();

        Assert.Equal("\"C:\\DeepL\\DeepL.exe\" \"%1\"", CommandValue());
    }
}
