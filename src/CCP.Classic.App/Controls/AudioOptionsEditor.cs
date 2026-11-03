using Avalonia.Controls;
using Avalonia.Layout;
using CCP.Classic.App.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public sealed class AudioOptionsEditor : OptionEditor, IDisposable
{
    private readonly AudioOptions options;
    private readonly IAudioPlayback playback;
    private readonly Func<CancellationToken, Task<IReadOnlyList<LocalAudioOutput>>> enumerate;
    private readonly ComboBox outputs = new() { MinWidth = 170, MaxWidth = 300, Name = "AudioOutput" };
    private readonly TextBlock status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly CancellationTokenSource closed = new();
    private bool binding, disposed;

    public AudioOptionsEditor(AudioOptions options, LocalizationService text, Action changed, Func<double>? testGain = null)
        : this(options, text, changed, testGain ?? (() => 0), new LibVlcAudioPlayback(), AudioOutputSelection.EnumerateAsync) { }
    internal AudioOptionsEditor(AudioOptions options, LocalizationService text, Action changed, Func<double> testGain,
        IAudioPlayback playback, Func<CancellationToken, Task<IReadOnlyList<LocalAudioOutput>>> enumerate) : base(text, changed)
    {
        this.options = options; this.playback = playback; this.enumerate = enumerate; Spacing = 8;
        Heading("AudioOutput"); Children.Add(OptionHelp.Wrap(outputs, "AudioOutput", text));
        outputs.SelectionChanged += (_, _) =>
        {
            if (binding || outputs.SelectedItem is not OutputChoice choice) return;
            options.OutputDeviceId = choice.Id; AudioOutputSelection.Configure(options); StopTest(); Changed();
        };
        var refresh = new Button { Content = Text["RefreshOutputs"], Name = "RefreshOutputs" };
        refresh.Click += (_, _) => _ = RefreshAsync();
        var test = new Button { Content = Text["TestAudio"], Name = "TestAudio" };
        test.Click += (_, _) =>
        {
            StopTest(); var gain = testGain();
            if (double.IsFinite(gain) && gain > 0 && new StarterPack().Phrases[2].AudioPath is { } clip)
            { AudioOutputSelection.Configure(options); playback.SetMasterGain(Math.Clamp(gain, 0, 1)); playback.Play(clip, 1); }
        };
        var stop = new Button { Content = Text["StopAudioTest"], Name = "StopAudioTest" }; stop.Click += (_, _) => StopTest();
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(OptionHelp.Wrap(refresh, "RefreshOutputs", text)); actions.Children.Add(OptionHelp.Wrap(test, "TestAudio", text)); actions.Children.Add(OptionHelp.Wrap(stop, "StopAudioTest", text)); Children.Add(actions); Children.Add(status);
        Toggle("AudioDucking", options.DuckingEnabled, value => options.DuckingEnabled = value);
        AttachedToVisualTree += (_, _) => _ = RefreshAsync(); DetachedFromVisualTree += (_, _) => Dispose();
    }
    internal async Task RefreshAsync()
    {
        if (disposed) return;
        try
        {
            var items = await enumerate(closed.Token);
            if (disposed) return;
            binding = true;
            var list = new[] { new OutputChoice("", Text["SystemDefault"]) }.Concat(items.Select(item => new OutputChoice(item.Id, item.Name))).ToList();
            if (!list.Any(item => item.Id == options.OutputDeviceId)) list.Add(new(options.OutputDeviceId, Text["AudioOutputUnavailable"]));
            outputs.ItemsSource = list; outputs.SelectedItem = list.First(item => item.Id == options.OutputDeviceId); status.Text = "";
        }
        catch (OperationCanceledException) when (closed.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or InvalidOperationException or DllNotFoundException or TypeInitializationException or BadImageFormatException)
        { if (!disposed) status.Text = Text["AudioOutputUnavailable"]; }
        finally { binding = false; }
    }
    public void StopTest() { if (!disposed) { playback.SetMasterGain(0); playback.StopAll(); } }
    public void Dispose() { if (disposed) return; StopTest(); disposed = true; closed.Cancel(); closed.Dispose(); playback.Dispose(); }
    private sealed record OutputChoice(string Id, string Name) { public override string ToString() => Name; }
}
