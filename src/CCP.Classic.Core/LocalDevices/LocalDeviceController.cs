namespace CCP.Classic.Core.LocalDevices;

public sealed class LocalDeviceController : IDisposable, IAsyncDisposable
{
    private sealed class ProviderState(LocalDeviceProvider provider, LocalDeviceConnection connection)
    {
        public readonly LocalDeviceProvider Provider = provider;
        public readonly LocalDeviceConnection Connection = connection;
        public bool Faulted;
        public double LastLevel = double.NaN;
        public long LastSend, LastRefresh, LastPing;
    }
    private sealed record Pulse(LocalDeviceEvent Event, long Started, int Priority, IReadOnlyList<LocalHapticStep> Steps);
    private readonly object gate = new();
    private readonly SemaphoreSlim signal = new(0, 1), outputLane = new(1, 1);
    private readonly Dictionary<LocalDeviceProvider, ProviderState> providers = [];
    private readonly List<Pulse> pulses = [];
    private readonly Func<LocalDeviceConnection> factory;
    private readonly TimeProvider clock;
    private Task worker = Task.CompletedTask, cleanup = Task.CompletedTask;
    private bool working, disposed, stopping, enabled = true;
    private int activeStops;
    private double globalIntensity = .7, audioLevel;
    private long epoch, mutedAt, workerVersion, audioUpdatedAt;
    private bool muted, hasAudioLevel;
    public event Action? Changed;
    public IReadOnlyList<LocalDeviceInfo> Devices
    { get { lock (gate) return providers.Values.Where(p => !p.Faulted).SelectMany(p => p.Connection.Devices).ToList().AsReadOnly(); } }
    public LocalDeviceStatus Status
    { get { lock (gate) return providers.Count == 0 ? LocalDeviceStatus.Disconnected : providers.Values.Any(p => !p.Faulted && p.Connection.Status == LocalDeviceStatus.Connected)
        ? LocalDeviceStatus.Connected : providers.Values.Any(p => p.Connection.Status == LocalDeviceStatus.Connecting) ? LocalDeviceStatus.Connecting : LocalDeviceStatus.Unavailable; } }
    public LocalDeviceStatus GetStatus(LocalDeviceProvider provider)
    { lock (gate) return providers.TryGetValue(provider, out var state) ? state.Faulted ? LocalDeviceStatus.Unavailable : state.Connection.Status : LocalDeviceStatus.Disconnected; }
    public int ActivePulseCount { get { lock (gate) return pulses.Count; } }
    public Task Completion { get { lock (gate) return disposed ? cleanup : worker; } }
    public double GlobalIntensity
    {
        get { lock (gate) return globalIntensity; }
        set { lock (gate) { globalIntensity = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0; Wake(); } }
    }
    public bool Enabled
    {
        get { lock (gate) return enabled; }
        set { lock (gate) { enabled = value; if (!value) { ClearAudio(); epoch++; } Wake(); } if (!value) _ = StopAsync(); }
    }

    public LocalDeviceController(Func<LocalDeviceConnection>? connectionFactory = null, TimeProvider? clock = null)
    { factory = connectionFactory ?? (() => new LocalDeviceConnection()); this.clock = clock ?? TimeProvider.System; }

