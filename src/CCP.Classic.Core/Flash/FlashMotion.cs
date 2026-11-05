// Adapted from public CCP fe40ea7587ac0b09e47d0724e59d7178db3336da, MIT.
using System;
using System.Collections.Generic;

namespace CCP.Classic.Core.Flash;

public sealed class FlashMotionState
{
    public FlashMotionStyle Style;

    public double BoundsX, BoundsY, BoundsW, BoundsH;

    public double MediaW, MediaH;

    public double X, Y, W, H;

    public double Vx, Vy;

    public double PivotX, PivotY, Rope, AmpRad, Omega, Phase, AngleRad;
    public double Elapsed;

    public FlashDragState? Drag;
}

public static class FlashMotion
{
    public const double CrossSecondsMin = 6.0, CrossSecondsMax = 8.0;
    public const double DriftAngleMinDeg = 20.0, DriftAngleMaxDeg = 70.0;
    public const double PendulumAmpMinDeg = 12.0, PendulumAmpMaxDeg = 18.0;
    public const double PendulumPeriodMinSec = 2.5, PendulumPeriodMaxSec = 3.5;

    public static double LevelScale(MotionLevel level) => level switch
    {
        MotionLevel.Full => 1.0,
        MotionLevel.Reduced => 0.5,
        _ => 0.0,
    };

    public static FlashMotionStyle Resolve(FlashMotionStyle picked, bool ownsDriftBounce, bool ownsPendulum,
        MotionLevel level, Random rng)
    {
        if (level == MotionLevel.Off) return FlashMotionStyle.Still;
        switch (picked)
        {
            case FlashMotionStyle.DriftBounce:
                return ownsDriftBounce ? FlashMotionStyle.DriftBounce : FlashMotionStyle.Still;
            case FlashMotionStyle.Pendulum:
                return ownsPendulum ? FlashMotionStyle.Pendulum : FlashMotionStyle.Still;
            case FlashMotionStyle.Mix:
                var pool = new List<FlashMotionStyle>(3) { FlashMotionStyle.Still };
                if (ownsDriftBounce) pool.Add(FlashMotionStyle.DriftBounce);
                if (ownsPendulum) pool.Add(FlashMotionStyle.Pendulum);
                return pool[rng.Next(pool.Count)];
            default:
                return FlashMotionStyle.Still;
        }
    }

    public static FlashMotionState Create(FlashMotionStyle style, double x, double y, double w, double h,
        double bx, double by, double bw, double bh, MotionLevel level, Random rng,
        IReadOnlyList<PendulumNeighbour>? neighbours = null)
    {
        var s = new FlashMotionState
        {
            Style = FlashMotionStyle.Still,
            X = x, Y = y, W = w, H = h, MediaW = w, MediaH = h,
            BoundsX = bx, BoundsY = by, BoundsW = bw, BoundsH = bh,
        };
        var scale = LevelScale(level);
        if (scale <= 0 || bw <= 0 || bh <= 0) return s;

        if (style == FlashMotionStyle.DriftBounce)
        {
            s.Style = FlashMotionStyle.DriftBounce;
            var secs = Lerp(CrossSecondsMin, CrossSecondsMax, rng.NextDouble());
            var speed = bw / secs * scale;
            var ang = Lerp(DriftAngleMinDeg, DriftAngleMaxDeg, rng.NextDouble()) * Math.PI / 180.0;
            s.Vx = speed * Math.Cos(ang) * (rng.Next(2) == 0 ? 1 : -1);
            s.Vy = speed * Math.Sin(ang) * (rng.Next(2) == 0 ? 1 : -1);
        }
        else if (style == FlashMotionStyle.Pendulum)
        {
            s.Style = FlashMotionStyle.Pendulum;
            s.PivotY = by;
            s.AmpRad = Lerp(PendulumAmpMinDeg, PendulumAmpMaxDeg, rng.NextDouble()) * Math.PI / 180.0 * scale;
            s.Omega = 2.0 * Math.PI / Lerp(PendulumPeriodMinSec, PendulumPeriodMaxSec, rng.NextDouble());
            var (ropeMin, ropeMax) = RopeRange(bw, bh, w, h, s.AmpRad);
            var wanted = (y + h / 2.0) - s.PivotY;
            s.Rope = ropeMax >= ropeMin ? Math.Clamp(wanted, ropeMin, ropeMax) : ropeMin;
            var span = FlashPendulumRig.PivotSpan(bw, w, h, s.Rope, s.AmpRad);
            s.PivotX = FlashPendulumRig.ChoosePivot(bx + bw / 2.0, span, neighbours, rng);
            s.Phase = neighbours == null || neighbours.Count == 0
                ? rng.NextDouble() * 2.0 * Math.PI
                : FlashPendulumRig.ChoosePhase(s.PivotX, w, neighbours, rng);
            s.AngleRad = s.AmpRad * Math.Sin(s.Phase);
            ApplyHang(s);
        }
        return s;
    }

