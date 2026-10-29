namespace DeepLTranslator.Tests;

public class LoginCallbackTests
{
    [Fact]
    public void login_callback_with_matching_state_accepted()
    {
        Assert.True(AuthManager.IsCallbackFor("deepl://app/?code=abc&state=s3cret", "s3cret"));
    }

    [Fact]
    public void copied_browser_address_with_matching_state_accepted()
    {
        Assert.True(AuthManager.IsCallbackFor(
            "https://www.deepl.com/de/redirect?code=abc&state=s3cret&iss=https%3A%2F%2Fauth.deepl.com", "s3cret"));
    }

    [Fact]
    public void login_callback_with_wrong_state_ignored()
    {
        Assert.False(AuthManager.IsCallbackFor("deepl://app/?code=abc&state=other", "s3cret"));
    }

    [Fact]
    public void login_callback_without_running_login_ignored()
    {
        Assert.False(AuthManager.IsCallbackFor("deepl://app/?code=abc&state=s3cret", null));
    }

    [Fact]
    public void malformed_login_callback_ignored_without_crash()
    {
        Assert.False(AuthManager.IsCallbackFor("not a link at all", "s3cret"));
    }
}
