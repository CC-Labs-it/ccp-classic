using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class CompanionTimelineTests
{
    private static ClassicSettings Settings() => new()
    { CompanionEnabled = true, BarksEnabled = true, KeywordTriggersEnabled = true, BarkIntervalSeconds = 60 };

    [Fact]
    public void TimedBarksUseExactlyTheApprovedWordsAndTheirExistingRecordingIndices()
    {
        var timeline = new CompanionTimeline(Settings()); timeline.Start();
        Assert.Null(timeline.Step(59).Bark);
        var expected = new[] { ("Breathe", 1), ("Relax", 2), ("Focus", 0), ("Listen", 3), ("Deeper", 4) };
        for (var index = 0; index < expected.Length; index++)
        {
            var bark = timeline.Step(60 * (index + 1)).Bark!;
            Assert.Equal(expected[index].Item1, bark.Text); Assert.Equal(expected[index].Item2, bark.AudioIndex);
        }
        Assert.Equal("Breathe", timeline.Step(360).Bark!.Text);
    }

    [Fact]
    public void BarkTimersNeverCatchUpAndCadenceChangesWaitANewInterval()
    {
        var settings = Settings(); var timeline = new CompanionTimeline(settings); timeline.Start();
        var first = timeline.Step(10000).Bark!;
        Assert.Equal("Breathe", first.Text); Assert.Empty(timeline.Step(10000).Triggers);
        settings.BarkIntervalSeconds = 10;
        timeline.Step(10001); Assert.Null(timeline.Step(10009).Bark);
        Assert.Equal("Relax", timeline.Step(10011).Bark!.Text);
    }

    [Fact]
    public void ExplicitAndTimedPhraseTriggersUseOnlyTheThreeApprovedMappings()
    {
        var settings = Settings(); settings.CompanionEnabled = settings.BarksEnabled = false;
        var timeline = new CompanionTimeline(settings) { TriggerPhrasesEnabled = true }; timeline.Start();
        Assert.False(timeline.TriggerPhrase("Breathe")); Assert.False(timeline.TriggerPhrase("unknown"));
        Assert.True(timeline.TriggerPhrase("RELAX")); Assert.True(timeline.TriggerPhrase("Focus"));
        Assert.True(timeline.TriggerPhrase("deeper"));
        Assert.Equal(new[] { CompanionEffect.Flash, CompanionEffect.Whisper, CompanionEffect.SpiralPulse },
            timeline.Step(0).Triggers.Select(cue => cue.Effect));
        Assert.Empty(timeline.Step(0).Triggers);
        Assert.Empty(timeline.Step(60).Triggers);
        var timed = Assert.Single(timeline.Step(120).Triggers);
        Assert.Equal(CompanionEffect.Flash, timed.Effect); Assert.Equal(CompanionTriggerSource.TimedPhrase, timed.Source);
        Assert.Null(timeline.Step(120).Bark);
    }

    [Fact]
    public void KeywordMatchingSpansChunksButWaitsForAFullWordBoundary()
    {
        var settings = Settings(); settings.CompanionEnabled = false;
        var timeline = new CompanionTimeline(settings); timeline.Start();
        timeline.FeedText("re"); timeline.FeedText("LaX"); Assert.Empty(timeline.Step(0).Triggers);
        timeline.FeedText(",FOCUS\tDeEpEr.");
        var actions = timeline.Step(0).Triggers;
        Assert.Equal(new[] { CompanionEffect.Flash, CompanionEffect.Whisper, CompanionEffect.SpiralPulse }, actions.Select(cue => cue.Effect));
        Assert.All(actions, cue => Assert.Equal(CompanionTriggerSource.Keyword, cue.Source));
        Assert.Empty(timeline.Step(0).Triggers);
    }

    [Theory]
    [InlineData("unrelax ")]
    [InlineData("focus123 ")]
    [InlineData("deeper_relax ")]
    [InlineData("relaxé ")]
    [InlineData("Focus\u0301 ")]
    public void EmbeddedWordsAreNeverTriggers(string text)
    {
        var timeline = new CompanionTimeline(Settings()); timeline.Start(); timeline.FeedText(text);
        Assert.Empty(timeline.Step(0).Triggers);
    }

    [Fact]
    public void LongInputAndRepeatedKeywordsKeepTheOutputBounded()
    {
        var timeline = new CompanionTimeline(Settings()); timeline.Start();
        timeline.FeedText(new string('x', 100000)); timeline.FeedText("relax ");
        Assert.Empty(timeline.Step(0).Triggers);
        timeline.FeedText(string.Concat(Enumerable.Repeat(" relax ", 1000)));
        Assert.Equal(16, timeline.Step(0).Triggers.Count);
        Assert.Empty(timeline.Step(0).Triggers);
        timeline.FeedText("focus "); Assert.Equal(CompanionEffect.Whisper, Assert.Single(timeline.Step(0).Triggers).Effect);
    }

    [Fact]
    public void KeywordDisableClearsBothPartialWordsAndPendingActions()
    {
        var settings = Settings(); var timeline = new CompanionTimeline(settings); timeline.Start();
        timeline.FeedText("focus relax"); settings.KeywordTriggersEnabled = false;
        Assert.Empty(timeline.Step(0).Triggers);
        settings.KeywordTriggersEnabled = true; timeline.FeedText(" "); Assert.Empty(timeline.Step(0).Triggers);
        timeline.FeedText("focus\b "); Assert.Empty(timeline.Step(0).Triggers);
        timeline.FeedText("relax "); Assert.Single(timeline.Step(0).Triggers);
    }

    [Fact]
    public void DisablingPhraseTriggersCancelsOnlyTheirPendingActions()
    {
        var timeline = new CompanionTimeline(Settings()) { TriggerPhrasesEnabled = true }; timeline.Start();
        timeline.TriggerPhrase("Relax"); timeline.FeedText("focus "); timeline.TriggerPhrasesEnabled = false;
        Assert.False(timeline.TriggerPhrase("Deeper"));
        Assert.Equal(CompanionEffect.Whisper, Assert.Single(timeline.Step(0).Triggers).Effect);
    }

    [Fact]
    public void GentleBarksEnterAndExitContinuouslyForAtLeast750ms()
    {
        var settings = Settings(); var timeline = new CompanionTimeline(settings); timeline.Start();
        timeline.Step(60); var before = timeline.Step(60.125).Bark!.Opacity;
        settings.GentleVisuals = true; Assert.Equal(before, timeline.Step(60.125).Bark!.Opacity);
        settings.GentleVisuals = false; Assert.Equal(.75, timeline.Step(60.5).Bark!.Opacity, 8);
        Assert.Equal(1, timeline.Step(60.875).Bark!.Opacity);
        settings.GentleVisuals = true; settings.BarksEnabled = false;
        timeline.Step(61); Assert.Equal(.5, timeline.Step(61.375).Bark!.Opacity, 8);
        Assert.Null(timeline.Step(61.75).Bark);
    }

    [Fact]
    public void StopClearsTheBarkSuffixAndActionsAndCannotProduceAnythingUntilRestarted()
    {
        var timeline = new CompanionTimeline(Settings()) { TriggerPhrasesEnabled = true }; timeline.Start();
        var old = timeline.Step(60).Bark!; timeline.FeedText("focus relax"); timeline.TriggerPhrase("Deeper");
        timeline.Stop(); Assert.False(timeline.IsRunning);
        Assert.Same(CompanionFrame.Empty, timeline.Step(10000));
        timeline.FeedText("focus "); Assert.False(timeline.TriggerPhrase("Relax"));
        timeline.Start(); timeline.FeedText(" "); Assert.Empty(timeline.Step(0).Triggers);
        Assert.NotEqual(old.Id, timeline.Step(60).Bark!.Id);
    }

    [Fact]
    public void InvalidAndBackwardTimesDoNotCreateOrRewindBarks()
    {
        var timeline = new CompanionTimeline(Settings()); timeline.Start(); timeline.Step(60);
        var first = timeline.Step(60.125).Bark!;
        foreach (var time in new[] { -1, 59, double.NaN, double.PositiveInfinity })
        {
            var frame = timeline.Step(time); Assert.Equal(first, frame.Bark); Assert.Empty(frame.Triggers);
        }
    }

    [Fact]
    public void ExpiredBarksStayGoneAfterClockJumpsAndEnablingGentle()
    {
        var settings = Settings(); var timeline = new CompanionTimeline(settings); timeline.Start();
        timeline.Step(60); timeline.Step(60.25); settings.GentleVisuals = true;
        Assert.Null(timeline.Step(70).Bark);
    }

    [Fact]
    public void FastCadencesDoNotReplaceABarkBeforeItsExit()
    {
        var settings = Settings(); settings.BarkIntervalSeconds = 1;
        var timeline = new CompanionTimeline(settings); timeline.Start();
        var first = timeline.Step(1).Bark!; timeline.Step(1.25);
        Assert.Equal(first.Id, timeline.Step(2).Bark!.Id);
        Assert.Equal(first.Id, timeline.Step(5).Bark!.Id);
        Assert.Null(timeline.Step(5.25).Bark);
        Assert.NotEqual(first.Id, timeline.Step(6).Bark!.Id);
    }
}
