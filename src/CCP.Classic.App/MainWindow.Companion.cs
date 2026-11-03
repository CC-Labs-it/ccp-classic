using Avalonia.Controls;
using CCP.Classic.Core;

namespace CCP.Classic.App;

public sealed partial class MainWindow
{
    private void FillCompanion(StackPanel content)
    {
        content.Children.Add(Row("Avatar", Toggle(Settings.CompanionEnabled, text["Avatar"], value => Settings.CompanionEnabled = value)));
        content.Children.Add(Row("Barks", Toggle(Settings.BarksEnabled, text["Barks"], value => Settings.BarksEnabled = value)));
        var interval = new NumericUpDown { Minimum = 1, Maximum = 3600, Value = Settings.BarkIntervalSeconds,
            Increment = 5, Width = 120, FormatString = "0", Name = "BarkInterval" };
        interval.ValueChanged += (_, _) => Settings.BarkIntervalSeconds = (int)(interval.Value ?? 60);
        content.Children.Add(Row("BarkInterval", interval));
        var more = new StackPanel { Spacing = 10 };
        more.Children.Add(Row("TriggerPhrases", Toggle(Settings.TriggerPhrasesEnabled, text["TriggerPhrases"], value => Settings.TriggerPhrasesEnabled = value)));
        more.Children.Add(Row("KeywordTriggers", Toggle(Settings.KeywordTriggersEnabled, text["KeywordTriggers"], value => Settings.KeywordTriggersEnabled = value)));
        var phrases = new WrapPanel();
        foreach (var phrase in CompanionTimeline.Phrases.Where(phrase => phrase.Effect is not null))
        {
            var button = new Button { Content = phrase.Text, Margin = new Avalonia.Thickness(0, 0, 8, 0) };
            button.Click += (_, _) => effects?.TriggerPhrase(phrase.Text);
            phrases.Children.Add(button);
        }
        more.Children.Add(phrases);
        content.Children.Add(new Expander { Header = text["More"], Content = more });
    }
}
