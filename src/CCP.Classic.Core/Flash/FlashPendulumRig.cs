// Adapted from public CCP fe40ea7587ac0b09e47d0724e59d7178db3336da, MIT.
using System;
using System.Collections.Generic;

namespace CCP.Classic.Core.Flash;

public readonly record struct PendulumNeighbour(double PivotX, double Phase);

public static class FlashPendulumRig
{
    private const int Candidates = 65;

    public static double PivotSpan(double bw, double mw, double mh, double rope, double ampRad)
    {
        var sin = Math.Abs(Math.Sin(ampRad));
        var cos = Math.Abs(Math.Cos(ampRad));
        var reach = (mw * cos + mh * sin) / 2.0 + Math.Abs(rope) * sin;
        return bw / 2.0 - reach;
    }

    public static double ChoosePivot(double centre, double span,
        IReadOnlyList<PendulumNeighbour>? taken, Random rng)
    {
        if (span <= 0 || taken == null || taken.Count == 0) return centre;

        var best = centre;
        var bestClearance = double.NegativeInfinity;
        for (int i = 0; i < Candidates; i++)
        {
            var candidate = centre - span + 2.0 * span * i / (Candidates - 1.0);
            var clearance = double.PositiveInfinity;
            foreach (var n in taken)
                clearance = Math.Min(clearance, Math.Abs(candidate - n.PivotX));

            if (clearance > bestClearance + 1e-9)
            {
                bestClearance = clearance;
                best = candidate;
            }
            else if (clearance >= bestClearance - 1e-9 && rng.Next(2) == 0)
            {
                best = candidate;
            }
        }
        return best;
    }

    public const double PhaseJitterRad = 0.35;

    public static double ChoosePhase(double pivotX, double mediaW,
        IReadOnlyList<PendulumNeighbour>? taken, Random rng)
    {
        if (taken == null || taken.Count == 0) return rng.NextDouble() * 2.0 * Math.PI;

        var nearest = taken[0];
        var nearestGap = Math.Abs(pivotX - nearest.PivotX);
        for (int i = 1; i < taken.Count; i++)
        {
            var gap = Math.Abs(pivotX - taken[i].PivotX);
            if (gap < nearestGap) { nearestGap = gap; nearest = taken[i]; }
        }

        if (nearestGap < Math.Abs(mediaW)) return Wrap(nearest.Phase + Math.PI);
        return Wrap(nearest.Phase + (rng.NextDouble() * 2.0 - 1.0) * PhaseJitterRad);
    }

    public static double EffectivePhase(FlashMotionState s) => Wrap(s.Omega * s.Elapsed + s.Phase);

    public static bool SameMonitor(double ax, double ay, double aw, double ah,
                                   double bx, double by, double bw, double bh, double slack = 1.0)
        => Math.Abs(ax - bx) <= slack && Math.Abs(ay - by) <= slack
           && Math.Abs(aw - bw) <= slack && Math.Abs(ah - bh) <= slack;

    public static double Wrap(double phase)
    {
        var twoPi = 2.0 * Math.PI;
        phase %= twoPi;
        return phase < 0 ? phase + twoPi : phase;
    }
}
