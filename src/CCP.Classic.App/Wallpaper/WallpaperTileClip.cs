using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CCP.Classic.App.Media;
using SkiaSharp;
using System.Runtime.InteropServices;

namespace CCP.Classic.App.Wallpaper;

internal sealed class WallpaperResourceException : IOException { }
internal sealed class WallpaperFrameBudget(Action? check = null)
{
    public const long MaximumBytes = 512L * 1024 * 1024;
    public long Used { get; private set; }
    public void Check(long allocation = 0)
    {
        try
        {
            if (check is not null) { check(); return; }
            AnalysisProcessBudget.RequireHeadroom();
            if (allocation <= 0) return;
            ulong total, available;
            if (OperatingSystem.IsWindows())
            {
                var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
                if (!GlobalMemoryStatusEx(ref memory)) throw new WallpaperResourceException();
                total = memory.TotalPhysical; available = memory.AvailablePhysical;
            }
            else
            {
                var values = File.ReadLines("/proc/meminfo").Take(128).Select(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    .Where(parts => parts.Length > 1 && parts[0] is "MemTotal:" or "MemAvailable:")
                    .ToDictionary(parts => parts[0], parts => ulong.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) * 1024);
                total = values["MemTotal:"]; available = values["MemAvailable:"];
            }
            if (available <= total / 4 + (ulong)allocation) throw new WallpaperResourceException();
        }
        catch (IOException) { throw new WallpaperResourceException(); }
    }
    public bool CanReserve(long bytes) => bytes > 0 && bytes <= MaximumBytes - Used;
    public void Reserve(long bytes)
    { Check(bytes); if (!CanReserve(bytes)) throw new WallpaperResourceException(); Used += bytes; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtended;
    }
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
}

// Public CCP DynamicFrameBudget: at most120 kept of900 source frames, with original delays.
internal sealed class WallpaperTileClip : IImage, IDisposable
{
    private readonly Bitmap[] frames;
    private readonly double[] ends;
    private int index;
    private bool disposed;
    public Size Size => disposed ? default : frames[0].Size;
    public int FrameCount => frames.Length;
    public int FrameIndex => index;
    public SKRect Focus { get; private init; }
    public long SourcePixels { get; private init; }
    public int SourceFrames { get; private init; }
    public static int Stride(int count) => Math.Max(1, (int)Math.Ceiling(count / 120d));
    private WallpaperTileClip(Bitmap[] frames, double[] ends) { this.frames = frames; this.ends = ends; }
    public bool Advance(double seconds)
    {
        if (disposed || frames.Length <= 1) return false;
        var previous = index; var position = Math.Max(0, seconds) % ends[^1];
        index = Array.FindIndex(ends, end => position < end); if (index < 0) index = frames.Length - 1;
        return previous != index;
    }
    public void Draw(DrawingContext context, Rect source, Rect destination)
    { if (!disposed) context.DrawImage(frames[index], source, destination); }
    public static WallpaperTileClip Decode(string path, bool animate, int cap, WallpaperFrameBudget budget, CancellationToken token, bool sharpen = false)
    {
        token.ThrowIfCancellationRequested(); budget.Check();
        path = LocalImageLibrary.LocalPath(path) ?? throw new IOException("Wallpaper image unavailable.");
        using var stream = File.OpenRead(path);
        if (stream.Length > 64 * 1024 * 1024) throw new IOException("Wallpaper image exceeds its source budget.");
        using var codec = SKCodec.Create(new SKManagedStream(stream, false));
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0
            || (long)codec.Info.Width * codec.Info.Height > 16_000_000) throw new IOException("Wallpaper image dimensions are unavailable.");
        var count = Math.Max(1, codec.FrameCount); animate &= count is > 1 and <= 900;
        var stride = animate ? Stride(count) : 1; var kept = animate ? (int)Math.Ceiling(count / (double)stride) : 1;
        var scale = Math.Min(1, cap / (double)Math.Max(codec.Info.Width, codec.Info.Height));
        var w = Math.Max(1, (int)(codec.Info.Width * scale)); var h = Math.Max(1, (int)(codec.Info.Height * scale));
        if (!budget.CanReserve((long)w * h * 4 * kept)) { animate = false; stride = kept = 1; }
        budget.Reserve((long)w * h * 4 * kept);
        budget.Check((long)codec.Info.Width * codec.Info.Height * 4 + (long)w * h * 4);
        using var canvas = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var scaled = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var painter = new SKCanvas(scaled);
        using var sharpenFilter = sharpen ? CollageComposer.HeroSharpen() : null;
        using var paint = new SKPaint { IsAntialias = true, ImageFilter = sharpenFilter };
        var output = new List<Bitmap>(); var ends = new List<double>(); var info = codec.FrameInfo;
        var last = animate ? count - 1 : Math.Min(count / 2, 120); var end = 0d;
        try
        {
            for (var frame = 0; frame <= last; frame++)
            {
                token.ThrowIfCancellationRequested(); budget.Check((long)w * h * 4);
                var prior = frame > 0 && info.Length > frame - 1 && info[frame - 1].DisposalMethod != SKCodecAnimationDisposalMethod.RestorePrevious ? frame - 1 : -1;
                var result = codec.GetPixels(canvas.Info, canvas.GetPixels(), new SKCodecOptions(frame, prior));
                if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) throw new IOException("Wallpaper image decode failed.");
                if (animate) end += info.Length > frame && info[frame].Duration > 0 ? info[frame].Duration / 1000d : .1;
                if (!animate && frame != last) continue;
                if (animate && frame % stride != 0) { if (ends.Count > 0) ends[^1] = end; continue; }
                painter.Clear(SKColors.Transparent); painter.DrawBitmap(canvas, new SKRect(0, 0, w, h), paint); painter.Flush();
                output.Add(new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, scaled.GetPixels(), new(w, h), new(96, 96), scaled.RowBytes));
                ends.Add(animate ? end : 1);
            }
            return new(output.ToArray(), ends.ToArray()) { Focus = CollageComposer.ComputeFocus(scaled), SourceFrames = count,
                SourcePixels = (long)codec.Info.Width * codec.Info.Height };
        }
        catch { foreach (var bitmap in output) bitmap.Dispose(); throw; }
    }
    internal SKBitmap CopyStill()
    {
        var size = frames[0].PixelSize; var result = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        frames[0].CopyPixels(new PixelRect(size), result.GetPixels(), result.ByteCount, result.RowBytes); return result;
    }
    public void Dispose() { if (disposed) return; disposed = true; foreach (var bitmap in frames) bitmap.Dispose(); }
}
