using Avalonia;
using Avalonia.Media.Imaging;
using CCP.Classic.App.Effects;
using CCP.Classic.App.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.Remix;
using SkiaSharp;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class RemixRenderingTests
{
    [Fact]
    public Task EverySourceLayoutAndAutomaticBlockHasRenderableNativeFrames() => UiDispatcher.Run(() =>
    {
        var blocks = new[] { new RemixBlock("tint", "creep", 0, 45), new RemixBlock("spiral", "through", 0, 45) { Style = 1 },
            new RemixBlock("glitch", "ghost", 0, 45), new RemixBlock("focus", "ring", 0, 45) { Path = [new(.1, .2), new(.9, .8)] } };
        foreach (var layout in RemixScene.Layouts)
        {
            using var image = Create(new(7, layout, "seamless", false, 96, 54, blocks));
            image.Advance(17d / 15, true); image.Advance(44d / 15, true);
            using var target = new RenderTargetBitmap(new PixelSize(96, 54));
            using (var drawing = target.CreateDrawingContext()) drawing.DrawImage(image, new Rect(0, 0, 96, 54));
            Assert.Equal(2, image.PresentedFrames); Assert.InRange(image.DecodedBytes, 1, 32 * 1024 * 1024);
        }
        using var source = Create(new(3, "flat", "snap", false, 96, 54,
            [new("tint", "wash", 0, 45), new("spiral", "over", 0, 45) { Style = 2 }, new("glitch", "double", 0, 45), new("focus", "dark", 0, 45)]));
        source.Advance(44d / 15, true); Assert.Equal(1, source.PresentedFrames);
    });

    [Fact]
    public Task NativeClipKeepsGifTimingAndMotionGatesWithoutCatchup() => UiDispatcher.Run(() =>
    {
        using var image = Create(); image.Advance(0, true); image.Advance(.1, true);
        Assert.Equal(2, image.PresentedFrames); image.Advance(10, false); Assert.Equal(2, image.PresentedFrames);
        image.Advance(10.1, true); Assert.Equal(3, image.PresentedFrames);
        image.Dispose(); image.Advance(11, true); Assert.Equal(default, image.Size);
        var a = new SKBitmap(1, 1); a.Erase(SKColors.Red); var b = new SKBitmap(1, 1); b.Erase(SKColors.Blue);
        using var strip = new RemixStrip([a, b], [.1, .3]);
        Assert.Same(a, strip.At(1)); Assert.Same(b, strip.At(2)); Assert.Same(a, strip.At(5));
    });

    [Fact]
    public async Task AStoppedSingleWorkerCannotPublishLatePreparedContent()
    {
        var settings = new ClassicSettings(); settings.Flash.RemixEnabled = true;
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        RemixImage? late = null;
        using var session = new RemixSession(settings, () => ["local.gif"], 1, (_, _, _) =>
        { entered.Set(); release.Wait(TestContext.Current.CancellationToken); return late = Create(); }, () => true);
        session.Start(); session.BeforeFrame(0, true);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        session.Stop(); session.Start(); session.BeforeFrame(21, true); session.Stop(); release.Set();
        await session.Completion.WaitAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(late); Assert.Equal(default, late.Size); Assert.Null(session.TryTake()); Assert.Null(session.Image);
    }

    [Fact]
    public void ScheduledRemixProducesOneCenteredCueAndNeverHydraChildren()
    {
        var settings = new ClassicSettings(); settings.Flash.RemixEnabled = true; settings.Flash.AudioEnabled = false;
        settings.Flash.Count = 8; settings.Effects["flash"].Values["hydra"] = 1;
        var calls = 0; var timeline = new EffectsTimeline(settings, 8, 0) { ScheduledRemix = () => { calls++; return 16d / 9; } };
        timeline.Start(); var cue = Assert.Single(timeline.Step(2.1).Flashes);
        Assert.True(cue.IsRemix); Assert.Equal(-1, cue.Image); Assert.Equal(1, calls);
        Assert.InRange(Math.Abs(.5 - (cue.X + cue.Width / 2)), 0, 1d / 1600);
        Assert.InRange(Math.Abs(.5 - (cue.Y + cue.Height / 2)), 0, 1d / 900);
        Assert.True(timeline.Dismiss(cue.Id)); Assert.DoesNotContain(timeline.Step(2.2).Flashes, flash => flash.Id != cue.Id);
        timeline.Stop(); settings.ReducedMotion = true; timeline.Start(); timeline.Step(2.1); Assert.Equal(1, calls);
        timeline.Stop(); settings.ReducedMotion = false; timeline.Start(); timeline.TriggerFlash(); Assert.Equal(1, calls);
    }

    private static RemixImage Create(RemixScene? scene = null)
    {
        var strips = Enumerable.Range(0, 8).Select(index =>
        {
            var bitmap = new SKBitmap(8, 8); bitmap.Erase(new SKColor((byte)(index * 30), (byte)(255 - index * 30), 140));
            return new RemixStrip([bitmap], [.3]);
        }).ToArray();
        return new(scene ?? new(1, "flat", "clean", false, 96, 54, []), strips);
    }
}
