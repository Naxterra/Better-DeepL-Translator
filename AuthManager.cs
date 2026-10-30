using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NaxDeepL;

public static class AuthManager
{
    const string Authority = "https://auth.deepl.com";
    const string ClientId  = "windowsApp";
    // Scopes from OidcService.cs in decompiled DeepL app
    const string Scope     = "email idp offline_access openid profile service_level organization";
    // DeepL's auth server redirects the browser to this HTTPS URL, which then
    // does a secondary JS redirect to deepl://app/?code=...&state=... triggering our handler.
    const string DeepLRedirect = "https://www.deepl.com/redirect";

    // ── PKCE browser login ───────────────────────────────────────────────────────

    /// <summary>
    /// Opens the system browser to auth.deepl.com.  After the user logs in, the auth
    /// server redirects to deepl://app[/]?code=…  We intercept this by temporarily
    /// registering our exe as the deepl:// URL handler, accept the callback over a
    /// named pipe (from the second instance the OS launches), then exchange the code.
    /// Returns null when the login was cancelled, timed out or was rejected.
    /// </summary>
    public static async Task<(string Access, string? Refresh, string? Identity)?> LoginAsync(
        CancellationToken ct = default)
    {
        var verifier  = MakeVerifier();
        var challenge = MakeChallenge(verifier);
        var state     = MakeVerifier();
        var nonce     = MakeVerifier();

        var ourExe      = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;
        var callbackTcs = new TaskCompletionSource<string?>();

        _callbackTcs   = callbackTcs;
        _expectedState = state;
        Scheme.Take(ourExe);
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
            _callbackTcs   = null;
            _expectedState = null;
            Scheme.RestoreLeftover();
        }
    }

    // ── Named pipe callback (from second instance launched by OS) ────────────────

    /// <summary>
    /// Passes a deepl:// link, or the address of DeepL's redirect page, to the waiting login.
    /// Links whose state does not match the login that is running are ignored, because any
    /// local program can send one through the pipe or put one on the clipboard.
    /// Returns true when the link was accepted.
    /// </summary>
    public static bool HandleCallback(string url) =>
        IsCallbackFor(url, _expectedState) && (_callbackTcs?.TrySetResult(url) ?? false);

    internal static bool IsCallbackFor(string url, string? expectedState)
    {
        if (string.IsNullOrEmpty(expectedState)) return false;
        try
        {
            return ParseQuery(new Uri(url).Query, "state") == expectedState;
        }
        catch (UriFormatException) { return false; }
    }

    static TaskCompletionSource<string?>? _callbackTcs;
    static string? _expectedState;

    // ── Silent refresh via stored refresh token ───────────────────────────────────

    /// <summary>
    /// Returns new tokens, or null when DeepL rejected the refresh token. Throws
    /// <see cref="OfflineException"/> when DeepL could not be reached, because the stored
    /// login is still good in that case and must not be thrown away.
    /// </summary>
    public static async Task<(string Access, string? Refresh)?> RefreshAsync(
        string refreshToken, CancellationToken ct = default, ResilientHttp? http = null)
    {
        using var resp = await (http ?? Net.Shared).SendAsync(() =>
            new HttpRequestMessage(HttpMethod.Post, $"{Authority}/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"]    = "refresh_token",
                    ["client_id"]     = ClientId,
                    ["refresh_token"] = refreshToken,
                }),
            }, ct);

        if ((int)resp.StatusCode >= 500 || resp.StatusCode == HttpStatusCode.TooManyRequests)
            throw new OfflineException($"HTTP {(int)resp.StatusCode}");
        if (!resp.IsSuccessStatusCode) return null;
        var raw = await resp.Content.ReadAsStringAsync(ct);
        return ParseTokenResponse(raw);
    }

    static readonly SchemeHandlerGuard Scheme = SchemeHandlerGuard.ForCurrentUser();

    /// <summary>Undoes the deepl:// handler change of a login that was cut short by a crash.</summary>
    public static void RestoreLeftoverSchemeHandler()
    {
        try { SchemeHandlerGuard.LegacyForCurrentUser().RestoreLeftover(); } catch { }
        try { Scheme.RestoreLeftover(); } catch { }
    }

    static async Task<(string Access, string? Refresh, string? Identity)?> ExchangeCodeFull(
        string code, string redirectUri, string verifier, CancellationToken ct)
    {
        using var resp = await Net.Shared.SendAsync(() =>
            new HttpRequestMessage(HttpMethod.Post, $"{Authority}/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"]    = "authorization_code",
                    ["client_id"]     = ClientId,
                    ["code"]          = code,
                    ["redirect_uri"]  = redirectUri,
                    ["code_verifier"] = verifier,
                }),
            }, ct);

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
