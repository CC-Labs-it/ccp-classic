using System.Globalization;
using Avalonia;
using Avalonia.Media;
using CCP.Classic.App.Controls;
using CCP.Classic.Core;

namespace CCP.Classic.App.Effects;

internal static class BouncingTextRenderer
{
    private sealed record TextShape(FormattedText Text, Geometry? Outline);
    private static readonly Dictionary<(string, string, double, Color, bool), TextShape> Cache = [];

    public static (double Width, double Height) Measure(string text, string family, double size)
    {
        var shape = Shape(text, family, size, Colors.White, false).Text;
        return (shape.Width, shape.Height);
    }

    public static void Draw(DrawingContext context, Rect bounds, BouncingTextCue cue)
    {
        if (!double.IsFinite(cue.Opacity) || cue.Opacity <= 0 || string.IsNullOrWhiteSpace(cue.Text)) return;
        var fontSize = Math.Clamp(72 * cue.Size, 1, 216);
        var color = Color.TryParse(cue.Colour, out var parsed) ? parsed : Color.Parse("#FF69B4");
        var shape = Shape(cue.Text, cue.FontFamily, fontSize, color, cue.Outline);
        var text = shape.Text;
        var over = BouncingTextTimeline.Overhang(text.Width, text.Height, cue.ScaleX, cue.ScaleY, cue.Angle);
        var travelX = Math.Max(0, bounds.Width - text.Width); var travelY = Math.Max(0, bounds.Height - text.Height);
        var x = Math.Clamp(cue.X * travelX, Math.Min(over.X, travelX / 2), travelX - Math.Min(over.X, travelX / 2));
        var y = Math.Clamp(cue.Y * travelY, Math.Min(over.Y, travelY / 2), travelY - Math.Min(over.Y, travelY / 2));
        var origin = new Point(bounds.X + x, bounds.Y + y);
        var centre = origin + new Vector(text.Width / 2, text.Height / 2);
        using (context.PushClip(bounds))
        using (context.PushRenderOptions(new RenderOptions { RequiresFullOpacityHandling = true }))
        using (context.PushOpacity(Math.Clamp(cue.Opacity, 0, 1)))
        {
            if (cue.BurstSeconds is >= 0 and <= .55)
            {
                var t = cue.BurstSeconds / .55; var scale = .1 + 1.2 * (1 - (1 - t) * (1 - t));
                using (context.PushOpacity(.9 * (1 - t)))
                    context.DrawEllipse(null, new Pen(new SolidColorBrush(color), 6 * scale),
                        new Point(bounds.X + cue.BurstCenterX * bounds.Width, bounds.Y + cue.BurstCenterY * bounds.Height),
                        120 * scale, 120 * scale);
            }
            using (context.PushTransform(Matrix.CreateTranslation(-centre.X, -centre.Y)
                * Matrix.CreateScale(cue.ScaleX, cue.ScaleY) * Matrix.CreateRotation(cue.Angle * Math.PI / 180)
                * Matrix.CreateTranslation(centre.X, centre.Y)))
            {
                if (shape.Outline is { } geometry)
                {
                    using (context.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y)))
                        context.DrawGeometry(null, new Pen(Brushes.Black, Math.Max(2, fontSize / 22)), geometry);
                    context.DrawText(text, origin);
                }
                else context.DrawText(text, origin);
            }
        }
    }

    private static TextShape Shape(string text, string font, double size, Color color, bool outline)
    {
        var key = (text, font, size, color, outline);
        if (Cache.TryGetValue(key, out var found)) return found;
        if (Cache.Count >= 16) Cache.Clear();
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(LocalFontPicker.Resolve(font), FontStyle.Normal, FontWeight.Bold), size, new SolidColorBrush(color));
        return Cache[key] = new(formatted, outline ? formatted.BuildGeometry(default) : null);
    }
}
