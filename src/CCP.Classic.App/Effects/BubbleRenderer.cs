using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CCP.Classic.Core;

namespace CCP.Classic.App.Effects;

// Ports CCP's bubble.png, variant tints and ellipse-clipped BubbleFace at 60% of the sprite.
internal static class BubbleRenderer
{
    private static readonly Lazy<Bitmap> Glass = new(() =>
    {
        using var stream = AssetLoader.Open(new Uri("avares://CCP.Classic.App/Assets/Bubbles/glass.png"));
        return new Bitmap(stream);
    });
    private static readonly RadialGradientBrush EdgeMask = new()
    {
        RadiusX = new(.5, RelativeUnit.Relative), RadiusY = new(.5, RelativeUnit.Relative),
        GradientStops = [new(Colors.White, 0), new(Colors.White, .8), new(Colors.Transparent, 1)]
    };
    private static readonly LinearGradientBrush Shine = new()
    {
        StartPoint = new(0, 0, RelativeUnit.Relative), EndPoint = new(1, 1, RelativeUnit.Relative),
        GradientStops = [new(Colors.Transparent, 0), new(Colors.White, .42), new(Colors.Transparent, .65)]
    };
    private static readonly Dictionary<(BubbleEffectKind, int), FormattedText> Labels = [];
    private static readonly Dictionary<BubbleEffectKind, Bitmap> Sprites = [];
    private static Bitmap Sprite(BubbleEffectKind kind)
    {
        if (kind == BubbleEffectKind.None) return Glass.Value;
        if (Sprites.TryGetValue(kind, out var bitmap)) return bitmap;
        var name = kind switch { BubbleEffectKind.Flash => "flash", BubbleEffectKind.Subliminal => "subliminal",
            BubbleEffectKind.Pink => "pink", BubbleEffectKind.Spiral => "spiral", BubbleEffectKind.BrainDrain => "braindrain_melt",
            BubbleEffectKind.Video => "video", BubbleEffectKind.Glitch => "glitch", BubbleEffectKind.Cascade => "htlink", _ => "braindrain" };
        using var stream = AssetLoader.Open(new Uri("avares://CCP.Classic.App/Assets/Bubbles/" + name + ".png"));
        return Sprites[kind] = new Bitmap(stream);
    }

    public static void Draw(DrawingContext context, Rect viewport, BubbleCue cue, IImage? image,
        double phase, bool reduced, bool gentle, bool performance)
    {
        if (!double.IsFinite(cue.Opacity) || cue.Opacity <= 0) return;
        var normalized = BubbleGeometry.BaseBounds(cue, viewport.Width, viewport.Height);
        if (normalized.Width <= 0 || normalized.Height <= 0) return;
        var rect = new Rect(viewport.X + normalized.X * viewport.Width, viewport.Y + normalized.Y * viewport.Height,
            normalized.Width * viewport.Width, normalized.Height * viewport.Height);
        var (colour, symbol) = Style(cue.Effect);
        var tint = new SolidColorBrush(colour);
        using (context.PushOpacity(Math.Clamp(cue.Opacity, 0, 1)))
        {
            var pop = double.IsFinite(cue.PopProgress) ? Math.Clamp(cue.PopProgress, 0, 1) : 0;
            var scale = Math.Clamp(cue.Animation?.Scale ?? 1, .05, 2);
            var angle = (cue.Animation?.Rotation ?? 0) * Math.PI / 180;
            using (context.PushTransform(Matrix.CreateTranslation(-rect.Center.X, -rect.Center.Y)
                * Matrix.CreateScale(scale, scale) * Matrix.CreateRotation(angle)
                * Matrix.CreateTranslation(rect.Center.X, rect.Center.Y)))
            {
                var glass = rect.Deflate(rect.Width * .145);
                if (cue.Effect == BubbleEffectKind.None)
                    using (context.PushOpacity(.12)) context.DrawEllipse(tint, null, glass);
                context.DrawImage(Sprite(cue.Effect), rect);
            }
            // CCP's shared compositor keeps preview faces and labels upright while the glass spins.
            var face = rect.Deflate(rect.Width * .2);
            if (cue.Effect == BubbleEffectKind.Flash && image is not null && image.Size.Width > 0 && image.Size.Height > 0)
            {
                var fit = Math.Max(face.Width / image.Size.Width, face.Height / image.Size.Height);
                var source = new Rect((image.Size.Width - face.Width / fit) / 2,
                    (image.Size.Height - face.Height / fit) / 2, face.Width / fit, face.Height / fit);
                using (context.PushGeometryClip(new EllipseGeometry(face)))
                using (context.PushOpacityMask(EdgeMask, face))
                    context.DrawImage(image, source, face);
            }
            else if (symbol.Length > 0)
            {
                var label = Label(cue.Effect, symbol, Math.Max(12, (int)(rect.Width * .26)));
                context.DrawText(label, rect.Center - new Vector(label.Width / 2, label.Height / 2));
            }
            if (cue.Effect == BubbleEffectKind.None) using (context.PushOpacity(.14)) context.DrawEllipse(Shine, null, face);
            if (pop > 0 && !reduced && !gentle && cue.Animation is { PopFrames: > 0 } animation)
            {
                var count = performance ? 4 : 8;
                for (var index = 0; index < count; index++)
                {
                    var spark = BubbleChoreography.Spark(animation.Seed, index, animation.PopFrames);
                    using (context.PushOpacity(spark.Alpha))
                        context.DrawEllipse(Brushes.HotPink, null, rect.Center + new Vector(spark.X, spark.Y), spark.Size / 2, spark.Size / 2);
                }
            }
        }
    }

    private static FormattedText Label(BubbleEffectKind kind, string symbol, int size)
    {
        if (Labels.TryGetValue((kind, size), out var label)) return label;
        if (Labels.Count >= 24) Labels.Clear();
        return Labels[(kind, size)] = new(symbol, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Arial", FontStyle.Normal, FontWeight.Bold), size, Brushes.White);
    }
    private static (Color, string) Style(BubbleEffectKind kind) => kind switch
    {
        BubbleEffectKind.Flash => (Color.Parse("#FFD0E8"), ""),
        BubbleEffectKind.Subliminal => (Color.Parse("#B080FF"), "♥"),
        BubbleEffectKind.Pink => (Color.Parse("#FF3DA5"), "◑"),
        BubbleEffectKind.Spiral => (Color.Parse("#40D0C0"), "◎"),
        BubbleEffectKind.BrainDrain => (Color.Parse("#4060C0"), "☁"),
        BubbleEffectKind.Video => (Color.Parse("#E0404D"), "▶"),
        BubbleEffectKind.Glitch => (Color.Parse("#9A40FF"), ""),
        BubbleEffectKind.Cascade => (Color.Parse("#FFC83D"), "▼"),
        BubbleEffectKind.BrainDrainWash => (Color.Parse("#4060C0"), "☁"),
        _ => (Color.Parse("#FF69B4"), "")
    };
}
