using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Classic.Core.LocalDevices;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public sealed class ToyRoutingEditor : OptionEditor, IDisposable
{
    private readonly LocalDeviceRoutingOptions options;
    private readonly LocalDeviceController controller;
    private readonly Func<bool>? requestStop;
    private readonly StackPanel devices = new() { Spacing = 7 };
    private string? registry;
    private bool disposed;

    public ToyRoutingEditor(LocalDeviceRoutingOptions options, LocalDeviceController controller,
        LocalizationService text, Action changed, Func<bool>? requestStop = null) : base(text, () => { controller.ConfigureRouting(options); changed(); })
    {
        this.options = options; this.controller = controller; this.requestStop = requestStop; Spacing = 8;
        Heading("DeviceRouting"); Number("MaximumPower", options.MaximumPower * 100, 0, 100, value => options.MaximumPower = value / 100);
        Number("ToySoftRamp", options.SoftRampMilliseconds, 1, 60000, value => options.SoftRampMilliseconds = (int)value);
        var profiles = LocalHapticTemperament.All.Select(profile => new RoleChoiceLabel(profile.Key,
            text["Temperament" + char.ToUpperInvariant(profile.Key[0]) + profile.Key[1..]])).ToArray();
        var temperament = new ComboBox { ItemsSource = profiles, SelectedItem = profiles.First(item => item.Key == LocalHapticTemperament.FromKey(options.Temperament).Key) };
        temperament.SelectionChanged += (_, _) =>
        {
            if (temperament.SelectedItem is not RoleChoiceLabel profile) return;
            options.Temperament = profile.Key; Changed(); registry = null; RefreshDevices();
        };
        Children.Add(Row("ToyTemperament", temperament));
        Number("MaximumConcurrentPulses", options.MaximumConcurrentPulses, 1, 12, value => options.MaximumConcurrentPulses = (int)value);
        Children.Add(devices); Heading("EventRouting");
        foreach (var kind in Enum.GetValues<LocalDeviceEvent>())
        {
            var eventTitle = kind switch
            {
                LocalDeviceEvent.Flash => "FlashImages", LocalDeviceEvent.Subliminal => "Subliminals", LocalDeviceEvent.Video => "MandatoryVideos",
                LocalDeviceEvent.Attention => "AttentionCheck", LocalDeviceEvent.Achievement => "Achievements", LocalDeviceEvent.LevelUp => "Level",
                LocalDeviceEvent.Keyword => "KeywordTriggers", LocalDeviceEvent.Manual => "Test", _ => kind.ToString()
            };
            var selected = options.EventTargets.GetValueOrDefault(kind.ToString());
            Children.Add(Row(eventTitle, Roles(selected, value => options.EventTargets[kind.ToString()] = value), "EventRouting"));
        }
        Children.Add(Row("FunScriptEnabled", Roles(options.FunScriptRole, value => options.FunScriptRole = value), "EventRouting"));
        Children.Add(Row("LuminanceSync", Roles(options.LuminanceRole, value => options.LuminanceRole = value), "EventRouting"));
        controller.Changed += DevicesChanged; DetachedFromVisualTree += (_, _) => Dispose(); RefreshDevices();
    }
    private void DevicesChanged() => Dispatcher.UIThread.Post(() => { if (!disposed) RefreshDevices(); });
    private void RefreshDevices()
    {
        var current = controller.Devices;
        var signature = string.Join('|', current.Select(item => LocalDeviceRoutingOptions.Key(item) + item.Name));
        if (registry == signature) return; registry = signature; devices.Children.Clear();
        if (current.Count == 0) { devices.Children.Add(new TextBlock { Text = Text["NoDevices"], TextWrapping = Avalonia.Media.TextWrapping.Wrap }); return; }
        foreach (var device in current)
        {
            var rule = options.Device(device);
            var group = new DeviceEditor(rule, device, options, controller, requestStop, Text, Changed);
            devices.Children.Add(group);
        }
    }
    private ComboBox Roles(LocalToyRole value, Action<LocalToyRole> set)
    {
        var choices = Enum.GetValues<LocalToyRole>().Select(role => new RoleChoice(role, Text["Role" + role])).ToArray();
        var input = new ComboBox { ItemsSource = choices, SelectedItem = choices.First(item => item.Role == value), MinWidth = 120 };
        input.SelectionChanged += (_, _) => { if (input.SelectedItem is RoleChoice choice) { set(choice.Role); Changed(); } };
        return input;
    }
    public void Dispose() { if (disposed) return; disposed = true; controller.Changed -= DevicesChanged; }
    private sealed record RoleChoice(LocalToyRole Role, string Label) { public override string ToString() => Label; }
    private sealed record RoleChoiceLabel(string Key, string Label) { public override string ToString() => Label; }
    private sealed class DeviceEditor : OptionEditor
    {
        public DeviceEditor(LocalDeviceRule rule, LocalDeviceInfo device, LocalDeviceRoutingOptions options, LocalDeviceController controller,
            Func<bool>? requestStop, LocalizationService text, Action changed) : base(text, changed)
        {
            Children.Add(new TextBlock { Text = device.Name, FontWeight = Avalonia.Media.FontWeight.SemiBold });
            Toggle("DeviceEnabled", rule.Enabled, value => rule.Enabled = value);
            String("DeviceNickname", rule.Nickname, value => rule.Nickname = value);
            if (device.Actuators.Count > 0) Number("DeviceTrim", rule.Trim * 100, 0, 100, value => rule.Trim = value / 100);
            var choices = Enum.GetValues<LocalToyRole>().Select(role => new RoleChoice(role, Text["Role" + role])).ToArray();
            var roles = new ComboBox { ItemsSource = choices, SelectedItem = choices.First(item => item.Role == rule.Role), MinWidth = 120 };
            roles.SelectionChanged += (_, _) => { if (roles.SelectedItem is RoleChoice choice) { rule.Role = choice.Role; Changed(); } };
            Children.Add(Row("ToyRole", roles));
            if (device.Actuators.Count == 0) return;
            var intensity = .3; var duration = 600; var selected = LocalHapticPattern.Constant;
            var graph = new ToyPatternPreview();
            void Refresh() => graph.Configure(selected, intensity, duration, options.Temperament);
            Number("Intensity", 30, 0, 100, value => { intensity = value / 100; Refresh(); });
            Number("ToyDuration", duration, 200, 10000, value => { duration = (int)value; Refresh(); });
            var patterns = Enum.GetValues<LocalHapticPattern>().Select(pattern => new PatternChoice(pattern, text["Pattern" + pattern])).ToArray();
            var patternInput = new ComboBox { ItemsSource = patterns, SelectedItem = patterns[0] };
            patternInput.SelectionChanged += (_, _) => { if (patternInput.SelectedItem is PatternChoice choice) { selected = choice.Pattern; Refresh(); } };
            Children.Add(Row("HapticPattern", patternInput)); Children.Add(new TextBlock { Text = text["ToyPatternPreview"] }); Children.Add(graph); Refresh();
            var test = new Button { Content = text["Test"], Name = "ToyDeviceTest_" + device.Provider + "_" + new string(device.Id.Where(char.IsLetterOrDigit).ToArray()) };
            test.Click += (_, _) => controller.EmitForDevice(device, intensity, TimeSpan.FromMilliseconds(duration), selected); Children.Add(OptionHelp.Wrap(test, "Test", text));
            if (requestStop is not null)
            {
                var stop = new Button { Content = text["Stop"], Name = "ToyDeviceStop_" + device.Provider + "_" + new string(device.Id.Where(char.IsLetterOrDigit).ToArray()) };
                stop.Click += (_, _) => requestStop(); Children.Add(OptionHelp.Wrap(stop, "Stop", text));
            }
        }
        private sealed record PatternChoice(LocalHapticPattern Pattern, string Label) { public override string ToString() => Label; }
    }
}
