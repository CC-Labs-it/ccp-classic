using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public sealed class SubliminalOptionsEditor : OptionEditor
{
    public SubliminalOptionsEditor(SubliminalOptions options, LocalizationService text, Action changed) : base(text, changed)
    {
        Spacing = 8;
        var font = new ComboBox { ItemsSource = LocalFontPicker.Families, SelectedItem = options.FontFamily, MinWidth = 170 };
        font.SelectionChanged += (_, _) => { if (font.SelectedItem is string value) { options.FontFamily = value; Changed(); } };
        Children.Add(Row("Font", font));
        Number("WhisperVolume", options.WhisperVolume * 100, 0, 100, v => options.WhisperVolume = v / 100);
        Heading("PhrasePool");
        var list = new StackPanel { Spacing = 8 }; Children.Add(list);
        void Refresh()
        {
            list.Children.Clear();
            foreach (var phrase in options.Phrases.ToArray())
            {
                var editor = new TextBox { Text = phrase.Text, MaxLength = 2000, AcceptsReturn = true };
                var captured = phrase;
                editor.TextChanged += (_, _) =>
                {
                    var index = options.Phrases.IndexOf(captured);
                    if (index < 0) return;
                    captured = captured with { Text = editor.Text ?? "" };
                    var next = options.Phrases.ToList(); next[index] = captured; options.Phrases = next; Changed();
                };
                var audio = new Button { Content = phrase.AudioPath is null ? Text["SelectAudio"] : System.IO.Path.GetFileName(phrase.AudioPath) };
                audio.Click += async (_, _) =>
                {
                    var path = await LocalAudioPicker.Pick(this); if (path is null) return;
                    var index = options.Phrases.IndexOf(captured); if (index < 0) return;
                    captured = captured with { AudioPath = path }; var next = options.Phrases.ToList(); next[index] = captured;
                    options.Phrases = next; audio.Content = System.IO.Path.GetFileName(path); Changed();
                };
                var clear = new Button { Content = Text["ClearAudio"] };
                clear.Click += (_, _) =>
                {
                    var index = options.Phrases.IndexOf(captured); if (index < 0) return;
                    captured = captured with { AudioPath = null }; var next = options.Phrases.ToList(); next[index] = captured;
                    options.Phrases = next; audio.Content = Text["SelectAudio"]; Changed();
                };
                var remove = new Button { Content = Text["Remove"] };
                remove.Click += (_, _) => { options.Phrases = options.Phrases.Where(p => !ReferenceEquals(p, captured)).ToList(); Refresh(); Changed(); };
                var row = new WrapPanel(); row.Children.Add(audio); row.Children.Add(clear); row.Children.Add(remove);
                list.Children.Add(editor); list.Children.Add(row);
            }
        }
        Refresh(); var add = new Button { Content = Text["Add"] };
        add.Click += (_, _) => { if (options.Phrases.Count < 256) { options.Phrases = [..options.Phrases, new("")]; Refresh(); Changed(); } };
        Children.Add(add);
    }
}

public sealed class PinkFilterOptionsEditor : OptionEditor
{
    public PinkFilterOptionsEditor(PinkFilterOptions options, LocalizationService text, Action changed) : base(text, changed)
    {
        Spacing = 8; Color("FilterColour", options.Colour, v => options.Colour = v);
        Toggle("UnderEffects", options.UnderEffects, v => options.UnderEffects = v);
        var monitors = new ComboBox { MinWidth = 170 };
        void Populate()
        {
            var count = TopLevel.GetTopLevel(this)?.Screens?.All.Count ?? 1;
            monitors.ItemsSource = new[] { Text["FollowDisplay"] }.Concat(Enumerable.Range(1, count).Select(i => Text["Monitor"] + " " + i)).ToArray();
            monitors.SelectedIndex = Math.Clamp(options.Monitor + 1, 0, count);
        }
        Populate(); AttachedToVisualTree += (_, _) => Populate();
        monitors.SelectionChanged += (_, _) => { if (monitors.SelectedIndex >= 0) { options.Monitor = monitors.SelectedIndex - 1; Changed(); } };
        Children.Add(Row("TargetMonitor", monitors));
    }
}

public sealed class MindWipeOptionsEditor : OptionEditor
{
    public MindWipeOptionsEditor(MindWipeOptions options, LocalizationService text, Action changed) : base(text, changed)
    {
        Spacing = 8; Heading("MindWipeAudio");
        var label = new TextBlock { Text = options.AudioPath is null ? Text["DefaultAudio"] : System.IO.Path.GetFileName(options.AudioPath) };
        var pick = new Button { Content = Text["SelectAudio"] };
        pick.Click += async (_, _) =>
        {
            var path = await LocalAudioPicker.Pick(this); if (path is null) return;
            options.AudioPath = path; label.Text = System.IO.Path.GetFileName(path); Changed();
        };
        var clear = new Button { Content = Text["Reset"] };
        clear.Click += (_, _) => { options.AudioPath = null; label.Text = Text["DefaultAudio"]; Changed(); };
        Children.Add(label); Children.Add(pick); Children.Add(clear);
    }
}

internal static class LocalAudioPicker
{
    public static async Task<string?> Pick(Control control)
    {
        if (TopLevel.GetTopLevel(control)?.StorageProvider is not { } provider) return null;
        var files = await provider.OpenFilePickerAsync(new() { AllowMultiple = false,
            FileTypeFilter = [new("Audio") { Patterns = ["*.mp3", "*.wav", "*.ogg", "*.flac", "*.m4a", "*.aac"] }] });
        return SubliminalOptions.LocalAudio(files.FirstOrDefault()?.TryGetLocalPath());
    }
}
