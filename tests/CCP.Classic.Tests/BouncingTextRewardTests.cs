using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class BouncingTextRewardTests
{
    [Fact]
    public void SourceRewardsShareOneCooldownAndOneMinuteCapAcrossBothTextLines()
    {
        var reward = new BouncingTextReward();
        Assert.Equal(15, reward.Grant(100)); Assert.Equal(0, reward.Grant(100)); Assert.Equal(0, reward.Grant(101.999));
        var paid = 15;
        for (var seconds = 102; seconds < 160; seconds += 2) paid += reward.Grant(seconds);
        Assert.Equal(150, paid); Assert.Equal(15, reward.Grant(160)); Assert.Equal(0, reward.Grant(160));
    }
    [Fact]
    public void InvalidBackwardAndRestartedSessionTimeCannotBypassTheRewardLimit()
    {
        var reward = new BouncingTextReward(); Assert.Equal(15, reward.Grant(10));
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, -100, 0, 10.5 }) Assert.Equal(0, reward.Grant(value));
        Assert.Equal(15, reward.Grant(12)); Assert.Equal(0, reward.Grant(12));
    }
}
