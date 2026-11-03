using CCP.Classic.App.Controls;
using CCP.Classic.Core;

namespace CCP.Classic.App;

public sealed partial class MainWindow
{
    private void OpenLoom()
    {
        var editor = new LoomEditor(Settings, text, new LoomStore(LoomStore.DefaultDirectory));
        editor.SavedSelection += saved =>
        {
            Settings.CustomSpiralId = saved.Id;
            Settings.Effects["spiral"].Enabled = true;
            effects?.ReloadCustomSpiral(); SaveSettings();
        };
        editor.Deleted += id =>
        {
            if (Settings.CustomSpiralId != id) return;
            Settings.CustomSpiralId = null; effects?.ReloadCustomSpiral(); SaveSettings();
        };
        editor.StatusChanged += SetStatus;
        ShowSettingsWindow("MakeYourOwn", editor);
        SettingsWindow!.Width = Math.Min(780, SettingsWindow.MaxWidth);
        SettingsWindow.Closed += (_, _) => editor.Dispose();
    }
}
