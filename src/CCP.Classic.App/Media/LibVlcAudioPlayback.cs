namespace CCP.Classic.App.Media;

public interface IAudioPlayback : IDisposable
{
    bool Play(string localPath, double gain);
    AudioPlaybackTicket? PlayTracked(string localPath, double gain) => null;
    void SetMasterGain(double gain);
    void StopAll();
}

internal interface IAudioDevice : IDisposable
{
    IAudioVoice Open(string path, double gain, Action wake);
}

internal interface IAudioVoice : IDisposable
{
    bool IsFinished { get; }
    bool WasSuccessful => false;
    double PlayedSeconds => 0;
    double SourceSeconds => 0;
    bool IsPlaying => false;
    void Start();
    void SetGain(double gain);
    void Refresh();
    void Stop();
}

public sealed class LibVlcAudioPlayback : IAudioPlayback
{
    private sealed record Request(string Path, double Gain, long Generation, AudioPlaybackTicket Ticket);
    private sealed record ActiveVoice(IAudioVoice Voice, double PerSoundGain, AudioPlaybackTicket Ticket, string Path);
    private readonly object gate = new();
    private readonly Queue<Request> pending = new();
    private readonly List<ActiveVoice> active = new();
    private readonly HashSet<AudioPlaybackTicket> tickets = [];
    private readonly AutoResetEvent signal = new(false);
    private readonly Func<IAudioDevice> factory;
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long generation;
    private bool stopRequested, disposed, gainChanged;
    private double masterGain = 1;
    private DateTime retryAfter;
    private string? lastError;
    private IReadOnlyList<AudioPlaybackSample> audioSamples = Array.Empty<AudioPlaybackSample>();
    internal Task Completion => completion.Task;
    public string? LastError => Volatile.Read(ref lastError);
    public IReadOnlyList<AudioPlaybackSample> GetAudioSamples() => Volatile.Read(ref audioSamples);

    public LibVlcAudioPlayback() : this(() => new NativeAudioDevice()) { }

    internal LibVlcAudioPlayback(Func<IAudioDevice> factory)
    {
        this.factory = factory;
        new Thread(Run) { IsBackground = true, Name = "CCP Classic audio" }.Start();
    }

    public bool Play(string localPath, double gain) => PlayTracked(localPath, gain) is not null;

    public AudioPlaybackTicket? PlayTracked(string localPath, double gain)
    {
        if (!double.IsFinite(gain) || gain <= 0 || LocalAudioPath(localPath) is not { } path) return null;
        lock (gate)
        {
            if (disposed || DateTime.UtcNow < retryAfter || pending.Count >= 32) return null;
            var ticket = new AudioPlaybackTicket(); tickets.Add(ticket);
            pending.Enqueue(new(path, Math.Min(gain, 1), generation, ticket));
            signal.Set();
            return ticket;
        }
    }

    public void SetMasterGain(double gain)
    {
        var next = double.IsFinite(gain) ? Math.Clamp(gain, 0, 1) : 0;
        lock (gate)
        {
            if (disposed || next == masterGain) return;
            masterGain = next;
            gainChanged = true;
            signal.Set();
        }
    }

    internal static string? LocalAudioPath(string? path) => LocalAudioInput.LocalPath(path);

    public void StopAll()
    {
        lock (gate)
        {
            if (disposed) return;
            generation++;
            foreach (var ticket in tickets) ticket.Complete(false);
            tickets.Clear();
            pending.Clear();
            stopRequested = true;
            Volatile.Write(ref audioSamples, Array.Empty<AudioPlaybackSample>());
            signal.Set();
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            generation++;
            foreach (var ticket in tickets) ticket.Complete(false);
            tickets.Clear();
            pending.Clear();
            Volatile.Write(ref audioSamples, Array.Empty<AudioPlaybackSample>());
            signal.Set();
        }
    }

    private void Wake() { lock (gate) { if (!disposed) signal.Set(); } }
    private bool Current(Request request) { lock (gate) return !disposed && request.Generation == generation; }
    private double EffectiveGain(Request request) { lock (gate) return request.Gain * masterGain; }

