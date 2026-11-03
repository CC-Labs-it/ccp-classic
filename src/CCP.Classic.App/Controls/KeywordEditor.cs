using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public sealed class KeywordEditor : OptionEditor
{
    private readonly KeywordOptions options;
    private readonly ComboBox selection = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel detail = new() { Spacing = 8 };
    private readonly Func<KeywordDefinition, bool>? test;

    public KeywordEditor(KeywordOptions options, LocalizationService text, Action changed,
        Func<IEnumerable<SubliminalPhrase>>? import = null, Func<KeywordDefinition, bool>? test = null) : base(text, changed)
    {
        this.options = options; this.test = test; Spacing = 8;
        Heading("KeywordTriggers");
        Number("KeywordBuffer", options.BufferTimeoutMs, 1000, 10000, v => options.BufferTimeoutMs = (int)v);
        Number("KeywordGlobalCooldown", options.GlobalCooldownSeconds, 1, 3600, v => options.GlobalCooldownSeconds = (int)v);
        Number("KeywordSameCooldown", options.SameWordCooldownSeconds, 1, 3600, v => options.SameWordCooldownSeconds = (int)v);
        var multiplier = Number("KeywordSessionXp", options.SessionXpMultiplier, 1, 3, v => options.SessionXpMultiplier = v); multiplier.Increment = .1m;
        Toggle("KeywordIgnoreOwn", options.IgnoreOwnFocus, v => options.IgnoreOwnFocus = v);
        Toggle("KeywordLoopProtection", options.LoopProtection, v => options.LoopProtection = v);
        Number("KeywordLoopTime", options.LoopProtectionMs, 500, 30000, v => options.LoopProtectionMs = (int)v);
        var scope = new ComboBox { ItemsSource = Enum.GetValues<KeywordAppScope>().Select(v => Text["Keyword" + v]).ToArray(), SelectedIndex = (int)options.AppScope };
        scope.SelectionChanged += (_, _) => { if (scope.SelectedIndex >= 0) { options.AppScope = (KeywordAppScope)scope.SelectedIndex; Changed(); } };
        Children.Add(Row("KeywordAppScope", scope));
        String("KeywordApps", string.Join(", ", options.Apps), v => options.Apps = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(64).ToList());
        Children.Add(selection); Children.Add(detail);
        selection.SelectionChanged += (_, _) => ShowSelected();
        var add = new Button { Content = Text["Add"], HorizontalAlignment = HorizontalAlignment.Right };
        add.Click += (_, _) =>
        {
            if (options.Triggers.Count >= 128) return;
            options.Triggers.Add(new()); Reload(options.Triggers.Count - 1); Changed();
        };
        Children.Add(add); Reload(0);
        if (import is not null)
        {
            var importButton = new Button { Content = Text["KeywordImportPhrases"] };
            importButton.Click += (_, _) => { options.ImportPhrases(import()); Reload(0); Changed(); };
            Children.Add(importButton);
        }
    }

    private void Reload(int index)
    {
        selection.ItemsSource = options.Triggers.Select((t, i) => string.IsNullOrEmpty(t.Keyword) ? Text["KeywordNew"] + " " + (i + 1) : t.Keyword).ToArray();
        selection.SelectedIndex = Math.Clamp(index, -1, options.Triggers.Count - 1);
        ShowSelected();
    }
    private void ShowSelected()
    {
        detail.Children.Clear();
        var index = selection.SelectedIndex;
        if (index < 0 || index >= options.Triggers.Count) return;
        var trigger = options.Triggers[index];
        var editor = new DefinitionEditor(trigger, Text, Changed, () => { options.Triggers.Remove(trigger); Reload(0); Changed(); });
        detail.Children.Add(editor);
        if (test is not null)
        {
            var result = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            var button = new Button { Content = Text["Test"] };
            button.Click += (_, _) => result.Text = test(trigger) ? Text["Running"] : Text["KeywordStartToTest"];
            detail.Children.Add(button); detail.Children.Add(result);
        }
    }

    private sealed class DefinitionEditor : OptionEditor
    {
        public DefinitionEditor(KeywordDefinition trigger, LocalizationService text, Action changed, Action remove) : base(text, changed)
        {
            Spacing = 8;
            Toggle("Enabled", trigger.Enabled, v => trigger.Enabled = v);
            String("KeywordText", trigger.Keyword, v => trigger.Keyword = v[..Math.Min(v.Length, 256)]);
            Choice("KeywordMatch", Enum.GetValues<KeywordMatch>(), trigger.Match, v => trigger.Match = v);
            Number("KeywordCooldown", trigger.CooldownSeconds, 1, 3600, v => trigger.CooldownSeconds = (int)v);
            Choice("KeywordAppScope", Enum.GetValues<KeywordAppScope>(), trigger.AppScope, v => trigger.AppScope = v);
            String("KeywordApps", string.Join(", ", trigger.Apps), v => trigger.Apps = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(64).ToList());
            Heading("KeywordActions");
            var list = new StackPanel { Spacing = 8 }; Children.Add(list);
            void Refresh()
            {
                list.Children.Clear();
                foreach (var action in trigger.Actions.ToArray())
                    list.Children.Add(new ActionEditor(action, text, changed, () => { trigger.Actions.Remove(action); Refresh(); Changed(); }));
            }
            Refresh();
            var add = new Button { Content = Text["KeywordAddAction"] };
            add.Click += (_, _) => { if (trigger.Actions.Count < 16) { trigger.Actions.Add(new()); Refresh(); Changed(); } };
            Children.Add(add);
            var delete = new Button { Content = Text["Remove"], HorizontalAlignment = HorizontalAlignment.Right };
            delete.Click += (_, _) => remove(); Children.Add(delete);
        }
        private void Choice<T>(string key, T[] values, T selected, Action<T> set) where T : struct, Enum
        {
            var choice = new ComboBox { ItemsSource = values.Select(v => Text["Keyword" + v]).ToArray(), SelectedIndex = Array.IndexOf(values, selected), MinWidth = 170 };
            choice.SelectionChanged += (_, _) => { if (choice.SelectedIndex >= 0) { set(values[choice.SelectedIndex]); Changed(); } };
            Children.Add(Row(key, choice));
        }
    }
    private sealed class ActionEditor : OptionEditor
    {
        public ActionEditor(KeywordAction action, LocalizationService text, Action changed, Action remove) : base(text, changed)
        {
            Spacing = 6; Margin = new(10, 4);
            Toggle("Enabled", action.Enabled, v => action.Enabled = v);
            var kind = new ComboBox { ItemsSource = Enum.GetValues<KeywordActionKind>().Select(v => Text["Keyword" + v]).ToArray(), SelectedIndex = (int)action.Kind };
            Children.Add(kind);
            var values = new StackPanel { Spacing = 6 }; Children.Add(values);
            void Fill()
            {
                values.Children.Clear();
                var fields = new Fields(text, changed); values.Children.Add(fields);
                switch (action.Kind)
                {
                    case KeywordActionKind.Visual:
                        var effects = new ComboBox { ItemsSource = Enum.GetValues<KeywordVisual>().Select(v => text["Keyword" + v]).ToArray(), SelectedIndex = (int)action.Visual };
                        effects.SelectionChanged += (_, _) => { if (effects.SelectedIndex >= 0) { action.Visual = (KeywordVisual)effects.SelectedIndex; Changed(); } };
                        fields.Children.Add(effects); break;
                    case KeywordActionKind.Audio:
                        var path = new TextBlock { Text = action.AudioPath is null ? text["SelectAudio"] : System.IO.Path.GetFileName(action.AudioPath) };
                        var pick = new Button { Content = text["SelectAudio"] };
                        pick.Click += async (_, _) =>
                        {
                            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } provider) return;
                            var files = await provider.OpenFilePickerAsync(new() { AllowMultiple = false, FileTypeFilter = [new("Audio") { Patterns = ["*.mp3", "*.wav", "*.ogg", "*.flac", "*.m4a"] }] });
                            var local = SubliminalOptions.LocalAudio(files.FirstOrDefault()?.TryGetLocalPath());
                            if (local is not null) { action.AudioPath = local; path.Text = System.IO.Path.GetFileName(local); Changed(); }
                        };
                        fields.Children.Add(path); fields.Children.Add(pick);
                        fields.N("Volume", action.Gain * 100, 0, 100, v => action.Gain = v / 100);
                        fields.N("Repeats", action.PlayCount, 1, 5, v => action.PlayCount = (int)v);
                        fields.N("KeywordAudioDelay", action.DelayMs, 0, 5000, v => action.DelayMs = (int)v);
                        fields.T("Duck", action.DuckAudio, v => action.DuckAudio = v); break;
                    case KeywordActionKind.Haptic: fields.N("Intensity", action.Gain * 100, 0, 100, v => action.Gain = v / 100); break;
                    case KeywordActionKind.Xp: fields.N("XP", action.Xp, 0, 100, v => action.Xp = (int)v); break;
                }
            }
            kind.SelectionChanged += (_, _) => { if (kind.SelectedIndex >= 0) { action.Kind = (KeywordActionKind)kind.SelectedIndex; Fill(); Changed(); } };
            Fill(); var delete = new Button { Content = Text["Remove"] };
            delete.Click += (_, _) => remove(); Children.Add(delete);
        }
    }
    private sealed class Fields(LocalizationService text, Action changed) : OptionEditor(text, changed)
    {
        public void N(string key, double value, double min, double max, Action<double> set) => Number(key, value, min, max, set);
        public void T(string key, bool value, Action<bool> set) => Toggle(key, value, set);
    }
}
