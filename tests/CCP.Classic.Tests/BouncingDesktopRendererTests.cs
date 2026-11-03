using Avalonia;
using Avalonia.Media.Imaging;
using CCP.Classic.App.Effects;
using CCP.Classic.Core;
using SkiaSharp;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class BouncingDesktopRendererTests
{
    [Fact]
    public Task OneActualGlyphIsClippedAcrossMixedDpiHostsRatherThanRepositionedInEach() => UiDispatcher.Run(() =>
    {
        var width = BouncingTextRenderer.Measure("Focus", "Fredoka", 72).Width;
        var cue = new BouncingTextCue("Focus", 380 / (960 - width), .4, 1, 1)
        { FontFamily = "Fredoka", Colour = "#FF69B4", Outline = true, DesktopBounds = new(0, 0, 960, 300) };
        var frame = new EffectFrame { Interactive = new([], cue) };
        var desktop = new BouncingDesktopLayout([new(0, 0, 480, 300), new(480, 0, 480, 300)]);
        using var left = Render(BouncingTextHostProjection.Apply(frame, desktop.Project(0, 0, 1)), 480, 300, 1);
        using var right = Render(BouncingTextHostProjection.Apply(frame, desktop.Project(480, 0, 1.5)), 480, 300, 1.5);
        using var whole = Render(frame, 960, 300, 1);
        using var leftPixels = Pixels(left); using var rightPixels = Pixels(right); using var wholePixels = Pixels(whole);
        var leftInk = Ink(leftPixels); var rightInk = Ink(rightPixels); var wholeInk = Ink(wholePixels);
        SaveJoined(leftPixels, rightPixels, wholePixels);
        Assert.NotEmpty(leftInk); Assert.NotEmpty(rightInk);
        Assert.InRange(leftInk.Min(p => p.X), 370, 479);
        Assert.InRange(rightInk.Max(p => p.X), 1, (int)Math.Ceiling(width));
        Assert.True(leftInk.Count + rightInk.Count >= wholeInk.Count * .95 && leftInk.Count + rightInk.Count <= wholeInk.Count * 1.05,
            $"Left {Describe(leftInk)}, right {Describe(rightInk)}, desktop {Describe(wholeInk)}");
        Assert.Equal(wholeInk.Min(p => p.Y), leftInk.Concat(rightInk).Min(p => p.Y));
    });

    [Fact]
    public void ExtraEffectOnlyHostsOmitBouncingWithoutRemovingTheirOtherEffects()
    {
        var first = new BouncingTextCue("Focus", .5, .5, 1, 1) { DesktopBounds = new(0, 0, 1920, 1080) };
        var second = first with { Text = "Listen", X = .1 };
        var original = new EffectFrame { PinkOpacity = .3, Interactive = new([], first) { BouncingTexts = [first, second] } };
        var omitted = BouncingTextHostProjection.Apply(original, null);
        Assert.Null(omitted.Interactive.BouncingText); Assert.Empty(omitted.Interactive.BouncingTexts); Assert.Equal(.3, omitted.PinkOpacity);
        var mapped = BouncingTextHostProjection.Apply(original, new(-1920, 0, 1));
        Assert.Equal(2, mapped.Interactive.BouncingTexts.Count);
        Assert.Equal(first.X, mapped.Interactive.BouncingTexts[0].X); Assert.Equal(second.X, mapped.Interactive.BouncingTexts[1].X);
        Assert.Equal(2, original.Interactive.BouncingTexts.Count); Assert.Null(first.Projection);
    }

    private static RenderTargetBitmap Render(EffectFrame frame, int width, int height, double scale)
    {
        var view = new VisualEffectsControl(); view.UpdateFrame(frame);
        view.Measure(new Size(width / scale, height / scale)); view.Arrange(new Rect(0, 0, width / scale, height / scale));
        // Explicit host scaling avoids the bitmap backend's hidden DPI being reapplied after PopClip.
        var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        using (context.PushTransform(Matrix.CreateScale(scale, scale))) view.Render(context);
        return bitmap;
    }
    private static SKBitmap Pixels(RenderTargetBitmap bitmap)
    {
        using var stream = new MemoryStream(); bitmap.Save(stream, PngBitmapEncoderOptions.Default); stream.Position = 0;
        return SKBitmap.Decode(stream);
    }
    private static List<(int X, int Y)> Ink(SKBitmap bitmap)
    {
        var ink = new List<(int, int)>();
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++) if (bitmap.GetPixel(x, y).Alpha > 32) ink.Add((x, y));
        return ink;
    }
    private static string Describe(List<(int X, int Y)> ink) => $"{ink.Count}: ({ink.Min(p => p.X)},{ink.Min(p => p.Y)})-({ink.Max(p => p.X)},{ink.Max(p => p.Y)})";
    private static void SaveJoined(SKBitmap left, SKBitmap right, SKBitmap whole)
    {
        var folder = Environment.GetEnvironmentVariable("CCP_CLASSIC_PROOF_DIR"); if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        using var proof = new SKBitmap(960, 300); using var canvas = new SKCanvas(proof);
        canvas.Clear(new SKColor(21, 19, 33)); canvas.DrawBitmap(left, 0, 0); canvas.DrawBitmap(right, 480, 0);
        using var paint = new SKPaint { Color = new SKColor(80, 75, 100), StrokeWidth = 1 };
        canvas.DrawLine(480, 0, 480, 300, paint);
        using var data = proof.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(Path.Combine(folder, "bouncing-text-desktop-seam.png")); data.SaveTo(file);
        using var reference = whole.Encode(SKEncodedImageFormat.Png, 100);
        using var original = File.Create(Path.Combine(folder, "bouncing-text-desktop-reference.png")); reference.SaveTo(original);
    }
}
