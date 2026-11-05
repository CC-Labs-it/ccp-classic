using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using CCP.Classic.Core.LocalDevices;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class LocalDeviceSocketTests
{
    private const string Input = "[{\"InputReading\":{\"Id\":0,\"DeviceIndex\":0,\"FeatureIndex\":4,\"Reading\":{\"Button\":{\"Value\":1}}}}]";
    [Fact]
    public async Task IdleInputAndInterleavedRepliesUseOneReceiverAndTheMatchingRequestOnly()
    {
        var socket = new Socket(); await using var transport = new LoopbackJsonTransport((_, _) => Task.FromResult<WebSocket>(socket));
        var notices = Channel.CreateUnbounded<string>(); transport.MessageReceived += value => notices.Writer.TryWrite(value);
        await transport.OpenAsync(LocalDeviceEndpoint.Create(LocalDeviceProvider.Intiface, 12345), TestContext.Current.CancellationToken);
        socket.Push(Input); Assert.Equal(Input, await Read(notices.Reader));
        var response = transport.ExchangeAsync("[{\"Ping\":{\"Id\":7}}]", TestContext.Current.CancellationToken);
        Assert.Contains("Ping", await Read(socket.Sent.Reader)); socket.Push("[{\"Ok\":{\"Id\":6}}]"); socket.Push(Input);
        await Read(notices.Reader); Assert.False(response.IsCompleted);
        socket.Push("[{\"DeviceList\":{\"Id\":6,\"Devices\":{}}}," + Input[1..^1] + ",{\"Ok\":{\"Id\":7}}]");
        Assert.Equal("[{\"Ok\":{\"Id\":7}}]", await response); Assert.Equal(Input, await Read(notices.Reader));
        Assert.Equal(1, socket.MaximumReceivers);
    }
    [Fact]
    public async Task ACancelledReplyCannotCompleteTheNextRequestAndDisposalReleasesAWaiter()
    {
        var socket = new Socket(); var transport = new LoopbackJsonTransport((_, _) => Task.FromResult<WebSocket>(socket));
        try
        {
            await transport.OpenAsync(LocalDeviceEndpoint.Create(LocalDeviceProvider.Intiface, 12345), TestContext.Current.CancellationToken);
            using var cancel = new CancellationTokenSource(); var first = transport.ExchangeAsync("[{\"Ping\":{\"Id\":1}}]", cancel.Token);
            await Read(socket.Sent.Reader); cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            var next = transport.ExchangeAsync("[{\"Ping\":{\"Id\":2}}]", TestContext.Current.CancellationToken);
            await Read(socket.Sent.Reader); socket.Push("[{\"Ok\":{\"Id\":1}}]"); socket.Push("[{\"Ok\":{\"Id\":2}}]");
            Assert.Contains("\"Id\":2", await next);
            var last = transport.ExchangeAsync("[{\"Ping\":{\"Id\":3}}]", TestContext.Current.CancellationToken);
            await Read(socket.Sent.Reader); await transport.DisposeAsync(); await Assert.ThrowsAsync<ObjectDisposedException>(() => last);
            Assert.Equal(WebSocketState.Aborted, socket.State);
        }
        finally { await transport.DisposeAsync(); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidOrOversizedFramesFailThePendingRequestAndAbortTheConnection(bool oversized)
    {
        var socket = new Socket(); await using var transport = new LoopbackJsonTransport((_, _) => Task.FromResult<WebSocket>(socket));
        await transport.OpenAsync(LocalDeviceEndpoint.Create(LocalDeviceProvider.Intiface, 12345), TestContext.Current.CancellationToken);
        var response = transport.ExchangeAsync("[{\"Ping\":{\"Id\":1}}]", TestContext.Current.CancellationToken);
        await Read(socket.Sent.Reader); socket.Push(oversized ? new string('x', 70000) : "[broken");
        await Assert.ThrowsAsync<IOException>(() => response); Assert.Equal(WebSocketState.Aborted, socket.State);
    }
    private static async Task<T> Read<T>(ChannelReader<T> channel) => await channel.ReadAsync(TestContext.Current.CancellationToken).AsTask()
        .WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

    private sealed class Socket : WebSocket
    {
        private readonly Channel<byte[]> incoming = Channel.CreateUnbounded<byte[]>();
        public readonly Channel<string> Sent = Channel.CreateUnbounded<string>();
        private readonly CancellationTokenSource aborted = new();
        private byte[]? buffered; private int offset, receivers;
        public int MaximumReceivers;
        private WebSocketState state = WebSocketState.Open;
        public override WebSocketState State => state;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override string? SubProtocol => null;
        public void Push(string value) => incoming.Writer.TryWrite(Encoding.UTF8.GetBytes(value));
        public override void Abort() { state = WebSocketState.Aborted; aborted.Cancel(); }
        public override void Dispose() => Abort();
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken token) { Abort(); return Task.CompletedTask; }
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken token) { Abort(); return Task.CompletedTask; }
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Sent.Writer.TryWrite(Encoding.UTF8.GetString(buffer)); return Task.CompletedTask; }
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken token)
        {
            var simultaneous = Interlocked.Increment(ref receivers); MaximumReceivers = Math.Max(MaximumReceivers, simultaneous);
            try
            {
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(token, aborted.Token);
                buffered ??= await incoming.Reader.ReadAsync(stop.Token); var count = Math.Min(buffer.Count, buffered.Length - offset);
                buffered.AsSpan(offset, count).CopyTo(buffer); offset += count; var end = offset == buffered.Length;
                if (end) { buffered = null; offset = 0; } return new(count, WebSocketMessageType.Text, end);
            }
            finally { Interlocked.Decrement(ref receivers); }
        }
    }
}
