using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public abstract class OptionEditor(LocalizationService text, Action changed) : StackPanel
{
    protected readonly LocalizationService Text = text;
    protected readonly Action Changed = changed;
    private bool prepared;
    private bool promoted;
    protected void Heading(string key)
    {
        var heading = new TextBlock { Text = Text[key], FontSize = 16,
            Foreground = Brush.Parse("#FF9BD5"), FontWeight = FontWeight.SemiBold };
        heading.Classes.Add("option-heading"); Children.Add(heading);
    }
    public Control? TakeVisibleOptions()
    {
        if (prepared || Children.Count == 0) return null;
        var firstHeading = Children.ToList().FindIndex(control => control.Classes.Contains("option-heading"));
        var count = firstHeading > 0 ? firstHeading : Math.Max(1, (Children.Count + 1) / 2);
        count = Math.Min(count, 12);
        promoted = true;
        var visible = Children.Take(count).ToArray();
        foreach (var child in visible) Children.Remove(child);
        var sections = new StackPanel { Spacing = 0 };
        foreach (var group in OptionLayout.Groups(visible, "")) sections.Children.Add(group);
        return sections;
    }
    public void PrepareLayout()
    {
        if (prepared) return;
        prepared = true;
        var controls = Children.ToArray(); Children.Clear(); Spacing = 0;
        foreach (var group in OptionLayout.Groups(controls, Text["OptionsGeneral"], promoted ? 1 : 0)) Children.Add(group);
    }
    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        PrepareLayout(); base.OnAttachedToLogicalTree(e);
    }
    protected CheckBox Toggle(string key, bool value, Action<bool> set, string? helpKey = null)
    {
        var control = new CheckBox { Content = new TextBlock { Text = Text[key], TextWrapping = TextWrapping.Wrap }, IsChecked = value, Name = key };
        control.IsCheckedChanged += (_, _) => { set(control.IsChecked == true); Changed(); };
        var helped = OptionHelp.Wrap(control, helpKey ?? key, Text); helped.Classes.Add("option-compact"); Children.Add(helped); return control;
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
        var row = Row(key, input); row.Classes.Remove("option-compact"); Children.Add(row);
    }
    protected Grid Row(string key, Control control, string? helpKey = null)
    {
        OptionHelp.SetTip(control, helpKey ?? key, Text);
        var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12, Margin = new(0, 3) };
        row.Classes.Add("option-compact");
        row.Children.Add(OptionHelp.Wrap(new TextBlock { Text = Text[key], TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center, Foreground = Brush.Parse("#D7C9F0") }, helpKey ?? key, Text));
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
