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
        LocalizationService text, Action open, Action changed, ClassicSettings? preferences = null)
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
        canvas.Children.Add(new Border { Height = 62, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
            Background = new LinearGradientBrush { StartPoint = new(0, 0, RelativeUnit.Relative), EndPoint = new(0, 1, RelativeUnit.Relative),
                GradientStops = [new(Color.Parse("#20FFFFFF"), 0), new(Colors.Transparent, 1)] } });
        canvas.Children.Add(new Border { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new(8), CornerRadius = new(7), Background = Brush("#A00F0D20"), Padding = new(5, 2), IsHitTestVisible = false,
            Child = new TextBlock { Text = "⚙", FontSize = 13, Foreground = Brush("#E8DAEF") } });
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
        frame = new Border { Child = canvas, CornerRadius = new CornerRadius(12),
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
        PropertyChanged += (_, change) => { if (change.Property == IsPressedProperty) Refresh(); };
        if (preferences is not null) VisualPolish.AttachHover(this, preferences, 1.025);
        Refresh();
    }

    public void Refresh()
    {
        lamp.Fill = Brush(settings.Enabled ? "#FF69B4" : "#66667C");
        frame.BorderBrush = new LinearGradientBrush { StartPoint = new(0, 0, RelativeUnit.Relative), EndPoint = new(1, 1, RelativeUnit.Relative),
            GradientStops = [new(Color.Parse(hovered || IsFocused ? "#FFFFB29F" : settings.Enabled ? "#99FF7E6B" : "#524B63"), 0),
                new(Color.Parse(hovered || IsFocused ? "#FFFF8FD0" : settings.Enabled ? "#AAFF69B4" : "#38314C"), 1)] };
        frame.BoxShadow = BoxShadows.Parse(IsPressed ? "0 1 3 0 #A0000000" : hovered || IsFocused
            ? "0 10 24 0 #90000000, 0 0 15 0 #40FF69B4" : settings.Enabled
                ? "0 4 12 0 #80000000, 0 0 7 0 #25FF69B4" : "0 3 10 0 #75000000");
        if (picture is not null) picture.Opacity = settings.Enabled ? 1 : hovered ? .86 : .68;
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
