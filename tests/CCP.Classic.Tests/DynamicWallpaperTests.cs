using CCP.Classic.App.Wallpaper;
using CCP.Classic.App.Media;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class DynamicWallpaperTests
{
    [Fact]
    public Task ActualDesktopGatesPauseDecodingAndGentleTransitionsFinishBeforeClosing() => UiDispatcher.Run(() =>
    {
        using var fallback = new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        var adapter = new FakeDesktop(); var playback = new FakeVideo(); var clock = new ManualClock();
        using var service = new DynamicWallpaperService(adapter, _ => playback, clock);
        Assert.True(service.Start([new(new PixelRect(0, 0, 320, 240), 1)], null, fallback,
            Path.Combine(Path.GetTempPath(), "wallpaper-test.mp4"), gentle: true, token: TestContext.Current.CancellationToken));
        Assert.Equal(0, playback.Gain); Assert.True(service.Ready.IsCompletedSuccessfully);
        Assert.False(service.EntranceCompleted.IsCompleted);
        clock.Advance(.75); service.Tick(); Assert.True(service.EntranceCompleted.IsCompletedSuccessfully);
        var frames = service.PresentedFrames; var copies = playback.Copies;
        adapter.State = new(true, Covered: true); clock.Advance(1.1); service.Tick();
        Assert.True(playback.Paused); Assert.Equal(copies, playback.Copies); Assert.Equal(frames, service.PresentedFrames);
        adapter.State = new(true, OnBattery: true); clock.Advance(1.1); service.Tick();
        Assert.True(playback.Paused); Assert.Equal(copies, playback.Copies);
        adapter.State = new(true, Minimized: true); clock.Advance(1.1); service.Tick();
        Assert.True(playback.Paused); Assert.Equal(copies, playback.Copies);
        adapter.State = new(true); service.Configure(true, true, false); clock.Advance(1.1); service.Tick();
        Assert.True(playback.Paused); Assert.Equal(copies, playback.Copies);
        service.Configure(true, false, true); clock.Advance(1.1); service.Tick(); Assert.False(playback.Paused);
        var stopping = service.StopAsync(); Assert.True(playback.Disposed);
        clock.Advance(.5); service.Tick(); Assert.False(stopping.IsCompleted); Assert.Equal(1, service.HostCount);
        clock.Advance(.25); service.Tick(); Assert.True(stopping.IsCompletedSuccessfully); Assert.Equal(0, service.HostCount);
        Assert.Equal(1, adapter.Detaches);
    });

    [Fact]
    public async Task CancellationAndUnavailableHostsResolveBothReadinessTasksWithoutLateFrames()
    {
        DynamicWallpaperService service = null!; WriteableBitmap fallback = null!;
        var adapter = new FakeDesktop(); var playback = new FakeVideo { FramesAvailable = false };
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await UiDispatcher.Run(() =>
        {
            fallback = new(new PixelSize(2, 2), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            service = new(adapter, _ => playback);
            Assert.True(service.Start([new(new PixelRect(0, 0, 320, 240), 1)], null, fallback,
                Path.Combine(Path.GetTempPath(), "wallpaper-test.mp4"), token: stop.Token));
            Assert.False(service.Ready.IsCompleted); stop.Cancel(); Dispatcher.UIThread.RunJobs();
            playback.FramesAvailable = true; var copies = playback.Copies; service.Tick();
            Assert.Equal(0, service.HostCount); Assert.Equal(copies, playback.Copies);
        });
        try
        {
            Assert.False(await service.Ready.WaitAsync(TestContext.Current.CancellationToken));
            Assert.False(await service.EntranceCompleted.WaitAsync(TestContext.Current.CancellationToken));
            await UiDispatcher.Run(() =>
            {
                adapter.CanAttach = false;
                Assert.False(service.Start([new(new PixelRect(0, 0, 320, 240), 1)], null, fallback,
                    Path.Combine(Path.GetTempPath(), "wallpaper-test.mp4"), token: TestContext.Current.CancellationToken));
                Assert.Equal(WallpaperStatus.Unavailable, service.Status); Assert.Equal(0, service.HostCount);
            });
            Assert.False(await service.EntranceCompleted.WaitAsync(TestContext.Current.CancellationToken));
        }
        finally { await UiDispatcher.Run(() => { service.Dispose(); fallback.Dispose(); }); }
    }

    [Fact]
    public void FrameBudgetsAndNativeStylesKeepTheLayerBoundedAndOutOfTheTaskbar()
    {
        Assert.True(DynamicWallpaperService.ValidMonitors([new(new PixelRect(-1920, 0, 1920, 1080), 1)]));
        Assert.False(DynamicWallpaperService.ValidMonitors([new(new PixelRect(0, 0, 16384, 16384), 1)]));
        Assert.False(DynamicWallpaperService.ValidMonitors([new(new PixelRect(0, 0, 1920, 1080), double.NaN)]));
        if (!OperatingSystem.IsWindows()) return;
        var styles = WindowsDesktopWallpaperAdapter.ChildStyles(0x80000000, 0x40008);
        Assert.Equal(0x40000000u, styles.Item1); Assert.Equal(0u, styles.Item2 & 0x40008u);
        Assert.Equal(0x080800a0u, styles.Item2 & 0x080800a0u);
    }

    private sealed class FakeDesktop : IDesktopWallpaperAdapter
    {
        public bool IsSupported => true;
        public bool CanAttach = true; public int Detaches;
        public WallpaperDesktopState State = new(true);
        public bool Attach(Window window, WallpaperMonitor monitor)
        { window.Width = monitor.Bounds.Width / monitor.Scaling; window.Height = monitor.Bounds.Height / monitor.Scaling; return CanAttach; }
        public WallpaperDesktopState ReadState(Window window, WallpaperMonitor monitor) => State;
        public void Detach(Window window) => Detaches++;
    }
    private sealed class FakeVideo : ILocalVideoPlayback
    {
        public bool IsFinished => false;
        public string? LastError => null;
        public bool FramesAvailable = true, Paused, Disposed; public int Copies; public double Gain = 1;
        public void Start() { }
        public void SetGain(double gain) => Gain = gain;
        public void SetPaused(bool paused) => Paused = paused;
        public bool TryCopyFrame(out VideoFrame? frame) { Copies++; frame = FramesAvailable ? new(new byte[16], 2, 2, 1) : null; return frame is not null; }
        public void Dispose() => Disposed = true;
    }
    private sealed class ManualClock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public void Advance(double seconds) => timestamp += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
