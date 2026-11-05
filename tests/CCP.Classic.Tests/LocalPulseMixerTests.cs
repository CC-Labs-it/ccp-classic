using CCP.Classic.Core.LocalDevices;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class LocalPulseMixerTests
{
    private static LocalHapticTemperament Balanced => LocalHapticTemperament.FromKey("balanced");
    [Fact]
    public void ProfileShapesOnlyEdgesAndUnknownValuesRemainBalanced()
    {
        var step = new LocalHapticStep(200, .4, 10, 130, 20); var gentle = LocalHapticTemperament.FromKey("gentle");
        Assert.Equal(new(200, .4, 14, 130, 26), gentle.Shape(step)); Assert.Equal(step, Balanced.Shape(step));
        Assert.Same(Balanced, LocalHapticTemperament.FromKey("unknown"));
        Assert.Equal(.70, gentle.ContinuousScale); Assert.Equal(.75, gentle.TransientScale);
        Assert.Equal(2, LocalHapticTemperament.FromKey("cruel").PulsePriorityBias);
    }
    [Fact]
    public void EqualPriorityCannotEvictALivePulseButHigherPriorityAndExpiredCorpsesCan()
    {
        var mixer = new LocalPulseMixer();
        mixer.Enqueue(LocalDeviceEvent.BubblePop, [new(0, .2, 0, 100, 0)], 0, null, 0, Balanced, 1);
        mixer.Enqueue(LocalDeviceEvent.Flash, [new(0, 1, 0, 100, 0)], 0, null, 0, Balanced, 1);
        Assert.Equal(1, mixer.ActiveCount); Assert.Equal(.2, mixer.Sample("toy", _ => true, 1));
        mixer.Enqueue(LocalDeviceEvent.Achievement, [new(0, .9, 0, 100, 0)], 2, null, 0, Balanced, 1);
        Assert.Equal(.9, mixer.Sample("toy", _ => true, 1));
        mixer.Enqueue(LocalDeviceEvent.Flash, [new(0, .3, 0, 100, 0)], 0, null, 110, Balanced, 1);
        Assert.Equal(.3, mixer.Sample("toy", _ => true, 1));
    }
    [Fact]
    public void SharedWindowSamplesThePeakOfTheSumAndDeviceTargetsStayIndependent()
    {
        var mixer = new LocalPulseMixer();
        mixer.Enqueue(LocalDeviceEvent.Manual, [new(0, .4, 100, 0, 100), new(100, .4, 100, 0, 100)], 0, "chosen", 0, Balanced, 4);
        mixer.Advance(100, Balanced, 4);
        Assert.Equal(.4, mixer.Sample("chosen", _ => false, 1), 8); Assert.Equal(0, mixer.Sample("other", _ => true, 1));
        mixer.Clear(); Assert.Equal(0, mixer.ActiveCount); Assert.Equal(0, mixer.PendingCount);
    }
    [Fact]
    public void ProfileBiasChangesCapacityAndFuturePatternStepsAreBounded()
    {
        var mixer = new LocalPulseMixer(); var profile = LocalHapticTemperament.FromKey("gentle");
        for (var index = 0; index < 100; index++) mixer.Enqueue(LocalDeviceEvent.Flash, [new(0, .2, 0, 100, 0)], 0, null, 0, profile, 4);
        Assert.Equal(3, mixer.ActiveCount);
        for (var index = 0; index < 100; index++) mixer.Enqueue(LocalDeviceEvent.Flash, [new(10000, .2, 0, 100, 0)], 0, null, 0, profile, 4);
        Assert.InRange(mixer.PendingCount, 1, 256); mixer.Advance(20000, profile, 4); Assert.InRange(mixer.ActiveCount, 0, 3);
    }
}
