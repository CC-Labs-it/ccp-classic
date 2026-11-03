using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class LockEffectsTimelineTests
{
    private static ClassicSettings Settings()
    {
        var settings = new ClassicSettings();
        settings.Effects["lock"].Enabled = true;
        settings.Effects["lock"].Values["rate"] = 10;
        settings.LockCard.Phrases = new() { ["Breathe"] = true, ["Relax"] = true };
        settings.Effects["wipe"].Values["rate"] = 12;
        return settings;
    }

    [Fact]
    public void LockCapturesItsRulesAndRequiresExactRepetitions()
    {
        var settings = Settings(); settings.Effects["lock"].Values["strict"] = 1;
        var timeline = new LockEffectsTimeline(settings, new FixedRandom());
        var events = new List<InteractiveEffectEvent>(); timeline.EventOccurred += events.Add;
        timeline.Start(); Assert.Null(timeline.Step(179).LockCard);
        var card = timeline.Step(300).LockCard!;
        Assert.Equal("Breathe", card.Phrase); Assert.Equal(3, card.Repetitions); Assert.True(card.Strict);
        settings.Effects["lock"].Values["repeats"] = 1;
        settings.Effects["lock"].Values["strict"] = 0;
        Assert.False(timeline.CanDismissLock); Assert.False(timeline.DismissLock());
        Assert.False(timeline.SubmitLock(card.Id, "wrong"));
        Assert.False(timeline.SubmitLock(card.Id, "Breathe extra"));
        Assert.False(timeline.SubmitLock(card.Id + 1, "Breathe"));
        for (var count = 0; count < 3; count++) Assert.True(timeline.SubmitLock(card.Id, " breathe "));
        Assert.False(timeline.SubmitLock(card.Id, "Breathe"));
        Assert.Equal(InteractiveEffectEventKind.LockCompleted, Assert.Single(events).Kind);
        timeline.Step(300); Assert.Null(timeline.Step(300.25).LockCard);
    }

    [Fact]
    public void NonStrictDismissalDoesNotAwardCompletionAndFades()
    {
        var timeline = new LockEffectsTimeline(Settings(), new FixedRandom());
        var events = 0; timeline.EventOccurred += _ => events++;
        timeline.Start(); timeline.Step(300); timeline.Step(300.25);
        Assert.True(timeline.CanDismissLock); Assert.True(timeline.DismissLock());
        Assert.False(timeline.DismissLock()); timeline.Step(300.25);
        Assert.InRange(timeline.Step(300.375).LockCard!.Opacity, .49, .51);
        Assert.Null(timeline.Step(300.5).LockCard); Assert.Equal(0, events);
    }

    [Fact]
    public void BusyLockScheduleDoesNotQueueCardsOrAdvanceThePhrasePool()
    {
        var timeline = new LockEffectsTimeline(Settings(), new FixedRandom());
        timeline.Start(); var original = timeline.Step(300).LockCard!;
        Assert.Equal(original.Id, timeline.Step(10000).LockCard!.Id);
        timeline.DismissLock(); timeline.Step(10000); timeline.Step(10000.25);
        Assert.Null(timeline.Step(10359).LockCard);
        Assert.Equal("Relax", timeline.Step(10360).LockCard!.Phrase);
    }

    [Fact]
    public void GentleLockEntrancesAndExitsStayContinuousDuringLiveChanges()
    {
        var settings = Settings(); var timeline = new LockEffectsTimeline(settings, new FixedRandom());
        timeline.Start(); timeline.Step(300);
        var before = timeline.Step(300.125).LockCard!.Opacity;
        settings.GentleVisuals = true;
        Assert.Equal(before, timeline.Step(300.125).LockCard!.Opacity);
        Assert.Equal(1, timeline.Step(300.875).LockCard!.Opacity);
        settings.Effects["lock"].Enabled = false; timeline.Step(300.875);
        Assert.Equal(.5, timeline.Step(301.25).LockCard!.Opacity, 8);
        Assert.Null(timeline.Step(301.625).LockCard);
    }

    [Fact]
    public void PeriodicMindWipeWaitsItsIntervalAndReservesOnlyOneVoice()
    {
        var settings = Settings(); settings.Effects["wipe"].Enabled = true;
        var timeline = new LockEffectsTimeline(settings, new FixedRandom());
        timeline.Start(); Assert.Null(timeline.Step(299).MindWipe);
        var cue = timeline.Step(300).MindWipe!;
        Assert.False(cue.Loop); Assert.Equal(.6, cue.Volume, 8);
        Assert.Equal(cue.Id, timeline.Step(100000).MindWipe!.Id);
        Assert.True(timeline.CompleteMindWipe(cue.Id));
        Assert.False(timeline.CompleteMindWipe(cue.Id));
        Assert.Null(timeline.Step(100299).MindWipe);
        Assert.NotEqual(cue.Id, timeline.Step(100300).MindWipe!.Id);
    }

    [Fact]
    public void LoopingMindWipeAdvancesOnlyAfterActualPlaybackCompletion()
    {
        var settings = Settings(); settings.Effects["wipe"].Enabled = true;
        settings.Effects["wipe"].Values["loop"] = 1;
        var timeline = new LockEffectsTimeline(settings, new FixedRandom());
        var events = new List<InteractiveEffectEvent>(); timeline.EventOccurred += events.Add;
        timeline.Start(); var first = timeline.Step(0).MindWipe!;
        Assert.True(first.Loop);
        Assert.Equal(first.Id, timeline.Step(10000).MindWipe!.Id);
        Assert.True(timeline.CompleteMindWipe(first.Id));
        var second = timeline.Step(10000).MindWipe!;
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(InteractiveEffectEventKind.WipeCompleted, Assert.Single(events).Kind);
        settings.Effects["wipe"].Values["loop"] = 0;
        Assert.False(timeline.Step(10000).MindWipe!.Loop);
        timeline.CompleteMindWipe(second.Id);
        Assert.Null(timeline.Step(10000).MindWipe);
    }

    [Fact]
    public void CancellingAFailedLoopDoesNotAwardOrSpinRetryAndVolumeUpdatesLive()
    {
        var settings = Settings(); settings.Effects["wipe"].Enabled = true;
        settings.Effects["wipe"].Values["loop"] = 1;
        var timeline = new LockEffectsTimeline(settings, new FixedRandom());
        var events = 0; timeline.EventOccurred += _ => events++;
        timeline.Start(); var cue = timeline.Step(0).MindWipe!;
        settings.Effects["wipe"].Values["volume"] = 20;
        Assert.Equal(.2, timeline.Step(1).MindWipe!.Volume, 8);
        Assert.True(timeline.CancelMindWipe(cue.Id));
        Assert.Null(timeline.Step(1).MindWipe); Assert.Null(timeline.Step(300).MindWipe);
        Assert.NotNull(timeline.Step(301).MindWipe); Assert.Equal(0, events);
    }

    [Fact]
    public void DisablingMindWipeDropsItsReservationAndLateCompletionIsIgnored()
    {
        var settings = Settings(); settings.Effects["wipe"].Enabled = true;
        settings.Effects["wipe"].Values["loop"] = 1;
        var timeline = new LockEffectsTimeline(settings, new FixedRandom());
        timeline.Start(); var cue = timeline.Step(0).MindWipe!;
        settings.Effects["wipe"].Enabled = false;
        Assert.Null(timeline.Step(.1).MindWipe);
        Assert.False(timeline.CompleteMindWipe(cue.Id));
        settings.Effects["wipe"].Enabled = true;
        Assert.NotEqual(cue.Id, timeline.Step(.2).MindWipe!.Id);
    }

    [Fact]
    public void PanicStopWinsEvenForAnUnfinishedStrictLockAndRestartsRejectStaleIds()
    {
        var settings = Settings(); settings.Effects["lock"].Values["strict"] = 1;
        settings.Effects["wipe"].Enabled = true; settings.Effects["wipe"].Values["loop"] = 1;
        settings.Effects["drain"].Enabled = true;
        var timeline = new LockEffectsTimeline(settings, new FixedRandom());
        timeline.Start(); var previous = timeline.Step(300);
        timeline.Stop(); Assert.False(timeline.IsRunning);
        Assert.Same(LockEffectsFrame.Empty, timeline.Step(10000));
        Assert.False(timeline.SubmitLock(previous.LockCard!.Id, previous.LockCard.Phrase));
        Assert.False(timeline.CompleteMindWipe(previous.MindWipe!.Id));
        timeline.Start(); var fresh = timeline.Step(300);
        Assert.NotEqual(previous.LockCard.Id, fresh.LockCard!.Id);
        Assert.NotEqual(previous.MindWipe.Id, fresh.MindWipe!.Id);
        Assert.False(timeline.SubmitLock(previous.LockCard.Id, fresh.LockCard.Phrase));
    }
    private sealed class FixedRandom : Random
    {
        public override double NextDouble() => .5;
        public override int Next(int maxValue) => 0;
    }
}
