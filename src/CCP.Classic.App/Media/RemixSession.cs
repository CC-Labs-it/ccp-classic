using Avalonia.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.Remix;
using CCP.Classic.App.Wallpaper;

namespace CCP.Classic.App.Media;

// JackpotRemixDirector policy: scheduled flashes only, one-in-100 roll, carried until ready.
// One prepared scene and one active scene; one local decode worker, cancelled on session stop.
internal sealed class RemixSession : IDisposable
{
    private readonly object gate = new();
    private readonly ClassicSettings settings;
    private readonly Func<IReadOnlyList<string>> paths;
    private readonly Func<IEnumerable<string>, int, CancellationToken, RemixImage> prepare;
    private readonly Func<bool> memoryAvailable;
    private readonly Random random;
    private readonly RemixRollState roll = new();
    private CancellationTokenSource cancellation = new();
    private RemixImage? ready, active;
    private bool running, disposed, working;
    private int generation;
    private double seconds, activeStarted, nextCheck;
    internal Task Completion { get; private set; } = Task.CompletedTask;
    internal IImage? Image => active;
    internal RemixSession(ClassicSettings settings, Func<IReadOnlyList<string>> paths, int? seed = null,
        Func<IEnumerable<string>, int, CancellationToken, RemixImage>? prepare = null, Func<bool>? memoryAvailable = null)
    {
        this.settings = settings; this.paths = paths; this.prepare = prepare ?? RemixImage.Prepare;
        random = seed is { } value ? new(value) : new();
        this.memoryAvailable = memoryAvailable ?? (() =>
        {
            try { new WallpaperFrameBudget().Check(1536L * 1024 * 1024); return true; }
            catch (IOException) { return false; }
        });
    }
    private bool Allowed => running && !disposed && settings.Flash.RemixEnabled && settings.Effects["flash"].Enabled
        && !settings.GentleVisuals && !settings.ReducedMotion && !settings.PerformanceMode;
    internal void Start()
    {
        Stop(); if (disposed) return;
        cancellation.Dispose(); cancellation = new(); running = true; seconds = nextCheck = 0;
    }
    internal double? TryTake()
    {
        lock (gate)
        {
            if (!Allowed || active is not null) return null;
            if (!roll.Take(true, random.Next(100) == 0, ready is not null, random.Next())) return null;
            active = ready; ready = null; activeStarted = seconds;
            return active!.Size.Width / active.Size.Height;
        }
    }
    internal void BeforeFrame(double elapsed, bool quiet)
    {
        if (!running || disposed || !double.IsFinite(elapsed)) return;
        seconds = Math.Max(seconds, elapsed);
        active?.Advance(seconds - activeStarted, Allowed);
        if (!Allowed || !quiet || seconds < nextCheck) return;
        lock (gate)
        {
            if (working || ready is not null || active is not null) return;
            nextCheck = seconds + 20;
            var selected = paths().Take(100000).ToArray();
            if (selected.Length == 0 || !memoryAvailable()) return;
            var seed = roll.Carried ? roll.Seed : random.Next(); var version = generation; var token = cancellation.Token;
            working = true;
            Completion = Task.Run(() =>
            {
                RemixImage? result = null;
                try { result = prepare(selected, seed, token); }
                catch (Exception error) when (error is not OutOfMemoryException)
                { lock (gate) if (version == generation && !token.IsCancellationRequested) nextCheck = seconds + 600; }
                finally
                {
                    lock (gate)
                    {
                        working = false;
                        if (version == generation && !token.IsCancellationRequested && running && !disposed) { ready?.Dispose(); ready = result; }
                        else result?.Dispose();
                    }
                }
            });
        }
    }
    internal void AfterFrame(bool hasRemixCue)
    {
        if (active is null) return;
        if (!hasRemixCue) { active.Dispose(); active = null; }
        else active.Advance(seconds - activeStarted, Allowed);
    }
    internal void Stop()
    {
        lock (gate)
        {
            running = false; generation++; cancellation.Cancel(); roll.Clear();
            active?.Dispose(); active = null; ready?.Dispose(); ready = null;
        }
    }
    public void Dispose() { if (disposed) return; Stop(); disposed = true; cancellation.Dispose(); }
}
