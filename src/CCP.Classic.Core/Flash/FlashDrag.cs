// Adapted from public CCP fe40ea7587ac0b09e47d0724e59d7178db3336da, MIT.
using System;

namespace CCP.Classic.Core.Flash;

public sealed class FlashDragState
{
    public readonly object Sync = new();

    public bool Held = true;

    public bool Flinging;

    public double GrabDx, GrabDy;

    public double StartX, StartY, StartMs;

    public double LastX, LastY, LastMs;
    public double PrevX, PrevY, PrevMs;
    public bool HasPrev;

    public double MaxTravelPx;
}

public enum FlashDragOutcome
{
    Tap,
    Place,
    Fling,
}

public static class FlashDrag
{
    public const double TapMovePx = 6.0;
    public const double TapMs = 250.0;
    public const double StaleMs = 120.0;
    public const double FlingThresholdPxPerMs = 0.5;
    public const double BounceKeep = 0.75;
    public const double FrictionKeepPerSec = 0.30;
    public const double StopSpeedPxPerSec = 40.0;

    public static FlashDragState Begin(FlashMotionState s, double pointerX, double pointerY, double nowMs)
    {
        s.MediaW = s.W;
        s.MediaH = s.H;
        s.Style = FlashMotionStyle.DriftBounce;
        s.Vx = 0;
        s.Vy = 0;
        var d = new FlashDragState
        {
            Held = true,
            GrabDx = s.X - pointerX,
            GrabDy = s.Y - pointerY,
            StartX = pointerX, StartY = pointerY, StartMs = nowMs,
            LastX = pointerX, LastY = pointerY, LastMs = nowMs,
        };
        s.Drag = d;
        return d;
    }

    public static void Sample(FlashDragState d, double x, double y, double nowMs)
    {
        lock (d.Sync)
        {
            if (!d.Held) return;
            if (nowMs > d.LastMs)
            {
                d.PrevX = d.LastX; d.PrevY = d.LastY; d.PrevMs = d.LastMs;
                d.HasPrev = true;
            }
            d.LastX = x; d.LastY = y; d.LastMs = nowMs;
            var travel = Length(x - d.StartX, y - d.StartY);
            if (travel > d.MaxTravelPx) d.MaxTravelPx = travel;
        }
    }

    public static bool IsTap(double travelPx, double elapsedMs)
        => travelPx < TapMovePx && elapsedMs < TapMs;

    public static (double Vx, double Vy) SampleVelocity(FlashDragState d, double nowMs)
    {
        lock (d.Sync)
        {
            if (!d.HasPrev) return (0, 0);
            if (nowMs - d.LastMs > StaleMs) return (0, 0);
            var dt = d.LastMs - d.PrevMs;
            if (dt <= 0) return (0, 0);
            return ((d.LastX - d.PrevX) / dt, (d.LastY - d.PrevY) / dt);
        }
    }

    public static bool ShouldFling(double vxPerMs, double vyPerMs)
        => Length(vxPerMs, vyPerMs) >= FlingThresholdPxPerMs;

    public static FlashDragOutcome Release(FlashMotionState s, double nowMs, MotionLevel level)
    {
        var d = s.Drag;
        if (d == null) return FlashDragOutcome.Place;

        double travel, elapsed;
        lock (d.Sync)
        {
            d.Held = false;
            travel = d.MaxTravelPx;
            elapsed = nowMs - d.StartMs;
        }

        if (IsTap(travel, elapsed))
        {
            Settle(s);
            return FlashDragOutcome.Tap;
        }

        var (vx, vy) = SampleVelocity(d, nowMs);
        var scale = FlashMotion.LevelScale(level);
        if (scale <= 0 || !ShouldFling(vx, vy))
        {
            Settle(s);
            return FlashDragOutcome.Place;
        }

        s.Vx = vx * 1000.0 * scale;   // px/ms to px/s
        s.Vy = vy * 1000.0 * scale;
        d.Flinging = true;
        return FlashDragOutcome.Fling;
    }

    public static bool Step(FlashMotionState s, double dt)
    {
        var d = s.Drag;
        if (d == null || dt <= 0) return false;

        if (d.Held)
        {
            double px, py;
            lock (d.Sync) { px = d.LastX; py = d.LastY; }
            var hx = px + d.GrabDx;
            var hy = py + d.GrabDy;
            if (hx == s.X && hy == s.Y) return false;
            s.X = hx;
            s.Y = hy;
            return true;
        }

        var (x, vx, bouncedX) = FlashMotion.Reflect(s.X + s.Vx * dt, s.W, s.BoundsX, s.BoundsW, s.Vx);
        var (y, vy, bouncedY) = FlashMotion.Reflect(s.Y + s.Vy * dt, s.H, s.BoundsY, s.BoundsH, s.Vy);
        if (bouncedX) vx *= BounceKeep;
        if (bouncedY) vy *= BounceKeep;

        var keep = Math.Pow(FrictionKeepPerSec, dt);
        vx *= keep;
        vy *= keep;

        var moved = x != s.X || y != s.Y;
        s.X = x; s.Y = y; s.Vx = vx; s.Vy = vy;

        if (Length(vx, vy) < StopSpeedPxPerSec) Settle(s);
        return moved;
    }

    private static void Settle(FlashMotionState s)
    {
        s.Vx = 0;
        s.Vy = 0;
        s.Drag = null;
    }

    private static double Length(double x, double y) => Math.Sqrt(x * x + y * y);
}
