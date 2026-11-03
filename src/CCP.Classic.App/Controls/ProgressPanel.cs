using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public sealed class ProgressPanel : UserControl
{
    private readonly ProgressTracker tracker;
    private readonly LocalizationService text;
    private readonly TextBlock level = new() { Name = "ProgressLevel", FontSize = 26, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock xp = new() { Name = "ProgressXp", TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar bar = new() { Name = "ProgressXpBar", Minimum = 0, Height = 8 };
    private readonly TextBlock count = new() { Name = "ProgressAchievementCount", FontSize = 16, FontWeight = FontWeight.SemiBold };
    private readonly Dictionary<string, TextBlock> stats = [], captions = [];
    private readonly Dictionary<string, (Border Card, TextBlock Mark, TextBlock Name, TextBlock Requirement)> badges = [];
    private bool listening;

    public ProgressPanel(ProgressTracker tracker, LocalizationService text)
    {
        this.tracker = tracker; this.text = text;
        var body = new StackPanel { Spacing = 14 };
        var summary = new StackPanel { Spacing = 8 };
        summary.Children.Add(level); summary.Children.Add(xp); summary.Children.Add(bar);
        body.Children.Add(Card(summary));
        var totals = new UniformGrid { Columns = 3 };
        foreach (var key in new[] { "SessionTime", "VideosFinished", "BubblesPopped" })
        {
            var content = new StackPanel { Spacing = 4 };
            var value = new TextBlock { Name = "ProgressStat_" + key, FontSize = 20, TextTrimming = TextTrimming.CharacterEllipsis };
            var caption = new TextBlock { TextWrapping = TextWrapping.Wrap };
            stats[key] = value; captions[key] = caption;
            content.Children.Add(value); content.Children.Add(caption); totals.Children.Add(Card(content));
        }
        body.Children.Add(totals); body.Children.Add(count);
        var grid = new UniformGrid { Name = "AchievementGrid", Columns = 3 };
        for (var index = 0; index < AchievementCatalog.All.Count; index++)
        {
            var definition = AchievementCatalog.All[index]; var suffix = (index + 1).ToString("00", CultureInfo.InvariantCulture);
            var mark = new TextBlock { Name = "AchievementMark_" + suffix, FontSize = 20, FontWeight = FontWeight.SemiBold };
            var name = new TextBlock { Name = "AchievementName_" + suffix, FontSize = 13,
                FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
            var requirement = new TextBlock { Name = "AchievementRequirement_" + suffix, FontSize = 11,
                TextWrapping = TextWrapping.Wrap };
            var content = new StackPanel { Spacing = 6 };
            content.Children.Add(mark); content.Children.Add(name); content.Children.Add(requirement);
            var card = Card(content); card.Name = "Achievement_" + suffix; card.MinHeight = 156;
            badges[definition.Id] = (card, mark, name, requirement); grid.Children.Add(card);
        }
        body.Children.Add(grid); Content = body;
        AttachedToVisualTree += (_, _) => Listen();
        DetachedFromVisualTree += (_, _) => StopListening();
        Refresh();
    }

    public void Refresh()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(RefreshIfAttached); return; }
        var culture = CultureInfo.GetCultureInfo(text.Language);
        level.Text = text["Level"] + " " + tracker.Level.ToString(culture);
        xp.Text = tracker.XpIntoLevel.ToString("N0", culture) + " / " + tracker.XpForNextLevel.ToString("N0", culture) + " " + text["XP"];
        bar.Maximum = tracker.XpForNextLevel; bar.Value = Math.Min(bar.Maximum, tracker.XpIntoLevel);
        var seconds = (long)ProgressDataSeconds(tracker.Data.SessionSeconds);
        stats["SessionTime"].Text = $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}";
        stats["VideosFinished"].Text = tracker.Data.VideosFinished.ToString("N0", culture);
        stats["BubblesPopped"].Text = tracker.Data.BubblesPopped.ToString("N0", culture);
        foreach (var (key, caption) in captions) caption.Text = text[key];
        count.Text = text["Achievements"] + " " + tracker.UnlockedAchievementCount + " / " + AchievementCatalog.All.Count;
        foreach (var definition in AchievementCatalog.All)
        {
            var badge = badges[definition.Id]; var unlocked = tracker.Data.UnlockedAchievements.Contains(definition.Id);
            badge.Mark.Text = unlocked ? "✓" : "○";
            badge.Mark.Foreground = Brush(unlocked ? "#FF69B4" : "#A0A0BC");
            badge.Card.BorderBrush = Brush(unlocked ? "#FF69B4" : "#2E2E4A");
            badge.Name.Text = text[definition.NameKey]; badge.Requirement.Text = text[definition.RequirementKey];
            AutomationProperties.SetName(badge.Card, badge.Name.Text + " " + badge.Mark.Text + ". " + badge.Requirement.Text);
        }
    }

    private static double ProgressDataSeconds(double seconds) => double.IsFinite(seconds) ? Math.Clamp(seconds, 0, 1e15) : 0;

    private void Listen()
    {
        if (listening) return;
        listening = true; tracker.Changed += Refresh; text.Changed += Refresh; Refresh();
    }
    private void StopListening()
    {
        if (!listening) return;
        listening = false; tracker.Changed -= Refresh; text.Changed -= Refresh;
    }
    private void RefreshIfAttached() { if (listening) Refresh(); }
    private static Border Card(Control content)
    {
        var card = new Border { Child = content, Padding = new Thickness(12), Margin = new Thickness(4),
            BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top };
        card.Classes.Add("card"); return card;
    }
    private static SolidColorBrush Brush(string value) => new(Color.Parse(value));
}
