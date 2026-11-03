using CCP.Classic.Core;

namespace CCP.Classic.App.Media;

public sealed class LayeredAudioService : IDisposable
{
    private sealed class Request(LayeredAudioOptions options, double gain, bool forced)
    {
        public readonly LayeredAudioOptions Structure = options;
        public LayeredAudioOptions Live = options;
        public double Gain = gain, Duck;
        public readonly bool Forced = forced;
        public readonly CancellationTokenSource Stop = new();
        public readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LayeredMixStream? Stream;
        public int RequestedStop;
    }
    private readonly object gate = new();
    private readonly AutoResetEvent signal = new(false);
    private readonly Func<ILayeredAudioBackend> backend;
    private readonly Func<LayeredPcmCache> cache;
    private readonly TimeProvider clock;
    private Request? pending, current;
    private Thread? worker;
    private bool disposed;
    private string? status;
    private int playing;
    private IReadOnlyList<AudioPlaybackSample> samples = [];
    public event Action? StateChanged;
    public string? StatusKey => Volatile.Read(ref status);
    public bool IsPlaying => Volatile.Read(ref playing) != 0;
    public bool IsActive { get { lock (gate) return current is not null || pending is not null; } }
    public Task Completion { get { lock (gate) return (pending ?? current)?.Ended.Task ?? Task.CompletedTask; } }
    public IReadOnlyList<AudioPlaybackSample> GetAudioSamples() => Volatile.Read(ref samples);
    public LayeredAudioService() : this(() => new LayeredAudioNative(), () => new LayeredPcmCache()) { }
    internal LayeredAudioService(Func<ILayeredAudioBackend> backend, Func<LayeredPcmCache> cache, TimeProvider? clock = null)
    { this.backend = backend; this.cache = cache; this.clock = clock ?? TimeProvider.System; }

