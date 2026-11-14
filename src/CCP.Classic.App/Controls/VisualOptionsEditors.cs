using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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
        // Each box owns its own row. Rows are matched by reference, never by value: phrases are records,
        // so two blank lines compare equal, and saving normalizes the live list (drops blanks, trims text).
        var rows = options.Phrases.Select(phrase => new PhraseRow(phrase.Text, phrase.AudioPath)).ToList();
        var list = new StackPanel { Spacing = 8 }; Children.Add(list);
        void Publish()
        {
            options.Phrases = rows.Where(row => !string.IsNullOrWhiteSpace(row.Text))
                .Select(row => new SubliminalPhrase(row.Text, row.AudioPath)).ToList();
            Changed();
        }
        TextBox? Refresh()
        {
            list.Children.Clear(); TextBox? last = null;
            foreach (var item in rows.ToArray())
            {
                var editor = new TextBox { Text = item.Text, MaxLength = 2000, AcceptsReturn = true };
                editor.TextChanged += (_, _) => { item.Text = editor.Text ?? ""; Publish(); };
                var audio = new Button { Content = item.AudioPath is null ? Text["SelectAudio"] : System.IO.Path.GetFileName(item.AudioPath) };
                audio.Click += async (_, _) =>
                {
                    var path = await LocalAudioPicker.Pick(this); if (path is null || !rows.Contains(item)) return;
                    item.AudioPath = path; audio.Content = System.IO.Path.GetFileName(path); Publish();
                };
                var clear = new Button { Content = Text["ClearAudio"] };
                clear.Click += (_, _) => { item.AudioPath = null; audio.Content = Text["SelectAudio"]; Publish(); };
                var remove = new Button { Content = Text["Remove"] };
                remove.Click += (_, _) => { rows.Remove(item); Refresh(); Publish(); };
                var row = new WrapPanel(); row.Children.Add(OptionHelp.Wrap(audio, "SelectAudio", text)); row.Children.Add(OptionHelp.Wrap(clear, "ClearAudio", text)); row.Children.Add(OptionHelp.Wrap(remove, "Remove", text));
                list.Children.Add(OptionHelp.Wrap(editor, "PhrasePool", text)); list.Children.Add(row);
                last = editor;
            }
            return last;
        }
        Refresh(); var add = new Button { Content = Text["Add"] };
        add.Click += (_, _) =>
        {
            if (rows.Count >= 256) return;
            rows.Add(new PhraseRow("", null));
            if (Refresh() is not { } added) return;
            Publish();
            // The new box lands at the bottom of a long list: focus it and scroll to it once it has a layout.
            Dispatcher.UIThread.Post(() => { added.Focus(); added.BringIntoView(); }, DispatcherPriority.Loaded);
        };
        Children.Add(OptionHelp.Wrap(add, "Add", text));
    }

    private sealed class PhraseRow(string text, string? audioPath)
    {
        public string Text { get; set; } = text;
        public string? AudioPath { get; set; } = audioPath;
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
        Children.Add(label); Children.Add(OptionHelp.Wrap(pick, "SelectAudio", text)); Children.Add(OptionHelp.Wrap(clear, "ClearAudio", text));
    }
}

internal static class LocalAudioPicker
{
    public static async Task<string?> Pick(Control control)
    {
        if (TopLevel.GetTopLevel(control)?.StorageProvider is not { } provider) return null;
        var files = await provider.OpenFilePickerAsync(new() { AllowMultiple = false,
            FileTypeFilter = [new("Audio") { Patterns = LocalAudioFormats.Patterns }] });
        return SubliminalOptions.LocalAudio(files.FirstOrDefault()?.TryGetLocalPath());
    }
}
