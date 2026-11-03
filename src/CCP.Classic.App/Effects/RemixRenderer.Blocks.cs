using CCP.Classic.Core.Remix;
using SkiaSharp;

namespace CCP.Classic.App.Effects;

internal sealed partial class RemixRenderer
{
    private void Tint(SKCanvas canvas, RemixBlock block, int frame)
    {
        var a = block.Strength * block.Ramp(frame); var colour = Colour(block.Colour);
        if (a <= .005) return;
        if (block.Mode == "wash")
        {
            Fill(canvas, colour, .6 * a, SKBlendMode.Multiply);
            Fill(canvas, new((byte)RemixScene.Round(colour.Red * .6), (byte)RemixScene.Round(colour.Green * .6), (byte)RemixScene.Round(colour.Blue * .6)), .32 * a, SKBlendMode.Screen);
            return;
        }
        var surface = Buffer("tint"); var c = surface.Canvas;
        var x = block.Corner is "tl" or "bl" ? 0 : Width; var y = block.Corner is "tl" or "tr" ? 0 : Height;
        var reach = Math.Sqrt(Width * Width + Height * Height) * (.2 + .72 * block.Progress(frame));
        Arms(c, x, y, reach * .5, 1, frame * .045, 1, 140, colour.WithAlpha(Alpha(.16)));
        Radial(c, x, y, reach, [SKColors.White.WithAlpha(107), colour.WithAlpha(250), colour.WithAlpha(179), colour.WithAlpha(122), SKColors.White.WithAlpha(77), colour.WithAlpha(0)], [0, .2f, .62f, .86f, .93f, 1]);
        Radial(c, x, y, reach, [SKColors.Black, SKColors.Black.WithAlpha(235), SKColors.Black.WithAlpha(140), SKColors.Transparent], [0, .7f, .9f, 1], SKBlendMode.DstIn);
        using var image = surface.Snapshot(); DrawImage(canvas, image, Bounds, a, SKBlendMode.Screen); DrawImage(canvas, image, Bounds, a * .34, SKBlendMode.Multiply);
    }
    private void Spiral(SKCanvas canvas, RemixBlock block, int frame)
    {
        var strength = block.Strength * block.Ramp(frame); if (strength <= .005) return;
        var surface = Buffer("spiral"); var c = surface.Canvas; var radius = Math.Sqrt(Width * Width + Height * Height) * .62;
        var colour = Colour(block.Colour); var x = Width / 2d; var y = Height / 2d;
        Arms(c, x, y, radius, block.Style, frame * (.015 + block.Speed * .13) * block.Direction, block.Direction, 160, SKColors.White);
        Radial(c, x, y, radius, block.Mode == "through" ? [colour, colour.WithAlpha(235), colour.WithAlpha(128)]
            : [SKColors.White.WithAlpha(242), colour.WithAlpha(242), colour.WithAlpha(89)],
            block.Mode == "through" ? [0, .55f, 1] : [0, .35f, 1], SKBlendMode.SrcIn);
        Radial(c, x, y, radius, [SKColors.Black, SKColors.Black, SKColors.Black.WithAlpha(153), SKColors.Black.WithAlpha(31)], [0, .55f, .85f, 1], SKBlendMode.DstIn);
        Radial(c, x, y, Math.Max(3, radius * .12), [SKColors.White.WithAlpha(217), colour.WithAlpha(140), colour.WithAlpha(0)], [0, .45f, 1]);
        using var image = surface.Snapshot();
        DrawImage(canvas, image, Bounds, block.Mode == "through" ? .2 + .7 * strength : .15 + .62 * strength,
            block.Mode == "through" ? SKBlendMode.SrcOver : SKBlendMode.Screen);
    }
    private static void Arms(SKCanvas canvas, double cx, double cy, double radius, int style, double phase, int direction, int steps, SKColor colour)
    {
        var (arms, turns, duty, stroke) = Math.Clamp(style, 0, 2) switch { 1 => (2, 7d, .12, 2.2f), 2 => (6, 1.25, .5, 0f), _ => (3, 3d, .5, 0f) };
        var maximum = turns * Math.Tau; var width = Math.Tau / arms * duty;
        using var paint = new SKPaint { IsAntialias = true, Color = colour, Style = stroke > 0 ? SKPaintStyle.Stroke : SKPaintStyle.Fill,
            StrokeWidth = stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        for (var arm = 0; arm < arms; arm++)
        {
            using var path = new SKPath(); var start = phase + arm * Math.Tau / arms;
            void Point(int i, bool other)
            {
                var theta = maximum * i / steps; var r = radius * theta / maximum;
                var angle = start + theta * direction + (other ? width * direction : 0);
                var x = (float)(cx + Math.Cos(angle) * r); var y = (float)(cy + Math.Sin(angle) * r);
                if (i == 0 && !other) path.MoveTo(x, y); else path.LineTo(x, y);
            }
            for (var i = 0; i <= steps; i++) Point(i, false);
            if (stroke == 0) { for (var i = steps; i >= 0; i--) Point(i, true); path.Close(); }
            canvas.DrawPath(path, paint);
        }
    }
    private void Glitch(SKCanvas canvas, SKImage source, RemixBlock block, int frame)
    {
        var t = block.Progress(frame); var life = t < .08 ? Smooth(t / .08) : t > .86 ? Smooth((1 - t) / .14) : 1;
        var alpha = (.12 + .48 * Math.Pow(block.Strength, 1.35)) * life * block.Ramp(frame);
        if (alpha <= .004) return;
        SKRect rect; SKImage image;
        if (block.Mode == "double") { image = source; rect = new(-Width / 2f, -Height / 2f, Width * 1.5f, Height * 1.5f); }
        else
        {
            // Canvas blocks have no underlying media to exclude. Preserve distinct strip order.
            var pool = strips.Distinct().ToArray(); var bitmap = pool[Math.Abs(block.Pick) % pool.Length].At(frame);
            image = SKImage.FromBitmap(bitmap); var scale = Math.Max(Width / (float)bitmap.Width, Height / (float)bitmap.Height);
            rect = SKRect.Create((Width - bitmap.Width * scale) / 2, (Height - bitmap.Height * scale) / 2, bitmap.Width * scale, bitmap.Height * scale);
        }
        try { DrawImage(canvas, image, rect, alpha); DrawImage(canvas, image, rect, alpha * .55, SKBlendMode.SoftLight); }
        finally { if (!ReferenceEquals(image, source)) image.Dispose(); }
        Fill(canvas, new(255, 105, 180), Math.Min(.09, alpha * .22), SKBlendMode.Overlay);
    }
    private void Focus(SKCanvas canvas, SKImage source, RemixBlock block, int frame)
    {
        var alpha = block.Ramp(frame); if (alpha <= .01) return;
        var t = block.Progress(frame); var u = t < .5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
        var point = SamplePath(block.Path, u); var x = Math.Clamp(point.X, 0, 1) * Width; var y = Math.Clamp(point.Y, 0, 1) * Height;
        var radius = Math.Max(12, Math.Min(Width, Height) * (.16 + block.Strength * .55));
        if (block.Mode == "dark")
        {
            Radial(canvas, x, y, radius * 2.1, [new(10, 10, 22, 0), new(10, 10, 22, Alpha(.3 * alpha)), new(10, 10, 22, Alpha(.68 * alpha)), new(10, 10, 22, Alpha(.86 * alpha))], [.72f / 2.1f, (float)((.72 + .3 * 1.38) / 2.1), (float)((.72 + .68 * 1.38) / 2.1), 1]);
            Radial(canvas, x, y, radius * 1.05, [SKColors.White.WithAlpha(Alpha(.09 * alpha)), SKColors.Transparent], [0, 1], SKBlendMode.Plus);
            return;
        }
        var blur = 4 + 9 * (1 - block.Strength * .5); var scale = Math.Clamp(blur / 2, 1, 12);
        var small = Buffer("focus-small", Math.Max(2, RemixScene.Round(Width / scale)), Math.Max(2, RemixScene.Round(Height / scale)));
        DrawImage(small.Canvas, source, new(0, 0, small.Canvas.LocalClipBounds.Width, small.Canvas.LocalClipBounds.Height));
        using var smallImage = small.Snapshot(); using var paint = Paint(alpha);
        using var filter = SKImageFilter.CreateBlur((float)(blur / scale), (float)(blur / scale)); paint.ImageFilter = filter;
        canvas.DrawImage(smallImage, Bounds, Sampling, paint);
        Radial(canvas, x, y, radius * 2.4, [new(12, 12, 26, 0), new(12, 12, 26, Alpha(.2 * alpha)), new(12, 12, 26, Alpha(.46 * alpha))], [.8f / 2.4f, (float)((.8 + .45 * 1.6) / 2.4), 1]);
        var disc = Buffer("focus-disc"); DrawImage(disc.Canvas, source, Bounds);
        Radial(disc.Canvas, x, y, radius * 1.15, [SKColors.Black, SKColors.Black.WithAlpha(217), SKColors.Transparent], [.55f / 1.15f, (float)((.55 + .7 * .6) / 1.15), 1], SKBlendMode.DstIn);
        using var discImage = disc.Snapshot(); DrawImage(canvas, discImage, Bounds, alpha);
        Radial(canvas, x, y, radius * 1.1, [SKColors.White.WithAlpha(Alpha(.1 * alpha)), SKColors.White.WithAlpha(Alpha(.045 * alpha)), SKColors.Transparent], [0, .6f, 1], SKBlendMode.Plus);
    }
    private void Radial(SKCanvas canvas, double x, double y, double radius, SKColor[] colours, float[] stops, SKBlendMode blend = SKBlendMode.SrcOver)
    {
        using var shader = SKShader.CreateRadialGradient(new((float)x, (float)y), (float)Math.Max(.01, radius), colours, stops, SKShaderTileMode.Clamp);
        using var paint = Paint(1, blend); paint.Shader = shader; canvas.DrawRect(Bounds, paint);
    }
    private static double Smooth(double u) { u = Math.Clamp(u, 0, 1); return u * u * (3 - 2 * u); }
    private static RemixPoint SamplePath(IReadOnlyList<RemixPoint> points, double u)
    {
        if (points.Count == 0) return new(.5, .5); if (points.Count == 1) return points[0];
        static double Length(RemixPoint a, RemixPoint b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
        var lengths = Enumerable.Range(1, points.Count - 1).Select(i => Length(points[i - 1], points[i])).ToArray(); var wanted = u * lengths.Sum();
        for (var i = 0; i < lengths.Length; i++)
        { if (wanted <= lengths[i] || i == lengths.Length - 1) { var k = lengths[i] > 0 ? wanted / lengths[i] : 0; return new(points[i].X + (points[i + 1].X - points[i].X) * k, points[i].Y + (points[i + 1].Y - points[i].Y) * k); } wanted -= lengths[i]; }
        return points[^1];
    }
}
