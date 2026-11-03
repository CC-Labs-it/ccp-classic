using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using CCP.Classic.App;
using CCP.Classic.Core;
using CCP.Classic.Core.LocalDevices;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class ToysUiTests
{
    [Fact]
    public Task OpeningControlsAndChangingPreferencesNeverConnects() => WithWindow(async run =>
    {
        await UiDispatcher.Run(() =>
        {
            run.Window.OpenSheet("Toys");
            Assert.Equal(2, run.Window.SettingsWindow!.GetVisualDescendants().OfType<Button>().Count(button => button.Name?.StartsWith("ToyConnect_", StringComparison.Ordinal) == true));
            Assert.False(run.Find<Expander>("ToyMore").IsExpanded);
            Assert.Equal(13, run.Window.SettingsWindow!.GetLogicalDescendants().OfType<ComboBox>().Count(choice => choice.Name?.StartsWith("ToyPattern_", StringComparison.Ordinal) == true));
            run.ExpandMore();
            run.Find<Slider>("ToyIntensity").Value = 42;
            run.Find<NumericUpDown>("ToyPort_LovenseConnect").Value = 20202;
            run.Find<Slider>("ToyLevel_Flash").Value = 23;
            run.Find<ComboBox>("ToyPattern_Flash").SelectedIndex = (int)LocalHapticPattern.Heartbeat;
            Assert.Equal(.42, run.Window.Settings.Devices.GlobalIntensity); Assert.Equal(.42, run.Controller.GlobalIntensity);
            Assert.Equal(20202, run.Window.Settings.Devices.LovensePort); Assert.Equal(.23, run.Window.Settings.Devices.Level(LocalDeviceEvent.Flash));
            Assert.Equal(LocalHapticPattern.Heartbeat, run.Window.Settings.Devices.Pattern(LocalDeviceEvent.Flash));
            run.Window.Session.Start(); Assert.Equal(0, run.Created); Assert.Equal(0, run.Controller.ActivePulseCount);
        });
    });

    [Fact]
    public Task ExplicitConnectUsesTheConfiguredLocalPortAndDisconnectWorksWithPanicDisabled() => WithWindow(async run =>
    {
        await UiDispatcher.Run(() => run.Window.Settings.Devices.LovensePort = 20202);
        await run.Toggle();
        Assert.Equal(1, run.Created); Assert.Equal("127.0.0.1", run.Wire.Endpoint!.Host); Assert.Equal(20202, run.Wire.Endpoint.Port);
        await UiDispatcher.Run(() =>
        {
            Assert.Equal(LocalDeviceStatus.Connected, run.Controller.Status);
            run.Window.Session.Start(); run.Window.Session.SetPanicEnabled(false, true);
        });
        await run.Toggle();
        Assert.Equal(LocalDeviceStatus.Disconnected, run.Controller.Status); Assert.True(run.Wire.Disposed);
        await UiDispatcher.Run(() => Assert.True(run.Window.Session.IsRunning));
    });

    [Fact]
    public Task IdleManualTestStopsThroughTheSameSessionGate() => WithWindow(async run =>
    {
        await run.Toggle();
        await UiDispatcher.Run(() => { run.Window.OpenSheet("Toys"); run.ExpandMore(); run.Click("ToyTest"); Assert.False(run.Window.Session.IsRunning); });
        await run.Wire.Nonzero.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        await UiDispatcher.Run(() => run.Click("ToyStop"));
        await run.Wire.Zero.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Equal(0, run.Controller.ActivePulseCount);
    });

    [Fact]
    public Task PanicDisabledAlsoBlocksTheManualTestStopButton() => WithWindow(async run =>
    {
        await run.Toggle();
        await UiDispatcher.Run(() =>
        {
            run.Window.OpenSheet("Toys"); run.ExpandMore(); run.Window.Session.SetPanicEnabled(false, true);
            run.Click("ToyTest"); Assert.Equal(1, run.Controller.ActivePulseCount); run.Click("ToyStop");
            Assert.Equal(1, run.Controller.ActivePulseCount); Assert.Equal("DisabledByPanic", run.Status);
            run.Window.Session.SetPanicEnabled(true); run.Click("ToyStop"); Assert.Equal(0, run.Controller.ActivePulseCount);
        });
    });

    [Theory]
    [InlineData(LocalDeviceEvent.Flash)]
    [InlineData(LocalDeviceEvent.BubblePop)]
    public Task ActualProgressEventsUseOnlyTheirConfiguredLevel(LocalDeviceEvent kind) => WithWindow(async run =>
    {
        await run.Toggle();
        await UiDispatcher.Run(() =>
        {
            run.Only(kind); run.Window.Session.Start();
            if (kind == LocalDeviceEvent.Flash) run.Window.Progress.FlashShown();
            else if (kind == LocalDeviceEvent.BubblePop) run.Window.Progress.BubblePopped();
            Assert.Equal(1, run.Controller.ActivePulseCount);
            run.Window.Session.Stop(); Assert.Equal(0, run.Controller.ActivePulseCount);
        });
    });

    [Fact]
    public Task VideoCompletionDoesNotCreateALatePulse() => WithWindow(async run =>
    {
        await run.Toggle();
        await UiDispatcher.Run(() =>
        {
            run.Only(LocalDeviceEvent.Video); run.Window.Session.Start();
            run.Window.Progress.VideoFinished(1);
            Assert.Equal(0, run.Controller.ActivePulseCount);
        });
    });

    [Theory]
    [InlineData(LocalDeviceEvent.LevelUp)]
    [InlineData(LocalDeviceEvent.Achievement)]
    public Task ActualProgressChangesEmitCelebrationPulsesDuringTheSession(LocalDeviceEvent kind) => WithWindow(async run =>
    {
        await run.Toggle();
        await UiDispatcher.Run(() =>
        {
            run.Only(kind); run.Window.Session.Start();
            if (kind == LocalDeviceEvent.LevelUp) run.Window.Progress.AddXp(800);
            else { run.Window.Progress.Data.PinkSeconds = 36000; run.Window.Progress.RefreshAchievements(); }
            Assert.Equal(1, run.Controller.ActivePulseCount);
        });
    });

    [Fact]
    public Task AutomaticEventsAreIgnoredOutsideSessionsAndMutedEventsStayOff() => WithWindow(async run =>
    {
        await run.Toggle();
        await UiDispatcher.Run(() =>
        {
            run.Call("EmitToys", LocalDeviceEvent.Flash); Assert.Equal(0, run.Controller.ActivePulseCount);
            run.Window.Session.Start(); run.Window.Settings.Devices.EventLevels["Flash"] = 0;
            run.Window.Progress.FlashShown(); run.Call("EmitToys", LocalDeviceEvent.AudioSync);
            Assert.Equal(0, run.Controller.ActivePulseCount);
        });
    });

    [Fact]
    public Task StoppingCancelsAnInFlightUserConnectionAndNeverReconnects() => WithWindow(async run =>
    {
        run.BlockOpen = true; Task? connect = null;
        await UiDispatcher.Run(() => connect = run.CallAsync("ToggleToyConnectionAsync", LocalDeviceProvider.LovenseConnect));
        await run.Wire.OpenEntered.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        await UiDispatcher.Run(() => Assert.True(run.Window.Session.Stop()));
        await connect!;
        Assert.True(run.Wire.Disposed); Assert.Equal(LocalDeviceStatus.Disconnected, run.Controller.Status); Assert.Equal(1, run.Created);
    });

    [Fact]
    public Task ClosingWithPanicDisabledCancelsConnectionsAndFlushesAnActiveTest() => WithWindow(async run =>
    {
        await run.Toggle();
        await UiDispatcher.Run(() =>
        {
            run.Window.Session.SetPanicEnabled(false, true); run.Call("EmitToys", LocalDeviceEvent.Manual);
        });
        await run.Wire.Nonzero.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        await run.Close(); await run.Controller.Completion.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);
        Assert.True(run.Wire.Disposed); Assert.Equal(0, run.Controller.ActivePulseCount); Assert.Equal(0, run.Wire.LastLevel);
    });

    private static async Task WithWindow(Func<Run, Task> test)
    {
        Run? run = null; await UiDispatcher.Run(() => run = new());
        try { await test(run!); }
        finally { await run!.Close(); await run.Controller.DisposeAsync(); }
    }
    private sealed class Run
    {
        public readonly MainWindow Window;
        public readonly LocalDeviceController Controller;
        public FakeTransport Wire = null!;
        public int Created;
        public bool BlockOpen;
        private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Status => (string)typeof(MainWindow).GetField("statusKey", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Window)!;
        public Run()
        {
            Controller = new(() => new(() => { Created++; Wire = new() { BlockOpen = BlockOpen }; return Wire; }));
            Window = new(deviceController: Controller); Window.Closed += (_, _) => closed.TrySetResult(); Window.Show();
        }
        public T Find<T>(string name) where T : Control => Window.SettingsWindow!.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);
        public void ExpandMore() => Find<Expander>("ToyMore").IsExpanded = true;
        public async Task Close()
        {
            if (!closed.Task.IsCompleted) await UiDispatcher.Run(() => Window.Close());
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);
        }
        public void Click(string name) => Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        public object? Call(string method, params object[] args) => typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Window, args);
        public Task CallAsync(string method, params object[] args) => (Task)Call(method, args)!;
        public async Task Toggle()
        {
            Task? pending = null; await UiDispatcher.Run(() => pending = CallAsync("ToggleToyConnectionAsync", LocalDeviceProvider.LovenseConnect));
            await pending!;
        }
        public void Only(LocalDeviceEvent kind)
        {
            foreach (var key in Window.Settings.Devices.EventLevels.Keys.ToArray()) Window.Settings.Devices.EventLevels[key] = 0;
            Window.Settings.Devices.EventLevels[kind.ToString()] = .5;
        }
    }
    private sealed class FakeTransport : ILocalDeviceTransport
    {
        public bool BlockOpen;
        public volatile bool Disposed;
        public Uri? Endpoint;
        private int lastLevel;
        private bool emitted;
        public int LastLevel => Volatile.Read(ref lastLevel);
        public readonly TaskCompletionSource OpenEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Nonzero = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Zero = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task OpenAsync(Uri endpoint, CancellationToken token)
        {
            Endpoint = endpoint; OpenEntered.TrySetResult();
            if (BlockOpen) await Task.Delay(Timeout.Infinite, token);
            token.ThrowIfCancellationRequested();
        }
        public Task<string> ExchangeAsync(string json, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var document = JsonDocument.Parse(json); var body = document.RootElement;
            if (body.GetProperty("command").GetString() == "GetToys")
                return Task.FromResult("{\"code\":200,\"data\":{\"toys\":{\"toy\":{\"name\":\"lush\",\"status\":1}}}}");
            var action = body.GetProperty("action").GetString()!;
            var level = action == "Stop" ? 0 : int.Parse(action.Split(':')[1], System.Globalization.CultureInfo.InvariantCulture);
            Volatile.Write(ref lastLevel, level);
            if (level > 0) { emitted = true; Nonzero.TrySetResult(); }
            else if (emitted) Zero.TrySetResult();
            return Task.FromResult("{\"code\":200}");
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