    public static bool Step(FlashMotionState s, double dt)
    {
        if (dt <= 0) return false;
        if (s.Drag != null) return FlashDrag.Step(s, dt);
        switch (s.Style)
        {
            case FlashMotionStyle.DriftBounce:
            {
                var (nx, vx, _) = Reflect(s.X + s.Vx * dt, s.W, s.BoundsX, s.BoundsW, s.Vx);
                var (ny, vy, _) = Reflect(s.Y + s.Vy * dt, s.H, s.BoundsY, s.BoundsH, s.Vy);
                bool moved = nx != s.X || ny != s.Y;
                s.X = nx; s.Y = ny; s.Vx = vx; s.Vy = vy;
                return moved;
            }
            case FlashMotionStyle.Pendulum:
            {
                if (s.AmpRad <= 0) return false;
                s.Elapsed += dt;
                s.AngleRad = s.AmpRad * Math.Sin(s.Omega * s.Elapsed + s.Phase);
                ApplyHang(s);
                return true;
            }
            default:
                return false;
        }
    }

    public static (double X, double Y, double W, double H) HangingBounds(double pivotX, double pivotY,
        double rope, double angle, double mw, double mh)
    {
        var sin = Math.Sin(angle);
        var cos = Math.Cos(angle);
        var cx = pivotX - rope * sin;
        var cy = pivotY + rope * cos;
        var hw = (mw * Math.Abs(cos) + mh * Math.Abs(sin)) / 2.0;
        var hh = (mw * Math.Abs(sin) + mh * Math.Abs(cos)) / 2.0;
        return (cx - hw, cy - hh, hw * 2.0, hh * 2.0);
    }

    public static (double Min, double Max) RopeRange(double bw, double bh, double mw, double mh, double ampRad)
    {
        var sin = Math.Sin(ampRad);
        var cos = Math.Cos(ampRad);
        var hw = (mw * cos + mh * sin) / 2.0;
        var hh = (mw * sin + mh * cos) / 2.0;
        var min = cos > 1e-6 ? hh / cos : hh;
        var max = cos > 1e-6 ? (bh - hh) / cos : bh - hh;
        if (sin > 1e-6) max = Math.Min(max, (bw / 2.0 - hw) / sin);
        return (min, max);
    }

    private static void ApplyHang(FlashMotionState s)
    {
        (s.X, s.Y, s.W, s.H) = HangingBounds(s.PivotX, s.PivotY, s.Rope, s.AngleRad, s.MediaW, s.MediaH);
    }

    internal static (double Pos, double V, bool Bounced) Reflect(double pos, double size, double min, double extent, double v)
    {
        var max = min + extent - size;
        if (max <= min) return (min, 0, false);        // wider than the monitor: park it
        var bounced = false;
        if (pos < min) { pos = min + (min - pos); v = Math.Abs(v); bounced = true; }
        else if (pos > max) { pos = max - (pos - max); v = -Math.Abs(v); bounced = true; }
        return (Math.Clamp(pos, min, max), v, bounced);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
