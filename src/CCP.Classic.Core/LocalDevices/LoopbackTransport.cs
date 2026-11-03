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

// Construction never connects. HTTP redirects/proxies are disabled even for the WebSocket handshake.
public sealed class LoopbackJsonTransport : ILocalDeviceTransport
{
    private const int MaximumBytes = 64 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly SemaphoreSlim wire = new(1, 1);
    private HttpClient? client;
    private ClientWebSocket? socket;
    private Uri? endpoint;
    private bool disposed;

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
        socket = new(); socket.Options.Proxy = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        await socket.ConnectAsync(local, client, timeout.Token).ConfigureAwait(false);
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
            if (socket is not null) return await ExchangeSocket(json, timeout.Token).ConfigureAwait(false);
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

    private async Task<string> ExchangeSocket(string json, CancellationToken token)
    {
        using var request = JsonDocument.Parse(json);
        var id = request.RootElement[0].EnumerateObject().Single().Value.GetProperty("Id").GetInt32();
        await socket!.SendAsync(Utf8.GetBytes(json).AsMemory(), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
        var messages = new List<JsonElement>(); var total = 0;
        for (var frame = 0; frame < 32; frame++)
        {
            var bytes = new byte[MaximumBytes + 1]; var count = 0;
            ValueWebSocketReceiveResult received;
            do
            {
                received = await socket.ReceiveAsync(bytes.AsMemory(count), token).ConfigureAwait(false);
                if (received.MessageType != WebSocketMessageType.Text) throw new IOException("Device connection closed or sent binary data");
                count += received.Count;
                if (count > MaximumBytes || (count == bytes.Length && !received.EndOfMessage)) throw new IOException("Device message is too large");
            } while (!received.EndOfMessage);
            total += count; if (total > MaximumBytes) throw new IOException("Device response is too large");
            using var response = JsonDocument.Parse(Utf8.GetString(bytes, 0, count), new JsonDocumentOptions { MaxDepth = 16 });
            if (response.RootElement.ValueKind != JsonValueKind.Array) throw new IOException("Invalid device response");
            var matched = false;
            foreach (var message in response.RootElement.EnumerateArray())
            {
                if (messages.Count >= 256) throw new IOException("Too many device messages");
                messages.Add(message.Clone());
                foreach (var body in message.EnumerateObject())
                    if (body.Value.TryGetProperty("Id", out var replyId) && replyId.GetInt32() == id) matched = true;
            }
            if (matched) return JsonSerializer.Serialize(messages);
        }
        throw new IOException("Missing device response");
    }

    public ValueTask DisposeAsync()
    {
        if (disposed) return ValueTask.CompletedTask;
        disposed = true; socket?.Abort(); socket?.Dispose(); client?.Dispose();
        return ValueTask.CompletedTask;
    }
}
