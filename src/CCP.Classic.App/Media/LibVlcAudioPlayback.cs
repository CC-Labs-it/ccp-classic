namespace CCP.Classic.App.Media;

public interface IAudioPlayback : IDisposable
{
    bool Play(string localPath, double gain);
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
    void Start();
    void SetGain(double gain);
    void Refresh();
    void Stop();
}

public sealed class LibVlcAudioPlayback : IAudioPlayback
{
    private sealed record Request(string Path, double Gain, long Generation);
    private sealed record ActiveVoice(IAudioVoice Voice, double PerSoundGain);
    private readonly object gate = new();
    private readonly Queue<Request> pending = new();
    private readonly List<ActiveVoice> active = new();
    private readonly AutoResetEvent signal = new(false);
    private readonly Func<IAudioDevice> factory;
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long generation;
    private bool stopRequested, disposed, gainChanged;
    private double masterGain = 1;
    private DateTime retryAfter;
    private string? lastError;
    internal Task Completion => completion.Task;
    public string? LastError => Volatile.Read(ref lastError);

    public LibVlcAudioPlayback() : this(() => new NativeAudioDevice()) { }

    internal LibVlcAudioPlayback(Func<IAudioDevice> factory)
    {
        this.factory = factory;
        new Thread(Run) { IsBackground = true, Name = "CCP Classic audio" }.Start();
    }

    public bool Play(string localPath, double gain)
    {
        if (!double.IsFinite(gain) || gain <= 0 || LocalAudioPath(localPath) is not { } path) return false;
        lock (gate)
        {
            if (disposed || DateTime.UtcNow < retryAfter || pending.Count >= 32) return false;
            pending.Enqueue(new(path, Math.Min(gain, 1), generation));
            signal.Set();
            return true;
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

    internal static string? LocalAudioPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith(@"\\", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal)) return null;
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && (!uri.IsFile || uri.IsUnc)) return null;
        try
        {
            var full = Path.GetFullPath(path);
            var extension = Path.GetExtension(full).ToLowerInvariant();
            return extension is ".mp3" or ".wav" or ".ogg" or ".flac" && File.Exists(full) ? full : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException) { return null; }
    }

    public void StopAll()
    {
        lock (gate)
        {
            if (disposed) return;
            generation++;
            pending.Clear();
            stopRequested = true;
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
            pending.Clear();
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
                    }
                    catch (Exception error) { Volatile.Write(ref lastError, error.Message); Release(voice); active.RemoveAt(index); continue; }
                    if (voice.IsFinished) { Release(voice); active.RemoveAt(index); }
                }
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
                        pending.Clear();
                        Volatile.Write(ref lastError, error.Message);
                    }
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
                    playingVoice = new(opened, request.Gain);
                    active.Add(playingVoice);
                    opened.Start();
                    if (!Current(request)) { Release(opened); active.Remove(playingVoice); }
                    else if (!opened.IsFinished) Volatile.Write(ref lastError, null);
                }
                catch (Exception error)
                {
                    lastError = error.Message;
                    if (opened is not null) { Release(opened); if (playingVoice is not null) active.Remove(playingVoice); }
                }
            }
        }
        finally
        {
            StopVoices();
            try { device?.Dispose(); } catch (Exception error) { lastError = error.Message; }
            lock (gate) signal.Dispose();
            completion.TrySetResult();
        }
    }

    private void StopVoices()
    {
        foreach (var playing in active) Release(playing.Voice);
        active.Clear();
    }

    private void Release(IAudioVoice voice)
    {
        try { voice.Stop(); } catch (Exception error) { lastError = error.Message; }
        try { voice.Dispose(); } catch (Exception error) { lastError = error.Message; }
    }
}
