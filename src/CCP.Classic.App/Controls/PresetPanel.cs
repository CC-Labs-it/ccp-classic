using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public sealed class PresetPanel : UserControl
{
    private readonly ClassicSettings settings;
    private readonly LocalizationService text;
    private readonly PresetStore store;
    private readonly Action changed;
    private readonly PanelArtworkClock artClock;
    private readonly Dictionary<string, Border> presetCards = [];
    private readonly StackPanel body = new() { Spacing = 14 };
    private readonly StackPanel custom = new() { Spacing = 8 };
    private readonly List<ToggleButton> durations = [];
    private readonly TextBox name = new() { Name = "PresetName", MaxLength = 80 };
    private readonly TextBlock status = new() { Name = "PresetStatus", TextWrapping = TextWrapping.Wrap };
    private IReadOnlyList<PresetDefinition> saved = [];
    public string? StatusKey { get; private set; }
    public event Action<string>? StatusChanged;

    public PresetPanel(ClassicSettings settings, LocalizationService text, PresetStore store, Action changed)
    {
        this.settings = settings; this.text = text; this.store = store; this.changed = changed;
        artClock = new(this, settings);
        Content = body;
        var builtIns = new UniformGrid { Columns = 3 };
        foreach (var preset in PresetStore.BuiltIns)
        {
            var card = new StackPanel { Spacing = 10 };
            var (asset, accent) = preset.Name switch { "Gentle" => ("hero_drift.png", "#69D8EA"),
                "Deep" => ("hero_deep.png", "#CC8DFF"), _ => ("hero_default.png", "#FF69B4") };
            card.Children.Add(artClock.Add(new("Presets/" + asset, accent) { Height = 138, Name = "PresetArt_" + preset.Id }));
            card.Children.Add(new TextBlock { Text = text[preset.Name], FontSize = 21, FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse(accent)), Margin = new(2, 0) });
            card.Children.Add(new TextBlock { Text = text[preset.Name + "Summary"], FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#BDB9D5")), TextWrapping = TextWrapping.Wrap, MinHeight = 58, Margin = new(2, 0) });
            var load = Button("Load", "PresetLoad_" + preset.Id, () => Load(preset));
            load.HorizontalAlignment = HorizontalAlignment.Stretch; load.HorizontalContentAlignment = HorizontalAlignment.Center;
            card.Children.Add(load);
            var surface = Card(card); surface.Name = "PresetCard_" + preset.Id; surface.Classes.Add("preset-art-card");
            presetCards[preset.Name] = surface; VisualPolish.AttachHover(surface, settings, 1.015);
            builtIns.Children.Add(surface);
        }
        body.Children.Add(builtIns);
        body.Children.Add(Duration());
        body.Children.Add(TimedChanges());
        body.Children.Add(Title(text["NewPreset"]));
        name.PlaceholderText = text["MyPreset"];
        AutomationProperties.SetName(name, text["NewPreset"]);
        var create = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        create.Children.Add(OptionHelp.Wrap(name, "PresetName", text));
        var save = Button("Save", "PresetSave", Save); Grid.SetColumn(save, 1); create.Children.Add(save);
        body.Children.Add(create); body.Children.Add(custom);
        status.Classes.Add("muted"); body.Children.Add(status);
        RefreshCustom(); UpdateSelection();
    }

    private Control Duration()
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = text["RunFor"], Margin = new(4, 7, 12, 0) });
        foreach (var (minutes, key) in new[] { (15, "Minutes15"), (30, "Minutes30"), (60, "Minutes60"), (0, "UntilStopped") })
        {
            var button = new ToggleButton { Content = text[key], Name = "PresetDuration_" + minutes,
                IsChecked = settings.RunMinutes == minutes, Margin = new(0, 0, 6, 6), Tag = minutes };
            button.Classes.Add("pill");
            button.Click += (_, _) =>
            {
                settings.RunMinutes = minutes; UpdateDurations(); changed();
            };
            durations.Add(button); panel.Children.Add(OptionHelp.Wrap(button, "RunFor", text));
        }
        return panel;
    }

    private void UpdateDurations()
    {
        foreach (var button in durations) button.IsChecked = button.Tag is int minutes && settings.RunMinutes == minutes;
    }

    private Control TimedChanges()
    {
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock { Text = text["TimedNextSession"], FontSize = 11, TextWrapping = TextWrapping.Wrap });
        var minutes = new NumericUpDown { Minimum = 0, Maximum = 1440, Value = 5, Width = 100, Name = "TimedMinute" };
        var effects = EffectCatalog.All.Where(effect => !effect.WindowsOnly || OperatingSystem.IsWindows()).ToArray();
        var selected = new ComboBox { ItemsSource = effects, SelectedIndex = 0, Width = 180, Name = "TimedEffect",
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<EffectDefinition>((effect, _) =>
                new TextBlock { Text = effect is null ? "" : text[effect.Title] }) };
        var enabled = new CheckBox { IsChecked = true, Content = text["EnableEffect"], Name = "TimedEnabled" };
        var row = new WrapPanel();
        row.Children.Add(new TextBlock { Text = text["AtMinute"], VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 8, 0) });
        row.Children.Add(OptionHelp.Wrap(minutes, "AtMinute", text)); row.Children.Add(OptionHelp.Wrap(selected, "TimedEffect", text)); row.Children.Add(OptionHelp.Wrap(enabled, "EnableEffect", text));
        row.Children.Add(Button("Add", "TimedAdd", () =>
        {
            if (selected.SelectedItem is not EffectDefinition effect || settings.TimedChanges.Count >= 64) return;
            settings.TimedChanges.Add(new((double)(minutes.Value ?? 5) * 60, effect.Id, enabled.IsChecked == true));
            settings.TimedChanges = TimedEffectChange.Normalize(settings.TimedChanges); changed();
        }));
        content.Children.Add(row);
        foreach (var change in settings.TimedChanges)
        {
            var line = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
            var definition = EffectCatalog.All.First(effect => effect.Id == change.EffectId);
            line.Children.Add(new CheckBox { Content = (change.AtSeconds / 60).ToString("0.#") + " " + text["MinutesUnit"] + " · " + text[definition.Title],
                IsChecked = change.Enabled, IsEnabled = false });
            var delete = Button("Delete", "TimedDelete_" + content.Children.Count, () => { settings.TimedChanges.Remove(change); changed(); });
            Grid.SetColumn(delete, 1); line.Children.Add(delete); content.Children.Add(line);
        }
        return new Expander { Header = text["TimedChanges"], Content = content };
    }

    private void Load(PresetDefinition preset)
    {
        try
        {
            PresetStore.Apply(preset, settings); UpdateDurations(); UpdateSelection(); changed();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { Report("SaveFailed"); }
    }

    private void Save()
    {
        try
        {
            var preset = PresetStore.Capture(name.Text ?? "", settings);
            var existing = saved.FirstOrDefault(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) preset.Id = existing.Id;
            var result = store.Save(preset);
            settings.Preset = result.Name;
            name.Text = ""; RefreshCustom(); UpdateSelection(); changed(); Report("Saved");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { Report("SaveFailed"); }
    }

    private void Delete(PresetDefinition preset)
    {
        try { store.Delete(preset.Id); RefreshCustom(); Report("Saved"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { Report("SaveFailed"); }
    }

    private void RefreshCustom()
    {
        saved = store.LoadAll(out var recovered);
        custom.Children.Clear();
        foreach (var preset in saved)
        {
            var row = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 8 };
            row.Children.Add(new TextBlock { Text = preset.Name, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis });
            var load = Button("Load", "PresetLoad_" + preset.Id, () => Load(preset));
            Grid.SetColumn(load, 1); row.Children.Add(load);
            var delete = Button("Delete", "PresetDelete_" + preset.Id, () => Delete(preset));
            Grid.SetColumn(delete, 2); row.Children.Add(delete);
            custom.Children.Add(Card(row));
        }
        if (recovered) Report("SettingsRecovered");
    }

    private void Report(string key)
    {
        StatusKey = key; status.Text = text[key]; StatusChanged?.Invoke(key);
    }

    private void UpdateSelection()
    {
        foreach (var (preset, card) in presetCards)
        {
            var selected = string.Equals(preset, settings.Preset, StringComparison.Ordinal);
            card.Classes.Set("selected", selected);
            card.BorderBrush = new SolidColorBrush(Color.Parse(selected ? "#FF82CA" : "#443C61"));
            card.BorderThickness = new Thickness(selected ? 2 : 1);
        }
    }

    private Button Button(string key, string controlName, Action action)
    {
        var button = new Button { Content = text[key], Name = controlName };
        button.Classes.Add("pill"); button.Click += (_, _) => action(); return button;
    }

    private static Border Card(Control child)
    {
        var card = new Border { Child = child, Padding = new Thickness(12), Margin = new Thickness(4),
            VerticalAlignment = VerticalAlignment.Top };
        card.Classes.Add("card"); card.Classes.Add("art-card"); return card;
    }

    private static TextBlock Title(string value)
    {
        var title = new TextBlock { Text = value, FontSize = 14, TextWrapping = TextWrapping.Wrap };
        title.Classes.Add("chrome-title"); return title;
    }
}
