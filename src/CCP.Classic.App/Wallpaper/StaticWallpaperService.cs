using System.Security.Cryptography;
using System.Text.Json;
using CCP.Classic.App.Media;
using CCP.Classic.Core;
using SkiaSharp;

namespace CCP.Classic.App.Wallpaper;

public enum WallpaperStatus { Idle, Active, Unavailable, RestoreFailed, Recovered }

public sealed class StaticWallpaperService : IDisposable, IAsyncDisposable
{
    private sealed record Journal(string Kind, int Version, string Id, WallpaperSnapshot Original, string Backup, string Hash);
    private readonly object gate = new();
    private readonly SemaphoreSlim lane = new(1, 1);
    private readonly IWallpaperNative native;
    private readonly Func<string, bool> verify;
    private readonly string directory, journalPath;
    private FileStream? lease;
    private Task<bool> completion = Task.FromResult(true);
    private long generation;
    private bool disposed;
    public WallpaperStatus Status { get; private set; }
    public Task Completion { get { lock (gate) return completion; } }
    public event Action<WallpaperStatus>? StatusChanged;

    public StaticWallpaperService(IWallpaperNative? native = null, string? dataDirectory = null, Func<string, bool>? imageValidator = null)
    {
        this.native = native ?? (OperatingSystem.IsWindows() ? new WindowsWallpaperNative() : new UnsupportedWallpaperNative());
        verify = imageValidator ?? VerifyImage;
        directory = Path.GetFullPath(dataDirectory ?? Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath)!, "wallpaper"));
        journalPath = Path.Combine(directory, "restore.json");
    }

    public Task<bool> StartAsync(string selectedImage, CancellationToken token = default)
    {
        lock (gate)
        {
            if (disposed) return Task.FromResult(false);
            var version = Interlocked.Increment(ref generation);
            return completion = Queue(() => Start(selectedImage, version, token), token);
        }
    }
    public Task<bool> StopAsync()
    {
        lock (gate) { Interlocked.Increment(ref generation); return completion = Queue(() => Restore(recovery: false)); }
    }
    public Task<bool> RecoverAsync()
    {
        lock (gate) { Interlocked.Increment(ref generation); return completion = Queue(() => Restore(recovery: true)); }
    }
    private async Task<bool> Queue(Func<bool> operation, CancellationToken token = default)
    {
        await lane.WaitAsync(token).ConfigureAwait(false);
        try { return await Task.Run(operation, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { Report(File.Exists(journalPath) ? WallpaperStatus.RestoreFailed : WallpaperStatus.Unavailable); return false; }
        finally { lane.Release(); }
    }

    private bool Start(string selected, long version, CancellationToken token)
    {
        if (!native.IsSupported) { Report(WallpaperStatus.Unavailable); return false; }
        if (version != Interlocked.Read(ref generation)) return false;
        var path = LocalImageLibrary.LocalPath(selected);
        if (path is null || !SupportedImage(path) || !verify(path)) { Report(WallpaperStatus.Unavailable); return false; }
        AcquireLease();
        if (File.Exists(journalPath) && !Restore(recovery: true)) return false;
        AcquireLease(); token.ThrowIfCancellationRequested();
        if (version != Interlocked.Read(ref generation)) return false;
        var original = native.Capture();
        if (!original.CanReplace || !ValidSnapshot(original)) { ReleaseLease(); Report(WallpaperStatus.Unavailable); return false; }
        var id = Guid.NewGuid().ToString("N"); var backup = ""; var hash = "";
        if (!original.SolidColor)
        {
            var source = LocalImageLibrary.LocalPath(original.Path);
            if (source is null || !SupportedImage(source) || !verify(source)) { ReleaseLease(); Report(WallpaperStatus.Unavailable); return false; }
            backup = "original-" + id + Path.GetExtension(source).ToLowerInvariant();
            using var input = File.OpenRead(source);
            if (input.Length > 64 * 1024 * 1024) throw new IOException("Original wallpaper is too large");
            using (var output = new FileStream(Path.Combine(directory, backup), FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.WriteThrough)) { input.CopyTo(output); output.Flush(flushToDisk: true); }
            hash = Hash(Path.Combine(directory, backup));
        }
        WriteJournal(new("CCP Classic wallpaper", 1, id, original, backup, hash));
        if (token.IsCancellationRequested || version != Interlocked.Read(ref generation)) { Restore(recovery: false); return false; }
        var applied = native.Apply(path);
        if (!applied || version != Interlocked.Read(ref generation) || token.IsCancellationRequested)
        { Restore(recovery: false); return false; }
        Report(WallpaperStatus.Active); return true;
    }

    private bool Restore(bool recovery)
    {
        if (!native.IsSupported) { Report(WallpaperStatus.Unavailable); return false; }
        if (!File.Exists(journalPath)) { ReleaseLease(); Report(WallpaperStatus.Idle); return true; }
        AcquireLease();
        var bytes = new byte[16385]; int count;
        using (var stream = File.OpenRead(journalPath)) count = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        if (count > 16384) throw new IOException("Wallpaper recovery record is too large");
        var record = JsonSerializer.Deserialize<Journal>(bytes.AsSpan(0, count), new JsonSerializerOptions { MaxDepth = 8 })
            ?? throw new IOException("Invalid wallpaper recovery record");
        if (record.Kind != "CCP Classic wallpaper" || record.Version != 1 || !Guid.TryParseExact(record.Id, "N", out _)
            || record.Original is null || !ValidSnapshot(record.Original)) throw new IOException("Invalid wallpaper recovery record");
        var target = ""; string? frozen = null;
        if (!record.Original.SolidColor)
        {
            if (record.Backup is null || !record.Backup.StartsWith("original-" + record.Id + ".", StringComparison.Ordinal)
                || record.Backup != Path.GetFileName(record.Backup) || !SupportedImage(record.Backup)) throw new IOException("Invalid wallpaper backup");
            frozen = LocalImageLibrary.LocalPath(Path.Combine(directory, record.Backup));
            if (frozen is null || record.Hash.Length != 64 || Hash(frozen) != record.Hash || !verify(frozen)) throw new IOException("Wallpaper backup cannot be verified");
            target = LocalImageLibrary.LocalPath(record.Original.Path) is { } own && verify(own) ? own : frozen;
        }
        if (!native.Restore(record.Original, target)) { Report(WallpaperStatus.RestoreFailed); return false; }
        File.Delete(journalPath);
        if (frozen is not null && !string.Equals(target, frozen, StringComparison.OrdinalIgnoreCase)) File.Delete(frozen);
        ReleaseLease(); Report(recovery ? WallpaperStatus.Recovered : WallpaperStatus.Idle); return true;
    }

    private static bool ValidSnapshot(WallpaperSnapshot original) => original.CanReplace
        && original.Style is "0" or "2" or "6" or "10" or "22" && original.Tile is "0" or "1"
        && (original.SolidColor ? original.Path == "" : MediaLibrary.IsLocalPath(original.Path));
    private void AcquireLease()
    {
        if (lease is not null) return;
        if (!MediaLibrary.IsLocalPath(directory) || OperatingSystem.IsWindows()
            && new DriveInfo(Path.GetPathRoot(directory)!).DriveType == DriveType.Network) throw new IOException("Wallpaper recovery must stay local");
        var ancestors = new Stack<string>();
        for (string? current = directory; current is not null; current = Path.GetDirectoryName(current)) ancestors.Push(current);
        foreach (var ancestor in ancestors)
        {
            try
            { if ((File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0) throw new IOException("Wallpaper recovery cannot follow links"); }
            catch (Exception failure) when (failure is FileNotFoundException or DirectoryNotFoundException) { break; }
        }
        Directory.CreateDirectory(directory);
        lease = new(Path.Combine(directory, "restore.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private void ReleaseLease() { lease?.Dispose(); lease = null; }
    private void WriteJournal(Journal record)
    {
        var temporary = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(record);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            File.Move(temporary, journalPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path); if (stream.Length > 64 * 1024 * 1024) throw new IOException("Wallpaper backup is too large");
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    private static bool SupportedImage(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp";
    private static bool VerifyImage(string path)
    {
        try
        {
            using var stream = File.OpenRead(path); if (stream.Length > 64 * 1024 * 1024) return false;
            using (var codec = SKCodec.Create(new SKManagedStream(stream, disposeManagedStream: false)))
                if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 64_000_000) return false;
            stream.Position = 0; using var image = SKBitmap.Decode(stream); return image is not null;
        }
        catch (Exception) { return false; }
    }
    private void Report(WallpaperStatus value) { Status = value; StatusChanged?.Invoke(value); }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true; Interlocked.Increment(ref generation);
            completion = Queue(() => { try { return Restore(recovery: false); } finally { ReleaseLease(); } });
        }
    }
    public async ValueTask DisposeAsync() { Dispose(); Task pending; lock (gate) pending = completion; await pending.ConfigureAwait(false); }
}
