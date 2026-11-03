using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CCP.Classic.Core.LocalDevices;

public enum LocalDeviceProvider { Intiface, LovenseConnect }

public static class LocalDeviceEndpoint
{
    public static int DefaultPort(LocalDeviceProvider provider) => provider == LocalDeviceProvider.Intiface ? 12345 : 20010;
    public static Uri Create(LocalDeviceProvider provider, int port, string host = "localhost")
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1); ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        if (!Enum.IsDefined(provider)) throw new ArgumentException("Unknown local provider", nameof(provider));
        var normalized = NormalizeHost(host);
        return new UriBuilder(provider == LocalDeviceProvider.Intiface ? "ws" : "http", normalized, port,
            provider == LocalDeviceProvider.Intiface ? "/" : "/command").Uri;
    }

    public static Uri Validate(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0
            || endpoint.Port is < 1 or > 65535 || endpoint.Scheme is not ("http" or "ws")
            || (endpoint.Scheme == "http" ? endpoint.AbsolutePath != "/command" : endpoint.AbsolutePath != "/"))
            throw new ArgumentException("Only local device endpoints are supported", nameof(endpoint));
        return new UriBuilder(endpoint) { Host = NormalizeHost(endpoint.Host) }.Uri;
    }

    private static string NormalizeHost(string host) => host.ToLowerInvariant() switch
    {
        "localhost" or "127.0.0.1" => "127.0.0.1",
        "::1" or "[::1]" => "::1",
        _ => throw new ArgumentException("A literal loopback address is required", nameof(host))
    };
}

public interface ILocalDeviceTransport : IAsyncDisposable
{
    Task OpenAsync(Uri endpoint, CancellationToken cancellationToken);
    Task<string> ExchangeAsync(string json, CancellationToken cancellationToken);
}
public interface ILocalDeviceEventTransport
{
    // Notification handlers must stay bounded and must not wait for a request on this receive thread.
    event Action<string>? MessageReceived;
}

// Construction never connects. HTTP redirects/proxies are disabled even for the WebSocket handshake.
public sealed class LoopbackJsonTransport(Func<Uri, CancellationToken, Task<WebSocket>>? socketFactory = null) : ILocalDeviceTransport, ILocalDeviceEventTransport
{
    private const int MaximumBytes = 64 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly SemaphoreSlim wire = new(1, 1);
    private HttpClient? client;
    private WebSocket? socket;
    private LocalDeviceSocket? incoming;
    private Uri? endpoint;
    private bool disposed;
    public event Action<string>? MessageReceived;

    public async Task OpenAsync(Uri value, CancellationToken cancellationToken)
    {
        var local = LocalDeviceEndpoint.Validate(value);
        cancellationToken.ThrowIfCancellationRequested();
        if (disposed) throw new ObjectDisposedException(nameof(LoopbackJsonTransport));
        if (endpoint is not null) throw new InvalidOperationException("Transport is already open");
        endpoint = local;
        var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false, UseCookies = false };
        client = new(handler) { Timeout = Timeout.InfiniteTimeSpan };
        if (local.Scheme != "ws") return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        if (socketFactory is not null) socket = await socketFactory(local, timeout.Token).ConfigureAwait(false);
        else
        {
            var native = new ClientWebSocket(); native.Options.Proxy = null; socket = native;
            await native.ConnectAsync(local, client, timeout.Token).ConfigureAwait(false);
        }
        incoming = new(socket, json => MessageReceived?.Invoke(json)); incoming.Start();
    }

    public async Task<string> ExchangeAsync(string json, CancellationToken cancellationToken)
    {
        if (Utf8.GetByteCount(json) > MaximumBytes) throw new IOException("Device message is too large");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        await wire.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            if (disposed || endpoint is null || client is null) throw new InvalidOperationException("Transport is not open");
            if (incoming is not null) return await incoming.ExchangeAsync(json, timeout.Token).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                { Content = new StringContent(json, Utf8, "application/json") };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            var bytes = new byte[MaximumBytes + 1]; var count = 0;
            while (count < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(count), timeout.Token).ConfigureAwait(false);
                if (read == 0) break;
                count += read;
            }
            if (count > MaximumBytes) throw new IOException("Device response is too large");
            return Utf8.GetString(bytes, 0, count);
        }
        finally { wire.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        if (incoming is not null) await incoming.DisposeAsync().ConfigureAwait(false);
        socket?.Abort(); socket?.Dispose(); client?.Dispose();
    }
}
