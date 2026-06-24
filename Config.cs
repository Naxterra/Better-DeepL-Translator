using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepLTranslator;

public class Config
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DeepL-Translator", "config.json");

    [JsonPropertyName("dl_access")]      public string? DlAccess      { get; set; }
    [JsonPropertyName("refresh_token")]  public string? RefreshToken  { get; set; }
    [JsonPropertyName("identity_token")] public string? IdentityToken { get; set; }
    [JsonPropertyName("src_lang")]  public string  SrcLang  { get; set; } = "auto";
    [JsonPropertyName("tgt_lang")]  public string  TgtLang  { get; set; } = "de";
    [JsonPropertyName("engine")]    public string  Engine   { get; set; } = "classic";
    [JsonPropertyName("ui_lang")]   public string  UiLang   { get; set; } = "de";
    [JsonPropertyName("geometry")]  public string? Geometry { get; set; }

    public static Config Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
