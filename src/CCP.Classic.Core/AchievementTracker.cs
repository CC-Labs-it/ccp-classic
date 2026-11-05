namespace CCP.Classic.Core;

public sealed record AchievementDefinition(string Id, string NameKey, string RequirementKey);

public static class AchievementCatalog
{
    private static readonly string[] Ids =
    [
        "plastic_initiation", "dumb_bimbo", "fully_synthetic", "perfect_plastic_puppet", "brainwashed_slavedoll",
        "platinum_puppet", "rose_tinted_reality", "deep_sleep", "daily_maintenance", "retinal_burn", "morning_glory",
        "player_2_disconnected", "sofa_decor", "look_but_dont_touch", "spiral_eyes", "mathematicians_nightmare",
        "pop_the_thought", "typing_tutor", "obedience_reflex", "mercy_beggar", "clean_slate", "corner_hit",
        "neon_obsession", "what_panic_button", "relapse", "total_lockdown", "system_overload"
    ];
    public static IReadOnlyList<AchievementDefinition> All { get; } = Array.AsReadOnly(Ids.Select((id, index) =>
        new AchievementDefinition(id, $"Achievement{index + 1:00}", $"AchievementRequirement{index + 1:00}")).ToArray());
    internal static readonly IReadOnlyDictionary<string, AchievementDefinition> ById = All.ToDictionary(a => a.Id);
}

public enum AchievementEventKind { BubbleCount, LockCard, Attention, AvatarClick, Corner, Panic, WipeStarted, WipeEnded }
public readonly record struct AchievementEvent(AchievementEventKind Kind, bool Successful = false, int Count = 0, double Seconds = 0);

public sealed partial class ProgressTracker
{
    private long? wipeStarted, avatarFirstClick, lastPanicTimestamp;
    private int avatarClicks;
    public int UnlockedAchievementCount => Data.UnlockedAchievements.Count(AchievementCatalog.ById.ContainsKey);
    public event Action<AchievementDefinition>? AchievementUnlocked;
    public event Action<AchievementEvent>? AchievementRecorded;

    // Call after attaching UI listeners, including when progress was already loaded on this calendar day.
    public void RefreshAchievements() => EvaluateProgress(new(ProgressEventKind.Xp));

    partial void EvaluateProgress(ProgressEvent progressEvent)
    {
        var level = Level;
        foreach (var (required, id) in new[] { (10, "plastic_initiation"), (20, "dumb_bimbo"), (50, "fully_synthetic"),
            (100, "perfect_plastic_puppet"), (125, "brainwashed_slavedoll"), (150, "platinum_puppet") })
            if (level >= required) Unlock(id);
        if (Data.PinkSeconds >= 36000) Unlock("rose_tinted_reality");
        if (Data.LongestSessionSeconds >= 10800) Unlock("deep_sleep");
        if (Data.ConsecutiveDays >= 7) Unlock("daily_maintenance");
        if (Data.FlashesShown >= 5000) Unlock("retinal_burn");
        if (Data.BubblesPopped >= 1000) Unlock("pop_the_thought");
        if (Data.VideoTargetBestStreak >= 5) Unlock("mathematicians_nightmare");
        if (Data.AttentionFailures >= 3) Unlock("mercy_beggar");
        if (progressEvent.Kind == ProgressEventKind.SessionStarted)
        {
            wipeStarted = null;
            var seconds = lastPanicTimestamp is { } timestamp ? clock.GetElapsedTime(timestamp).TotalSeconds
                : Data.LastPanicUtc is { } utc ? (clock.GetUtcNow() - utc).TotalSeconds : double.PositiveInfinity;
            if (seconds >= 0 && seconds <= 10) Unlock("relapse");
        }
        if (progressEvent.Kind == ProgressEventKind.SessionEnded) wipeStarted = null;
    }

    partial void EvaluateFrame(EffectProgressState effects)
    {
        if (ContinuousSpiralSeconds >= 1200) Unlock("spiral_eyes");
        if (effects.StrictLock && effects.PanicDisabled && effects.PinkActive) Unlock("total_lockdown");
        if (effects.BubblesActive && effects.BouncingTextActive && effects.SpiralActive) Unlock("system_overload");
        CheckWipe();
    }

