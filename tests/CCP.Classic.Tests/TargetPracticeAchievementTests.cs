using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class TargetPracticeAchievementTests
{
    [Fact]
    public void LegacyCountsAndGenericAttentionCannotAwardTargetPractice()
    {
        var data = new ProgressData { BubbleCountCorrectStreak = 20, BubbleCountBestStreak = 20 };
        var tracker = new ProgressTracker(data); tracker.RefreshAchievements(); tracker.StartSession("Daily");
        for (var i = 0; i < 8; i++) { tracker.BubbleCountAnswer(true); tracker.AttentionAnswer(true); }
        Assert.DoesNotContain("mathematicians_nightmare", tracker.Data.UnlockedAchievements);
        Assert.Equal(0, tracker.Data.VideoTargetBestStreak); Assert.Equal(0, tracker.Data.VideoTargetCorrectStreak);
    }

    [Fact]
    public void NewTargetStreakSurvivesStorageAndAHitAfterFourAwardsTheSameBadgeIdOnce()
    {
        var folder = Path.Combine(Path.GetTempPath(), "CCP Classic target progress", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var store = new ProgressStore(Path.Combine(folder, "progress.json")); var tracker = new ProgressTracker(); tracker.StartSession("Daily");
            for (var i = 0; i < 4; i++) tracker.VideoTargetAnswer(true);
            store.Save(tracker.Data); var loaded = store.Load(out var recovered); Assert.False(recovered);
            Assert.Equal(4, loaded.VideoTargetCorrectStreak); Assert.Equal(4, loaded.VideoTargetBestStreak);
            var next = new ProgressTracker(loaded); var unlocks = 0; next.AchievementUnlocked += badge => { if (badge.Id == "mathematicians_nightmare") unlocks++; };
            next.StartSession("Daily"); next.VideoTargetAnswer(true); next.VideoTargetAnswer(true);
            Assert.Contains("mathematicians_nightmare", loaded.UnlockedAchievements); Assert.Equal(1, unlocks);
            next.VideoTargetAnswer(false); Assert.Equal(0, loaded.VideoTargetCorrectStreak); Assert.Equal(6, loaded.VideoTargetBestStreak);
            store.Save(loaded); Assert.Contains("mathematicians_nightmare", store.Load(out _).UnlockedAchievements);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void PreviouslyUnlockedBadgeHistoryIsPreservedWithoutMigratingTheOldStreak()
    {
        var data = new ProgressData { BubbleCountBestStreak = 5, UnlockedAchievements = ["mathematicians_nightmare"] };
        var tracker = new ProgressTracker(data); tracker.RefreshAchievements();
        Assert.Contains("mathematicians_nightmare", tracker.Data.UnlockedAchievements); Assert.Equal(0, data.VideoTargetBestStreak);
        tracker.VideoTargetAnswer(true); Assert.Equal(0, data.VideoTargetCorrectStreak);
    }

    [Fact]
    public void TargetCountersNormalizeSeparatelyFromHistoricalCountFields()
    {
        var data = new ProgressData { BubbleCountCorrectStreak = 5, BubbleCountBestStreak = 8,
            VideoTargetCorrectStreak = int.MaxValue, VideoTargetBestStreak = -1 };
        data.Normalize(); Assert.Equal(5, data.BubbleCountCorrectStreak); Assert.Equal(8, data.BubbleCountBestStreak);
        Assert.Equal(1_000_000, data.VideoTargetCorrectStreak); Assert.Equal(1_000_000, data.VideoTargetBestStreak);
    }
}
