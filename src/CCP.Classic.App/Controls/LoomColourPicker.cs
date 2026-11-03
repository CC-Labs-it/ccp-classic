using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public sealed class LoomColourPicker : Expander
{
    internal static readonly string[] Palette = ["#ff69b4", "#e56cc0", "#8a5cff", "#00e5ff", "#39ff9d", "#ffd94d", "#ff6a2f", "#ffffff"];
    public LoomColourPicker(string controlName, string label, string initial, LocalizationService text, Action<string> changed)
    {
        Name = controlName;
        var color = Color.Parse(initial); var updating = false;
        var swatch = new Border { Width = 24, Height = 18, Background = new SolidColorBrush(color), CornerRadius = new(4) };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(swatch); header.Children.Add(new TextBlock { Text = label }); Header = header;
        AutomationProperties.SetName(this, label);
        var body = new StackPanel { Spacing = 6 }; Content = body;
        var values = new NumericUpDown[3];
        var keys = new[] { "LoomRed", "LoomGreen", "LoomBlue" };
        var initialValues = new[] { color.R, color.G, color.B };
        void Publish()
        {
            if (updating) return;
            color = Color.FromRgb((byte)Math.Clamp(values[0].Value ?? 0, 0, 255), (byte)Math.Clamp(values[1].Value ?? 0, 0, 255),
                (byte)Math.Clamp(values[2].Value ?? 0, 0, 255));
            swatch.Background = new SolidColorBrush(color);
            changed($"#{color.R:x2}{color.G:x2}{color.B:x2}");
        }
        for (var index = 0; index < 3; index++)
        {
            var row = new Grid { ColumnDefinitions = new("*,100"), ColumnSpacing = 8 };
            row.Children.Add(new TextBlock { Text = text[keys[index]], VerticalAlignment = VerticalAlignment.Center });
            var number = new NumericUpDown { Minimum = 0, Maximum = 255, Increment = 1, Value = initialValues[index], Name = controlName + "_" + keys[index] };
            AutomationProperties.SetName(number, text[keys[index]]); values[index] = number;
            number.ValueChanged += (_, _) => Publish(); Grid.SetColumn(number, 1); row.Children.Add(number); body.Children.Add(row);
        }
        var palette = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var value in Palette)
        {
            var button = new Button { Content = new Border { Width = 20, Height = 20, Background = new SolidColorBrush(Color.Parse(value)) }, Margin = new(2), Padding = new(3) };
            AutomationProperties.SetName(button, text["LoomColour"] + " " + value);
            button.Click += (_, _) =>
            {
                var chosen = Color.Parse(value); updating = true;
                values[0].Value = chosen.R; values[1].Value = chosen.G; values[2].Value = chosen.B;
                updating = false; Publish();
            };
            palette.Children.Add(button);
        }
        body.Children.Add(palette);
    }
}
