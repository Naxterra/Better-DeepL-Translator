namespace NaxDeepL.Tests;

public class SwapTests
{
    [Fact]
    public void swap_with_automatic_source_uses_detected_german()
    {
        Assert.Equal(("en", "de"), DeepLApi.Swap("auto", "en-US", "DE"));
    }

    [Fact]
    public void swap_with_automatic_source_and_nothing_detected_still_swaps()
    {
        Assert.Equal(("en", "de"), DeepLApi.Swap("auto", "en-GB", null));
        Assert.Equal(("de", "en-US"), DeepLApi.Swap("auto", "de", null));
    }

    [Fact]
    public void swap_with_fixed_source_exchanges_both_languages()
    {
        Assert.Equal(("de", "tr"), DeepLApi.Swap("tr", "de", null));
        Assert.Equal(("de", "en-US"), DeepLApi.Swap("en", "de", null));
    }

    [Fact]
    public void swap_with_detected_language_missing_from_target_list_falls_back()
    {
        Assert.Equal(("de", "en-US"), DeepLApi.Swap("auto", "de", "FR"));
    }

    [Fact]
    public void every_swap_result_is_in_both_language_lists()
    {
        foreach (var src in DeepLApi.SrcLangs.Values)
        foreach (var tgt in DeepLApi.TgtLangs.Values)
        foreach (var detected in new[] { null, "DE", "EN", "TR", "FR" })
        {
            var (newSrc, newTgt) = DeepLApi.Swap(src, tgt, detected);
            Assert.Contains(newSrc, DeepLApi.SrcLangs.Values);
            Assert.Contains(newTgt, DeepLApi.TgtLangs.Values);
        }
    }
}
