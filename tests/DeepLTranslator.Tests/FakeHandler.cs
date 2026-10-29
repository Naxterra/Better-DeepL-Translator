using System.Net;
using System.Net.Http;

namespace DeepLTranslator.Tests;

/// <summary>An HTTP handler whose behaviour each test supplies as a function.</summary>
sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => send(request, ct);

    public static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}
