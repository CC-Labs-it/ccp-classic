using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class InteractiveEffectsTimelineTests
{
    private static ClassicSettings Settings()
    {
        var settings = new ClassicSettings();
        settings.Effects["bubble"].Values["rate"] = 30;
        return settings;
    }

    [Fact]
    public void BubbleRateHasNoCatchUpBurstAndThePoolIsBounded()
    {
        var timeline = new InteractiveEffectsTimeline(Settings());
        timeline.Start();
        Assert.Empty(timeline.Step(1.99).Bubbles);
        Assert.Single(timeline.Step(2).Bubbles);
        Assert.Single(timeline.Step(1000).Bubbles);
        for (var index = 0; index < 500; index++)
            Assert.InRange(timeline.Step(1002 + index * 2).Bubbles.Count, 1, 8);
    }

    [Fact]
    public void PopIsCountedOnceAndSoundFollowsItsSetting()
    {
        var settings = Settings();
        var timeline = new InteractiveEffectsTimeline(settings);
        var events = new List<InteractiveEffectEvent>();
        timeline.EventOccurred += events.Add;
        timeline.Start();
        var first = Assert.Single(timeline.Step(2).Bubbles);
        Assert.False(timeline.Pop(first.Id));
        timeline.Step(2.25);
        Assert.True(timeline.Pop(first.Id));
        Assert.False(timeline.Pop(first.Id));
        Assert.True(Assert.Single(events).PlaySound);
        Assert.Equal(InteractiveEffectEventKind.BubblePopped, events[0].Kind);
        Assert.False(Assert.Single(timeline.Step(2.25).Bubbles).CanPop);
        Assert.Empty(timeline.Step(2.5).Bubbles);
        settings.Effects["bubble"].Values["sound"] = 0;
        var second = Assert.Single(timeline.Step(4).Bubbles);
        timeline.Step(4.25);
        Assert.True(timeline.Pop(second.Id));
        Assert.False(events[1].PlaySound);
    }

    [Fact]
    public void ClickDisabledDoesNotConsumeTheBubbleOrEmitStats()
    {
        var settings = Settings();
        settings.Effects["bubble"].Values["click"] = 0;
        var timeline = new InteractiveEffectsTimeline(settings);
        var events = 0;
        timeline.EventOccurred += _ => events++;
        timeline.Start(); timeline.Step(2);
        var bubble = Assert.Single(timeline.Step(2.5).Bubbles);
        Assert.False(bubble.CanPop);
        Assert.False(timeline.Pop(bubble.Id));
        Assert.Equal(0, events);
        Assert.Single(timeline.Step(3).Bubbles);
    }

    [Fact]
    public void GentleBubbleEntrancesAndExitsLastAtLeastThreeQuartersOfASecond()
    {
        var settings = Settings(); settings.GentleVisuals = true;
        var timeline = new InteractiveEffectsTimeline(settings);
        timeline.Start(); timeline.Step(2);
        var half = Assert.Single(timeline.Step(2.375).Bubbles);
        Assert.Equal(.5, half.Opacity, 8);
        Assert.Equal(1, Assert.Single(timeline.Step(2.75).Bubbles).Opacity);
        Assert.True(timeline.Pop(half.Id)); timeline.Step(2.75);
        Assert.Equal(.5, Assert.Single(timeline.Step(3.125).Bubbles).Opacity, 8);
        Assert.Empty(timeline.Step(3.5).Bubbles);
    }

    [Fact]
    public void LiveGentleRetimesAnEntranceContinuouslyAndDisabledBubblesFadeOut()
    {
        var settings = Settings();
        var timeline = new InteractiveEffectsTimeline(settings);
        timeline.Start(); timeline.Step(2);
        var before = Assert.Single(timeline.Step(2.125).Bubbles).Opacity;
        settings.GentleVisuals = true;
        Assert.Equal(before, Assert.Single(timeline.Step(2.125).Bubbles).Opacity);
        Assert.Equal(1, Assert.Single(timeline.Step(2.875).Bubbles).Opacity);
        settings.Effects["bubble"].Enabled = false;
        Assert.Equal(1, Assert.Single(timeline.Step(2.875).Bubbles).Opacity);
        Assert.InRange(Assert.Single(timeline.Step(3.5).Bubbles).Opacity, .01, .1);
        Assert.Empty(timeline.Step(3.625).Bubbles);
    }

    [Fact]
    public void NaturalBubbleExpiryDoesNotCountAsAPop()
    {
        var settings = Settings();
        var timeline = new InteractiveEffectsTimeline(settings);
        var events = 0; timeline.EventOccurred += _ => events++;
        timeline.Start(); timeline.Step(2); timeline.Step(2.5);
        settings.Effects["bubble"].Values["rate"] = 0;
        timeline.Step(3); timeline.Step(14);
        Assert.Empty(timeline.Step(14.25).Bubbles);
        Assert.Equal(0, events);
    }

    [Fact]
    public void BubblePositionsStayOnScreenAndReducedMotionFreezesThem()
    {
        var settings = Settings(); settings.ReducedMotion = true;
        var timeline = new InteractiveEffectsTimeline(settings);
        timeline.Start();
        var first = Assert.Single(timeline.Step(2).Bubbles);
        var later = Assert.Single(timeline.Step(3).Bubbles);
        Assert.Equal(first.X, later.X); Assert.Equal(first.Y, later.Y);
        Assert.InRange(first.X, 0, 1 - first.Size);
        Assert.InRange(first.Y, 0, 1 - first.Size);
        settings.ReducedMotion = false;
        Assert.True(Assert.Single(timeline.Step(3.1).Bubbles).Y < later.Y);
    }

    [Fact]
    public void BounceReflectsAtEdgesAndEmitsOnlyAnExactCorner()
    {
        var settings = Settings(); settings.Effects["bounce"].Enabled = true;
        var timeline = new InteractiveEffectsTimeline(settings, initialBounce: new(.9, .9, 1, 1));
        var events = new List<InteractiveEffectEvent>(); timeline.EventOccurred += events.Add;
        timeline.Start(); timeline.Step(0);
        var corner = timeline.Step(.1).BouncingText!;
        Assert.Equal(1, corner.X, 8); Assert.Equal(1, corner.Y, 8);
        Assert.Equal(InteractiveEffectEventKind.CornerHit, Assert.Single(events).Kind);
        var reflected = timeline.Step(.2).BouncingText!;
        Assert.True(reflected.X < corner.X && reflected.Y < corner.Y);
        Assert.Single(events);

        var nearMiss = new InteractiveEffectsTimeline(settings, initialBounce: new(.9, .900001, 1, 1));
        var misses = 0; nearMiss.EventOccurred += _ => misses++;
        nearMiss.Start(); nearMiss.Step(0); nearMiss.Step(.1);
        Assert.Equal(0, misses);
    }

    [Fact]
    public void BounceFreezesInReducedMotionAndExitsGently()
    {
        var settings = Settings(); settings.GentleVisuals = true; settings.ReducedMotion = true;
        settings.Effects["bounce"].Enabled = true;
        var timeline = new InteractiveEffectsTimeline(settings);
        timeline.Start(); var first = timeline.Step(0).BouncingText!;
        var later = timeline.Step(.75).BouncingText!;
        Assert.Equal(first.X, later.X); Assert.Equal(first.Y, later.Y);
        Assert.Equal(.7, later.Opacity, 8);
        settings.Effects["bounce"].Enabled = false;
        timeline.Step(.75);
        Assert.Equal(.35, timeline.Step(1.125).BouncingText!.Opacity, 8);
        Assert.Null(timeline.Step(1.5).BouncingText);
    }

    [Fact]
    public void StopClearsImmediatelyAndStaleBubbleIdsCannotPopAfterRestart()
    {
        var settings = Settings(); settings.Effects["bounce"].Enabled = true;
        var timeline = new InteractiveEffectsTimeline(settings);
        timeline.Start(); var previous = Assert.Single(timeline.Step(2).Bubbles);
        timeline.Stop();
        Assert.Same(InteractiveEffectsFrame.Empty, timeline.Step(10));
        Assert.False(timeline.Pop(previous.Id));
        timeline.Start(); var current = Assert.Single(timeline.Step(2).Bubbles);
        Assert.NotEqual(previous.Id, current.Id);
        timeline.Step(2.25);
        Assert.False(timeline.Pop(previous.Id)); Assert.True(timeline.Pop(current.Id));
    }

    [Fact]
    public void BackwardAndInvalidTimesCannotReverseMotionOrCreateNewBubbles()
    {
        var settings = Settings(); settings.Effects["bounce"].Enabled = true;
        var timeline = new InteractiveEffectsTimeline(settings);
        timeline.Start(); var first = timeline.Step(2);
        foreach (var time in new[] { -1, 1, double.NaN, double.PositiveInfinity })
        {
            var frame = timeline.Step(time);
            Assert.Single(frame.Bubbles);
            Assert.Equal(first.BouncingText!.X, frame.BouncingText!.X);
            Assert.Equal(first.BouncingText.Y, frame.BouncingText.Y);
        }
    }
}
