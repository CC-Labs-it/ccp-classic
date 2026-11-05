namespace CCP.Classic.Core.Remix;

public readonly record struct RemixPoint(double X, double Y);
public sealed record RemixBlock(string Effect, string Mode, int Start, int End)
{
    public double Strength { get; init; } = .5;
    public double Speed { get; init; } = .5;
    public int Style { get; init; }
    public int Direction { get; init; } = 1;
    public string Colour { get; init; } = "pink";
    public string Corner { get; init; } = "tl";
    public int Pick { get; init; }
    public IReadOnlyList<RemixPoint> Path { get; init; } = [];
    public double Ramp(int frame)
    {
        var length = Math.Max(1, End - Start); var r = length <= 1 ? 0 : Math.Max(1, Math.Min(3, length / 2));
        if (r == 0) return 1;
        var k = frame - Start; if (k < 0 || k >= length) return 0;
        var u = Math.Min(Math.Min(1, (k + 1d) / (r + 1)), Math.Min(1, (length - k) / (r + 1d)));
        return u * u * (3 - 2 * u);
    }
    public double Progress(int frame) => End - Start <= 1 ? 1 : Math.Clamp((frame - Start) / (End - Start - 1d), 0, 1);
}
public sealed record RemixScene(uint Seed, string Layout, string Loop, bool Portrait, int Width, int Height, IReadOnlyList<RemixBlock> Blocks)
{
    public const int Frames = 45, Fps = 15, Tiles = 8;
    // Public auto.js registry order is part of the seeded roll. Do not sort these entries.
    public static IReadOnlyList<string> Layouts { get; } = Array.AsReadOnly(new[]
        { "grow", "flat", "shuffle", "mirror", "stack", "spread", "slide", "ripple", "swallow", "cover", "tunnel", "deal", "carousel", "hop", "kenburns", "pinwheel" });
    public static RemixScene Create(uint seed, IReadOnlyList<double> aspects)
    {
        var first = aspects.Count > 0 && double.IsFinite(aspects[0]) && aspects[0] > 0 ? aspects[0] : 16d / 9;
        var portrait = first < .83; var square = first is >= .83 and <= 1.2;
        RemixScene result = null!;
        for (var attempt = 0; attempt <= 3; attempt++)
        {
            result = Candidate(seed, unchecked(seed + (uint)attempt), aspects, portrait, square);
            if (PassesGate(result.Blocks)) break;
        }
        return result;
    }
    private static RemixScene Candidate(uint projectSeed, uint rollSeed, IReadOnlyList<double> aspects, bool portrait, bool square)
    {
        var r = new RemixRandom(rollSeed, "auto:8");
        var weights = new Dictionary<string, double> { ["grow"] = 4, ["slide"] = 4, ["shuffle"] = 3, ["stack"] = 3,
            ["spread"] = 3, ["flat"] = 1, ["mirror"] = 1, ["ripple"] = 3, ["swallow"] = 2, ["cover"] = 2,
            ["deal"] = 2, ["carousel"] = 2, ["hop"] = 3, ["kenburns"] = 3 };
        if (aspects.Count > 0) weights[aspects.Count(a => (a > 1) == (!portrait && !square)) * 2 >= aspects.Count ? "flat" : "grow"]++;
        var layout = r.Weighted(Layouts.Where(weights.ContainsKey).Select(key => (key, weights[key])));
        _ = r.Pick(new[] { 400 }); // Keep the source RNG draw for its single stage-length option.
        var atHead = r.Chance(.65); var lo = atHead ? 15 : 0; var hi = atHead ? Frames : Frames - 15; var room = hi - lo;
        var count = int.Parse(r.Weighted([("1", 3), ("2", 4), ("3", 2)]), System.Globalization.CultureInfo.InvariantCulture);
        var pool = new List<(string Key, double Weight)> { ("tint", 4), ("glitch", 3), ("spiral", 2), ("focus", 2) };
        var picks = new List<string>();
        for (var i = 0; i < count; i++) { var pick = r.Weighted(pool); picks.Add(pick); pool.RemoveAll(item => item.Key == pick); }
        var blocks = new List<RemixBlock>();
        foreach (var effect in picks)
        {
            RemixBlock b; var spanLow = .3; var spanHigh = .5;
            if (effect == "tint")
            {
                var mode = r.Weighted([("creep", 3), ("wash", 1)]);
                var colour = r.Weighted([("pink", 3), ("lavender", 2), ("gold", 1)]); var corner = r.Pick(new[] { "tl", "tr", "bl", "br" });
                b = new(effect, mode, 0, 1) { Colour = colour, Corner = corner, Strength = r.Int(35, 70) / 100d };
                (spanLow, spanHigh) = mode == "creep" ? (.35, .6) : (.25, .4);
            }
            else if (effect == "spiral")
            {
                var mode = r.Weighted([("over", 3), ("through", 1)]);
                b = new(effect, mode, 0, 1) { Strength = r.Int(35, 70) / 100d, Speed = r.Int(35, 70) / 100d,
                    Style = r.Int(0, 2), Direction = r.Chance(.5) ? 1 : -1 };
            }
            else if (effect == "glitch") b = new(effect, r.Weighted([("ghost", 3), ("double", 2)]), 0, 1)
                { Strength = r.Int(35, 70) / 100d, Pick = r.Int(0, 96) };
            else
            {
                var mode = r.Weighted([("ring", 3), ("dark", 1)]); var radius = r.Int(35, 70) / 100d;
                var y1 = r.Range(.28, .72); var y2 = r.Range(.28, .72);
                b = new(effect, mode, 0, 1) { Strength = radius, Path = r.Chance(.5)
                    ? [new(.12, y1), new(.5, (y1 + y2) / 2), new(.88, y2)] : [new(.88, y1), new(.5, (y1 + y2) / 2), new(.12, y2)] };
                (spanLow, spanHigh) = (.4, .6);
            }
            var length = Math.Max(2, Math.Min(room, Round(Frames * r.Range(spanLow, spanHigh))));
            var start = b.Mode == "creep" ? hi - length : lo + r.Int(0, room - length);
            blocks.Add(b with { Start = start, End = start + length });
        }
        for (var guard = 0; guard < 8 && Coverage(blocks) > .7; guard++)
        {
            var longest = blocks.OrderByDescending(b => b.End - b.Start).First(); var cut = Math.Max(1, Round((longest.End - longest.Start) * .15));
            if (longest.End - longest.Start - cut < 2) break;
            blocks[blocks.IndexOf(longest)] = longest with { Start = longest.Start + cut };
        }
        var loop = r.Weighted(blocks.Any(b => b.Effect == "tint" && Frames - b.End <= 3)
            ? [("clean", 40), ("snap", 30), ("seamless", 30)] : [("clean", 40), ("seamless", 30)]);
        return new(projectSeed, layout, loop, portrait, square ? 360 : portrait ? 270 : 480, square ? 360 : portrait ? 480 : 270, blocks.AsReadOnly());
    }
    public static double Coverage(IReadOnlyList<RemixBlock> blocks) => Enumerable.Range(0, Frames).Count(frame => blocks.Any(b => frame >= b.Start && frame < b.End)) / (double)Frames;
    public static bool PassesGate(IReadOnlyList<RemixBlock> blocks)
    {
        var clean = 0; var longest = 0;
        for (var frame = 0; frame < Frames; frame++)
        { clean = blocks.Any(b => frame >= b.Start && frame < b.End) ? 0 : clean + 1; longest = Math.Max(longest, clean); }
        return blocks.Count is >= 1 and <= 3 && Coverage(blocks) <= .7 && longest >= Fps
            && blocks.All(b => b.Start >= 0 && b.End <= Frames && b.End > b.Start && b.Effect is not ("drain" or "caption"));
    }
    public static int Round(double value) => (int)Math.Floor(value + .5);
}