    partial void EvaluateSession(CompletedSession session)
    {
        if (session.Seconds >= 10800) Unlock("deep_sleep");
        if (session.PanicDisabled) Unlock("what_panic_button");
        if (session.Preset == "Gentle")
        {
            if (session.FinishedAt.Hour is >= 6 and < 9) Unlock("morning_glory");
            if (session.Seconds >= 1800) Unlock("sofa_decor");
        }
        if (session.Preset == "Daily" && !session.SwitchedAway) Unlock("player_2_disconnected");
        if (session.Preset == "Deep" && session.StrictLock) Unlock("look_but_dont_touch");
    }

    public void BubbleCountAnswer(bool correct)
    {
        if (!IsRunning) return;
        Data.BubbleCountCorrectStreak = correct ? Math.Min(Data.BubbleCountCorrectStreak + 1, 1_000_000) : 0;
        Data.BubbleCountBestStreak = Math.Max(Data.BubbleCountBestStreak, Data.BubbleCountCorrectStreak);
        RecordAchievement(new(AchievementEventKind.BubbleCount, correct, Data.BubbleCountCorrectStreak));
    }

    public void VideoTargetAnswer(bool correct)
    {
        if (!IsRunning) return;
        Data.VideoTargetCorrectStreak = correct ? Math.Min(Data.VideoTargetCorrectStreak + 1, 1_000_000) : 0;
        Data.VideoTargetBestStreak = Math.Max(Data.VideoTargetBestStreak, Data.VideoTargetCorrectStreak);
        if (Data.VideoTargetCorrectStreak >= 5) Unlock("mathematicians_nightmare");
        AttentionAnswer(correct);
    }

    public void LockResult(int errors, int phraseCount, TimeSpan elapsed) => LockResult(errors, phraseCount, elapsed.TotalSeconds);
    public void LockResult(int errors, int phraseCount, double seconds)
    {
        if (!IsRunning || errors < 0 || phraseCount < 1 || !double.IsFinite(seconds) || seconds < 0) return;
        Data.LockCardsCompleted = Math.Min(Data.LockCardsCompleted + 1, 1_000_000_000_000);
        if (errors == 0) Unlock("typing_tutor");
        if (phraseCount >= 3 && seconds < 15) Unlock("obedience_reflex");
        RecordAchievement(new(AchievementEventKind.LockCard, errors == 0, phraseCount, seconds));
    }

    public void AttentionAnswer(bool correct)
    {
        if (!IsRunning) return;
        if (!correct) Data.AttentionFailures = Math.Min(Data.AttentionFailures + 1, 1_000_000_000_000);
        if (Data.AttentionFailures >= 3) Unlock("mercy_beggar");
        RecordAchievement(new(AchievementEventKind.Attention, correct));
    }

    public void AvatarClicked()
    {
        var now = clock.GetTimestamp();
        if (avatarFirstClick is null || clock.GetElapsedTime(avatarFirstClick.Value, now).TotalSeconds > 10)
        { avatarFirstClick = now; avatarClicks = 1; }
        else avatarClicks++;
        if (avatarClicks >= 20) Unlock("neon_obsession");
        RecordAchievement(new(AchievementEventKind.AvatarClick, Count: avatarClicks));
    }

    // The caller supplies a successful panic action, before or after the session's stop notification.
    public void PanicPressed()
    {
        Data.LastPanicUtc = clock.GetUtcNow(); lastPanicTimestamp = clock.GetTimestamp();
        RecordAchievement(new(AchievementEventKind.Panic));
    }

    public void CornerHit()
    {
        if (!IsRunning) return;
        Unlock("corner_hit"); RecordAchievement(new(AchievementEventKind.Corner));
    }

    public void WipeStarted()
    {
        if (!IsRunning || wipeStarted is not null) return;
        wipeStarted = clock.GetTimestamp(); RecordAchievement(new(AchievementEventKind.WipeStarted));
    }

    public void WipeEnded()
    {
        if (wipeStarted is null) return;
        if (IsRunning) CheckWipe();
        wipeStarted = null; RecordAchievement(new(AchievementEventKind.WipeEnded));
    }

    private void CheckWipe()
    {
        if (wipeStarted is { } startedAt && clock.GetElapsedTime(startedAt).TotalSeconds >= 60) Unlock("clean_slate");
    }

    private void RecordAchievement(AchievementEvent progressEvent)
    {
        AchievementRecorded?.Invoke(progressEvent); Changed?.Invoke();
    }

    private void Unlock(string id)
    {
        if (!AchievementCatalog.ById.TryGetValue(id, out var definition) || !Data.UnlockedAchievements.Add(id)) return;
        AchievementUnlocked?.Invoke(definition); Changed?.Invoke();
    }
}
