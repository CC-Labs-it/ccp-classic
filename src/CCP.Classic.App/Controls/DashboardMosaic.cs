using Avalonia.Controls;
using Avalonia.Layout;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public static class DashboardMosaic
{
    public static Control Build(ClassicSettings settings, LocalizationService text, bool windows,
        Func<Control> logo, Action<EffectDefinition> open)
    {
        var mosaic = new Grid { Name = "DashboardMosaic", Width = 680, Height = 600,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), RowDefinitions = new RowDefinitions("*,*,*,*") };
        var locations = new (string Id, int Row, int Column)[]
        {
            ("flash", 0, 0), ("video", 0, 1), ("sub", 0, 2), ("bounce", 0, 3),
            ("audio", 1, 0), ("spiral", 1, 3), ("wall", 2, 0), ("pink", 2, 3),
            ("wipe", 3, 0), ("drain", 3, 1), ("bubble", 3, 2), ("lock", 3, 3)
        };
        foreach (var (id, row, column) in locations)
        {
            var definition = EffectCatalog.All.Single(x => x.Id == id);
            if (definition.WindowsOnly && !windows) continue;
            var tile = new EffectTile(definition, settings.Effects[id], text,
                () => open(definition), () => { });
            Grid.SetRow(tile, row);
            Grid.SetColumn(tile, column);
            mosaic.Children.Add(tile);
        }
        var centre = logo();
        Grid.SetRow(centre, 1);
        Grid.SetColumn(centre, 1);
        Grid.SetRowSpan(centre, 2);
        Grid.SetColumnSpan(centre, 2);
        mosaic.Children.Add(centre);
        return new Viewbox { Child = mosaic, Stretch = Avalonia.Media.Stretch.Uniform,
            StretchDirection = Avalonia.Media.StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            MaxWidth = 680 };
    }
}
