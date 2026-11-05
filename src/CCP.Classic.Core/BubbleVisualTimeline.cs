namespace CCP.Classic.Core;

public sealed record BubbleImageVisual(int Image, bool Fullscreen, double X, double YDip, double SizeDip, double Scale, double Opacity);

// Public CCP ChaosFlashOverlay and ChaosGifCascadeOverlay, fe40ea7. Local cached images only.
public sealed class BubbleVisualTimeline(int seed = 0)
{
    private sealed class Faller(int image, double x, double speed, double born, double expires)
    {
        public readonly int Image = image;
        public readonly double X = x, Speed = speed, Born = born, Expires = expires;
        public double Y = -400;
    }
    private readonly Random random = new(seed);
    private readonly List<Faller> falling = [];
    private double last, nextSpawn, rainUntil, washStarted, washUntil, washPeak;
    private int washImage = -1, imageCount;
    public bool CascadeActive => rainUntil > last || falling.Count > 0;
    public void Clear() { falling.Clear(); last = nextSpawn = rainUntil = washStarted = washUntil = 0; washImage = -1; }
    public bool Trigger(BubbleEffectKind kind, double now, int count)
    {
        if (count <= 0 || kind is not (BubbleEffectKind.Glitch or BubbleEffectKind.Cascade or BubbleEffectKind.BrainDrainWash)) return false;
        now = double.IsFinite(now) ? Math.Max(last, now) : last;
        imageCount = Math.Clamp(count, 0, 100000);
        if (kind == BubbleEffectKind.Cascade)
        { falling.Clear(); rainUntil = now + 6; nextSpawn = now; }
        else
        { washImage = random.Next(imageCount); washStarted = now; washUntil = now + (kind == BubbleEffectKind.Glitch ? 3.96 : 10);
            washPeak = kind == BubbleEffectKind.Glitch ? .3 : .1; }
        return true;
    }
    public IReadOnlyList<BubbleImageVisual> Step(double seconds, double height, bool gentle, bool reduced, bool performance)
    {
        var now = double.IsFinite(seconds) ? Math.Max(last, seconds) : last; var dt = Math.Clamp(now - last, 0, .1); last = now;
        var output = new List<BubbleImageVisual>();
        if (washImage >= 0)
        {
            var enter = gentle ? .75 : .5; var exit = gentle ? .75 : .7;
            var alpha = Math.Clamp((now - washStarted) / enter, 0, 1) * Math.Clamp(1 - (now - washUntil) / exit, 0, 1);
            if (alpha > 0) output.Add(new(washImage, true, 0, 0, 0, 1, washPeak * alpha));
            else if (now >= washUntil) washImage = -1;
        }
        if (now < rainUntil && now >= nextSpawn)
        {
            nextSpawn = now + 1 / 1.67;
            if (falling.Count < (performance ? 7 : 14)) falling.Add(new(random.Next(imageCount), random.NextDouble(),
                3.6 * (.7 + random.NextDouble() * .6) / .016, now, now + 120) { Y = reduced ? height * random.NextDouble() * .6 : -400 });
        }
        foreach (var item in falling.ToArray())
        {
            if (!reduced) item.Y += item.Speed * dt * (gentle ? .5 : 1);
            var alpha = Math.Clamp((now - item.Born) / (gentle ? .75 : .25), 0, 1);
            if (reduced) alpha *= Math.Clamp(1 - (now - rainUntil) / (gentle ? .75 : .3), 0, 1);
            if (item.Y > height + 400 || now >= item.Expires || reduced && alpha <= 0 && now >= rainUntil)
            { falling.Remove(item); continue; }
            var scale = .45 + .55 * Math.Clamp(item.Y / Math.Max(1, height * .75), 0, 1);
            output.Add(new(item.Image, false, item.X, item.Y, 400, scale, .9 * alpha));
        }
        return Array.AsReadOnly(output.ToArray());
    }
}
