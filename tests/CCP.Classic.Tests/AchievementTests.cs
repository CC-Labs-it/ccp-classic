using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class AchievementTests
{
    [Fact]
    public void CatalogPreservesTheOriginalTwentySevenIdsWithNeutralLocalizationKeys()
    {
        Assert.Equal(27, AchievementCatalog.All.Count);
        Assert.Equal(27, AchievementCatalog.All.Select(a => a.Id).Distinct().Count());
        Assert.Equal("plastic_initiation", AchievementCatalog.All[0].Id);
        Assert.Equal("system_overload", AchievementCatalog.All[26].Id);
        Assert.Equal(Enumerable.Range(1, 27).Select(i => $"Achievement{i:00}"), AchievementCatalog.All.Select(a => a.NameKey));
    }

    [Fact]
    public void LoadedLevelAndCumulativeAchievementsRetroawardExactlyOnce()
    {
        var clock = new ManualClock();
        var lifetime = Enumerable.Range(1, 149).Sum(ProgressTracker.GetXpForLevel);
        var data = new ProgressData { LifetimeXp = lifetime, PinkSeconds = 36000, LongestSessionSeconds = 10800,
            ConsecutiveDays = 7, LastLaunchDay = DateOnly.FromDateTime(clock.GetLocalNow().DateTime),
            FlashesShown = 5000, BubblesPopped = 1000, BubbleCountBestStreak = 5, AttentionFailures = 3 };
        var tracker = new ProgressTracker(data, clock); var events = new List<string>();
        tracker.AchievementUnlocked += badge => events.Add(badge.Id);
        tracker.RefreshAchievements(); var count = tracker.UnlockedAchievementCount;
        Assert.Equal(13, count); Assert.Equal(count, events.Count);
        tracker.RefreshAchievements(); tracker.AddXp(1);
        Assert.Equal(count, events.Count);
        var reloaded = new ProgressTracker(data, clock); reloaded.AchievementUnlocked += badge => events.Add(badge.Id);
        reloaded.RefreshAchievements(); Assert.Equal(count, events.Count);
    }

    [Fact]
    public void SpiralNeedsTwentyContinuousMinutesAndCombinationsNeedSimultaneousActivity()
    {
        var clock = new ManualClock(); var tracker = new ProgressTracker(clock: clock);
        tracker.StartSession("Daily");
        clock.Advance(1199); tracker.Tick(false, true); Locked(tracker, "spiral_eyes");
        tracker.Tick(false, false); clock.Advance(1); tracker.Tick(false, true);
        Locked(tracker, "spiral_eyes");
        clock.Advance(1199); tracker.Tick(false, true); Unlocked(tracker, "spiral_eyes");
        tracker.Tick(new EffectProgressState(true, false, StrictLock: true));
        Locked(tracker, "total_lockdown");
        tracker.Tick(new EffectProgressState(true, false, StrictLock: true, PanicDisabled: true));
        Unlocked(tracker, "total_lockdown");
        tracker.Tick(new EffectProgressState(false, true, BubblesActive: true));
        Locked(tracker, "system_overload");
        tracker.Tick(new EffectProgressState(false, true, BubblesActive: true, BouncingTextActive: true));
        Unlocked(tracker, "system_overload");
    }

    [Fact]
    public void BubbleAnswersRequireFiveConsecutiveSuccessesAndAttentionRequiresThreeFailures()
    {
        var tracker = new ProgressTracker(); tracker.StartSession("Daily");
        for (var i = 0; i < 4; i++) tracker.BubbleCountAnswer(true);
        tracker.BubbleCountAnswer(false); tracker.BubbleCountAnswer(true);
        Locked(tracker, "mathematicians_nightmare");
        for (var i = 0; i < 4; i++) tracker.BubbleCountAnswer(true);
        Unlocked(tracker, "mathematicians_nightmare");
        tracker.AttentionAnswer(false); tracker.AttentionAnswer(true); tracker.AttentionAnswer(false);
        Locked(tracker, "mercy_beggar"); tracker.AttentionAnswer(false);
        Unlocked(tracker, "mercy_beggar"); Assert.Equal(3, tracker.Data.AttentionFailures);
    }

    [Fact]
    public void LockAccuracyAndSpeedAreIndependentAndFifteenSecondsDoesNotMeetSpeedCondition()
    {
        var tracker = new ProgressTracker(); tracker.StartSession("Daily");
        tracker.LockResult(1, 3, 15); Locked(tracker, "typing_tutor"); Locked(tracker, "obedience_reflex");
        tracker.LockResult(1, 3, 14.999); Unlocked(tracker, "obedience_reflex"); Locked(tracker, "typing_tutor");
        tracker.LockResult(0, 1, 30); Unlocked(tracker, "typing_tutor");
        tracker.LockResult(0, 0, 1); tracker.LockResult(-1, 3, 1); tracker.LockResult(0, 3, double.NaN);
        Assert.Equal(3, tracker.Data.LockCardsCompleted);
    }

    [Fact]
    public void WipeAndAvatarConditionsUseContinuousMonotonicTime()
    {
        var clock = new ManualClock(); var tracker = new ProgressTracker(clock: clock); tracker.StartSession("Daily");
        tracker.WipeStarted(); clock.Advance(59); tracker.Tick(false, false);
        tracker.WipeEnded(); Locked(tracker, "clean_slate");
        tracker.WipeStarted(); clock.Advance(60); tracker.Tick(false, false); Unlocked(tracker, "clean_slate");
        for (var i = 0; i < 19; i++) tracker.AvatarClicked();
        clock.Advance(10.001); tracker.AvatarClicked(); Locked(tracker, "neon_obsession");
        for (var i = 0; i < 18; i++) tracker.AvatarClicked();
        Locked(tracker, "neon_obsession"); clock.Advance(10); tracker.AvatarClicked();
        Unlocked(tracker, "neon_obsession");
        tracker.CornerHit(); Unlocked(tracker, "corner_hit");
    }

    [Fact]
    public void OnlyNaturalCompletionEarnsTheFourApprovedReplacementConditions()
    {
        var clock = new ManualClock(); var tracker = new ProgressTracker(clock: clock);
        tracker.StartSession("Gentle"); clock.Advance(1800); tracker.EndSession();
        Locked(tracker, "morning_glory"); Locked(tracker, "sofa_decor");
        tracker.StartSession("Gentle"); clock.Advance(1800); tracker.EndSession(naturalCompletion: true);
        Unlocked(tracker, "morning_glory"); Unlocked(tracker, "sofa_decor");
        tracker.StartSession("Daily"); tracker.SwitchedToOtherApplication(); tracker.EndSession(naturalCompletion: true);
        Locked(tracker, "player_2_disconnected");
        tracker.StartSession("Daily"); tracker.EndSession(naturalCompletion: true);
        Unlocked(tracker, "player_2_disconnected");
        tracker.StartSession("Deep"); tracker.EndSession(naturalCompletion: true);
        Locked(tracker, "look_but_dont_touch");
        tracker.StartSession("Deep"); tracker.EndSession(naturalCompletion: true, panicDisabled: true, strictLock: true);
        Unlocked(tracker, "look_but_dont_touch"); Unlocked(tracker, "what_panic_button");
    }

    [Fact]
    public void MorningWindowEndsAtNineAndThreeHoursNeedsNaturalCompletion()
    {
        var clock = new ManualClock(); var tracker = new ProgressTracker(clock: clock);
        tracker.StartSession("Gentle"); clock.Advance(7200); tracker.EndSession(naturalCompletion: true);
        Locked(tracker, "morning_glory");
        tracker.StartSession("Daily"); clock.Advance(10800); tracker.EndSession();
        Locked(tracker, "deep_sleep");
        tracker.StartSession("Daily"); clock.Advance(10800); tracker.EndSession(naturalCompletion: true);
        Unlocked(tracker, "deep_sleep");
    }

    [Fact]
    public void PanicRestartUsesTheTenSecondBoundaryAndRejectsFuturePersistedTimes()
    {
        var clock = new ManualClock(); var tracker = new ProgressTracker(clock: clock);
        tracker.StartSession("Daily"); tracker.PanicPressed(); tracker.EndSession(); clock.Advance(10.001);
        tracker.StartSession("Daily"); Locked(tracker, "relapse");
        tracker.PanicPressed(); tracker.EndSession(); clock.Advance(10);
        tracker.StartSession("Daily"); Unlocked(tracker, "relapse");
        var future = new ProgressTracker(new ProgressData { LastPanicUtc = clock.GetUtcNow().AddSeconds(1) }, clock);
        future.StartSession("Daily"); Locked(future, "relapse");
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset utc = new(2026, 11, 3, 7, 0, 0, TimeSpan.Zero);
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => utc;
        public override long GetTimestamp() => timestamp;
        public void Advance(double seconds) { var elapsed = TimeSpan.FromSeconds(seconds); utc += elapsed; timestamp += elapsed.Ticks; }
    }

    private static void Locked(ProgressTracker tracker, string id) => Assert.DoesNotContain(id, tracker.Data.UnlockedAchievements);
    private static void Unlocked(ProgressTracker tracker, string id) => Assert.Contains(id, tracker.Data.UnlockedAchievements);
}
