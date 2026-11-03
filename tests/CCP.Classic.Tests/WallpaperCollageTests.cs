using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using CCP.Classic.App.Wallpaper;
using CCP.Classic.App.Media;
using CCP.Classic.Core;
using Avalonia.Platform;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class WallpaperCollageTests
{
    // Same synthetic red/green/blue4px animation as AnimatedImageTests, with original100/200/300ms timing.
    private const string ValidGif = "R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQICgAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQIFAAAACwAAAAABAAEAIEAgAAAAAAAAAAAAAAICQABCBxIsCCAgAAh+QQIHgAAACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7";

    [Fact]
    public void WallpaperPresetCaptureNormalizesAndAppliesIndependentCustomOptions()
    {
        var settings = new ClassicSettings(); settings.Wallpaper.Collage = true; settings.Wallpaper.LayoutMask = 2;
        settings.Wallpaper.IntervalSeconds = 0; settings.Wallpaper.Accent = "#123456";
        var saved = FeatureCustomization.Capture(settings); Assert.Equal(10, saved.Wallpaper.IntervalSeconds);
        settings.Wallpaper.Accent = "#FFFFFF"; Assert.Equal("#123456", saved.Wallpaper.Accent);
        saved.Apply(settings); Assert.True(settings.Wallpaper.Collage); Assert.Equal(2, settings.Wallpaper.LayoutMask);
        Assert.NotSame(saved.Wallpaper, settings.Wallpaper); Assert.Equal("#123456", settings.Wallpaper.Accent);
    }

    [Fact]
    public Task SourceFrameStrideAndNativeGifTimingRemainBounded() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(); var budget = new WallpaperFrameBudget(() => { });
        using var clip = WallpaperTileClip.Decode(fixture.Files[0], true, 320, budget, TestContext.Current.CancellationToken);
        Assert.Equal(3, clip.FrameCount); Assert.Equal(0, clip.FrameIndex); Assert.True(clip.Advance(.15)); Assert.Equal(1, clip.FrameIndex);
        clip.Advance(.35); Assert.Equal(2, clip.FrameIndex); clip.Advance(.65); Assert.Equal(0, clip.FrameIndex);
        Assert.Equal(1, WallpaperTileClip.Stride(120)); Assert.Equal(2, WallpaperTileClip.Stride(121));
        Assert.Equal(8, WallpaperTileClip.Stride(900)); Assert.InRange(budget.Used, 1, WallpaperFrameBudget.MaximumBytes);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => WallpaperTileClip.Decode(fixture.Files[0], true, 320, budget, cancelled.Token));
    });

    [Fact]
    public async Task ActualMovingScenesUseSourceLayoutsAndFreezeWithoutCatchingUp()
    {
        using var fixture = new Fixture(); await UiDispatcher.Run(() => { });
        var monitor = new WallpaperMonitor(new PixelRect(0, 0, 960, 540), 1);
        var pool = fixture.Files.Concat(new StarterPack().Images.Take(2)).ToArray();
        using var scene = await WallpaperCollageSet.BuildAsync(pool, [monitor], new(null, null, true, 1, "#E84393", "#08080C"),
            TestContext.Current.CancellationToken, 13, () => { });
        Assert.InRange(scene.MovingTiles, 1, WallpaperCollageSet.AnimatedCap); Assert.InRange(scene.DecodedBytes, 1, WallpaperFrameBudget.MaximumBytes);
        scene.Advance(0, true, 1); Assert.True(scene.Advance(.15, true, 1));
        Assert.False(scene.Advance(100, false, 1)); Assert.False(scene.Advance(100.01, true, 1));
        await UiDispatcher.Run(() =>
        {
            var image = new Image { Source = scene.For(monitor), Width = 960, Height = 540 };
            image.Measure(new Size(960, 540)); image.Arrange(new Rect(0, 0, 960, 540));
            using var target = new RenderTargetBitmap(new PixelSize(960, 540)); target.Render(image);
            var folder = Environment.GetEnvironmentVariable("CCP_CLASSIC_PROOF_DIR"); if (string.IsNullOrEmpty(folder)) return;
            Directory.CreateDirectory(folder); target.Save(Path.Combine(folder, "wallpaper-moving-collage.png"), PngBitmapEncoderOptions.Default);
        });
    }

    [Fact]
    public async Task CanceledNativeHostBuildCannotAttachOrPublishAfterAnImmediateStop()
    {
        using var fixture = new Fixture(); var adapter = new Desktop(); DynamicWallpaperService service = null!; WriteableBitmap fallback = null!;
        Task retired = Task.CompletedTask;
        await UiDispatcher.Run(() =>
        {
            fallback = new(new PixelSize(2, 2), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
            service = new(adapter);
            Assert.True(service.StartCollage([new(new PixelRect(0, 0, 320, 240), 1)], fixture.Files,
                new(null, null, true, 7, "#E84393", "#08080C"), fallback, false, false, false, TestContext.Current.CancellationToken));
            retired = service.CollageBuildCompletion; Assert.True(service.StopAsync(true).IsCompletedSuccessfully);
        });
        try
        {
            await retired.WaitAsync(TestContext.Current.CancellationToken);
            await UiDispatcher.Run(() => { Assert.Equal(0, adapter.Attaches); Assert.Equal(0, service.HostCount); });
            Assert.False(await service.Ready.WaitAsync(TestContext.Current.CancellationToken));
            Assert.False(await service.EntranceCompleted.WaitAsync(TestContext.Current.CancellationToken));
        }
        finally { await UiDispatcher.Run(() => { service.Dispose(); fallback.Dispose(); }); }
    }

    [Fact]
    public async Task MemoryCeilingStopsTheWholeBakeAndCancellationCannotPublishAPartialScene()
    {
        using var fixture = new Fixture(); var monitor = new WallpaperMonitor(new PixelRect(0, 0, 320, 240), 1);
        var choice = new WallpaperChoice(null, null, true, 7, "#E84393", "#08080C");
        await Assert.ThrowsAsync<WallpaperResourceException>(() => WallpaperCollageSet.BuildAsync(fixture.Files, [monitor], choice,
            TestContext.Current.CancellationToken, 1, () => throw new IOException("Resource ceiling")));
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WallpaperCollageSet.BuildAsync(fixture.Files, [monitor], choice, stop.Token));
        var budget = new WallpaperFrameBudget(() => { }); budget.Reserve(WallpaperFrameBudget.MaximumBytes);
        Assert.False(budget.CanReserve(1)); Assert.Throws<WallpaperResourceException>(() => budget.Reserve(1));
    }

    [Fact]
    public async Task VerifiedPublicArtAndOriginalSpiralsShowTheActualMovingCollageComposition()
    {
        using var fixture = new Fixture(); var pool = new List<string>();
        await UiDispatcher.Run(() =>
        {
            foreach (var name in new[] { "flash", "pink", "spiral", "drain", "bubble", "sub", "wall" })
            {
                using var stream = AssetLoader.Open(new Uri("avares://CCP.Classic.App/Assets/" + name + ".png"));
                pool.Add(fixture.Write(name + ".png", stream));
            }
        });
        pool.AddRange(new StarterPack().Images);
        var spirals = Path.Combine(AppContext.BaseDirectory, "Starter", "Spirals");
        pool.Add(Path.Combine(spirals, "spiral.gif")); pool.Add(Path.Combine(spirals, "spiral_corner.gif"));
        var monitor = new WallpaperMonitor(new PixelRect(0, 0, 1280, 720), 1);
        using var scene = await WallpaperCollageSet.BuildAsync(pool, [monitor], new(null, null, true, 1, "#E84393", "#08080C"),
            TestContext.Current.CancellationToken, 43, () => { });
        Assert.InRange(scene.MovingTiles, 1, WallpaperCollageSet.AnimatedCap);
        scene.Advance(0, true, 1); scene.Advance(.35, true, 1);
        await UiDispatcher.Run(() =>
        {
            var image = new Image { Source = scene.For(monitor), Width = 1280, Height = 720 };
            image.Measure(new Size(1280, 720)); image.Arrange(new Rect(0, 0, 1280, 720));
            using var target = new RenderTargetBitmap(new PixelSize(1280, 720)); target.Render(image);
            var folder = Environment.GetEnvironmentVariable("CCP_CLASSIC_PROOF_DIR"); if (string.IsNullOrEmpty(folder)) return;
            Directory.CreateDirectory(folder); target.Save(Path.Combine(folder, "wallpaper-moving-collage-public.png"), PngBitmapEncoderOptions.Default);
        });
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "CCP Classic collage tests", Guid.NewGuid().ToString("N"));
        public string[] Files { get; }
        public Fixture()
        {
            Directory.CreateDirectory(root); Files = [Path.Combine(root, "one.gif"), Path.Combine(root, "two.gif")];
            foreach (var path in Files) File.WriteAllBytes(path, Convert.FromBase64String(ValidGif));
        }
        public string Write(string name, Stream source)
        { var path = Path.Combine(root, name); using var output = File.Create(path); source.CopyTo(output); return path; }
        public void Dispose() => Directory.Delete(root, true);
    }
    private sealed class Desktop : IDesktopWallpaperAdapter
    {
        public int Attaches;
        public bool IsSupported => true;
        public bool Attach(Window window, WallpaperMonitor monitor) { Attaches++; return true; }
        public WallpaperDesktopState ReadState(Window window, WallpaperMonitor monitor) => new(true);
        public void Detach(Window window) { }
    }
}
