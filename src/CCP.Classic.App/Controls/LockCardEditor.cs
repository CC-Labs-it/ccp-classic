using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public sealed class LockCardEditor : OptionEditor
{
    public LockCardEditor(ClassicSettings settings, LocalizationService text, Action changed, Action test) : base(text, changed)
    {
        Spacing = 8; var options = settings.LockCard;
        Toggle("LockRandomRepeats", options.RandomRepeats, value => options.RandomRepeats = value);
        Number("LockMinimumRepeats", options.MinimumRepeats, 1, 10, value => options.MinimumRepeats = (int)value);
        Toggle("LockTargetEnabled", options.TargetLengthEnabled, value => options.TargetLengthEnabled = value);
        Number("LockTargetLength", options.TargetLength, 20, 600, value => options.TargetLength = (int)value);
        Number("LockTargetVariance", options.TargetVariance, 0, 200, value => options.TargetVariance = (int)value);
        Toggle("LockResetTypo", options.ResetOnTypo, value => options.ResetOnTypo = value);
        Toggle("LockStrictCopy", options.StrictCopy, value => options.StrictCopy = value);
        Heading("LockPhrases");
        var pool = new StackPanel { Spacing = 5 };
        void Refresh()
        {
            pool.Children.Clear();
            foreach (var phrase in options.Phrases.ToArray())
            {
                var enabled = new CheckBox { IsChecked = phrase.Value, Content = new TextBlock { Text = phrase.Key,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 280 } };
                enabled.IsCheckedChanged += (_, _) => { options.Phrases[phrase.Key] = enabled.IsChecked == true; Changed(); };
                var remove = new Button { Content = Text["Remove"], Padding = new Thickness(8, 3) };
                remove.Click += (_, _) => { options.Phrases.Remove(phrase.Key); Refresh(); Changed(); };
                var row = new DockPanel(); DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove); row.Children.Add(enabled);
                pool.Children.Add(row);
            }
        }
        Refresh(); Children.Add(pool);
        var input = new TextBox { MaxLength = 2000, PlaceholderText = Text["LockNewPhrase"], AcceptsReturn = true, MinHeight = 65 };
        Children.Add(input);
        var add = new Button { Content = Text["Add"], HorizontalAlignment = HorizontalAlignment.Right };
        add.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(input.Text) && options.Phrases.Count < 500)
            { options.Phrases[input.Text.Trim()] = true; input.Text = ""; Refresh(); Changed(); }
        };
        Children.Add(add); Heading("LockColors");
        Color("LockBackground", options.Background, value => options.Background = value);
        Color("TextColour", options.TextColor, value => options.TextColor = value);
        Color("LockInputBackground", options.InputBackground, value => options.InputBackground = value);
        Color("LockInputText", options.InputText, value => options.InputText = value);
        Color("LockAccent", options.Accent, value => options.Accent = value);
        var preview = new Button { Content = Text["Test"], HorizontalAlignment = HorizontalAlignment.Right };
        preview.Click += (_, _) => test(); Children.Add(preview);
    }
}
