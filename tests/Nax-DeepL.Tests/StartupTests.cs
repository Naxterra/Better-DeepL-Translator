using Microsoft.Win32;

namespace NaxDeepL.Tests;

/// <summary>
/// Runs the "Start with Windows" helper against a throw-away key under HKCU, never against the
/// real Run key.
/// </summary>
public sealed class StartupTests : IDisposable
{
    readonly string _base = $@"Software\NaxDeepL.Tests\{Guid.NewGuid():N}";
    string Run => $@"{_base}\Run";

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_base, throwOnMissingSubKey: false);
        using var parent = Registry.CurrentUser.OpenSubKey(@"Software\NaxDeepL.Tests");
        if (parent is { SubKeyCount: 0, ValueCount: 0 })
            Registry.CurrentUser.DeleteSubKey(@"Software\NaxDeepL.Tests", throwOnMissingSubKey: false);
    }

    [Fact]
    public void start_with_windows_switched_on_registers_quoted_app_path()
    {
        StartupHelper.SetEnabled(true, @"C:\Apps\Nax DeepL\Nax-DeepL.exe", Registry.CurrentUser, Run);

        using var key = Registry.CurrentUser.OpenSubKey(Run);
        Assert.Equal("\"C:\\Apps\\Nax DeepL\\Nax-DeepL.exe\"", key?.GetValue("Nax-DeepL"));
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
        Assert.Null(run?.GetValue("Nax-DeepL"));
        Assert.Equal("\"C:\\Other\\other.exe\"", run?.GetValue("OtherProgram"));
        Assert.False(StartupHelper.IsEnabled(Registry.CurrentUser, Run));
    }

    [Fact]
    public void start_with_windows_switched_off_also_removes_entry_under_old_app_name()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(Run))
            key.SetValue("DeepL-Translator", "\"C:\\Old\\Better DeepL Translator.exe\"");

        StartupHelper.SetEnabled(false, @"C:\Apps\Nax-DeepL.exe", Registry.CurrentUser, Run);

        using var run = Registry.CurrentUser.OpenSubKey(Run);
        Assert.Null(run?.GetValue("DeepL-Translator"));
        Assert.Null(run?.GetValue("Nax-DeepL"));
    }

    [Fact]
    public void old_start_with_windows_entry_replaced_by_one_for_renamed_app()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(Run))
            key.SetValue("DeepL-Translator", "\"C:\\Old\\Better DeepL Translator.exe\"");

        StartupHelper.MoveLegacyEntry(@"C:\Apps\Nax-DeepL.exe", Registry.CurrentUser, Run);

        using var run = Registry.CurrentUser.OpenSubKey(Run);
        Assert.Null(run?.GetValue("DeepL-Translator"));
        Assert.Equal("\"C:\\Apps\\Nax-DeepL.exe\"", run?.GetValue("Nax-DeepL"));
    }

    [Fact]
    public void start_without_old_entry_does_not_switch_on_start_with_windows()
    {
        StartupHelper.MoveLegacyEntry(@"C:\Apps\Nax-DeepL.exe", Registry.CurrentUser, Run);

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
