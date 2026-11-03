// Adapted from public CCP fe40ea7587ac0b09e47d0724e59d7178db3336da, MIT.
using System;

namespace CCP.Classic.Core.Flash;

public sealed class FlashShard
{
    public double U0, V0, U1, V1;

    public double Dx, Dy;

    public double Vx, Vy;

    public double AngleRad, AngularVelRad;

    public double Alpha = 1.0;
}

public sealed class FlashShatterState
{
    public FlashShard[] Shards = Array.Empty<FlashShard>();

    public double RectX, RectY, RectW, RectH;

    public double FrozenAngleRad;
    public double PivotX, PivotY;

    public double BoundsX, BoundsY, BoundsW, BoundsH;

    public double DurationSec;

    public double GravityPxPerSec2;

    public double ElapsedSec;

    public bool Done;
}

public static class FlashShatter
{
    public const double DurationSec = 0.70;

    public const double ReducedDurationScale = 0.5;

    public const double GravityPxPerSec2 = 1400.0;

    public const int FullColumns = 3;
    public const int FullRowsMin = 2, FullRowsMax = 3;

    public const int ReducedColumns = 2, ReducedRows = 2;

    public const double CutJitterFraction = 0.12;

    public const double OutwardSpeedMin = 90.0, OutwardSpeedMax = 220.0;

    public const double DownKickMin = 30.0, DownKickMax = 140.0;

    public const double SpinMaxRadPerSec = 2.4;

    public const double HoldFraction = 0.30;

    public static int ShardCount(MotionLevel level, int fullRows) => level switch
    {
        MotionLevel.Full => FullColumns * Math.Clamp(fullRows, FullRowsMin, FullRowsMax),
        MotionLevel.Reduced => ReducedColumns * ReducedRows,
        _ => 0,
    };

    public static FlashShatterState Create(double x, double y, double w, double h,
        double bx, double by, double bw, double bh, MotionLevel level, Random rng, bool ninePieces = false)
    {
        var s = new FlashShatterState
        {
            RectX = x, RectY = y, RectW = w, RectH = h,
            BoundsX = bx, BoundsY = by, BoundsW = bw, BoundsH = bh,
            GravityPxPerSec2 = GravityPxPerSec2,
        };

        if (level == MotionLevel.Off || w <= 0 || h <= 0)
        {
            s.Done = true;
            return s;
        }

        var reduced = level == MotionLevel.Reduced;
        s.DurationSec = reduced ? DurationSec * ReducedDurationScale : DurationSec;

        var cols = reduced ? ReducedColumns : FullColumns;
        var rows = reduced ? ReducedRows : ninePieces ? 3 : rng.Next(FullRowsMin, FullRowsMax + 1);

        var vCuts = CutLines(cols, rng);
        var hCuts = CutLines(rows, rng);

        var shards = new FlashShard[cols * rows];
        var n = 0;
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                var u0 = vCuts[c];
                var u1 = vCuts[c + 1];
                var v0 = hCuts[r];
                var v1 = hCuts[r + 1];

                var ox = (u0 + u1) / 2.0 - 0.5;
                var oy = (v0 + v1) / 2.0 - 0.5;
                var len = Math.Sqrt(ox * ox + oy * oy);
                if (len < 1e-6) { ox = rng.NextDouble() - 0.5; oy = -0.25; len = Math.Sqrt(ox * ox + oy * oy); }
                if (len < 1e-6) { ox = 1; oy = 0; len = 1; }
                ox /= len;
                oy /= len;

                var speed = Lerp(OutwardSpeedMin, OutwardSpeedMax, rng.NextDouble());
                shards[n++] = new FlashShard
                {
                    U0 = u0, V0 = v0, U1 = u1, V1 = v1,
                    Vx = ox * speed,
                    Vy = oy * speed + Lerp(DownKickMin, DownKickMax, rng.NextDouble()),
                    AngularVelRad = (rng.NextDouble() * 2.0 - 1.0) * SpinMaxRadPerSec,
                    Alpha = 1.0,
                };
            }
        }
        s.Shards = shards;
        return s;
    }

    public static void TakeOverDrawnRect(FlashShatterState s, double x, double y, double w, double h)
    {
        s.RectX = x; s.RectY = y; s.RectW = w; s.RectH = h;
        s.FrozenAngleRad = 0;
        s.PivotX = 0; s.PivotY = 0;
    }

    public static void TakeOverHangingRect(FlashShatterState s, double pivotX, double pivotY,
        double rope, double angleRad, double mediaW, double mediaH)
    {
        s.RectX = pivotX - mediaW / 2.0;
        s.RectY = pivotY + rope - mediaH / 2.0;
        s.RectW = mediaW;
        s.RectH = mediaH;
        s.FrozenAngleRad = angleRad;
        s.PivotX = pivotX;
        s.PivotY = pivotY;
    }

    public static bool Step(FlashShatterState s, double dt)
    {
        if (s.Done || dt <= 0 || s.Shards.Length == 0) return false;

        s.ElapsedSec += dt;
        var alpha = AlphaAt(s.ElapsedSec, s.DurationSec);
        var allGone = true;

        foreach (var shard in s.Shards)
        {
            shard.Vy += s.GravityPxPerSec2 * dt;
            shard.Dx += shard.Vx * dt;
            shard.Dy += shard.Vy * dt;
            shard.AngleRad += shard.AngularVelRad * dt;
            shard.Alpha = alpha;
            if (!OffScreen(s, shard)) allGone = false;
        }

        if (alpha <= 0 || allGone) s.Done = true;
        return true;
    }

    public static double AlphaAt(double elapsedSec, double durationSec)
    {
        if (durationSec <= 0) return 0.0;
        var t = elapsedSec / durationSec;
        if (t <= HoldFraction) return 1.0;
        if (t >= 1.0) return 0.0;
        return 1.0 - (t - HoldFraction) / (1.0 - HoldFraction);
    }

    public static bool OffScreen(FlashShatterState s, FlashShard shard)
    {
        if (s.BoundsW <= 0 || s.BoundsH <= 0) return false;
        var left = s.RectX + shard.U0 * s.RectW + shard.Dx;
        var right = s.RectX + shard.U1 * s.RectW + shard.Dx;
        var top = s.RectY + shard.V0 * s.RectH + shard.Dy;
        var bottom = s.RectY + shard.V1 * s.RectH + shard.Dy;
        return top >= s.BoundsY + s.BoundsH
            || bottom <= s.BoundsY
            || left >= s.BoundsX + s.BoundsW
            || right <= s.BoundsX;
    }

    private static double[] CutLines(int cells, Random rng)
    {
        var cuts = new double[cells + 1];
        var step = 1.0 / cells;
        cuts[0] = 0.0;
        cuts[cells] = 1.0;
        for (var i = 1; i < cells; i++)
        {
            var jitter = (rng.NextDouble() * 2.0 - 1.0) * CutJitterFraction * step;
            cuts[i] = i * step + jitter;
        }
        return cuts;
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
