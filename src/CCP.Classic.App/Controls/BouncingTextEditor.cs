using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public sealed class BouncingTextEditor : OptionEditor
{
    public BouncingTextEditor(ClassicSettings settings, LocalizationService text, Action changed) : base(text, changed)
    {
        Spacing = 8; var options = settings.BouncingText;
        var colorRow = new StackPanel { Spacing = 4, IsVisible = options.ColorMode == 1 };
        var modes = new ComboBox { Name = "BounceColorMode", ItemsSource = new[] { text["option_color_random"],
            text["option_color_fixed"], text["option_color_rainbow"] }, SelectedIndex = options.ColorMode, MinWidth = 150 };
        modes.SelectionChanged += (_, _) => { options.ColorMode = Math.Max(0, modes.SelectedIndex);
            colorRow.IsVisible = options.ColorMode == 1; Changed(); };
        Children.Add(Row("setting_color_mode", modes));
        var firstColorChild = Children.Count;
        Color("setting_text_color", options.FixedColor, value => options.FixedColor = value);
        var colorControl = Children[firstColorChild]; Children.Remove(colorControl); colorRow.Children.Add(colorControl);
        var reset = new Button { Content = text["btn_reset"], HorizontalAlignment = HorizontalAlignment.Right };
        reset.Click += (_, _) => { options.FixedColor = "#FF69B4";
            colorControl.GetLogicalDescendants().OfType<TextBox>().Single().Text = options.FixedColor; Changed(); };
        colorRow.Children.Add(reset); Children.Add(colorRow);
        var fonts = LocalFontPicker.Families.Concat([options.FontFamily]).Distinct().ToArray();
        var font = new ComboBox { Name = "BounceFont", ItemsSource = fonts, SelectedItem = options.FontFamily,
            MaxWidth = 220, MinWidth = 150 };
        font.SelectionChanged += (_, _) => { if (font.SelectedItem is string family) { options.FontFamily = family; Changed(); } };
        Children.Add(Row("label_font", font));
        Toggle("setting_second_text", options.SecondText, value => options.SecondText = value);
        Toggle("setting_show_over_videos", options.ShowOverVideos, value => options.ShowOverVideos = value);
        Heading("label_effects");
        Toggle("setting_fx_breathing", options.Breathing, value => options.Breathing = value);
        Toggle("setting_fx_wobble", options.Wobble, value => options.Wobble = value);
        Toggle("setting_fx_spin", options.Spin, value => options.Spin = value);
        Toggle("setting_fx_velocity_tilt", options.VelocityTilt, value => options.VelocityTilt = value);
        Toggle("setting_fx_squash_stretch", options.SquashStretch, value => options.SquashStretch = value);
        Toggle("setting_fx_corner_burst", options.CornerBurst, value => options.CornerBurst = value);
        Toggle("setting_fx_outline", options.Outline, value => options.Outline = value);
        Heading("btn_edit_phrases"); var pool = new StackPanel { Spacing = 5 };
        void Refresh()
        {
            pool.Children.Clear();
            foreach (var phrase in options.PhrasePool.ToArray())
            {
                var enabled = new CheckBox { IsChecked = phrase.Value, Content = new TextBlock { Text = phrase.Key,
                    TextWrapping = TextWrapping.Wrap, MaxWidth = 280 } };
                enabled.IsCheckedChanged += (_, _) => { options.PhrasePool[phrase.Key] = enabled.IsChecked == true; Changed(); };
                var remove = new Button { Content = text["Remove"], Padding = new Thickness(8, 3) };
                remove.Click += (_, _) => { options.PhrasePool.Remove(phrase.Key); Refresh(); Changed(); };
                var row = new DockPanel(); DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove); row.Children.Add(enabled);
                pool.Children.Add(row);
            }
        }
        Refresh(); Children.Add(pool);
        var input = new TextBox { Name = "BounceNewPhrase", MaxLength = 2000, MinHeight = 40 };
        Children.Add(input); var add = new Button { Name = "BounceAddPhrase", Content = text["Add"], HorizontalAlignment = HorizontalAlignment.Right };
        add.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text) && options.PhrasePool.Count < 500)
            { options.PhrasePool[input.Text.Trim()] = true; input.Text = ""; Refresh(); Changed(); } };
        Children.Add(add);
    }
}
