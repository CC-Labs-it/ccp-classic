using CCP.Classic.Core;

namespace CCP.Classic.App;

// Player actions the panel owns, raised as companion bark events (docs/BARKS.md).
public sealed partial class MainWindow
{
    private int barkLevel;
    private bool barksReady;
    private DateTimeOffset? sessionAwaySince;

    private void InitializeBarks()
    {
        if (effects is null) return;
        effects.BarkFacts = () => new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["level"] = Progress.Level, ["streak"] = Progress.Data.ConsecutiveDays,
            ["lifetime_minutes"] = Math.Floor(Progress.Data.SessionSeconds / 60),
            ["achievements_left"] = AchievementCatalog.All.Count - Progress.UnlockedAchievementCount
        };
        effects.DaysAwayAtLaunch = Progress.DaysAwayAtLaunch;
        barkLevel = Progress.Level;
        Progress.Changed += () =>
        {
            var level = Progress.Level;
            if (level > barkLevel) effects?.Bark("LevelUp", new Dictionary<string, double> { ["level"] = level });
            barkLevel = level;
        };
        Progress.AchievementUnlocked += _ => effects?.Bark("Achievement");
        // A new day in a streak first, else the greeting; both wait until the tube is on screen.
        if (Progress.DaysAwayAtLaunch == 1)
            effects.QueueIdleBark("StreakMilestone");
        effects.QueueIdleBark("AppOpened", Progress.DaysAwayAtLaunch is { } away
            ? new() { ["days_away"] = away, ["first"] = 0 } : new() { ["first"] = 1 });
        Deactivated += (_, _) => { if (Session.IsRunning) sessionAwaySince ??= DateTimeOffset.UtcNow; };
        Activated += (_, _) =>
        {
            if (Session.IsRunning && sessionAwaySince is { } since && DateTimeOffset.UtcNow - since >= TimeSpan.FromSeconds(20))
                effects?.Bark("SessionRefocused");
            sessionAwaySince = null;
        };
        barksReady = true;
    }

    // Opening a page or an effect's window; never while the shell is being built.
    private void BarkOpened(string key)
    {
        if (barksReady) effects?.Bark("Opened." + key);
    }

    // Every panic route: progress first, then the bark lane falls silent and stays quiet.
    private void Panicked()
    {
        Progress.PanicPressed(); effects?.SilenceBarks();
    }
}
