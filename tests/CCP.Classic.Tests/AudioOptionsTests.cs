using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CCP.Classic.App.Controls;
using CCP.Classic.App.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class AudioOptionsTests
{
    [Fact]
    public void DeviceSelectionIsAppliedOnlyWhenItChangesAndDefaultCanBeRestored()
    {
        string? previous = null; var applied = new List<string>();
        AudioOutputSelection.Apply("speaker", ref previous, applied.Add);
        AudioOutputSelection.Apply("speaker", ref previous, applied.Add);
        AudioOutputSelection.Apply("headphones", ref previous, applied.Add);
        AudioOutputSelection.Apply("", ref previous, applied.Add);
        Assert.Equal(new[] { "speaker", "headphones", "" }, applied);
        var options = new AudioOptions { OutputDeviceId = "bad\nidentity" }; options.Normalize(); Assert.Empty(options.OutputDeviceId);
    }

    [Fact]
    public Task EditorEnumeratesActualChoicesAndTestHonoursGainAndTeardown() => UiDispatcher.Run(() =>
    {
        var options = new AudioOptions(); var audio = new Playback(); var gain = 0d; var saved = 0;
        using var editor = new AudioOptionsEditor(options, new("en"), () => saved++, () => gain, audio,
            _ => Task.FromResult<IReadOnlyList<LocalAudioOutput>>([new("speaker", "Speakers"), new("phones", "Headphones")]));
        editor.RefreshAsync().GetAwaiter().GetResult();
        var devices = Assert.Single(editor.GetLogicalDescendants().OfType<ComboBox>(), control => control.Name == "AudioOutput");
        Assert.Equal(3, devices.ItemCount); devices.SelectedIndex = 2; Assert.Equal("phones", options.OutputDeviceId); Assert.Equal(1, saved);
        var test = Assert.Single(editor.GetLogicalDescendants().OfType<Button>(), button => button.Name == "TestAudio");
        test.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Empty(audio.Paths);
        gain = .25; test.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.EndsWith("relax.mp3", Assert.Single(audio.Paths)); Assert.Equal(.25, audio.Gain);
        editor.StopTest(); Assert.Equal(0, audio.Gain); editor.Dispose(); Assert.True(audio.Disposed);
        AudioOutputSelection.Configure(new());
    });

    [Fact]
    public async Task LateEnumerationCannotReopenADisposedEditor()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<LocalAudioOutput>>(TaskCreationOptions.RunContinuationsAsynchronously);
        AudioOptionsEditor? editor = null; Task refresh = Task.CompletedTask;
        await UiDispatcher.Run(() =>
        {
            editor = new(new(), new("en"), () => { }, () => 0, new Playback(), _ => completion.Task);
            refresh = editor.RefreshAsync(); editor.Dispose();
        });
        completion.SetResult([new("late", "Late")]);
        await refresh.WaitAsync(TestContext.Current.CancellationToken);
        await UiDispatcher.Run(() => Assert.Equal(0, Assert.Single(editor!.GetLogicalDescendants().OfType<ComboBox>(), control => control.Name == "AudioOutput").ItemCount));
    }

    [Fact]
    public Task NativeEnumerationFailureUsesTheExistingLocalizedUnavailableMessage() => UiDispatcher.Run(() =>
    {
        var text = new LocalizationService("it");
        using var editor = new AudioOptionsEditor(new(), text, () => { }, () => 0, new Playback(),
            _ => Task.FromException<IReadOnlyList<LocalAudioOutput>>(new IOException("Native audio enumeration failed")));
        editor.RefreshAsync().GetAwaiter().GetResult();
        Assert.Contains(editor.GetLogicalDescendants().OfType<TextBlock>(), label => label.Text == text["AudioOutputUnavailable"]);
    });

    private sealed class Playback : IAudioPlayback
    {
        public readonly List<string> Paths = []; public double Gain; public bool Disposed;
        public bool Play(string localPath, double gain) { Paths.Add(localPath); return true; }
        public void SetMasterGain(double gain) => Gain = gain;
        public void StopAll() { }
        public void Dispose() => Disposed = true;
    }
}
