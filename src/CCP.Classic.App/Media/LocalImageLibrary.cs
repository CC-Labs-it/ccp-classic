using System.Collections;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CCP.Classic.Core;
using SkiaSharp;

namespace CCP.Classic.App.Media;

// Proxies are cheap. Only requested images enter the bounded worker queue and native cache.
public sealed class LocalImageLibrary : IReadOnlyList<IImage>, IDisposable
{
    private readonly object gate = new();
    private readonly string[] paths;
    private readonly IImage[] proxies;
    private readonly Size?[] sizes;
    private readonly IImage fallback;
    private readonly Func<string, Bitmap> decode;
    private readonly int cacheCapacity, pendingCapacity;
    private readonly Queue<int> queue = new();
    private readonly HashSet<int> pending = [], failed = [];
    private readonly Dictionary<int, (Bitmap Image, LinkedListNode<int> Node)> cache = [];
    private readonly LinkedList<int> recent = new();
    private Task worker = Task.CompletedTask;
    private bool working, disposed;
    public int Count => proxies.Length;
    public IImage this[int index] => proxies[index];
    internal int CachedCount { get { lock (gate) return cache.Count; } }
    internal int PendingCount { get { lock (gate) return pending.Count; } }
    internal Task Completion { get { lock (gate) return worker; } }

    public LocalImageLibrary(IEnumerable<string> selectedPaths, IImage fallback)
        : this(selectedPaths, fallback, Decode) { }

    internal LocalImageLibrary(IEnumerable<string> selectedPaths, IImage fallback, Func<string, Bitmap> decode,
        int cacheCapacity = 12, int pendingCapacity = 12)
    {
        ArgumentNullException.ThrowIfNull(selectedPaths); ArgumentNullException.ThrowIfNull(fallback);
        ArgumentNullException.ThrowIfNull(decode);
        ArgumentOutOfRangeException.ThrowIfLessThan(cacheCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cacheCapacity, 12);
        ArgumentOutOfRangeException.ThrowIfLessThan(pendingCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pendingCapacity, 12);
        this.fallback = fallback; this.decode = decode;
        this.cacheCapacity = cacheCapacity; this.pendingCapacity = pendingCapacity;
        paths = selectedPaths.Where(MediaLibrary.IsLocalPath).Select(Path.GetFullPath)
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .Take(100000).ToArray();
        sizes = new Size?[paths.Length];
        proxies = Enumerable.Range(0, paths.Length).Select(index => (IImage)new ImageProxy(this, index)).ToArray();
    }

    private Size Size(int index)
    {
        lock (gate)
        {
            if (disposed) return default;
            Request(index);
            if (cache.TryGetValue(index, out var entry)) Touch(entry.Node);
            return sizes[index] ?? fallback.Size;
        }
    }

    private void Draw(int index, DrawingContext context, Rect source, Rect destination)
    {
        lock (gate)
        {
            if (disposed) return;
            Request(index);
            IImage image = fallback;
            if (cache.TryGetValue(index, out var entry)) { Touch(entry.Node); image = entry.Image; }
            var logical = sizes[index] ?? fallback.Size; var actual = image.Size;
            if (logical.Width <= 0 || logical.Height <= 0 || actual.Width <= 0 || actual.Height <= 0) return;
            var mapped = new Rect(source.X / logical.Width * actual.Width, source.Y / logical.Height * actual.Height,
                source.Width / logical.Width * actual.Width, source.Height / logical.Height * actual.Height);
            // Keep eviction outside the bitmap's draw call while it records the platform image reference.
            context.DrawImage(image, mapped, destination);
        }
    }

    private void Request(int index)
    {
        if (cache.ContainsKey(index) || failed.Contains(index) || pending.Contains(index) || pending.Count >= pendingCapacity) return;
        pending.Add(index); queue.Enqueue(index);
        if (working) return;
        working = true; worker = Task.Run(Work);
    }

    private void Touch(LinkedListNode<int> node) { recent.Remove(node); recent.AddLast(node); }

    private void Work()
    {
        while (true)
        {
            int index;
            lock (gate)
            {
                if (disposed || !queue.TryDequeue(out index)) { working = false; return; }
            }
            Bitmap? image = null;
            try
            {
                var path = LocalPath(paths[index]);
                lock (gate) { if (disposed) { working = false; return; } }
                if (path is not null) image = decode(path);
            }
            catch (Exception) { }
            lock (gate)
            {
                pending.Remove(index);
                if (disposed) { image?.Dispose(); working = false; return; }
                if (image is null) { failed.Add(index); continue; }
                sizes[index] = image.Size;
                cache.Add(index, (image, recent.AddLast(index)));
                while (cache.Count > cacheCapacity)
                {
                    var oldest = recent.First!; recent.RemoveFirst();
                    var evicted = cache[oldest.Value]; cache.Remove(oldest.Value); evicted.Image.Dispose();
                }
            }
        }
    }

    internal static string? LocalPath(string? value)
    {
        if (!MediaLibrary.IsLocalPath(value)) return null;
        try
        {
            var full = Path.GetFullPath(value!);
            if (Path.GetExtension(full).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp")) return null;
            if (OperatingSystem.IsWindows() && new DriveInfo(Path.GetPathRoot(full)!).DriveType == DriveType.Network) return null;
            var ancestors = new Stack<string>();
            for (string? current = full; current is not null; current = Path.GetDirectoryName(current)) ancestors.Push(current);
            foreach (var ancestor in ancestors)
                if ((File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0) return null;
            return File.Exists(full) ? full : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return null; }
    }

    private static Bitmap Decode(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 64 * 1024 * 1024) throw new IOException("Image is too large");
        int width, height;
        // The codec owns its wrapper; the wrapper borrows this same file handle for both header and decode.
        using (var codec = SKCodec.Create(new SKManagedStream(stream, disposeManagedStream: false)))
        {
            if (codec is null) throw new IOException("Unsupported image");
            width = codec.Info.Width; height = codec.Info.Height;
        }
        if (width <= 0 || height <= 0 || (long)width * height > 64_000_000) throw new IOException("Image dimensions are too large");
        stream.Position = 0;
        return width >= height ? Bitmap.DecodeToWidth(stream, Math.Min(1024, width))
            : Bitmap.DecodeToHeight(stream, Math.Min(1024, height));
    }

    internal static Bitmap DecodeThumbnail(string path) => Decode(LocalPath(path) ?? throw new IOException("Image is unavailable"));

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true; queue.Clear(); pending.Clear();
            foreach (var entry in cache.Values) entry.Image.Dispose();
            cache.Clear(); recent.Clear();
        }
    }

    public IEnumerator<IImage> GetEnumerator() => ((IEnumerable<IImage>)proxies).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    private sealed class ImageProxy(LocalImageLibrary owner, int index) : IImage
    {
        public Size Size => owner.Size(index);
        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect) => owner.Draw(index, context, sourceRect, destRect);
    }
}
