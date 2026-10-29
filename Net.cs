using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;

namespace DeepLTranslator;

/// <summary>Thrown when DeepL could not be reached after all retries.</summary>
public sealed class OfflineException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Shared HTTP access that survives network switches. Connections are short-lived, every attempt
/// has its own time limit, and a network change throws away the whole connection pool so that no
/// request keeps waiting on a socket that belongs to the old network.
/// </summary>
public sealed class ResilientHttp : IDisposable
{
    readonly Func<HttpMessageHandler> _handlerFactory;
    readonly TimeSpan _attemptTimeout;
    readonly TimeSpan[] _retryDelays;
    HttpClient _client;

    /// <summary>Raised after the connection pool was replaced because the network changed.</summary>
    public event Action? Reset;

    public ResilientHttp(
        Func<HttpMessageHandler>? handlerFactory = null,
        TimeSpan? attemptTimeout = null,
        TimeSpan[]? retryDelays = null)
    {
        _handlerFactory = handlerFactory ?? CreateDefaultHandler;
        _attemptTimeout = attemptTimeout ?? TimeSpan.FromSeconds(15);
        _retryDelays    = retryDelays ?? [TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(1.5)];
        _client         = CreateClient();
    }

    static HttpMessageHandler CreateDefaultHandler() => new SocketsHttpHandler
    {
        AutomaticDecompression      = DecompressionMethods.All,
        UseCookies                  = false,
        ConnectTimeout              = TimeSpan.FromSeconds(5),
        PooledConnectionLifetime    = TimeSpan.FromMinutes(2),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
        // HTTP/2 pings detect a connection that silently died, for example after a Wi-Fi switch.
        KeepAlivePingDelay   = TimeSpan.FromSeconds(15),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(5),
        KeepAlivePingPolicy  = HttpKeepAlivePingPolicy.WithActiveRequests,
    };

    HttpClient CreateClient()
    {
        var client = new HttpClient(_handlerFactory(), disposeHandler: true)
        {
            // Each attempt has its own limit below, so the client itself never times out.
            Timeout = Timeout.InfiniteTimeSpan,
        };
        client.DefaultRequestHeaders.Referrer = new Uri("https://www.deepl.com/");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DeepLNative/1.0 Windows");
        return client;
    }

    /// <summary>
    /// Replaces the connection pool. Requests still running on the old pool fail at once and are
    /// retried on the new one by <see cref="SendAsync"/>.
    /// </summary>
    public void ResetConnections()
    {
        var old = Interlocked.Exchange(ref _client, CreateClient());
        old.Dispose();
        Reset?.Invoke();
    }

    /// <summary>
    /// Sends a request built by <paramref name="build"/>, which is called again for every attempt
    /// because a request message cannot be sent twice. Network failures, time-outs, 429 and 5xx
    /// answers are retried. Cancellation by the caller is never retried.
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> build, CancellationToken ct = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var client = Volatile.Read(ref _client);
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attemptCts.CancelAfter(_attemptTimeout);

            Exception failure;
            try
            {
                using var req = build();
                var resp = await client.SendAsync(req, HttpCompletionOption.ResponseContentRead, attemptCts.Token);
                if (!IsTransient(resp.StatusCode) || attempt >= _retryDelays.Length)
                    return resp;
                resp.Dispose();
                failure = new OfflineException($"HTTP {(int)resp.StatusCode}");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException
                                           or ObjectDisposedException or IOException)
            {
                // OperationCanceledException here means the attempt ran out of time, and
                // ObjectDisposedException means a network change replaced the pool mid-request.
                failure = ex;
            }

            if (attempt >= _retryDelays.Length)
                throw new OfflineException("DeepL is not reachable.", failure);
            await Task.Delay(_retryDelays[attempt], ct);
        }
    }

    static bool IsTransient(HttpStatusCode code) =>
        code == HttpStatusCode.TooManyRequests || (int)code >= 500;

    public void Dispose() => _client.Dispose();
}

/// <summary>The one shared <see cref="ResilientHttp"/> instance, wired to Windows network events.</summary>
public static class Net
{
    public static ResilientHttp Shared { get; } = new();

    static int _started;

    /// <summary>Starts listening for network changes. Safe to call more than once.</summary>
    public static void WatchNetworkChanges()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        NetworkChange.NetworkAddressChanged      += (_, _) => Shared.ResetConnections();
        NetworkChange.NetworkAvailabilityChanged += (_, _) => Shared.ResetConnections();
    }
}
