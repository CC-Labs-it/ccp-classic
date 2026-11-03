using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Controls.Shapes;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public sealed class EffectTile : Button
{
    private static readonly Dictionary<string, Bitmap> Images = [];
    private readonly EffectSettings settings;
    private readonly Border frame;
    private readonly Ellipse lamp;
    private readonly Image? picture;
    private bool hovered;

    public EffectTile(EffectDefinition definition, EffectSettings settings,
        LocalizationService text, Action open, Action changed)
    {
        this.settings = settings;
        Name = "Effect_" + definition.Id;
        Margin = new Thickness(5);
        Padding = new Thickness(0);
        MinHeight = MinWidth = 0;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(this, text[definition.Title]);
        ToolTip.SetTip(this, text[definition.Title]);
        var canvas = new Grid { ClipToBounds = true };
        var bitmap = ReadImage(definition.Id);
        if (bitmap is not null)
        {
            picture = new Image { Source = bitmap, Stretch = Stretch.UniformToFill };
            canvas.Children.Add(picture);
        }
        else
        {
            canvas.Children.Add(new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(definition.Icon),
                Width = 52, Height = 52, Stretch = Stretch.Uniform, Stroke = Brush("#FF69B4"),
                StrokeThickness = 1.3, Opacity = .45 });
        }
        canvas.Children.Add(new Border { Height = 68, VerticalAlignment = VerticalAlignment.Bottom,
            Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative), GradientStops =
                [new GradientStop(Colors.Transparent, 0), new GradientStop(Color.Parse("#E6050512"), 1)] } });
        var caption = new Grid { ColumnDefinitions = new ColumnDefinitions("10,*"),
            Margin = new Thickness(10, 7), VerticalAlignment = VerticalAlignment.Bottom };
        lamp = new Ellipse { Width = 5, Height = 5, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left };
        caption.Children.Add(lamp);
        var title = new TextBlock { Text = text[definition.Title], FontSize = 12,
            FontWeight = FontWeight.SemiBold, Foreground = Brush("#F0F0F5"),
            TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(title, 1);
        caption.Children.Add(title);
        canvas.Children.Add(caption);
        frame = new Border { Child = canvas, CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1), ClipToBounds = true, Background = Brush("#1C1C35") };
        Template = new FuncControlTemplate<EffectTile>((_, _) => frame);
        Click += (_, _) => { open(); Refresh(); };
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
            settings.Enabled = !settings.Enabled;
            Refresh();
            changed();
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        PointerEntered += (_, _) => { hovered = true; Refresh(); };
        PointerExited += (_, _) => { hovered = false; Refresh(); };
        GotFocus += (_, _) => Refresh();
        LostFocus += (_, _) => Refresh();
        Refresh();
    }

    public void Refresh()
    {
        lamp.Fill = Brush(settings.Enabled ? "#FF69B4" : "#66667C");
        frame.BorderBrush = Brush(hovered || IsFocused ? "#FF9ACE" : settings.Enabled ? "#A75487" : "#33334E");
        frame.BoxShadow = BoxShadows.Parse(settings.Enabled ? "0 2 8 0 #45000000, 0 0 5 0 #20FF69B4" : "0 2 6 0 #59000000");
        if (picture is not null) picture.Opacity = settings.Enabled ? 1 : .45;
    }

    private static Bitmap? ReadImage(string id)
    {
        lock (Images)
        {
            if (Images.TryGetValue(id, out var image)) return image;
            var uri = new Uri("avares://CCP.Classic.App/Assets/" + id + ".png");
            if (!AssetLoader.Exists(uri)) return null;
            using var stream = AssetLoader.Open(uri);
            image = new Bitmap(stream);
            Images.Add(id, image);
            return image;
        }
    }
    private static SolidColorBrush Brush(string colour) => new(Color.Parse(colour));
}
