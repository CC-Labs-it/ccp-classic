using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public sealed class TutorialWindow : Window
{
    private readonly LocalizationService text;
    private readonly IReadOnlyList<TutorialTopic> topics;
    private readonly ComboBox selector = new() { Name = "HelpTopics", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock body = new() { Name = "HelpBody", TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 23 };
    private readonly TextBlock heading = new() { Name = "HelpHeading", FontSize = 21, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock motion = new() { Name = "HelpMotionStatus", TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock topicLabel = new() { FontSize = 12 };
    private readonly TextBlock demo = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly Button previous, next, pause, close;
    private readonly TutorialAnimation animation;
    private int selected;
    private bool selecting, released;
    public string TopicId => topics[selected].Id;
    internal TutorialAnimation Animation => animation;

    public TutorialWindow(Window owner, ClassicSettings settings, LocalizationService text,
        Action<Window> configureInput, bool? windows = null)
    {
        this.text = text;
        topics = TutorialCatalog.ForPlatform(windows ?? OperatingSystem.IsWindows());
        Title = "CCP Classic · " + text["Help"];
        Width = 650; Height = 700; MinWidth = 430; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ClosingBehavior = WindowClosingBehavior.OwnerWindowOnly;
        animation = new(settings, text) { Name = "HelpAnimation", Height = 270,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        animation.MotionChanged += RefreshMotion;
        previous = MakeButton("HelpPrevious", () => SelectIndex(selected - 1));
        next = MakeButton("HelpNext", () => SelectIndex(selected + 1));
        pause = MakeButton("HelpPause", () => { animation.UserPaused = !animation.UserPaused; RefreshMotion(); });
        close = MakeButton("Close", Close);
        selector.SelectionChanged += (_, _) => { if (!selecting) SelectIndex(selector.SelectedIndex); };
        var layout = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new(22) };
        var top = new StackPanel { Spacing = 12 };
        top.Children.Add(topicLabel);
        top.Children.Add(selector); top.Children.Add(heading); layout.Children.Add(top);
        var content = new StackPanel { Spacing = 14, Margin = new(0, 16, 18, 16) };
        content.Children.Add(new Border { Child = animation, CornerRadius = new(14), ClipToBounds = true });
        content.Children.Add(demo);
        content.Children.Add(motion); content.Children.Add(body);
        var scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        Control[] buttons = [previous, next, pause, close];
        foreach (var button in buttons) { button.Margin = new(0, 0, 8, 8); footer.Children.Add(button); }
        Grid.SetRow(footer, 2); layout.Children.Add(footer); Content = layout;
        Opened += (_, _) => VisualPolish.Enter(layout, settings);
        configureInput(this);
        text.Changed += LanguageChanged;
        owner.PropertyChanged += OwnerChanged;
        PropertyChanged += SelfChanged;
        Closed += (_, _) =>
        {
            if (released) return; released = true;
            text.Changed -= LanguageChanged; owner.PropertyChanged -= OwnerChanged;
            animation.Dispose();
        };
        void OwnerChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        { if (args.Property == IsVisibleProperty || args.Property == WindowStateProperty) RefreshVisibility(); }
        void SelfChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        { if (args.Property == IsVisibleProperty || args.Property == WindowStateProperty) RefreshVisibility(); }
        void RefreshVisibility() => animation.PresentationAvailable = !released && IsVisible && WindowState != WindowState.Minimized
            && owner.IsVisible && owner.WindowState != WindowState.Minimized;
        LanguageChanged();
    }

    private Button MakeButton(string key, Action action)
    {
        var button = new Button { Content = text[key], Name = key };
        button.Classes.Add("pill");
        AutomationProperties.SetName(button, text[key]); button.Click += (_, _) => action(); return button;
    }

    public void SelectTopic(string? id)
    {
        var topic = TutorialCatalog.Find(id);
        SelectIndex(Math.Max(0, topics.ToList().FindIndex(item => item.Id == topic.Id)));
    }

    private void SelectIndex(int index)
    {
        if (released || index < 0 || index >= topics.Count) return;
        selected = index; selecting = true; selector.SelectedIndex = index; selecting = false;
        var topic = topics[index]; heading.Text = text[topic.TitleKey]; body.Text = text[topic.BodyKey];
        animation.SelectScene(topic.SceneId);
        animation.IsVisible = topic.SceneId is not null;
        demo.IsVisible = topic.SceneId is not null;
        previous.IsEnabled = index > 0; next.IsEnabled = index + 1 < topics.Count;
        RefreshMotion();
    }

    private void LanguageChanged()
    {
        selecting = true;
        selector.ItemsSource = topics.Select(topic => text[topic.TitleKey]).ToArray(); selecting = false;
        Title = "CCP Classic · " + text["Help"];
        previous.Content = text["HelpPrevious"]; next.Content = text["HelpNext"];
        topicLabel.Text = text["HelpTopics"]; demo.Text = text["HelpDemo"];
        close.Content = text["Close"];
        SelectIndex(selected);
    }

    private void RefreshMotion()
    {
        pause.IsVisible = topics[selected].SceneId is not null;
        pause.IsEnabled = !animation.PreferencesPaused;
        pause.Content = text[animation.UserPaused ? "HelpResume" : "HelpPause"];
        motion.IsVisible = topics[selected].SceneId is not null && animation.PreferencesPaused;
        motion.Text = text["HelpMotionPaused"];
    }
}
