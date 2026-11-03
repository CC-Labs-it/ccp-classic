namespace CCP.Classic.Core;

// Public CCP BouncingTextService: shared by both logos,15XP/2seconds and150XP per minute.
public sealed class BouncingTextReward
{
    private double lastObserved, lastAward = double.NegativeInfinity;
    private double? minuteStarted;
    private int paid;
    public int Grant(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) return 0;
        var now = Math.Max(lastObserved, seconds); lastObserved = now;
        if (minuteStarted is null || now - minuteStarted.Value >= 60) { minuteStarted = now; paid = 0; }
        if (now - lastAward < 2 || paid >= 150) return 0;
        lastAward = now; paid += 15; return 15;
    }
}
