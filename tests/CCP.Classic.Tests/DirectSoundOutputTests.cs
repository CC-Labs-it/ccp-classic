using System.Runtime.InteropServices;
using CCP.Classic.App.Media;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class DirectSoundOutputTests
{
    [Fact]
    public void SelectedAndDefaultDevicesAreStoredBeforePlaybackAndAppliedToTheActiveOutput()
    {
        var calls = new List<(string? Module, string Id)>();
        AudioOutputSelection.SetNativeDevice(true, "{B6EC7D2E-166D-4A78-A2B8-3E43B2DBA612}", (module, id) => calls.Add((module, id)));
        AudioOutputSelection.SetNativeDevice(true, "", (module, id) => calls.Add((module, id)));
        Assert.Equal(new (string?, string)[] { ("directx", "{B6EC7D2E-166D-4A78-A2B8-3E43B2DBA612}"),
            (null, "{B6EC7D2E-166D-4A78-A2B8-3E43B2DBA612}"), ("directx", ""), (null, "") }, calls);
        // PulseAudio refuses "" as a sink name and the stream ends up silent, so Linux never sends it.
        calls.Clear(); AudioOutputSelection.SetNativeDevice(false, "", (module, id) => calls.Add((module, id)));
        Assert.Empty(calls);
        AudioOutputSelection.SetNativeDevice(false, "alsa_output.pci-0000_00_1f.3.analog-stereo", (module, id) => calls.Add((module, id)));
        Assert.Equal((null, "alsa_output.pci-0000_00_1f.3.analog-stereo"), Assert.Single(calls));
    }
    [Fact]
    public void RealGuidIdentifiersMatchVlcAndUnicodeNamesArePreservedWithoutDefaultDuplicates()
    {
        var speaker = new Guid("b6ec7d2e-166d-4a78-a2b8-3e43b2dba612");
        var headphones = new Guid("51bdc8f7-bfa3-4fd7-a474-23863f90e822");
        var outputs = AudioOutputSelection.EnumerateDirectSound(visit =>
        {
            Assert.True(visit(null, "Primary Sound Driver")); Assert.True(visit(speaker, "Speakers"));
            Assert.True(visit(speaker, "Duplicate")); Assert.True(visit(headphones, "ヘッドホン\n")); return 0;
        }, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { new LocalAudioOutput("{B6EC7D2E-166D-4A78-A2B8-3E43B2DBA612}", "Speakers"),
            new LocalAudioOutput("{51BDC8F7-BFA3-4FD7-A474-23863F90E822}", "ヘッドホン") }, outputs);
    }
    [Fact]
    public void EnumerationIsBoundedAndDoesNotInventAnOutputWhenTheSystemHasNone()
    {
        var outputs = AudioOutputSelection.EnumerateDirectSound(visit =>
        {
            for (var index = 0; index < 1000; index++) if (!visit(Guid.NewGuid(), "Native device")) break;
            return 0;
        }, TestContext.Current.CancellationToken);
        Assert.Equal(128, outputs.Count);
        Assert.Empty(AudioOutputSelection.EnumerateDirectSound(visit => { visit(null, "Default"); return 0; }, TestContext.Current.CancellationToken));
    }
    [Fact]
    public void NativeFailureAndCancellationStayVisibleToTheExistingLocalizedUnavailablePath()
    {
        var failure = Assert.Throws<IOException>(() => AudioOutputSelection.EnumerateDirectSound(_ => unchecked((int)0x88780078), TestContext.Current.CancellationToken));
        Assert.IsType<COMException>(failure.InnerException);
        using var stop = new CancellationTokenSource(); var calls = 0;
        Assert.Throws<OperationCanceledException>(() => AudioOutputSelection.EnumerateDirectSound(visit =>
        { calls++; stop.Cancel(); Assert.False(visit(Guid.NewGuid(), "Device")); return 0; }, stop.Token));
        Assert.Equal(1, calls);
        Assert.Throws<OperationCanceledException>(() => AudioOutputSelection.EnumerateDirectSound(_ => { calls++; return 0; }, stop.Token));
        Assert.Equal(1, calls);
    }
    [Fact]
    public void LinuxOutputsComeFromThePlayerListAndStaySelectable()
    {
        var token = TestContext.Current.CancellationToken;
        var outputs = AudioOutputSelection.FromVlcDevices(new (string?, string?)[]
        {
            ("", "Default"), (null, "Missing"), ("alsa_output.pci-0000_00_1f.3.analog-stereo", "Built-in Audio\n"),
            ("alsa_output.pci-0000_00_1f.3.analog-stereo", "Duplicate"), ("bluez_output.AA_BB", ""),
            ("bad\nid", "Control"), (new string('x', 1025), "Too long"), ("ヘッドホン", "ヘッドホン")
        }, token);
        Assert.Equal(new[] { new LocalAudioOutput("alsa_output.pci-0000_00_1f.3.analog-stereo", "Built-in Audio"),
            new LocalAudioOutput("bluez_output.AA_BB", "bluez_output.AA_BB"), new LocalAudioOutput("ヘッドホン", "ヘッドホン") }, outputs);
        // Every listed id survives Configure, so picking it never silently falls back to System default.
        Assert.All(outputs, output => Assert.Equal(output.Id, AudioOutputSelection.Sanitize(output.Id)));
        Assert.Equal(128, AudioOutputSelection.FromVlcDevices(Enumerable.Range(0, 1000).Select(index => ((string?)$"sink{index}", (string?)"Sink")), token).Count);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => AudioOutputSelection.FromVlcDevices(new (string?, string?)[] { ("sink", "Sink") }, stop.Token));
    }
}
