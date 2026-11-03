using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using CCP.Classic.App;
using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class StartupUiTests
{
    [Fact]
    public Task OpeningManuallyNeverRegistersOrHidesEvenWithSavedStartupPreferences() => UiDispatcher.Run(() =>
    {
        using var run = new Run(new() { StartWithSystem = true, StartHidden = true });
        run.Arm(systemLaunch: false, recoverableTray: true);
        Assert.True(run.Window.IsVisible); Assert.Equal(WindowState.Normal, run.Window.WindowState);
        Assert.Empty(run.Writes); Assert.Null(Field("startupTray").GetValue(run.Window));
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task HiddenSystemLaunchAlwaysHasARecoveryPath(bool recoverableTray) => UiDispatcher.Run(() =>
    {
        using var run = new Run(new() { StartHidden = true });
        run.Arm(systemLaunch: true, recoverableTray);
        if (recoverableTray) Assert.False(run.Window.IsVisible);
        else { Assert.True(run.Window.IsVisible); Assert.True(run.Window.ShowInTaskbar); Assert.Equal(WindowState.Minimized, run.Window.WindowState); }
        Assert.Empty(run.Writes);
    });

    [Fact]
    public Task AutoRunWaitsForGlobalInputThenStartsOnlyOnce() => UiDispatcher.Run(() =>
    {
        using var run = new Run(new() { AutoRun = true }); run.Arm();
        run.Tick(); Assert.False(run.Window.Session.IsRunning);
        run.GlobalAvailable = true; run.Tick(); Assert.True(run.Window.Session.IsRunning);
        run.Window.Session.Stop(); run.Clock.Advance(30); run.Tick();
        Assert.False(run.Window.Session.IsRunning); Assert.Empty(run.Writes);
    });

    [Fact]
    public Task ReadyAutoRunRestoresTheHiddenPanelBeforeStartingOwnedEffects() => UiDispatcher.Run(() =>
    {
        using var run = new Run(new() { AutoRun = true, StartHidden = true });
        run.Arm(systemLaunch: true, recoverableTray: true); Assert.False(run.Window.IsVisible);
        var visibleAtStart = false;
        run.Window.Session.Changed += () => { if (run.Window.Session.IsRunning) visibleAtStart = run.Window.IsVisible; };
        run.GlobalAvailable = true; run.Tick();
        Assert.True(run.Window.Session.IsRunning); Assert.True(visibleAtStart); Assert.True(run.Window.IsVisible);
    });

    [Fact]
    public Task FailedGlobalInputShowsTheOwnerBeforeAutoRunAfterTwoSeconds() => UiDispatcher.Run(() =>
    {
        using var run = new Run(new() { AutoRun = true, StartHidden = true });
        run.Arm(systemLaunch: true, recoverableTray: true);
        run.Clock.Advance(1.999); run.Tick(); Assert.False(run.Window.Session.IsRunning); Assert.False(run.Window.IsVisible);
        run.Clock.Advance(.001); run.Tick();
        Assert.True(run.Window.Session.IsRunning); Assert.True(run.Window.IsVisible);
        Assert.Equal(WindowState.Normal, run.Window.WindowState); Assert.Equal("GlobalPanicUnavailable", run.Status);
    });

    [Fact]
    public Task PanicDuringTheStartupWaitCancelsRatherThanDelaysAutoRun() => UiDispatcher.Run(() =>
    {
        using var run = new Run(new() { AutoRun = true }); run.Arm();
        Assert.True(run.Window.Session.KeyDown("Escape")); run.Clock.Advance(10); run.GlobalAvailable = true; run.Tick();
        Assert.False(run.Window.Session.IsRunning); Assert.Empty(run.Writes);
    });

    [Fact]
    public Task ManualStartAndStopCannotBeFollowedByAStaleAutomaticStart() => UiDispatcher.Run(() =>
    {
        using var run = new Run(new() { AutoRun = true }); run.Arm();
        run.Window.Session.Start(); run.Window.Session.Stop(); run.Arm(); run.Clock.Advance(10); run.Tick();
        Assert.False(run.Window.Session.IsRunning);
    });

    [Fact]
    public Task TurningAutoRunOffCancelsThePendingStartEvenIfItIsReenabledLater() => UiDispatcher.Run(() =>
    {
        using var run = new Run(new() { AutoRun = true }); run.Arm();
        run.Window.Settings.AutoRun = false; run.Tick(); run.Window.Settings.AutoRun = true;
        run.Clock.Advance(10); run.Tick(); Assert.False(run.Window.Session.IsRunning);
    });

    [Fact]
    public Task ExplicitPreferenceWritesOnlyTheInjectedRegistrationAndSavesTheResult() => UiDispatcher.Run(() =>
    {
        using var run = new Run(new());
        run.Call("ChangeStartupPreference", true);
        Assert.True(run.Window.Settings.StartWithSystem); Assert.True(run.Store.Load(out _).StartWithSystem);
        Assert.Equal("\"C:\\Programs\\CCP Classic\\CCP.Classic.App.exe\" --startup", Assert.Single(run.Writes));
        run.Call("ChangeStartupPreference", false);
        Assert.False(run.Window.Settings.StartWithSystem); Assert.Null(run.Writes[1]);
        Assert.False(run.Store.Load(out _).StartWithSystem);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RegistrationFailureKeepsThePreviousPreferenceVisible(bool previous) => UiDispatcher.Run(() =>
    {
        using var run = new Run(new() { StartWithSystem = previous }); run.FailRegistration = true;
        run.Window.OpenSheet("Settings");
        var toggle = run.Window.SettingsWindow!.GetVisualDescendants().OfType<ToggleButton>().Single(toggle => toggle.Name == "StartWithSystem");
        toggle.IsChecked = !previous;
        run.Call("ChangeStartupPreference", !previous);
        Assert.Equal(previous, run.Window.Settings.StartWithSystem); Assert.Equal("StartupFailed", run.Status);
        Assert.Equal(previous, toggle.IsChecked);
        Assert.Empty(run.Writes);
    });

    [Fact]
    public Task ResetCannotBypassPanicDisabledDuringARunningSession() => UiDispatcher.Run(() =>
    {
        using var run = new Run(new() { StartWithSystem = true, GentleVisuals = true });
        run.Window.Session.Start(); run.Window.Session.SetPanicEnabled(false, true); run.Call("ResetSettings");
        Assert.True(run.Window.Session.IsRunning); Assert.True(run.Window.Settings.GentleVisuals);
        Assert.True(run.Window.Settings.StartWithSystem); Assert.Empty(run.Writes); Assert.Equal("DisabledByPanic", run.Status);
    });

    [Fact]
    public Task ResetUnregistersStopsAndRestoresDefaultsWithoutResettingProgress() => UiDispatcher.Run(() =>
    {
        using var run = new Run(new() { StartWithSystem = true, GentleVisuals = true, AutoRun = true, Language = "it" });
        run.Arm(); run.Window.Progress.AddXp(100); run.Window.Session.Start(); run.Call("ResetSettings");
        Assert.False(run.Window.Session.IsRunning); Assert.False(run.Window.Session.CompletedNormally);
        Assert.Null(Assert.Single(run.Writes)); Assert.False(run.Window.Settings.StartWithSystem);
        Assert.False(run.Window.Settings.GentleVisuals); Assert.False(run.Window.Settings.AutoRun);
        Assert.Equal("en", run.Window.Settings.Language); Assert.True(run.Window.Progress.Data.LifetimeXp >= 100);
        Assert.False(run.Store.Load(out _).StartWithSystem); run.Clock.Advance(10); run.Tick(); Assert.False(run.Window.Session.IsRunning);
    });

    [Fact]
    public Task FailedResetKeepsSettingsAndDoesNotLeaveTheSessionRunning() => UiDispatcher.Run(() =>
    {
        using var run = new Run(new() { StartWithSystem = true, GentleVisuals = true });
        run.Window.Session.Start(); run.FailRegistration = true; run.Call("ResetSettings");
        Assert.False(run.Window.Session.IsRunning); Assert.True(run.Window.Settings.StartWithSystem);
        Assert.True(run.Window.Settings.GentleVisuals); Assert.Equal("ResetFailed", run.Status);
    });

    [Fact]
    public Task DisposingStartupCancelsPendingWorkWithoutRemovingTheOptedInRegistration() => UiDispatcher.Run(() =>
    {
        using var run = new Run(new() { StartWithSystem = true, AutoRun = true }); run.Arm();
        run.Call("DisposeStartup"); run.Clock.Advance(10); run.GlobalAvailable = true; run.Tick();
        Assert.False(run.Window.Session.IsRunning); Assert.Empty(run.Writes); Assert.Null(Field("startupTray").GetValue(run.Window));
    });

    private static FieldInfo Field(string name) => typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private sealed class Run : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "classic-startup-ui-" + Guid.NewGuid().ToString("N"));
        public readonly MainWindow Window;
        public readonly ManualClock Clock = new();
        public readonly List<string?> Writes = [];
        public readonly SettingsStore Store;
        public bool GlobalAvailable, FailRegistration;
        public string Status => (string)Field("statusKey").GetValue(Window)!;
        public Run(ClassicSettings settings)
        {
            Store = new(Path.Combine(directory, "settings.json"));
            var registration = new StartupRegistration(windows: true, windowsWriter: command =>
            {
                if (FailRegistration) throw new IOException("Simulated startup failure");
                Writes.Add(command);
            });
            Window = new(settings, windows: true, globalPanicAvailable: () => GlobalAvailable, startupRegistration: registration);
            // A store at construction also enables the real progress path. Inject only our temporary settings store afterward.
            Field("store").SetValue(Window, Store);
            Field("startupTime").SetValue(Window, Clock);
            Field("startupProcess").SetValue(Window, (Func<(string, string?)>)(() => (@"C:\Programs\CCP Classic\CCP.Classic.App.exe", null)));
            Window.Show();
        }
        public void Arm(bool systemLaunch = false, bool recoverableTray = false) => Call("ApplyStartupPreferences", systemLaunch, recoverableTray);
        public void Tick() => Call("TickStartup");
        public void Call(string method, params object[] arguments) => typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Window, arguments);
        public void Dispose()
        {
            Call("DisposeStartup"); Window.Close();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
    private sealed class ManualClock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public void Advance(double seconds) => timestamp += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
