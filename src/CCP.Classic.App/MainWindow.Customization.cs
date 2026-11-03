using Avalonia.Controls;
using CCP.Classic.App.Controls;
using CCP.Classic.App.Media;

namespace CCP.Classic.App;

public sealed partial class MainWindow
{
    private Control? EffectCustomization(string id) => id switch
    {
        "flash" => new FlashOptionsEditor(Settings, text, () => effects?.RefreshSettings()),
        "video" => new VideoOptionsEditor(Settings.Video, text, () => effects?.RefreshSettings(), () => effects?.PreviewVideo()),
        "spiral" => SpiralCustomization(),
        "audio" => AudioCustomization(),
        "wall" => new WallpaperOptionsEditor(Settings.Wallpaper, text, () => effects?.RefreshSettings(), () => effects?.ShuffleWallpaper()),
        "bounce" => new BouncingTextEditor(Settings, text, () => effects?.RefreshSettings()),
        "drain" => BrainDrainCustomization(),
        "bubble" => new BubbleOptionsEditor(Settings.Bubbles, text, () => effects?.RefreshSettings()),
        "sub" => new SubliminalOptionsEditor(Settings.Subliminal, text, () => effects?.RefreshSettings()),
        "pink" => new PinkFilterOptionsEditor(Settings.PinkFilter, text, () => effects?.RefreshSettings()),
        "wipe" => new MindWipeOptionsEditor(Settings.MindWipe, text, () => effects?.RefreshSettings()),
        "lock" => new LockCardEditor(Settings, text, () => effects?.RefreshSettings(), () => effects?.PreviewLockCard()),
        _ => null
    };
    private Control BrainDrainCustomization()
    {
        var editor = new BrainDrainEditor(Settings, text, () => effects?.RefreshSettings());
        editor.RefreshRequested += () => effects?.ReloadBrainDrainAudio(); return editor;
    }
    private Control SpiralCustomization()
    {
        var editor = new SpiralOptionsEditor(Settings, text, () => effects?.RefreshSettings());
        editor.ReloadRequested += () => effects?.ReloadSpiralMedia(); return editor;
    }
    private Control AudioCustomization()
    {
        var editor = new AudioOptionsEditor(Settings.Audio, text, () =>
        { AudioOutputSelection.Configure(Settings.Audio); effects?.RefreshSettings(); },
            () => Settings.Effects["audio"].Enabled ? Settings.Effects["audio"].Values["master"] / 100 : 0);
        Session.StopRequested += editor.StopTest;
        editor.DetachedFromVisualTree += (_, _) => { Session.StopRequested -= editor.StopTest; editor.Dispose(); };
        var layers = new LayeredAudioEditor(Settings.LayeredAudio, text, () => effects?.RefreshSettings(),
            () => { if (effects is not null) _ = effects.ApplyLayeredAudioAsync(); },
            () => effects?.RefreshSettings(), () => effects?.LayeredAudioStatus);
        if (effects is not null)
        {
            effects.LayeredAudio.StateChanged += layers.RefreshStatus;
            layers.DetachedFromVisualTree += (_, _) => effects.LayeredAudio.StateChanged -= layers.RefreshStatus;
        }
        return new StackPanel { Spacing = 14, Children = { editor, layers } };
    }
}
