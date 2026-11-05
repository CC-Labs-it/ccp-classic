using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Classic.App.Media;
using CCP.Classic.Core;

namespace CCP.Classic.App.Wallpaper;

public sealed partial class DynamicWallpaperService : IDisposable
{
    private sealed record Host(Window Window, WallpaperView View, WallpaperMonitor Monitor)
    { public WallpaperDesktopState State = new(true); }
    private readonly List<Host> hosts = [];
    private readonly IDesktopWallpaperAdapter adapter;
    private readonly Func<string, ILocalVideoPlayback> createVideo;
    private readonly TimeProvider clock;
    private readonly DispatcherTimer timer;
    private LocalImageLibrary? images;
    private ILocalVideoPlayback? video;
    private WriteableBitmap? bitmap;
    private string? videoPath;
    private IImage? image;
    private TaskCompletionSource<bool> ready = NewCompletion(), entered = NewCompletion(), stopped = NewCompletion();
    private CancellationTokenRegistration cancellation;
    private long started, lastDesktopPoll, generation, sequence = -1;
    private double alpha, fromAlpha, targetAlpha, fadeStarted, fadeDuration;
    private bool gentle, reduced, performance, running, ending, disposed;
    public Task<bool> Ready => ready.Task;
    public Task<bool> EntranceCompleted => entered.Task;
    public Task Completion => stopped.Task;
    public WallpaperStatus Status { get; private set; }
    public event Action<WallpaperStatus>? StatusChanged;
    internal int HostCount => hosts.Count;
    internal long PresentedFrames { get; private set; }

    public DynamicWallpaperService(IDesktopWallpaperAdapter? adapter = null, Func<string, ILocalVideoPlayback>? playbackFactory = null, TimeProvider? clock = null)
    {
        this.adapter = adapter ?? (OperatingSystem.IsWindows() ? new WindowsDesktopWallpaperAdapter() : new UnsupportedDesktopWallpaperAdapter());
        createVideo = playbackFactory ?? (path => new LocalVideoPlayback(path, 1800)); this.clock = clock ?? TimeProvider.System;
        timer = new(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, (_, _) => Tick());
        ready.TrySetResult(false); entered.TrySetResult(false); stopped.TrySetResult(true);
    }

    public bool Start(IReadOnlyList<WallpaperMonitor> monitors, string? imagePath, IImage fallback, string? videoPath = null,
        bool gentle = false, bool reducedMotion = false, bool performanceMode = false, CancellationToken token = default)
    {
        if (!Prepare(monitors, fallback, gentle, reducedMotion, performanceMode, token)) return false;
        if (videoPath is not null && !MediaLibrary.IsLocalPath(videoPath) || imagePath is null && videoPath is null)
        { Fail(); return false; }
        this.videoPath = videoPath;
        try
        {
            if (imagePath is not null)
            { images = new([imagePath], fallback); if (images.Count == 0) { Fail(); return false; } image = images[0]; _ = image.Size; }
            if (!AttachHosts(monitors)) { Fail(); return false; }
            if (videoPath is not null) { video = createVideo(videoPath); video.SetGain(0); video.Start(); }
            timer.Start(); Tick(); return true;
        }
        catch (Exception) { Fail(); return false; }
    }

