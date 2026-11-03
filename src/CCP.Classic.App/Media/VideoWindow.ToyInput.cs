namespace CCP.Classic.App.Media;

internal sealed partial class VideoWindow
{
    private double toyInputResumedAt = double.NegativeInfinity;
    private bool toyInputResumePending;
    internal bool TryAcceptToyAttention(double observedElapsed)
    {
        if (!cue.Attention || cue.Floating || !IsVisible || IsSuppressed || completed || ending is not null
            || firstFrame is not { } start || playback.GetTimelineSample()?.IsPlaying != true || !double.IsFinite(observedElapsed)
            || toyInputResumePending || observedElapsed < toyInputResumedAt) return false;
        observedElapsed -= excludedTime;
        var target = checks.Targets.OrderBy(item => item.Spawned).FirstOrDefault(item =>
            start + item.Spawned <= observedElapsed && observedElapsed < start + item.Expires);
        return target is not null && HitTarget(target.Id);
    }
}
