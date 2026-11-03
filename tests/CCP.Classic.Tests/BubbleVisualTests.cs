using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class BubbleVisualTests
{
    [Theory]
    [InlineData(BubbleEffectKind.Glitch, .3, 3.96)]
    [InlineData(BubbleEffectKind.BrainDrainWash, .1, 10)]
    public void OriginalImageWashesUseTheirActualOpacityAndFadeDurations(BubbleEffectKind kind, double peak, double duration)
    {
        var clock = new BubbleVisualTimeline(); Assert.False(clock.Trigger(kind, 0, 0));
        Assert.True(clock.Trigger(kind, 0, 3)); Assert.Empty(clock.Step(0, 900, false, false, false));
        var image = Assert.Single(clock.Step(.5, 900, false, false, false)); Assert.True(image.Fullscreen);
        Assert.Equal(peak, image.Opacity, 8); Assert.InRange(image.Image, 0, 2);
        Assert.Equal(peak / 2, Assert.Single(clock.Step(duration + .35, 900, false, false, false)).Opacity, 8);
        Assert.Empty(clock.Step(duration + .701, 900, false, false, false));
    }

    [Fact]
    public void OriginalCascadeUsesSixSecondSpawnWindowFourHundredDipTilesAndBoundedFlight()
    {
        var clock = new BubbleVisualTimeline(5); clock.Trigger(BubbleEffectKind.Cascade, 0, 4);
        var first = Assert.Single(clock.Step(0, 900, false, false, false));
        Assert.Equal(400, first.SizeDip); Assert.Equal(-400, first.YDip); Assert.Equal(.45, first.Scale);
        var next = Assert.Single(clock.Step(.1, 900, false, false, false));
        Assert.InRange((next.YDip - first.YDip) / .1, 3.6 * .7 / .016, 3.6 * 1.3 / .016);
        Assert.True(clock.CascadeActive); Assert.Empty(clock.Step(121, 900, false, false, false)); Assert.False(clock.CascadeActive);
        clock.Trigger(BubbleEffectKind.Cascade, 121, 3);
        for (var i = 0; i < 100; i++) Assert.InRange(clock.Step(121 + i * .1, 900, false, false, false).Count, 0, 14);
        clock.Clear(); Assert.Empty(clock.Step(1000, 900, false, false, false)); Assert.False(clock.CascadeActive);
    }

    [Fact]
    public void GentleAndReducedMotionRetainSoftFadesWithoutFrozenInvisibleRain()
    {
        var clock = new BubbleVisualTimeline(); clock.Trigger(BubbleEffectKind.Cascade, 0, 1);
        var first = Assert.Single(clock.Step(0, 900, true, true, true)); Assert.InRange(first.YDip, 0, 540);
        var next = Assert.Single(clock.Step(.375, 900, true, true, true)); Assert.Equal(first.YDip, next.YDip);
        Assert.Equal(.45, next.Opacity, 8); Assert.Empty(clock.Step(6.751, 900, true, true, true));
    }

    [Theory]
    [InlineData(BubbleEffectKind.Glitch)] [InlineData(BubbleEffectKind.Cascade)] [InlineData(BubbleEffectKind.BrainDrainWash)]
    public void NewPayloadVariantsRequireEnabledLocalImagesAndFireOnlyOnADeliberatePop(BubbleEffectKind kind)
    {
        var settings = new ClassicSettings(); settings.Effects["bubble"].Values["rate"] = 30;
        settings.Effects["bubble"].Values["chance"] = 100; settings.Effects["bubble"].Values["kind"] = (int)kind;
        settings.Bubbles.Variants = [kind]; var timeline = new InteractiveEffectsTimeline(settings);
        timeline.Start(); Assert.Equal(BubbleEffectKind.None, Assert.Single(timeline.Step(2).Bubbles).Effect);
        timeline.Stop(); timeline.SetImageCount(2); timeline.Start(); timeline.Step(2);
        var bubble = Assert.Single(timeline.Step(2.25).Bubbles); Assert.Equal(kind, bubble.Effect);
        Assert.Empty(timeline.Step(2.25).ImageVisuals); Assert.True(timeline.Pop(bubble.Id));
        timeline.Step(2.25); Assert.NotEmpty(timeline.Step(2.5).ImageVisuals);
        timeline.Stop(); Assert.Empty(timeline.Step(100).ImageVisuals); Assert.False(timeline.Pop(bubble.Id));
    }

    [Fact]
    public void EffectsKeepSourceFloatUpWhilePlainBubblesHonorRainAndSpiralIn()
    {
        var settings = new ClassicSettings(); settings.Effects["bubble"].Values["rate"] = 30;
        settings.Effects["bubble"].Values["chance"] = 100; settings.Effects["bubble"].Values["kind"] = 1;
        settings.Bubbles.Motion = BubbleMotion.Rain; var timeline = new InteractiveEffectsTimeline(settings);
        timeline.SetImageCount(1); timeline.Start(); var effect = Assert.Single(timeline.Step(2).Bubbles);
        Assert.True(effect.Y > .9); Assert.True(Assert.Single(timeline.Step(2.1).Bubbles).Y < effect.Y);
        settings.Effects["bubble"].Values["effects"] = 0; timeline.Stop(); timeline.Start();
        var plain = Assert.Single(timeline.Step(2).Bubbles); Assert.True(plain.Y < 0);
        Assert.True(Assert.Single(timeline.Step(2.1).Bubbles).Y > plain.Y);
        Assert.Equal(200, BubbleChoreography.Diameter(BubbleEffectKind.Glitch, .5));
        Assert.Equal(220, BubbleChoreography.Diameter(BubbleEffectKind.BrainDrain, .5));
        Assert.InRange(BubbleChoreography.Diameter(BubbleEffectKind.Video, .5), 126, 158);
        Assert.InRange(BubbleChoreography.DrainPulse(30), .82, 1);
    }
}
