using CCP.Classic.Core;
using CCP.Classic.Core.Flash;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class FlashTimingTests
{
    [Theory]
    [InlineData(1, 4.6)]
    [InlineData(4.2, 5.2)]
    public void FiniteLinkedHydraIncludesFadesWithinTheInheritedTotalWithTheSourceMinimum(double clickedAt, double deadline)
    {
        var settings = Settings(); var timeline = new EffectsTimeline(settings, 1, 0);
        timeline.Start(); timeline.TriggerFlash(); var parent = Assert.Single(timeline.Step(clickedAt).Flashes);
        Assert.True(timeline.Dismiss(parent.Id));
        Assert.Equal(2, timeline.Step(deadline - .01).Flashes.Count);
        Assert.Empty(timeline.Step(deadline + .01).Flashes);
    }
    [Fact]
    public void IndependentHydraKeepsAFreshWholeLifetime()
    {
        var settings = Settings(); settings.Flash.HydraLinkedTiming = false;
        var timeline = new EffectsTimeline(settings, 1, 0); timeline.Start(); timeline.TriggerFlash();
        Assert.True(timeline.Dismiss(Assert.Single(timeline.Step(1).Flashes).Id));
        Assert.Equal(2, timeline.Step(4.7).Flashes.Count); Assert.Empty(timeline.Step(5.61).Flashes);
    }
    [Theory]
    [InlineData(FlashMotionStyle.Still)]
    [InlineData(FlashMotionStyle.DriftBounce)]
    [InlineData(FlashMotionStyle.Pendulum)]
    public void FirstDecodedAspectCorrectsTheExistingImageWithoutRestartingItsPoseOrEnvelope(FlashMotionStyle style)
    {
        var settings = Settings(); settings.Flash.Motion = style; var aspect = 1d;
        var timeline = new EffectsTimeline(settings, 1, 0) { ImageAspectRatio = _ => aspect };
        timeline.Start(); timeline.TriggerFlash(); var before = Assert.Single(timeline.Step(.5).Flashes);
        aspect = .5; var after = Assert.Single(timeline.Step(.5).Flashes);
        Assert.Equal(before.Id, after.Id); Assert.Equal(before.Opacity, after.Opacity); Assert.Equal(before.Rotation, after.Rotation);
        Assert.Equal(.5, after.Width * 1600 / (after.Height * 900), 6);
        Assert.Equal(before.X + before.Width / 2, after.X + after.Width / 2, 6);
        Assert.Equal(before.Y + before.Height / 2, after.Y + after.Height / 2, 6);
        var stable = Assert.Single(timeline.Step(.5).Flashes); Assert.Equal(after, stable);
        Assert.Empty(timeline.Step(4.61).Flashes);
    }
    private static ClassicSettings Settings()
    {
        var settings = new ClassicSettings(); settings.Flash.AudioEnabled = false;
        settings.Effects["flash"].Values["hydra"] = 1; settings.Effects["flash"].Values["rate"] = 0;
        return settings;
    }
}
