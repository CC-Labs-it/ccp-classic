using CCP.Classic.Core;

namespace CCP.Classic.App.Effects;

internal static class PinkFilterRouting
{
    public static EffectFrame ForMonitor(EffectFrame frame, PinkFilterOptions options, int monitor, bool otherEffects = true)
    {
        var opacity = options.Monitor < 0 || options.Monitor == monitor ? frame.PinkOpacity : 0;
        return otherEffects ? frame with { PinkOpacity = opacity } : new EffectFrame
        {
            PinkOpacity = opacity, PinkColour = frame.PinkColour, PinkUnderEffects = frame.PinkUnderEffects,
            GentleVisuals = frame.GentleVisuals, ReducedMotion = frame.ReducedMotion, PerformanceMode = frame.PerformanceMode
        };
    }
}
