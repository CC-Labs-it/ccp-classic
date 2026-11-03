using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using System.Threading.Channels;
using CCP.Classic.App.Wallpaper;
using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class WallpaperSessionTests
{
    [Fact]
    public Task StaticWritesWaitForTheOpaqueCoverAndNormalRestoreGetsANewCover() => WithSession(async run =>
    {
        await UiDispatcher.Run(() => { run.Update(); Assert.Equal(0, run.Native.Starts); Assert.Single(run.Covers); });
        await run.Enter(); await run.Done();
        Assert.Equal(new[] { "restore", "cover", "opaque", "apply", "fade" }, run.Log);
        Task? stop = null;
        await UiDispatcher.Run(() =>
        {
            run.Log.Clear(); stop = run.Session.StopAsync(false);
            Assert.Equal(new[] { "cover" }, run.Log); Assert.False(stop.IsCompleted);
        });
        await run.Enter(); await stop!;
        Assert.Equal(new[] { "cover", "opaque", "restore", "fade" }, run.Log);
    });

    [Fact]
    public Task PanicDuringEntranceImmediatelyClosesTheCoverAndPreventsALateNativeWrite() => WithSession(async run =>
    {
        Task? previous = null;
        await UiDispatcher.Run(() =>
        {
            run.Update(); previous = run.Session.Completion; _ = run.Session.StopAsync();
            Assert.True(run.Covers[0].Stopped); Assert.True(run.Covers[0].Disposed);
            run.Covers[0].Enter();
        });
        await previous!; await run.Done(); Assert.Equal(0, run.Native.Starts); Assert.Null(run.Session.StatusKey);
    });

    [Fact]
    public Task NativeRestoreIsRequestedEvenIfTheCoverCannotBeClosedCleanly() => WithSession(async run =>
    {
        await UiDispatcher.Run(() =>
        {
            run.Update(dynamic: true); run.Covers[0].ThrowOnStop = true; var before = run.Native.Stops;
            _ = run.Session.StopAsync(); Assert.Equal(before + 1, run.Native.Stops); Assert.True(run.Covers[0].Disposed);
        });
        await run.Done();
    });

    [Fact]
    public Task LateScansCannotCreateWindowsAfterStopEvenWhenTheyIgnoreCancellation() => WithSession(async run =>
    {
        var delayed = new TaskCompletionSource<WallpaperMedia>(TaskCreationOptions.RunContinuationsAsynchronously); Task? previous = null;
        await UiDispatcher.Run(() =>
        {
            run.ScanOverride = (_, _) => delayed.Task; run.Update(); previous = run.Session.Completion;
            _ = run.Session.StopAsync(); delayed.SetResult(run.Media);
        });
        await previous!; Assert.Empty(run.Covers); Assert.Equal(0, run.Native.Starts); Assert.Null(run.Session.StatusKey);
    });

    [Fact]
    public Task UnchangedSelectionsOnlyConfigureLiveMotionWhileLayoutChangesRestartTheGeneration() => WithSession(async run =>
    {
        await UiDispatcher.Run(() => run.Update(dynamic: true)); await run.Enter(); await run.Done();
        await UiDispatcher.Run(() =>
        {
            run.Update(dynamic: true, gentle: true, reduced: true, performance: true);
            Assert.Equal(1, run.Scans); Assert.Single(run.Covers); Assert.Equal((true, true, true), run.Covers[0].Motion);
            run.Update(dynamic: true, monitors: [new(new PixelRect(320, 0, 640, 480), 2)]);
            Assert.True(run.Covers[0].Stopped); Assert.Equal(2, run.Scans); Assert.Equal(2, run.Covers.Count);
        });
        await run.Enter(); await run.Done(); Assert.Equal(run.Video, run.Covers[1].Video); Assert.Equal(0, run.Native.Starts);
    });

    [Fact]
    public Task StaticSelectionRejectsOtherFormatsAndExclusionsBeforeUsingTheStarterFallback() => WithSession(async run =>
    {
        await UiDispatcher.Run(() =>
        {
            run.Media = new([Path.ChangeExtension(run.Image, ".gif"), run.Image], []);
            run.Library.ExcludedFiles.Add(run.Image); run.Update();
            Assert.Equal(run.Starter, run.Covers[0].Image);
        });
        await run.Enter(); await run.Done(); Assert.Equal(run.Starter, run.Native.Selected);
    });

    [Fact]
    public Task TheScannerReceivesABoundedSnapshotRatherThanLiveLibraryLists() => WithSession(async run =>
    {
        var delayed = new TaskCompletionSource<WallpaperMedia>(TaskCreationOptions.RunContinuationsAsynchronously);
        await UiDispatcher.Run(() =>
        {
            run.Library.ImageFolders.Add(Path.GetTempPath()); run.ScanOverride = (_, _) => delayed.Task; run.Update();
            run.Library.ImageFolders.Clear(); run.Library.ExcludedFiles.Add(run.Image);
            Assert.Single(run.Seen!.ImageFolders); Assert.Empty(run.Seen.ExcludedFiles); delayed.SetResult(run.Media);
        });
        await run.Enter(); await run.Done(); Assert.Equal(run.Image, run.Native.Selected);
    });

    [Fact]
    public Task DynamicWithoutLocalVideosReportsOnceAndNeverChangesTheStaticWallpaper() => WithSession(async run =>
    {
        await UiDispatcher.Run(() => { run.Media = new([run.Image], []); run.Update(dynamic: true); run.Update(dynamic: true); });
        await run.Done(); Assert.Equal("NoVideos", run.Session.StatusKey); Assert.Empty(run.Covers);
        Assert.Equal(1, run.Scans); Assert.Equal(0, run.Native.Starts);
    });

    [Fact]
    public Task UnsupportedPlatformsDoNotScanRecoverOrCallTheNativeWriter() => WithSession(async run =>
    {
        await UiDispatcher.Run(() => { _ = run.Session.RecoverAsync(); run.Update(); });
        Assert.Equal(0, run.Scans); Assert.Equal(0, run.Native.Starts); Assert.Equal(0, run.Native.Stops); Assert.Equal(0, run.Native.Recoveries);
    }, windows: false);

    [Fact]
    public Task FailedRestorationStaysVisibleAndADelayedRecoveryCannotReplaceTheStoppedStatus() => WithSession(async run =>
    {
        await UiDispatcher.Run(() => { run.Update(); }); await run.Enter(); await run.Done();
        await UiDispatcher.Run(() => { run.Native.RestoreWorks = false; _ = run.Session.StopAsync(); });
        await run.Done(); Assert.Equal("WallpaperRestoreFailed", run.Session.StatusKey);
        var delayed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); Task? recovery = null;
        await UiDispatcher.Run(() =>
        {
            run.Native.Recovery = delayed.Task; recovery = run.Session.RecoverAsync(); _ = run.Session.StopAsync();
            run.Native.Status = WallpaperStatus.Recovered; delayed.SetResult(true);
        });
        await recovery!; Assert.Equal("WallpaperRestoreFailed", run.Session.StatusKey);
    });

    [Fact]
    public Task AFailedRestorePreventsTheNextModeFromCoveringUpTheFailure() => WithSession(async run =>
    {
        await UiDispatcher.Run(() => { run.Native.RestoreWorks = false; run.Update(dynamic: true); });
        await run.Done(); Assert.Equal("WallpaperRestoreFailed", run.Session.StatusKey);
        Assert.Empty(run.Covers); Assert.Equal(0, run.Scans);
    });

    [Fact]
    public Task LateCoverFailuresAfterDisposalCannotReportOrReopenAnything() => WithSession(async run =>
    {
        await UiDispatcher.Run(() => run.Update(dynamic: true)); await run.Enter(); await run.Done();
        await UiDispatcher.Run(() =>
        {
            run.Session.Dispose(); var count = run.Log.Count;
            run.Covers[0].Fail(); Dispatcher.UIThread.RunJobs(); Assert.Equal(count, run.Log.Count); Assert.Single(run.Covers);
        });
    });

    private static async Task WithSession(Func<Run, Task> test, bool windows = true)
    {
        Run? run = null; await UiDispatcher.Run(() => run = new(windows));
        try { await test(run!); }
        finally { await UiDispatcher.Run(() => { run!.Session.Dispose(); run.Fallback.Dispose(); }); await run!.Done(); }
    }
    private sealed class Run
    {
        public readonly string Image = Path.Combine(Path.GetTempPath(), "wall-selected.jpg"), Video = Path.Combine(Path.GetTempPath(), "wall-local.mp4"), Starter = Path.Combine(Path.GetTempPath(), "wall-starter.png");
        public readonly WriteableBitmap Fallback = new(new PixelSize(1, 1), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        public readonly List<string> Log = [];
        public readonly List<Cover> Covers = [];
        private readonly Channel<Cover> created = Channel.CreateUnbounded<Cover>();
        public readonly Native Native;
        public readonly WallpaperSession Session;
        public readonly LibrarySettings Library = new();
        public WallpaperMedia Media;
        public LibrarySettings? Seen;
        public int Scans;
        public Func<LibrarySettings, CancellationToken, Task<WallpaperMedia>>? ScanOverride;
        public Run(bool windows)
        {
            Media = new([Image], [Video]); Native = new(Log);
            Session = new(Fallback, [Starter], windows, Native, () => { var value = new Cover(Log); Covers.Add(value); created.Writer.TryWrite(value); return value; },
                (library, token) => { Scans++; Seen = library; return ScanOverride?.Invoke(library, token) ?? Task.FromResult(Media); });
            Assert.Empty(Log);
        }
        public void Update(bool dynamic = false, bool gentle = false, bool reduced = false, bool performance = false, WallpaperMonitor[]? monitors = null)
            => Session.Update(true, dynamic, monitors ?? [new(new PixelRect(0, 0, 320, 240), 1)], Library, gentle, reduced, performance);
        public async Task Enter()
        {
            var current = await created.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            await UiDispatcher.Run(current.Enter);
        }
        public Task Done() => Session.Completion.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
    }
    private sealed class Native(List<string> log) : IStaticWallpaperBackend
    {
        public int Starts, Stops, Recoveries;
        public string? Selected;
        public bool RestoreWorks = true;
        public Task<bool>? Recovery;
        public WallpaperStatus Status { get; set; }
        public Task Completion => Task.CompletedTask;
        public Task<bool> StartAsync(string path, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Starts++; Selected = path; log.Add("apply"); Status = WallpaperStatus.Active; return Task.FromResult(true); }
        public Task<bool> StopAsync()
        { Stops++; log.Add("restore"); Status = RestoreWorks ? WallpaperStatus.Idle : WallpaperStatus.RestoreFailed; return Task.FromResult(RestoreWorks); }
        public Task<bool> RecoverAsync() { Recoveries++; return Recovery ?? Task.FromResult(true); }
        public void Dispose() { }
    }
    private sealed class Cover(List<string> log) : IWallpaperCover
    {
        private readonly TaskCompletionSource<bool> entrance = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Image, Video;
        public bool Stopped, Disposed, ThrowOnStop;
        public (bool, bool, bool) Motion;
        public Task<bool> EntranceCompleted => entrance.Task;
        public event Action<WallpaperStatus>? StatusChanged;
        public bool Start(IReadOnlyList<WallpaperMonitor> monitors, string? image, IImage fallback, string? video, bool gentle, bool reduced, bool performance, CancellationToken token)
        { Image = image; Video = video; Motion = (gentle, reduced, performance); log.Add("cover"); return true; }
        public void Enter() { if (entrance.TrySetResult(true)) log.Add("opaque"); }
        public void Configure(bool gentle, bool reduced, bool performance) => Motion = (gentle, reduced, performance);
        public Task StopAsync(bool immediate)
        { if (ThrowOnStop) throw new InvalidOperationException("Fake cover failure"); Stopped = true; log.Add(immediate ? "cut" : "fade"); if (immediate) entrance.TrySetResult(false); return Task.CompletedTask; }
        public void Fail() => StatusChanged?.Invoke(WallpaperStatus.Unavailable);
        public void Dispose() { Disposed = true; entrance.TrySetResult(false); }
    }
}