    public Task StartAsync(LayeredAudioOptions options, double appMaster, bool ignoreMasterToggle = false)
    {
        var copy = options.Copy();
        if (!copy.Enabled && !ignoreMasterToggle) return StopAsync();
        lock (gate)
        {
            if (disposed) return Task.CompletedTask;
            CancelPending(); Cancel(current);
            Volatile.Write(ref samples, Array.Empty<AudioPlaybackSample>()); Volatile.Write(ref playing, 0);
            var request = new Request(copy, Finite(appMaster), ignoreMasterToggle); pending = request;
            if (worker is null) { worker = new(Run) { IsBackground = true, Name = "CCP Classic layers" }; worker.Start(); }
            Wake(); return request.Ready.Task;
        }
    }
    public Task ApplyAsync(LayeredAudioOptions options, double appMaster, bool ignoreMasterToggle = false)
        => StartAsync(options, appMaster, ignoreMasterToggle);
    public void UpdateGains(LayeredAudioOptions options, double appMaster, double duckAmount = 0)
    {
        var copy = options.Copy();
        lock (gate)
        {
            foreach (var request in new[] { current, pending })
                if (request is not null) { request.Live = copy; request.Gain = Finite(appMaster); request.Duck = Finite(duckAmount); }
        }
        Wake();
    }
    public Task StopAsync()
    {
        Task cleanup;
        lock (gate)
        {
            CancelPending(); Cancel(current); cleanup = current?.Ended.Task ?? Task.CompletedTask;
            Volatile.Write(ref samples, Array.Empty<AudioPlaybackSample>()); Volatile.Write(ref playing, 0);
            Wake();
        }
        Status(null); return cleanup;
    }
    private static double Finite(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
    private static void Cancel(Request? request)
    { if (request is null) return; Volatile.Write(ref request.RequestedStop, 1); request.Stream?.SetMaster(0); request.Stop.Cancel(); }
    private void CancelPending()
    {
        if (pending is not { } old) return;
        Cancel(old); old.Ready.TrySetResult(); old.Ended.TrySetResult(); old.Stop.Dispose(); pending = null;
    }
    private void Wake() { try { signal.Set(); } catch (ObjectDisposedException) { } }
    private void Status(string? key, Request? request = null)
    {
        Action? handlers;
        lock (gate)
        {
            if (request is not null && (!ReferenceEquals(current, request) || Volatile.Read(ref request.RequestedStop) != 0)) return;
            if (request is null && key is null && (pending is not null || current?.Stop.IsCancellationRequested == false)) return;
            if (Volatile.Read(ref status) == key) return; Volatile.Write(ref status, key); handlers = StateChanged;
        }
        if (handlers is null) return;
        foreach (Action handler in handlers.GetInvocationList())
            try { handler(); } catch (Exception error) when (error is not OutOfMemoryException) { }
    }
    private void Run()
    {
        try
        {
            while (true)
            {
                Request? request;
                lock (gate)
                {
                    request = pending; pending = null;
                    if (request is null && disposed) break;
                    current = request;
                }
                if (request is null) { signal.WaitOne(); continue; }
                try { RunGraph(request); }
                catch (Exception error) when (error is not OutOfMemoryException)
                { if (Volatile.Read(ref request.RequestedStop) == 0) Status("AudioLayersUnavailable", request); }
                finally
                {
                    lock (gate) { if (ReferenceEquals(current, request)) current = null; }
                    request.Ready.TrySetResult(); request.Ended.TrySetResult(); request.Stop.Dispose();
                }
            }
        }
        finally { signal.Dispose(); }
    }
    private void RunGraph(Request request)
    {
        var token = request.Stop.Token;
        if (token.IsCancellationRequested) return;
        var selected = request.Structure.Tracks.Where(track => track.Enabled && !string.IsNullOrWhiteSpace(track.Path)).Take(8).ToArray();
        if (selected.Length == 0) { Status("AudioLayersEmpty", request); return; }
        Status("AudioLayersBuffering", request);
        LayeredPcmCache? storage = null; ILayeredAudioBackend? native = null; ILayeredAudioOutput? output = null;
        var tracks = new List<LayeredPcmTrack>(); var decoders = new List<ILayeredAudioDecoder?>(); var paused = new bool[selected.Length];
        try
        {
            storage = cache(); native = backend();
            foreach (var selection in selected)
            {
                if (token.IsCancellationRequested) return;
                var track = storage.Create(selection.Id, selection.Path, Wake); tracks.Add(track);
                try { decoders.Add(native.OpenDecoder(track, token, Wake)); }
                catch (Exception error) when (error is not OutOfMemoryException) { track.Finish(false); decoders.Add(null); }
            }
            using var stream = new LayeredMixStream(tracks.ToArray(), token, Wake);
            lock (gate) request.Stream = stream;
            while (!token.IsCancellationRequested)
            {
                LayeredAudioOptions live; double gain, duck;
                lock (gate) { live = request.Live; gain = request.Gain; duck = request.Duck; }
                stream.SetMaster(gain * live.MasterVolume / 100 * (1 - duck));
                foreach (var track in live.Tracks) stream.SetGain(track.Id, track.Enabled ? track.Volume / 100d : 0);
                for (var index = 0; index < decoders.Count; index++)
                {
                    var decoder = decoders[index]; if (decoder is null) continue;
                    if (decoder.Finished) { decoder.Dispose(); decoders[index] = null; continue; }
                    var ahead = tracks[index].Frames - Interlocked.Read(ref tracks[index].ConsumedFrames);
                    if (ahead > 2 * LayeredPcmTrack.Rate) paused[index] = true;
                    else if (ahead < LayeredPcmTrack.Rate) paused[index] = false;
                    decoder.Poll(paused[index]);
                }
                if (tracks.All(track => track.Failed)) { Status("AudioLayersUnavailable", request); return; }
                if (output is null && tracks.All(track => track.Ready || track.Failed))
                { output = native.OpenOutput(stream, clock); }
                if (output is not null)
                {
                    output.Poll(stream.Waiting); var audible = output.IsPlaying && !stream.Waiting;
                    lock (gate)
                    {
                        if (!token.IsCancellationRequested && ReferenceEquals(current, request))
                        {
                            Volatile.Write(ref playing, audible ? 1 : 0);
                            Volatile.Write(ref samples, audible ? stream.AudioSamples(output.PositionSeconds, clock) : Array.Empty<AudioPlaybackSample>());
                        }
                    }
                    if (!token.IsCancellationRequested) Status(audible ? tracks.Any(track => track.Failed) ? "AudioLayersPartialFailure" : "AudioLayersPlaying" : "AudioLayersBuffering", request);
                    request.Ready.TrySetResult();
                }
                signal.WaitOne(20);
            }
        }
        finally
        {
            request.Stop.Cancel(); request.Stream?.SetMaster(0);
            Volatile.Write(ref samples, Array.Empty<AudioPlaybackSample>()); Volatile.Write(ref playing, 0);
            try { output?.Mute(); } catch (Exception error) when (error is not OutOfMemoryException) { }
            foreach (var resource in new IDisposable?[] { output }.Concat(decoders).Concat(new IDisposable?[] { native, storage }))
                try { resource?.Dispose(); } catch (Exception error) when (error is not OutOfMemoryException) { }
            lock (gate) request.Stream = null;
        }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return; disposed = true; CancelPending(); Cancel(current);
            Volatile.Write(ref samples, Array.Empty<AudioPlaybackSample>()); Volatile.Write(ref playing, 0);
            if (worker is null) signal.Dispose(); else Wake();
        }
        Status(null);
    }
}
