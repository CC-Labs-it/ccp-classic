using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class BouncingTextTests
{
    [Fact]
    public void CustomPoolTwoIndependentLinesAndLiveFixedColorUseOriginalOptions()
    {
        var options = new BouncingTextOptions { SecondText = true, ColorMode = 1, FixedColor = "#12ABEF",
            PhrasePool = new() { ["Custom phrase"] = true, ["Disabled"] = false } };
        var motion = new BouncingTextTimeline(19);
        var cues = motion.Step(0, 1600, 900, 5, 1, 1, options, false, false);
        Assert.Equal(2, cues.Count); Assert.All(cues, cue => { Assert.Equal("Custom phrase", cue.Text); Assert.Equal("#12ABEF", cue.Colour); });
        Assert.NotEqual(cues[0].X, cues[1].X); Assert.NotEqual(cues[0].Y, cues[1].Y);
        options.SecondText = false; options.FixedColor = "#AABBCC";
        Assert.Equal("#AABBCC", Assert.Single(motion.Step(0, 1600, 900, 5, 1, 1, options, false, false)).Colour);
        options.PhrasePool.Clear(); Assert.Equal("Focus", Assert.Single(motion.Step(0, 1600, 900, 5, 1, 1, options, false, false)).Text);
    }

    [Fact]
    public void ActualMeasuredGlyphSizeControlsDipFlightAndUpdatesOnlyWhenNeeded()
    {
        var count = 0; var motion = new BouncingTextTimeline(initial: new(.5, .5, 1, 1))
        { MeasureText = (_, _, size) => { count++; return (size * 3, size); } };
        var options = new BouncingTextOptions { SquashStretch = false, CornerBurst = false };
        var before = Assert.Single(motion.Step(0, 1000, 800, 5, 1, 1, options, false, false));
        var after = Assert.Single(motion.Step(.1, 1000, 800, 5, 1, 1, options, false, false));
        Assert.Equal(before.X + .1, after.X, 9); Assert.Equal(1, count);
        motion.Step(0, 1000, 800, 5, 2, 1, options, false, false); Assert.Equal(2, count);
        options.FontFamily = "Other local font";
        motion.Step(0, 1000, 800, 5, 2, 1, options, false, false); Assert.Equal(3, count);
    }

    [Fact]
    public void SpinKeepsSourceFortyDegreesPerSecondAndMotionPoliciesFreezeIt()
    {
        var options = new BouncingTextOptions { Spin = true, SquashStretch = false, CornerBurst = false };
        var motion = new BouncingTextTimeline(initial: new(.5, .5, .1, .1));
        motion.Step(0, 1600, 900, 5, 1, 1, options, false, false);
        var normal = Assert.Single(motion.Step(.1, 1600, 900, 5, 1, 1, options, false, false));
        Assert.Equal(4, normal.Angle, 9);
        var reduced = Assert.Single(motion.Step(.1, 1600, 900, 5, 1, 1, options, false, true));
        Assert.Equal(normal, reduced);
        var gentle = Assert.Single(motion.Step(.1, 1600, 900, 5, 1, 1, options, true, false));
        Assert.Equal(4, gentle.Angle, 9); Assert.Equal(normal.X + .005, gentle.X, 8);
    }

    [Fact]
    public void SourceWallSquashNearCornerBurstAndTransformOverhangAreBounded()
    {
        var options = new BouncingTextOptions();
        var motion = new BouncingTextTimeline(initial: new(.99, .99, 1, 1));
        var walls = 0; var corners = 0; motion.WallHit += () => walls++; motion.CornerHit += () => corners++;
        motion.Step(0, 1600, 900, 5, 1, 1, options, false, false);
        var cue = Assert.Single(motion.Step(.02, 1600, 900, 5, 1, 1, options, false, false));
        Assert.Equal(1, walls); Assert.Equal(1, corners); Assert.InRange(cue.BurstSeconds, 0, .55);
        Assert.NotEqual(1, cue.ScaleX); Assert.NotEqual(1, cue.ScaleY);
        var over = BouncingTextTimeline.Overhang(200, 50, 1, 1, 90);
        Assert.Equal(0, over.X); Assert.Equal(75, over.Y, 8);
    }

    [Fact]
    public void OptionsNormalizeBoundedCustomTextAndMalformedStorage()
    {
        var options = new BouncingTextOptions { ColorMode = 90, FixedColor = "bad", FontFamily = " ",
            PhrasePool = new() { ["  one "] = true, ["one"] = false, [" "] = true, [new string('x', 2100)] = true } };
        options.Normalize(); Assert.Equal(2, options.ColorMode); Assert.Equal("#FF69B4", options.FixedColor);
        Assert.Equal("Inter", options.FontFamily); Assert.Equal(2, options.PhrasePool.Count);
        Assert.True(options.PhrasePool["one"]); Assert.All(options.PhrasePool.Keys, phrase => Assert.InRange(phrase.Length, 1, 2000));
    }

    [Fact]
    public void TriggeredBubblesIgnoreScheduleToggleAndPanicClearsTheirQueue()
    {
        var settings = new ClassicSettings(); settings.Effects["bubble"].Enabled = false;
        var timeline = new InteractiveEffectsTimeline(settings); timeline.Start(); timeline.TriggerBubbles(30);
        Assert.Equal(8, timeline.Step(0).Bubbles.Count);
        var cue = timeline.Step(.25).Bubbles[0]; Assert.True(cue.CanPop); Assert.True(timeline.Pop(cue.Id));
        timeline.TriggerBubbles(); timeline.Stop(); timeline.Start(); Assert.Empty(timeline.Step(0).Bubbles);
    }

    [Fact]
    public void VideoPauseHidesAndFreezesTextUnlessExplicitlyEnabledOverVideos()
    {
        var settings = new ClassicSettings(); settings.Effects["bounce"].Enabled = true;
        var timeline = new InteractiveEffectsTimeline(settings); timeline.Start(); timeline.Step(0);
        var before = timeline.Step(.25).BouncingText!; timeline.VideoPlaying = true;
        timeline.Step(.25); Assert.Null(timeline.Step(.5).BouncingText);
        settings.BouncingText.ShowOverVideos = true;
        var resumed = timeline.Step(.5).BouncingText!; Assert.Equal(before.X, resumed.X); Assert.Equal(before.Y, resumed.Y);
        Assert.NotEqual(resumed.X, timeline.Step(.6).BouncingText!.X);
    }
}
