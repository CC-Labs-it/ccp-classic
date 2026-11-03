using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public abstract class OptionEditor(LocalizationService text, Action changed) : StackPanel
{
    protected readonly LocalizationService Text = text;
    protected readonly Action Changed = changed;
    protected void Heading(string key) => Children.Add(new TextBlock { Text = Text[key], FontSize = 17,
        Foreground = Brush.Parse("#FF9BD5"), FontWeight = FontWeight.SemiBold, Margin = new(0, 10, 0, 2) });
    protected void Toggle(string key, bool value, Action<bool> set)
    {
        var control = new CheckBox { Content = Text[key], IsChecked = value, Name = key };
        control.IsCheckedChanged += (_, _) => { set(control.IsChecked == true); Changed(); };
        Children.Add(control);
    }
    protected NumericUpDown Number(string key, double value, double minimum, double maximum, Action<double> set)
    {
        var control = new NumericUpDown { Minimum = (decimal)minimum, Maximum = (decimal)maximum, Value = (decimal)value,
            Width = 120, Increment = 1, Name = key };
        control.ValueChanged += (_, _) => { set((double)(control.Value ?? (decimal)value)); Changed(); };
        Children.Add(Row(key, control)); return control;
    }
    protected void String(string key, string value, Action<string> set)
    {
        var input = new TextBox { Text = value, MinWidth = 160, MaxLength = 2000, Name = key };
        input.TextChanged += (_, _) => { set(input.Text ?? ""); Changed(); };
        Children.Add(Row(key, input));
    }
    protected Grid Row(string key, Control control)
    {
        var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12, Margin = new(0, 3) };
        row.Children.Add(new TextBlock { Text = Text[key], TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center, Foreground = Brush.Parse("#D7C9F0") });
        Grid.SetColumn(control, 1); row.Children.Add(control); return row;
    }
    protected void Color(string key, string value, Action<string> set)
    {
        var swatch = new Border { Width = 24, Height = 24, CornerRadius = new(6), Background = Brush.Parse(value) };
        var input = new TextBox { Text = value, MaxLength = 7, Width = 105, Name = key };
        input.TextChanged += (_, _) =>
        {
            if (input.Text is { Length: 7 } color && Avalonia.Media.Color.TryParse(color, out var parsed))
            { set(color); swatch.Background = new SolidColorBrush(parsed); Changed(); }
        };
        var pair = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        pair.Children.Add(swatch); pair.Children.Add(input); Children.Add(Row(key, pair));
    }
}
