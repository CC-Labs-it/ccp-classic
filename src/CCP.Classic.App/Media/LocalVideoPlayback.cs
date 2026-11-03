using System.Diagnostics;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace CCP.Classic.App.Media;

public sealed record VideoFrame(byte[] Pixels, int Width, int Height, long Sequence);

public interface ILocalVideoPlayback : IDisposable
{
    bool IsFinished { get; }
    string? LastError { get; }
    double PlayedSeconds => 0;
    AudioPlaybackSample? GetAudioSample() => null;
    void Start();
    void SetGain(double gain);
    void SetPaused(bool paused);
    bool TryCopyFrame(out VideoFrame? frame);
}

internal interface ILocalVideoDevice : IDisposable
{
    int Width { get; }
    int Height { get; }
    bool IsFinished { get; }
    bool IsPlaying { get; }
    long PositionMilliseconds { get; }
    bool TryCopyFrame(out VideoFrame? frame);
    void Start();
    void SetGain(double gain);
    void SetPaused(bool paused);
    void Refresh();
}

// Each player owns one worker. No native initialization, controls or teardown run on the UI thread.
public sealed class LocalVideoPlayback : ILocalVideoPlayback
{
    private readonly object gate = new();
    private readonly AutoResetEvent signal = new(false);
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<string, ILocalVideoDevice> factory;
    private readonly string path;
    private readonly double capSeconds;
    private ILocalVideoDevice? device;
    private bool started, disposed, paused;
    private double gain;
    private int finished;
    private long playedMilliseconds;
    private string? lastError;
    private AudioPlaybackSample? audioSample;
    public int Width => Volatile.Read(ref device)?.Width ?? 0;
    public int Height => Volatile.Read(ref device)?.Height ?? 0;
    public bool IsFinished => Volatile.Read(ref finished) != 0;
    public string? LastError => Volatile.Read(ref lastError);
    public double PlayedSeconds => Interlocked.Read(ref playedMilliseconds) / 1000d;
    public AudioPlaybackSample? GetAudioSample() { lock (gate) return disposed || paused ? null : audioSample; }
    internal Task Completion => completion.Task;

    public LocalVideoPlayback(string localPath, double capSeconds) : this(localPath, capSeconds, p => new NativeVideo(p)) { }
    internal LocalVideoPlayback(string localPath, double capSeconds, Func<string, ILocalVideoDevice> factory)
    {
        path = localPath;
        this.capSeconds = double.IsFinite(capSeconds) ? Math.Clamp(capSeconds, 1, 1800) : 480;
        this.factory = factory;
    }

