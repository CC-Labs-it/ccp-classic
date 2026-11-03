using System.Collections.Concurrent;
using System.Text.Json;
using CCP.Classic.Core.LocalDevices;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class LocalDeviceRoutingTests
{
    [Fact]
    public async Task RolesCapTrimAndLiveDisablingAffectTheActualDeviceCommands()
    {
        var wire = new Wire(); await using var controller = new LocalDeviceController(() => new(() => wire));
        var options = new LocalDeviceRoutingOptions { MaximumPower = .6 };
        options.DeviceRules["Intiface:0"] = new() { Role = LocalToyRole.Reward, Trim = .5 };
        options.DeviceRules["Intiface:1"] = new() { Role = LocalToyRole.Punish };
        options.EventTargets[LocalDeviceEvent.BubblePop.ToString()] = LocalToyRole.Reward;
        controller.GlobalIntensity = 1; controller.ConfigureRouting(options);
        options.DeviceRules["Intiface:0"].Trim = 1;
        Assert.True(await controller.ConnectAsync(LocalDeviceProvider.Intiface, 12345, TestContext.Current.CancellationToken));
        controller.Emit(LocalDeviceEvent.BubblePop, 1, TimeSpan.FromSeconds(10));
        await Until(() => wire.Levels.GetValueOrDefault(0) == 30 && wire.Levels.ContainsKey(1)); Assert.Equal(0, wire.Levels[1]);
        options.DeviceRules["Intiface:0"].Enabled = false; options.DeviceRules["Intiface:0"].Trim = .5; controller.ConfigureRouting(options);
        await Until(() => wire.Levels[0] == 0); Assert.Equal(0, wire.Levels[1]);
        options.DeviceRules["Intiface:0"].Enabled = true; options.EventTargets[LocalDeviceEvent.BubblePop.ToString()] = LocalToyRole.All;
        controller.ConfigureRouting(options); await Until(() => wire.Levels[0] == 30 && wire.Levels[1] == 60);
        await controller.StopAsync(); Assert.All(wire.Levels.Values, level => Assert.Equal(0, level));
    }

    [Fact]
    public void PersistedInvalidRoutingValuesAreBoundedAndDeviceIdentityIsProviderQualified()
    {
        var options = new LocalDeviceRoutingOptions { MaximumPower = double.NaN }; var info = new LocalDeviceInfo("0", "Toy", []);
        options.Device(info).Trim = double.PositiveInfinity; options.Device(info).Role = (LocalToyRole)999;
        options.Normalize(); Assert.Equal(.7, options.MaximumPower); Assert.Equal(1, options.Device(info).Trim);
        Assert.Equal(LocalToyRole.All, options.Device(info).Role);
        Assert.NotSame(options.Device(info), options.Device(info with { Provider = LocalDeviceProvider.LovenseConnect }));
    }

    [Fact]
    public async Task BandSplitUsesTwoVibrationMotorsWhileSingleMotorToysKeepTheCombinedLevel()
    {
        var wire = new Wire(); await using var controller = new LocalDeviceController(() => new(() => wire));
        controller.ConfigureRouting(new() { MaximumPower = 1, SoftRampMilliseconds = 1 });
        controller.GlobalIntensity = 1;
        Assert.True(await controller.ConnectAsync(LocalDeviceProvider.Intiface, 12345, TestContext.Current.CancellationToken));
        controller.SetAudioBands(.5, .2, .8, true);
        await Until(() => wire.Motors.GetValueOrDefault((0, 0)) == 20 && wire.Motors.GetValueOrDefault((0, 1)) == 80
            && wire.Motors.GetValueOrDefault((1, 0)) == 50, () => controller.SetAudioBands(.5, .2, .8, true));
        controller.SetAudioLevel(.4);
        await Until(() => wire.Motors.Values.All(value => value == 40));
        await controller.StopAsync(); Assert.All(wire.Motors.Values, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task ScriptPositionsAreExplicitAndPausingNeverSendsAHomeMovement()
    {
        var wire = new Wire(); await using var controller = new LocalDeviceController(() => new(() => wire));
        controller.ConfigureRouting(new() { SoftRampMilliseconds = 1 });
        controller.GlobalIntensity = 1;
        Assert.True(await controller.ConnectAsync(LocalDeviceProvider.Intiface, 12345, TestContext.Current.CancellationToken));
        controller.SetScriptLevel(.6, .3);
        await Until(() => wire.Motors.GetValueOrDefault((1, 1)) == 30 && wire.Levels.GetValueOrDefault(0) == 60,
            () => controller.SetScriptLevel(.6, .3));
        var positions = wire.PositionCommands;
        controller.SetScriptLevel(0, null); await Until(() => wire.Motors.Values.All(value => value == 0));
        controller.SetAudioLevel(.4); await Until(() => wire.Levels.GetValueOrDefault(0) == 40);
        Assert.Equal(positions, wire.PositionCommands);
        await controller.StopAsync(); Assert.All(wire.Motors.Values, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task LuminanceUsesItsRolePowerAndTrimAndCanBeZeroedIndependently()
    {
        var wire = new Wire(); await using var controller = new LocalDeviceController(() => new(() => wire));
        controller.GlobalIntensity = 1; controller.ConfigureRouting(new()
        {
            MaximumPower = .4, SoftRampMilliseconds = 1, LuminanceRole = LocalToyRole.Reward,
            DeviceRules = new() { ["Intiface:0"] = new() { Role = LocalToyRole.Reward, Trim = .5 }, ["Intiface:1"] = new() { Role = LocalToyRole.Punish } }
        });
        Assert.True(await controller.ConnectAsync(LocalDeviceProvider.Intiface, 12345, TestContext.Current.CancellationToken));
        controller.SetLuminanceLevel(1);
        await Until(() => wire.Levels.GetValueOrDefault(0) == 20 && wire.Levels.ContainsKey(1), () => controller.SetLuminanceLevel(1)); Assert.Equal(0, wire.Levels[1]);
        controller.SetLuminanceLevel(0); await Until(() => wire.Levels.Values.All(level => level == 0));
    }
    [Fact]
    public async Task ContinuousVideoUsesRolesAndTrimWithoutAllocatingTransientPulses()
    {
        var wire = new Wire(); await using var controller = new LocalDeviceController(() => new(() => wire));
        controller.GlobalIntensity = 1; controller.ConfigureRouting(new()
        {
            DeviceRules = new() { ["Intiface:0"] = new() { Role = LocalToyRole.Ambient, Trim = .5 }, ["Intiface:1"] = new() { Role = LocalToyRole.Punish } },
            EventTargets = new() { [LocalDeviceEvent.Video.ToString()] = LocalToyRole.Ambient }
        });
        Assert.True(await controller.ConnectAsync(LocalDeviceProvider.Intiface, 12345, TestContext.Current.CancellationToken));
        await Until(() => wire.Levels.GetValueOrDefault(0) == 25 && wire.Levels.ContainsKey(1), () => controller.SetVideoLevel(.5));
        Assert.Equal(0, wire.Levels[1]); Assert.Equal(0, controller.ActivePulseCount);
        controller.SetVideoLevel(0); await Until(() => wire.Levels.Values.All(level => level == 0));
    }
    private static async Task Until(Func<bool> predicate, Action? refresh = null)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); stop.CancelAfter(TimeSpan.FromSeconds(5));
        while (!predicate()) { refresh?.Invoke(); await Task.Delay(20, stop.Token); }
    }
    private sealed class Wire : ILocalDeviceTransport
    {
        public ConcurrentDictionary<int, int> Levels { get; } = new();
        public ConcurrentDictionary<(int Device, int Motor), int> Motors { get; } = new();
        public int PositionCommands;
        public Task OpenAsync(Uri endpoint, CancellationToken token) => Task.CompletedTask;
        public Task<string> ExchangeAsync(string request, CancellationToken token)
        {
            using var document = JsonDocument.Parse(request); var command = document.RootElement[0].EnumerateObject().Single();
            var id = command.Value.GetProperty("Id").GetInt32();
            if (command.Name == "RequestServerInfo") return Task.FromResult($"[{{\"ServerInfo\":{{\"Id\":{id},\"ProtocolVersionMajor\":4}}}}]");
            if (command.Name == "RequestDeviceList") return Task.FromResult(JsonSerializer.Serialize(new[]
                { new { DeviceList = new { Id = id, Devices = Enumerable.Range(0, 2).ToDictionary(index => index.ToString(), Device) } } }));
            if (command.Name == "OutputCmd")
            {
                var value = command.Value; var device = value.GetProperty("DeviceIndex").GetInt32();
                var output = value.GetProperty("Command").EnumerateObject().Single();
                if (output.Name == "Position") Interlocked.Increment(ref PositionCommands);
                var level = output.Value.GetProperty("Value").GetInt32();
                Levels[device] = level; Motors[(device, value.GetProperty("FeatureIndex").GetInt32())] = level;
            }
            if (command.Name == "StopCmd") { Levels[0] = Levels[1] = 0; foreach (var key in Motors.Keys) Motors[key] = 0; }
            return Task.FromResult($"[{{\"Ok\":{{\"Id\":{id}}}}}]");
        }
        private static object Device(int index) => new { DeviceIndex = index, DeviceName = "Toy" + index,
            DeviceFeatures = Enumerable.Range(0, 2).ToDictionary(motor => motor.ToString(), motor => new { FeatureIndex = motor,
                Output = new Dictionary<string, object> { [index == 1 && motor == 1 ? "Position" : "Vibrate"] = new { Value = new[] { 0, 100 } } } }) };
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
