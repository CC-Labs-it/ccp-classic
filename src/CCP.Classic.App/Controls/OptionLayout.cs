using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CCP.Classic.App.Controls;

/// <summary>Soft section bands and responsive pairs, with full rows for editors and lists.</summary>
public static class OptionLayout
{
    public static IReadOnlyList<Control> Groups(IEnumerable<Control> controls, string general, int startIndex = 0)
    {
        var result = new List<Control>(); var items = new List<Control>(); string title = general;
        foreach (var control in controls)
        {
            if (control.Classes.Contains("option-heading") && control is TextBlock heading)
            {
                if (items.Count > 0) { result.Add(Group(items, title, startIndex + result.Count)); items.Clear(); }
                title = heading.Text ?? general;
            }
            else items.Add(control);
        }
        if (items.Count > 0) result.Add(Group(items, title, startIndex + result.Count));
        return result;
    }

    public static Control Group(IEnumerable<Control> controls, string title, int index = 0)
    {
        var panel = new OptionPairsPanel();
        foreach (var control in controls) panel.Children.Add(control);
        var body = new StackPanel { Spacing = 10 };
        if (!string.IsNullOrWhiteSpace(title)) body.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse(new[] { "#A8DDE4", "#CFB9E9", "#E7C6A4", "#B9DAC6" }[index % 4]) });
        body.Children.Add(panel);
        return new Border { Padding = new Thickness(12, 13), Child = body,
            Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative), GradientStops =
                [new GradientStop(Color.Parse("#242032"), 0),
                 new GradientStop(Color.Parse(new[] { "#20343C", "#31283E", "#39302C", "#25372F" }[index % 4]), .35),
                 new GradientStop(Color.Parse(new[] { "#20343C", "#31283E", "#39302C", "#25372F" }[index % 4]), .7),
                 new GradientStop(Color.Parse("#242032"), 1)] } };
    }
}

public sealed class OptionPairsPanel : Panel
{
    private const double Gap = 16;
    private IEnumerable<Control[]> Rows(double width)
    {
        Control? pending = null;
        foreach (var child in Children.Where(child => child.IsVisible))
        {
            if (width >= 620 && child.Classes.Contains("option-compact"))
            {
                if (pending is null) pending = child;
                else { yield return [pending, child]; pending = null; }
            }
            else
            {
                if (pending is not null) { yield return [pending]; pending = null; }
                yield return [child];
            }
        }
        if (pending is not null) yield return [pending];
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 700;
        double height = 0, desiredWidth = 0;
        foreach (var row in Rows(width))
        {
            var cellWidth = row.Length == 2 ? (width - Gap) / 2 : width;
            foreach (var child in row) child.Measure(new Size(cellWidth, double.PositiveInfinity));
            height += row.Max(child => child.DesiredSize.Height) + 8;
            desiredWidth = Math.Max(desiredWidth, row.Sum(child => child.DesiredSize.Width) + (row.Length - 1) * Gap);
        }
        return new Size(Math.Min(width, desiredWidth), Math.Max(0, height - 8));
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        foreach (var row in Rows(finalSize.Width))
        {
            var height = row.Max(child => child.DesiredSize.Height);
            var cellWidth = row.Length == 2 ? (finalSize.Width - Gap) / 2 : finalSize.Width;
            for (var i = 0; i < row.Length; i++) row[i].Arrange(new Rect(i * (cellWidth + Gap), y, cellWidth, height));
            y += height + 8;
        }
        return finalSize;
    }
}
