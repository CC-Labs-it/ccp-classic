using Avalonia;
using Avalonia.Media.Imaging;
using CCP.Classic.App.Effects;
using CCP.Classic.App.Media;
using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class FlashExitTests
{
    [Fact]
    public void OriginalMixNeverRepeatsAndAllFivePosesEndTransparently()
    {
        var random = new Random(27); FlashExitStyle? last = null; var seen = new HashSet<FlashExitStyle>();
        for (var i = 0; i < 100; i++)
        {
            var next = FlashExit.Pick(FlashExitStyle.Mix, last, random)!.Value;
            Assert.NotEqual(last, next); seen.Add(next); last = next;
        }
        Assert.Equal(5, seen.Count); Assert.Null(FlashExit.Pick(FlashExitStyle.None, last, random));
        foreach (var style in FlashExit.Pool)
        {
            var state = FlashExit.Begin(style, 1, false, false, false);
            Assert.Equal(1, FlashExit.Sample(state).Alpha);
            Assert.InRange(state.DurationSec, .22, .48);
            Assert.Equal(0, FlashExit.Sample(state with { ElapsedSec = state.DurationSec }).Alpha, 8);
        }
        Assert.Equal(10, FlashExit.SparkCount(FlashExit.Begin(FlashExitStyle.Pop, 1, false, false, false)));
    }

    [Fact]
    public void ClicksUseTheSelectedExitButExpiryKeepsItsFadeAndStopClearsEverything()
    {
        var settings = new ClassicSettings(); settings.Effects["flash"].Values["exit"] = (int)FlashExitStyle.Spiral;
        var timeline = new EffectsTimeline(settings, 2, 5); timeline.Start(); timeline.Step(0);
        Assert.True(timeline.TriggerFlash(1)); Assert.False(timeline.TriggerFlash(2));
        var flash = Assert.Single(timeline.Step(.5).Flashes); Assert.Equal(1, flash.Image);
        Assert.True(timeline.Dismiss(flash.Id)); Assert.False(timeline.Dismiss(flash.Id));
        var exiting = Assert.Single(timeline.Step(.6).Flashes); Assert.Equal(FlashExitStyle.Spiral, exiting.Exit!.Style);
        var captured = exiting.Exit;
        Assert.Empty(timeline.Step(.9).Flashes); Assert.Equal(captured, exiting.Exit);
        Assert.True(timeline.TriggerPinkPulse()); Assert.True(timeline.Step(1.2).PinkOpacity > 0);
        timeline.Stop(); Assert.Empty(timeline.Step(10).Flashes); Assert.False(timeline.TriggerPinkPulse());
        var timed = new EffectsTimeline(settings, 1, 5); timed.Start(); timed.Step(2);
        Assert.Null(Assert.Single(timed.Step(6.4).Flashes).Exit);
    }

    [Fact]
    public void MotionPreferencesRemoveSpinFlickerAndSparksAndCanSoftenALiveExit()
    {
        foreach (var style in FlashExit.Pool)
        {
            foreach (var state in new[] { FlashExit.Begin(style, 1, true, false, false), FlashExit.Begin(style, 1, false, true, false) })
            {
                var pose = FlashExit.Sample(state with { ElapsedSec = state.DurationSec / 2 });
                Assert.Equal((1d, 1d, 0d, 0d, 0d), (pose.ScaleX, pose.ScaleY, pose.RotationDeg, pose.Whiten, pose.Glitch));
                Assert.Equal(0, FlashExit.SparkCount(state));
            }
        }
        var original = FlashExit.Begin(FlashExitStyle.Spiral, 0, false, false, false) with { ElapsedSec = .15 };
        var softened = FlashExit.Soften(original, .75);
        Assert.Equal(FlashExit.Sample(original), FlashExit.Sample(softened));
        Assert.Equal(FlashExit.Sample(original).RotationDeg, FlashExit.Sample(softened with { ElapsedSec = .3 }).RotationDeg);
        var settings = new ClassicSettings { GentleVisuals = true };
        settings.Effects["flash"].Values["exit"] = (int)FlashExitStyle.None;
        var timeline = new EffectsTimeline(settings, 1, 5); timeline.Start(); timeline.Step(2);
        var visible = Assert.Single(timeline.Step(3).Flashes); Assert.True(timeline.Dismiss(visible.Id));
        Assert.Equal(FlashExitStyle.Mix, Assert.Single(timeline.Step(3.1).Flashes).Exit!.Style);
    }

    [Fact]
    public Task FiveOriginalOutroMidframesRenderDistinctlyAndExportProofs() => UiDispatcher.Run(() =>
    {
        var starter = new StarterPack(); using var image = new Bitmap(starter.Images[0]);
        var signatures = new HashSet<string>();
        foreach (var style in FlashExit.Pool)
        {
            var state = FlashExit.Begin(style, 42, false, false, false);
            state = state with { ElapsedSec = state.DurationSec * .5 };
            var view = new VisualEffectsControl { Width = 420, Height = 340 };
            view.UpdateFrame(new() { Flashes = [new(image, new(.22, .18, .55, .62), .85, state)] });
            view.Measure(new Size(420, 340)); view.Arrange(new Rect(0, 0, 420, 340));
            using var bitmap = new RenderTargetBitmap(new PixelSize(420, 340), new Vector(96, 96)); bitmap.Render(view);
            using var encoded = new MemoryStream(); bitmap.Save(encoded, PngBitmapEncoderOptions.Default);
            Assert.True(encoded.Length > 500); signatures.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(encoded.ToArray())));
            if (Environment.GetEnvironmentVariable("CCP_CLASSIC_PROOF_DIR") is { } folder)
            { Directory.CreateDirectory(folder); bitmap.Save(Path.Combine(folder, "flash-outro-" + style + ".png"), PngBitmapEncoderOptions.Default); }
        }
        Assert.Equal(5, signatures.Count);
    });
}
