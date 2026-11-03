using Avalonia;
using Avalonia.Media;
using CCP.Classic.App.Media;
using CCP.Classic.Core;

namespace CCP.Classic.App.Effects;

internal static class EffectsFrameBuilder
{
    public static EffectFrame Build(EffectsSnapshot snapshot, ClassicSettings settings,
        IReadOnlyList<IImage> images, IReadOnlyList<StarterPhrase> phrases) => new()
    {
        Flashes = snapshot.Flashes.Select(f => new FlashFrame(images[f.Image],
            new Rect(f.X, f.Y, f.Size, f.Size), f.Opacity, f.Exit)).ToArray(),
        SubliminalText = snapshot.Phrase >= 0 ? phrases[snapshot.Phrase].Text : null,
        SubliminalOpacity = snapshot.TextOpacity,
        SubliminalColour = (int)settings.Effects["sub"].Values["colour"] switch
            { 1 => Colors.White, 2 => Color.Parse("#C9C9DE"), _ => Color.Parse("#FF69B4") },
        SpiralPhase = snapshot.SpiralPhase, SpiralKind = snapshot.SpiralKind,
        SpiralOpacity = snapshot.SpiralOpacity, PinkOpacity = snapshot.PinkOpacity,
        ReducedMotion = settings.ReducedMotion, GentleVisuals = settings.GentleVisuals,
        PerformanceMode = settings.PerformanceMode, BubbleImages = images
    };
}
