using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DeepLTranslator;

public static class AuthManager
{
    const string Authority = "https://auth.deepl.com";
    const string ClientId  = "windowsApp";
    // Scopes from OidcService.cs in decompiled DeepL app
    const string Scope     = "email idp offline_access openid profile service_level organization";
    // DeepL's auth server redirects the browser to this HTTPS URL, which then
    // does a secondary JS redirect to deepl://app/?code=...&state=... triggering our handler.
    const string DeepLRedirect = "https://www.deepl.com/redirect";

    const string ProtocolKey = @"Software\Classes\deepl\shell\open\command";

    static readonly HttpClient Http = new(new HttpClientHandler { UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestVersion = System.Net.HttpVersion.Version11,
    };

    // ── Legacy token renewal (used after initial PKCE login) ─────────────────────

    /// <summary>
    /// Renews an OIDC access token using the legacy_session grant (DeepL's custom
    /// refresh mechanism that also sends the identity token as an extra parameter).
    /// The real DeepL app uses this instead of standard refresh_token when the
    /// token was originally issued by www.deepl.com.
    /// </summary>
    public static async Task<(string Access, string? Refresh)?> LegacyLoginAsync(
        string refreshToken, string identityToken, CancellationToken ct = default)
    {
        // Mirrors LegacyRenewRequester.SendRefreshTokenRequest from decompiled source:
        // grant_type=legacy_session, refresh_token=<token>, id_token=<identity>, scope=...
        var form = new Dictionary<string, string>
        {
            ["grant_type"]    = "legacy_session",
            ["client_id"]     = ClientId,
            ["refresh_token"] = refreshToken,
            ["scope"]         = Scope,
        };
        if (!string.IsNullOrEmpty(identityToken))
            form["id_token"] = identityToken;

        try
        {
            var resp = await Http.PostAsync($"{Authority}/token",
                new FormUrlEncodedContent(form), ct);
            if (!resp.IsSuccessStatusCode) return null;
            var raw = await resp.Content.ReadAsStringAsync(ct);
            return ParseTokenResponse(raw);
        }
        catch { return null; }
    }

    // ── Fallback: full PKCE browser login ────────────────────────────────────────

    /// <summary>
    /// Opens the system browser to auth.deepl.com.  After the user logs in, the auth
    /// server redirects to deepl://app[/]?code=…  We intercept this by temporarily
    /// registering our exe as the deepl:// URL handler, accept the callback over a
    /// named pipe (from the second instance the OS launches), then exchange the code.
    /// </summary>
    public static async Task<(string Access, string? Refresh, string? Identity)?> LoginAsync(
        CancellationToken ct = default)
    {
        var verifier  = MakeVerifier();
        var challenge = MakeChallenge(verifier);
        var state     = MakeVerifier();
        var nonce     = MakeVerifier();

        var ourExe      = Process.GetCurrentProcess().MainModule!.FileName;
        var callbackTcs = new TaskCompletionSource<string?>();

        RegisterSchemeHandler(ourExe, callbackTcs);
        try
        {
            var authUrl = $"{Authority}/authorize" +
                          $"?client_id={Uri.EscapeDataString(ClientId)}" +
                          $"&redirect_uri={Uri.EscapeDataString(DeepLRedirect)}" +
                          $"&response_type=code" +
                          $"&scope={Uri.EscapeDataString(Scope)}" +
                          $"&state={state}" +
                          $"&nonce={nonce}" +
                          $"&code_challenge={challenge}" +
                          $"&code_challenge_method=S256" +
                          $"&response_mode=query" +
                          $"&il=de-DE";

            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

            using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts2.CancelAfter(TimeSpan.FromMinutes(5));
            cts2.Token.Register(() => callbackTcs.TrySetResult(null));

            var callbackUrl = await callbackTcs.Task;
            if (callbackUrl == null) return null;

            var q    = new Uri(callbackUrl).Query;
            var code = ParseQuery(q, "code");
            if (string.IsNullOrEmpty(code)) return null;
            return await ExchangeCodeFull(code, DeepLRedirect, verifier, ct);
        }
        finally
        {
            RestoreSchemeHandler();
        }
    }

    // ── Named pipe callback (from second instance launched by OS) ────────────────

    public static void HandleCallback(string url)
        => _callbackTcs?.TrySetResult(url);

    static TaskCompletionSource<string?>? _callbackTcs;

    // ── Silent refresh via stored refresh token ───────────────────────────────────

    public static async Task<(string Access, string? Refresh)?> RefreshAsync(
        string refreshToken, CancellationToken ct = default)
    {
        var resp = await Http.PostAsync($"{Authority}/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"]    = "refresh_token",
                ["client_id"]     = ClientId,
                ["refresh_token"] = refreshToken,
            }), ct);

        if (!resp.IsSuccessStatusCode) return null;
        var raw = await resp.Content.ReadAsStringAsync(ct);
        return ParseTokenResponse(raw);
    }

    // ── PKCE helpers ─────────────────────────────────────────────────────────────

    static string? _savedHandler;

    static void RegisterSchemeHandler(string exePath, TaskCompletionSource<string?> tcs)
    {
        _callbackTcs = tcs;
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ProtocolKey, writable: true)
                     ?? Microsoft.Win32.Registry.CurrentUser.CreateSubKey(ProtocolKey);
        _savedHandler = key.GetValue(null) as string;
        key.SetValue(null, $"\"{exePath}\" \"%1\"");
    }

    static void RestoreSchemeHandler()
    {
        _callbackTcs = null;
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ProtocolKey, writable: true);
        if (key == null) return;
        if (_savedHandler != null) key.SetValue(null, _savedHandler);
        _savedHandler = null;
    }

    static async Task<(string Access, string? Refresh, string? Identity)?> ExchangeCodeFull(
        string code, string redirectUri, string verifier, CancellationToken ct)
    {
        var resp = await Http.PostAsync($"{Authority}/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"]    = "authorization_code",
                ["client_id"]     = ClientId,
                ["code"]          = code,
                ["redirect_uri"]  = redirectUri,
                ["code_verifier"] = verifier,
            }), ct);

        if (!resp.IsSuccessStatusCode) return null;
        var raw = await resp.Content.ReadAsStringAsync(ct);
        return ParseTokenResponseFull(raw);
    }

    static (string Access, string? Refresh)? ParseTokenResponse(string raw)
    {
        var full = ParseTokenResponseFull(raw);
        if (!full.HasValue) return null;
        return (full.Value.Access, full.Value.Refresh);
    }

    static (string Access, string? Refresh, string? Identity)? ParseTokenResponseFull(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("access_token", out var at)) return null;
            var access = at.GetString()!;
            string? refresh  = null;
            string? identity = null;
            if (doc.RootElement.TryGetProperty("refresh_token", out var rt))  refresh  = rt.GetString();
            if (doc.RootElement.TryGetProperty("id_token",      out var it))  identity = it.GetString();
            return (access, refresh, identity);
        }
        catch { return null; }
    }

    static string MakeVerifier()
    {
        var b = new byte[32];
        RandomNumberGenerator.Fill(b);
        return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    static string MakeChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    static string? ParseQuery(string query, string key)
    {
        foreach (var part in query.TrimStart('?').Split('&'))
        {
            var eq = part.IndexOf('=');
            if (eq > 0 && part[..eq] == key)
                return Uri.UnescapeDataString(part[(eq + 1)..]);
        }
        return null;
    }
}
