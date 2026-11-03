using CCP.Classic.Core.LocalDevices;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class LocalContinuousFloorTests
{
    [Fact]
    public void OriginalEightHundredMillisecondRiseIsSharedAcrossEarlyWakesAndFallsImmediately()
    {
        var floor = new LocalContinuousFloor(); Assert.Equal(.125, floor.Step(1, 0, 800));
        Assert.Equal(.1875, floor.Step(1, 50, 800)); Assert.Equal(.25, floor.Step(1, 100, 800));
        Assert.Equal(.25, floor.Step(1, 100, 800));
        for (var time = 200; time <= 700; time += 100) floor.Step(1, time, 800);
        Assert.Equal(1, floor.Value); Assert.Equal(.2, floor.Step(.2, 710, 800)); Assert.Equal(0, floor.Step(0, 711, 800));
        Assert.Equal(0, floor.Step(1, 700, 800)); Assert.Equal(.125, floor.Step(1, 100000, 800));
    }
    [Fact]
    public void PerceptibleFloorIsAppliedBeforeCapAndTrimAndCannotDefeatZeroPower()
    {
        Assert.Equal(.06, LocalContinuousFloor.Finish(.01, 1, 1, 1));
        Assert.Equal(.025, LocalContinuousFloor.Finish(.01, 1, .05, .5));
        Assert.Equal(.7, LocalContinuousFloor.Finish(2, 1, .7, 1));
        Assert.Equal(0, LocalContinuousFloor.Finish(1, 0, 1, 1)); Assert.Equal(0, LocalContinuousFloor.Finish(0, 1, 1, 1));
        Assert.Equal(.7, new LocalDeviceRoutingOptions().MaximumPower); Assert.Equal(800, new LocalDeviceRoutingOptions().SoftRampMilliseconds);
    }
}