    public void Start()
    {
        lock (gate)
        {
            if (started || disposed) return;
            started = true;
        }
        new Thread(Run) { IsBackground = true, Name = "CCP Classic video" }.Start();
    }
    public void SetGain(double value)
    {
        lock (gate) { if (disposed) return; gain = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0; signal.Set(); }
    }
    public void SetPaused(bool value)
    {
        lock (gate) { if (disposed) return; paused = value; signal.Set(); }
    }
    public bool TryCopyFrame(out VideoFrame? frame)
    {
        frame = null;
        lock (gate) return !disposed && device is { } current && current.TryCopyFrame(out frame);
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true; audioSample = null;
            Interlocked.Exchange(ref finished, 1);
            if (started) signal.Set();
            else { signal.Dispose(); completion.TrySetResult(); }
        }
    }
    private bool Current() { lock (gate) return !disposed; }

    private void Run()
    {
        ILocalVideoDevice? opened = null;
        try
        {
            if (!Current()) return;
            var local = LocalPath(path) ?? throw new IOException("Video must be an existing local file.");
            // Validation and native opening can both outlive a panic request.
            if (!Current()) return;
            opened = factory(local);
            if (!Current()) return;
            double initialGain;
            lock (gate) { device = opened; initialGain = gain; }
            opened.SetGain(initialGain);
            if (!Current()) return;
            opened.Start();
            var clock = new VideoPlaybackAllowance();
            var elapsed = Stopwatch.StartNew();
            double appliedGain = double.NaN;
            bool? appliedPause = null;
            while (Current())
            {
                double nextGain;
                bool nextPause;
                lock (gate) { nextGain = gain; nextPause = paused; }
                if (nextGain != appliedGain) { opened.SetGain(nextGain); appliedGain = nextGain; }
                if (nextPause != appliedPause) { opened.SetPaused(nextPause); appliedPause = nextPause; }
                opened.Refresh();
                lock (gate) if (!disposed) audioSample = new(path, Math.Max(0, opened.PositionMilliseconds) / 1000d,
                    appliedGain, !nextPause && opened.IsPlaying, TimeProvider.System.GetTimestamp());
                clock.Observe(opened.PositionMilliseconds, elapsed.ElapsedMilliseconds, !nextPause && (opened.IsPlaying || opened.IsFinished));
                Interlocked.Exchange(ref playedMilliseconds, clock.PlayedMilliseconds);
                if (opened.IsFinished || clock.PlayedMilliseconds >= capSeconds * 1000) break;
                signal.WaitOne(25);
            }
        }
        catch (Exception error) { if (Current()) Volatile.Write(ref lastError, error.Message); }
        finally
        {
            lock (gate) { device = null; audioSample = null; }
            try { opened?.Dispose(); } catch (Exception error) { if (Current()) Volatile.Write(ref lastError, error.Message); }
            Interlocked.Exchange(ref finished, 1);
            lock (gate) { disposed = true; signal.Dispose(); }
            completion.TrySetResult();
        }
    }

    internal static string? LocalPath(string? value)
    {
        if (!CCP.Classic.Core.MediaLibrary.IsLocalPath(value)) return null;
        try
        {
            var full = Path.GetFullPath(value!);
            if (Path.GetExtension(full).ToLowerInvariant() is not (".mp4" or ".m4v" or ".mov" or ".mkv" or ".webm" or ".avi")) return null;
            if (OperatingSystem.IsWindows() && new DriveInfo(Path.GetPathRoot(full)!).DriveType == DriveType.Network) return null;
            var ancestors = new Stack<string>();
            for (string? current = full; current is not null; current = Path.GetDirectoryName(current)) ancestors.Push(current);
            foreach (var ancestor in ancestors)
                if ((File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0) return null;
            return File.Exists(full) ? full : null;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException) { return null; }
    }

    internal static string? Demux(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 12 && header.Slice(4, 4).SequenceEqual("ftyp"u8)) return "mp4";
        if (header.Length >= 4 && header[..4].SequenceEqual(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 })) return "mkv";
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header.Slice(8, 4).SequenceEqual("AVI "u8)) return "avi";
        return null;
    }
    internal static (int Width, int Height) FrameSize(uint width, uint height, uint sarNum = 1, uint sarDen = 1, bool rotated = false)
    {
        if (width == 0 || height == 0) return (0, 0);
        double displayWidth = width * (sarNum > 0 && sarDen > 0 ? (double)sarNum / sarDen : 1), displayHeight = height;
        if (rotated) (displayWidth, displayHeight) = (displayHeight, displayWidth);
        var scale = Math.Min(1, Math.Min(960d / displayWidth, 540d / displayHeight));
        return (Math.Max(1, (int)Math.Round(displayWidth * scale)), Math.Max(1, (int)Math.Round(displayHeight * scale)));
    }
    internal static string[] Options(bool windows, string demux)
    {
        var options = new List<string> { "--quiet", "--no-media-library", "--no-lua", "--no-metadata-network-access",
            "--no-sub-autodetect-file", "--no-spu", "--no-osd", "--no-video-title-show", "--no-mkv-use-ordered-chapters",
            "--no-mkv-use-chapter-codec", "--no-mkv-preload-local-dir", "--avcodec-hw=none", "--avcodec-threads=2", "--rate=1", "--demux=" + demux + ",none" };
        if (windows) options.Add("--aout=directsound");
        return options.ToArray();
    }

    private sealed class NativeVideo : ILocalVideoDevice
    {
        private readonly SemaphoreSlim pixels = new(1, 1);
        private FileStream? stream;
        private StreamMediaInput? input;
        private LibVLC? vlc;
        private LibVLCSharp.Shared.Media? media;
        private MediaPlayer? player;
        private IntPtr allocation, buffer;
        private int width, height, pitch, ended, playingEdge;
        private long sequence, copiedSequence;
        private long watchedSequence, lastFrameAt = Environment.TickCount64;
        private string? error;
        private double currentGain;
        private bool pause;
        public int Width => Volatile.Read(ref width);
        public int Height => Volatile.Read(ref height);
        public bool IsFinished => Volatile.Read(ref ended) != 0;
        public bool IsPlaying => player?.State == VLCState.Playing;
        public long PositionMilliseconds => player?.Time ?? -1;

        public NativeVideo(string path)
        {
            try
            {
                stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                Span<byte> header = stackalloc byte[12];
                var count = stream.Read(header);
                stream.Position = 0;
                var demux = Demux(header[..count]) ?? throw new IOException("Unsupported video container.");
                NativeAudioDevice.EnsureInitialized();
                vlc = new LibVLC(Options(OperatingSystem.IsWindows(), demux));
                // No native path, playlist expansion, sibling lookup or URL resolution is supplied.
                input = new StreamMediaInput(stream);
                media = new LibVLCSharp.Shared.Media(vlc, input);
                media.SubItemAdded += (_, _) => { error = "Reference playlists are unsupported."; Interlocked.Exchange(ref ended, 1); };
                player = new MediaPlayer(media) { EnableKeyInput = false, EnableMouseInput = false };
                player.SetVideoCallbacks(Lock, Unlock, Display);
                player.SetVideoFormatCallbacks(Format, Cleanup);
                player.Playing += (_, _) => Interlocked.Exchange(ref playingEdge, 1);
                player.EndReached += (_, _) => Interlocked.Exchange(ref ended, 1);
                player.EncounteredError += (_, _) => { error = "Video playback failed."; Interlocked.Exchange(ref ended, 1); };
            }
            catch { Dispose(); throw; }
        }
        public void Start()
        {
            player!.Volume = NativeAudioDevice.PlayerVolume(currentGain);
            player.SetRate(1);
            if (!player.Play()) throw new InvalidOperationException("Video playback could not start.");
        }
        public void SetGain(double value) { currentGain = value; if (player is not null) player.Volume = NativeAudioDevice.PlayerVolume(value); }
        public void SetPaused(bool value) { pause = value; player?.SetPause(value); }
        public void Refresh()
        {
            if (Volatile.Read(ref error) is { } failure) throw new InvalidOperationException(failure);
            var next = Volatile.Read(ref sequence);
            if (pause || next != watchedSequence) { watchedSequence = next; lastFrameAt = Environment.TickCount64; }
            else if (!IsFinished && Environment.TickCount64 - lastFrameAt > 15_000)
                throw new InvalidOperationException("Video did not produce frames.");
            if (Interlocked.Exchange(ref playingEdge, 0) == 0) return;
            player!.SetRate(1);
            player.Volume = NativeAudioDevice.PlayerVolume(currentGain);
            if (pause) player.SetPause(true);
        }
        private uint Format(ref IntPtr opaque, IntPtr chroma, ref uint w, ref uint h, ref uint pitches, ref uint lines)
        {
            pixels.Wait();
            try
            {
                Free();
                // Callback dimensions can include codec padding. Track dimensions describe the visible image.
                var track = media?.Tracks.FirstOrDefault(item => item.TrackType == TrackType.Video);
                if (track is { TrackType: TrackType.Video } actual && actual.Data.Video.Width > 0 && actual.Data.Video.Height > 0)
                {
                    var video = actual.Data.Video;
                    (width, height) = FrameSize(video.Width, video.Height, video.SarNum, video.SarDen, (int)video.Orientation >= 4);
                }
                else (width, height) = FrameSize(w, h);
                if (width == 0 || height == 0) return 0;
                w = (uint)width; h = (uint)height;
                pitch = (width * 4 + 31) & ~31;
                pitches = (uint)pitch; lines = (uint)((height + 31) & ~31);
                allocation = Marshal.AllocHGlobal(checked(pitch * (int)lines + 31));
                buffer = new IntPtr((allocation.ToInt64() + 31) & ~31L);
                Marshal.Copy("RV32"u8.ToArray(), 0, chroma, 4);
                return 1;
            }
            catch (Exception failure) { error = failure.Message; return 0; }
            finally { pixels.Release(); }
        }
        private IntPtr Lock(IntPtr opaque, IntPtr planes)
        {
            pixels.Wait();
            Marshal.WriteIntPtr(planes, buffer);
            return IntPtr.Zero;
        }
        private void Unlock(IntPtr opaque, IntPtr picture, IntPtr planes) => pixels.Release();
        private void Display(IntPtr opaque, IntPtr picture) => Interlocked.Increment(ref sequence);
        private void Cleanup(ref IntPtr opaque) { pixels.Wait(); try { Free(); } finally { pixels.Release(); } }
        private void Free() { if (allocation != IntPtr.Zero) Marshal.FreeHGlobal(allocation); allocation = buffer = IntPtr.Zero; }
        public bool TryCopyFrame(out VideoFrame? frame)
        {
            frame = null;
            if (!pixels.Wait(0)) return false;
            try
            {
                var next = Volatile.Read(ref sequence);
                if (buffer == IntPtr.Zero || next == copiedSequence) return false;
                var data = new byte[width * height * 4];
                for (var row = 0; row < height; row++) Marshal.Copy(buffer + row * pitch, data, row * width * 4, width * 4);
                for (var alpha = 3; alpha < data.Length; alpha += 4) data[alpha] = 255;
                copiedSequence = next;
                frame = new(data, width, height, next);
                return true;
            }
            finally { pixels.Release(); }
        }
        public void Dispose()
        {
            try { player?.Stop(); } catch { }
            foreach (var resource in new IDisposable?[] { player, media, input, stream, vlc })
                try { resource?.Dispose(); } catch { }
            player = null;
            IntPtr opaque = IntPtr.Zero;
            Cleanup(ref opaque);
        }
    }
}

internal sealed class VideoPlaybackAllowance
{
    private long? position;
    private long observedAt;
    public long PlayedMilliseconds { get; private set; }
    public void Observe(long mediaMilliseconds, long monotonicMilliseconds, bool playing)
    {
        if (!playing || mediaMilliseconds < 0) { position = null; return; }
        if (position is { } previous && mediaMilliseconds == previous) return;
        if (position is { } prior)
        {
            var delta = mediaMilliseconds - prior;
            var elapsed = Math.Max(0, monotonicMilliseconds - observedAt);
            // Initial seeks and discontinuities never spend the playback allowance.
            if (delta > 0 && delta <= elapsed * 1.25 + 250) PlayedMilliseconds += delta;
        }
        position = mediaMilliseconds;
        observedAt = monotonicMilliseconds;
    }
}
