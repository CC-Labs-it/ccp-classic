using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

/// <summary>Shared hover and keyboard-accessible click help for an option.</summary>
public static class OptionHelp
{
    public static Control Wrap(Control control, string key, LocalizationService text)
    {
        var description = Description(key, text);
        var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6 };
        row.Classes.Add("option-help-row");
        row.Children.Add(control);
        var help = new Button { Content = "?", Name = "Help_" + key, Width = 24, Height = 24,
            MinWidth = 24, MinHeight = 24, Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center, FontSize = 12,
            Background = Brushes.Transparent, Foreground = Brush.Parse("#B7B3CF") };
        AutomationProperties.SetName(help, text[key] == key ? text["Help"] : text["Help"] + ": " + text[key]);
        AutomationProperties.SetHelpText(control, description);
        ToolTip.SetTip(control, Tip(description));
        ToolTip.SetTip(help, Tip(description));
        ToolTip.SetTip(row, Tip(description));
        help.Flyout = new Flyout { Content = Tip(description) };
        Grid.SetColumn(help, 1); row.Children.Add(help);
        return row;
    }

    public static void SetTip(Control control, string key, LocalizationService text)
    {
        var description = Description(key, text);
        ToolTip.SetTip(control, Tip(description));
        AutomationProperties.SetHelpText(control, description);
    }

    private static TextBlock Tip(string description) => new()
    { Text = description, MaxWidth = 310, TextWrapping = TextWrapping.Wrap, FontSize = 13 };

    public static string Description(string key, LocalizationService text)
    {
        var helpKey = "OptionHelp_" + key;
        var description = text[helpKey];
        if (description != helpKey) return description;
        helpKey = OptionHelpKeys.Resolve(key);
        description = text[helpKey];
        return description != helpKey ? description : text["HelpSettingsBody"];
    }
}
