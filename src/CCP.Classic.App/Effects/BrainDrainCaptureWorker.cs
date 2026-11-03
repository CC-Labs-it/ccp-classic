using Avalonia;
using Avalonia.Media.Imaging;
using System.Runtime.InteropServices;

namespace CCP.Classic.App.Effects;

internal sealed record BrainDrainCaptureRequest(PixelRect[] Screens, BrainDrainVisual Visual, bool Performance, int FramesPerSecond);

// Native capture may stall in a display driver. The dispatcher never waits for it or its teardown.
// One replaceable frame per screen, eight screens, and no second worker while this one is retiring.
internal sealed class BrainDrainCaptureWorker : IDisposable
{
    private readonly object gate = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly Func<IBrainDrainDesktopCapture> create;
    private readonly Dictionary<PixelRect, Bitmap> pending = [];
    private readonly TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private BrainDrainCaptureRequest? request;
    private bool stopped, started, disposed;
    private int frames;
    internal Task Completion => done.Task;
    internal int FramesPublished => Volatile.Read(ref frames);
    internal string? Status { get; private set; }
    internal int PendingFrames { get { lock (gate) return pending.Count; } }
    internal BrainDrainCaptureWorker(Func<IBrainDrainDesktopCapture>? create = null) => this.create = create ?? (() => new BrainDrainDesktopCapture());

    internal void Update(BrainDrainCaptureRequest value)
    {
        lock (gate)
        {
            if (stopped) return;
            request = value with { Screens = value.Screens.Distinct().Take(8).ToArray(), FramesPerSecond = Math.Clamp(value.FramesPerSecond, 1, 30) };
            foreach (var key in pending.Keys.Where(key => !request.Screens.Contains(key)).ToArray())
            { pending[key].Dispose(); pending.Remove(key); }
            if (!started)
            {
                started = true;
                new Thread(Loop) { IsBackground = true, Name = "Classic Brain Drain capture", Priority = ThreadPriority.BelowNormal }.Start();
            }
            wake.Set();
        }
    }
    internal Dictionary<PixelRect, Bitmap> TakeFrames()
    {
        lock (gate)
        {
            var result = new Dictionary<PixelRect, Bitmap>(pending); pending.Clear(); return result;
        }
    }
    private void Loop()
    {
        IBrainDrainDesktopCapture? capture = null;
        var processors = new Dictionary<PixelRect, BrainDrainFrameProcessor>();
        try
        {
            capture = create();
            while (true)
            {
                BrainDrainCaptureRequest? current;
                lock (gate) { if (stopped) return; current = request; }
                var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                if (current is not null)
                {
                    capture.SetScreens(current.Screens);
                    foreach (var key in processors.Keys.Where(key => !current.Screens.Contains(key)).ToArray())
                    { processors[key].Dispose(); processors.Remove(key); }
                    foreach (var screen in current.Screens)
                    {
                        lock (gate) { if (stopped) return; }
                        var raw = capture.Capture(screen, current.Performance);
                        lock (gate) { if (stopped) return; }
                        if (raw is null) { Status = "BrainDrainUnavailable"; continue; }
                        if (!processors.TryGetValue(screen, out var processor)) processors[screen] = processor = new();
                        var image = processor.Process(raw.Value, current.Visual);
                        if (image is null) continue;
                        lock (gate)
                        {
                            if (stopped) { image.Dispose(); return; }
                            if (request is null || !request.Screens.Contains(screen)) { image.Dispose(); continue; }
                            if (pending.Remove(screen, out var old)) old.Dispose();
                            pending[screen] = image; Interlocked.Increment(ref frames); Status = null;
                        }
                    }
                }
                var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                // Update wakes a quiet worker, but does not increase the configured capture rate.
                var delay = Math.Max(1, 1000d / (current?.FramesPerSecond ?? 4) - elapsed);
                if (wake.WaitOne((int)delay))
                {
                    lock (gate) { if (stopped) return; }
                    var remaining = Math.Max(0, delay - System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds + elapsed);
                    if (remaining > 0) Thread.Sleep((int)remaining);
                }
            }
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or NotSupportedException or ExternalException)
        { Status = "BrainDrainUnavailable"; }
        finally
        {
            try { foreach (var processor in processors.Values) processor.Dispose(); capture?.Dispose(); }
            finally { lock (gate) { stopped = true; wake.Dispose(); done.TrySetResult(); } }
        }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return; disposed = stopped = true;
            foreach (var image in pending.Values) image.Dispose(); pending.Clear();
            if (started) { if (!done.Task.IsCompleted) wake.Set(); }
            else { wake.Dispose(); done.TrySetResult(); }
        }
    }
}
