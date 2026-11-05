using System.Diagnostics;
using CCP.Classic.Core;

namespace CCP.Classic.App;

public sealed partial class MainWindow
{
    // The cooldown survives session restarts, as in CCP's long-lived bouncing service.
    private readonly BouncingTextReward bounceReward = new();
    private void AwardBouncingTextWall()
    {
        if (!Session.IsRunning) return;
        var amount = bounceReward.Grant(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        if (amount > 0) Progress.AddXp(amount);
    }
}
