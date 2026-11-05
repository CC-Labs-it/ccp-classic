using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using CCP.Classic.App.Companion;
using CCP.Classic.Core;

namespace CCP.Classic.App;

public sealed partial class MainWindow
{
    private void FillCompanion(StackPanel content)
    {
        var avatar = Toggle(Settings.CompanionEnabled, text["Avatar"], value => { Settings.CompanionEnabled = value; effects?.RefreshSettings(); });
        avatar.Name = "CompanionAvatar"; content.Children.Add(Row("Avatar", avatar));
        var barks = Toggle(Settings.BarksEnabled, text["Barks"], value => { Settings.BarksEnabled = value; effects?.RefreshSettings(); });
        barks.Name = "CompanionBarks"; content.Children.Add(Row("Barks", barks));
        var interval = new NumericUpDown { Minimum = 1, Maximum = 3600, Value = Settings.BarkIntervalSeconds,
            Increment = 5, Width = 120, FormatString = "0", Name = "BarkInterval" };
        interval.ValueChanged += (_, _) => Settings.BarkIntervalSeconds = (int)(interval.Value ?? 60);
        content.Children.Add(Row("BarkInterval", interval));
        var more = new StackPanel { Spacing = 10 };
        content.Children.Add(new Controls.CompanionPlacementEditor(Settings.CompanionPlacement, text, () => effects?.RefreshSettings()));
        content.Children.Add(Row("TriggerPhrases", Toggle(Settings.TriggerPhrasesEnabled, text["TriggerPhrases"], value => Settings.TriggerPhrasesEnabled = value)));
        var phrases = new WrapPanel();
        foreach (var phrase in CompanionTimeline.Phrases.Where(phrase => phrase.Effect is not null))
        {
            var button = new Button { Content = phrase.Text, Margin = new Avalonia.Thickness(0, 0, 8, 0) };
            button.Click += (_, _) => effects?.TriggerPhrase(phrase.Text);
            phrases.Children.Add(Controls.OptionHelp.Wrap(button, "TriggerPhrases", text));
        }
        more.Children.Add(phrases);
        var rows = content.Children.ToArray(); content.Children.Clear(); content.Spacing = 0;
        content.Children.Add(Controls.OptionLayout.Group(rows.Take(3), text["OptionsGeneral"]));
        content.Children.Add(rows[3]);
        content.Children.Add(Controls.OptionLayout.Group(rows.Skip(4), text["TriggerPhrases"], 1));
        content.Children.Add(new Expander { Header = text["More"], Content = more });
    }
    // The tube's right-click menu: the same Start and Stop as the panel (Stop goes through the panic
    // gate, so a session with panic off refuses it), and switches that persist like Ctrl+wheel sizing.
    internal void CompanionMenuChosen(CompanionMenuCommand command)
    {
        switch (command)
        {
            case CompanionMenuCommand.StartSession: if (!Session.IsRunning) StartSession(); break;
            case CompanionMenuCommand.StopSession:
                CancelPendingStartup();
                if (!Session.Stop(finishUntilStopped: true)) SetStatus("DisabledByPanic");
                break;
            case CompanionMenuCommand.BarksChanged: CompanionSwitchesChanged(); break;
            case CompanionMenuCommand.AvatarHidden: CompanionSwitchesChanged(); SetStatus("AvatarHidden"); break;
            case CompanionMenuCommand.OpenSettings: OpenSheet("Companion"); SettingsWindow?.Activate(); break;
        }
    }

    internal CompanionSessionState CompanionMenuSession() =>
        !(Session.IsRunning || startupPending || effects?.LayeredAudio.IsActive == true) ? CompanionSessionState.Idle
            : Session.PanicEnabled ? CompanionSessionState.Running : CompanionSessionState.Locked;

    private void CompanionSwitchesChanged()
    {
        if (SettingsWindow is { } window)
            foreach (var toggle in window.GetVisualDescendants().OfType<ToggleButton>())
            {
                if (toggle.Name == "CompanionAvatar") toggle.IsChecked = Settings.CompanionEnabled;
                if (toggle.Name == "CompanionBarks") toggle.IsChecked = Settings.BarksEnabled;
            }
        effects?.RefreshSettings(); SaveSettings();
    }
}
