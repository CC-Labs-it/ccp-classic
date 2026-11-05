namespace CCP.Classic.Core.Remix;

// Exact uint32 mixSeed, mulberry32 and stateless helpers from public Remix engine/rng.js at fe40ea7.
public sealed class RemixRandom(uint seed, string label = "")
{
    private uint state = Mix(seed, label) is var mixed && mixed != 0 ? mixed : 1;
    public static uint Mix(uint seed, string label)
    {
        unchecked
        {
            var h = seed ^ 0x9e3779b9;
            foreach (var c in label) { h = (h ^ c) * 0x85ebca6b; h = h << 13 | h >> 19; }
            h = (h ^ h >> 16) * 0x2545f491; return h ^ h >> 15;
        }
    }
    public double Next()
    {
        unchecked
        {
            state += 0x6d2b79f5; var t = state;
            t = (t ^ t >> 15) * (t | 1); t ^= t + (t ^ t >> 7) * (t | 61);
            return (t ^ t >> 14) / 4294967296d;
        }
    }
    public double Range(double min, double max) => min + Next() * (max - min);
    public int Int(int min, int max) => min + (int)Math.Floor(Next() * (max - min + 1));
    public bool Chance(double probability) => Next() < probability;
    public T Pick<T>(IReadOnlyList<T> values) => values[(int)(Next() * values.Count) % values.Count];
    public T[] Shuffle<T>(IEnumerable<T> values)
    {
        var result = values.ToArray();
        for (var i = result.Length - 1; i > 0; i--) { var j = (int)(Next() * (i + 1)); (result[i], result[j]) = (result[j], result[i]); }
        return result;
    }
    public string Weighted(IEnumerable<(string Key, double Weight)> table)
    {
        var values = table.Where(item => item.Weight > 0).ToArray(); var x = Next() * values.Sum(item => item.Weight);
        foreach (var item in values) { x -= item.Weight; if (x < 0) return item.Key; }
        return values[^1].Key;
    }
    public static double Hash(uint seed, params int[] coordinates)
    {
        unchecked
        {
            var h = seed;
            foreach (var coordinate in coordinates) { h = (h ^ (uint)coordinate) * 0x27d4eb2d; h = h << 15 | h >> 17; }
            h = (h ^ h >> 13) * 0x85ebca6b; return (h ^ h >> 16) / 4294967296d;
        }
    }
    public static double Noise(uint seed, double x)
    {
        var i = (int)Math.Floor(x); var f = x - i; var u = f * f * (3 - 2 * f);
        return Hash(seed, i) + (Hash(seed, i + 1) - Hash(seed, i)) * u;
    }
    public static double Fbm(uint seed, double x) => Noise(seed, x) * .65 + Noise(seed ^ 0x5bf03635, x * 2.17) * .35;
}
