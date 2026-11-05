using System.Text.Json;
using CCP.Classic.Core.LocalDevices;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class LocalDeviceConnectionTests
{
    [Fact]
    public async Task IntifaceConnectsOnlyOnRequestNegotiatesSpecFourAndStopsEveryOutput()
    {
        var transport = new FakeTransport(); var created = 0;
        await using var connection = new LocalDeviceConnection(() => { created++; return transport; });
        Assert.Equal(0, created); Assert.Equal(LocalDeviceStatus.Disconnected, connection.Status);
        var token = TestContext.Current.CancellationToken;
        Assert.True(await connection.ConnectAsync(LocalDeviceProvider.Intiface, 12345, token));
        Assert.Equal("ws://127.0.0.1:12345/", transport.Endpoint!.ToString());
        Assert.Single(connection.Devices); Assert.Equal(LocalDeviceStatus.Connected, connection.Status);
        Assert.Contains("ProtocolVersionMajor\":4", transport.Requests[0]);
        await connection.SetLevelAsync(.5, token);
        Assert.Contains(transport.Requests, json => json.Contains("\"OutputCmd\"", StringComparison.Ordinal) && json.Contains("\"Value\":10", StringComparison.Ordinal));
        await connection.StopAsync(token);
        Assert.Contains("\"StopCmd\"", transport.Requests[^1]);
        await connection.DisconnectAsync(); Assert.True(transport.Disposed); Assert.Empty(connection.Devices);
    }

    [Fact]
    public async Task LovenseUsesShortLeasesAndUnconditionalStopInsteadOfRetainedCommands()
    {
        var transport = new FakeTransport { Lovense = true };
        await using var connection = new LocalDeviceConnection(() => transport);
        var token = TestContext.Current.CancellationToken;
        Assert.True(await connection.ConnectAsync(LocalDeviceProvider.LovenseConnect, 20010, token));
        Assert.Equal("http://127.0.0.1:20010/command", transport.Endpoint!.ToString());
        await connection.SetLevelAsync(.5, token);
        using (var body = JsonDocument.Parse(transport.Requests[^1]))
        {
            Assert.Equal("Vibrate:10", body.RootElement.GetProperty("action").GetString());
            Assert.Equal(2, body.RootElement.GetProperty("timeSec").GetInt32());
            Assert.Equal("toy", body.RootElement.GetProperty("toy").GetString());
        }
        await connection.SetLevelAsync(double.NaN, token);
        using var stop = JsonDocument.Parse(transport.Requests[^1]);
        Assert.Equal("Stop", stop.RootElement.GetProperty("action").GetString());
        Assert.Equal(0, stop.RootElement.GetProperty("timeSec").GetInt32());
        Assert.False(stop.RootElement.TryGetProperty("toy", out _));
    }

    [Fact]
    public async Task ConnectCancellationClosesTheTransportAndCannotPublishAConnectedRegistry()
    {
        var transport = new FakeTransport { BlockOpen = true };
        await using var connection = new LocalDeviceConnection(() => transport);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var pending = connection.ConnectAsync(LocalDeviceProvider.Intiface, 12345, stop.Token);
        await transport.Opened.Task.WaitAsync(TestContext.Current.CancellationToken); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.True(transport.Disposed); Assert.Empty(connection.Devices);
        Assert.Equal(LocalDeviceStatus.Disconnected, connection.Status);
    }

    private sealed class FakeTransport : ILocalDeviceTransport
    {
        public readonly List<string> Requests = [];
        public readonly TaskCompletionSource Opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Uri? Endpoint; public bool Disposed, Lovense, BlockOpen;
        public async Task OpenAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            Endpoint = endpoint; Opened.TrySetResult();
            if (BlockOpen) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        public Task<string> ExchangeAsync(string json, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Requests.Add(json);
            using var document = JsonDocument.Parse(json);
            if (Lovense)
            {
                if (document.RootElement.GetProperty("command").GetString() == "GetToys")
                    return Task.FromResult("{\"code\":200,\"data\":{\"toys\":{\"toy\":{\"id\":\"toy\",\"name\":\"lush\",\"status\":1}}}}");
                return Task.FromResult("{\"code\":200}");
            }
            var request = document.RootElement[0].EnumerateObject().Single(); var id = request.Value.GetProperty("Id").GetInt32();
            return Task.FromResult(request.Name switch
            {
                "RequestServerInfo" => $"[{{\"ServerInfo\":{{\"Id\":{id},\"ProtocolVersionMajor\":4,\"MaxPingTime\":0}}}}]",
                "RequestDeviceList" => JsonSerializer.Serialize(new[] { new { DeviceList = new { Id = id,
                    Devices = new Dictionary<string, object> { ["0"] = new { DeviceIndex = 0, DeviceName = "Motor",
                        DeviceFeatures = new Dictionary<string, object> { ["0"] = new { FeatureIndex = 0,
                            Output = new { Vibrate = new { Value = new[] { 0, 20 } } } } } } } } } }),
                _ => $"[{{\"Ok\":{{\"Id\":{id}}}}}]"
            });
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
