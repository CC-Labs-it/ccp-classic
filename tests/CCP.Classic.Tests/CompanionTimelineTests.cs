using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class CompanionTimelineTests
{
    private static ClassicSettings Settings() => new()
    { CompanionEnabled = true, BarksEnabled = true, BarkIntervalSeconds = 60 };

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
    public void RepeatedPhraseTriggersKeepTheOutputBounded()
    {
        var timeline = new CompanionTimeline(Settings()) { TriggerPhrasesEnabled = true }; timeline.Start();
        for (var count = 0; count < 1000; count++) timeline.TriggerPhrase("relax");
        Assert.Equal(16, timeline.Step(0).Triggers.Count);
        Assert.Empty(timeline.Step(0).Triggers);
        Assert.True(timeline.TriggerPhrase("focus"));
        Assert.Equal(CompanionEffect.Whisper, Assert.Single(timeline.Step(0).Triggers).Effect);
    }

    [Fact]
    public void DisablingPhraseTriggersCancelsTheirPendingActions()
    {
        var timeline = new CompanionTimeline(Settings()) { TriggerPhrasesEnabled = true }; timeline.Start();
        timeline.TriggerPhrase("Relax"); timeline.TriggerPhrasesEnabled = false;
        Assert.False(timeline.TriggerPhrase("Deeper"));
        Assert.Empty(timeline.Step(0).Triggers);
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
    public void StopClearsTheBarkAndActionsAndCannotProduceAnythingUntilRestarted()
    {
        var timeline = new CompanionTimeline(Settings()) { TriggerPhrasesEnabled = true }; timeline.Start();
        var old = timeline.Step(60).Bark!; timeline.TriggerPhrase("Deeper");
        timeline.Stop(); Assert.False(timeline.IsRunning);
        Assert.Same(CompanionFrame.Empty, timeline.Step(10000));
        Assert.False(timeline.TriggerPhrase("Relax"));
        timeline.Start(); Assert.Empty(timeline.Step(0).Triggers);
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
    public void IdleBarksRunOnTheIntervalWithoutASessionAndNeverRaiseEffects()
    {
        var settings = Settings(); settings.BarkIntervalSeconds = 6; settings.TriggerPhrasesEnabled = true;
        var timeline = new CompanionTimeline(settings) { TriggerPhrasesEnabled = true };
        // The idle clock is the app's clock, far from zero: the first word still waits a full interval.
        timeline.StartIdle(5000); Assert.True(timeline.IsRunning); Assert.True(timeline.IsIdle);
        Assert.Null(timeline.Step(5005.9).Bark);
        var words = new List<string>();
        for (var step = 0; step < 5; step++)
        {
            var frame = timeline.Step(5006 + step * 6); words.Add(frame.Bark!.Text); Assert.Empty(frame.Triggers);
            Assert.Empty(timeline.Step(5006 + step * 6 + 5).Triggers);
        }
        Assert.Equal(CompanionTimeline.Phrases.Select(phrase => phrase.Text), words);
        Assert.False(timeline.TriggerPhrase("Relax"));
        Assert.Empty(timeline.Step(5040).Triggers);
        // Barks off ends the word and the timer; turning them back on waits a new interval.
        settings.BarksEnabled = false; timeline.Step(5041); Assert.Null(timeline.Step(5043).Bark); Assert.Null(timeline.Step(5060).Bark);
        settings.BarksEnabled = true; Assert.Null(timeline.Step(5061).Bark); Assert.Null(timeline.Step(5066.9).Bark);
        Assert.NotNull(timeline.Step(5067).Bark);
        settings.CompanionEnabled = false; timeline.Step(5068); Assert.Null(timeline.Step(5072).Bark);
    }

    [Fact]
    public void AClickSaysTheNextWordAtOnceWithACooldownAndNoEffect()
    {
        var settings = Settings(); settings.BarkIntervalSeconds = 6;
        var timeline = new CompanionTimeline(settings) { TriggerPhrasesEnabled = true }; timeline.StartIdle(100);
        Assert.True(timeline.Poke(100.2));
        var first = timeline.Step(100.2); Assert.Equal("Breathe", first.Bark!.Text); Assert.Empty(first.Triggers);
        Assert.False(timeline.Poke(101)); Assert.Equal(first.Bark.Id, timeline.Step(101).Bark!.Id);
        Assert.True(timeline.Poke(101.7));
        var second = timeline.Step(101.7); Assert.Equal("Relax", second.Bark!.Text); Assert.Empty(second.Triggers);
        // The click resets the timer, so a timed word does not follow straight after it.
        Assert.Null(timeline.Step(107.6).Bark); Assert.Equal("Focus", timeline.Step(107.7).Bark!.Text);
        settings.BarksEnabled = false; Assert.False(timeline.Poke(200));
        settings.BarksEnabled = true; timeline.Stop(); Assert.False(timeline.Poke(300));
        // A session click works the same and never queues the phrase's effect.
        timeline.Start(); Assert.True(timeline.Poke(1)); var session = timeline.Step(1);
        Assert.NotNull(session.Bark); Assert.Empty(session.Triggers);
    }

    [Fact]
    public void AStopSilencesAnIdleWordAndTheNextOneWaitsAFullInterval()
    {
        var settings = Settings(); settings.BarkIntervalSeconds = 6;
        var timeline = new CompanionTimeline(settings); timeline.StartIdle(0);
        Assert.NotNull(timeline.Step(6).Bark);
        timeline.Stop(); Assert.Same(CompanionFrame.Empty, timeline.Step(7));
        Assert.False(timeline.Poke(7));
        timeline.StartIdle(30); Assert.Null(timeline.Step(30).Bark); Assert.Null(timeline.Step(35.9).Bark);
        Assert.NotNull(timeline.Step(36).Bark);
        // A session restarts its own zero-based clock and leaves idle mode.
        timeline.Start(); Assert.False(timeline.IsIdle); Assert.Null(timeline.Step(5).Bark);
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
