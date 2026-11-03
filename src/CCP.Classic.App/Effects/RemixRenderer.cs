using CCP.Classic.App.Media;
using CCP.Classic.Core.Remix;
using SkiaSharp;

namespace CCP.Classic.App.Effects;

// Native port of public Remix engine render.js at fe40ea7587ac0b09e47d0724e59d7178db3336da.
// The seeded Jackpot plan uses eight GIF tiles and canvas tint, spiral, glitch and focus blocks.
internal sealed partial class RemixRenderer(RemixScene scene, RemixStrip[] strips) : IDisposable
{
    private static readonly SKColor Ground = new(20, 20, 43);
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.None);
    private readonly Dictionary<string, SKSurface> buffers = [];
    private readonly RemixSlot[][] layouts = Enumerable.Range(0, RemixScene.Frames).Select(frame => RemixLayouts.At(scene, frame).OrderBy(slot => slot.Z).ToArray()).ToArray();
    private int Width => scene.Width;
    private int Height => scene.Height;
    private SKRect Bounds => new(0, 0, Width, Height);
    private SKSurface Buffer(string key, int width = 0, int height = 0)
    {
        if (!buffers.TryGetValue(key, out var surface))
            buffers.Add(key, surface = SKSurface.Create(new SKImageInfo(width == 0 ? Width : width, height == 0 ? Height : height)));
        surface.Canvas.RestoreToCount(1); surface.Canvas.Clear(SKColors.Transparent);
        return surface;
    }
    internal void Draw(SKSurface surface, int frame, bool tail = true)
    {
        var canvas = surface.Canvas;
        frame = Math.Clamp(frame, 0, RemixScene.Frames - 1);
        canvas.Save(); canvas.ClipRect(Bounds); canvas.Clear(Ground);
        if (RemixLayouts.HasBackdrop(scene.Layout))
        {
            var tiny = Buffer("backdrop", Math.Max(8, RemixScene.Round(Width / 12d)), Math.Max(8, RemixScene.Round(Height / 12d)));
            Cover(tiny.Canvas, strips[0].At(frame), new(0, 0, tiny.Canvas.LocalClipBounds.Width, tiny.Canvas.LocalClipBounds.Height));
            using var image = tiny.Snapshot(); DrawImage(canvas, image, Bounds, .9);
            Fill(canvas, Ground, .55);
        }
        foreach (var slot in layouts[frame]) DrawSlot(canvas, slot, frame);
        foreach (var block in scene.Blocks.OrderBy(block => block.Start))
        {
            if (frame < block.Start || frame >= block.End || block.Ramp(frame) <= 0) continue;
            if (block.Effect == "tint") Tint(canvas, block, frame);
            else if (block.Effect == "spiral") Spiral(canvas, block, frame);
            else
            {
                // Snapshot the destination immediately before a source-reading block.
                using var source = surface.Snapshot();
                if (block.Effect == "glitch") Glitch(canvas, source, block, frame);
                else if (block.Effect == "focus") Focus(canvas, source, block, frame);
            }
        }
        if (tail) Loop(canvas, frame);
        canvas.Restore();
    }
    private void DrawSlot(SKCanvas canvas, RemixSlot slot, int frame)
    {
        if (slot.Alpha <= .002) return;
        var r = slot.Rect; var x = r.X * Width; var y = r.Y * Height; var w = r.Width * Width; var h = r.Height * Height;
        if (slot.Gap)
        {
            var left = r.X > .0001 ? 1 : 0; var top = r.Y > .0001 ? 1 : 0;
            var right = r.X + r.Width < .9999 ? 1 : 0; var bottom = r.Y + r.Height < .9999 ? 1 : 0;
            x += left; y += top; w = Math.Max(1, w - left - right); h = Math.Max(1, h - top - bottom);
        }
        canvas.Save(); canvas.Translate((float)(x + w / 2), (float)(y + h / 2));
        canvas.RotateDegrees((float)(slot.Rotation * 180 / Math.PI)); w *= slot.Scale; h *= slot.Scale;
        using var paint = Paint(slot.Alpha);
        if (slot.Plate) { paint.Color = Ground.WithAlpha(Alpha(slot.Alpha)); canvas.DrawRect((float)(-w / 2 - 3), (float)(-h / 2 - 3), (float)(w + 6), (float)(h + 6), paint); paint.Color = SKColors.White.WithAlpha(Alpha(slot.Alpha)); }
        canvas.ClipRect(new((float)(-w / 2), (float)(-h / 2), (float)(w / 2), (float)(h / 2)), SKClipOperation.Intersect, true);
        var bitmap = strips[slot.Tile % strips.Length].At(frame + slot.FrameOffset);
        var crop = slot.Crop; var scale = Math.Max(w / bitmap.Width, h / bitmap.Height) * (crop?.Zoom ?? 1);
        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, new SKRect((float)(-(crop?.X ?? .5) * bitmap.Width * scale), (float)(-(crop?.Y ?? .5) * bitmap.Height * scale),
            (float)((1 - (crop?.X ?? .5)) * bitmap.Width * scale), (float)((1 - (crop?.Y ?? .5)) * bitmap.Height * scale)), Sampling, paint);
        canvas.Restore();
    }
    private void Loop(SKCanvas canvas, int frame)
    {
        if (scene.Loop == "seamless" && frame >= RemixScene.Frames - 6)
        {
            var head = Buffer("seam"); var at = frame - (RemixScene.Frames - 6);
            Draw(head, at, false); using var image = head.Snapshot(); DrawImage(canvas, image, Bounds, (at + 1d) / 6 * .92);
        }
        else if (scene.Loop == "snap" && frame >= RemixScene.Frames - 3)
        {
            Fill(canvas, Colour(scene.Blocks.LastOrDefault(block => block.Effect == "tint")?.Colour ?? "pink"), .55 + .45 * (frame - 41d) / 3);
            using var font = new SKFont(SKTypeface.FromFamilyName("Inter", SKFontStyle.Bold), RemixScene.Round(Height * .22));
            using var paint = new SKPaint { IsAntialias = true, Color = Ground };
            canvas.DrawText("drop", Width / 2f, Height / 2f - (font.Metrics.Ascent + font.Metrics.Descent) / 2, SKTextAlign.Center, font, paint);
        }
    }
    private static SKPaint Paint(double alpha = 1, SKBlendMode blend = SKBlendMode.SrcOver) => new() { IsAntialias = true, Color = SKColors.White.WithAlpha(Alpha(alpha)), BlendMode = blend };
    private static byte Alpha(double value) => (byte)Math.Clamp(RemixScene.Round(value * 255), 0, 255);
    private static SKColor Colour(string name) => name switch { "lavender" => new(184, 166, 232), "gold" => new(240, 194, 75), _ => new(255, 105, 180) };
    private void Fill(SKCanvas canvas, SKColor color, double alpha, SKBlendMode blend = SKBlendMode.SrcOver)
    { using var paint = Paint(alpha, blend); paint.Color = color.WithAlpha(Alpha(alpha)); canvas.DrawRect(Bounds, paint); }
    private static void DrawImage(SKCanvas canvas, SKImage image, SKRect target, double alpha = 1, SKBlendMode blend = SKBlendMode.SrcOver)
    { using var paint = Paint(alpha, blend); canvas.DrawImage(image, target, Sampling, paint); }
    private static void Cover(SKCanvas canvas, SKBitmap bitmap, SKRect target, double alpha = 1)
    {
        var scale = Math.Max(target.Width / bitmap.Width, target.Height / bitmap.Height);
        using var image = SKImage.FromBitmap(bitmap);
        var width = bitmap.Width * scale; var height = bitmap.Height * scale;
        DrawImage(canvas, image, SKRect.Create(target.MidX - width / 2, target.MidY - height / 2, width, height), alpha);
    }
    public void Dispose() { foreach (var buffer in buffers.Values) buffer.Dispose(); buffers.Clear(); }
}
