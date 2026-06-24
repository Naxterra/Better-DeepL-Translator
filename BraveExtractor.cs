using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace DeepLTranslator;

public static class BraveExtractor
{
    // Browser profiles to search, in preference order
    static readonly (string UserDataDir, string CookiePath)[] Profiles =
    [
        // DeepL's own embedded Chromium browser (most authoritative source)
        (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
             "DeepL_SE", "cache"),
         Path.Combine("Default", "Cookies")),
        // Brave
        (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
             "BraveSoftware", "Brave-Browser", "User Data"),
         Path.Combine("Default", "Network", "Cookies")),
        // Chrome
        (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
             "Google", "Chrome", "User Data"),
         Path.Combine("Default", "Network", "Cookies")),
        // Edge
        (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
             "Microsoft", "Edge", "User Data"),
         Path.Combine("Default", "Network", "Cookies")),
    ];

    /// <summary>Extracts all deepl.com cookies from the first available browser. Returns null on failure.</summary>
    public static Dictionary<string, string>? ExtractAll()
    {
        foreach (var (userDataDir, cookieRel) in Profiles)
        {
            try
            {
                var lsPath = Path.Combine(userDataDir, "Local State");
                var dbPath = Path.Combine(userDataDir, cookieRel);
                if (!File.Exists(lsPath) || !File.Exists(dbPath)) continue;

                using var lsDoc = JsonDocument.Parse(File.ReadAllText(lsPath));
                var encB64 = lsDoc.RootElement
                    .GetProperty("os_crypt")
                    .GetProperty("encrypted_key")
                    .GetString()!;
                var encKey = Convert.FromBase64String(encB64);
                var aesKey = ProtectedData.Unprotect(encKey[5..], null, DataProtectionScope.CurrentUser);

                var rows   = ReadCookies(dbPath);
                var result = new Dictionary<string, string>();
                foreach (var (name, blob) in rows)
                {
                    var val = Decrypt(blob, aesKey);
                    if (!string.IsNullOrEmpty(val) && !result.ContainsKey(name))
                        result[name] = val;
                }
                if (result.Count > 0) return result;
            }
            catch { }
        }
        return null;
    }

    /// <summary>Returns just the dl_access JWT, or null.</summary>
    public static string? Extract()
    {
        var all = ExtractAll();
        return all != null && all.TryGetValue("dl_access", out var tok) ? tok : null;
    }

    static string? Decrypt(byte[] data, byte[] key)
    {
        try
        {
            if (data.Length > 3 && data[0] == 'v' && data[1] == '1' && data[2] == '0')
            {
                // Chrome/Brave v10 cookie: 3B prefix + 12B nonce + ciphertext + 16B tag
                var nonce     = data[3..15];
                var cipher    = data[15..^16];
                var tag       = data[^16..];
                var plain     = new byte[cipher.Length];
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(nonce, cipher, tag, plain);
                return Encoding.UTF8.GetString(plain);
            }
            // Legacy DPAPI cookie
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser));
        }
        catch { return null; }
    }

    static List<(string Name, byte[] Value)> ReadCookies(string dbPath)
    {
        const string Sql = """
            SELECT name, encrypted_value FROM cookies
            WHERE host_key LIKE '%.deepl.com'
            ORDER BY last_access_utc DESC
            """;

        // Retry up to 5 times with explicit FileShare.ReadWrite|Delete
        // This is the key difference from Python — C# FileStream maps directly
        // to CreateFile with FILE_SHARE_READ|WRITE|DELETE, working even while Brave runs.
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
                {
                    var blob = rdr.GetFieldValue<byte[]>(1);
                    rows.Add((rdr.GetString(0), blob));
                }
                return rows;
            }
            catch
            {
                if (attempt < 4) Thread.Sleep(150);
            }
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
        // FileShare.ReadWrite|Delete → Win32 FILE_SHARE_READ|WRITE|DELETE
        // Allows reading while Brave holds the file open for writing
        using var r = new FileStream(src, FileMode.Open, FileAccess.Read,
                                     FileShare.ReadWrite | FileShare.Delete);
        using var w = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None);
        r.CopyTo(w);
    }
}
