using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CCP.Classic.App.Effects;
using CCP.Classic.Core.Remix;
using SkiaSharp;

namespace CCP.Classic.App.Media;

// Native adapter for the public Jackpot builder. No browser, temporary movie or source copies.
internal sealed class RemixImage : IImage, IDisposable
{
    private readonly RemixStrip[] strips;
    private readonly RemixRenderer renderer;
    private WriteableBitmap? raster;
    private int frame = -1;
    private double lastSeconds, animationSeconds;
    private bool disposed;
    internal RemixScene Scene { get; }
    internal long DecodedBytes => strips.Distinct().Sum(strip => strip.Bytes);
    internal int PresentedFrames { get; private set; }
    public Size Size => disposed ? default : new(Scene.Width, Scene.Height);

    internal RemixImage(RemixScene scene, RemixStrip[] strips)
    { Scene = scene; this.strips = strips; renderer = new(scene, strips); }

    internal static RemixImage Prepare(IEnumerable<string> paths, int seed, CancellationToken cancellationToken)
    {
        var chosen = RemixPlan.SelectSources(paths, seed, path =>
        {
            var local = LocalImageLibrary.LocalPath(path);
            if (local is null) return long.MaxValue;
            return new FileInfo(local).Length;
        });
        if (chosen.Count == 0) throw new IOException("No local GIF is available");
        var decoded = new Dictionary<string, RemixStrip>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var path in chosen.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var clip = LocalImageClip.Decode(path, cancellationToken, 4 * 1024 * 1024, 480);
                decoded.Add(path, new(clip.CopyNativeFrames(cancellationToken), clip.FrameEnds));
            }
            var strips = chosen.Select(path => decoded[path]).ToArray();
            var scene = RemixScene.Create(unchecked((uint)seed), strips.Select(strip => strip.Aspect).ToArray());
            cancellationToken.ThrowIfCancellationRequested();
            return new(scene, strips);
        }
        catch { foreach (var strip in decoded.Values) strip.Dispose(); throw; }
    }

    internal void Advance(double seconds, bool motionAllowed)
    {
        if (disposed || !double.IsFinite(seconds)) return;
        var elapsed = Math.Max(0, seconds - lastSeconds); lastSeconds = Math.Max(lastSeconds, seconds);
        if (motionAllowed) animationSeconds += elapsed;
        var next = (int)(animationSeconds * RemixScene.Fps % RemixScene.Frames);
        if (next == frame) return;
        raster ??= new(new(Scene.Width, Scene.Height), new(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var pixels = raster.Lock();
        using var surface = SKSurface.Create(new SKImageInfo(Scene.Width, Scene.Height, SKColorType.Bgra8888, SKAlphaType.Premul), pixels.Address, pixels.RowBytes);
        renderer.Draw(surface, next); surface.Canvas.Flush(); frame = next; PresentedFrames++;
    }
    public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
    { if (!disposed && raster is not null) context.DrawImage(raster, sourceRect, destRect); }
    public void Dispose()
    {
        if (disposed) return; disposed = true; raster?.Dispose(); raster = null; renderer.Dispose();
        foreach (var strip in strips.Distinct()) strip.Dispose();
    }
}

internal sealed class RemixStrip(SKBitmap[] frames, double[] ends) : IDisposable
{
    private readonly int length = Math.Clamp((int)Math.Ceiling(ends[^1] * RemixScene.Fps), 1, RemixScene.Frames);
    internal double Aspect => frames[0].Width / (double)frames[0].Height;
    internal long Bytes => frames.Sum(bitmap => (long)bitmap.RowBytes * bitmap.Height);
    internal SKBitmap At(int frame)
    {
        var at = ((frame % length) + length) % length / (double)RemixScene.Fps;
        var index = Array.FindIndex(ends, end => at < end);
        return frames[index < 0 ? frames.Length - 1 : index];
    }
    public void Dispose() { foreach (var bitmap in frames) bitmap.Dispose(); }
}
