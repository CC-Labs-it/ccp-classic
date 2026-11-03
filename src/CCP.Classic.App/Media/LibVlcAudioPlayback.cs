namespace CCP.Classic.App.Media;

public interface IAudioPlayback : IDisposable
{
    bool Play(string localPath, double gain);
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
    void Refresh();
    void Stop();
}

public sealed class LibVlcAudioPlayback : IAudioPlayback
{
    private sealed record Request(string Path, double Gain, long Generation);
    private readonly object gate = new();
    private readonly Queue<Request> pending = new();
    private readonly List<IAudioVoice> active = new();
    private readonly AutoResetEvent signal = new(false);
    private readonly Func<IAudioDevice> factory;
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long generation;
    private bool stopRequested, disposed;
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

    private void Run()
    {
        IAudioDevice? device = null;
        try
        {
            while (true)
            {
                signal.WaitOne(25);
                bool stop, quit;
                lock (gate) { stop = stopRequested; stopRequested = false; quit = disposed; }
                if (stop || quit) StopVoices();
                if (quit) break;
                for (var index = active.Count - 1; index >= 0; index--)
                {
                    var voice = active[index];
                    try { voice.Refresh(); }
                    catch (Exception error) { Volatile.Write(ref lastError, error.Message); Release(voice); active.RemoveAt(index); continue; }
                    if (voice.IsFinished) { Release(voice); active.RemoveAt(index); }
                }
                Request? request = null;
                lock (gate) if (active.Count < 10 && pending.Count > 0) request = pending.Dequeue();
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
                try
                {
                    opened = device.Open(request.Path, request.Gain, Wake);
                    if (!Current(request)) { Release(opened); continue; }
                    active.Add(opened);
                    opened.Start();
                    if (!Current(request)) { Release(opened); active.Remove(opened); }
                    else if (!opened.IsFinished) Volatile.Write(ref lastError, null);
                }
                catch (Exception error)
                {
                    lastError = error.Message;
                    if (opened is not null) { Release(opened); active.Remove(opened); }
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
        foreach (var voice in active) Release(voice);
        active.Clear();
    }

    private void Release(IAudioVoice voice)
    {
        try { voice.Stop(); } catch (Exception error) { lastError = error.Message; }
        try { voice.Dispose(); } catch (Exception error) { lastError = error.Message; }
    }
}
