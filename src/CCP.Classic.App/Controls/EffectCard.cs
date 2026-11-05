using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public sealed class EffectCard : Border
{
    private readonly EffectDefinition definition;
    private readonly EffectSettings settings;
    private readonly LocalizationService text;
    private readonly Action changed;

    public int PrimaryControlCount { get; private set; }
    public event Action<string>? ActionRequested;
    public event Action<string>? ValueChanged;

    public EffectCard(EffectDefinition definition, EffectSettings settings,
        LocalizationService text, Action changed, Control? customization = null)
    {
        this.definition = definition;
        this.settings = settings;
        this.text = text;
        this.changed = changed;
        Name = "Effect_" + definition.Id;
        Classes.Add("card");
        Classes.Add("effect-editor-card");
        Padding = new Thickness(17, 15, 17, 13);
        var body = new StackPanel { Spacing = 0 };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("12,28,*,76"), MinHeight = 28 };
        var lamp = new Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center, Fill = LampBrush() };
        header.Children.Add(lamp);
        var icon = new Path { Data = Geometry.Parse(definition.Icon), Width = 19, Height = 19,
            Stretch = Stretch.Uniform, Stroke = Brush("#83D9E9"), StrokeThickness = 1.6 };
        Grid.SetColumn(icon, 1);
        header.Children.Add(icon);
        var title = new TextBlock { Text = text[definition.Title], FontSize = 18,
            FontWeight = FontWeight.SemiBold, Foreground = Brush("#FF9BD5"), VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 9, 0) };
        ToolTip.SetTip(title, text[definition.Title]);
        Grid.SetColumn(title, 2);
        header.Children.Add(title);
        var enabled = Switch(settings.Enabled, text[definition.Title], value =>
        {
            settings.Enabled = value;
            lamp.Fill = LampBrush();
            changed();
        });
        enabled.Name = "Enable_" + definition.Id;
        var enableHelp = OptionHelp.Wrap(enabled, definition.Title, text);
        Grid.SetColumn(enableHelp, 3);
        header.Children.Add(enableHelp);
        header.Margin = new Thickness(0, 0, 0, 10);
        body.Children.Add(header);
        var promoted = definition.More.Where(knob => !(definition.Id == "video" && knob.Key == "maximum")
                && !(definition.Id == "flash" && knob.Key == "fade"))
            .Take((definition.More.Count + 1) / 2).ToArray();
        var remaining = definition.More.Except(promoted).ToArray();
        PrimaryControlCount = definition.Primary.Count + promoted.Length;
        var primary = definition.Primary.Concat(promoted).Select(CreateKnob).ToArray();
        body.Children.Add(OptionLayout.Group(primary, text["OptionsGeneral"]));
        if (customization is OptionEditor editor)
        {
            if (editor.TakeVisibleOptions() is { } visible) body.Children.Add(visible);
            if (definition.Id == "flash")
            {
                foreach (var knob in remaining) editor.Children.Add(CreateKnob(knob));
                remaining = [];
            }
            editor.PrepareLayout();
        }
        else if (customization is StackPanel composite && composite.Children.Count > 0)
        {
            // Composite editors retain their own callbacks and lifetime when promoted.
            var first = composite.Children[0];
            composite.Children.RemoveAt(0);
            if (first is OptionEditor nested) nested.PrepareLayout();
            body.Children.Add(first);
            composite.Spacing = 0;
        }
        var hasCustomization = customization is StackPanel options ? options.Children.Count > 0 : customization is not null;
        if (remaining.Length > 0 || hasCustomization)
        {
            var advanced = new StackPanel { Spacing = 0, IsVisible = false,
                Margin = new Thickness(0, 4, 0, 0) };
            if (remaining.Length > 0)
                advanced.Children.Add(OptionLayout.Group(remaining.Select(CreateKnob), text["OptionsUtility"], 2));
            if (hasCustomization) advanced.Children.Add(customization!);
            var more = new Button { Content = text["More"], HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 8) };
            more.Classes.Add("more");
            more.Name = "More_" + definition.Id;
            more.Click += (_, _) =>
            {
                advanced.IsVisible = !advanced.IsVisible;
                more.Content = text[advanced.IsVisible ? "Less" : "More"];
            };
            body.Children.Add(more);
            body.Children.Add(advanced);
        }
        if (definition.WindowsOnly)
        {
            var note = Label(text["WallpaperRestore"]);
            note.TextWrapping = TextWrapping.Wrap;
            note.Classes.Add("muted");
            body.Children.Add(note);
        }
        Child = body;
    }

    private Control CreateKnob(KnobDefinition knob)
    {
        var control = OptionHelp.Wrap(CreateKnobControl(knob), definition.Id == "wall" && knob.Label == "Mode" ? "WallpaperMode" : knob.Label, text);
        if (knob.Kind is KnobKind.Toggle or KnobKind.Range) control.Classes.Add("option-compact");
        return control;
    }

    private Control CreateKnobControl(KnobDefinition knob)
    {
        if (!settings.Values.ContainsKey(knob.Key)) settings.Values[knob.Key] = knob.Default;
        var value = settings.Values[knob.Key];
        var label = Label(text[knob.Label]);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        switch (knob.Kind)
        {
            case KnobKind.Toggle:
                row.Children.Add(label);
                var toggle = Switch(value != 0, text[knob.Label], state => Save(knob.Key, state ? 1 : 0));
                toggle.Name = "Knob_" + definition.Id + "_" + knob.Key;
                if (knob.Key.Contains("strict", StringComparison.OrdinalIgnoreCase) && definition.Id is not ("video" or "lock"))
                {
                    toggle.IsEnabled = false;
                    ToolTip.SetTip(toggle, text["Unavailable"]);
                }
                Grid.SetColumn(toggle, 1);
                row.Children.Add(toggle);
                row.MinHeight = 30;
                return row;
            case KnobKind.Range:
                row.Children.Add(label);
                var output = Label(Format(value, knob.Unit));
                output.Foreground = Brush("#91E0EE"); output.FontSize = 16; output.FontWeight = FontWeight.SemiBold;
                Grid.SetColumn(output, 1);
                row.Children.Add(output);
                var slider = new Slider { Minimum = knob.Min, Maximum = knob.Max,
                    Value = value, SmallChange = 1, LargeChange = 5, TickFrequency = 1,
                    IsSnapToTickEnabled = true, Name = "Knob_" + definition.Id + "_" + knob.Key };
                slider.Classes.Add("knob");
                AutomationProperties.SetName(slider, text[knob.Label]);
                slider.PropertyChanged += (_, e) =>
                {
                    if (e.Property != RangeBase.ValueProperty) return;
                    Save(knob.Key, Math.Round(slider.Value));
                    output.Text = Format(slider.Value, knob.Unit);
                };
                var range = new StackPanel { Spacing = 4 };
                range.Children.Add(row);
                range.Children.Add(slider);
                return range;
            case KnobKind.Choice:
                return Choice(knob, label, value);
            case KnobKind.Action:
                var supported = definition.Id == "spiral" && knob.Key == "loom";
                var action = new Button { Content = text[knob.Label], IsEnabled = supported,
                    Name = "Action_" + definition.Id + "_" + knob.Key,
                    HorizontalAlignment = HorizontalAlignment.Right, FontSize = 14 };
                action.Classes.Add("pill");
                action.Click += (_, _) => ActionRequested?.Invoke(knob.Key);
                if (!supported) ToolTip.SetTip(action, text["Unavailable"]);
                return action;
            default:
                throw new ArgumentOutOfRangeException(nameof(knob));
        }
    }

    private Control Choice(KnobDefinition knob, TextBlock label, double value)
    {
        var group = new StackPanel { Spacing = 6 };
        group.Children.Add(label);
        var choices = new WrapPanel { Orientation = Orientation.Horizontal };
        var buttons = new List<ToggleButton>();
        var names = knob.Choices ?? [];
        for (var index = 0; index < names.Length; index++)
        {
            var pick = index;
            var button = new ToggleButton { IsChecked = index == (int)value, FontSize = 13.5,
                Content = text[names[index]], MinHeight = 32, Padding = new Thickness(10, 4), Margin = new(0, 0, 6, 4) };
            button.Classes.Add("pill");
            if (knob.Label == "Spiral")
            {
                button.Content = SpiralIcon(index);
                button.Width = 36;
                button.Height = 36;
                button.Padding = new Thickness(4);
                AutomationProperties.SetName(button, text["Spiral"] + " " + (index + 1));
            }
            else AutomationProperties.SetName(button, text[knob.Label] + ": " + text[names[index]]);
            button.Click += (_, _) =>
            {
                foreach (var other in buttons) other.IsChecked = ReferenceEquals(other, button);
                Save(knob.Key, pick);
            };
            buttons.Add(button);
            choices.Children.Add(button);
        }
        group.Children.Add(choices);
        return group;
    }

    private static Path SpiralIcon(int variant)
    {
        var data = new StreamGeometry();
        using (var drawing = data.Open())
        {
            drawing.BeginFigure(new Point(12, 12), false);
            for (var i = 1; i <= 120; i++)
            {
                var angle = i * Math.PI / (22 - variant * 3);
                var radius = i * .08;
                drawing.LineTo(new Point(12 + Math.Cos(angle) * radius, 12 + Math.Sin(angle) * radius));
            }
            drawing.EndFigure(false);
        }
        return new Path { Data = data, Width = 21, Height = 21, Stretch = Stretch.Uniform,
            Stroke = Brush("#C9C9DE"), StrokeThickness = 1.2 };
    }

    private static ToggleButton Switch(bool enabled, string name, Action<bool> update)
    {
        var toggle = new ToggleButton { IsChecked = enabled, Width = 42, Height = 24,
            VerticalAlignment = VerticalAlignment.Center };
        toggle.Classes.Add("switch");
        AutomationProperties.SetName(toggle, name);
        toggle.Click += (_, _) => update(toggle.IsChecked == true);
        return toggle;
    }

    private void Save(string key, double value) { settings.Values[key] = value; ValueChanged?.Invoke(key); changed(); }
    private IBrush LampBrush() => Brush(settings.Enabled ? "#FF69B4" : "#2E2E4A");
    private string Format(double value, string unit)
    {
        var suffix = unit.Trim() switch
        {
            "min" => " " + text["MinutesUnit"],
            "ms" => " " + text["MillisecondsUnit"],
            _ => unit
        };
        return Math.Round(value).ToString(CultureInfo.CurrentCulture) + suffix;
    }
    private static SolidColorBrush Brush(string colour) => new(Color.Parse(colour));
    private static TextBlock Label(string value) => new() { Text = value, FontSize = 14, FontWeight = FontWeight.Medium,
        Foreground = Brush("#D7C9F0"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
}
