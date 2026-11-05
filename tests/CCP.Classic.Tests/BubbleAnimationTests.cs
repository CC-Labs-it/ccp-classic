using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class BubbleAnimationTests
{
    [Theory]
    [InlineData(0, .34)] [InlineData(1, .14)] [InlineData(2, -.66)] [InlineData(3, .54)]
    public void FourSourceProfilesKeepTheirOriginalSwayAndAngularVelocity(int kind, double rotation)
    {
        Assert.Equal(rotation * 30, BubbleChoreography.Rotation(kind, 30), 9);
        var t = .6;
        var expected = kind switch { 0 => Math.Sin(t * 6) * 25, 1 => Math.Sin(t * 7.5) * 30,
            2 => Math.Cos(t * 5.4) * 25, _ => Math.Sin(t * 3) * 30 + Math.Cos(t * 6) * 15 };
        Assert.Equal(expected, BubbleChoreography.Sway(kind, 30), 9);
        Assert.Equal(.06 * Math.Sin(4.5 + 13), BubbleChoreography.Breathe(30, 13), 9);
    }

    [Fact]
    public void BubbleFliesInFromBelowAtSourceDipSpeedAndHitPoseKeepsBreathing()
    {
        var settings = Settings(); var timeline = new InteractiveEffectsTimeline(settings);
        timeline.SetViewport(1600, 900); timeline.Start(); timeline.Step(2);
        var first = Assert.Single(timeline.Step(2.1).Bubbles);
        settings.Effects["bubble"].Values["rate"] = 0;
        BubbleCue later = first;
        for (var i = 2; i <= 11; i++) later = Assert.Single(timeline.Step(2 + i * .1).Bubbles);
        var pose = Assert.IsType<BubbleAnimation>(later.Animation);
        Assert.InRange((first.Y - later.Y) * 900, 30, 60);
        Assert.InRange(pose.Scale, .94, 1.06); Assert.NotEqual(first.Animation!.Rotation, pose.Rotation);
        Assert.True(later.Y < first.Y); Assert.InRange(later.Y, .9, 1);
    }

    [Fact]
    public void SourcePopExpandsRotatesAndFadesForItsActualHalfSecond()
    {
        var settings = Settings(); var timeline = new InteractiveEffectsTimeline(settings);
        timeline.Start(); timeline.Step(2); var before = Assert.Single(timeline.Step(2.25).Bubbles);
        Assert.True(timeline.Pop(before.Id));
        var middle = Assert.Single(timeline.Step(2.5).Bubbles);
        Assert.Equal(before.Animation!.Scale + .04 * 7.5, middle.Animation!.Scale, 9);
        Assert.Equal(before.Animation.Rotation + 15, middle.Animation.Rotation, 9);
        Assert.Equal(1 - .066 * 7.5, middle.Opacity, 9);
        Assert.Equal(before.X, middle.X); Assert.Equal(before.Y, middle.Y); Assert.False(middle.CanPop);
        Assert.Empty(timeline.Step(2.25 + BubbleChoreography.PopDuration + .001).Bubbles);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MotionPolicyChangesDoNotJumpAnActivePopAndStopClearsIt(bool gentle)
    {
        var settings = Settings(); var timeline = new InteractiveEffectsTimeline(settings);
        timeline.Start(); timeline.Step(2); var cue = Assert.Single(timeline.Step(2.25).Bubbles);
        timeline.Pop(cue.Id); var before = Assert.Single(timeline.Step(2.4).Bubbles);
        settings.GentleVisuals = gentle; settings.ReducedMotion = !gentle;
        var after = Assert.Single(timeline.Step(2.4).Bubbles);
        Assert.Equal(before.Opacity, after.Opacity); Assert.Equal(before.Animation!.Scale, after.Animation!.Scale);
        Assert.Equal(0, after.Animation.PopFrames);
        timeline.Stop(); Assert.Same(InteractiveEffectsFrame.Empty, timeline.Step(100));
    }

    [Fact]
    public void SourceSparkParticlesKeepBallisticGravityAndAreReproducible()
    {
        var start = BubbleChoreography.Spark(7, 2, 0);
        var one = BubbleChoreography.Spark(7, 2, 1);
        var two = BubbleChoreography.Spark(7, 2, 2);
        Assert.Equal(start.Size, one.Size); Assert.InRange(start.Size, 3, 6);
        Assert.Equal(one.X * 2, two.X, 9); Assert.Equal(one.Y * 2 + .15, two.Y, 9);
        Assert.Equal(.94, one.Alpha, 9); Assert.Equal(0, BubbleChoreography.Spark(7, 2, 17).Alpha);
        Assert.Equal(two, BubbleChoreography.Spark(7, 2, 2));
    }

    private static ClassicSettings Settings()
    {
        var settings = new ClassicSettings(); settings.Effects["bubble"].Values["rate"] = 30;
        return settings;
    }
}
