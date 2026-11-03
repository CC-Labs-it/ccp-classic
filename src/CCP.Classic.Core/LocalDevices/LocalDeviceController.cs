namespace CCP.Classic.Core.LocalDevices;

public sealed class LocalDeviceController : IDisposable, IAsyncDisposable
{
    private sealed class ProviderState(LocalDeviceProvider provider, LocalDeviceConnection connection)
    {
        public readonly LocalDeviceProvider Provider = provider;
        public readonly LocalDeviceConnection Connection = connection;
        public bool Faulted;
        public long LastSend, LastRefresh, LastPing;
        public string LastLevels = "";
    }
    private readonly object gate = new();
    private readonly SemaphoreSlim signal = new(0, 1), outputLane = new(1, 1);
    private readonly Dictionary<LocalDeviceProvider, ProviderState> providers = [];
    private readonly LocalPulseMixer pulses = new();
    private readonly Dictionary<string, (LocalContinuousFloor Floor, long Cycle)> floors = new();
    private long mixCycle;
    private readonly Func<LocalDeviceConnection> factory;
    private readonly TimeProvider clock;
    private readonly long mixerOrigin;
    private Task worker = Task.CompletedTask, cleanup = Task.CompletedTask;
    private bool working, disposed, stopping, enabled = true;
    private int activeStops;
    private double globalIntensity = .7, audioLevel;
    private double audioLow, audioHigh;
    private bool splitAudio;
    private double scriptLevel;
    private double? scriptPosition;
    private long scriptUpdatedAt;
    private bool hasScript;
    private double luminanceLevel;
    private long luminanceAt;
    private double videoLevel;
    private long videoAt;
    private long epoch, mutedAt, workerVersion, audioUpdatedAt;
    private bool muted, hasAudioLevel;
    private LocalDeviceRoutingOptions routing = new();
    public event Action? Changed;
    public IReadOnlyList<LocalDeviceInfo> Devices
    { get { lock (gate) return providers.Values.Where(p => !p.Faulted).SelectMany(p => p.Connection.Devices).ToList().AsReadOnly(); } }
    public LocalDeviceStatus Status
    { get { lock (gate) return providers.Count == 0 ? LocalDeviceStatus.Disconnected : providers.Values.Any(p => !p.Faulted && p.Connection.Status == LocalDeviceStatus.Connected)
        ? LocalDeviceStatus.Connected : providers.Values.Any(p => p.Connection.Status == LocalDeviceStatus.Connecting) ? LocalDeviceStatus.Connecting : LocalDeviceStatus.Unavailable; } }
    public LocalDeviceStatus GetStatus(LocalDeviceProvider provider)
    { lock (gate) return providers.TryGetValue(provider, out var state) ? state.Faulted ? LocalDeviceStatus.Unavailable : state.Connection.Status : LocalDeviceStatus.Disconnected; }
    public int ActivePulseCount { get { lock (gate) return pulses.ActiveCount; } }
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
    { factory = connectionFactory ?? (() => new LocalDeviceConnection()); this.clock = clock ?? TimeProvider.System; mixerOrigin = this.clock.GetTimestamp(); }

    public void ConfigureRouting(LocalDeviceRoutingOptions options)
    {
        var snapshot = options.Snapshot();
        lock (gate) { if (disposed) return; routing = snapshot; epoch++; foreach (var provider in providers.Values) provider.LastLevels = ""; Wake(); }
    }

