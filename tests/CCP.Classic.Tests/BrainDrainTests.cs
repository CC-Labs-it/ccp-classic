using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using CCP.Classic.App.Controls;
using CCP.Classic.App.Effects;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class BrainDrainTests
{
    [Fact]
    public void OptionsClampAndVisualZeroHasNoFloor()
    {
        var options = new BrainDrainOptions { BlurStrength = -1, AudioFolders = ["https://invalid.example/audio", "relative"] };
        options.Normalize(); Assert.Equal(0, options.BlurStrength); Assert.Empty(options.AudioFolders);
        Assert.Equal(0, BrainDrainFrameProcessor.AlphaFor(0));
        Assert.Equal(26 / 255d, BrainDrainFrameProcessor.AlphaFor(1));
        Assert.Equal(217 / 255d, BrainDrainFrameProcessor.AlphaFor(100));
        Assert.Equal(100 * .14 / 4 / 3, BrainDrainFrameProcessor.SigmaFor(100, 4));
    }

    [Fact]
    public Task GdiUnusedAlphaCannotHideTheCaptureOrSwapItsColours() => UiDispatcher.Run(() =>
    {
        using var source = new FakeCapture(); source.Fill(unchecked((int)0x00EF2911));
        using var processor = new BrainDrainFrameProcessor();
        Assert.Null(processor.Process(source.Frame, new(0, false, 0)));
        using var frame = processor.Process(source.Frame, new(1, false, 0));
        Assert.NotNull(frame); Assert.Equal(0xFFEF2911u, Pixel(frame, 32, 32));
        Assert.Equal(0, processor.NoiseTilesCreated);
    });

    [Fact]
    public Task BlurAndCachedPerlinMeltChangeTheActualImage() => UiDispatcher.Run(() =>
    {
        using var source = new FakeCapture(); source.Checker(); using var processor = new BrainDrainFrameProcessor();
        using var plain = processor.Process(source.Frame, new(100, false, 0));
        using var first = processor.Process(source.Frame, new(100, true, .25));
        using var second = processor.Process(source.Frame, new(100, true, 3.25));
        Assert.NotNull(plain); Assert.NotNull(first); Assert.NotNull(second);
        var blurred = Pixel(plain, 31, 31) & 255;
        Assert.InRange(blurred, 1u, 254u); Assert.Equal(1, processor.NoiseTilesCreated);
        Assert.False(Pixels(plain).SequenceEqual(Pixels(first))); Assert.False(Pixels(first).SequenceEqual(Pixels(second)));
    });

    [Fact]
    public Task EditorKeepsVisualZeroIndependentOfAudioAndRefreshIsExplicit() => UiDispatcher.Run(() =>
    {
        var settings = new ClassicSettings(); var changes = 0; var refreshes = 0;
        var editor = new BrainDrainEditor(settings, new LocalizationService("en"), () => changes++);
        editor.RefreshRequested += () => refreshes++;
        editor.GetLogicalDescendants().OfType<Slider>().Single().Value = 0;
        foreach (var toggle in editor.GetLogicalDescendants().OfType<ToggleSwitch>()) toggle.IsChecked = true;
        var refresh = editor.GetLogicalDescendants().OfType<Button>().Single(button => button.Name == "BrainDrainRefresh");
        refresh.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(0, settings.BrainDrain.BlurStrength); Assert.True(settings.BrainDrain.MeltEnabled);
        Assert.True(settings.BrainDrain.AllowCapture); Assert.Equal(3, changes); Assert.Equal(1, refreshes);
        Assert.Empty(settings.BrainDrain.AudioFolders); Assert.False(settings.Effects["drain"].Enabled);
    });

    [Fact]
    public async Task ConstructionIsIdleAndStopNeverWaitsForOrPublishesABlockedCapture()
    {
        await UiDispatcher.Run(() => { });
        using var release = new ManualResetEventSlim(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeCapture? source = null; var calls = 0;
        using var worker = new BrainDrainCaptureWorker(() =>
        {
            Interlocked.Increment(ref calls); source = new() { OnCapture = () => { entered.TrySetResult(); release.Wait(TestContext.Current.CancellationToken); } }; return source;
        });
        Assert.Equal(0, calls); worker.Update(Request());
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            worker.Dispose(); Assert.False(worker.Completion.IsCompleted); Assert.Equal(0, worker.PendingFrames);
            release.Set(); await worker.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(0, worker.FramesPublished); Assert.Empty(worker.TakeFrames()); Assert.True(source!.Disposed);
        }
        finally { release.Set(); worker.Dispose(); await worker.Completion.WaitAsync(TestContext.Current.CancellationToken); }
    }

    [Fact]
    public async Task LatestFrameSlotsStayBoundedAcrossRepeatedCaptures()
    {
        await UiDispatcher.Run(() => { });
        using var worker = new BrainDrainCaptureWorker(() => new FakeCapture());
        worker.Update(Request() with { Screens = Enumerable.Range(0, 20).Select(index => new PixelRect(index * 64, 0, 64, 64)).ToArray() });
        try
        {
            for (var i = 0; i < 200 && worker.FramesPublished < 16; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.True(worker.FramesPublished >= 16); Assert.Equal(8, worker.PendingFrames);
            var frames = worker.TakeFrames(); Assert.Equal(8, frames.Count);
            foreach (var frame in frames.Values) frame.Dispose();
        }
        finally { worker.Dispose(); await worker.Completion.WaitAsync(TestContext.Current.CancellationToken); }
    }

    private static BrainDrainCaptureRequest Request() => new([new(0, 0, 64, 64)], new(50, false, 0), false, 30);
    private static uint Pixel(Bitmap image, int x, int y) => unchecked((uint)Pixels(image)[y * image.PixelSize.Width + x]);
    private static int[] Pixels(Bitmap image)
    {
        var pixels = new int[image.PixelSize.Width * image.PixelSize.Height]; var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { image.CopyPixels(new PixelRect(0, 0, image.PixelSize.Width, image.PixelSize.Height), pinned.AddrOfPinnedObject(), pixels.Length * 4, image.PixelSize.Width * 4); }
        finally { pinned.Free(); }
        return pixels;
    }
    private sealed class FakeCapture : IBrainDrainDesktopCapture
    {
        private readonly nint pixels = Marshal.AllocHGlobal(64 * 64 * 4);
        public Action? OnCapture;
        public bool Disposed;
        public BrainDrainCapture Frame => new(pixels, 64, 64, 64 * 4, 4);
        public FakeCapture() => Fill(unchecked((int)0x00FFFFFF));
        public void Fill(int colour) => Marshal.Copy(Enumerable.Repeat(colour, 64 * 64).ToArray(), 0, pixels, 64 * 64);
        public void Checker() => Marshal.Copy(Enumerable.Range(0, 64 * 64).Select(i => (i % 64 / 8 + i / 64 / 8) % 2 == 0 ? 0x00FFFFFF : 0).ToArray(), 0, pixels, 64 * 64);
        public void SetScreens(IReadOnlyList<PixelRect> screens) { }
        public BrainDrainCapture? Capture(PixelRect screen, bool performance) { OnCapture?.Invoke(); return Frame; }
        public void Dispose() { if (Disposed) return; Disposed = true; Marshal.FreeHGlobal(pixels); }
    }
}
