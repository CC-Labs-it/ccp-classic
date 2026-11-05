using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace CCP.Classic.App.Effects;

internal readonly record struct BrainDrainCapture(nint Pixels, int Width, int Height, int Stride, int Downscale);
internal readonly record struct BrainDrainVisual(double Strength, bool Melt, double Seconds);

// Adapted from public CCP BrainDrainLayer/BrainDrainCapturePump at fe40ea7587ac0b09e47d0724e59d7178db3336da.
// GDI BGRX's unused alpha byte is ignored, then red/blue are swapped back, as in the original fix.
internal sealed class BrainDrainFrameProcessor : IDisposable
{
    private readonly SKColorFilter swap = SKColorFilter.CreateColorMatrix([
        0,0,1,0,0, 0,1,0,0,0, 1,0,0,0,0, 0,0,0,1,0]);
    private readonly SKPaint paint = new();
    private SKBitmap? target;
    private SKCanvas? canvas;
    private SKImage? noise;
    private int noiseShortEdge;
    private float coverage;
    internal int NoiseTilesCreated { get; private set; }

    internal static double AlphaFor(double strength)
    {
        if (!double.IsFinite(strength) || strength <= 0) return 0;
        var t = (Math.Clamp(strength, 1, 100) - 1) / 99d;
        return Math.Round((.10 + .75 * Math.Pow(t, .66)) * 255) / 255;
    }
    internal static double SigmaFor(double strength, int downscale) => Math.Clamp(strength, 0, 200) * .14 / Math.Max(1, downscale) / 3;

    internal Bitmap? Process(BrainDrainCapture capture, BrainDrainVisual visual)
    {
        if (visual.Strength <= 0) return null;
        if (capture.Pixels == 0 || capture.Width is <= 0 or > 1024 || capture.Height is <= 0 or > 1024
            || capture.Stride < capture.Width * 4 || !double.IsFinite(visual.Strength + visual.Seconds))
            throw new IOException("Desktop capture frame is invalid");
        if (target is null || target.Width != capture.Width || target.Height != capture.Height)
        {
            canvas?.Dispose(); target?.Dispose();
            target = new(new SKImageInfo(capture.Width, capture.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            canvas = new(target);
        }
        using var raw = SKImage.FromPixels(new(capture.Width, capture.Height, SKColorType.Rgb888x, SKAlphaType.Opaque), capture.Pixels, capture.Stride);
        if (raw is null) throw new IOException("Desktop capture frame is unavailable");
        var sigma = (float)SigmaFor(visual.Strength, capture.Downscale);
        using var blur = sigma > .05f ? SKImageFilter.CreateBlur(sigma, sigma) : null;
        SKShader? drift = null; SKImageFilter? noiseFilter = null, melt = null;
        try
        {
            paint.ColorFilter = swap; paint.ImageFilter = blur;
            var gutter = 0f;
            if (visual.Melt)
            {
                EnsureNoise(capture.Width, capture.Height);
                var tierScale = 4f / Math.Max(1, capture.Downscale);
                var phase = (float)visual.Seconds;
                var dx = 2.5f * tierScale * MathF.Sin(phase * .55f);
                var dy = (6f * tierScale * phase) % coverage;
                var scale = coverage / 256;
                drift = SKShader.CreateImage(noise!, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat,
                    new SKSamplingOptions(SKFilterMode.Linear), SKMatrix.CreateScaleTranslation(scale, scale, dx, dy));
                noiseFilter = SKImageFilter.CreateShader(drift);
                var amplitude = (float)((1 + Math.Clamp(visual.Strength, 0, 200) * .045) * tierScale);
                melt = SKImageFilter.CreateDisplacementMapEffect(SKColorChannel.R, SKColorChannel.G, amplitude, noiseFilter, blur);
                paint.ImageFilter = melt; gutter = amplitude * .5f + 2;
            }
            canvas!.Clear(SKColors.Transparent);
            canvas.Save();
            if (gutter > 0)
            {
                canvas.Translate(capture.Width * .5f, capture.Height * .5f);
                canvas.Scale((capture.Width + 2 * gutter) / capture.Width, (capture.Height + 2 * gutter) / capture.Height);
                canvas.Translate(-capture.Width * .5f, -capture.Height * .5f);
            }
            canvas.DrawImage(raw, 0, 0, paint); canvas.Restore(); canvas.Flush();
            return new(PixelFormat.Bgra8888, AlphaFormat.Premul, target!.GetPixels(),
                new(capture.Width, capture.Height), new(96, 96), target.RowBytes);
        }
        finally
        {
            paint.ImageFilter = null; paint.ColorFilter = null;
            melt?.Dispose(); noiseFilter?.Dispose(); drift?.Dispose();
        }
    }
    private void EnsureNoise(int width, int height)
    {
        var shortEdge = Math.Min(width, height);
        if (noise is not null && shortEdge == noiseShortEdge) return;
        noise?.Dispose(); noiseShortEdge = shortEdge;
        coverage = Math.Max(8, shortEdge * .20f) * 12;
        using var shader = SKShader.CreatePerlinNoiseTurbulence(12f / 256, 12f / 256 * .65f, 2, 7, new SKSizeI(256, 256));
        using var tile = SKSurface.Create(new SKImageInfo(256, 256, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var brush = new SKPaint { Shader = shader };
        tile.Canvas.Clear(SKColors.Transparent); tile.Canvas.DrawRect(0, 0, 256, 256, brush);
        noise = tile.Snapshot(); NoiseTilesCreated++;
    }
    public void Dispose()
    { paint.Dispose(); canvas?.Dispose(); target?.Dispose(); noise?.Dispose(); swap.Dispose(); }
}
