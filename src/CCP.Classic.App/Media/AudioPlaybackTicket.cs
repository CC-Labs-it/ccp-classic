namespace CCP.Classic.App.Media;

// A ticket owns one accepted request. Completion is published once, after its final played time.
public sealed class AudioPlaybackTicket
{
    private readonly object gate = new();
    private int outcome;
    private long playedMilliseconds;
    public bool IsFinished => Volatile.Read(ref outcome) != 0;
    public bool WasSuccessful => Volatile.Read(ref outcome) == 1;
    public double PlayedSeconds => Interlocked.Read(ref playedMilliseconds) / 1000d;

    internal void UpdatePlayed(double seconds)
    {
        lock (gate)
        {
            if (outcome != 0 || !double.IsFinite(seconds)) return;
            Interlocked.Exchange(ref playedMilliseconds, Math.Max(playedMilliseconds, (long)(Math.Clamp(seconds, 0, 86400) * 1000)));
        }
    }
    internal void Complete(bool successful, double seconds = 0)
    {
        lock (gate)
        {
            if (outcome != 0) return;
            UpdatePlayed(seconds);
            Volatile.Write(ref outcome, successful ? 1 : 2);
        }
    }
}
