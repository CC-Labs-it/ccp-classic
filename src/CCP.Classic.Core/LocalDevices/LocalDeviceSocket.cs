using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CCP.Classic.Core.LocalDevices;

// Exactly one receiver owns this socket. Replies and unsolicited v4 input never compete for ReceiveAsync.
internal sealed class LocalDeviceSocket(WebSocket socket, Action<string> notify) : IAsyncDisposable
{
    private const int MaximumBytes = 65536;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly object gate = new();
    private readonly CancellationTokenSource stop = new();
    private Task receiver = Task.CompletedTask;
    private TaskCompletionSource<string>? pending;
    private int expectedId;
    private Exception? failure;
    private bool disposed;
    public void Start() => receiver = Task.Run(Receive);
    public async Task<string> ExchangeAsync(string json, CancellationToken token)
    {
        using var request = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        if (request.RootElement.ValueKind != JsonValueKind.Array || request.RootElement.GetArrayLength() != 1
            || request.RootElement[0].ValueKind != JsonValueKind.Object || request.RootElement[0].EnumerateObject().Count() != 1
            || !request.RootElement[0].EnumerateObject().Single().Value.TryGetProperty("Id", out var id)
            || !id.TryGetInt32(out var number) || number <= 0) throw new IOException("Invalid device request");
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (failure is not null) throw new IOException("Device connection failed", failure);
            if (pending is not null) throw new InvalidOperationException("Device request already pending");
            expectedId = number; pending = completion;
        }
        try
        {
            using var cancel = token.Register(() => completion.TrySetCanceled(token));
            await socket.SendAsync(Utf8.GetBytes(json).AsMemory(), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
            return await completion.Task.ConfigureAwait(false);
        }
        finally { lock (gate) { if (ReferenceEquals(pending, completion)) pending = null; } }
    }
    private async Task Receive()
    {
        try
        {
            var buffer = new byte[MaximumBytes + 1];
            while (!stop.IsCancellationRequested)
            {
                var count = 0; ValueWebSocketReceiveResult part;
                do
                {
                    part = await socket.ReceiveAsync(buffer.AsMemory(count), stop.Token).ConfigureAwait(false);
                    if (part.MessageType != WebSocketMessageType.Text) throw new IOException("Device connection closed or sent binary data");
                    count += part.Count;
                    if (count > MaximumBytes || count == buffer.Length && !part.EndOfMessage) throw new IOException("Device message is too large");
                } while (!part.EndOfMessage);
                var json = Utf8.GetString(buffer, 0, count);
                using var message = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
                if (message.RootElement.ValueKind != JsonValueKind.Array || message.RootElement.GetArrayLength() > 256) throw new IOException("Invalid device response");
                foreach (var envelope in message.RootElement.EnumerateArray())
                {
                    if (envelope.ValueKind != JsonValueKind.Object || envelope.EnumerateObject().Count() != 1) throw new IOException("Invalid device response");
                    var body = envelope.EnumerateObject().Single().Value;
                    if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("Id", out var id) || !id.TryGetInt32(out var number) || number < 0)
                        throw new IOException("Invalid device response");
                    if (number == 0)
                    { try { notify("[" + envelope.GetRawText() + "]"); } catch (Exception) { } }
                    else lock (gate) { if (number == expectedId) pending?.TrySetResult("[" + envelope.GetRawText() + "]"); }
                }
            }
        }
        catch (Exception error)
        {
            lock (gate) { failure = error; pending?.TrySetException(new IOException("Device connection failed", error)); }
            socket.Abort();
        }
    }
    public async ValueTask DisposeAsync()
    {
        lock (gate)
        { if (disposed) return; disposed = true; pending?.TrySetException(new ObjectDisposedException(nameof(LocalDeviceSocket))); }
        stop.Cancel(); socket.Abort(); await receiver.ConfigureAwait(false); stop.Dispose();
    }
}
