using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public static class SecondaryTabs
{
    public static Control Presets(ClassicSettings settings, LocalizationService text, Action changed)
    {
        var body = new StackPanel { Spacing = 16 };
        var cards = new UniformGrid { Columns = 4 };
        foreach (var name in new[] { "Gentle", "Daily", "Deep" })
        {
            var content = new StackPanel { Spacing = 10 };
            content.Children.Add(Text(text[name], true));
            content.Children.Add(new TextBlock { Text = text[name + "Summary"], FontSize = 12,
                TextWrapping = TextWrapping.Wrap, MinHeight = 80, Foreground = Brush("#C9C9DE") });
            var load = new Button { Content = text["Load"], IsEnabled = false };
            load.Classes.Add("pill");
            ToolTip.SetTip(load, text["Unavailable"]);
            content.Children.Add(load);
            cards.Children.Add(Card(content));
        }
        var create = new Button { Content = text["NewPreset"], IsEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        create.Classes.Add("pill");
        ToolTip.SetTip(create, text["Unavailable"]);
        cards.Children.Add(Card(create));
        body.Children.Add(cards);
        var duration = new WrapPanel { Orientation = Orientation.Horizontal };
        duration.Children.Add(new TextBlock { Text = text["RunFor"], Margin = new Thickness(6, 8, 12, 0) });
        var buttons = new List<ToggleButton>();
        foreach (var (minutes, key) in new[] { (15, "Minutes15"), (30, "Minutes30"), (60, "Minutes60"), (0, "UntilStopped") })
        {
            var button = new ToggleButton { Content = text[key], IsChecked = settings.RunMinutes == minutes,
                Margin = new Thickness(0, 0, 6, 6) };
            button.Classes.Add("pill");
            button.Click += (_, _) =>
            {
                foreach (var other in buttons) other.IsChecked = ReferenceEquals(button, other);
                settings.RunMinutes = minutes;
                changed();
            };
            buttons.Add(button);
            duration.Children.Add(button);
        }
        body.Children.Add(duration);
        return body;
    }

    public static Control Progress(LocalizationService text)
    {
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("280,*") };
        var summary = new StackPanel { Spacing = 10 };
        summary.Children.Add(new TextBlock { Text = text["Level"] + " 1", FontSize = 26,
            FontWeight = FontWeight.SemiBold });
        summary.Children.Add(Text("0 " + text["XP"]));
        summary.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, Height = 8 });
        foreach (var (key, value) in new[] { ("SessionTime", "00:00"), ("VideosFinished", "0"), ("BubblesPopped", "0") })
        {
            var stat = new StackPanel { Spacing = 2 };
            stat.Children.Add(new TextBlock { Text = value, FontSize = 20 });
            stat.Children.Add(Text(text[key]));
            summary.Children.Add(new Border { Child = stat, Padding = new Thickness(10, 8),
                CornerRadius = new CornerRadius(6), Background = Brush("#181830") });
        }
        summary.Children.Add(Note(text["ProgressPending"]));
        layout.Children.Add(Card(summary));
        var achievements = new StackPanel { Spacing = 12 };
        achievements.Children.Add(Text(text["Achievements"] + " 0 / 27", true));
        achievements.Children.Add(Note(text["AchievementsPending"]));
        var badges = new UniformGrid { Columns = 9, Rows = 3 };
        for (var index = 0; index < 27; index++)
        {
            var slot = new Border { MinHeight = 52, Margin = new Thickness(4), CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1), BorderBrush = Brush("#2E2E4A"), Background = Brush("#181830") };
            AutomationProperties.SetName(slot, text["Achievements"] + " " + (index + 1));
            ToolTip.SetTip(slot, text["AchievementsPending"]);
            badges.Children.Add(slot);
        }
        achievements.Children.Add(badges);
        var badgeCard = Card(achievements);
        Grid.SetColumn(badgeCard, 1);
        layout.Children.Add(badgeCard);
        return layout;
    }

    private static Border Card(Control child)
    {
        var card = new Border { Child = child, Padding = new Thickness(14), Margin = new Thickness(6),
            VerticalAlignment = VerticalAlignment.Top, MinHeight = 180 };
        card.Classes.Add("card");
        return card;
    }
    private static TextBlock Text(string value, bool title = false)
    {
        var text = new TextBlock { Text = value, FontSize = title ? 14 : 12 };
        if (title) text.Classes.Add("chrome-title");
        return text;
    }
    private static TextBlock Note(string value)
    {
        var note = Text(value);
        note.Classes.Add("muted");
        note.TextWrapping = TextWrapping.Wrap;
        return note;
    }
    private static SolidColorBrush Brush(string colour) => new(Color.Parse(colour));
}