    public async Task<bool> ConnectAsync(LocalDeviceProvider provider, int port, CancellationToken token)
    {
        LocalDeviceEndpoint.Create(provider, port);
        ProviderState? old; ProviderState state;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            providers.Remove(provider, out old);
            state = new(provider, factory()); providers.Add(provider, state); epoch++;
            state.Connection.Changed += ConnectionChanged;
        }
        if (old is not null) { old.Connection.Changed -= ConnectionChanged; await old.Connection.DisposeAsync().ConfigureAwait(false); }
        try
        {
            var connected = await state.Connection.ConnectAsync(provider, port, token).ConfigureAwait(false);
            lock (gate)
            {
                if (disposed || !IsCurrent(state)) connected = false;
                else if (connected)
                {
                    state.LastRefresh = state.LastPing = clock.GetTimestamp();
                    if (!working) { working = true; var version = ++workerVersion; worker = Task.Run(() => Run(version)); }
                    Wake();
                }
            }
            if (!connected && !IsCurrentThreadSafe(state)) await state.Connection.DisposeAsync().ConfigureAwait(false);
            Changed?.Invoke(); return connected;
        }
        catch
        {
            lock (gate) { if (IsCurrent(state)) providers.Remove(provider); }
            state.Connection.Changed -= ConnectionChanged; await state.Connection.DisposeAsync().ConfigureAwait(false);
            Changed?.Invoke(); throw;
        }
    }

    public void Emit(LocalDeviceEvent kind, double intensity, TimeSpan duration, LocalHapticPattern pattern = LocalHapticPattern.Constant)
    {
        if (kind == LocalDeviceEvent.AudioSync) { SetAudioLevel(intensity); return; }
        lock (gate)
        {
            if (disposed || !enabled || stopping || !providers.Values.Any(p => !p.Faulted && p.Connection.Status == LocalDeviceStatus.Connected)
                || muted && clock.GetElapsedTime(mutedAt).TotalMilliseconds < 400) return;
            var steps = LocalHapticPatterns.Render(pattern, intensity, duration); if (steps.Count == 0) return;
            var priority = kind is LocalDeviceEvent.Achievement or LocalDeviceEvent.LevelUp ? 2 : kind is LocalDeviceEvent.Attention or LocalDeviceEvent.LockCard ? 1 : 0;
            if (pulses.Count >= 4)
            {
                var lowest = pulses.Min(p => p.Priority);
                if (priority < lowest) return;
                pulses.RemoveAt(pulses.FindIndex(p => p.Priority == lowest));
            }
            pulses.Add(new(kind, clock.GetTimestamp(), priority, steps)); Wake();
        }
    }

    public void SetAudioLevel(double value)
    {
        var next = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
        lock (gate)
        {
            if (disposed) return;
            if (next == 0) { ClearAudio(); Wake(); return; }
            if (!enabled || stopping || !providers.Values.Any(p => !p.Faulted && p.Connection.Status == LocalDeviceStatus.Connected)
                || muted && clock.GetElapsedTime(mutedAt).TotalMilliseconds < 400) return;
            audioLevel = next; audioUpdatedAt = clock.GetTimestamp(); hasAudioLevel = true; Wake();
        }
    }

    private void ClearAudio() { audioLevel = 0; audioUpdatedAt = 0; hasAudioLevel = false; }
    private double MixedLevel()
    {
        if (!enabled || stopping) return 0;
        var audio = hasAudioLevel && clock.GetElapsedTime(audioUpdatedAt).TotalMilliseconds is >= 0 and < 300 ? audioLevel : 0;
        var events = pulses.GroupBy(p => p.Priority).Select(group => group.Sum(p =>
            LocalHapticPatterns.Sample(p.Steps, clock.GetElapsedTime(p.Started).TotalMilliseconds))).DefaultIfEmpty().Max();
        return Math.Clamp(Math.Max(events, audio), 0, 1) * globalIntensity;
    }

    private async Task Run(long ownVersion)
    {
        try
        {
            while (true)
            {
                ProviderState[] states; long version;
                lock (gate)
                {
                    states = providers.Values.Where(p => !p.Faulted && p.Connection.Status == LocalDeviceStatus.Connected).ToArray();
                    if (disposed || states.Length == 0) { working = false; return; }
                    pulses.RemoveAll(p => clock.GetElapsedTime(p.Started).TotalMilliseconds >= LocalHapticPatterns.TotalMilliseconds(p.Steps));
                    version = epoch;
                }
                await outputLane.WaitAsync().ConfigureAwait(false);
                try
                {
                    foreach (var state in states)
                    {
                        lock (gate) { if (disposed || !IsCurrent(state) || state.Faulted) continue; }
                        try
                        {
                            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                            if (state.Connection.PingIntervalMilliseconds > 0 && clock.GetElapsedTime(state.LastPing).TotalMilliseconds >= state.Connection.PingIntervalMilliseconds)
                            { await state.Connection.PingAsync(timeout.Token).ConfigureAwait(false); state.LastPing = clock.GetTimestamp(); }
                            double level;
                            lock (gate)
                            {
                                if (disposed || !IsCurrent(state) || state.Faulted) continue;
                                level = version == epoch ? MixedLevel() : 0;
                            }
                            if (level != state.LastLevel || clock.GetElapsedTime(state.LastSend).TotalSeconds >= 1)
                            { await state.Connection.SetLevelAsync(level, timeout.Token).ConfigureAwait(false); state.LastLevel = level; state.LastSend = clock.GetTimestamp(); }
                            if (clock.GetElapsedTime(state.LastRefresh).TotalSeconds >= 2)
                            { await state.Connection.RefreshAsync(timeout.Token).ConfigureAwait(false); state.LastRefresh = clock.GetTimestamp(); }
                        }
                        catch (Exception)
                        {
                            lock (gate) state.Faulted = true;
                            await StopProvider(state).ConfigureAwait(false); await state.Connection.DisconnectAsync().ConfigureAwait(false);
                            Changed?.Invoke();
                        }
                    }
                }
                finally { outputLane.Release(); }
                await signal.WaitAsync(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
            }
        }
        finally { lock (gate) { if (ownVersion == workerVersion) working = false; } }
    }

    public async Task StopAsync()
    {
        ProviderState[] states;
        lock (gate)
        {
            pulses.Clear(); ClearAudio(); activeStops++; stopping = true; muted = true; mutedAt = clock.GetTimestamp(); epoch++;
            states = providers.Values.ToArray(); Wake();
        }
        await outputLane.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(states.Select(StopProvider)).ConfigureAwait(false);
            foreach (var state in states) { state.LastLevel = 0; state.LastSend = clock.GetTimestamp(); }
        }
        finally
        {
            outputLane.Release(); lock (gate) { activeStops--; stopping = activeStops > 0; Wake(); }
        }
    }

    private async Task StopProvider(ProviderState state)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1)); await state.Connection.StopAsync(timeout.Token).ConfigureAwait(false); }
            catch (Exception) { lock (gate) state.Faulted = true; }
            if (attempt < 2) await Task.Delay(150).ConfigureAwait(false);
        }
    }

    public async Task DisconnectAsync(LocalDeviceProvider? provider = null)
    {
        ProviderState[] removed;
        lock (gate)
        {
            removed = providers.Values.Where(p => provider is null || p.Provider == provider).ToArray();
            foreach (var state in removed) { providers.Remove(state.Provider); state.Connection.Changed -= ConnectionChanged; }
            if (providers.Count == 0) { pulses.Clear(); ClearAudio(); } epoch++; Wake();
        }
        await outputLane.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(removed.Select(async state =>
            { await StopProvider(state).ConfigureAwait(false); await state.Connection.DisposeAsync().ConfigureAwait(false); })).ConfigureAwait(false);
        }
        finally { outputLane.Release(); }
        Changed?.Invoke();
    }

    private bool IsCurrent(ProviderState state) => providers.TryGetValue(state.Provider, out var current) && ReferenceEquals(current, state);
    private bool IsCurrentThreadSafe(ProviderState state) { lock (gate) return IsCurrent(state); }
    private void ConnectionChanged() => Changed?.Invoke();
    private void Wake() { if (signal.CurrentCount == 0) signal.Release(); }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true; pulses.Clear(); ClearAudio(); epoch++; Wake(); cleanup = Cleanup();
        }
    }
    private async Task Cleanup()
    {
        await DisconnectAsync().ConfigureAwait(false);
        Task pending; lock (gate) pending = worker;
        await pending.ConfigureAwait(false);
    }
    public ValueTask DisposeAsync() { Dispose(); lock (gate) return new ValueTask(cleanup); }
}
