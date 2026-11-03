using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CCP.Classic.App.Effects;
using CCP.Classic.App.Media;
using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class EffectBubbleTests
{
    private static ClassicSettings Settings(BubbleEffectKind kind)
    {
        var settings = new ClassicSettings();
        settings.Effects["bubble"].Values["rate"] = 30;
        settings.Effects["bubble"].Values["effects"] = 1;
        settings.Effects["bubble"].Values["chance"] = 100;
        settings.Effects["bubble"].Values["kind"] = (int)kind;
        return settings;
    }

    [Fact]
    public void FlashPopCarriesTheExactPreviewImageAndFiresOnlyOnce()
    {
        var timeline = new InteractiveEffectsTimeline(Settings(BubbleEffectKind.Flash));
        timeline.SetImageCount(5); timeline.Start(); timeline.Step(2);
        var preview = Assert.Single(timeline.Step(2.25).Bubbles);
        Assert.Equal(BubbleEffectKind.Flash, preview.Effect); Assert.InRange(preview.Image, 0, 4);
        var events = new List<InteractiveEffectEvent>(); timeline.EventOccurred += events.Add;
        Assert.True(timeline.Pop(preview.Id)); Assert.False(timeline.Pop(preview.Id));
        Assert.Equal(preview.Image, Assert.Single(events).Bubble!.Image);
        Assert.Equal(BubbleEffectKind.Flash, events[0].Bubble!.Effect);
        timeline.Stop(); Assert.False(timeline.Pop(preview.Id)); Assert.Empty(timeline.Step(100).Bubbles);
    }

    [Fact]
    public void DisabledPayloadsAndUnavailableVideoCannotEnterThePool()
    {
        var settings = Settings(BubbleEffectKind.Flash); settings.Effects["flash"].Enabled = false;
        var timeline = new InteractiveEffectsTimeline(settings); timeline.SetImageCount(2); timeline.Start();
        Assert.Equal(BubbleEffectKind.None, Assert.Single(timeline.Step(2).Bubbles).Effect);
        settings = Settings(BubbleEffectKind.Video); settings.Effects["video"].Enabled = true;
        timeline = new(settings); timeline.Start();
        Assert.Equal(BubbleEffectKind.None, Assert.Single(timeline.Step(2).Bubbles).Effect);
        timeline.VideoAvailable = true; settings.Effects["video"].Values["strict"] = 1;
        Assert.All(timeline.Step(4).Bubbles, bubble => Assert.Equal(BubbleEffectKind.None, bubble.Effect));
    }

    [Fact]
    public void PayloadCapacityIsFourAndDisablingItCancelsPendingPops()
    {
        var settings = Settings(BubbleEffectKind.Spiral); var timeline = new InteractiveEffectsTimeline(settings);
        timeline.Start(); for (var time = 2; time <= 10; time += 2) timeline.Step(time);
        var frame = timeline.Step(10.25);
        Assert.Equal(4, frame.Bubbles.Count(b => b.Effect != BubbleEffectKind.None));
        Assert.Single(frame.Bubbles, b => b.Effect == BubbleEffectKind.None);
        settings.Effects["bubble"].Values["effects"] = 0;
        var events = new List<InteractiveEffectEvent>(); timeline.EventOccurred += events.Add;
        Assert.True(timeline.Pop(frame.Bubbles[0].Id));
        Assert.Equal(BubbleEffectKind.None, Assert.Single(events).Bubble!.Effect);
    }

    [Fact]
    public Task FlashFaceIsEllipseClippedAndCallerKeepsItsBitmap() => UiDispatcher.Run(() =>
    {
        using var image = RedImage(); var view = new VisualEffectsControl();
        view.UpdateFrame(new EffectFrame { ReducedMotion = true, BubbleImages = [image], Interactive = new(
            [new(1, .1, .1, .8, 1, true, BubbleEffectKind.Flash, 0)], null) });
        view.Measure(new Size(300, 300)); view.Arrange(new Rect(0, 0, 300, 300));
        using var target = new RenderTargetBitmap(new PixelSize(300, 300)); target.Render(view);
        var pixels = new int[300 * 300]; var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { target.CopyPixels(new PixelRect(0, 0, 300, 300), pinned.AddrOfPinnedObject(), pixels.Length * 4, 300 * 4); }
        finally { pinned.Free(); }
        var centre = (uint)pixels[150 * 300 + 150];
        Assert.InRange((centre >> 16) & 255, 240u, 255u);
        Assert.InRange((centre >> 8) & 255, 0u, 40u); Assert.InRange(centre & 255, 0u, 40u);
        var corner = (uint)pixels[80 * 300 + 80];
        Assert.False(((corner >> 16) & 255) > 240 && ((corner >> 8) & 255) < 40 && (corner & 255) < 40);
        view.UpdateFrame(EffectFrame.Empty); using var stillOwned = image.Lock(); Assert.NotEqual(IntPtr.Zero, stillOwned.Address);
    });

    [Fact]
    public Task AllBubbleFacesHaveADeterministicNativeRenderProof() => UiDispatcher.Run(() =>
    {
        using var image = new Bitmap(new StarterPack().Images[0]);
        var kinds = Enum.GetValues<BubbleEffectKind>();
        var cues = kinds.Select((kind, index) => new BubbleCue(index + 1, .01 + index * .14, .15, .44,
            1, true, kind, kind == BubbleEffectKind.Flash ? 0 : -1)).ToArray();
        var view = new VisualEffectsControl();
        view.UpdateFrame(new EffectFrame { ReducedMotion = true, BubbleImages = [image], Interactive = new(cues, null) });
        var panel = new Border { Width = 1280, Height = 400, Background = new SolidColorBrush(Color.Parse("#151321")), Child = view };
        panel.Measure(new Size(1280, 400)); panel.Arrange(new Rect(0, 0, 1280, 400));
        using var target = new RenderTargetBitmap(new PixelSize(1280, 400)); target.Render(panel);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.Parent is not null && !File.Exists(Path.Combine(root.FullName, "CCP.Classic.slnx"))) root = root.Parent;
        var folder = Environment.GetEnvironmentVariable("CCP_CLASSIC_PROOF_DIR") ?? Path.Combine(root.FullName, "artifacts", "bubble-proof");
        Directory.CreateDirectory(folder); target.Save(Path.Combine(folder, "bubble-faces.png"), PngBitmapEncoderOptions.Default);
    });

    private static WriteableBitmap RedImage()
    {
        var image = new WriteableBitmap(new PixelSize(8, 8), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var locked = image.Lock(); var row = Enumerable.Repeat(unchecked((int)0xFFFF0000), 8).ToArray();
        for (var y = 0; y < 8; y++) Marshal.Copy(row, 0, locked.Address + y * locked.RowBytes, 8);
        return image;
    }
}
