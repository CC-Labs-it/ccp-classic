using System.Collections.Concurrent;
using System.Text.Json;
using CCP.Classic.Core.LocalDevices;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class IntifaceButtonTests
{
    [Fact]
    public async Task OnlyAdvertisedSubscriptionsCountEdgesWithPerDeviceDebounceAndOptOut()
    {
        var wire = new Wire(); var clock = new Clock();
        await using var controller = new LocalDeviceController(() => new(() => wire, clock), clock);
        var presses = new ConcurrentQueue<LocalButtonPress>(); controller.ButtonPressed += presses.Enqueue;
        Assert.True(await controller.ConnectAsync(LocalDeviceProvider.Intiface, 12345, TestContext.Current.CancellationToken));
        await Until(() => wire.Commands.Count(command => command.Command == "Subscribe") == 2);
        Assert.Contains(controller.Devices, device => device.Id == "1" && device.Actuators.Count == 0 && device.ButtonFeatures.SequenceEqual([2]));
        Assert.DoesNotContain(wire.Commands, command => command.Feature == 5);
        wire.Button(0, 5, 1); wire.Button(0, 4, 1); wire.Button(0, 4, 1); Assert.Single(presses);
        clock.Milliseconds = 1000; wire.Button(0, 4, 1); Assert.Single(presses);
        wire.Button(0, 4, 0); wire.Button(0, 4, 1); wire.Button(1, 2, 1); Assert.Equal(3, presses.Count);
        wire.Button(1, 2, 0); clock.Milliseconds = 1100; wire.Button(1, 2, 1); Assert.Equal(3, presses.Count);
        var previous = presses.Last(); Assert.True(controller.IsCurrentInput(previous));
        controller.InputEnabled = false; Assert.False(controller.IsCurrentInput(previous));
        await Until(() => wire.Commands.Count(command => command.Command == "Unsubscribe") == 2);
        wire.Button(0, 4, 0); wire.Button(0, 4, 1); Assert.Equal(3, presses.Count);
        controller.InputEnabled = true; await Until(() => wire.Commands.Count(command => command.Command == "Subscribe") == 4);
        clock.Milliseconds = 2000; wire.Button(0, 4, 0); wire.Button(0, 4, 1); Assert.Equal(4, presses.Count);
    }

    [Fact]
    public async Task RemovalIndexReuseAndStopInvalidateQueuedPressesAndDisconnectDropsLateMessages()
    {
        var wire = new Wire(); var clock = new Clock();
        await using var controller = new LocalDeviceController(() => new(() => wire, clock), clock);
        var presses = new ConcurrentQueue<LocalButtonPress>(); controller.ButtonPressed += presses.Enqueue;
        await controller.ConnectAsync(LocalDeviceProvider.Intiface, 12345, TestContext.Current.CancellationToken);
        await Until(() => wire.Commands.Count(command => command.Device == 0 && command.Command == "Subscribe") == 1);
        wire.Button(0, 4, 1); var old = Assert.Single(presses); Assert.True(controller.IsCurrentInput(old));
        wire.Present = false; wire.DeviceList(); Assert.False(controller.IsCurrentInput(old)); wire.Button(0, 4, 1); Assert.Single(presses);
        wire.Present = true; wire.DeviceList();
        await Until(() => wire.Commands.Count(command => command.Device == 0 && command.Command == "Subscribe") == 2);
        wire.Button(0, 4, 1); Assert.Equal(2, presses.Count); Assert.False(controller.IsCurrentInput(old));
        var recent = presses.Last(); Assert.True(controller.IsCurrentInput(recent));
        var stopped = controller.StopAsync(); Assert.False(controller.IsCurrentInput(recent)); await stopped;
        await controller.DisconnectAsync(); wire.Button(0, 4, 0); wire.Button(0, 4, 1); Assert.Equal(2, presses.Count);
        Assert.All(wire.Stops, body => { Assert.False(body.GetProperty("Inputs").GetBoolean()); Assert.True(body.GetProperty("Outputs").GetBoolean()); });
    }

    [Fact]
    public async Task StopCancelsAStalledSubscriptionAndNoInputIsSentBeforeAV4Handshake()
    {
        var wire = new Wire { BlockSubscribe = true };
        await using (var controller = new LocalDeviceController(() => new(() => wire)))
        {
            await controller.ConnectAsync(LocalDeviceProvider.Intiface, 12345, TestContext.Current.CancellationToken);
            await wire.SubscribeEntered.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            var stopped = controller.StopAsync();
            await wire.SubscribeCanceled.Task.WaitAsync(TimeSpan.FromMilliseconds(750), TestContext.Current.CancellationToken);
            await stopped;
        }
        var old = new Wire { Version = 3 }; await using var legacy = new LocalDeviceController(() => new(() => old));
        Assert.False(await legacy.ConnectAsync(LocalDeviceProvider.Intiface, 12345, TestContext.Current.CancellationToken)); Assert.Empty(old.Commands);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AWaitingRawEdgeKeepsItsTimestampAndCannotSurviveInputOptOut(bool toggle)
    {
        var wire = new Wire(); var clock = new Clock(); var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim(); var connection = new LocalDeviceConnection(() => wire, clock);
        connection.ButtonPressed += _ => { captured.TrySetResult(); release.Wait(TimeSpan.FromSeconds(3)); };
        await using var controller = new LocalDeviceController(() => connection, clock);
        var presses = new ConcurrentQueue<LocalButtonPress>(); controller.ButtonPressed += presses.Enqueue;
        await controller.ConnectAsync(LocalDeviceProvider.Intiface, 12345, TestContext.Current.CancellationToken);
        await Until(() => wire.Commands.Count(command => command.Device == 0 && command.Command == "Subscribe") == 1);
        var delivering = Task.Run(() => wire.Button(0, 4, 1), TestContext.Current.CancellationToken);
        try
        {
            await captured.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken); clock.Milliseconds = 500;
            if (toggle) { controller.InputEnabled = false; controller.InputEnabled = true; }
            release.Set(); await delivering;
            if (toggle) Assert.Empty(presses); else Assert.Equal(0, Assert.Single(presses).ObservedAt);
        }
        finally { release.Set(); await delivering; }
    }
    private static async Task Until(Func<bool> predicate)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); stop.CancelAfter(TimeSpan.FromSeconds(3));
        while (!predicate()) await Task.Delay(10, stop.Token);
    }
    private sealed class Clock : TimeProvider
    {
        public long Milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
    }
    private sealed class Wire : ILocalDeviceTransport, ILocalDeviceEventTransport
    {
        public readonly ConcurrentQueue<(int Device, int Feature, string Command)> Commands = new();
        public readonly ConcurrentQueue<JsonElement> Stops = new();
        public readonly TaskCompletionSource SubscribeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource SubscribeCanceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Present = true, BlockSubscribe;
        public int Version = 4;
        public event Action<string>? MessageReceived;
        public Task OpenAsync(Uri endpoint, CancellationToken token) => Task.CompletedTask;
        public void Button(int device, int feature, int value) => MessageReceived?.Invoke(JsonSerializer.Serialize(new[] { new { InputReading = new
            { Id = 0, DeviceIndex = device, FeatureIndex = feature, Reading = new { Button = new { Value = value } } } } }));
        private string Devices(int id) => JsonSerializer.Serialize(new[] { new { DeviceList = new { Id = id, Devices = DeviceData() } } });
        public void DeviceList() => MessageReceived?.Invoke(Devices(0));
        private Dictionary<string, object> DeviceData()
        {
            var result = new Dictionary<string, object>
            {
                ["1"] = new { DeviceIndex = 1, DeviceName = "Button", DeviceFeatures = new Dictionary<string, object>
                    { ["2"] = new { FeatureIndex = 2, Input = new { Button = new { Value = new[] { 0, 1 }, Command = new[] { "Subscribe", "Unsubscribe" } } } } } }
            };
            if (Present) result["0"] = new { DeviceIndex = 0, DeviceName = "Toy", DeviceFeatures = new Dictionary<string, object>
            {
                ["0"] = new { FeatureIndex = 0, Output = new { Vibrate = new { Value = new[] { 0, 20 } } } },
                ["4"] = new { FeatureIndex = 4, Input = new { Button = new { Value = new[] { 0, 1 }, Command = new[] { "Subscribe", "Unsubscribe" } } } },
                ["5"] = new { FeatureIndex = 5, Input = new { Button = new { Value = new[] { 0, 1 }, Command = new[] { "Read" } } } }
            } };
            return result;
        }
        public async Task<string> ExchangeAsync(string request, CancellationToken token)
        {
            using var json = JsonDocument.Parse(request); var command = json.RootElement[0].EnumerateObject().Single(); var body = command.Value;
            var id = body.GetProperty("Id").GetInt32();
            if (command.Name == "RequestServerInfo") return JsonSerializer.Serialize(new[] { new { ServerInfo = new { Id = id, ProtocolVersionMajor = Version } } });
            if (command.Name == "RequestDeviceList") return Devices(id);
            if (command.Name == "StopCmd") Stops.Enqueue(body.Clone());
            if (command.Name == "InputCmd")
            {
                Assert.Equal("Button", body.GetProperty("Type").GetString()); var action = body.GetProperty("Command").GetString()!;
                Commands.Enqueue((body.GetProperty("DeviceIndex").GetInt32(), body.GetProperty("FeatureIndex").GetInt32(), action));
                if (BlockSubscribe && action == "Subscribe")
                {
                    SubscribeEntered.TrySetResult();
                    try { await Task.Delay(Timeout.Infinite, token); } catch (OperationCanceledException) { SubscribeCanceled.TrySetResult(); throw; }
                }
            }
            return JsonSerializer.Serialize(new[] { new { Ok = new { Id = id } } });
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
