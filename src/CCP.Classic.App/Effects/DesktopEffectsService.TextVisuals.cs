using Avalonia.Controls;
using CCP.Classic.App.Media;
using CCP.Classic.Core;

namespace CCP.Classic.App.Effects;

public sealed partial class DesktopEffectsService
{
    private long resolvedSubliminalId = -1;
    private StarterPhrase? resolvedSubliminal;
    private bool resolvedAudioReady;

    private StarterPhrase? ResolveSubliminal(EffectsSnapshot current)
    {
        var ready = audioLibrary?.IsCompletedSuccessfully == true;
        if (current.SubliminalId == resolvedSubliminalId && ready == resolvedAudioReady) return resolvedSubliminal;
        resolvedSubliminalId = current.SubliminalId; resolvedAudioReady = ready;
        if (current.TextCue is not { } phrase)
            return resolvedSubliminal = current.Phrase >= 0 && current.Phrase < starter.Phrases.Count ? starter.Phrases[current.Phrase] : null;
        return resolvedSubliminal = RecordedPhraseResolver.Resolve(phrase, starter.Phrases,
            ready ? audioLibrary!.Result.Audio.Where(settings.Library.Includes) : []);
    }

    private EffectFrame FrameForWindow(EffectFrame frame, Window window)
    {
        var all = owner.Screens.All;
        var screen = owner.Screens.ScreenFromWindow(window);
        var index = -1;
        for (var i = 0; i < all.Count; i++) if (all[i].Bounds == screen?.Bounds) { index = i; break; }
        var baseScreen = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
        return PinkFilterRouting.ForMonitor(frame, settings.PinkFilter, index,
            settings.FillAllMonitors || screen is null || baseScreen is null || screen.Bounds == baseScreen.Bounds);
    }
}
