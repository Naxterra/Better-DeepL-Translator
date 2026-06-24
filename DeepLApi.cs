using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepLTranslator;

public static class DeepLApi
{
    const string ProEndpoint = "https://oneshot-pro.www.deepl.com/v1/translate";

    static readonly string InstanceId = Guid.NewGuid().ToString();

    static readonly HttpClient Http = new(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        UseCookies = false,
    })
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    static DeepLApi()
    {
        Http.DefaultRequestHeaders.Referrer = new Uri("https://www.deepl.com/");
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("DeepLNative/1.0 Windows");
    }

    sealed class TranslationRequest
    {
        [JsonPropertyName("text")]           public string[]  Text          { get; init; } = [];
        [JsonPropertyName("target_lang")]    public string    TargetLang    { get; init; } = "";
        [JsonPropertyName("source_lang")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                             public string?   SourceLang    { get; init; }
        [JsonPropertyName("language_model")] public string    LanguageModel { get; init; } = "next-gen";
        [JsonPropertyName("app_information")]public AppInfo   AppInformation{ get; init; } = new();
    }

    sealed class AppInfo
    {
        [JsonPropertyName("os")]          public string Os         { get; init; } = "windows";
        [JsonPropertyName("os_version")]  public string OsVersion  { get; init; } = "";
        [JsonPropertyName("app_version")] public string AppVersion { get; init; } = "1.0.0";
        [JsonPropertyName("app_build")]   public string AppBuild   { get; init; } = "1.0.0";
        [JsonPropertyName("instance_id")] public string InstanceId { get; init; } = "";
    }

    sealed class TranslationResponse
    {
        [JsonPropertyName("translations")] public Translation[] Translations { get; init; } = [];
    }

    sealed class Translation
    {
        [JsonPropertyName("text")]                     public string  Text  { get; init; } = "";
        [JsonPropertyName("detected_source_language")] public string? DetectedSourceLanguage { get; init; }
    }

    public static readonly Dictionary<string, string> SrcLangs = new()
    {
        ["Automatisch"]   = "auto",
        ["Deutsch"]       = "de",
        ["Englisch"]      = "en",
        ["Türkisch"]      = "tr",
    };

    public static readonly Dictionary<string, string> TgtLangs = new()
    {
        ["Deutsch"]        = "de",
        ["Englisch (US)"]  = "en-US",
        ["Englisch (GB)"]  = "en-GB",
        ["Türkisch"]       = "tr",
    };

    public static async Task<(string Text, string? DetectedLang)> TranslateAsync(
        string text,
        string tgtLang,
        string srcLang                      = "auto",
        Dictionary<string, string>? cookies = null,
        bool   nextGen                      = false,
        CancellationToken ct                = default)
    {
        var token = cookies != null && cookies.TryGetValue("dl_access", out var t) ? t : null;

        var body = new TranslationRequest
        {
            Text          = [text],
            TargetLang    = tgtLang,
            SourceLang    = srcLang == "auto" ? null : srcLang,
            LanguageModel = nextGen ? "next-gen" : "latency_optimized",
            AppInformation = new AppInfo
            {
                OsVersion  = Environment.OSVersion.VersionString,
                InstanceId = InstanceId,
            },
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, ProEndpoint)
        {
            Content = JsonContent.Create(body),
        };
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        if (!string.IsNullOrEmpty(token))
            req.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var raw  = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"HTTP {(int)resp.StatusCode}: {raw[..Math.Min(200, raw.Length)]}");

        var result = JsonSerializer.Deserialize<TranslationResponse>(raw);
        var first  = result?.Translations.FirstOrDefault()
                     ?? throw new Exception("Keine Übersetzung erhalten.");

        return (first.Text, first.DetectedSourceLanguage);
    }

    public static bool IsTokenExpired(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return true;
            var pad  = parts[1].PadRight((parts[1].Length + 3) / 4 * 4, '=');
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(pad));
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("exp", out var exp)) return true;
            var expiry = DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64());
            return expiry < DateTimeOffset.UtcNow.AddMinutes(2);
        }
        catch { return true; }
    }
}