    public void Configure(bool gentle, bool reducedMotion, bool performanceMode)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (gentle && !this.gentle && alpha != targetAlpha && fadeDuration < .75)
        { fromAlpha = alpha; fadeStarted = Elapsed; fadeDuration = .75; }
        this.gentle |= gentle; reduced = reducedMotion; performance = performanceMode;
    }

    internal static bool ValidMonitors(IReadOnlyList<WallpaperMonitor> monitors) => monitors.Count is > 0 and <= 8
        && monitors.All(m => m.Bounds.Width is > 0 and <= 16384 && m.Bounds.Height is > 0 and <= 16384 && double.IsFinite(m.Scaling) && m.Scaling is >= .5 and <= 8)
        && monitors.Sum(m => (long)m.Bounds.Width * m.Bounds.Height) <= 64_000_000;
    private double Elapsed => clock.GetElapsedTime(started).TotalSeconds;

    internal void Tick()
    {
        if (!running) return;
        try
        {
            var now = Elapsed;
            var changed = false;
            if (clock.GetElapsedTime(lastDesktopPoll).TotalSeconds >= 1)
            {
                if (collage is not null) AnalysisProcessBudget.RequireHeadroom();
                foreach (var host in hosts)
                { var state = adapter.ReadState(host.Window, host.Monitor); changed |= state != host.State; host.State = state; }
                lastDesktopPoll = clock.GetTimestamp();
            }
            if (hosts.Any(h => !h.State.ParentAvailable)) { Fail(); return; }
            var visible = hosts.Any(h => !h.State.Covered && !h.State.Minimized);
            var hold = reduced || hosts.Any(h => h.State.OnBattery) || !visible || ending;
            if (collage is not null) changed |= collage.Advance(now, !hold, gentle ? .5 : 1);
            video?.SetPaused(hold && bitmap is not null);
            if (!ending && video is not null)
            {
                if (video.LastError is not null) { Fail(); return; }
                if ((!hold || bitmap is null) && video.TryCopyFrame(out var frame) && frame is not null && frame.Sequence != sequence)
                { CopyFrame(frame); image = bitmap; sequence = frame.Sequence; changed = true; }
                if (video.IsFinished)
                { video.Dispose(); video = createVideo(videoPath!); video.SetGain(0); video.Start(); sequence = -1; }
            }
            var loaded = collage is not null || (videoPath is not null ? bitmap is not null : images?.CachedCount > 0);
            if (!ending && !loaded && (now >= 15 || images is { CachedCount: 0, PendingCount: 0 } && images.Completion.IsCompleted))
            { Fail(); return; }
            if (!ending && loaded && !ready.Task.IsCompleted)
            { ready.TrySetResult(true); fromAlpha = alpha; targetAlpha = 1; fadeStarted = now; changed = true; Report(WallpaperStatus.Active); }
            var progress = Math.Clamp((now - fadeStarted) / Math.Max(.001, fadeDuration), 0, 1);
            var next = fromAlpha + (targetAlpha - fromAlpha) * progress * progress * (3 - 2 * progress);
            changed |= next != alpha; alpha = next;
            if (alpha == 1 && !ending) entered.TrySetResult(true);
            if (ending && alpha == 0) { Close(); return; }
            if (changed && visible)
            foreach (var host in hosts.Where(h => !h.State.Covered && !h.State.Minimized))
            { host.View.Update(collage?.For(host.Monitor) ?? image, alpha); PresentedFrames++; }
            timer.Interval = TimeSpan.FromSeconds(!visible ? 1 : alpha != targetAlpha || (video is not null || collage is not null) && !hold
                ? 1d / (performance ? 15 : 30) : loaded ? .5 : .05);
        }
        catch (Exception) { Fail(); }
    }

    private void CopyFrame(VideoFrame frame)
    {
        if (frame.Width is <= 0 or > 960 || frame.Height is <= 0 or > 540 || frame.Pixels.Length < frame.Width * frame.Height * 4)
            throw new IOException("Wallpaper frame is invalid");
        if (bitmap?.PixelSize != new PixelSize(frame.Width, frame.Height))
        { foreach (var host in hosts) host.View.Update(null, 0); bitmap?.Dispose(); bitmap = new(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque); }
        using var locked = bitmap.Lock();
        for (var row = 0; row < frame.Height; row++) Marshal.Copy(frame.Pixels, row * frame.Width * 4, locked.Address + row * locked.RowBytes, frame.Width * 4);
    }

    public Task StopAsync(bool immediate = false)
    {
        if (!Dispatcher.UIThread.CheckAccess()) return Dispatcher.UIThread.InvokeAsync(() => StopAsync(immediate));
        if (!running || immediate || alpha == 0) { Close(); return stopped.Task; }
        if (!ending)
        {
            ending = true; video?.Dispose(); video = null; ready.TrySetResult(false); entered.TrySetResult(false);
            fromAlpha = alpha; targetAlpha = 0; fadeStarted = Elapsed; fadeDuration = gentle ? .75 : .25; timer.Interval = TimeSpan.FromMilliseconds(33);
        }
        return stopped.Task;
    }
    private void Fail() { Close(); Report(WallpaperStatus.Unavailable); }
    private void Close()
    {
        generation++; running = false; timer.Stop(); cancellation.Dispose();
        collageBuildStop?.Cancel(); collageBuildStop?.Dispose(); collageBuildStop = null;
        video?.Dispose(); video = null; videoPath = null;
        foreach (var host in hosts)
        {
            try { host.View.Update(null, 0); host.Window.Hide(); } catch (Exception) { }
            try { adapter.Detach(host.Window); } catch (Exception) { }
            try { host.Window.Close(); } catch (Exception) { }
        }
        hosts.Clear(); images?.Dispose(); images = null; bitmap?.Dispose(); bitmap = null; image = null;
        collage?.Dispose(); collage = null;
        ready.TrySetResult(false); entered.TrySetResult(false); stopped.TrySetResult(true); Report(WallpaperStatus.Idle);
    }
    private void Report(WallpaperStatus value) { if (Status != value) { Status = value; StatusChanged?.Invoke(value); } }
    private static TaskCompletionSource<bool> NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Dispose()
    {
        if (Dispatcher.UIThread.CheckAccess()) { disposed = true; Close(); }
        else Dispatcher.UIThread.Post(() => { disposed = true; Close(); });
    }

    private sealed class WallpaperView : Control
    {
        private IImage? image; private double alpha;
        public WallpaperView() => IsHitTestVisible = false;
        public void Update(IImage? value, double opacity) { image = value; alpha = opacity; InvalidateVisual(); }
        public override void Render(DrawingContext context)
        {
            if (image is null || alpha <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0 || image.Size.Width <= 0 || image.Size.Height <= 0) return;
            var scale = Math.Max(Bounds.Width / image.Size.Width, Bounds.Height / image.Size.Height);
            var source = new Rect((image.Size.Width - Bounds.Width / scale) / 2, (image.Size.Height - Bounds.Height / scale) / 2, Bounds.Width / scale, Bounds.Height / scale);
            using (context.PushOpacity(alpha)) context.DrawImage(image, source, new Rect(Bounds.Size));
        }
    }
}
