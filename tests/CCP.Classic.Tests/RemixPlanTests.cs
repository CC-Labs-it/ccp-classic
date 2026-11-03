using CCP.Classic.Core.Remix;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class RemixPlanTests
{
    // Golden values executed directly from pinned public auto.js/rng.js, not the native implementation.
    [Theory]
    [InlineData(0u, 539199131u, .6972269753459841, "cover", "clean", "focus", 0, 26)]
    [InlineData(1u, 1249861921u, .5968422056175768, "ripple", "seamless", "spiral", 17, 36)]
    [InlineData(7u, 315666753u, .7821806001011282, "deal", "seamless", "glitch", 6, 27)]
    [InlineData(42u, 3792945621u, .24740377604030073, "mirror", "seamless", "spiral", 0, 19)]
    [InlineData(999u, 2307284363u, .5488037876784801, "ripple", "clean", "tint", 16, 31)]
    [InlineData(uint.MaxValue, 789400726u, .8330246764235198, "carousel", "seamless", "tint", 17, 29)]
    public void SeededRollMatchesTheOriginalJavaScript(uint seed, uint mix, double first, string layout, string loop, string effect, int start, int end)
    {
        Assert.Equal(mix, RemixRandom.Mix(seed, "auto:8")); Assert.Equal(first, new RemixRandom(seed, "auto:8").Next());
        var scene = RemixScene.Create(seed, Enumerable.Repeat(480d / 270, 8).ToArray());
        Assert.Equal(layout, scene.Layout); Assert.Equal(loop, scene.Loop); Assert.Equal(seed, scene.Seed);
        Assert.Equal(effect, scene.Blocks[0].Effect); Assert.Equal(start, scene.Blocks[0].Start); Assert.Equal(end, scene.Blocks[0].End);
        Assert.Equal(480, scene.Width); Assert.Equal(270, scene.Height); Assert.True(RemixScene.PassesGate(scene.Blocks));
    }
    [Fact]
    public void OriginalSourceSelectionCyclesOnlyEligibleLocalGifsAndKeepsEightTiles()
    {
        var folder = Path.GetTempPath(); var a = Path.Combine(folder, "a.gif"); var b = Path.Combine(folder, "b.gif"); var large = Path.Combine(folder, "large.gif");
        var sources = RemixPlan.SelectSources([a, a, b, large, Path.Combine(folder, "still.png"), "https://invalid.example/remote.gif"], 7,
            path => path == large ? RemixPlan.MaximumSourceBytes + 1 : 100);
        Assert.Equal(8, sources.Count); Assert.All(sources, path => Assert.Contains(path, new[] { a, b }));
        Assert.Equal(sources.Take(2), sources.Skip(2).Take(2));
        Assert.Empty(RemixPlan.SelectSources([a], 7, _ => -1));
    }
    [Fact]
    public void CarriedRollWaitsForActualReadyMediaWithoutLosingItsSeed()
    {
        var state = new RemixRollState(); Assert.False(state.Take(false, true, true, 1));
        Assert.False(state.Take(true, true, false, 7)); Assert.True(state.Carried); Assert.Equal(7, state.Seed);
        Assert.False(state.Take(true, false, false, 99)); Assert.Equal(7, state.Seed);
        Assert.True(state.Take(true, false, true, 99)); Assert.False(state.Carried);
        Assert.False(state.Take(true, false, true, 99));
    }
    [Fact]
    public void OriginalCenterGeometryAndOrientationRemainBounded()
    {
        var box = RemixPlan.Centered(1920, 1080, 480, 270); Assert.Equal((336d, 189d, 1248d, 702d), box);
        var tall = RemixScene.Create(7, [.5]); Assert.True(tall.Portrait); Assert.Equal((270, 480), (tall.Width, tall.Height));
        var square = RemixScene.Create(7, [1]); Assert.Equal((360, 360), (square.Width, square.Height));
    }
}