    private void Run()
    {
        IAudioDevice? device = null;
        try
        {
            while (true)
            {
                signal.WaitOne(25);
                bool stop, quit, updateGain;
                double master;
                lock (gate)
                {
                    stop = stopRequested; stopRequested = false; quit = disposed;
                    updateGain = gainChanged; gainChanged = false; master = masterGain;
                }
                if (stop || quit) StopVoices();
                if (quit) break;
                for (var index = active.Count - 1; index >= 0; index--)
                {
                    var playing = active[index];
                    var voice = playing.Voice;
                    try
                    {
                        if (updateGain) voice.SetGain(playing.PerSoundGain * master);
                        voice.Refresh();
                        playing.Ticket.UpdatePlayed(voice.PlayedSeconds);
                    }
                    catch (Exception error) { Volatile.Write(ref lastError, error.Message); Finish(playing.Ticket, false); Release(voice); active.RemoveAt(index); continue; }
                    if (voice.IsFinished) { Finish(playing.Ticket, voice.WasSuccessful, voice.PlayedSeconds); Release(voice); active.RemoveAt(index); }
                }
                lock (gate)
                    Volatile.Write(ref audioSamples, disposed || stopRequested ? Array.Empty<AudioPlaybackSample>()
                        : Array.AsReadOnly(active.Where(v => !v.Ticket.IsFinished).Select(v => new AudioPlaybackSample(v.Path,
                            v.Voice.SourceSeconds, v.PerSoundGain * master, v.Voice.IsPlaying, TimeProvider.System.GetTimestamp())).ToArray()));
                Request? request = null;
                // A stop received during Refresh belongs before any new-generation playback.
                lock (gate) if (!stopRequested && active.Count < 10 && pending.Count > 0) request = pending.Dequeue();
                if (request is null || !Current(request)) continue;
                try
                {
                    device ??= factory();
                }
                catch (Exception error)
                {
                    lock (gate)
                    {
                        retryAfter = DateTime.UtcNow.AddSeconds(30);
                        foreach (var queued in pending) { queued.Ticket.Complete(false); tickets.Remove(queued.Ticket); }
                        pending.Clear();
                        Volatile.Write(ref lastError, error.Message);
                    }
                    Finish(request.Ticket, false);
                    continue;
                }
                // Native loading can finish after panic. Never open the cancelled request.
                if (!Current(request)) continue;
                IAudioVoice? opened = null;
                ActiveVoice? playingVoice = null;
                try
                {
                    opened = device.Open(request.Path, EffectiveGain(request), Wake);
                    if (!Current(request)) { Release(opened); continue; }
                    // The master may have changed while the native output was opening.
                    opened.SetGain(EffectiveGain(request));
                    if (!Current(request)) { Release(opened); continue; }
                    playingVoice = new(opened, request.Gain, request.Ticket, request.Path);
                    active.Add(playingVoice);
                    opened.Start();
                    if (!Current(request)) { Release(opened); active.Remove(playingVoice); }
                    else if (!opened.IsFinished) Volatile.Write(ref lastError, null);
                }
                catch (Exception error)
                {
                    lastError = error.Message;
                    Finish(request.Ticket, false);
                    if (opened is not null) { Release(opened); if (playingVoice is not null) active.Remove(playingVoice); }
                }
            }
        }
        finally
        {
            StopVoices();
            try { device?.Dispose(); } catch (Exception error) { lastError = error.Message; }
            lock (gate)
            {
                foreach (var ticket in tickets) ticket.Complete(false);
                tickets.Clear(); pending.Clear(); signal.Dispose();
            }
            completion.TrySetResult();
        }
    }

    private void StopVoices()
    {
        foreach (var playing in active) { Finish(playing.Ticket, false); Release(playing.Voice); }
        active.Clear();
    }

    private void Finish(AudioPlaybackTicket ticket, bool successful, double seconds = 0)
    {
        lock (gate) { ticket.Complete(successful, seconds); tickets.Remove(ticket); }
    }

    private void Release(IAudioVoice voice)
    {
        try { voice.Stop(); } catch (Exception error) { lastError = error.Message; }
        try { voice.Dispose(); } catch (Exception error) { lastError = error.Message; }
    }
}
