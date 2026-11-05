using CCP.Classic.Core.Remix;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class RemixLayoutTests
{
    private static RemixScene Scene(string mode, bool portrait = false) => new(7, mode, "clean", portrait, portrait ? 270 : 480, portrait ? 480 : 270, []);
    [Fact]
    public void EveryOriginalLayoutHasFiniteBoundedSlotsForEveryFrameAndOrientation()
    {
        Assert.Equal(16, RemixScene.Layouts.Count);
        foreach (var mode in RemixScene.Layouts)
        foreach (var portrait in new[] { false, true })
        for (var frame = 0; frame < 45; frame++)
        {
            var slots = RemixLayouts.At(Scene(mode,portrait),frame); Assert.InRange(slots.Count,1,64);
            Assert.All(slots,slot=>
            {
                Assert.InRange(slot.Tile,0,7); Assert.True(double.IsFinite(slot.Rect.X+slot.Rect.Y+slot.Rect.Width+slot.Rect.Height+slot.Rotation+slot.Scale+slot.Alpha));
                Assert.InRange(slot.Rect.Width,0,3);Assert.InRange(slot.Rect.Height,0,3);Assert.InRange(slot.Alpha,0,1);
            });
        }
    }
    [Theory]
    [InlineData("stack", 9, .12943617592876155, .23213880162755102, .4395833333333333, .5851851851851851)]
    [InlineData("slide", 30, -.0819137639429472, 0, .15416666666666667, .24259259259259258)]
    [InlineData("deal", 8, .02789374999999999, .21878554847741952, .3142125, .42)]
    [InlineData("carousel", 8, .09080024417706276, .2876453587798991, .317735631925576, .4247092824402019)]
    [InlineData("tunnel", 12, -.16261732661668216, -.16261732661668216, 1.3252346532333643, 1.3252346532333643)]
    [InlineData("pinwheel", 9, .5055338923179389, .7482837525359265, .1472374506024683, .20940437419017713)]
    public void FirstSlotMatchesPinnedJavaScriptMotionGolden(string mode,int count,double x,double y,double w,double h)
    {
        var slots=RemixLayouts.At(Scene(mode),17); Assert.Equal(count,slots.Count); var r=slots[0].Rect;
        Assert.Equal(x,r.X,11);Assert.Equal(y,r.Y,11);Assert.Equal(w,r.Width,11);Assert.Equal(h,r.Height,11);
    }
    [Fact]
    public void ActualJackpotGrowPrunesItsEightTileTreeAndMirrorRetainsSourceOffsets()
    {
        var grow=RemixLayouts.At(Scene("grow"),17);Assert.Equal(3,grow.Count);
        Assert.Equal(new RemixRect(0,0,.25,1),grow[0].Rect);Assert.Equal(new RemixRect(.25,0,.25,1),grow[1].Rect);
        Assert.Equal(new RemixRect(.5,0,.5,1),grow[2].Rect);
        var mirror=RemixLayouts.At(Scene("mirror"),17);Assert.All(mirror,slot=>Assert.Equal(0,slot.Tile));
        Assert.Equal(Enumerable.Range(0,8).Select(index=>index*6),mirror.Select(slot=>slot.FrameOffset));
    }
    [Fact]
    public void SourceCropAndMotionGoldenValuesArePreserved()
    {
        var crop=RemixLayouts.At(Scene("kenburns"),17)[0].Crop!.Value;
        Assert.Equal(.4652595964298586,crop.X,11);Assert.Equal(.3437965368163365,crop.Y,11);Assert.Equal(1.5518314475545596,crop.Zoom,11);
        var ripple=RemixLayouts.At(Scene("ripple"),17)[0];Assert.Equal(-.019134171618254495,ripple.Rotation,11);Assert.Equal(1.0634003833130468,ripple.Scale,11);
        Assert.Equal(new RemixRect(.25,0,.25,.5),RemixLayouts.At(Scene("hop"),17)[0].Rect);
    }
}
