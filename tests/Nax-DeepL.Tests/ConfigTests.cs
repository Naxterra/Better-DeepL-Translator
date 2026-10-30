using System.IO;

namespace NaxDeepL.Tests;

public sealed class ConfigTests : IDisposable
{
    readonly string _dir  = Path.Combine(Path.GetTempPath(), "NaxDeepL.Tests", Guid.NewGuid().ToString("N"));
    string ConfigPath => Path.Combine(_dir, "config.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void login_tokens_saved_encrypted_never_in_plain_text()
    {
        var cfg = Config.Load(ConfigPath);
        cfg.DlAccess     = "access-secret-123";
        cfg.RefreshToken = "refresh-secret-456";
        cfg.Save();

        var json = File.ReadAllText(ConfigPath);
        Assert.DoesNotContain("access-secret-123", json);
        Assert.DoesNotContain("refresh-secret-456", json);

        var reloaded = Config.Load(ConfigPath);
        Assert.Equal("access-secret-123", reloaded.DlAccess);
        Assert.Equal("refresh-secret-456", reloaded.RefreshToken);
    }

    [Fact]
    public void old_plain_text_tokens_migrated_and_removed_from_file()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(ConfigPath, """
            {"dl_access":"old-access","refresh_token":"old-refresh","identity_token":"old-id","tgt_lang":"tr"}
            """);

        var cfg = Config.Load(ConfigPath);

        Assert.Equal("old-access", cfg.DlAccess);
        Assert.Equal("old-refresh", cfg.RefreshToken);
        Assert.Equal("tr", cfg.TgtLang);
        var json = File.ReadAllText(ConfigPath);
        Assert.DoesNotContain("old-access", json);
        Assert.DoesNotContain("old-refresh", json);
        Assert.DoesNotContain("old-id", json);
    }

    [Fact]
    public void settings_saved_and_reloaded_with_no_temporary_file_left()
    {
        var cfg = Config.Load(ConfigPath);
        cfg.TgtLang  = "en-GB";
        cfg.Geometry = "10,20,800,600";
        cfg.Save();
        cfg.Save();

        var reloaded = Config.Load(ConfigPath);
        Assert.Equal("en-GB", reloaded.TgtLang);
        Assert.Equal("10,20,800,600", reloaded.Geometry);
        Assert.Equal(["config.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void save_cannot_write_original_settings_file_unchanged()
    {
        var cfg = Config.Load(ConfigPath);
        cfg.TgtLang = "de";
        cfg.Save();
        var before = File.ReadAllText(ConfigPath);

        // Another program holds the temporary file open, so the new settings cannot be written.
        using (new FileStream(ConfigPath + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            cfg.TgtLang = "tr";
            cfg.Save();
        }

        Assert.Equal(before, File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void damaged_settings_file_loads_defaults_without_crashing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(ConfigPath, "{ this is not json");

        var cfg = Config.Load(ConfigPath);

        Assert.Equal("de", cfg.TgtLang);
        Assert.Null(cfg.RefreshToken);
    }

    [Fact]
    public void settings_of_old_app_name_moved_with_login_intact()
    {
        var oldDir = Path.Combine(_dir, "DeepL-Translator");
        var newDir = Path.Combine(_dir, "Nax-DeepL");
        var old = Config.Load(Path.Combine(oldDir, "config.json"));
        old.RefreshToken = "refresh-secret-456";
        old.TgtLang = "tr";
        old.Save();

        Config.MoveLegacyFolder(oldDir, newDir);

        Assert.False(Directory.Exists(oldDir));
        var moved = Config.Load(Path.Combine(newDir, "config.json"));
        Assert.Equal("refresh-secret-456", moved.RefreshToken);
        Assert.Equal("tr", moved.TgtLang);
    }

    [Fact]
    public void settings_of_old_app_name_never_replace_existing_new_settings()
    {
        var oldDir = Path.Combine(_dir, "DeepL-Translator");
        var newDir = Path.Combine(_dir, "Nax-DeepL");
        var old = Config.Load(Path.Combine(oldDir, "config.json"));
        old.TgtLang = "tr";
        old.Save();
        var current = Config.Load(Path.Combine(newDir, "config.json"));
        current.TgtLang = "en-US";
        current.Save();

        Config.MoveLegacyFolder(oldDir, newDir);

        Assert.True(Directory.Exists(oldDir));
        Assert.Equal("en-US", Config.Load(Path.Combine(newDir, "config.json")).TgtLang);
    }
}
