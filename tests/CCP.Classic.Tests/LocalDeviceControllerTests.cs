using System.Text.Json;
using CCP.Classic.Core.LocalDevices;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class LocalDeviceControllerTests
{
    [Fact]
    public async Task ConstructionIsIdleAndPulseBurstsStayBoundedAndExpireToZero()
    {
        var wire = new FakeTransport(); var created = 0; var clock = new ManualClock();
        await using var controller = new LocalDeviceController(() => { created++; return new(() => wire); }, clock);
        controller.Emit(LocalDeviceEvent.Manual, 1, TimeSpan.FromSeconds(1));
        controller.SetAudioLevel(1); controller.SetAudioLevel(0);
        Assert.Equal(0, created); Assert.Equal(0, controller.ActivePulseCount);
        Assert.True(await controller.ConnectAsync(LocalDeviceProvider.Intiface, 12345, TestContext.Current.CancellationToken));
        for (var index = 0; index < 100; index++) controller.Emit(LocalDeviceEvent.BubblePop, 1, TimeSpan.FromMilliseconds(20));
        Assert.InRange(controller.ActivePulseCount, 1, 4);
        clock.Advance(100); controller.SetAudioLevel(0);
        await wire.Nonzero.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        clock.Advance(1000); controller.SetAudioLevel(0);
        await wire.ZeroAfterNonzero.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.All(wire.Levels, value => Assert.InRange(value, 0, 14));
        Assert.Equal(0, controller.ActivePulseCount);
    }

    [Fact]
    public async Task PanicWaitsForTheOldSendThenStopsAndRejectsNewEmitsDuringTheFlush()
    {
        var wire = new FakeTransport { BlockOutput = true };
        await using var controller = new LocalDeviceController(() => new(() => wire));
        await controller.ConnectAsync(LocalDeviceProvider.Intiface, 12345, TestContext.Current.CancellationToken);
        controller.Emit(LocalDeviceEvent.Manual, 1, TimeSpan.FromSeconds(10));
        await wire.OutputEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var stopped = controller.StopAsync();
        controller.Emit(LocalDeviceEvent.Manual, 1, TimeSpan.FromSeconds(10));
        Assert.False(stopped.IsCompleted); Assert.Equal(0, controller.ActivePulseCount);
        wire.Release.TrySetResult(); await stopped;
        Assert.Equal(0, wire.Levels[^1]); Assert.Equal(0, controller.ActivePulseCount);
    }

    [Fact]
    public async Task BothExplicitProvidersParticipateAndDisposeFlushesEveryDevice()
    {
        var wires = new List<FakeTransport>();
        var controller = new LocalDeviceController(() => new(() => { var value = new FakeTransport(); wires.Add(value); return value; }));
        try
        {
            await controller.ConnectAsync(LocalDeviceProvider.Intiface, 12345, TestContext.Current.CancellationToken);
            await controller.ConnectAsync(LocalDeviceProvider.LovenseConnect, 20010, TestContext.Current.CancellationToken);
            Assert.Equal(2, controller.Devices.Count);
            controller.Emit(LocalDeviceEvent.Manual, .8, TimeSpan.FromSeconds(5));
            await Task.WhenAll(wires.Select(w => w.Nonzero.Task)).WaitAsync(TestContext.Current.CancellationToken);
            await controller.DisposeAsync();
            Assert.All(wires, wire => { Assert.True(wire.Disposed); Assert.Equal(0, wire.Levels[^1]); });
            Assert.Equal(LocalDeviceStatus.Disconnected, controller.Status); Assert.Empty(controller.Devices);
            var totals = wires.Sum(w => w.Levels.Count);
            controller.Emit(LocalDeviceEvent.Manual, 1, TimeSpan.FromSeconds(1));
            Assert.Equal(totals, wires.Sum(w => w.Levels.Count)); Assert.True(controller.Completion.IsCompletedSuccessfully);
        }
        finally { await controller.DisposeAsync(); }
    }

    [Fact]
    public async Task DisablingTheGateClearsEveryEnvelopeAndFlushesZero()
    {
        var wire = new FakeTransport();
        await using var controller = new LocalDeviceController(() => new(() => wire));
        await controller.ConnectAsync(LocalDeviceProvider.Intiface, 12345, TestContext.Current.CancellationToken);
        controller.Emit(LocalDeviceEvent.LevelUp, 1, TimeSpan.FromSeconds(10));
        await wire.Nonzero.Task.WaitAsync(TestContext.Current.CancellationToken);
        controller.Enabled = false;
        await wire.ZeroAfterNonzero.Task.WaitAsync(TestContext.Current.CancellationToken);
        controller.Emit(LocalDeviceEvent.Manual, 1, TimeSpan.FromSeconds(10));
        Assert.Equal(0, controller.ActivePulseCount); Assert.Equal(0, wire.Levels[^1]);
    }

    [Fact]
    public async Task AudioUpdatesReplaceTheLevelWithoutPulseAllocationOrOverlapAmplification()
    {
        var wire = new FakeTransport();
        await using var controller = new LocalDeviceController(() => new(() => wire));
        controller.ConfigureRouting(new() { SoftRampMilliseconds = 1 });
        await controller.ConnectAsync(LocalDeviceProvider.Intiface, 12345, TestContext.Current.CancellationToken);
        controller.SetAudioLevel(.5); await wire.WaitForLevel(7);
        for (var index = 0; index < 100; index++) controller.Emit(LocalDeviceEvent.AudioSync, .5, TimeSpan.FromSeconds(10));
        Assert.Equal(0, controller.ActivePulseCount); Assert.All(wire.Levels, level => Assert.InRange(level, 0, 7));
        controller.SetAudioLevel(1); await wire.WaitForLevel(14);
        controller.SetAudioLevel(.2); await wire.WaitForLevel(3);
        controller.SetAudioLevel(double.NaN); await wire.WaitForLevel(0);
        controller.SetAudioLevel(double.PositiveInfinity); Assert.Equal(0, controller.ActivePulseCount);
        controller.SetAudioLevel(2); await wire.WaitForLevel(14);
        controller.SetAudioLevel(0); await wire.WaitForLevel(0);
    }

    [Fact]
    public async Task AudioIsFreshAt299MillisecondsAndSilentAt300WithoutAnotherUpdate()
    {
        var wire = new FakeTransport(); var clock = new ManualClock();
        await using var controller = new LocalDeviceController(() => new(() => wire), clock);
        controller.ConfigureRouting(new() { SoftRampMilliseconds = 1 });
        await controller.ConnectAsync(LocalDeviceProvider.Intiface, 12345, TestContext.Current.CancellationToken);
        clock.Advance(100); SetFreshAudio(controller, clock, .5); await wire.WaitForLevel(7);
        clock.Advance(299); controller.GlobalIntensity = .6; await wire.WaitForLevel(6);
        clock.Advance(1); controller.GlobalIntensity = .7; await wire.WaitForLevel(0);
        Assert.Equal(0, controller.ActivePulseCount);
    }

    [Fact]
    public async Task AudioAndPriorityEventsUseTheLargerLaneAndOneGlobalCap()
    {
        var wire = new FakeTransport(); var clock = new ManualClock();
        await using var controller = new LocalDeviceController(() => new(() => wire), clock);
        controller.ConfigureRouting(new() { SoftRampMilliseconds = 1 });
        await controller.ConnectAsync(LocalDeviceProvider.Intiface, 12345, TestContext.Current.CancellationToken);
        clock.Advance(100); SetFreshAudio(controller, clock, .5); await wire.WaitForLevel(7);
        controller.Emit(LocalDeviceEvent.LevelUp, .8, TimeSpan.FromSeconds(10)); clock.Advance(100);
        controller.SetAudioLevel(.5); await wire.WaitForLevel(11);
        controller.Emit(LocalDeviceEvent.LevelUp, 1, TimeSpan.FromSeconds(10)); clock.Advance(100);
        controller.SetAudioLevel(1); await wire.WaitForLevel(14);
        Assert.Equal(2, controller.ActivePulseCount); Assert.All(wire.Levels, level => Assert.InRange(level, 0, 14));
    }

    [Fact]
    public async Task StopClearsAudioBeforeAnOlderSendFinishesAndCannotResurrectIt()
    {
        var wire = new FakeTransport { BlockOutput = true }; var clock = new ManualClock();
        await using var controller = new LocalDeviceController(() => new(() => wire), clock);
        await controller.ConnectAsync(LocalDeviceProvider.Intiface, 12345, TestContext.Current.CancellationToken);
        clock.Advance(100); SetFreshAudio(controller, clock, 1); await wire.OutputEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var stopping = controller.StopAsync(); controller.SetAudioLevel(1);
        Assert.False(stopping.IsCompleted); Assert.Equal(0, controller.ActivePulseCount);
        wire.Release.TrySetResult(); await stopping;
        var afterStop = wire.Levels.Count; controller.GlobalIntensity = .6;
        await Task.Delay(150, TestContext.Current.CancellationToken);
        Assert.Equal(0, wire.Levels[^1]); Assert.All(wire.Levels.Skip(afterStop), level => Assert.Equal(0, level));
        // A held sample remains fresh on this fixed clock, so only explicit clearing can keep it silent.
        controller.SetAudioLevel(1); await Task.Delay(150, TestContext.Current.CancellationToken);
        Assert.Equal(0, wire.Levels[^1]);
    }

    [Fact]
    public async Task DisableAndFullDisconnectClearAudioBeforeReconnection()
    {
        var wires = new List<FakeTransport>(); var clock = new ManualClock();
        await using var controller = new LocalDeviceController(() => new(() => { var wire = new FakeTransport(); wires.Add(wire); return wire; }), clock);
        controller.ConfigureRouting(new() { SoftRampMilliseconds = 1 });
        await controller.ConnectAsync(LocalDeviceProvider.Intiface, 12345, TestContext.Current.CancellationToken);
        clock.Advance(100); SetFreshAudio(controller, clock, .5); await wires[0].WaitForLevel(7);
        controller.Enabled = false; await controller.StopAsync(); controller.Enabled = true;
        await Task.Delay(150, TestContext.Current.CancellationToken); Assert.Equal(0, wires[0].Levels[^1]);
        clock.Advance(401); SetFreshAudio(controller, clock, .5); await wires[0].WaitForLevel(7);
        await controller.DisconnectAsync();
        await controller.ConnectAsync(LocalDeviceProvider.Intiface, 12345, TestContext.Current.CancellationToken);
        var firstLevel = await wires[1].Sent.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, firstLevel); Assert.Equal(0, controller.ActivePulseCount);
    }

    private static void SetFreshAudio(LocalDeviceController controller, ManualClock clock, double level)
    {
        // A previous zero pass can consume elapsed time before the first update.
        // Advance after publishing, then refresh so freshness checks start at age zero.
        controller.SetAudioLevel(level); clock.Advance(1); controller.SetAudioLevel(level);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Interlocked.Read(ref timestamp);
        public void Advance(long milliseconds) => Interlocked.Add(ref timestamp, milliseconds);
    }

    private sealed class FakeTransport : ILocalDeviceTransport
    {
        private readonly object gate = new(); private readonly List<int> levels = [];
        private bool lovense, hadNonzero;
        public bool BlockOutput, Disposed;
        public readonly TaskCompletionSource Nonzero = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ZeroAfterNonzero = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource OutputEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly System.Threading.Channels.Channel<int> Sent = System.Threading.Channels.Channel.CreateUnbounded<int>();
        public IReadOnlyList<int> Levels { get { lock (gate) return levels.ToArray(); } }
        public async Task WaitForLevel(int expected)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            while (await Sent.Reader.ReadAsync(deadline.Token) != expected) { }
        }
        public Task OpenAsync(Uri endpoint, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); lovense = endpoint.Scheme == "http"; return Task.CompletedTask; }
        public async Task<string> ExchangeAsync(string json, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(json);
            if (lovense)
            {
                var body = document.RootElement;
                if (body.GetProperty("command").GetString() == "GetToys")
                    return "{\"code\":200,\"data\":{\"toys\":{\"toy\":{\"name\":\"lush\",\"status\":1}}}}";
                var action = body.GetProperty("action").GetString()!;
                Apply(action == "Stop" ? 0 : int.Parse(action.Split(':')[1], System.Globalization.CultureInfo.InvariantCulture));
                return "{\"code\":200}";
            }
            var request = document.RootElement[0].EnumerateObject().Single(); var id = request.Value.GetProperty("Id").GetInt32();
            switch (request.Name)
            {
                case "RequestServerInfo": return JsonSerializer.Serialize(new[] { new { ServerInfo = new { Id = id, ProtocolVersionMajor = 4, MaxPingTime = 0 } } });
                case "RequestDeviceList": return JsonSerializer.Serialize(new[] { new { DeviceList = new { Id = id,
                    Devices = new Dictionary<string, object> { ["0"] = new { DeviceIndex = 0, DeviceName = "Motor",
                        DeviceFeatures = new Dictionary<string, object> { ["0"] = new { FeatureIndex = 0,
                            Output = new { Vibrate = new { Value = new[] { 0, 20 } } } } } } } } } });
                case "OutputCmd":
                    var value = request.Value.GetProperty("Command").GetProperty("Vibrate").GetProperty("Value").GetInt32();
                    if (BlockOutput) { OutputEntered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
                    Apply(value); break;
                case "StopCmd": Apply(0); break;
            }
            return JsonSerializer.Serialize(new[] { new { Ok = new { Id = id } } });
        }
        private void Apply(int value)
        {
            lock (gate)
            {
                levels.Add(value); Sent.Writer.TryWrite(value);
                if (value > 0) { hadNonzero = true; Nonzero.TrySetResult(); }
                else if (hadNonzero) ZeroAfterNonzero.TrySetResult();
            }
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
