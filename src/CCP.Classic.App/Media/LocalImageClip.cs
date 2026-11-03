using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace CCP.Classic.App.Media;

// SKCodec composites GIF disposal and transparency before downsampling each complete frame.
// Decode runs on the library's single worker. Rendering only selects immutable native bitmaps.
internal sealed class LocalImageClip : IImage, IDisposable
{
    internal const long MaximumBytes = 16 * 1024 * 1024;
    private readonly Bitmap[] frames;
    private readonly double[] ends;
    private readonly int repetitions;
    private bool disposed;
    private int frame;
    public Size Size => disposed ? default : frames[0].Size;
    internal int FrameCount => frames.Length;
    internal int FrameIndex => frame;
    internal long DecodedBytes { get; }
    internal double? Brightness { get; }

    internal LocalImageClip(Bitmap still, double? brightness = null) : this([still], [1], 0, brightness) { }
    private LocalImageClip(Bitmap[] frames, double[] ends, int repetitions, double? brightness)
    {
        this.frames = frames; this.ends = ends; this.repetitions = repetitions;
        Brightness = brightness;
        DecodedBytes = frames.Sum(item => (long)item.PixelSize.Width * item.PixelSize.Height * 4);
    }

    internal void Advance(double seconds)
    {
        if (disposed || frames.Length <= 1 || !double.IsFinite(seconds)) return;
        seconds = Math.Max(0, seconds);
        if (repetitions >= 0 && seconds >= ends[^1] * (repetitions + 1d)) { frame = frames.Length - 1; return; }
        var position = seconds % ends[^1];
        frame = Array.FindIndex(ends, end => position < end);
        if (frame < 0) frame = frames.Length - 1;
    }
    public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
    { if (!disposed) context.DrawImage(frames[frame], sourceRect, destRect); }

    internal SKBitmap[] CopyNativeFrames(CancellationToken cancellationToken)
    {
        var copies = new List<SKBitmap>();
        try
        {
            foreach (var bitmap in frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var copy = new SKBitmap(bitmap.PixelSize.Width, bitmap.PixelSize.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                copies.Add(copy);
                bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), copy.GetPixels(), copy.RowBytes * copy.Height, copy.RowBytes);
                copy.SetImmutable();
            }
            return copies.ToArray();
        }
        catch { foreach (var copy in copies) copy.Dispose(); throw; }
    }
    internal double[] FrameEnds => ends.ToArray();

    internal static LocalImageClip Decode(string path, CancellationToken cancellationToken = default)
        => Decode(path, cancellationToken, MaximumBytes, 1024);

    internal static LocalImageClip Decode(string path, CancellationToken cancellationToken,
        long maximumBytes, int maximumDimension)
    {
        cancellationToken.ThrowIfCancellationRequested();
        path = LocalImageLibrary.LocalPath(path) ?? throw new IOException("Image is unavailable");
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > 64 * 1024 * 1024) throw new IOException("Image is too large");
        using var codec = SKCodec.Create(new SKManagedStream(stream, disposeManagedStream: false));
        if (codec is null) throw new IOException("Unsupported image");
        var width = codec.Info.Width; var height = codec.Info.Height;
        if (width <= 0 || height <= 0 || (long)width * height > 64_000_000) throw new IOException("Image dimensions are too large");
        if (codec.FrameCount <= 1)
        {
            var factor = Math.Min(1, maximumDimension / (float)Math.Max(width, height)); var dimensions = codec.GetScaledDimensions(factor);
            using var decoded = new SKBitmap(new SKImageInfo(dimensions.Width, dimensions.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            if (codec.GetPixels(decoded.Info, decoded.GetPixels()) != SKCodecResult.Success) throw new IOException("Image could not be decoded");
            using var scaledStill = new SKBitmap(new SKImageInfo(Math.Max(1, (int)(width * factor)), Math.Max(1, (int)(height * factor)), SKColorType.Bgra8888, SKAlphaType.Premul));
            if (!decoded.ScalePixels(scaledStill.PeekPixels(), new SKSamplingOptions(SKFilterMode.Linear))) throw new IOException("Image could not be scaled");
            return new(new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, scaledStill.GetPixels(),
                new(scaledStill.Width, scaledStill.Height), new(96, 96), scaledStill.RowBytes), ImageLuminance.Sample(scaledStill));
        }
        var count = codec.FrameCount;
        if (count > 300 || (long)width * height > 16_000_000 || (long)width * height * count > 1_000_000_000)
            throw new IOException("Animation exceeds its frame budget");
        var scale = Math.Min(1, Math.Min(maximumDimension / (double)Math.Max(width, height),
            Math.Sqrt(maximumBytes / (4d * width * height * count))));
        var outputWidth = Math.Max(1, (int)(width * scale)); var outputHeight = Math.Max(1, (int)(height * scale));
        using var canvas = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var scaled = new SKBitmap(new SKImageInfo(outputWidth, outputHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var painter = new SKCanvas(scaled);
        var frames = new List<Bitmap>(); var ends = new double[count]; var info = codec.FrameInfo;
        try
        {
            double end = 0; double? brightness = null;
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var prior = index > 0 && info[index - 1].DisposalMethod != SKCodecAnimationDisposalMethod.RestorePrevious ? index - 1 : -1;
                if (codec.GetPixels(canvas.Info, canvas.GetPixels(), new SKCodecOptions(index, prior)) != SKCodecResult.Success)
                    throw new IOException("Animation frame could not be decoded");
                painter.Clear(SKColors.Transparent);
                painter.DrawBitmap(canvas, new SKRect(0, 0, outputWidth, outputHeight)); painter.Flush();
                if (index == 0) brightness = ImageLuminance.Sample(scaled);
                frames.Add(new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, scaled.GetPixels(),
                    new(outputWidth, outputHeight), new(96, 96), scaled.RowBytes));
                ends[index] = end += info[index].Duration > 0 ? info[index].Duration / 1000d : .1;
            }
            return new(frames.ToArray(), ends, codec.RepetitionCount, brightness);
        }
        catch { foreach (var bitmap in frames) bitmap.Dispose(); throw; }
    }
    public void Dispose()
    { if (disposed) return; disposed = true; foreach (var bitmap in frames) bitmap.Dispose(); }
}
