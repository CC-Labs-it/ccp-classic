namespace CCP.Classic.Core.LocalDevices;

public sealed partial class LocalDeviceController
{
    private bool inputEnabled = true;
    private long inputGeneration;
    public event Action<LocalButtonPress>? ButtonPressed;
    public bool HasButtonInput { get { lock (gate) return providers.Values.Any(state => !state.Faulted && state.Connection.HasButtonInput); } }
    public bool InputEnabled
    {
        get { lock (gate) return inputEnabled; }
        set
        {
            lock (gate)
            {
                if (disposed || value == inputEnabled) return;
                inputEnabled = value; inputGeneration++;
                foreach (var state in providers.Values) state.Connection.SetInputEnabled(value);
            }
        }
    }
    public bool IsCurrentInput(LocalButtonPress press)
    {
        lock (gate) return !disposed && !stopping && inputEnabled && press.Generation == inputGeneration
            && providers.TryGetValue(press.Device.Provider, out var state) && !state.Faulted
            && state.Connection.Status == LocalDeviceStatus.Connected
            && state.Connection.IsCurrentInput(press)
            && state.Connection.Devices.Any(device => device.Id == press.Device.Id && device.ButtonFeatures.Contains(press.FeatureIndex))
            && routing.DeviceRules.GetValueOrDefault(LocalDeviceRoutingOptions.Key(press.Device))?.Enabled != false;
    }
    private void AttachControllerInput(ProviderState state)
    {
        state.Connection.SetInputEnabled(inputEnabled);
        state.InputHandler = press =>
        {
            LocalButtonPress current;
            lock (gate)
            {
                if (disposed || stopping || !inputEnabled || !IsCurrent(state) || state.Faulted) return;
                var observed = press.ObservedAt;
                if (!ReferenceEquals(press.ObservationClock, clock))
                {
                    var age = press.ObservationClock.GetElapsedTime(observed).TotalSeconds;
                    if (!double.IsFinite(age) || age < 0 || age > 60) return;
                    observed = clock.GetTimestamp() - (long)(age * clock.TimestampFrequency);
                }
                current = press with { Device = press.Device with { Provider = state.Provider }, ObservedAt = observed, ObservationClock = clock, Generation = inputGeneration };
                if (!IsCurrentInput(current)) return;
            }
            ButtonPressed?.Invoke(current);
        };
        state.Connection.ButtonPressed += state.InputHandler;
    }
    private static void DetachControllerInput(ProviderState state)
    {
        if (state.InputHandler is not null) state.Connection.ButtonPressed -= state.InputHandler;
        state.InputHandler = null;
    }
}
