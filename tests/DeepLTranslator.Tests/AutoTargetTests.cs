namespace DeepLTranslator.Tests;

public class AutoTargetTests
{
    [Fact]
    public void german_text_with_german_target_switches_to_english_us()
    {
        Assert.Equal("en-US", DeepLApi.AutoTarget("DE", "de"));
    }

    [Fact]
    public void english_text_with_british_english_target_switches_to_german()
    {
        Assert.Equal("de", DeepLApi.AutoTarget("EN", "en-GB"));
    }

    [Fact]
    public void turkish_text_with_turkish_target_switches_to_german()
    {
        Assert.Equal("de", DeepLApi.AutoTarget("TR", "tr"));
    }

    [Fact]
    public void text_in_other_language_than_target_keeps_target()
    {
        Assert.Null(DeepLApi.AutoTarget("EN", "de"));
    }

    [Fact]
    public void unknown_detected_language_keeps_target()
    {
        Assert.Null(DeepLApi.AutoTarget(null, "de"));
    }

    [Fact]
    public void every_switch_target_is_in_the_target_list()
    {
        Assert.Contains("en-US", DeepLApi.TgtLangs.Values);
        Assert.Contains("de", DeepLApi.TgtLangs.Values);
    }
}
