using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace CCP.Classic.App.Wallpaper;

public sealed partial class DynamicWallpaperService
{
    private WallpaperCollageSet? collage;
    private CancellationTokenSource? collageBuildStop;
    internal Task CollageBuildCompletion { get; private set; } = Task.CompletedTask;
    internal Func<IReadOnlyList<string>, IReadOnlyList<WallpaperMonitor>, WallpaperChoice, CancellationToken, Task<WallpaperCollageSet>> CollageBuilder { get; set; }
        = (files, monitors, choice, token) => WallpaperCollageSet.BuildAsync(files, monitors, choice, token);
    internal bool StartCollage(IReadOnlyList<WallpaperMonitor> monitors, IReadOnlyList<string> paths, WallpaperChoice choice,
        IImage fallback, bool gentle, bool reduced, bool performance, CancellationToken token)
    {
        if (!Prepare(monitors, fallback, gentle, reduced, performance, token)) return false;
        if (paths.Count < 2) { Fail(); return false; }
        var own = generation; var selected = paths.ToArray(); var targets = monitors.ToArray();
        collageBuildStop = CancellationTokenSource.CreateLinkedTokenSource(token); collageBuildStop.CancelAfter(TimeSpan.FromSeconds(30));
        var stop = collageBuildStop.Token;
        CollageBuildCompletion = BuildCollage(selected, targets, choice, own, stop); return true;
    }
    private async Task BuildCollage(string[] paths, WallpaperMonitor[] monitors, WallpaperChoice choice, long own, CancellationToken token)
    {
        try
        {
            var built = await CollageBuilder(paths, monitors, choice, token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation != own || disposed || token.IsCancellationRequested) { built.Dispose(); return; }
                collageBuildStop?.CancelAfter(Timeout.InfiniteTimeSpan);
                collage = built; started = lastDesktopPoll = clock.GetTimestamp(); fadeDuration = .9;
                try { if (!AttachHosts(monitors)) { Fail(); return; } timer.Start(); Tick(); }
                catch (Exception) { Fail(); }
            });
        }
        catch (Exception)
        { await Dispatcher.UIThread.InvokeAsync(() => { if (generation == own && !disposed) Fail(); }); }
    }
    private bool Prepare(IReadOnlyList<WallpaperMonitor> monitors, IImage fallback,
        bool gentle, bool reduced, bool performance, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess(); Close(); ready = NewCompletion(); entered = NewCompletion(); stopped = NewCompletion();
        if (disposed || !adapter.IsSupported || token.IsCancellationRequested || !ValidMonitors(monitors)) { Fail(); return false; }
        this.gentle = gentle; this.reduced = reduced; this.performance = performance;
        started = lastDesktopPoll = clock.GetTimestamp(); alpha = fromAlpha = targetAlpha = 0; fadeStarted = 0; fadeDuration = gentle ? .75 : .25;
        image = fallback; ending = false; sequence = -1; PresentedFrames = 0; running = true;
        var own = ++generation; cancellation = token.Register(() => Dispatcher.UIThread.Post(() => { if (generation == own) Close(); }));
        return true;
    }
    private bool AttachHosts(IReadOnlyList<WallpaperMonitor> monitors)
    {
        foreach (var monitor in monitors)
        {
            var view = new WallpaperView(); var window = new Window { ShowActivated = false, ShowInTaskbar = false, Topmost = false,
                WindowDecorations = WindowDecorations.None, CanResize = false, Background = Brushes.Transparent,
                TransparencyBackgroundFallback = Brushes.Transparent, TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
                Width = 1, Height = 1, Position = new PixelPoint(-32000, -32000), Content = view };
            var host = new Host(window, view, monitor); hosts.Add(host); window.Show();
            if (!adapter.Attach(window, monitor)) return false; host.State = adapter.ReadState(window, monitor);
        }
        return true;
    }
}
