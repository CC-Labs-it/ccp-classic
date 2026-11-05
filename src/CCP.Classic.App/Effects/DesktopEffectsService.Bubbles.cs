using CCP.Classic.Core;

namespace CCP.Classic.App.Effects;

public sealed partial class DesktopEffectsService
{
    private void TriggerBubbleEffect(InteractiveEffectEvent value)
    {
        if (!running || disposed || !session.IsRunning || value.Kind != InteractiveEffectEventKind.BubblePopped
            || value.Bubble is not { } bubble) return;
        _ = bubble.Effect switch
        {
            BubbleEffectKind.Flash => timeline.TriggerFlash(bubble.Image),
            BubbleEffectKind.Subliminal => timeline.TriggerWhisper(),
            BubbleEffectKind.Pink => timeline.TriggerPinkPulse(),
            BubbleEffectKind.Spiral => timeline.TriggerSpiralPulse(),
            BubbleEffectKind.BrainDrain => TriggerBrainDrainPulse(),
            BubbleEffectKind.Video => videos.TryTriggerBubble(),
            _ => false
        };
    }
}
