using Microsoft.Win32;

namespace DeepLTranslator.Tests;

/// <summary>
/// Runs the "Start with Windows" helper against a throw-away key under HKCU, never against the
/// real Run key.
/// </summary>
public sealed class StartupTests : IDisposable
{
    readonly string _base = $@"Software\DeepLTranslator.Tests\{Guid.NewGuid():N}";
    string Run => $@"{_base}\Run";

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_base, throwOnMissingSubKey: false);
        using var parent = Registry.CurrentUser.OpenSubKey(@"Software\DeepLTranslator.Tests");
        if (parent is { SubKeyCount: 0, ValueCount: 0 })
            Registry.CurrentUser.DeleteSubKey(@"Software\DeepLTranslator.Tests", throwOnMissingSubKey: false);
    }

    [Fact]
    public void start_with_windows_switched_on_registers_quoted_app_path()
    {
        StartupHelper.SetEnabled(true, @"C:\Apps\Better DeepL Translator.exe", Registry.CurrentUser, Run);

        using var key = Registry.CurrentUser.OpenSubKey(Run);
        Assert.Equal("\"C:\\Apps\\Better DeepL Translator.exe\"", key?.GetValue("DeepL-Translator"));
        Assert.True(StartupHelper.IsEnabled(Registry.CurrentUser, Run));
    }

    [Fact]
    public void start_with_windows_switched_off_removes_entry_and_keeps_other_programs()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(Run))
            key.SetValue("OtherProgram", "\"C:\\Other\\other.exe\"");
        StartupHelper.SetEnabled(true, @"C:\Apps\app.exe", Registry.CurrentUser, Run);

        StartupHelper.SetEnabled(false, @"C:\Apps\app.exe", Registry.CurrentUser, Run);

        using var run = Registry.CurrentUser.OpenSubKey(Run);
        Assert.Null(run?.GetValue("DeepL-Translator"));
        Assert.Equal("\"C:\\Other\\other.exe\"", run?.GetValue("OtherProgram"));
        Assert.False(StartupHelper.IsEnabled(Registry.CurrentUser, Run));
    }

    [Fact]
    public void start_with_windows_switched_off_when_never_on_changes_nothing()
    {
        StartupHelper.SetEnabled(false, @"C:\Apps\app.exe", Registry.CurrentUser, Run);

        using var key = Registry.CurrentUser.OpenSubKey(Run);
        Assert.Null(key);
    }
}
