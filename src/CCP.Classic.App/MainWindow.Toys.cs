using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using CCP.Classic.Core;
using CCP.Classic.Core.LocalDevices;
using CCP.Classic.App.Controls;

namespace CCP.Classic.App;

public sealed partial class MainWindow
{
    private readonly LocalDeviceController haptics;
    private readonly Dictionary<LocalDeviceProvider, CancellationTokenSource> toysConnecting = [];
    private readonly Dictionary<LocalDeviceProvider, (Button Button, TextBlock Status)> toysRows = [];
    private bool toysDisposed;
    private int toysLevel;

    private void InitializeToys()
    {
        haptics.GlobalIntensity = Settings.Devices.GlobalIntensity; toysLevel = Progress.Level;
        haptics.ConfigureRouting(Settings.Devices.Routing);
        haptics.InputEnabled = Settings.Devices.ToyInputEnabled;
        haptics.ButtonPressed += ToysButtonPressed;
        haptics.Changed += ToysChanged;
        Progress.Recorded += ToysProgress;
        Progress.Changed += ToysLevelChanged;
        Progress.AchievementUnlocked += ToysAchievement;
        Session.StopRequested += ToysStopped;
    }

    private void FillToys(StackPanel content)
    {
        toysRows.Clear();
        foreach (var provider in Enum.GetValues<LocalDeviceProvider>())
        {
            var action = Button("Connect", () => _ = ToggleToyConnectionAsync(provider));
            action.Name = "ToyConnect_" + provider;
            var state = Label("", 11); state.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
            var controls = new StackPanel { Spacing = 3, MaxWidth = 240 };
            controls.Children.Add(action); controls.Children.Add(state);
            toysRows[provider] = (action, state); content.Children.Add(Row(provider.ToString(), controls));
        }
        content.Children.Add(Row("GlobalIntensity", ToySlider("ToyIntensity", Settings.Devices.GlobalIntensity, value =>
        { Settings.Devices.GlobalIntensity = value; haptics.GlobalIntensity = value; })));
        content.Children.Add(Note("LocalhostOnly"));
        var more = new StackPanel { Spacing = 10 };
        more.Children.Add(new ToyRoutingEditor(Settings.Devices.Routing, haptics, text, () => { }, () =>
        {
            var stopped = Session.Stop(finishUntilStopped: true);
            if (!stopped) SetStatus("DisabledByPanic");
            return stopped;
        }));
        more.Children.Add(new AudioSyncDspEditor(Settings.Devices.AudioSyncDsp, text, () => { }));
        more.Children.Add(new ToyInputOptionsEditor(Settings.Devices, haptics, text, () => { }));
        foreach (var provider in Enum.GetValues<LocalDeviceProvider>())
        {
            var port = new NumericUpDown { Minimum = 1, Maximum = 65535, Increment = 1, FormatString = "0", Width = 110,
                Name = "ToyPort_" + provider, Value = provider == LocalDeviceProvider.Intiface ? Settings.Devices.IntifacePort : Settings.Devices.LovensePort };
            port.ValueChanged += (_, _) =>
            {
                if (port.Value is not { } value) return;
                if (provider == LocalDeviceProvider.Intiface) Settings.Devices.IntifacePort = (int)value;
                else Settings.Devices.LovensePort = (int)value;
            };
            more.Children.Add(Row(provider.ToString(), port, "LocalPort"));
        }
        more.Children.Add(Label(text["PerEvent"]));
        foreach (var kind in Enum.GetValues<LocalDeviceEvent>())
        {
            var line = new Grid { ColumnDefinitions = new("*,130"), ColumnSpacing = 8 };
            var level = ToySlider("ToyLevel_" + kind, Settings.Devices.Level(kind), value => Settings.Devices.EventLevels[kind.ToString()] = value);
            line.Children.Add(OptionHelp.Wrap(level, "Intensity", text));
            if (kind is not (LocalDeviceEvent.AudioSync or LocalDeviceEvent.Video))
            {
                var patterns = Enum.GetValues<LocalHapticPattern>().Select(pattern => new ToyPattern(pattern, text["Pattern" + pattern])).ToArray();
                var choice = new ComboBox { Name = "ToyPattern_" + kind, ItemsSource = patterns,
                    SelectedItem = patterns.First(item => item.Pattern == Settings.Devices.Pattern(kind)), MinHeight = 26 };
                AutomationProperties.SetName(choice, text["HapticPattern"] + " · " + text[ToyEventTitle(kind)]);
                choice.SelectionChanged += (_, _) =>
                { if (choice.SelectedItem is ToyPattern selected) Settings.Devices.EventPatterns[kind.ToString()] = (int)selected.Pattern; };
                var patternHelp = OptionHelp.Wrap(choice, "HapticPattern", text); Grid.SetColumn(patternHelp, 1); line.Children.Add(patternHelp);
            }
            more.Children.Add(Label(text[ToyEventTitle(kind)], 12)); more.Children.Add(line);
        }
        var sync = Toggle(Settings.Devices.AudioSyncEnabled, text["AudioSync"], value =>
        {
            Settings.Devices.AudioSyncEnabled = value;
            if (!value) haptics.SetAudioLevel(0);
        });
        sync.Name = "ToyAudioSync"; content.Children.Add(Row("AudioSync", sync));
        content.Children.Add(Row("FunScriptEnabled", Toggle(Settings.Devices.FunScriptEnabled, text["FunScriptEnabled"], value =>
        { Settings.Devices.FunScriptEnabled = value; if (!value) haptics.SetScriptLevel(0, null); })));
        more.Children.Add(Row("FunScriptToVibration", Toggle(Settings.Devices.FunScriptToVibration, text["FunScriptToVibration"], value => Settings.Devices.FunScriptToVibration = value)));
        more.Children.Add(Note("FunScriptHelp"));
        content.Children.Add(Row("MandatoryVideos", Toggle(Settings.Devices.VideoSyncEnabled, text["MandatoryVideos"], value =>
        { Settings.Devices.VideoSyncEnabled = value; if (!value) haptics.SetVideoLevel(0); })));
        content.Children.Add(Row("LuminanceSync", Toggle(Settings.Devices.LuminanceSyncEnabled, text["LuminanceSync"], value =>
        { Settings.Devices.LuminanceSyncEnabled = value; if (!value) haptics.SetLuminanceLevel(0); })));
        content.Children.Add(Row("LuminanceIntensity", ToySlider("LuminanceIntensity", Settings.Devices.LuminanceSyncIntensity,
            value => Settings.Devices.LuminanceSyncIntensity = value)));
        var test = Button("Test", () => EmitToys(LocalDeviceEvent.Manual)); test.Name = "ToyTest";
        var stop = Button("Stop", () => { if (!Session.Stop(finishUntilStopped: true)) SetStatus("DisabledByPanic"); }); stop.Name = "ToyStop";
        var manual = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        manual.Children.Add(OptionHelp.Wrap(test, "Test", text)); manual.Children.Add(OptionHelp.Wrap(stop, "Stop", text)); content.Children.Add(manual);
        var rows = content.Children.ToArray(); content.Children.Clear(); content.Spacing = 0;
        content.Children.Add(OptionLayout.Group(rows.Take(4), text["OptionsGeneral"]));
        content.Children.Add(OptionLayout.Group(rows.Skip(4), text["AudioSync"], 1));
        content.Children.Add(new Expander { Name = "ToyMore", Header = text["More"], Content = more });
        RefreshToys();
    }

