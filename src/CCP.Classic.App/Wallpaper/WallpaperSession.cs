using Avalonia.Media;
using Avalonia.Threading;
using CCP.Classic.Core;

namespace CCP.Classic.App.Wallpaper;

public sealed record WallpaperMedia(IReadOnlyList<string> Images, IReadOnlyList<string> Videos);
public interface IStaticWallpaperBackend : IDisposable
{
    WallpaperStatus Status { get; }
    Task<bool> StartAsync(string path, CancellationToken token);
    Task<bool> StopAsync();
    Task<bool> RecoverAsync();
    Task Completion { get; }
}
public interface IWallpaperCover : IDisposable
{
    Task<bool> EntranceCompleted { get; }
    event Action<WallpaperStatus>? StatusChanged;
    bool Start(IReadOnlyList<WallpaperMonitor> monitors, string? image, IImage fallback, string? video,
        bool gentle, bool reduced, bool performance, CancellationToken token);
    bool StartCollage(IReadOnlyList<WallpaperMonitor> monitors, IReadOnlyList<string> images, WallpaperOptions options,
        IImage fallback, bool gentle, bool reduced, bool performance, CancellationToken token) => false;
    void Configure(bool gentle, bool reduced, bool performance);
    Task StopAsync(bool immediate);
}

// The native writer has its own generation. This owner also invalidates scans and UI continuations.
public sealed class WallpaperSession : IDisposable
{
    private sealed record Selection(bool Dynamic, WallpaperMonitor[] Monitors, LibrarySettings Library, WallpaperChoice Choice)
    {
        public bool Matches(bool dynamic, IReadOnlyList<WallpaperMonitor> monitors, LibrarySettings library, WallpaperChoice choice) => Dynamic == dynamic && Choice == choice
            && Monitors.SequenceEqual(monitors.Take(9)) && Library.ImageFolders.SequenceEqual(library.ImageFolders.Take(32))
            && Library.VideoFolders.SequenceEqual(library.VideoFolders.Take(32)) && Library.ExcludedFiles.SequenceEqual(library.ExcludedFiles.Take(10000))
            && Library.IncludedFiles.SequenceEqual(library.IncludedFiles.Take(10000)) && Library.FolderSelections.SequenceEqual(library.FolderSelections.Take(10000));
    }
    private readonly bool windows;
    private readonly IImage? fallback;
    private readonly string[] starterImages;
    private readonly IStaticWallpaperBackend stationary;
    private readonly Func<IWallpaperCover> createCover;
    private readonly Func<LibrarySettings, CancellationToken, Task<WallpaperMedia>> scan;
    private readonly WallpaperContent content = new();
    private readonly TimeProvider clock;
    private long rotationAt;
    private int rotationInterval = 60;
    private bool rotate;
    private Selection? selection;
    private IWallpaperCover? cover;
    private CancellationTokenSource? cancellation;
    private string? staticImage;
    private Task operation = Task.CompletedTask, recovery = Task.CompletedTask;
    private bool gentle, reduced, performance, stopping, staticCoverLeaving, disposed;
    private long generation;
    public string? StatusKey { get; private set; }
    public event Action<string?>? StatusChanged;
    public Task Completion => operation;

    public WallpaperSession(IImage? fallback, IReadOnlyList<string> starterImages, bool? windows = null,
        IStaticWallpaperBackend? staticBackend = null, Func<IWallpaperCover>? coverFactory = null,
        Func<LibrarySettings, CancellationToken, Task<WallpaperMedia>>? scanner = null, TimeProvider? clock = null)
    {
        this.fallback = fallback; this.starterImages = starterImages.Take(10000).ToArray();
        this.windows = windows ?? OperatingSystem.IsWindows(); stationary = staticBackend ?? new StaticBackend();
        createCover = coverFactory ?? (() => new CoverBackend()); scan = scanner ?? Scan;
        this.clock = clock ?? TimeProvider.System;
    }

