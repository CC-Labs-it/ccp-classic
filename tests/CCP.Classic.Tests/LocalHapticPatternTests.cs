using CCP.Classic.Core.LocalDevices;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class LocalHapticPatternTests
{
    [Theory]
    [InlineData(LocalHapticPattern.Constant)]
    [InlineData(LocalHapticPattern.Pulse)]
    [InlineData(LocalHapticPattern.Wave)]
    [InlineData(LocalHapticPattern.Heartbeat)]
    [InlineData(LocalHapticPattern.Escalate)]
    [InlineData(LocalHapticPattern.Earthquake)]
    public void ShortEventsStillRenderFeltMotorEnvelopesAndFinishAtZero(LocalHapticPattern pattern)
    {
        var steps = LocalHapticPatterns.Render(pattern, .8, TimeSpan.FromMilliseconds(20), seed: 42);
        Assert.NotEmpty(steps); Assert.InRange(steps.Count, 1, 60);
        foreach (var step in steps)
        {
            Assert.InRange(step.Intensity, 0, .8);
            Assert.True(step.HoldMilliseconds >= 130 || step.AttackMilliseconds >= 130 && step.DecayMilliseconds >= 130);
        }
        var end = LocalHapticPatterns.TotalMilliseconds(steps);
        Assert.InRange(end, 130, 61000);
        Assert.Equal(0, LocalHapticPatterns.Sample(steps, -1));
        Assert.Equal(0, LocalHapticPatterns.Sample(steps, end));
        Assert.Contains(Enumerable.Range(0, end).Select(t => LocalHapticPatterns.Sample(steps, t)), level => level > .1);
    }

    [Fact]
    public void ModesHaveDifferentRhythmsRatherThanOneSecondRoundedPulses()
    {
        var sequences = Enum.GetValues<LocalHapticPattern>().Select(mode => LocalHapticPatterns.Render(mode, 1, TimeSpan.FromSeconds(2), seed: 42)).ToArray();
        var samples = sequences.Select(steps => string.Join(',', Enumerable.Range(0, 40)
            .Select(i => LocalHapticPatterns.Sample(steps, i * 50).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)))).ToArray();
        Assert.Equal(6, samples.Distinct().Count());
        Assert.Equal(130, sequences[(int)LocalHapticPattern.Pulse][0].HoldMilliseconds);
        Assert.Equal(140, sequences[(int)LocalHapticPattern.Heartbeat][1].OffsetMilliseconds);
    }

    [Fact]
    public void InvalidStrengthsZeroDurationAndLargeRequestsStayBounded()
    {
        Assert.Empty(LocalHapticPatterns.Render(LocalHapticPattern.Pulse, double.NaN, TimeSpan.FromSeconds(1)));
        Assert.Empty(LocalHapticPatterns.Render(LocalHapticPattern.Pulse, 1, TimeSpan.Zero));
        Assert.Empty(LocalHapticPatterns.Render(LocalHapticPattern.Pulse, -1, TimeSpan.FromSeconds(1)));
        foreach (var mode in Enum.GetValues<LocalHapticPattern>())
        {
            var steps = LocalHapticPatterns.Render(mode, 9, TimeSpan.MaxValue, seed: 42);
            Assert.InRange(steps.Count, 1, 60); Assert.InRange(LocalHapticPatterns.TotalMilliseconds(steps), 1, 61000);
            Assert.All(steps, step => Assert.InRange(step.Intensity, 0, 1));
        }
    }
}