    public async Task<bool> ConnectAsync(LocalDeviceProvider provider, int port, CancellationToken token)
    {
        LocalDeviceEndpoint.Create(provider, port);
        ProviderState? old; ProviderState state;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            providers.Remove(provider, out old); floors.Clear();
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
        => EmitScoped(kind, intensity, duration, pattern, null);
    public void EmitForDevice(LocalDeviceInfo device, double intensity, TimeSpan duration, LocalHapticPattern pattern = LocalHapticPattern.Constant)
        => EmitScoped(LocalDeviceEvent.Manual, intensity, duration, pattern, LocalDeviceRoutingOptions.Key(device));

    private void EmitScoped(LocalDeviceEvent kind, double intensity, TimeSpan duration, LocalHapticPattern pattern, string? device)
    {
        if (kind == LocalDeviceEvent.AudioSync) { SetAudioLevel(intensity); return; }
        lock (gate)
        {
            if (disposed || !enabled || stopping || !providers.Values.Any(p => !p.Faulted && p.Connection.Status == LocalDeviceStatus.Connected)
                || muted && clock.GetElapsedTime(mutedAt).TotalMilliseconds < 400) return;
            var steps = LocalHapticPatterns.Render(pattern, intensity, duration); if (steps.Count == 0) return;
            var priority = kind is LocalDeviceEvent.Achievement or LocalDeviceEvent.LevelUp ? 2 : kind is LocalDeviceEvent.Attention or LocalDeviceEvent.LockCard ? 1 : 0;
            pulses.Enqueue(kind, steps, priority, device, clock.GetElapsedTime(mixerOrigin).TotalMilliseconds,
                LocalHapticTemperament.FromKey(routing.Temperament), routing.MaximumConcurrentPulses); Wake();
        }
    }

    public void SetAudioLevel(double value) => SetAudioBands(value, value, value, false);

    public void SetScriptLevel(double intensity, double? position)
    {
        lock (gate)
        {
            if (disposed) return;
            var level = double.IsFinite(intensity) ? Math.Clamp(intensity, 0, 1) : 0;
            var target = position.HasValue && double.IsFinite(position.Value) ? Math.Clamp(position.Value, 0, 1) : (double?)null;
            if (level == 0 && target is null) { ClearScript(); Wake(); return; }
            if (!enabled || stopping || !providers.Values.Any(provider => !provider.Faulted && provider.Connection.Status == LocalDeviceStatus.Connected)
                || muted && clock.GetElapsedTime(mutedAt).TotalMilliseconds < 400) return;
            scriptLevel = level; scriptPosition = target; hasScript = true; scriptUpdatedAt = clock.GetTimestamp(); Wake();
        }
    }
    private void ClearScript() { hasScript = false; scriptLevel = 0; scriptPosition = null; }
    public void SetLuminanceLevel(double value)
    {
        lock (gate)
        {
            if (disposed) return;
            var next = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
            if (next == 0) { luminanceLevel = 0; Wake(); return; }
            if (!enabled || stopping || !providers.Values.Any(provider => !provider.Faulted && provider.Connection.Status == LocalDeviceStatus.Connected)
                || muted && clock.GetElapsedTime(mutedAt).TotalMilliseconds < 400) return;
            luminanceLevel = next; luminanceAt = clock.GetTimestamp(); Wake();
        }
    }
    public void SetVideoLevel(double value)
    {
        lock (gate)
        {
            if (disposed) return;
            var next = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
            if (next == 0) { videoLevel = 0; Wake(); return; }
            if (!enabled || stopping || !providers.Values.Any(provider => !provider.Faulted && provider.Connection.Status == LocalDeviceStatus.Connected)
                || muted && clock.GetElapsedTime(mutedAt).TotalMilliseconds < 400) return;
            videoLevel = next; videoAt = clock.GetTimestamp(); Wake();
        }
    }

    public void SetAudioBands(double value, double low, double high, bool split)
    {
        var next = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
        lock (gate)
        {
            if (disposed) return;
            if (next == 0) { ClearAudio(); Wake(); return; }
            if (!enabled || stopping || !providers.Values.Any(p => !p.Faulted && p.Connection.Status == LocalDeviceStatus.Connected)
                || muted && clock.GetElapsedTime(mutedAt).TotalMilliseconds < 400) return;
            audioLevel = next; audioLow = double.IsFinite(low) ? Math.Clamp(low, 0, 1) : 0;
            audioHigh = double.IsFinite(high) ? Math.Clamp(high, 0, 1) : 0; splitAudio = split;
            audioUpdatedAt = clock.GetTimestamp(); hasAudioLevel = true; Wake();
        }
    }

    private void ClearAudio() { audioLevel = audioLow = audioHigh = 0; splitAudio = false; audioUpdatedAt = 0; hasAudioLevel = false; }
    private static string ActuatorKey(LocalDeviceInfo device, LocalActuator actuator) => device.Id + ":" + actuator.Index + ":" + actuator.Kind;
    private double MixedLevel(LocalDeviceInfo device, LocalActuator actuator)
    {
        if (!enabled || stopping) return actuator.Kind == LocalActuatorKind.Position ? -1 : 0;
        var rule = routing.DeviceRules.GetValueOrDefault(LocalDeviceRoutingOptions.Key(device));
        if (rule?.Enabled == false) { floors.Remove(LocalDeviceRoutingOptions.Key(device)); return actuator.Kind == LocalActuatorKind.Position ? -1 : 0; }
        var role = rule?.Role ?? LocalToyRole.All;
        var scripted = hasScript && clock.GetElapsedTime(scriptUpdatedAt).TotalMilliseconds is >= 0 and < 300
            && LocalDeviceRoutingOptions.Matches(routing.FunScriptRole, role);
        if (actuator.Kind == LocalActuatorKind.Position)
            return scripted && scriptPosition is { } position && globalIntensity > 0 && routing.MaximumPower > 0
                ? Math.Min(routing.MaximumPower, position * globalIntensity) * (rule?.Trim ?? 1) : -1;
        var audio = hasAudioLevel && clock.GetElapsedTime(audioUpdatedAt).TotalMilliseconds is >= 0 and < 300 ? audioLevel : 0;
        var perMotorAudio = audio;
        if (audio > 0 && splitAudio)
        {
            var motors = device.Actuators.Where(value => value.Kind == LocalActuatorKind.Vibrate).OrderBy(value => value.Index).ToArray();
            if (motors.Length >= 2)
            {
                audio = Math.Max(audioLow, audioHigh);
                perMotorAudio = actuator == motors[0] ? audioLow : actuator == motors[1] ? audioHigh : audio;
            }
        }
        if (!LocalDeviceRoutingOptions.Matches(routing.Target(LocalDeviceEvent.AudioSync), role)) audio = perMotorAudio = 0;
        var luminance = clock.GetElapsedTime(luminanceAt).TotalMilliseconds is >= 0 and < 300
            && LocalDeviceRoutingOptions.Matches(routing.LuminanceRole, role) ? luminanceLevel : 0;
        var video = clock.GetElapsedTime(videoAt).TotalMilliseconds is >= 0 and < 300
            && LocalDeviceRoutingOptions.Matches(routing.Target(LocalDeviceEvent.Video), role) ? videoLevel : 0;
        var temperament = LocalHapticTemperament.FromKey(routing.Temperament);
        var events = pulses.Sample(LocalDeviceRoutingOptions.Key(device), kind => LocalDeviceRoutingOptions.Matches(routing.Target(kind), role), temperament.TransientScale);
        var continuous = Math.Max(Math.Max(Math.Max(audio, luminance), video), scripted ? scriptLevel : 0) * temperament.ContinuousScale;
        var key = LocalDeviceRoutingOptions.Key(device);
        if (!floors.TryGetValue(key, out var floor)) floor = (new(), -1);
        if (floor.Cycle != mixCycle)
        {
            floor.Floor.Step(continuous, clock.GetElapsedTime(mixerOrigin).TotalMilliseconds, routing.SoftRampMilliseconds);
            floors[key] = (floor.Floor, mixCycle);
        }
        var motorTarget = Math.Max(Math.Max(Math.Max(perMotorAudio, luminance), video), scripted ? scriptLevel : 0) * temperament.ContinuousScale;
        var ratio = actuator.Kind == LocalActuatorKind.Vibrate && continuous > 0 ? Math.Clamp(motorTarget / continuous, 0, 1) : 1;
        return LocalContinuousFloor.Finish(Math.Max(events, floor.Floor.Value * ratio), globalIntensity, routing.MaximumPower, rule?.Trim ?? 1);
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
                    var connectedKeys = states.SelectMany(state => state.Connection.Devices).Select(LocalDeviceRoutingOptions.Key).ToHashSet(StringComparer.Ordinal);
                    foreach (var key in floors.Keys.Where(key => !connectedKeys.Contains(key)).ToArray()) floors.Remove(key);
                    pulses.Advance(clock.GetElapsedTime(mixerOrigin).TotalMilliseconds, LocalHapticTemperament.FromKey(routing.Temperament), routing.MaximumConcurrentPulses);
                    mixCycle++;
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
                            Dictionary<string, double> levels;
                            lock (gate)
                            {
                                if (disposed || !IsCurrent(state) || state.Faulted) continue;
                                levels = state.Connection.Devices.SelectMany(device => device.Actuators.Select(actuator =>
                                    new KeyValuePair<string, double>(ActuatorKey(device, actuator), version == epoch ? MixedLevel(device, actuator)
                                        : actuator.Kind == LocalActuatorKind.Position ? -1 : 0))).ToDictionary();
                            }
                            var signature = string.Join(';', levels.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item.Key + "=" + item.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
                            if (signature != state.LastLevels || clock.GetElapsedTime(state.LastSend).TotalSeconds >= 1)
                            {
                                var positionTarget = state.Connection.Devices.Any(device => device.Actuators.Any(actuator => actuator.Kind == LocalActuatorKind.Position
                                    && levels.GetValueOrDefault(ActuatorKey(device, actuator), -1) >= 0));
                                if (!positionTarget && levels.Values.All(value => value <= 0)) await state.Connection.SetLevelAsync(0, timeout.Token).ConfigureAwait(false);
                                else await state.Connection.SetActuatorLevelsAsync((device, actuator) => levels.GetValueOrDefault(ActuatorKey(device, actuator),
                                    actuator.Kind == LocalActuatorKind.Position ? -1 : 0), timeout.Token).ConfigureAwait(false);
                                state.LastLevels = signature; state.LastSend = clock.GetTimestamp();
                            }
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
            pulses.Clear(); floors.Clear(); ClearAudio(); ClearScript(); luminanceLevel = videoLevel = 0; activeStops++; stopping = true; muted = true; mutedAt = clock.GetTimestamp(); epoch++;
            states = providers.Values.ToArray(); Wake();
        }
        await outputLane.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(states.Select(StopProvider)).ConfigureAwait(false);
            foreach (var state in states) { state.LastLevels = ""; state.LastSend = clock.GetTimestamp(); }
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
            floors.Clear();
            if (providers.Count == 0) { pulses.Clear(); ClearAudio(); ClearScript(); luminanceLevel = videoLevel = 0; } epoch++; Wake();
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
            disposed = true; pulses.Clear(); floors.Clear(); ClearAudio(); ClearScript(); luminanceLevel = videoLevel = 0; epoch++; Wake(); cleanup = Cleanup();
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
