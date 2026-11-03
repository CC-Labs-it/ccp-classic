using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Effects;

internal sealed class LockCardWindow : Window
{
    private readonly TextBlock phrase, count;
    private readonly TextBox entry;
    private readonly ProgressBar progress;
    private readonly LockCardOptions options;
    private bool updating, closing;
    public event Action<string>? AnswerChanged;
    public event Action? StopRequested;

    public LockCardWindow(LockCardCue cue, LockCardOptions options, LocalizationService text, bool primary)
    {
        this.options = options;
        Title = text["LockCard"]; ShowInTaskbar = false; Topmost = true;
        WindowDecorations = WindowDecorations.None; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual; ShowActivated = primary;
        Background = Brush.Parse(options.Background); Opacity = .01;
        var panel = new StackPanel { Spacing = 24, Margin = new Thickness(36), MaxWidth = 1000,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = text["LockCard"], FontSize = 20,
            Foreground = Brush.Parse(options.Accent), HorizontalAlignment = HorizontalAlignment.Center });
        phrase = new TextBlock { Text = cue.Phrase, FontSize = 36, FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = Brush.Parse(options.TextColor) };
        count = new TextBlock { FontSize = 20, Foreground = Brush.Parse(options.Accent), HorizontalAlignment = HorizontalAlignment.Center };
        progress = new ProgressBar { Minimum = 0, Maximum = cue.Repetitions, Height = 8, Foreground = Brush.Parse(options.Accent) };
        entry = new TextBox { Name = "LockAnswer", FontSize = 24, MinWidth = 280, MaxWidth = 920,
            MaxLength = 2200, TextWrapping = TextWrapping.Wrap, IsUndoEnabled = !options.StrictCopy,
            IsReadOnly = !primary, Background = Brush.Parse(options.InputBackground), Foreground = Brush.Parse(options.InputText),
            ContextMenu = null, ContextFlyout = null };
        DragDrop.SetAllowDrop(entry, false);
        entry.PastingFromClipboard += (_, e) => { if (options.StrictCopy) e.Handled = true; };
        entry.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (options.StrictCopy && ((e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key is Key.V or Key.X or Key.C or Key.Z or Key.Y)
                || (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key is Key.Insert or Key.Delete))) e.Handled = true;
        }, RoutingStrategies.Tunnel);
        entry.TextChanged += (_, _) => { if (!updating && primary) AnswerChanged?.Invoke(entry.Text ?? ""); };
        panel.Children.Add(phrase); panel.Children.Add(count); panel.Children.Add(progress); panel.Children.Add(entry);
        Content = panel;
        Closing += (_, e) => { if (!closing) { e.Cancel = true; StopRequested?.Invoke(); } };
        Opened += (_, _) => { if (primary) entry.Focus(); };
    }

    public void Update(LockCardCue cue, string input)
    {
        phrase.Text = cue.Phrase; count.Text = cue.Completed + " / " + cue.Repetitions;
        progress.Value = cue.Completed; Opacity = Math.Max(.01, cue.Opacity);
        updating = true;
        if (entry.Text != input) { entry.Text = input; entry.CaretIndex = input.Length; }
        updating = false;
        entry.BorderBrush = Brush.Parse(input.Length == 0 || LockCardText.IsPrefixOf(input, cue.Phrase) ? options.Accent : "#F26078");
    }
    public void Finish() { closing = true; Close(); }
}
