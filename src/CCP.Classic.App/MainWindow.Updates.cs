using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using CCP.Classic.Core.Online;

namespace CCP.Classic.App;

// Opt-in update check (owner approval 2026-11-09). Nothing runs unless the player turned the switch on,
// which checks once per launch, or pressed Check now. A newer release shows one calm link to its page.
public sealed partial class MainWindow
{
    private readonly UpdateCheck updates;
    private UpdateResult? updateFound;
    private Task<UpdateResult>? updateRunning;
    private TextBlock? updateLine;
    private HyperlinkButton? updateSettingsLink;

    /// <summary>The last check started this run, for tests. Null when none ran.</summary>
    internal Task<UpdateResult>? UpdateTask => updateRunning;

    private void CheckForUpdatesAtStartup()
    {
        if (Settings.CheckForUpdates && updateRunning is null) _ = RunUpdateCheck(manual: false);
    }

    internal async Task<UpdateResult> RunUpdateCheck(bool manual)
    {
        if (manual) ShowUpdateLine("UpdateChecking");
        // A press during a running check waits for that one; it never sends a second request.
        if (updateRunning is not { IsCompleted: false }) updateRunning = updates.CheckAsync(AppVersion);
        var result = await updateRunning;
        if (result.Status == UpdateStatus.Available) { updateFound = result; RefreshUpdateLink(); }
        // A failed check stays silent unless the player asked for it.
        if (manual && result.Status != UpdateStatus.Available)
            ShowUpdateLine(result.Status == UpdateStatus.UpToDate ? "UpdateNone" : "UpdateFailed");
        return result;
    }

    private void ShowUpdateLine(string key)
    {
        if (updateLine is null) return;
        updateLine.Text = text[key]; updateLine.IsVisible = true;
        if (updateSettingsLink is not null) updateSettingsLink.IsVisible = false;
    }

    // Lives in the support banner row; hidden until a check finds a newer release.
    private HyperlinkButton UpdateLink()
    {
        var link = new HyperlinkButton { Name = "UpdateLink", IsVisible = false, Padding = new(2, 3), MinHeight = 24,
            FontSize = 13.5, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Brush("#9BE3C9"), Background = Brushes.Transparent };
        ToolTip.SetTip(link, "github.com/CC-Labs-it/ccp-classic/releases");
        return link;
    }

    private void RefreshUpdateLink()
    {
        if (updateFound is not null && updateSettingsLink is not null)
        {
            FillUpdateLink(updateSettingsLink);
            if (updateLine is not null) updateLine.IsVisible = false;
        }
        if (root?.Children.OfType<Border>().FirstOrDefault(b => b.Name == "SupportBanner")?.Child is not StackPanel rows
            || rows.Children.OfType<HyperlinkButton>().FirstOrDefault(c => c.Name == "UpdateLink") is not { } link) return;
        link.IsVisible = false;
        if (updateFound is not null) FillUpdateLink(link);
    }

    private void FillUpdateLink(HyperlinkButton link)
    {
        link.Content = text["UpdateAvailable"].Replace("{version}", "v" + updateFound!.Version, StringComparison.Ordinal);
        link.NavigateUri = updateFound.Page ?? UpdateCheck.ReleasesPage;
        link.IsVisible = true;
    }

    // Owner 2026-11-09: the switch stays off by default but must be hard to miss. This strip sits above the
    // support banner while checks are off and the player has not said Not now. Nothing goes online until Turn on.
    private Border UpdatePrompt()
    {
        var words = Label(text["UpdatePromptText"], 13); words.TextWrapping = TextWrapping.Wrap; words.Foreground = Brush("#DCC8E8");
        var on = Button("UpdatePromptOn", () =>
        {
            Settings.CheckForUpdates = true; RefreshUpdatePrompt();
            if (SettingsWindow?.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>()
                .FirstOrDefault(c => c.Name == "CheckUpdates") is { } toggle) toggle.IsChecked = true;
            _ = RunUpdateCheck(manual: false);
        });
        on.Name = "UpdatePromptOn"; on.Classes.Add("primary");
        var later = Button("UpdatePromptLater", () => { Settings.UpdatePromptDismissed = true; RefreshUpdatePrompt(); });
        later.Name = "UpdatePromptLater";
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(words); row.Children.Add(on); row.Children.Add(later);
        return new Border { Name = "UpdatePrompt", Child = row, Padding = new(10, 4), Margin = new(0, 0, 0, 3), CornerRadius = new(6),
            Background = Brush("#2C2140"), BorderBrush = Brush("#5A3E66"), BorderThickness = new(1),
            IsVisible = ShowsUpdatePrompt };
    }

    internal bool ShowsUpdatePrompt => !Settings.CheckForUpdates && !Settings.UpdatePromptDismissed;

    private void RefreshUpdatePrompt()
    {
        if (root?.Children.OfType<Border>().FirstOrDefault(b => b.Name == "SupportBanner")?.Child is StackPanel rows
            && rows.Children.OfType<Border>().FirstOrDefault(b => b.Name == "UpdatePrompt") is { } prompt)
            prompt.IsVisible = ShowsUpdatePrompt;
    }

    private Control UpdateControls()
    {
        var toggle = Toggle(Settings.CheckForUpdates, text["CheckUpdates"], value => { Settings.CheckForUpdates = value; RefreshUpdatePrompt(); });
        toggle.Name = "CheckUpdates"; ToolTip.SetTip(toggle, text["OptionHelp_CheckUpdates"]);
        var now = Button("CheckNow", () => _ = RunUpdateCheck(manual: true)); now.Name = "CheckNow";
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        line.Children.Add(now); line.Children.Add(toggle);
        updateLine = Label("", 12.5); updateLine.Name = "UpdateResultText"; updateLine.Classes.Add("muted");
        updateLine.IsVisible = false; updateLine.HorizontalAlignment = HorizontalAlignment.Right;
        updateSettingsLink = UpdateLink(); updateSettingsLink.Name = "UpdateSettingsLink";
        updateSettingsLink.HorizontalAlignment = HorizontalAlignment.Right;
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(line); panel.Children.Add(updateLine); panel.Children.Add(updateSettingsLink);
        if (updateFound is not null) FillUpdateLink(updateSettingsLink);
        return panel;
    }
}
