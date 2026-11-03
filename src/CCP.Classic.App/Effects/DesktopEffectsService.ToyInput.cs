namespace CCP.Classic.App.Effects;

public sealed partial class DesktopEffectsService
{
    public bool TryAcceptToyAttention(long observedAt)
    {
        if (disposed || !running || !session.IsRunning || !settings.Devices.ToyInputEnabled || !settings.Devices.AttentionCheckToyButton
            || observedAt < started || observedAt > clock.GetTimestamp()) return false;
        return videos.TryAcceptToyAttention(clock.GetElapsedTime(started, observedAt).TotalSeconds);
    }
}
