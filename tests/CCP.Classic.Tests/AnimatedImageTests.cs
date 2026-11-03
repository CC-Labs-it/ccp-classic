using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using CCP.Classic.App.Controls;
using CCP.Classic.App.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class AnimatedImageTests
{
    // Three original 4x4 test frames, red/green/blue, at100/200/300ms. No external assets.
    private const string Gif = "R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQICgAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQIFAAAACwAAAAABAAEAIEAgAAAAAAAAAAAAAAICQABCBxIsCCAgAAh+QQIHgAAACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7";
    private const string DeltaGif = "R0lGODlhBAAEAIEAAAAAAP8AAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQFCgAAACwAAAAABAAEAAAICQADCBxIsCCAgAAh+QQNFAAAACwAAAAABAAEAIEAAAAA/wAAAAAAAAAIDgADBAAAQCBBgwUHAggIACH5BAUeAAAALAAAAAAEAAQAgQAAAAAA/wAAAAAAAAgJAAEIHEiwYICAADs=";

    [Fact]
    public Task NativeFramesKeepTimingLoopAndRestorePreviousDisposal() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(); using var clip = LocalImageClip.Decode(fixture.Write(Gif), TestContext.Current.CancellationToken);
        Assert.Equal(3, clip.FrameCount); Assert.Equal(0xFFFF0000u, Pixel(clip));
        Assert.Equal(.299, clip.Brightness!.Value, 3);
        clip.Advance(.15); Assert.Equal(1, clip.FrameIndex); Assert.Equal(0xFF008000u, Pixel(clip));
        clip.Advance(.35); Assert.Equal(0xFF0000FFu, Pixel(clip));
        Assert.Equal(.299, clip.Brightness!.Value, 3);
        clip.Advance(.65); Assert.Equal(0xFFFF0000u, Pixel(clip));
        using var delta = LocalImageClip.Decode(fixture.Write(DeltaGif, "delta.gif"), TestContext.Current.CancellationToken);
        delta.Advance(.15); Assert.Equal(0xFF00FF00u, Pixel(delta, 0, 0)); Assert.Equal(0xFFFF0000u, Pixel(delta, 3, 0));
        delta.Advance(.35); Assert.Equal(0xFFFF0000u, Pixel(delta, 0, 0)); Assert.Equal(0xFF0000FFu, Pixel(delta, 3, 3));
    });

    [Fact]
    public Task FiniteLoopsStopOnTheirLastFrameAndDecodeHonorsCancellation() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(); var bytes = Convert.FromBase64String(Gif);
        var loop = Array.IndexOf(bytes, (byte)0x21); // NETSCAPE loop extension starts here.
        bytes[loop + 16] = 1;
        var path = fixture.Write(bytes); using var clip = LocalImageClip.Decode(path, TestContext.Current.CancellationToken);
        clip.Advance(.65); Assert.Equal(0, clip.FrameIndex);
        clip.Advance(1.3); Assert.Equal(2, clip.FrameIndex);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => LocalImageClip.Decode(path, cancelled.Token));
    });

    [Fact]
    public async Task OneProxySharesAnimationFramesAndMotionGatesFreezeWithoutCatchingUp()
    {
        using var fixture = new Fixture(); LocalImageLibrary library = null!; Bitmap fallback = null!;
        await UiDispatcher.Run(() =>
        {
            fallback = new WriteableBitmap(new(1, 1), new(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
            library = new([fixture.Write(Gif)], fallback); _ = library[0].Size;
        });
        try
        {
            await library.Completion.WaitAsync(TestContext.Current.CancellationToken);
            await UiDispatcher.Run(() =>
            {
                var preview = library[0]; var flash = library[0]; Assert.Same(preview, flash);
                library.AdvanceAnimations(.15, true); Assert.Equal(0xFF008000u, Pixel(preview)); Assert.Equal(Pixel(preview), Pixel(flash));
                library.AdvanceAnimations(3, false); Assert.Equal(0xFF008000u, Pixel(preview));
                library.AdvanceAnimations(3.2, true); Assert.Equal(0xFF0000FFu, Pixel(preview));
                Assert.InRange(library.CachedBytes, 1, 64 * 1024 * 1024);
            });
        }
        finally { library.Dispose(); fallback.Dispose(); }
    }

    [Fact]
    public Task LargeCanvasAnimationsAdaptResolutionWithoutDroppingFrames() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(); var bytes = Convert.FromBase64String(Gif);
        var firstFrame = Enumerable.Range(0, bytes.Length - 3).First(i => bytes[i] == 0x21 && bytes[i + 1] == 0xF9);
        var header = bytes[..firstFrame]; header[6] = 0x60; header[7] = 0x09; header[8] = 0x40; header[9] = 0x06; //2400x1600
        var frames = bytes[firstFrame..^1];
        var source = header.Concat(Enumerable.Range(0, 10).SelectMany(_ => frames)).Append((byte)0x3B).ToArray();
        using var clip = LocalImageClip.Decode(fixture.Write(source), TestContext.Current.CancellationToken);
        Assert.Equal(30, clip.FrameCount); Assert.InRange(clip.DecodedBytes, 1, LocalImageClip.MaximumBytes);
        Assert.InRange(clip.Size.Width, 1, 1024); Assert.InRange(clip.Size.Height, 1, 1024);
        Assert.InRange(clip.Size.Width / clip.Size.Height, 1.49, 1.51);
    });

    [Fact]
    public async Task LibraryPreviewAnimatesAndFreezesUnderMotionPreferences()
    {
        using var fixture = new Fixture(); var settings = new ClassicSettings(); var clock = new Clock();
        Window owner = null!; LibraryPreviewWindow window = null!; Image image = null!;
        await UiDispatcher.Run(() =>
        {
            owner = new(); owner.Show();
            window = new(new(fixture.Write(Gif), "Images"), settings, new LocalizationService("en"), _ => { },
                _ => throw new InvalidOperationException("An image must not open video playback"), clock);
            window.Show(owner); image = window.GetVisualDescendants().OfType<Image>().Single();
        });
        try
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var ready = false; await UiDispatcher.Run(() => ready = image.Source is LocalImageClip);
                if (ready) break;
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
            await UiDispatcher.Run(() =>
            {
                var clip = Assert.IsType<LocalImageClip>(image.Source);
                clock.Advance(.15); Tick(window); Assert.Equal(1, clip.FrameIndex);
                settings.GentleVisuals = true; clock.Advance(2); Tick(window); Assert.Equal(1, clip.FrameIndex);
                settings.GentleVisuals = false; settings.ReducedMotion = true; clock.Advance(2); Tick(window); Assert.Equal(1, clip.FrameIndex);
                settings.ReducedMotion = false; settings.PerformanceMode = true; clock.Advance(2); Tick(window); Assert.Equal(1, clip.FrameIndex);
                settings.PerformanceMode = false; clock.Advance(.2); Tick(window); Assert.Equal(2, clip.FrameIndex);
                window.Close(); Assert.Null(image.Source); Assert.Equal(default, clip.Size);
            });
        }
        finally { await UiDispatcher.Run(() => { window.Close(); owner.Close(); }); }
    }

    private static void Tick(LibraryPreviewWindow window) => typeof(LibraryPreviewWindow)
        .GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
    private static uint Pixel(IImage image, int x = 1, int y = 1)
    {
        using var target = new RenderTargetBitmap(new PixelSize(4, 4));
        using (var context = target.CreateDrawingContext()) context.DrawImage(image, new Rect(image.Size), new Rect(0, 0, 4, 4));
        var pixels = new int[16]; var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { target.CopyPixels(new PixelRect(0, 0, 4, 4), pinned.AddrOfPinnedObject(), 64, 16); }
        finally { pinned.Free(); }
        return unchecked((uint)pixels[y * 4 + x]);
    }
    private sealed class Clock : TimeProvider
    {
        private long value;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => value;
        public void Advance(double seconds) => value += TimeSpan.FromSeconds(seconds).Ticks;
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(root);
        public string Write(string data, string name = "test.gif") => Write(Convert.FromBase64String(data), name);
        public string Write(byte[] data, string name = "test.gif") { var path = Path.Combine(root, name); File.WriteAllBytes(path, data); return path; }
        public void Dispose() => Directory.Delete(root, true);
    }
}
