namespace CCP.Classic.App.Media;

// Multiple monitor views share one decoder, immutable frames and actual playback allowance.
internal sealed class SharedVideoPlayback(ILocalVideoPlayback playback)
{
    private readonly ILocalVideoPlayback source = playback;
    private readonly List<View> views = [];
    private VideoFrame? latest;
    private bool started, closed;
    public ILocalVideoPlayback CreateView()
    { var view = new View(this); views.Add(view); return view; }
    private void Controls()
    {
        if (closed) return;
        source.SetPaused(views.All(v => v.Paused)); source.SetGain(views.Count > 0 ? views.Max(v => v.Gain) : 0);
    }
    private sealed class View(SharedVideoPlayback owner) : ILocalVideoPlayback
    {
        public bool Paused = true;
        public double Gain;
        private bool disposed;
        private long copied = -1;
        public bool IsFinished => disposed || owner.source.IsFinished;
        public string? LastError => owner.source.LastError;
        public double PlayedSeconds => owner.source.PlayedSeconds;
        public double DurationSeconds => owner.source.DurationSeconds;
        public AudioPlaybackSample? GetAudioSample() => disposed || Paused || Gain <= 0 ? null : owner.source.GetAudioSample();
        public AudioPlaybackSample? GetTimelineSample() => disposed || Paused ? null : owner.source.GetTimelineSample();
        public void Start() { if (!owner.started && !owner.closed) { owner.started = true; owner.source.Start(); } }
        public void SetGain(double gain) { if (!disposed) { Gain = gain; owner.Controls(); } }
        public void SetPaused(bool paused) { if (!disposed) { Paused = paused; owner.Controls(); } }
        public bool TryCopyFrame(out VideoFrame? frame)
        {
            frame = null; if (disposed || owner.closed) return false;
            if (owner.source.TryCopyFrame(out var next) && next is not null) owner.latest = next;
            if (owner.latest is null || copied == owner.latest.Sequence) return false;
            frame = owner.latest; copied = frame.Sequence; return true;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; owner.views.Remove(this);
            if (owner.views.Count == 0) { owner.closed = true; owner.latest = null; owner.source.Dispose(); }
            else owner.Controls();
        }
    }
}
