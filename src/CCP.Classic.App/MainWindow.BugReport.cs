using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.Online;

namespace CCP.Classic.App;

// Opt-in bug report (owner approval 2026-11-09): a short form, then the exact body in a preview,
// then Send. Nothing is collected or sent before the player presses Send on that preview.
public sealed partial class MainWindow
{
    private readonly BugReportSender bugReports;

    /// <summary>The draft on the preview, for tests. Null until the player reviews one.</summary>
    internal BugReportDraft? BugDraft { get; private set; }
    internal Task<BugReportResult>? BugSendTask { get; private set; }

    public void OpenBugReport()
    {
        capturePanic = false; sheet = null; BugDraft = null;
        var content = new StackPanel { Spacing = 8, Margin = new(14, 12) };
        ShowBugForm(content, "", "", includeLog: false);
        ShowSettingsWindow("ReportBug", content);
    }

    private static string OsDescription() =>
        System.Runtime.InteropServices.RuntimeInformation.OSDescription + " " +
        System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;

    private void ShowBugForm(StackPanel content, string description, string contact, bool includeLog)
    {
        content.Children.Clear();
        var what = new TextBox { Name = "BugDescription", Text = description, AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, MinHeight = 120, MaxHeight = 240, MaxLength = BugReport.MaxDescription };
        AutomationProperties.SetName(what, text["BugWhatHappened"]);
        var who = new TextBox { Name = "BugContact", Text = contact, MaxLength = BugReport.MaxContact };
        AutomationProperties.SetName(who, text["BugContact"]);
        var log = new CheckBox { Name = "BugIncludeLog", IsChecked = includeLog, Content = text["BugIncludeLog"] };
        var review = Button("BugReview", () => ShowBugPreview(content, BugReport.Create(AppVersion, OsDescription(),
            what.Text ?? "", who.Text ?? "", log.IsChecked == true, SessionLog.Shared.Text())));
        review.Name = "BugReview"; review.Classes.Add("primary");
        review.IsEnabled = !string.IsNullOrWhiteSpace(description);
        what.TextChanged += (_, _) => review.IsEnabled = !string.IsNullOrWhiteSpace(what.Text);
        content.Children.Add(BugHeading("BugWhatHappened")); content.Children.Add(what);
        content.Children.Add(BugHeading("BugContact")); content.Children.Add(who); content.Children.Add(Note("BugContactNote"));
        content.Children.Add(log); content.Children.Add(Note("BugIncludeLogNote"));
        content.Children.Add(BugButtons(Button("Cancel", CloseSheet), review));
    }

    private void ShowBugPreview(StackPanel content, BugReportDraft draft)
    {
        BugDraft = draft;
        content.Children.Clear();
        content.Children.Add(Note("BugPreviewNote"));
        content.Children.Add(new TextBox { Name = "BugPreviewText", Text = BugReport.Preview(draft), IsReadOnly = true,
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 340, FontSize = 12,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New, monospace") });
        var back = Button("BugBack", () => ShowBugForm(content, draft.Description, draft.Contact, draft.IncludeLog));
        back.Name = "BugBack";
        var send = Button("BugSend", () => { });
        send.Name = "BugSend"; send.Classes.Add("primary");
        send.Click += (_, _) => { send.IsEnabled = back.IsEnabled = false; send.Content = text["BugSending"]; _ = SendBugReport(content, draft); };
        content.Children.Add(BugButtons(back, send));
    }

    private async Task SendBugReport(StackPanel content, BugReportDraft draft)
    {
        BugSendTask = bugReports.SendAsync(draft);
        var result = await BugSendTask;
        content.Children.Clear();
        var key = result.Outcome switch
        {
            BugReportOutcome.Sent => "BugSent",
            BugReportOutcome.Saved => "BugSaved",
            BugReportOutcome.RateLimited => "BugRateLimited",
            _ => "BugFailed",
        };
        var line = Label(text[key], 14);
        line.Name = "BugResult"; line.TextWrapping = TextWrapping.Wrap; line.Foreground = Brush("#D7C9F0");
        content.Children.Add(line);
        if (result.Id is { } id)
        {
            var number = new SelectableTextBlock { Name = "BugId", Text = id, FontSize = 18, FontWeight = FontWeight.SemiBold,
                Foreground = Brush("#FF9BD5"), HorizontalAlignment = HorizontalAlignment.Left };
            content.Children.Add(number);
            content.Children.Add(BugButtons(Button("Close", CloseSheet)));
        }
        else content.Children.Add(BugButtons(Button("BugBack", () => ShowBugPreview(content, draft)), Button("Close", CloseSheet)));
    }

    private TextBlock BugHeading(string key)
    {
        var label = Label(text[key], 14); label.Foreground = Brush("#D7C9F0"); label.FontWeight = FontWeight.Medium; return label;
    }

    private static StackPanel BugButtons(params Button[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new(0, 6, 0, 0) };
        foreach (var button in buttons) row.Children.Add(button);
        return row;
    }
}
