using Avalonia;
using Avalonia.Media;
using CCP.Classic.App.Media;
using CCP.Classic.Core;

namespace CCP.Classic.App.Effects;

internal static class EffectsFrameBuilder
{
    public static EffectFrame Build(EffectsSnapshot snapshot, ClassicSettings settings,
        IReadOnlyList<IImage> images, IReadOnlyList<StarterPhrase> phrases, IImage? remix = null) => new()
    {
        Flashes = snapshot.Flashes.Where(f => !f.IsRemix || remix is not null).Select(f => new FlashFrame(f.IsRemix ? remix! : images[f.Image],
            new Rect(f.X, f.Y, f.Width, f.Height), f.Opacity, f.Exit) { Rotation = f.Rotation, Shatter = f.Shatter,
                InputBounds = new(f.HitArea.X, f.HitArea.Y, f.HitArea.Width, f.HitArea.Height),
                RoundedCorners = settings.Flash.RoundedCorners, Glow = settings.Flash.Glow && !settings.PerformanceMode }).ToArray(),
        SubliminalText = snapshot.TextCue?.Text ?? (snapshot.Phrase >= 0 && snapshot.Phrase < phrases.Count ? phrases[snapshot.Phrase].Text : null),
        SubliminalFont = settings.Subliminal.FontFamily,
        SubliminalOpacity = snapshot.TextOpacity,
        SubliminalColour = (int)settings.Effects["sub"].Values["colour"] switch
            { 1 => Colors.White, 2 => Color.Parse("#C9C9DE"), _ => Color.Parse("#FF69B4") },
        SpiralPhase = snapshot.SpiralPhase, SpiralKind = snapshot.SpiralKind,
        SpiralOpacity = snapshot.SpiralOpacity, PinkOpacity = snapshot.PinkOpacity,
        PinkColour = Color.TryParse(settings.PinkFilter.Colour, out var colour) ? colour : Color.Parse("#FF69B4"),
        PinkUnderEffects = settings.PinkFilter.UnderEffects,
        ReducedMotion = settings.ReducedMotion, GentleVisuals = settings.GentleVisuals,
        PerformanceMode = settings.PerformanceMode, BubbleImages = images
    };
}
