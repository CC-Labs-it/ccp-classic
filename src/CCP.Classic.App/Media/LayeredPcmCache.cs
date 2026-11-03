using System.Runtime.InteropServices;

namespace CCP.Classic.App.Media;

// Private, session-scoped decoded PCM. Never source/starter media or a package input.
internal sealed class LayeredPcmCache : IDisposable
{
    internal const long MaximumBytes = 8L * 1024 * 1024 * 1024, MaximumTrackBytes = 4L * 1024 * 1024 * 1024;
    private const string Marker = "classic-pcm-owner";
    private readonly object gate = new();
    private readonly string folder;
    private readonly FileStream owner;
    private readonly Func<bool> resources;
    private readonly long limit;
    private long reserved;
    private readonly List<LayeredPcmTrack> tracks = [];
    internal LayeredPcmCache(string? root = null, long limit = MaximumBytes, Func<bool>? resources = null)
    {
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCP Classic", "AudioLayers");
        Directory.CreateDirectory(root);
        if (!LocalFolderLauncher.IsVerified(root)) throw new IOException("Local audio storage is unavailable");
        Cleanup(root);
        folder = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        owner = new(Path.Combine(folder, Marker), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
        this.limit = Math.Min(MaximumBytes, limit);
        this.resources = resources ?? (() =>
        {
            try
            {
                AnalysisProcessBudget.RequireHeadroom();
                return new DriveInfo(Path.GetPathRoot(folder)!).AvailableFreeSpace > 512L * 1024 * 1024;
            }
            catch (IOException) { return false; }
        });
    }
    internal LayeredPcmTrack Create(Guid id, string path, Action changed)
    {
        lock (gate)
        {
            if (tracks.Count >= 8) throw new IOException("Audio layer limit reached");
            var track = new LayeredPcmTrack(id, path, Path.Combine(folder, Guid.NewGuid().ToString("N") + ".pcm"), this, changed);
            tracks.Add(track); return track;
        }
    }
    internal bool Reserve(int bytes)
    {
        lock (gate)
        {
            if (bytes < 0 || bytes > limit - reserved || !resources()) return false;
            reserved += bytes; return true;
        }
    }
    public void Dispose()
    {
        foreach (var track in tracks) track.Dispose(); owner.Dispose(); Remove(folder);
    }
    private static void Cleanup(string root)
    {
        foreach (var candidate in Directory.EnumerateDirectories(root).Take(128))
        {
            if (!Guid.TryParseExact(Path.GetFileName(candidate), "N", out _) || !LocalFolderLauncher.IsVerified(candidate)) continue;
            try
            {
                using (File.Open(Path.Combine(candidate, Marker), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                Remove(candidate);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    private static void Remove(string folder)
    {
        try
        {
            if (!LocalFolderLauncher.IsVerified(folder)) return;
            var files = Directory.EnumerateFiles(folder).Take(10).ToArray();
            if (files.Length > 9 || Directory.EnumerateDirectories(folder).Any() || files.Any(file => Path.GetFileName(file) != Marker
                && !(Path.GetExtension(file) == ".pcm" && Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out _)))) return;
            foreach (var file in files)
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) return;
                File.Delete(file);
            }
            Directory.Delete(folder);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal sealed class LayeredPcmTrack : IDisposable
{
    internal const int Rate = 44100, Channels = 2;
    private readonly object gate = new();
    private readonly FileStream file;
    private readonly LayeredPcmCache cache;
    private readonly Action changed;
    private long bytes;
    private short[]? readCache;
    private long cacheAt = -1;
    private int cacheCount;
    internal long DiskReads { get; private set; }
    private bool complete, failed, disposed;
    internal Guid Id { get; }
    internal string Path { get; }
    internal long Frames { get { lock (gate) return bytes / 4; } }
    internal bool Complete { get { lock (gate) return complete; } }
    internal bool Failed { get { lock (gate) return failed; } }
    internal bool Ready { get { lock (gate) return !failed && (bytes >= Rate || complete && bytes > 0); } }
    internal long ConsumedFrames;
    internal LayeredPcmTrack(Guid id, string path, string temporary, LayeredPcmCache cache, Action changed)
    {
        Id = id; Path = path; this.cache = cache; this.changed = changed;
        file = new(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete, 1, FileOptions.RandomAccess);
    }
    internal bool Append(ReadOnlySpan<short> samples)
    {
        lock (gate)
        {
            if (disposed || complete || failed) return false;
            var size = samples.Length * 2;
            if (samples.Length % 2 != 0 || size > LayeredPcmCache.MaximumTrackBytes - bytes || !cache.Reserve(size))
            { failed = true; changed(); return false; }
            try { RandomAccess.Write(file.SafeFileHandle, MemoryMarshal.AsBytes(samples), bytes); bytes += size; }
            catch (IOException) { failed = true; changed(); return false; }
        }
        changed(); return true;
    }
    internal int Read(long sample, Span<short> destination)
    {
        lock (gate)
        {
            if (disposed || failed || sample < 0) return 0;
            readCache ??= new short[32768];
            try
            {
                // Short completed clips repeat in memory, including one-frame loops.
                if (complete && bytes is > 0 and <= 65536)
                {
                    if (cacheAt != 0 || cacheCount != bytes / 2) { cacheAt = -1; cacheCount = 0; }
                    FillCache(0);
                    for (var i = 0; i < destination.Length; i++) destination[i] = readCache[(int)((sample + i) % cacheCount)];
                    return destination.Length;
                }
                var count = (int)Math.Min(destination.Length, Math.Max(0, bytes / 2 - sample)); var copied = 0;
                while (copied < count)
                {
                    FillCache(sample + copied);
                    var offset = (int)(sample + copied - cacheAt); var take = Math.Min(count - copied, cacheCount - offset);
                    if (take <= 0) break;
                    readCache.AsSpan(offset, take).CopyTo(destination[copied..]); copied += take;
                }
                return copied;
            }
            catch (IOException) { failed = true; changed(); return 0; }
        }
    }
    private void FillCache(long sample)
    {
        if (sample >= cacheAt && sample < cacheAt + cacheCount) return;
        cacheAt = sample; cacheCount = 0;
        var cached = readCache!;
        var target = MemoryMarshal.AsBytes(cached.AsSpan(0, (int)Math.Min(cached.Length, bytes / 2 - sample)));
        while (cacheCount * 2 < target.Length)
        {
            var count = RandomAccess.Read(file.SafeFileHandle, target[(cacheCount * 2)..], sample * 2 + cacheCount * 2);
            DiskReads++; if (count == 0) break; cacheCount += count / 2;
        }
    }
    internal void Finish(bool success)
    { lock (gate) { if (disposed) return; complete = true; failed |= !success || bytes == 0; } changed(); }
    public void Dispose() { lock (gate) { if (disposed) return; disposed = true; failed = true; file.Dispose(); } }
}