    private Slider ToySlider(string name, double value, Action<double> changed)
    {
        var slider = new Slider { Name = name, Minimum = 0, Maximum = 100, Value = value * 100,
            SmallChange = 1, LargeChange = 10, Width = 145, IsSnapToTickEnabled = true, TickFrequency = 1 };
        AutomationProperties.SetName(slider, text[name == "ToyIntensity" ? "GlobalIntensity" : "Intensity"]);
        slider.PropertyChanged += (_, change) => { if (change.Property == RangeBase.ValueProperty) changed(slider.Value / 100); };
        return slider;
    }

    private async Task ToggleToyConnectionAsync(LocalDeviceProvider provider)
    {
        if (toysDisposed) return;
        if (toysConnecting.Remove(provider, out var pending)) pending.Cancel();
        if (pending is not null || haptics.GetStatus(provider) is LocalDeviceStatus.Connected or LocalDeviceStatus.Connecting)
        {
            await haptics.DisconnectAsync(provider); RefreshToys(); return;
        }
        using var cancellation = new CancellationTokenSource(); toysConnecting[provider] = cancellation;
        try
        {
            var port = provider == LocalDeviceProvider.Intiface ? Settings.Devices.IntifacePort : Settings.Devices.LovensePort;
            await haptics.ConnectAsync(provider, port, cancellation.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException)
        { if (!toysDisposed) SetStatus("DeviceUnavailable"); }
        finally
        {
            if (toysConnecting.TryGetValue(provider, out var current) && ReferenceEquals(current, cancellation)) toysConnecting.Remove(provider);
            if (!toysDisposed) RefreshToys();
        }
    }

    private void ToysChanged() => Dispatcher.UIThread.Post(() => { if (!toysDisposed) RefreshToys(); });
    private void ToysButtonPressed(LocalButtonPress press) => Dispatcher.UIThread.Post(() =>
    {
        if (!toysDisposed && Session.IsRunning && Settings.Devices.AttentionCheckToyButton && haptics.IsCurrentInput(press))
            effects?.TryAcceptToyAttention(press.ObservedAt);
    });
    private void RefreshToys()
    {
        if (toysDisposed) return;
        foreach (var (provider, row) in toysRows)
        {
            var state = haptics.GetStatus(provider);
            row.Button.Content = text[state is LocalDeviceStatus.Connecting or LocalDeviceStatus.Connected ? "Disconnect" : "Connect"];
            row.Status.Text = text[state switch { LocalDeviceStatus.Connecting => "DeviceConnecting", LocalDeviceStatus.Connected => "DeviceConnected",
                LocalDeviceStatus.Unavailable => "DeviceUnavailable", _ => "NotConnected" }];
            if (state == LocalDeviceStatus.Connected)
            {
                var names = haptics.Devices.Where(device => device.Provider == provider).Select(device => device.Name).ToArray();
                row.Status.Text += " · " + (names.Length == 0 ? text["NoDevices"] : string.Join(", ", names));
            }
        }
    }

    private void EmitToys(LocalDeviceEvent kind)
    {
        if (toysDisposed || kind != LocalDeviceEvent.Manual && !Session.IsRunning
            || kind == LocalDeviceEvent.AudioSync && !Settings.Devices.AudioSyncEnabled) return;
        haptics.GlobalIntensity = Settings.Devices.GlobalIntensity;
        var duration = kind is LocalDeviceEvent.Achievement or LocalDeviceEvent.LevelUp ? 1000 : kind == LocalDeviceEvent.Manual ? 600 : 400;
        haptics.Emit(kind, Settings.Devices.Level(kind), TimeSpan.FromMilliseconds(duration), Settings.Devices.Pattern(kind));
    }
    private void ToysAudioLevel(double level)
    {
        if (toysDisposed) return;
        haptics.GlobalIntensity = Settings.Devices.GlobalIntensity;
        haptics.SetAudioLevel(Session.IsRunning && Settings.Devices.AudioSyncEnabled
            ? level * Settings.Devices.Level(LocalDeviceEvent.AudioSync) : 0);
    }
    private void ToysAudioBands(Media.AudioDspLevels levels)
    {
        if (toysDisposed) return;
        haptics.GlobalIntensity = Settings.Devices.GlobalIntensity;
        var gain = Session.IsRunning && Settings.Devices.AudioSyncEnabled ? Settings.Devices.Level(LocalDeviceEvent.AudioSync) : 0;
        haptics.SetAudioBands(levels.Combined * gain, levels.Low * gain, levels.High * gain, Settings.Devices.AudioSyncDsp.BandSplit);
    }
    private void ToysScriptLevel(Media.ScriptOutput output)
    {
        if (toysDisposed) return;
        haptics.GlobalIntensity = Settings.Devices.GlobalIntensity;
        var enabled = Session.IsRunning && Settings.Devices.FunScriptEnabled;
        haptics.SetScriptLevel(enabled ? output.Intensity : 0, enabled ? output.Position : null);
    }
    private void ToysLuminanceLevel(double value)
    {
        if (toysDisposed) return;
        haptics.GlobalIntensity = Settings.Devices.GlobalIntensity;
        haptics.SetLuminanceLevel(Session.IsRunning && Settings.Devices.LuminanceSyncEnabled ? value : 0);
    }
    private void ToysVideoLevel(double value)
    {
        if (toysDisposed) return;
        haptics.GlobalIntensity = Settings.Devices.GlobalIntensity;
        haptics.SetVideoLevel(Session.IsRunning && Settings.Devices.VideoSyncEnabled ? value : 0);
    }

    private void ToysProgress(ProgressEvent value)
    {
        if (value.Kind == ProgressEventKind.Flash) EmitToys(LocalDeviceEvent.Flash);
        else if (value.Kind == ProgressEventKind.Bubble) EmitToys(LocalDeviceEvent.BubblePop);
    }
    private void ToysAchievement(AchievementDefinition _) => EmitToys(LocalDeviceEvent.Achievement);
    private void ToysLevelChanged()
    {
        var level = Progress.Level;
        if (level > toysLevel) EmitToys(LocalDeviceEvent.LevelUp);
        toysLevel = level;
    }
    private void ToysStopped() => _ = StopToysAsync();
    private Task StopToysAsync()
    {
        foreach (var pending in toysConnecting.Values.ToArray()) pending.Cancel();
        return haptics.StopAsync();
    }
    private void DisposeToys()
    {
        if (toysDisposed) return;
        toysDisposed = true;
        if (effects is not null)
        {
            effects.DeviceEvent -= EmitToys; effects.AudioBandsChanged -= ToysAudioBands;
            effects.ScriptLevelChanged -= ToysScriptLevel; effects.LuminanceLevelChanged -= ToysLuminanceLevel;
            effects.VideoLevelChanged -= ToysVideoLevel;
        }
        haptics.Changed -= ToysChanged; haptics.ButtonPressed -= ToysButtonPressed;
        Progress.Recorded -= ToysProgress; Progress.Changed -= ToysLevelChanged;
        Progress.AchievementUnlocked -= ToysAchievement; Session.StopRequested -= ToysStopped;
        foreach (var pending in toysConnecting.Values.ToArray()) pending.Cancel();
        haptics.Dispose(); toysRows.Clear();
    }
    private static string ToyEventTitle(LocalDeviceEvent kind) => kind switch
    {
        LocalDeviceEvent.Flash => "FlashImages", LocalDeviceEvent.Subliminal => "Subliminals", LocalDeviceEvent.BubblePop => "BubblePop",
        LocalDeviceEvent.BouncingText => "BouncingText", LocalDeviceEvent.Video => "MandatoryVideos", LocalDeviceEvent.Attention => "AttentionCheck",
        LocalDeviceEvent.LockCard => "LockCard", LocalDeviceEvent.Achievement => "Achievements", LocalDeviceEvent.LevelUp => "Level",
        LocalDeviceEvent.Keyword => "KeywordTriggers", LocalDeviceEvent.MindWipe => "MindWipe", LocalDeviceEvent.BrainDrain => "BrainDrain",
        LocalDeviceEvent.Manual => "Test", _ => kind.ToString()
    };
    private sealed record ToyPattern(LocalHapticPattern Pattern, string Label)
    { public override string ToString() => Label; }
}
