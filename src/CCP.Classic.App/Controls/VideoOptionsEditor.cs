using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public sealed class VideoOptionsEditor : OptionEditor
{
    public VideoOptionsEditor(VideoOptions options, LocalizationService text, Action changed, Action? test = null) : base(text, changed)
    {
        Spacing = 8;
        Number("VideoFloatingSize", options.FloatingSizePercent, 15, 60, v => options.FloatingSizePercent = (int)v);
        Number("VideoFloatingSpeed", options.FloatingSpeedPercent, 10, 100, v => options.FloatingSpeedPercent = (int)v);
        Toggle("VideoTogether", options.FloatingTogether, v => options.FloatingTogether = v);
        Toggle("VideoKeepSound", options.FloatingKeepSound, v => options.FloatingKeepSound = v);
        var monitor = new ComboBox { Name = "VideoMonitor", MinWidth = 160, ItemsSource = new[] { text["VideoAllMonitors"], text["VideoFollowMonitor"] }
            .Concat(Enumerable.Range(0, 32).Select(i => (i + 1).ToString())).ToArray(), SelectedIndex = options.TargetMonitor + 2 };
        monitor.SelectionChanged += (_, _) => { options.TargetMonitor = monitor.SelectedIndex - 2; Changed(); }; Children.Add(Row("VideoMonitor", monitor));
        Number("VideoMinimum", options.MinDurationSeconds, 0, 600, v => options.MinDurationSeconds = (int)v);
        Number("VideoMaximum", options.MaxDurationSeconds, 0, 1800, v => options.MaxDurationSeconds = (int)v);
        Toggle("VideoRandomStart", options.RandomStart, v => options.RandomStart = v);
        Toggle("VideoBlurredBackground", options.BlurredBackground, v => options.BlurredBackground = v);
        Heading("VideoAttention");
        Number("VideoTargetCount", options.TargetCount, 1, 10, v => options.TargetCount = (int)v);
        Toggle("VideoRandomTargets", options.RandomTargets, v => options.RandomTargets = v);
        Number("VideoTargetLife", options.TargetLifespan, 1, 30, v => options.TargetLifespan = (int)v);
        Number("VideoTargetSize", options.TargetSize, 30, 150, v => options.TargetSize = (int)v);
        Toggle("VideoReplay", options.ReplayAfterFailedCheck, v => options.ReplayAfterFailedCheck = v);
        Toggle("VideoMercy", options.MercyEnabled, v => options.MercyEnabled = v);
        Number("VideoMercyFails", options.MercyAfterFails, 2, 10, v => options.MercyAfterFails = (int)v);
        Heading("VideoTargetStyle");
        Toggle("VideoTargetFloating", options.TargetFloatingText, v => options.TargetFloatingText = v);
        Color("VideoTargetColor1", options.TargetColor1, v => options.TargetColor1 = v);
        Color("VideoTargetColor2", options.TargetColor2, v => options.TargetColor2 = v);
        Color("VideoTargetText", options.TargetTextColor, v => options.TargetTextColor = v);
        Toggle("VideoTargetBorder", options.TargetShowBorder, v => options.TargetShowBorder = v);
        Color("VideoTargetBorderColor", options.TargetBorderColor, v => options.TargetBorderColor = v);
        var font = new ComboBox { Name = "VideoTargetFont", MinWidth = 160, MaxWidth = 220, ItemsSource = LocalFontPicker.Families, SelectedItem = options.TargetFont };
        font.SelectionChanged += (_, _) => { if (font.SelectedItem is string selected) { options.TargetFont = selected; Changed(); } }; Children.Add(Row("label_font", font));
        Heading("VideoTargetPhrases"); var pool = new StackPanel { Spacing = 5 };
        void Refresh()
        {
            pool.Children.Clear();
            foreach (var phrase in options.TargetPhrases.ToArray())
            {
                var check = new CheckBox { IsChecked = phrase.Value, Content = new TextBlock { Text = phrase.Key, TextWrapping = TextWrapping.Wrap, MaxWidth = 270 } };
                check.IsCheckedChanged += (_, _) => { options.TargetPhrases[phrase.Key] = check.IsChecked == true; Changed(); };
                var remove = new Button { Content = text["Remove"], Padding = new Thickness(8, 3) };
                remove.Click += (_, _) => { options.TargetPhrases.Remove(phrase.Key); Refresh(); Changed(); };
                var row = new DockPanel(); var removeHelp = OptionHelp.Wrap(remove, "Remove", text); DockPanel.SetDock(removeHelp, Dock.Right); row.Children.Add(removeHelp); row.Children.Add(OptionHelp.Wrap(check, "VideoTargetPhrases", text)); pool.Children.Add(row);
            }
        }
        Refresh(); Children.Add(pool);
        var input = new TextBox { Name = "VideoNewPhrase", MaxLength = 2000 }; Children.Add(OptionHelp.Wrap(input, "VideoNewPhrase", text));
        var add = new Button { Name = "VideoAddPhrase", Content = text["Add"], HorizontalAlignment = HorizontalAlignment.Right };
        add.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text) && options.TargetPhrases.Count < 500)
            { options.TargetPhrases[input.Text.Trim()] = true; input.Text = ""; Refresh(); Changed(); } }; Children.Add(OptionHelp.Wrap(add, "Add", text));
        var preview = new Button { Name = "TestVideo", Content = text["Test"], HorizontalAlignment = HorizontalAlignment.Right };
        preview.Click += (_, _) => test?.Invoke(); Children.Add(OptionHelp.Wrap(preview, "Test", text));
    }
}
