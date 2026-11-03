using System.Text.Json;

namespace CCP.Classic.Core.LocalDevices;

public enum LocalDeviceStatus { Disconnected, Connecting, Connected, Unavailable }

public sealed partial class LocalDeviceConnection(Func<ILocalDeviceTransport>? transportFactory = null, TimeProvider? timeProvider = null) : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly Func<ILocalDeviceTransport> factory = transportFactory ?? (() => new LoopbackJsonTransport());
    private ILocalDeviceTransport? transport;
    private LocalDeviceProvider provider;
    private IReadOnlyList<LocalDeviceInfo> devices = [];
    private LocalDeviceStatus status;
    private int nextId, outputEpoch;
    private bool disconnecting;
    public LocalDeviceStatus Status { get { lock (gate) return status; } }
    public IReadOnlyList<LocalDeviceInfo> Devices { get { lock (gate) return devices; } }
    public int PingIntervalMilliseconds { get; private set; }
    public event Action? Changed;

    public async Task<bool> ConnectAsync(LocalDeviceProvider selected, int port, CancellationToken token)
    {
        var endpoint = LocalDeviceEndpoint.Create(selected, port);
        await DisconnectAsync().ConfigureAwait(false);
        var candidate = factory();
        lock (gate) { transport = candidate; provider = selected; status = LocalDeviceStatus.Connecting; disconnecting = false; }
        AttachInput(candidate, selected);
        Changed?.Invoke();
        try
        {
            await candidate.OpenAsync(endpoint, token).ConfigureAwait(false);
            if (selected == LocalDeviceProvider.Intiface)
            {
                var info = await Request(candidate, "RequestServerInfo", new()
                    { ["ClientName"] = "CCP Classic", ["ProtocolVersionMajor"] = 4, ["ProtocolVersionMinor"] = 0 }, "ServerInfo", token).ConfigureAwait(false);
                if (!info.TryGetProperty("ProtocolVersionMajor", out var version) || version.GetInt32() != 4)
                    throw new IOException("Intiface protocol4 is required");
                PingIntervalMilliseconds = info.TryGetProperty("MaxPingTime", out var ping) && ping.TryGetInt32(out var maximum) && maximum > 0
                    ? Math.Clamp(maximum / 2, 10, 1000) : 0;
                try { await Request(candidate, "StartScanning", new(), "Ok", token).ConfigureAwait(false); }
                catch (IOException) { }
            }
            await Refresh(candidate, selected, token).ConfigureAwait(false);
            lock (gate)
            {
                if (!ReferenceEquals(transport, candidate) || disconnecting) return false;
                status = LocalDeviceStatus.Connected;
                QueueInputLocked();
            }
            Changed?.Invoke(); return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { await CloseCandidate(candidate, LocalDeviceStatus.Disconnected).ConfigureAwait(false); throw; }
        catch (Exception)
        { await CloseCandidate(candidate, LocalDeviceStatus.Unavailable).ConfigureAwait(false); return false; }
    }

    public async Task RefreshAsync(CancellationToken token)
    {
        ILocalDeviceTransport? current; LocalDeviceProvider selected;
        lock (gate) { current = disconnecting ? null : transport; selected = provider; }
        if (current is not null) await Refresh(current, selected, token).ConfigureAwait(false);
    }
    private async Task Refresh(ILocalDeviceTransport current, LocalDeviceProvider selected, CancellationToken token)
    {
        if (selected == LocalDeviceProvider.Intiface)
            await Request(current, "RequestDeviceList", new(), "DeviceList", token).ConfigureAwait(false);
        else
        {
            var response = LocalDeviceProtocol.Parse(await current.ExchangeAsync("{\"command\":\"GetToys\"}", token).ConfigureAwait(false));
            SetDevices(current, LocalDeviceProtocol.LovenseDevices(response));
        }
    }

    public async Task PingAsync(CancellationToken token)
    {
        ILocalDeviceTransport? current;
        lock (gate) current = provider == LocalDeviceProvider.Intiface && !disconnecting ? transport : null;
        if (current is not null) await Request(current, "Ping", new(), "Ok", token).ConfigureAwait(false);
    }

    public async Task SetLevelAsync(double intensity, CancellationToken token)
    {
        var level = double.IsFinite(intensity) ? Math.Clamp(intensity, 0, 1) : 0;
        if (level == 0) { await StopAsync(token).ConfigureAwait(false); return; }
        await SetLevelsAsync(_ => level, token).ConfigureAwait(false);
    }

    public Task SetLevelsAsync(Func<LocalDeviceInfo, double> levels, CancellationToken token) => SetActuatorLevelsAsync((device, _) => levels(device), token);

    // Negative requests omit an actuator, allowing an inactive absolute-position motor to hold its target.
    public async Task SetActuatorLevelsAsync(Func<LocalDeviceInfo, LocalActuator, double> levels, CancellationToken token)
    {
        ILocalDeviceTransport? current; IReadOnlyList<LocalDeviceInfo> targets; LocalDeviceProvider selected; int epoch;
        lock (gate) { current = status == LocalDeviceStatus.Connected && !disconnecting ? transport : null; targets = devices; selected = provider; epoch = outputEpoch; }
        if (current is null) return;
        foreach (var device in targets)
        {
            if (!CanOutput(current, epoch)) return;
            double Level(LocalActuator actuator) { var requested = levels(device, actuator); return double.IsFinite(requested) ? Math.Clamp(requested, 0, 1) : 0; }
            if (selected == LocalDeviceProvider.LovenseConnect)
            {
                var included = device with { Actuators = device.Actuators.Where(actuator => levels(device, actuator) >= 0).ToArray() };
                var active = included.Actuators.Any(actuator => Level(actuator) > 0 || actuator.Kind == LocalActuatorKind.Position);
                // A short lease prevents retained output if the local app vanishes. Every function is restated together.
                var command = JsonSerializer.Serialize(new { command = "Function", action = !active ? "Stop" : LocalDeviceProtocol.LovenseAction(included, Level),
                    timeSec = !active ? 0 : 2, apiVer = 1, stopPrevious = 1, toy = device.Id });
                LocalDeviceProtocol.CheckLovense(LocalDeviceProtocol.Parse(await current.ExchangeAsync(command, token).ConfigureAwait(false)));
                continue;
            }
            foreach (var actuator in device.Actuators)
            {
                if (!CanOutput(current, epoch)) return;
                if (levels(device, actuator) < 0) continue;
                var value = new Dictionary<string, object> { ["Value"] = LocalDeviceProtocol.NativeLevel(actuator, Level(actuator)) };
                if (actuator.Command == "HwPositionWithDuration") value["Duration"] = Math.Clamp(100, actuator.MinimumDuration, actuator.MaximumDuration);
                await Request(current, "OutputCmd", new() { ["DeviceIndex"] = int.Parse(device.Id, System.Globalization.CultureInfo.InvariantCulture),
                    ["FeatureIndex"] = actuator.Index, ["Command"] = new Dictionary<string, object> { [actuator.Command] = value } }, "Ok", token).ConfigureAwait(false);
            }
        }
    }

    private bool CanOutput(ILocalDeviceTransport current, int epoch)
    { lock (gate) return ReferenceEquals(transport, current) && !disconnecting && epoch == outputEpoch; }

    public async Task StopAsync(CancellationToken token = default)
    {
        ILocalDeviceTransport? current; LocalDeviceProvider selected;
        lock (gate) { outputEpoch++; current = transport; selected = provider; inputPauses++; inputCancellation?.Cancel(); }
        try
        {
            if (current is null) return;
            if (selected == LocalDeviceProvider.Intiface)
                await Request(current, "StopCmd", new() { ["Inputs"] = false, ["Outputs"] = true }, "Ok", token).ConfigureAwait(false);
            else
                LocalDeviceProtocol.CheckLovense(LocalDeviceProtocol.Parse(await current.ExchangeAsync(
                    "{\"command\":\"Function\",\"action\":\"Stop\",\"timeSec\":0,\"apiVer\":1,\"stopPrevious\":1}", token).ConfigureAwait(false)));
        }
        finally { lock (gate) { inputPauses--; QueueInputLocked(); } }
    }

    public async Task DisconnectAsync()
    {
        ILocalDeviceTransport? current;
        lock (gate) { disconnecting = true; outputEpoch++; current = transport; }
        if (current is not null)
        {
            try { await StopAsync().ConfigureAwait(false); } catch (Exception) { }
            await CloseCandidate(current, LocalDeviceStatus.Disconnected).ConfigureAwait(false);
        }
        else { lock (gate) { devices = []; status = LocalDeviceStatus.Disconnected; disconnecting = false; } }
    }

    private async Task<JsonElement> Request(ILocalDeviceTransport current, string name, Dictionary<string, object> fields,
        string expected, CancellationToken token)
    {
        var id = Interlocked.Increment(ref nextId); fields["Id"] = id;
        var json = JsonSerializer.Serialize(new[] { new Dictionary<string, object> { [name] = fields } });
        var response = LocalDeviceProtocol.Parse(await current.ExchangeAsync(json, token).ConfigureAwait(false));
        if (response.ValueKind != JsonValueKind.Array) throw new IOException("Invalid Intiface response");
        JsonElement? result = null;
        foreach (var message in response.EnumerateArray())
        foreach (var property in message.EnumerateObject())
        {
            if (property.Name == "DeviceList") SetDevices(current, LocalDeviceProtocol.IntifaceDevices(property.Value));
            if (!property.Value.TryGetProperty("Id", out var replyId) || replyId.GetInt32() != id) continue;
            if (property.Name == "Error") throw new IOException("Intiface rejected the command");
            if (property.Name == expected) result = property.Value.Clone();
        }
        return result ?? throw new IOException("Missing Intiface response");
    }
    private void SetDevices(ILocalDeviceTransport current, IReadOnlyList<LocalDeviceInfo> values)
    {
        lock (gate) { if (!ReferenceEquals(transport, current) || disconnecting) return; devices = values; }
        UpdateInputs(current);
        Changed?.Invoke();
    }
    private async Task CloseCandidate(ILocalDeviceTransport current, LocalDeviceStatus result)
    {
        var inputDone = DetachInput(current);
        lock (gate)
        {
            if (ReferenceEquals(transport, current)) { transport = null; devices = []; status = result; PingIntervalMilliseconds = 0; disconnecting = false; outputEpoch++; }
        }
        await current.DisposeAsync().ConfigureAwait(false); await inputDone.ConfigureAwait(false); Changed?.Invoke();
    }
    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);
}
