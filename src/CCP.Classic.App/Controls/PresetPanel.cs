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
        Content = body;
        var builtIns = new UniformGrid { Columns = 3 };
        foreach (var preset in PresetStore.BuiltIns)
        {
            var card = new StackPanel { Spacing = 10 };
            card.Children.Add(Title(text[preset.Name]));
            card.Children.Add(new TextBlock { Text = text[preset.Name + "Summary"], FontSize = 12,
                TextWrapping = TextWrapping.Wrap, MinHeight = 90 });
            card.Children.Add(Button("Load", "PresetLoad_" + preset.Id, () => Load(preset)));
            builtIns.Children.Add(Card(card));
        }
        body.Children.Add(builtIns);
        body.Children.Add(Duration());
        body.Children.Add(Title(text["NewPreset"]));
        name.PlaceholderText = text["MyPreset"];
        AutomationProperties.SetName(name, text["NewPreset"]);
        var create = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        create.Children.Add(name);
        var save = Button("Save", "PresetSave", Save); Grid.SetColumn(save, 1); create.Children.Add(save);
        body.Children.Add(create); body.Children.Add(custom);
        status.Classes.Add("muted"); body.Children.Add(status);
        RefreshCustom();
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
            durations.Add(button); panel.Children.Add(button);
        }
        return panel;
    }

    private void UpdateDurations()
    {
        foreach (var button in durations) button.IsChecked = button.Tag is int minutes && settings.RunMinutes == minutes;
    }

    private void Load(PresetDefinition preset)
    {
        try
        {
            PresetStore.Apply(preset, settings); UpdateDurations(); changed();
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
            name.Text = ""; RefreshCustom(); changed(); Report("Saved");
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

    private Button Button(string key, string controlName, Action action)
    {
        var button = new Button { Content = text[key], Name = controlName };
        button.Classes.Add("pill"); button.Click += (_, _) => action(); return button;
    }

    private static Border Card(Control child)
    {
        var card = new Border { Child = child, Padding = new Thickness(12), Margin = new Thickness(4),
            VerticalAlignment = VerticalAlignment.Top };
        card.Classes.Add("card"); return card;
    }

    private static TextBlock Title(string value)
    {
        var title = new TextBlock { Text = value, FontSize = 14, TextWrapping = TextWrapping.Wrap };
        title.Classes.Add("chrome-title"); return title;
    }
}
