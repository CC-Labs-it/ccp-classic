using System.Text.Json;
using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class LoomTests
{
    [Fact]
    public void NormalizationBoundsEveryFieldAndCopiesTheThreadColors()
    {
        var colors = new[] { "bad", "#FF69B4", "#ff69b4", "#FFFFFF" };
        var original = new LoomDefinition
        {
            Speed = 99, Glow = double.NaN, HueCycles = 9,
            Layer = new() { Arms = 999, Turns = double.PositiveInfinity, Duty = .63, Colors = colors, Style = "bad", Direction = 9 },
            Pulse = new(.9, 99), Wobble = new(-1, 99, 99),
            Centerpiece = new("mantra", "bad", 2, "abcdefghijklmnop\n", 99)
        };
        var valid = original.Normalize(); colors[1] = "#000000";
        Assert.Equal(12, valid.Layer.Arms); Assert.Equal(2, valid.Layer.Turns);
        Assert.Equal(.65, valid.Layer.Duty, 8); Assert.Equal("log", valid.Layer.Style);
        Assert.Equal(1, valid.Layer.Direction); Assert.Equal(["#ff69b4", "#ffffff"], valid.Layer.Colors);
        Assert.Equal(5, valid.Speed); Assert.Equal(0, valid.Glow); Assert.Equal(2, valid.HueCycles);
        Assert.Equal(new LoomPulse(.25, 4), valid.Pulse); Assert.Equal(new LoomWobble(0, 6, 3), valid.Wobble);
        Assert.Equal("abcdefghijkl", valid.Centerpiece.Text); Assert.Equal(.4, valid.Centerpiece.SizeFrac);
        Assert.Equal("#ffffff", valid.Centerpiece.Color); Assert.Equal(4, valid.Centerpiece.FlashCycles);
        Assert.Equal(999, original.Layer.Arms);
    }

    [Fact]
    public void DesktopSchemaOneImportsWithAllNewEffectsAtNeutralDefaults()
    {
        var spiral = LoomDefinition.Parse("""
            {"schema":1,"arms":5,"turns":1.75,"style":"arch","duty":0.4,"speed":4,
             "direction":-1,"colors":["#ffffff","#ff69b4"],"bg":"#123456"}
            """);
        Assert.Equal(2, spiral.Schema); Assert.Equal(5, spiral.Layer.Arms); Assert.Equal(1.75, spiral.Layer.Turns);
        Assert.Equal("arch", spiral.Layer.Style); Assert.Equal(-1, spiral.Layer.Direction);
        Assert.Equal("#123456", spiral.Bg.Color); Assert.Equal(4, spiral.Speed);
        Assert.False(spiral.Layer2.Enabled); Assert.Equal(0, spiral.Glow); Assert.Equal(0, spiral.Pulse.Amp);
        Assert.Equal(0, spiral.Wobble.Amp); Assert.Equal(0, spiral.HueCycles); Assert.Equal("none", spiral.Centerpiece.Kind);
        Assert.Throws<JsonException>(() => LoomDefinition.Parse("{\"schema\":9,\"arms\":4}"));
        Assert.Throws<JsonException>(() => LoomDefinition.Parse("{\"schema\":\"2\"}"));
    }

    [Theory]
    [InlineData("log", .5665759239675465, false, 1)]
    [InlineData("arch", .37, false, 1)]
    [InlineData("ribbon", .4124382105274775, true, 1)]
    [InlineData("golden", .6009431574235197, true, 1)]
    [InlineData("tunnel", .6009431574235197, false, 0)]
    [InlineData("petal", .6009431574235197, false, 1)]
    public void GeometryMatchesSamplesEvaluatedByTheCanonicalJavaScript(string style, double t, bool lit, int index)
    {
        var layer = new LoomLayer { Style = style, Arms = 5, Turns = 2.25, Duty = .45, Colors = ["#ff69b4", "#ffffff", "#8a5cff"] };
        Assert.Equal(t, LoomMath.StyleT(.37, style), 12);
        Assert.Equal(new LoomBand(lit, index), LoomMath.BandAt(layer, .37, 1.23, .17, .06));
        Assert.Equal(.37, LoomMath.StyleR(t, style), 6);
    }

    [Fact]
    public void ThreadSymmetryAndTimingProduceSeamlessLoops()
    {
        Assert.Equal(new[] { 3600, 2520, 1800, 1080, 720 }, Enumerable.Range(1, 5).Select(LoomMath.LoopMilliseconds));
        Assert.Equal((512, 288), LoomMath.Dimensions("wide", 900));
        Assert.Equal((288, 512), LoomMath.Dimensions("tall", 512));
        foreach (var style in LoomDefinition.Styles)
        foreach (var arms in new[] { 3, 4, 12 })
        {
            var layer = new LoomLayer { Style = style, Arms = arms, Colors = ["#ffffff", "#ff69b4"] };
            Assert.Equal(style == "tunnel" || arms % 2 == 0 ? Math.Tau / arms * 2 : Math.Tau, LoomMath.Span(layer), 10);
            for (var point = 1; point < 20; point++)
                Assert.Equal(LoomMath.BandAt(layer, point / 21d, point * .173),
                    LoomMath.BandAt(layer, point / 21d, point * .173, LoomMath.Rotation(layer, 1)));
        }
    }

    [Fact]
    public void SavedSpiralsRoundTripAtomicallyAndHaveASmallRackLimit()
    {
        using var fixture = new LoomFixture();
        var saved = fixture.Store.Save("  First  ", new() { Layer = new() { Colors = ["#ffffff"] } });
        var loaded = Assert.Single(fixture.Store.LoadAll(out var recovered));
        Assert.False(recovered); Assert.Equal(saved.Id, loaded.Id); Assert.Equal("First", loaded.Name);
        Assert.Equal("#ffffff", loaded.Definition.Layer.Colors[0]);
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.tmp"));
        Assert.False(File.ReadAllBytes(Path.Combine(fixture.Root, saved.Id + ".json")).AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        for (var index = 1; index < LoomStore.MaximumSpirals; index++) fixture.Store.Save("Spiral " + index, new());
        Assert.Throws<IOException>(() => fixture.Store.Save("Overflow", new()));
        fixture.Store.Save("Renamed", saved.Definition, saved.Id);
        Assert.Equal(12, fixture.Store.LoadAll().Count);
        fixture.Store.Delete(saved.Id); fixture.Store.Delete(saved.Id);
        Assert.Equal(11, fixture.Store.LoadAll().Count);
        Assert.Throws<ArgumentException>(() => fixture.Store.Delete("../settings"));
        Assert.Throws<ArgumentException>(() => fixture.Store.Save("x", new(), "../../settings"));
    }

    [Fact]
    public void BrokenOversizedAndNullDefinitionsDoNotHideValidSpirals()
    {
        using var fixture = new LoomFixture(); fixture.Store.Save("Valid", new());
        File.WriteAllText(Path.Combine(fixture.Root, Guid.NewGuid().ToString("N") + ".json"), new string('x', 65537));
        var id = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(fixture.Root, id + ".json"), $"{{\"id\":\"{id}\",\"name\":\"Null\",\"definition\":null}}");
        Assert.Single(fixture.Store.LoadAll(out var recovered)); Assert.True(recovered);
        var remote = new LoomStore("//server/share/spirals");
        Assert.Empty(remote.LoadAll(out recovered)); Assert.True(recovered);
        Assert.Throws<IOException>(() => remote.Save("Remote", new()));
    }

    private sealed class LoomFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public LoomStore Store => new(Root);
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
    }
}
