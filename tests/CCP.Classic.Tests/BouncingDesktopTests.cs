using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class BouncingDesktopTests
{
    private static BouncingTextOptions Options() => new()
    { PhrasePool = new() { ["Focus"] = true }, SquashStretch = false, CornerBurst = false };
    private static BouncingTextTimeline Motion(BounceMotion start) => new(initial: start)
    { MeasureText = (_, _, _) => (100, 50) };
    private static double WorldX(BouncingTextCue cue) => cue.DesktopBounds!.Value.X + cue.X * (cue.DesktopBounds.Value.Width - 100);

    [Fact]
    public void NegativeOriginsAndVerticalOffsetsUseTheSourceRectangularUnion()
    {
        var desktop = new BouncingDesktopLayout([new(-1280, -200, 1280, 1024), new(0, 0, 1920, 1080)], 1.25);
        Assert.Equal(new BouncingTextBounds(-1024, -160, 2560, 1024), desktop.Bounds);
        var projection = desktop.Project(-1280, -200, 1.5);
        Assert.Equal(0, projection.ToLocal(-1024, -160).X, 8); Assert.Equal(0, projection.ToLocal(-1024, -160).Y, 8);
    }

    [Fact]
    public void DifferentDpiHostsProjectTheSameGlyphAndSeamToContinuousPhysicalPixels()
    {
        var desktop = new BouncingDesktopLayout([new(0, 0, 1920, 1080), new(1920, 0, 2560, 1440)], 1.25);
        var left = desktop.Project(0, 0, 1.25); var right = desktop.Project(1920, 0, 2);
        var seam = 1920 / 1.25;
        Assert.Equal(1536, left.ToLocal(seam, 0).X); Assert.Equal(0, right.ToLocal(seam, 0).X);
        Assert.Equal(250, 200 * left.Scale * 1.25); Assert.Equal(250, 200 * right.Scale * 2);
        Assert.Equal(125, right.ToLocal(seam + 200, 0).X);
    }

    [Fact]
    public void CrossingASeamDoesNotBounceAndKeepsOnlyTheRealOneOrTwoInstances()
    {
        var options = Options(); var motion = Motion(new(.49, .5, 1, 0)); var walls = 0;
        motion.WallHit += () => walls++;
        motion.SetDesktopBounds(new(0, 0, 2000, 1000));
        motion.Step(0, 1000, 1000, 5, 1, 1, options, false, false);
        var crossing = Assert.Single(motion.Step(.01, 1000, 1000, 5, 1, 1, options, false, false));
        Assert.Equal(950, WorldX(crossing), 8); Assert.Equal(0, walls);
        var passed = Assert.Single(motion.Step(.1, 1000, 1000, 5, 1, 1, options, false, false));
        Assert.True(WorldX(passed) > 1000); Assert.Equal(0, walls);
        options.SecondText = true;
        var two = motion.Step(0, 1000, 1000, 5, 1, 1, options, false, false);
        Assert.Equal(2, two.Count); Assert.Equal(passed.X, two[0].X); Assert.NotEqual(two[0].Y, two[1].Y);
    }

    [Fact]
    public void GapsAreTraversedWithoutInventingInnerWallsOrRewards()
    {
        var desktop = new BouncingDesktopLayout([new(0, 0, 1000, 1000), new(1500, 0, 1000, 1000)]);
        var motion = Motion(new(.5, .5, 1, 0)); var walls = 0; var corners = 0;
        motion.WallHit += () => walls++; motion.CornerHit += () => corners++;
        motion.SetDesktopBounds(desktop.Bounds);
        var inGap = Assert.Single(motion.Step(0, 1000, 1000, 5, 1, 1, Options(), false, false));
        Assert.Equal(1200, WorldX(inGap));
        for (var i = 0; i < 2; i++) inGap = Assert.Single(motion.Step(.1, 1000, 1000, 5, 1, 1, Options(), false, false));
        Assert.True(WorldX(inGap) > 1500); Assert.Equal(0, walls); Assert.Equal(0, corners);
    }

    [Fact]
    public void MonitorRemovalQuietlyClampsAndReversesWithoutCreditingAWallHit()
    {
        var motion = Motion(new(.9, .5, 1, 0)); var walls = 0; motion.WallHit += () => walls++;
        motion.SetDesktopBounds(new(-1000, 0, 3000, 1000));
        var original = Assert.Single(motion.Step(0, 1000, 1000, 5, 1, 1, Options(), false, false));
        Assert.Equal(1610, WorldX(original));
        motion.SetDesktopBounds(new(0, 0, 1000, 1000));
        var clamped = Assert.Single(motion.Step(0, 1000, 1000, 5, 1, 1, Options(), false, false));
        Assert.Equal(900, WorldX(clamped)); Assert.Equal(0, walls);
        var inward = Assert.Single(motion.Step(.1, 1000, 1000, 5, 1, 1, Options(), false, false));
        Assert.True(WorldX(inward) < 900); Assert.Equal(0, walls);
        motion.SetDesktopBounds(new(-1000, -300, 3000, 1300));
        var expanded = Assert.Single(motion.Step(0, 1000, 1000, 5, 1, 1, Options(), false, false));
        Assert.Equal(WorldX(inward), WorldX(expanded), 8); Assert.Equal(0, walls);
    }

    [Fact]
    public void OnlyTheOuterCornerBouncesAndMotionPreferencesStillApply()
    {
        var motion = Motion(new(.99, .99, 1, 1)); var options = Options(); var walls = 0; var corners = 0;
        motion.SetDesktopBounds(new(-1000, -200, 3000, 1200));
        motion.WallHit += () => walls++; motion.CornerHit += () => corners++;
        var start = Assert.Single(motion.Step(0, 1000, 1000, 5, 1, 1, options, false, false));
        Assert.Equal(start, Assert.Single(motion.Step(.1, 1000, 1000, 5, 1, 1, options, false, true)));
        motion.Step(.02, 1000, 1000, 5, 1, 1, options, false, false);
        Assert.Equal(1, walls); Assert.Equal(1, corners);
        motion.Reset(); motion.SetDesktopBounds(new(0, 0, 3000, 1200));
        var before = Assert.Single(motion.Step(0, 1000, 1000, 5, 1, 1, options, true, false));
        var after = Assert.Single(motion.Step(.01, 1000, 1000, 5, 1, 1, options, true, false));
        Assert.Equal(before.X + .005, after.X, 9);
    }

    [Fact]
    public void InvalidLayoutsCannotInjectNonfiniteTransforms()
    {
        Assert.Throws<ArgumentException>(() => new BouncingDesktopLayout([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BouncingDesktopLayout([new(0, 0, 1000, 1000)], double.NaN));
        var desktop = new BouncingDesktopLayout([new(0, 0, 1000, 1000)]);
        Assert.Throws<ArgumentOutOfRangeException>(() => desktop.Project(0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Motion(new(0, 0, 1, 1)).SetDesktopBounds(new(0, 0, double.NaN, 1)));
    }
}
