using System.Diagnostics;
using System.Net;
using System.Net.Http;

namespace NaxDeepL.Tests;

public class NetworkTests
{
    static readonly TimeSpan[] FastRetries = [TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10)];

    static HttpRequestMessage Get() => new(HttpMethod.Get, "https://example.invalid/");

    static ResilientHttp Http(Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
                              TimeSpan? attemptTimeout = null)
    {
        var calls = 0;
        return new ResilientHttp(
            () => new FakeHandler((req, ct) => send(Interlocked.Increment(ref calls), req, ct)),
            attemptTimeout ?? TimeSpan.FromSeconds(5),
            FastRetries);
    }

    [Fact]
    public async Task request_fails_once_with_network_error_then_succeeds_on_retry()
    {
        using var http = Http((n, _, _) => n == 1
            ? throw new HttpRequestException("socket closed")
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        using var resp = await http.SendAsync(Get);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task request_hangs_past_attempt_limit_then_retried_and_succeeds()
    {
        using var http = Http(async (n, _, ct) =>
        {
            if (n == 1) await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, attemptTimeout: TimeSpan.FromMilliseconds(200));

        using var resp = await http.SendAsync(Get);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task network_change_mid_request_moves_request_to_fresh_connection()
    {
        var firstStarted = new TaskCompletionSource();
        var handlersMade = 0;
        using var http = new ResilientHttp(() =>
        {
            var handlerNo = Interlocked.Increment(ref handlersMade);
            return new FakeHandler(async (_, ct) =>
            {
                if (handlerNo == 1)
                {
                    // The old network: the request hangs, as it did in the official app.
                    firstStarted.TrySetResult();
                    await Task.Delay(Timeout.Infinite, ct);
                }
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
        }, attemptTimeout: TimeSpan.FromSeconds(30), retryDelays: FastRetries);

        var sw   = Stopwatch.StartNew();
        var send = http.SendAsync(Get);
        await firstStarted.Task;
        http.ResetConnections();
        using var resp = await send;

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(2, handlersMade);
        // It must not wait for the 30 second attempt limit of the dead connection.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed}");
    }

    [Fact]
    public async Task caller_cancels_request_not_retried()
    {
        var calls = 0;
        using var http = Http(async (_, _, ct) =>
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => http.SendAsync(Get, cts.Token));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task server_error_retried_then_success_returned()
    {
        using var http = Http((n, _, _) => Task.FromResult(
            new HttpResponseMessage(n == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)));

        using var resp = await http.SendAsync(Get);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task client_error_returned_at_once_without_retry()
    {
        var calls = 0;
        using var http = Http((n, _, _) =>
        {
            calls = n;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
        });

        using var resp = await http.SendAsync(Get);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task every_attempt_fails_reports_offline_instead_of_hanging()
    {
        using var http = Http((_, _, _) => throw new HttpRequestException("no route to host"));

        await Assert.ThrowsAsync<OfflineException>(() => http.SendAsync(Get));
    }

    [Fact]
    public async Task translate_with_rejected_token_asks_for_new_login_token()
    {
        using var http = Http((_, _, _) =>
            Task.FromResult(FakeHandler.Json(HttpStatusCode.Unauthorized, "{}")));

        await Assert.ThrowsAsync<AuthExpiredException>(() =>
            DeepLApi.TranslateAsync("Hallo Welt", "en-US", accessToken: "old", http: http));
    }

    [Fact]
    public async Task translate_sends_bearer_token_and_returns_translated_text()
    {
        string? auth = null;
        using var http = Http((_, req, _) =>
        {
            auth = req.Headers.Authorization?.ToString();
            return Task.FromResult(FakeHandler.Json(HttpStatusCode.OK,
                """{"translations":[{"text":"Hello world","detected_source_language":"DE"}]}"""));
        });

        var (text, detected) = await DeepLApi.TranslateAsync("Hallo Welt", "en-US", accessToken: "tok", http: http);

        Assert.Equal("Bearer tok", auth);
        Assert.Equal("Hello world", text);
        Assert.Equal("DE", detected);
    }

    [Fact]
    public async Task token_refresh_rejected_by_deepl_returns_no_token()
    {
        using var http = Http((_, _, _) =>
            Task.FromResult(FakeHandler.Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""")));

        var result = await AuthManager.RefreshAsync("expired-refresh-token", http: http);

        Assert.Null(result);
    }

    [Fact]
    public async Task token_refresh_while_offline_reports_offline_so_login_is_kept()
    {
        using var http = Http((_, _, _) => throw new HttpRequestException("network unreachable"));

        await Assert.ThrowsAsync<OfflineException>(() => AuthManager.RefreshAsync("good-refresh-token", http: http));
    }
}
