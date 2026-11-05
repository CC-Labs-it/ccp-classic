// Adapted from public CCP fe40ea7587ac0b09e47d0724e59d7178db3336da, MIT.
namespace CCP.Classic.Core.Flash;

public static class FlashPlacement
{
    internal const int SpawnEdgePadding = 50;
    internal static (int MinX, int MinY, int MaxX, int MaxY) SpawnBounds(int monW, int monH, int w, int h)
    {
        var minX = SpawnEdgePadding;
        var minY = SpawnEdgePadding;
        var maxX = Math.Max(minX + 1, monW - w - SpawnEdgePadding);
        var maxY = Math.Max(minY + 1, monH - h - SpawnEdgePadding);
        return (minX, minY, maxX, maxY);
    }

    internal static bool TryPickAvoidCenterPoint(
        int monW, int monH, int w, int h, int pct, Random random, out int x, out int y)
    {
        x = y = 0;

        var b = new AvoidCenterBands(monW, monH, w, h, pct);
        long total = b.TotalArea;
        if (total <= 0) return false;

        long roll = (long)(random.NextDouble() * total);
        if (roll >= total) roll = total - 1;
        Span<long> areas = stackalloc long[4] { b.LeftArea, b.RightArea, b.AboveArea, b.BelowArea };
        int band = 0;
        for (; band < 3; band++)
        {
            if (roll < areas[band]) break;
            roll -= areas[band];
        }

        switch (band)
        {
            case 0: x = random.Next(b.MinX, b.LeftMaxX); y = random.Next(b.MinY, b.MaxY); break;
            case 1: x = random.Next(b.RightMinX, b.MaxX); y = random.Next(b.MinY, b.MaxY); break;
            case 2: x = random.Next(b.StripMinX, b.StripMaxX); y = random.Next(b.MinY, b.AboveMaxY); break;
            default: x = random.Next(b.StripMinX, b.StripMaxX); y = random.Next(b.BelowMinY, b.MaxY); break;
        }
        return true;
    }

    internal const int MinExclusionPercent = 5;

    internal const int MaxExclusionPercent = 60;

    internal const double MinLegalAreaFraction = 0.10;

    public static bool TryPickAvoidCenterPointAdaptive(
        int monW, int monH, int w, int h, int pct, Random random,
        out int x, out int y, out int effectivePct)
    {
        x = y = 0;
        effectivePct = Math.Clamp(pct, MinExclusionPercent, MaxExclusionPercent);

        while (true)
        {
            var bands = new AvoidCenterBands(monW, monH, w, h, effectivePct);
            if (bands.LegalFraction >= MinLegalAreaFraction || effectivePct <= MinExclusionPercent)
            {
                if (bands.TotalArea <= 0) return false;
                return TryPickAvoidCenterPoint(monW, monH, w, h, effectivePct, random, out x, out y);
            }
            effectivePct = Math.Max(MinExclusionPercent, effectivePct - 5);
        }
    }

    internal static double LegalAreaFraction(int monW, int monH, int w, int h, int pct)
        => new AvoidCenterBands(monW, monH, w, h, pct).LegalFraction;

    private readonly struct AvoidCenterBands
    {
        public readonly int MinX, MinY, MaxX, MaxY;
        public readonly int LeftMaxX, RightMinX, StripMinX, StripMaxX, AboveMaxY, BelowMinY;
        public readonly long LeftArea, RightArea, AboveArea, BelowArea;

        public AvoidCenterBands(int monW, int monH, int w, int h, int pct)
        {
            (MinX, MinY, MaxX, MaxY) = SpawnBounds(monW, monH, w, h);

            double side = Math.Clamp(pct, MinExclusionPercent, MaxExclusionPercent) / 100.0 * Math.Min(monW, monH);
            int exLeft = (int)Math.Round((monW - side) / 2.0);
            int exTop = (int)Math.Round((monH - side) / 2.0);
            int exRight = exLeft + (int)Math.Round(side);
            int exBottom = exTop + (int)Math.Round(side);

            LeftMaxX = Math.Min(MaxX, exLeft - w + 1);
            RightMinX = Math.Max(MinX, exRight);
            StripMinX = Math.Max(MinX, LeftMaxX);
            StripMaxX = Math.Min(MaxX, RightMinX);
            AboveMaxY = Math.Min(MaxY, exTop - h + 1);
            BelowMinY = Math.Max(MinY, exBottom);

            LeftArea = Area(MinX, LeftMaxX, MinY, MaxY);
            RightArea = Area(RightMinX, MaxX, MinY, MaxY);
            AboveArea = Area(StripMinX, StripMaxX, MinY, AboveMaxY);
            BelowArea = Area(StripMinX, StripMaxX, BelowMinY, MaxY);
        }

        public long TotalArea => LeftArea + RightArea + AboveArea + BelowArea;

        public long SpawnArea => (long)Math.Max(0, MaxX - MinX) * Math.Max(0, MaxY - MinY);

        public double LegalFraction => SpawnArea <= 0 ? 0.0 : (double)TotalArea / SpawnArea;

        private static long Area(int x0, int x1, int y0, int y1)
            => (long)Math.Max(0, x1 - x0) * Math.Max(0, y1 - y0);
    }
}
