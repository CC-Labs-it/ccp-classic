using Avalonia.Automation;
using Avalonia.Controls;
using CCP.Classic.App.Controls;

namespace CCP.Classic.App;

public sealed partial class MainWindow
{
    public TutorialWindow? HelpWindow { get; private set; }

    public Button HelpButton(string? topicId)
    {
        var button = new Button { Content = "?", Name = "Help_" + (topicId ?? "guide"),
            MinWidth = 30, MinHeight = 30 };
        button.Classes.Add("pill");
        ToolTip.SetTip(button, text["Help"]);
        AutomationProperties.SetName(button, text["Help"]);
        button.Click += (_, _) => OpenHelp(topicId);
        return button;
    }

    public void OpenHelp(string? topicId = null)
    {
        if (HelpWindow is { } current)
        {
            current.SelectTopic(topicId);
            if (!current.IsVisible) current.Show(this);
            if (current.WindowState == WindowState.Minimized) current.WindowState = WindowState.Normal;
            current.Activate();
            return;
        }
        var child = new TutorialWindow(this, Settings, text, ConfigureEffectInput, windows);
        VisualPolish.ApplyWindow(child, Settings);
        void StopHelp() => child.Close();
        void OwnerClosed(object? sender, EventArgs args) => child.Close();
        Session.StopRequested += StopHelp;
        Closed += OwnerClosed;
        child.Closed += (_, _) =>
        {
            Session.StopRequested -= StopHelp;
            Closed -= OwnerClosed;
            if (ReferenceEquals(HelpWindow, child)) HelpWindow = null;
        };
        HelpWindow = child;
        child.SelectTopic(topicId);
        child.Show(this);
    }
}
