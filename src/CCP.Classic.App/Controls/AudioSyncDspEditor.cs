using CCP.Classic.Core.LocalDevices;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public sealed class AudioSyncDspEditor : OptionEditor
{
    public AudioSyncDspEditor(AudioSyncDspOptions options, LocalizationService text, Action changed) : base(text, changed)
    {
        Heading("AudioSyncTuning"); Spacing = 7;
        Number("Sensitivity", options.Sensitivity * 100, 10, 300, value => options.Sensitivity = value / 100);
        Number("Smoothing", options.Smoothing * 100, 0, 95, value => options.Smoothing = value / 100);
        Number("BassWeight", options.BassWeight * 100, 0, 100, value => options.BassWeight = value / 100);
        Number("RmsWeight", options.RmsWeight * 100, 0, 100, value => options.RmsWeight = value / 100);
        Number("OnsetWeight", options.OnsetWeight * 100, 0, 100, value => options.OnsetWeight = value / 100);
        Number("MinimumIntensity", options.MinimumIntensity * 100, 0, 50, value => options.MinimumIntensity = value / 100);
        Number("MaximumIntensity", options.MaximumIntensity * 100, 50, 100, value => options.MaximumIntensity = value / 100);
        Number("AudioSyncDelay", options.OffsetMilliseconds, -600, 600, value => options.OffsetMilliseconds = (int)value);
        Toggle("AudioBandSplit", options.BandSplit, value => options.BandSplit = value);
        Number("AudioLiveIntensity", options.LiveIntensity * 100, 0, 100, value => options.LiveIntensity = value / 100);
    }
}
