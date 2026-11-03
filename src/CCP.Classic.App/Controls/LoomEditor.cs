using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;
using SkiaSharp;

namespace CCP.Classic.App.Controls;

public sealed class LoomEditor : UserControl, IDisposable
{
    private readonly LocalizationService text;
    private readonly LoomStore store;
    private readonly LoomPreview preview;
    private readonly StackPanel parameters = new() { Spacing = 10 }, rack = new() { Spacing = 8 };
    private readonly TextBox name = new() { Name = "LoomName", MaxLength = 80 };
    private readonly TextBlock status = new() { Name = "LoomStatus", TextWrapping = TextWrapping.Wrap };
    private readonly List<Bitmap> thumbnails = [];
    private readonly Dictionary<string, bool> expanded = [];
    private readonly Button save, create;
    private LoomDefinition definition = new();
    private string? selectedId;
    private int generation;
    private bool disposed;
    public bool IsBusy { get; private set; }
    public string? StatusKey { get; private set; }
    public string? SelectedId => selectedId;
    public LoomDefinition Definition => definition.Normalize();
    public event Action<SavedLoom>? SavedSelection;
    public event Action<string>? Deleted;
    public event Action<string>? StatusChanged;

    public LoomEditor(ClassicSettings settings, LocalizationService text, LoomStore store)
    {
        this.text = text; this.store = store;
        var body = new StackPanel { Spacing = 12, Margin = new(12) };
        Content = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        preview = new(settings) { Height = 260, Name = "LoomPreview" }; body.Children.Add(preview);
        body.Children.Add(parameters); BuildParameters();
        name.PlaceholderText = text["LoomName"]; AutomationProperties.SetName(name, text["LoomName"]);
        var actions = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 8 };
        actions.Children.Add(name);
        save = Button("Save", "LoomSave", () => _ = SaveAsync()); Grid.SetColumn(save, 1); actions.Children.Add(save);
        create = Button("LoomNew", "LoomNew", () => Load(null)); Grid.SetColumn(create, 2); actions.Children.Add(create);
        body.Children.Add(actions); body.Children.Add(new TextBlock { Text = text["LoomRack"], FontWeight = FontWeight.SemiBold });
        body.Children.Add(rack); status.Classes.Add("muted"); body.Children.Add(status);
        _ = ReloadAsync();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { Dispose(); base.OnDetachedFromVisualTree(e); }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; generation++; IsBusy = false; preview.Active = false; rack.Children.Clear();
        foreach (var image in thumbnails) image.Dispose(); thumbnails.Clear();
    }
    private void Change(Func<LoomDefinition, LoomDefinition> change, bool rebuild = false)
    {
        definition = change(definition).Normalize(); preview.Definition = definition;
        if (rebuild) BuildParameters();
    }
    private LoomLayer Layer(bool second) => second ? definition.Layer2 : definition.Layer;
    private void ChangeLayer(bool second, Func<LoomLayer, LoomLayer> change, bool rebuild = false) =>
        Change(q => second ? q with { Layer2 = change(q.Layer2) } : q with { Layer = change(q.Layer) }, rebuild);
    private void BuildParameters()
    {
        parameters.Children.Clear(); parameters.Children.Add(Primary(false));
        var more = new StackPanel { Spacing = 10 };
        more.Children.Add(LayerOptions(false));
        more.Children.Add(Choice("LoomFormat", "LoomFormat", ["LoomSquare", "LoomWide", "LoomTall"], Array.IndexOf(new[] { "square", "wide", "tall" }, definition.Format),
            index => Change(q => q with { Format = new[] { "square", "wide", "tall" }[index] })));
        more.Children.Add(Number("Speed", "LoomSpeed", definition.Speed, 1, 5, 1, value => Change(q => q with { Speed = (int)value })));
        var background = new StackPanel { Spacing = 8 };
        background.Children.Add(new LoomColourPicker("LoomBackgroundColour", text["LoomBackground"], definition.Bg.Color, text, value => Change(q => q with { Bg = q.Bg with { Color = value } })));
        background.Children.Add(Check("LoomRadial", "LoomRadial", definition.Bg.Kind == "radial", value => Change(q => q with { Bg = q.Bg with { Kind = value ? "radial" : "solid" } })));
        background.Children.Add(new LoomColourPicker("LoomOuterColour", text["LoomOuterColour"], definition.Bg.Outer, text, value => Change(q => q with { Bg = q.Bg with { Outer = value } })));
        more.Children.Add(Group("LoomBackground", background));
        var second = new StackPanel { Spacing = 8, IsVisible = definition.Layer2.Enabled };
        second.Children.Add(Primary(true)); second.Children.Add(LayerOptions(true));
        more.Children.Add(Check("LoomSecondLayer", "LoomSecondEnabled", definition.Layer2.Enabled,
            value => { ChangeLayer(true, layer => layer with { Enabled = value }); second.IsVisible = value; }));
        more.Children.Add(second); more.Children.Add(Effects()); more.Children.Add(Centerpiece());
        parameters.Children.Add(Group("More", more));
    }
    private Control Primary(bool second)
    {
        var prefix = second ? "LoomSecond" : "Loom"; var layer = Layer(second);
        var panel = new StackPanel { Name = prefix + "Primary", Spacing = 8 };
        panel.Children.Add(Number("LoomArms", prefix + "Arms", layer.Arms, 1, 12, 1, value => ChangeLayer(second, l => l with { Arms = (int)value })));
        panel.Children.Add(Number("LoomTurns", prefix + "Turns", layer.Turns, .5, 6, .25, value => ChangeLayer(second, l => l with { Turns = value })));
        panel.Children.Add(Percentage("LoomBody", prefix + "Body", layer.Duty, .2, .8, .05, value => ChangeLayer(second, l => l with { Duty = value })));
        return panel;
    }
    private Control LayerOptions(bool second)
    {
        var prefix = second ? "LoomSecond" : "Loom"; var layer = Layer(second);
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Choice("LoomStyle", prefix + "Style", ["LoomLog", "LoomArch", "LoomRibbon", "LoomGolden", "LoomTunnel", "LoomPetal"],
            LoomDefinition.Styles.ToList().IndexOf(layer.Style), index => ChangeLayer(second, l => l with { Style = LoomDefinition.Styles[index] })));
        panel.Children.Add(Choice("LoomDirection", prefix + "Direction", ["LoomInward", "LoomOutward"], layer.Direction == -1 ? 1 : 0,
            index => ChangeLayer(second, l => l with { Direction = index == 1 ? -1 : 1 })));
        panel.Children.Add(Number("LoomLayerSpeed", prefix + "LayerSpeed", layer.SpeedMul, 1, 3, 1, value => ChangeLayer(second, l => l with { SpeedMul = (int)value })));
        panel.Children.Add(Check("LoomGradient", prefix + "Gradient", layer.BandMode == "gradient",
            value => ChangeLayer(second, l => l with { BandMode = value ? "gradient" : "hard" })));
        for (var index = 0; index < layer.Colors.Length; index++)
        {
            var at = index; var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
            row.Children.Add(new LoomColourPicker(prefix + "Colour" + index, text["LoomColour"] + " " + (index + 1), layer.Colors[index], text, value =>
            {
                var colors = Layer(second).Colors.ToArray(); if (at >= colors.Length) return; colors[at] = value;
                ChangeLayer(second, l => l with { Colors = colors }, colors.Distinct(StringComparer.OrdinalIgnoreCase).Count() != colors.Length);
            }));
            var remove = Button("Delete", prefix + "RemoveColour" + index, () => ChangeLayer(second,
                l => l with { Colors = l.Colors.Where((_, i) => i != at).ToArray() }, true));
            remove.IsEnabled = layer.Colors.Length > 1; Grid.SetColumn(remove, 1); row.Children.Add(remove); panel.Children.Add(row);
        }
        var add = Button("Add", prefix + "AddColour", () => ChangeLayer(second,
            l => l with { Colors = [.. l.Colors, LoomColourPicker.Palette.First(color => !l.Colors.Contains(color, StringComparer.OrdinalIgnoreCase))] }, true));
        add.IsEnabled = layer.Colors.Length < 6; panel.Children.Add(add);
        return Group(second ? "LoomSecondLayer" : "LoomThreads", panel);
    }
    private Control Effects()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Percentage("LoomGlow", "LoomGlow", definition.Glow, 0, 1, .05, value => Change(q => q with { Glow = value })));
        panel.Children.Add(Percentage("LoomPulse", "LoomPulse", definition.Pulse.Amp, 0, .25, .01, value => Change(q => q with { Pulse = q.Pulse with { Amp = value } })));
        panel.Children.Add(Number("LoomCycles", "LoomPulseCycles", definition.Pulse.Cycles, 1, 4, 1, value => Change(q => q with { Pulse = q.Pulse with { Cycles = (int)value } })));
        panel.Children.Add(Percentage("LoomWobble", "LoomWobble", definition.Wobble.Amp, 0, .35, .01, value => Change(q => q with { Wobble = q.Wobble with { Amp = value } })));
        panel.Children.Add(Number("LoomRipples", "LoomRipples", definition.Wobble.Freq, 1, 6, 1, value => Change(q => q with { Wobble = q.Wobble with { Freq = (int)value } })));
        panel.Children.Add(Number("LoomCycles", "LoomWobbleCycles", definition.Wobble.Cycles, 1, 3, 1, value => Change(q => q with { Wobble = q.Wobble with { Cycles = (int)value } })));
        panel.Children.Add(Number("LoomHue", "LoomHue", definition.HueCycles, 0, 2, 1, value => Change(q => q with { HueCycles = (int)value })));
        return Group("LoomEffects", panel);
    }
    private Control Centerpiece()
    {
        var panel = new StackPanel { Spacing = 8 }; var cp = definition.Centerpiece;
        panel.Children.Add(Choice("LoomCenterpiece", "LoomCenterpiece", ["LoomNone", "LoomDot", "LoomStar", "LoomCross", "LoomX", "LoomWord"],
            Array.IndexOf(new[] { "none", "dot", "star", "cross", "x", "mantra" }, cp.Kind),
            index => Change(q => q with { Centerpiece = q.Centerpiece with { Kind = new[] { "none", "dot", "star", "cross", "x", "mantra" }[index] } })));
        panel.Children.Add(new LoomColourPicker("LoomCenterColour", text["LoomColour"], cp.Color, text, value => Change(q => q with { Centerpiece = q.Centerpiece with { Color = value } })));
        panel.Children.Add(Percentage("Size", "LoomCenterSize", cp.SizeFrac, .08, .4, .01, value => Change(q => q with { Centerpiece = q.Centerpiece with { SizeFrac = value } })));
        var word = new TextBox { Name = "LoomWord", Text = cp.Text, MaxLength = 12, PlaceholderText = text["LoomWord"] };
        AutomationProperties.SetName(word, text["LoomWord"]);
        word.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Change(q => q with { Centerpiece = q.Centerpiece with { Text = word.Text ?? "" } }); }; panel.Children.Add(word);
        panel.Children.Add(Number("LoomFlash", "LoomFlash", cp.FlashCycles, 0, 4, 1, value => Change(q => q with { Centerpiece = q.Centerpiece with { FlashCycles = (int)value } })));
        return Group("LoomCenterpiece", panel);
    }
    private void Load(SavedLoom? saved)
    {
        selectedId = saved?.Id; name.Text = saved?.Name ?? ""; definition = saved?.Definition.Normalize() ?? new();
        preview.Definition = definition; BuildParameters(); if (saved is not null) SavedSelection?.Invoke(saved);
    }
    private async Task SaveAsync()
    {
        if (IsBusy || disposed) return;
        var title = name.Text ?? ""; var q = definition; var id = selectedId; Busy(true);
        try
        {
            var result = await Task.Run(() =>
            {
                var full = id is null && store.LoadAll().Count >= LoomStore.MaximumSpirals;
                return (full, saved: full ? null : store.Save(title, q, id));
            });
            if (disposed) return;
            if (result.full) { Report("LoomFull"); return; }
            var saved = result.saved!;
            selectedId = saved.Id; name.Text = saved.Name; SavedSelection?.Invoke(saved); Report("LoomSaved"); await ReloadAsync();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { if (!disposed) Report("LoomSaveFailed"); }
        finally { if (!disposed) Busy(false); }
    }
    private async Task DeleteAsync(SavedLoom saved)
    {
        if (IsBusy || disposed) return; Busy(true);
        try
        {
            await Task.Run(() => store.Delete(saved.Id)); if (disposed) return;
            if (selectedId == saved.Id) selectedId = null; Deleted?.Invoke(saved.Id); Report("LoomSaved"); await ReloadAsync();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { if (!disposed) Report("LoomSaveFailed"); }
        finally { if (!disposed) Busy(false); }
    }
    private async Task ReloadAsync()
    {
        var version = ++generation; Busy(true);
        try
        {
            var result = await Task.Run(() =>
            {
                var entries = store.LoadAll(out var recovered);
                var images = new List<(SavedLoom Saved, Bitmap Image)>();
                try
                {
                    foreach (var entry in entries)
                    {
                        using var rendered = LoomRenderer.Render(entry.Definition, 96, 0);
                        using var image = SKImage.FromBitmap(rendered); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                        using var stream = new MemoryStream(data.ToArray()); images.Add((entry, new Bitmap(stream)));
                    }
                    return (images, recovered);
                }
                catch { foreach (var item in images) item.Image.Dispose(); throw; }
            });
            if (disposed || version != generation) { foreach (var item in result.images) item.Image.Dispose(); return; }
            rack.Children.Clear(); foreach (var old in thumbnails) old.Dispose(); thumbnails.Clear();
            foreach (var item in result.images)
            {
                thumbnails.Add(item.Image);
                var row = new Grid { ColumnDefinitions = new("64,*,Auto,Auto"), ColumnSpacing = 8, MinHeight = 64 };
                row.Children.Add(new Image { Source = item.Image, Width = 64, Height = 64, Stretch = Stretch.Uniform });
                var label = new TextBlock { Text = item.Saved.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(label, 1); row.Children.Add(label);
                var load = Button("Load", "LoomLoad_" + item.Saved.Id, () => Load(item.Saved)); Grid.SetColumn(load, 2); row.Children.Add(load);
                var delete = Button("Delete", "LoomDelete_" + item.Saved.Id, () => _ = DeleteAsync(item.Saved)); Grid.SetColumn(delete, 3); row.Children.Add(delete); rack.Children.Add(row);
            }
            if (result.recovered) Report("LoomRecovered");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException) { if (!disposed) Report("LoomSaveFailed"); }
        finally { if (!disposed && version == generation) Busy(false); }
    }
    private void Busy(bool value) { IsBusy = value; save.IsEnabled = create.IsEnabled = name.IsEnabled = parameters.IsEnabled = rack.IsEnabled = !value; }
    private void Report(string key) { StatusKey = key; status.Text = text[key]; StatusChanged?.Invoke(key); }
    private Button Button(string key, string id, Action action)
    {
        var button = new Button { Content = text[key], Name = id }; button.Classes.Add("pill"); button.Click += (_, _) => action(); return button;
    }
    private Control Percentage(string key, string id, double value, double min, double max, double step, Action<double> changed) =>
        Number(key, id, value * 100, min * 100, max * 100, step * 100, next => changed(next / 100), " (%)");
    private Control Number(string key, string id, double value, double min, double max, double step, Action<double> changed, string unit = "")
    {
        var number = new NumericUpDown { Name = id, Value = (decimal)value, Minimum = (decimal)min, Maximum = (decimal)max, Increment = (decimal)step, Width = 110, FormatString = "0.##" };
        AutomationProperties.SetName(number, text[key]); number.ValueChanged += (_, _) => { if (number.Value is { } next) changed((double)next); };
        return Row(text[key] + unit, number);
    }
    private Control Choice(string key, string id, string[] choices, int index, Action<int> changed)
    {
        var combo = new ComboBox { Name = id, ItemsSource = choices.Select(choice => text[choice]).ToArray(), SelectedIndex = Math.Max(0, index), MinWidth = 150 };
        AutomationProperties.SetName(combo, text[key]); combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex is >= 0 && combo.SelectedIndex < choices.Length) changed(combo.SelectedIndex); };
        return Row(text[key], combo);
    }
    private CheckBox Check(string key, string id, bool value, Action<bool> changed)
    {
        var check = new CheckBox { Name = id, Content = text[key], IsChecked = value }; check.IsCheckedChanged += (_, _) => changed(check.IsChecked == true); return check;
    }
    private Expander Group(string key, Control content)
    {
        var group = new Expander { Name = "Group_" + key, Header = text[key], Content = content, IsExpanded = expanded.GetValueOrDefault(key) };
        group.PropertyChanged += (_, e) => { if (e.Property == Expander.IsExpandedProperty) expanded[key] = group.IsExpanded; }; return group;
    }
    private static Control Row(string label, Control input)
    {
        var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 10 };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }); Grid.SetColumn(input, 1); row.Children.Add(input); return row;
    }
}
