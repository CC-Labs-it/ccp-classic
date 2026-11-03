using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CCP.Classic.App.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public sealed class LayeredAudioEditor : OptionEditor
{
    private readonly LayeredAudioOptions options;
    private readonly Action? apply, gainChanged;
    private readonly Func<string?>? status;
    private readonly TextBlock statusLabel = new() { Name = "LayeredAudioStatus", TextWrapping = TextWrapping.Wrap,
        Foreground = Brush.Parse("#FFB0D9"), FontSize = 12 };
    private readonly StackPanel tracks = new() { Spacing = 8, Name = "LayeredTracks" };
    private readonly Button add;
    private long pickerGeneration;

    public LayeredAudioEditor(LayeredAudioOptions options, LocalizationService text, Action changed,
        Action? apply = null, Action? gainChanged = null, Func<string?>? status = null) : base(text, changed)
    {
        this.options = options; this.apply = apply; this.gainChanged = gainChanged; this.status = status;
        Spacing = 8; Heading("LayeredAudio");
        Children.Add(Help("LayeredAudioHelp"));
        var enabled = new CheckBox { Name = "LayeredAudioEnabled", Content = text["LayeredAudioEnabled"], IsChecked = options.Enabled };
        enabled.IsCheckedChanged += (_, _) => { options.Enabled = enabled.IsChecked == true; Refresh(); Structural(); };
        Children.Add(OptionHelp.Wrap(enabled, "LayeredAudioEnabled", text));
        Children.Add(Gain("LayeredMasterVolume", "MasterVolume", options.MasterVolume, value => options.MasterVolume = value));
        Toggle("LayeredAudioOnly", options.AudioOnlySession, value => options.AudioOnlySession = value);
        Children.Add(Help("LayeredAudioOnlyHelp"));
        add = new Button { Name = "LayeredAddTrack", Content = text["LayeredAudioAdd"], HorizontalAlignment = HorizontalAlignment.Left };
        add.Classes.Add("pill"); add.Click += async (_, _) => await Pick();
        Children.Add(OptionHelp.Wrap(add, "LayeredAudioAdd", text)); Children.Add(tracks); Children.Add(statusLabel); Refresh(); RefreshStatus();
        DetachedFromVisualTree += (_, _) => pickerGeneration++;
    }

    private TextBlock Help(string key) => new() { Text = Text[key], TextWrapping = TextWrapping.Wrap,
        Foreground = Brush.Parse("#B8ABCE"), FontSize = 12 };
    private void Structural() { Changed(); apply?.Invoke(); }
    private void UpdateGain() { Changed(); gainChanged?.Invoke(); }
    public void RefreshStatus()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            var generation = pickerGeneration;
            Dispatcher.UIThread.Post(() => { if (generation == pickerGeneration) RefreshStatus(); }); return;
        }
        var key = status?.Invoke(); statusLabel.Text = key is null ? "" : Text[key]; statusLabel.IsVisible = key is not null;
    }

    private async Task Pick()
    {
        var owner = TopLevel.GetTopLevel(this);
        if (owner?.StorageProvider is not { } provider || options.Tracks.Count >= LayeredAudioOptions.MaximumTracks) return;
        var generation = ++pickerGeneration;
        try
        {
            var files = await provider.OpenFilePickerAsync(new() { AllowMultiple = true, Title = Text["LayeredAudioAdd"],
                FileTypeFilter = [new(Text["Audio"]) { Patterns = LocalAudioFormats.Patterns }] });
            if (generation == pickerGeneration && ReferenceEquals(owner, TopLevel.GetTopLevel(this)))
                AddTracks(files.Select(file => file.TryGetLocalPath()).OfType<string>());
        }
        catch (Exception failure) when (failure is OperationCanceledException or InvalidOperationException or IOException or UnauthorizedAccessException) { }
    }

    internal int AddTracks(IEnumerable<string> paths)
    {
        var added = 0;
        foreach (var path in paths.Take(LayeredAudioOptions.MaximumTracks))
        {
            if (options.Tracks.Count >= LayeredAudioOptions.MaximumTracks) break;
            if (LocalAudioInput.LocalPath(path) is not { } local) continue;
            options.Tracks.Add(new() { Path = local, Volume = 70, Enabled = true }); added++;
        }
        if (added > 0) { Refresh(); Structural(); }
        return added;
    }

    private void Refresh()
    {
        tracks.Children.Clear(); add.IsEnabled = options.Tracks.Count < LayeredAudioOptions.MaximumTracks;
        if (options.Tracks.Count == 0)
        { tracks.Children.Add(Help(options.Enabled ? "LayeredAudioEmptyEnabled" : "LayeredAudioEmpty")); return; }
        foreach (var track in options.Tracks)
        {
            var name = System.IO.Path.GetFileName(track.Path);
            var body = Gain("LayeredTrackVolume_" + track.Id.ToString("N"), "Volume", track.Volume, value => track.Volume = value);
            body.IsEnabled = track.Enabled; body.Opacity = track.Enabled ? 1 : .45;
            var enabled = new CheckBox { Name = "LayeredTrackEnabled_" + track.Id.ToString("N"), IsChecked = track.Enabled,
                Content = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 230 } };
            ToolTip.SetTip(enabled, track.Path);
            enabled.IsCheckedChanged += (_, _) => { track.Enabled = enabled.IsChecked == true;
                body.IsEnabled = track.Enabled; body.Opacity = track.Enabled ? 1 : .45; Structural(); };
            var remove = new Button { Name = "LayeredRemoveTrack_" + track.Id.ToString("N"), Content = "×", Padding = new(9, 3) };
            remove.Classes.Add("pill"); ToolTip.SetTip(remove, Text["Remove"]);
            AutomationProperties.SetName(remove, Text["Remove"] + " " + name);
            remove.Click += (_, _) => { options.Tracks.Remove(track); Refresh(); Structural(); };
            var header = new DockPanel(); var removeHelp = OptionHelp.Wrap(remove, "Remove", Text); DockPanel.SetDock(removeHelp, Dock.Right); header.Children.Add(removeHelp); header.Children.Add(OptionHelp.Wrap(enabled, "LayeredTrackEnabled", Text));
            var content = new StackPanel { Spacing = 6 }; content.Children.Add(header); content.Children.Add(body);
            tracks.Children.Add(new Border { Background = Brush.Parse("#1A1A2E"), BorderBrush = Brush.Parse("#3D3D60"),
                BorderThickness = new(1), CornerRadius = new(10), Padding = new(12, 10), Child = content });
        }
    }

    private Control Gain(string name, string label, int initial, Action<int> set)
    {
        var grid = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 10 };
        var value = new TextBlock { Text = initial + "%", Width = 42, TextAlignment = TextAlignment.Right,
            Foreground = Brush.Parse("#FF9BD5"), VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider { Name = name, Minimum = 0, Maximum = 100, Value = initial, MinWidth = 100,
            TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(slider, Text[label]);
        slider.ValueChanged += (_, _) => { var next = (int)Math.Round(slider.Value); set(next); value.Text = next + "%"; UpdateGain(); };
        grid.Children.Add(new TextBlock { Text = Text[label], Foreground = Brush.Parse("#D7C9F0"), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(slider, 1); grid.Children.Add(slider); Grid.SetColumn(value, 2); grid.Children.Add(value); return OptionHelp.Wrap(grid, label, Text);
    }
}
