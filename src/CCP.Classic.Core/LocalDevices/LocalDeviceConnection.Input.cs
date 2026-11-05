using System.Text.Json;

namespace CCP.Classic.Core.LocalDevices;

public sealed record LocalButtonPress(LocalDeviceInfo Device, int FeatureIndex, long ObservedAt, long Generation = 0, long FeatureGeneration = 0)
{ internal TimeProvider ObservationClock { get; init; } = TimeProvider.System; }

public sealed partial class LocalDeviceConnection
{
    private readonly record struct ButtonKey(string Device, int Feature);
    private readonly TimeProvider inputClock = timeProvider ?? TimeProvider.System;
    private readonly HashSet<ButtonKey> advertised = [], subscribed = [], attempted = [], held = [], failedInput = [], unwanted = [];
    private readonly Dictionary<string, long> lastPress = [];
    private readonly Dictionary<ButtonKey, long> featureGenerations = [];
    private long nextFeatureGeneration;
    private ILocalDeviceTransport? inputOwner;
    private Action<string>? inputHandler;
    private CancellationTokenSource? inputCancellation;
    private bool inputEnabled = true, inputWorking, inputDirty;
    private Task inputWork = Task.CompletedTask;
    private int inputPauses;
    public event Action<LocalButtonPress>? ButtonPressed;
    public bool HasButtonInput { get { lock (gate) return inputOwner is not null && advertised.Count > 0; } }
    public bool IsCurrentInput(LocalButtonPress press)
    {
        lock (gate) return inputEnabled && !disconnecting && inputOwner is not null
            && featureGenerations.TryGetValue(new(press.Device.Id, press.FeatureIndex), out var generation) && generation == press.FeatureGeneration;
    }
    public void SetInputEnabled(bool value)
    {
        lock (gate)
        {
            if (inputEnabled == value) return; inputEnabled = value;
            foreach (var key in featureGenerations.Keys.ToArray()) featureGenerations[key] = ++nextFeatureGeneration;
            if (!value) unwanted.UnionWith(attempted);
            subscribed.Clear();
            held.Clear(); lastPress.Clear(); failedInput.Clear(); inputCancellation?.Cancel();
            inputDirty = true; QueueInputLocked();
        }
    }
    private void AttachInput(ILocalDeviceTransport current, LocalDeviceProvider selected)
    {
        if (selected != LocalDeviceProvider.Intiface || current is not ILocalDeviceEventTransport events) return;
        lock (gate)
        {
            inputOwner = current; inputHandler = json => InputMessage(current, json); events.MessageReceived += inputHandler;
        }
    }
    private void UpdateInputs(ILocalDeviceTransport current)
    {
        lock (gate)
        {
            if (!ReferenceEquals(inputOwner, current)) return;
            var next = devices.SelectMany(device => device.ButtonFeatures.Select(feature => new ButtonKey(device.Id, feature))).ToHashSet();
            foreach (var key in featureGenerations.Keys.Where(key => !next.Contains(key)).ToArray()) featureGenerations.Remove(key);
            foreach (var key in next) if (!featureGenerations.ContainsKey(key)) featureGenerations[key] = ++nextFeatureGeneration;
            advertised.IntersectWith(next); advertised.UnionWith(next);
            subscribed.RemoveWhere(key => !next.Contains(key)); attempted.RemoveWhere(key => !next.Contains(key));
            held.RemoveWhere(key => !next.Contains(key)); failedInput.RemoveWhere(key => !next.Contains(key));
            unwanted.RemoveWhere(key => !next.Contains(key));
            foreach (var key in lastPress.Keys.Where(id => !next.Any(feature => feature.Device == id)).ToArray()) lastPress.Remove(key);
            inputDirty = true; QueueInputLocked();
        }
    }
    private void QueueInputLocked()
    {
        if (inputWorking || inputOwner is null || disconnecting || status != LocalDeviceStatus.Connected || inputPauses > 0 || !inputDirty) return;
        inputWorking = true; var current = inputOwner; inputCancellation = new(); var stop = inputCancellation;
        inputWork = Task.Run(() => ReconcileInputs(current, stop));
    }
    private async Task ReconcileInputs(ILocalDeviceTransport current, CancellationTokenSource stop)
    {
        try
        {
            while (true)
            {
                ButtonKey? selected = null; var enable = false; long generation = 0;
                lock (gate)
                {
                    if (!ReferenceEquals(inputOwner, current) || disconnecting || stop.IsCancellationRequested) return;
                    inputDirty = false;
                    if (inputEnabled)
                    {
                        var missing = advertised.Where(key => !subscribed.Contains(key) && !failedInput.Contains(key)).Take(1).ToArray();
                        if (missing.Length > 0) { selected = missing[0]; enable = true; attempted.Add(selected.Value); }
                    }
                    else if (unwanted.Count > 0 || subscribed.Count > 0) selected = unwanted.Concat(subscribed).First();
                    if (selected is { } pending) featureGenerations.TryGetValue(pending, out generation);
                }
                if (selected is not { } key) return;
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(1000);
                    await Request(current, "InputCmd", new() { ["DeviceIndex"] = int.Parse(key.Device, System.Globalization.CultureInfo.InvariantCulture),
                        ["FeatureIndex"] = key.Feature, ["Type"] = "Button", ["Command"] = enable ? "Subscribe" : "Unsubscribe" }, "Ok", timeout.Token).ConfigureAwait(false);
                    lock (gate)
                    {
                        if (!ReferenceEquals(inputOwner, current) || !featureGenerations.TryGetValue(key, out var latest) || latest != generation) continue;
                        if (enable) subscribed.Add(key); else { unwanted.Remove(key); subscribed.Remove(key); attempted.Remove(key); held.Remove(key); }
                    }
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    lock (gate)
                    {
                        if (!ReferenceEquals(inputOwner, current) || !featureGenerations.TryGetValue(key, out var latest) || latest != generation) continue;
                        if (!enable) unwanted.Remove(key); attempted.Remove(key); subscribed.Remove(key); held.Remove(key);
                        if (!stop.IsCancellationRequested) failedInput.Add(key);
                    }
                }
            }
        }
        finally
        {
            lock (gate)
            {
                if (ReferenceEquals(inputCancellation, stop)) { inputCancellation = null; inputWorking = false; QueueInputLocked(); }
            }
            stop.Dispose();
        }
    }
    private void InputMessage(ILocalDeviceTransport current, string json)
    {
        try
        {
            var messages = LocalDeviceProtocol.Parse(json); if (messages.ValueKind != JsonValueKind.Array) return;
            foreach (var envelope in messages.EnumerateArray())
            {
                if (envelope.TryGetProperty("DeviceList", out var list) && list.TryGetProperty("Id", out var listId) && listId.TryGetInt32(out var listNumber) && listNumber == 0)
                { SetDevices(current, LocalDeviceProtocol.IntifaceDevices(list)); continue; }
                if (!envelope.TryGetProperty("InputReading", out var reading) || !reading.TryGetProperty("DeviceIndex", out var deviceIndex)
                    || !reading.TryGetProperty("Id", out var readingId) || !readingId.TryGetInt32(out var readingNumber) || readingNumber != 0
                    || !deviceIndex.TryGetInt32(out var device) || !reading.TryGetProperty("FeatureIndex", out var featureIndex) || !featureIndex.TryGetInt32(out var feature)
                    || !reading.TryGetProperty("Reading", out var values) || !values.TryGetProperty("Button", out var button)
                    || !button.TryGetProperty("Value", out var raw) || !raw.TryGetInt32(out var value) || value is not (0 or 1)) continue;
                LocalButtonPress? pressed = null;
                lock (gate)
                {
                    var key = new ButtonKey(device.ToString(System.Globalization.CultureInfo.InvariantCulture), feature);
                    if (!ReferenceEquals(inputOwner, current) || !ReferenceEquals(transport, current) || disconnecting || !inputEnabled
                        || status != LocalDeviceStatus.Connected || !advertised.Contains(key) || !attempted.Contains(key)) continue;
                    if (value == 0) { held.Remove(key); continue; }
                    if (!held.Add(key)) continue;
                    var now = inputClock.GetTimestamp();
                    if (lastPress.TryGetValue(key.Device, out var previous) && inputClock.GetElapsedTime(previous, now).TotalMilliseconds < 350) continue;
                    var info = devices.FirstOrDefault(item => item.Id == key.Device); if (info is null) continue;
                    lastPress[key.Device] = now; pressed = new(info, feature, now, FeatureGeneration: featureGenerations[key]) { ObservationClock = inputClock };
                }
                if (pressed is not null) ButtonPressed?.Invoke(pressed);
            }
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException) { }
    }
    private Task DetachInput(ILocalDeviceTransport current)
    {
        lock (gate)
        {
            if (!ReferenceEquals(inputOwner, current)) return Task.CompletedTask;
            if (current is ILocalDeviceEventTransport events && inputHandler is not null) events.MessageReceived -= inputHandler;
            inputOwner = null; inputHandler = null; inputCancellation?.Cancel(); inputDirty = false;
            advertised.Clear(); subscribed.Clear(); attempted.Clear(); held.Clear(); failedInput.Clear(); unwanted.Clear(); lastPress.Clear();
            featureGenerations.Clear();
            return inputWork;
        }
    }
}
