using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace DeepLTranslator;

public static class CookieExtractor
{
    static readonly string Local  = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static readonly string Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    // Chromium-family profiles: (UserDataDir, relative cookie path)
    static readonly (string Dir, string CookieRel)[] ChromiumProfiles =
    [
        // DeepL's own embedded browser (most authoritative)
        (Path.Combine(Local,   "DeepL_SE",      "cache"),                                 Path.Combine("Default", "Cookies")),
        // Brave
        (Path.Combine(Local,   "BraveSoftware",  "Brave-Browser",  "User Data"),          Path.Combine("Default", "Network", "Cookies")),
        // Chrome
        (Path.Combine(Local,   "Google",         "Chrome",         "User Data"),          Path.Combine("Default", "Network", "Cookies")),
        // Edge
        (Path.Combine(Local,   "Microsoft",      "Edge",           "User Data"),          Path.Combine("Default", "Network", "Cookies")),
        // Opera
        (Path.Combine(Roaming, "Opera Software", "Opera Stable"),                         Path.Combine("Network", "Cookies")),
        // Vivaldi
        (Path.Combine(Local,   "Vivaldi",        "User Data"),                            Path.Combine("Default", "Network", "Cookies")),
        // Arc (Windows preview)
        (Path.Combine(Local,   "Arc",            "User Data"),                            Path.Combine("Default", "Network", "Cookies")),
    ];

    public static Dictionary<string, string>? ExtractAll()
    {
        // Try Chromium browsers first
        foreach (var (dir, cookieRel) in ChromiumProfiles)
        {
            try
            {
                var lsPath = Path.Combine(dir, "Local State");
                var dbPath = Path.Combine(dir, cookieRel);
                if (!File.Exists(lsPath) || !File.Exists(dbPath)) continue;

                using var doc = JsonDocument.Parse(File.ReadAllText(lsPath));
                var encB64 = doc.RootElement
                    .GetProperty("os_crypt")
                    .GetProperty("encrypted_key")
                    .GetString()!;
                var aesKey = ProtectedData.Unprotect(
                    Convert.FromBase64String(encB64)[5..], null, DataProtectionScope.CurrentUser);

                var result = new Dictionary<string, string>();
                foreach (var (name, blob) in ReadChromiumCookies(dbPath))
                {
                    var val = DecryptChromium(blob, aesKey);
                    if (!string.IsNullOrEmpty(val) && !result.ContainsKey(name))
                        result[name] = val;
                }
                if (result.Count > 0) return result;
            }
            catch { }
        }

        // Try Firefox (plaintext cookie values)
        try
        {
            var result = ReadFirefoxCookies();
            if (result?.Count > 0) return result;
        }
        catch { }

        return null;
    }

    public static string? Extract()
    {
        var all = ExtractAll();
        return all != null && all.TryGetValue("dl_access", out var tok) ? tok : null;
    }

    // ── Firefox ──────────────────────────────────────────────────────────────

    static Dictionary<string, string>? ReadFirefoxCookies()
    {
        var profilesDir = Path.Combine(Roaming, "Mozilla", "Firefox", "Profiles");
        if (!Directory.Exists(profilesDir)) return null;

        // Prefer default-release profile, fall back to any profile with cookies.sqlite
        var profiles = Directory.GetDirectories(profilesDir)
            .OrderByDescending(d => d.EndsWith(".default-release") ? 1 : 0);

        const string Sql = """
            SELECT name, value FROM moz_cookies
            WHERE host LIKE '%.deepl.com'
            ORDER BY lastAccessed DESC
            """;

        foreach (var profile in profiles)
        {
            var dbPath = Path.Combine(profile, "cookies.sqlite");
            if (!File.Exists(dbPath)) continue;
            try
            {
                var tmp = Path.GetTempFileName();
                try
                {
                    CopyShared(dbPath, tmp);
                    using var conn = new SqliteConnection($"Data Source={tmp}");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = Sql;
                    var result = new Dictionary<string, string>();
                    using var rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        var name = rdr.GetString(0);
                        var val  = rdr.GetString(1);
                        if (!string.IsNullOrEmpty(val) && !result.ContainsKey(name))
                            result[name] = val;
                    }
                    if (result.Count > 0) return result;
                }
                finally { try { File.Delete(tmp); } catch { } }
            }
            catch { }
        }
        return null;
    }

    // ── Chromium ─────────────────────────────────────────────────────────────

    static string? DecryptChromium(byte[] data, byte[] key)
    {
        try
        {
            if (data.Length > 3 && data[0] == 'v' && data[1] == '1' && data[2] == '0')
            {
                var nonce  = data[3..15];
                var cipher = data[15..^16];
                var tag    = data[^16..];
                var plain  = new byte[cipher.Length];
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(nonce, cipher, tag, plain);
                return Encoding.UTF8.GetString(plain);
            }
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser));
        }
        catch { return null; }
    }

    static List<(string Name, byte[] Value)> ReadChromiumCookies(string dbPath)
    {
        const string Sql = """
            SELECT name, encrypted_value FROM cookies
            WHERE host_key LIKE '%.deepl.com'
            ORDER BY last_access_utc DESC
            """;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            var tmp = Path.GetTempFileName();
            try
            {
                CopyShared(dbPath, tmp);
                foreach (var sfx in new[] { "-wal", "-shm" })
                    if (File.Exists(dbPath + sfx))
                        try { CopyShared(dbPath + sfx, tmp + sfx); } catch { }

                using var conn = new SqliteConnection($"Data Source={tmp}");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = Sql;
                var rows = new List<(string, byte[])>();
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    rows.Add((rdr.GetString(0), rdr.GetFieldValue<byte[]>(1)));
                return rows;
            }
            catch { if (attempt < 4) Thread.Sleep(150); }
            finally
            {
                try { File.Delete(tmp); } catch { }
                foreach (var sfx in new[] { "-wal", "-shm" })
                    try { File.Delete(tmp + sfx); } catch { }
            }
        }
        return [];
    }

    static void CopyShared(string src, string dst)
    {
        using var r = new FileStream(src, FileMode.Open, FileAccess.Read,
                                     FileShare.ReadWrite | FileShare.Delete);
        using var w = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None);
        r.CopyTo(w);
    }
}