    public Task RecoverAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (disposed || !windows) return Task.CompletedTask;
        return recovery = Recover(generation);
    }
    private async Task Recover(long version)
    {
        var restored = await stationary.RecoverAsync();
        if (Current(version)) Report(!restored ? "WallpaperRestoreFailed" : stationary.Status == WallpaperStatus.Recovered ? "WallpaperRecovered" : null);
    }

    public void Update(bool enabled, bool dynamic, IReadOnlyList<WallpaperMonitor> monitors, LibrarySettings library,
        bool gentle, bool reducedMotion, bool performanceMode, WallpaperOptions? options = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (disposed) return;
        this.gentle = gentle; reduced = reducedMotion; performance = performanceMode;
        options ??= new();
        var interval = Math.Clamp(options.IntervalSeconds, dynamic && options.Collage ? 30 : 10, 86400);
        if (rotate != options.Rotate || rotationInterval != interval) rotationAt = clock.GetTimestamp();
        rotate = options.Rotate; rotationInterval = interval;
        var choice = WallpaperChoice.From(options);
        cover?.Configure(gentle, reduced, performance);
        if (!enabled || !windows) { if (selection is not null) _ = StopAsync(immediate: false); return; }
        if (selection?.Matches(dynamic, monitors, library, choice) == true)
        {
            if (rotate && clock.GetElapsedTime(rotationAt).TotalSeconds >= rotationInterval) Shuffle();
            return;
        }
        var next = new Selection(dynamic, monitors.Take(9).ToArray(), new()
        {
            ImageFolders = library.ImageFolders.Take(32).ToList(), VideoFolders = library.VideoFolders.Take(32).ToList(),
            ExcludedFiles = library.ExcludedFiles.Take(10000).ToList(), IncludedFiles = library.IncludedFiles.Take(10000).ToList(),
            FolderSelections = library.FolderSelections.Take(10000).ToDictionary()
        }, choice);
        var barrier = StopAsync(); selection = next; var version = ++generation;
        cancellation = new(); Report(null);
        operation = Start(next, version, cancellation.Token, barrier);
        rotationAt = clock.GetTimestamp();
    }

    public bool Shuffle()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (disposed || !windows || stopping || selection is not { } own || !operation.IsCompleted) return false;
        rotationAt = clock.GetTimestamp();
        Cancel(cancellation); cancellation = new(); var version = ++generation;
        operation = Start(own, version, cancellation.Token, Task.CompletedTask); return true;
    }

    private async Task Start(Selection own, long version, CancellationToken token, Task barrier)
    {
        IWallpaperCover? replaced = null;
        try
        {
            await barrier; await recovery;
            if (!Current(version)) return;
            stopping = false;
            if (stationary.Status == WallpaperStatus.RestoreFailed) { Report("WallpaperRestoreFailed"); return; }
            var scanLibrary = own.Library;
            if (!string.IsNullOrWhiteSpace(own.Choice.SourceFolder)) scanLibrary = new()
            {
                ImageFolders = [own.Choice.SourceFolder], VideoFolders = [own.Choice.SourceFolder],
                ExcludedFiles = own.Library.ExcludedFiles, IncludedFiles = own.Library.IncludedFiles, FolderSelections = own.Library.FolderSelections
            };
            var media = await scan(scanLibrary, token);
            if (!Current(version) || token.IsCancellationRequested) return;
            var selectedImages = media.Images.Where(path => MediaLibrary.IsLocalPath(path) && own.Library.Includes(path)
                && (own.Choice.Collage || SupportedImage(path))).ToArray();
            if (selectedImages.Length == 0) selectedImages = starterImages.Where(path => own.Library.Includes(path)
                && (own.Choice.Collage || SupportedImage(path))).ToArray();
            var selected = new WallpaperMedia(selectedImages,
                media.Videos.Where(path => MediaLibrary.IsLocalPath(path) && own.Library.Includes(path)).ToArray());
            if (!DynamicWallpaperService.ValidMonitors(own.Monitors)) { Report("WallpaperUnavailable"); return; }
            if (own.Dynamic && own.Choice.Collage)
            {
                if (fallback is null || selected.Images.Count < 2) { Report("WallpaperUnavailable"); return; }
                replaced = cover; var animated = CreateCover(version); cover = animated;
                var options = new WallpaperOptions { LayoutMask = own.Choice.LayoutMask, Accent = own.Choice.Accent, Background = own.Choice.Background };
                if (!animated.StartCollage(own.Monitors, selected.Images, options, fallback, gentle, reduced, performance, token)
                    || !await animated.EntranceCompleted) { if (Current(version)) await Fail(version); return; }
                if (Current(version)) { Cut(replaced); replaced = null; } return;
            }
            var path = await content.SelectAsync(own.Dynamic, selected, own.Choice, own.Monitors, staticImage, token);
            if (!Current(version)) { content.ClearExcept(staticImage); return; }
            if (path is null || fallback is null || !DynamicWallpaperService.ValidMonitors(own.Monitors))
            { Report(own.Dynamic && path is null ? "NoVideos" : "WallpaperUnavailable"); return; }
            replaced = cover; var current = CreateCover(version); cover = current;
            if (!own.Dynamic) staticImage = path;
            if (!current.Start(own.Monitors, own.Dynamic ? null : path, fallback, own.Dynamic ? path : null, gentle, reduced, performance, token)
                || !await current.EntranceCompleted)
            { if (Current(version)) await Fail(version); return; }
            if (!Current(version)) return;
            Cut(replaced); replaced = null;
            if (own.Dynamic) return;
            var applied = await stationary.StartAsync(path, token);
            if (!Current(version)) return;
            if (!applied) { await Fail(version); return; }
            content.ClearExcept(path);
            staticCoverLeaving = true; await current.StopAsync(false);
            if (Current(version)) { current.Dispose(); if (ReferenceEquals(cover, current)) cover = null; }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException)
        { if (Current(version)) await Fail(version); }
        finally { Cut(replaced); }
    }

    private IWallpaperCover CreateCover(long version)
    {
        var result = createCover();
        result.StatusChanged += status =>
        {
            if (status != WallpaperStatus.Unavailable) return;
            Dispatcher.UIThread.Post(() => { if (Current(version)) _ = Fail(version); });
        };
        return result;
    }
    private async Task Fail(long version)
    {
        if (!Current(version)) return;
        var failed = selection; var stopped = StopAsync(); selection = failed; var ownVersion = generation;
        await stopped;
        if (Current(ownVersion)) Report(stationary.Status == WallpaperStatus.RestoreFailed ? "WallpaperRestoreFailed" : "WallpaperUnavailable");
    }

    public Task StopAsync(bool immediate = true)
    {
        if (!Dispatcher.UIThread.CheckAccess()) return Dispatcher.UIThread.InvokeAsync(() => StopAsync(immediate));
        if (!immediate && stopping) return operation;
        var own = selection; selection = null; var version = ++generation; stopping = true;
        var current = cover; cover = null; var source = cancellation; cancellation = null;
        var image = staticImage; staticImage = null; var leaving = staticCoverLeaving; staticCoverLeaving = false;
        if (immediate || !windows)
        {
            var restored = windows ? stationary.StopAsync() : Task.FromResult(true);
            Cancel(source); Cut(current);
            return operation = FinishImmediate(version, restored);
        }
        cancellation = new(); var token = cancellation.Token;
        return operation = StopGently(own, current, source, image, leaving, version, token);
    }
    private async Task FinishImmediate(long version, Task<bool> restored)
    {
        var success = await restored;
        if (Current(version)) { stopping = false; if (success) content.ClearExcept(null); Report(success ? null : "WallpaperRestoreFailed"); }
    }
    private async Task StopGently(Selection? own, IWallpaperCover? current, CancellationTokenSource? source,
        string? image, bool leaving, long version, CancellationToken token)
    {
        try
        {
            if (image is not null && own is not null && fallback is not null)
            {
                if (leaving && current is not null)
                { await current.StopAsync(false); current.Dispose(); current = null; if (!Current(version)) return; }
                var reused = current is not null;
                current ??= CreateCover(version); cover = current;
                if (!reused && !current.Start(own.Monitors, image, fallback, null, gentle, reduced, performance, token))
                { current.Dispose(); current = null; cover = null; }
                else await current.EntranceCompleted;
            }
            else cover = current;
            if (!Current(version)) return;
            var success = await stationary.StopAsync();
            if (!Current(version)) return;
            if (current is not null) await current.StopAsync(false);
            if (Current(version)) { stopping = false; if (success) content.ClearExcept(null); Report(success ? null : "WallpaperRestoreFailed"); }
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException)
        {
            if (Current(version))
            {
                var restored = await stationary.StopAsync();
                if (Current(version)) { stopping = false; Report(restored ? "WallpaperUnavailable" : "WallpaperRestoreFailed"); }
            }
        }
        finally
        {
            Cancel(source); try { current?.Dispose(); } catch (Exception) { }
            if (ReferenceEquals(cover, current)) cover = null;
        }
    }
    private bool Current(long version) => !disposed && version == generation;
    private static void Cancel(CancellationTokenSource? source)
    {
        try { source?.Cancel(); } catch (ObjectDisposedException) { }
        source?.Dispose();
    }
    private static void Cut(IWallpaperCover? current)
    {
        try { current?.StopAsync(true); } catch (Exception) { }
        try { current?.Dispose(); } catch (Exception) { }
    }
    private void Report(string? key) { if (StatusKey != key) { StatusKey = key; StatusChanged?.Invoke(key); } }
    private static bool SupportedImage(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp";
    private static Task<WallpaperMedia> Scan(LibrarySettings library, CancellationToken token) => Task.Run(() =>
    {
        var media = MediaLibrary.Scan(library.ImageFolders, library.VideoFolders, cancellationToken: token);
        return new WallpaperMedia(media.Images, media.Videos);
    }, token);
    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (disposed) return;
        var stop = StopAsync(); disposed = true; stationary.Dispose(); operation = Task.WhenAll(stop, stationary.Completion);
    }

    private sealed class StaticBackend : IStaticWallpaperBackend
    {
        private readonly StaticWallpaperService service = new();
        public WallpaperStatus Status => service.Status;
        public Task Completion => service.Completion;
        public Task<bool> StartAsync(string path, CancellationToken token) => service.StartAsync(path, token);
        public Task<bool> StopAsync() => service.StopAsync();
        public Task<bool> RecoverAsync() => service.RecoverAsync();
        public void Dispose() => service.Dispose();
    }
    private sealed class CoverBackend : IWallpaperCover
    {
        private readonly DynamicWallpaperService service = new();
        public Task<bool> EntranceCompleted => service.EntranceCompleted;
        public event Action<WallpaperStatus>? StatusChanged { add => service.StatusChanged += value; remove => service.StatusChanged -= value; }
        public bool Start(IReadOnlyList<WallpaperMonitor> monitors, string? image, IImage fallback, string? video, bool gentle, bool reduced, bool performance, CancellationToken token)
            => service.Start(monitors, image, fallback, video, gentle, reduced, performance, token);
        public bool StartCollage(IReadOnlyList<WallpaperMonitor> monitors, IReadOnlyList<string> images, WallpaperOptions options,
            IImage fallback, bool gentle, bool reduced, bool performance, CancellationToken token)
            => service.StartCollage(monitors, images, WallpaperChoice.From(options), fallback, gentle, reduced, performance, token);
        public void Configure(bool gentle, bool reduced, bool performance) => service.Configure(gentle, reduced, performance);
        public Task StopAsync(bool immediate) => service.StopAsync(immediate);
        public void Dispose() => service.Dispose();
    }
}
