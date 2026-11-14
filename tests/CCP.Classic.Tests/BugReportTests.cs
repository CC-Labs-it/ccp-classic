using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Classic.App;
using CCP.Classic.Core;
using CCP.Classic.Core.Online;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class BugReportTests
{
    [Fact]
    public void TheScrubberRemovesUserNamesFileNamesEmailsAndTokens()
    {
        var raw = string.Join("\n",
            @"error video: Could not open 'C:\Users\Alice Smith\Videos\My Secret Clip.MP4'",
            "error library: /home/alice/Pictures/private/one.png missing",
            @"loaded D:\Hypno\Files\deep.trance.mp3, then /Users/bob/x.gif",
            "mail me@example.com, \"auth_token\": \"abcdef0123456789\" Bearer abcdefghijklmnopqrstuvwxyz",
            "fetched https://api.github.com/repos/x and /root/kept");
        var (text, counts) = ReportScrubber.Scrub(raw);
        Assert.DoesNotContain("Alice", text); Assert.DoesNotContain("alice", text); Assert.DoesNotContain("bob", text);
        Assert.DoesNotContain("Secret", text); Assert.DoesNotContain("one.png", text); Assert.DoesNotContain("deep.trance", text);
        Assert.DoesNotContain("me@example.com", text); Assert.DoesNotContain("abcdef0123456789", text);
        Assert.Contains(@"C:\Users\<redacted>\Videos\<file>.mp4", text);
        Assert.Contains("/home/<redacted>/Pictures/private/<file>.png", text);
        Assert.Contains(@"D:\Hypno\Files\<file>.mp3", text);
        Assert.Contains("https://api.github.com/repos/x", text);
        Assert.Contains("/root/kept", text);
        Assert.Equal(3, counts.Paths); Assert.Equal(4, counts.Files); Assert.Equal(1, counts.Emails); Assert.Equal(2, counts.Tokens);
        Assert.Equal(("", ScrubCounts.None), ReportScrubber.Scrub(null));
    }

    [Fact]
    public void TheLogStaysOutUnlessTickedAndEveryFieldIsCapped()
    {
        var raw = @"opened C:\Users\Alice\clip.mp4";
        var without = BugReport.Create("1.0.0-beta.7", "Windows", "  it froze  ", "me", includeLog: false, raw);
        Assert.Equal("", without.Log); Assert.Equal(ScrubCounts.None, without.Counts);
        Assert.Equal("it froze", without.Description);
        Assert.Equal("", JsonNode.Parse(BugReport.Body(without))!["app_log"]!.GetValue<string>());
        var with = BugReport.Create("1.0.0-beta.7", "Windows", "x", "", includeLog: true, raw);
        Assert.Equal(@"opened C:\Users\<redacted>\<file>.mp4", with.Log);
        var huge = BugReport.Create(new string('9', 99), new string('o', 999), new string('d', 9000),
            "line one\nline two " + new string('c', 400), includeLog: true, new string('l', 50000) + "END");
        Assert.Equal(BugReport.MaxDescription, huge.Description.Length);
        Assert.Equal(BugReport.MaxContact, huge.Contact.Length);
        Assert.StartsWith("line one line two", huge.Contact);
        Assert.Equal(BugReport.MaxLog, huge.Log.Length); Assert.EndsWith("END", huge.Log);
        Assert.Equal(40, huge.AppVersion.Length); Assert.Equal(80, huge.Os.Length);
    }

    [Fact]
    public void ThePreviewIsTheBodyIndentedAndCarriesOnlyTheListedFields()
    {
        var draft = BugReport.Create("1.0.0-beta.7", "Linux 6.8 X64", "Spirale bloccata, è così", "discord: me", true, "status Idle");
        var body = BugReport.Body(draft);
        var preview = BugReport.Preview(draft);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(body), JsonNode.Parse(preview)));
        Assert.Contains("è così", preview);
        var root = JsonNode.Parse(body)!.AsObject();
        Assert.Equal(["app_log", "contact", "crash_log", "description", "metadata", "scrubber_counts", "steps"],
            root.Select(p => p.Key).Order(StringComparer.Ordinal));
        Assert.Equal(["app", "app_version", "os"], root["metadata"]!.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal));
        Assert.Equal("classic", root["metadata"]!["app"]!.GetValue<string>());
    }

    [Fact]
    public async Task SendingPostsExactlyThePreviewedBodyWithASignature()
    {
        var network = FakeNetwork.Answer(HttpStatusCode.OK, "{\"token\":\"BUG-ABCDEFGH23\"}");
        var clock = new FixedClock(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));
        var draft = BugReport.Create("1.0.0-beta.7", "Windows 11", "It froze", "", false, "");
        var result = await new BugReportSender(network, clock).SendAsync(draft, TestContext.Current.CancellationToken);
        Assert.Equal(new BugReportResult(BugReportOutcome.Sent, "BUG-ABCDEFGH23"), result);
        var request = Assert.Single(network.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal(BugReport.Endpoint, request.Uri);
        Assert.Equal(BugReport.Body(draft), request.Body);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(BugReport.Preview(draft)), JsonNode.Parse(request.Body!)));
        Assert.Equal("1800000000", request.Headers["X-Bug-Timestamp"]);
        Assert.Equal(BugReport.Signature("1800000000", request.Body!), request.Headers["X-Bug-Signature"]);
        Assert.Equal("CCP-Classic/1.0.0-beta.7", request.Headers["User-Agent"]);
        Assert.All(request.Headers.Keys, key => Assert.Contains(key,
            new[] { "User-Agent", "X-Bug-Timestamp", "X-Bug-Signature", "Content-Type", "Content-Length" }));
    }

    [Theory]
    [InlineData(HttpStatusCode.Accepted, "{\"token\":\"BUG-ABCDEFGH23\",\"code\":\"bot_unreachable\"}", BugReportOutcome.Saved, "BUG-ABCDEFGH23")]
    [InlineData(HttpStatusCode.TooManyRequests, "{\"error\":\"rate_limited\"}", BugReportOutcome.RateLimited, null)]
    [InlineData(HttpStatusCode.BadRequest, "{\"error\":\"field_too_large\"}", BugReportOutcome.Failed, null)]
    [InlineData(HttpStatusCode.OK, "{\"token\":\"<script>\"}", BugReportOutcome.Failed, null)]
    [InlineData(HttpStatusCode.OK, "not json", BugReportOutcome.Failed, null)]
    public async Task EveryAnswerMapsToAnOutcome(HttpStatusCode status, string body, BugReportOutcome outcome, string? id)
    {
        var draft = BugReport.Create("1.0.0", "Windows", "x", "", false, "");
        var result = await new BugReportSender(FakeNetwork.Answer(status, body)).SendAsync(draft, TestContext.Current.CancellationToken);
        Assert.Equal(new BugReportResult(outcome, id), result);
        Assert.Equal(BugReportOutcome.Failed,
            (await new BugReportSender(FakeNetwork.Unreachable()).SendAsync(draft, TestContext.Current.CancellationToken)).Outcome);
    }

    [Fact]
    public void TheSessionLogIsBoundedAndSkipsRepeats()
    {
        var log = new SessionLog(capacity: 3, new FixedClock(new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.Zero)));
        log.Add("status Saved"); log.Add("status Saved"); log.Add("a"); log.Add("b"); log.Add("c"); log.Add(" ");
        Assert.Equal("10:00:00 a\n10:00:00 b\n10:00:00 c", log.Text());
        log.Add(new string('x', 5000));
        Assert.Equal(SessionLog.MaxLineChars + 9, log.Text().Split('\n')[^1].Length);
    }

    internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

[Collection("Avalonia UI")]
public sealed class BugReportWindowTests
{
    [Fact]
    public Task NothingIsSentUntilSendOnThePreviewAndTheIdIsShown() => UiDispatcher.Run(() =>
    {
        var network = FakeNetwork.Answer(HttpStatusCode.OK, "{\"token\":\"BUG-ABCDEFGH23\"}");
        var window = new MainWindow(new ClassicSettings(), windows: true, bugReportSender: new BugReportSender(network));
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            window.OpenSheet("Settings"); Dispatcher.UIThread.RunJobs();
            Click(window.SettingsWindow!, Named<Button>(window.SettingsWindow!, "ReportBugOpen"));
            var form = window.SettingsWindow!;
            Assert.False(Named<Button>(form, "BugReview").IsEnabled);
            Assert.False(Named<CheckBox>(form, "BugIncludeLog").IsChecked);
            Named<TextBox>(form, "BugDescription").Text = "The spiral froze after panic.";
            Named<TextBox>(form, "BugContact").Text = "discord: someone";
            Dispatcher.UIThread.RunJobs();
            Click(form, Named<Button>(form, "BugReview"));
            var preview = Named<TextBox>(form, "BugPreviewText").Text!;
            Assert.Equal(BugReport.Preview(window.BugDraft!), preview);
            Assert.Contains("The spiral froze after panic.", preview);
            Assert.Equal("", window.BugDraft!.Log);
            Assert.Empty(network.Requests);
            Click(form, Named<Button>(form, "BugSend"));
            for (var i = 0; i < 400 && !(window.BugSendTask?.IsCompleted == true && form.GetVisualDescendants().OfType<SelectableTextBlock>().Any(t => t.Name == "BugId")); i++)
            { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
            var request = Assert.Single(network.Requests);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(preview), JsonNode.Parse(request.Body!)));
            Assert.Equal("BUG-ABCDEFGH23", Named<SelectableTextBlock>(form, "BugId").Text);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    private static T Named<T>(Window window, string name) where T : Control =>
        Assert.Single(window.GetVisualDescendants().OfType<T>(), control => control.Name == name);

    private static void Click(Window window, Control control)
    {
        window.Activate(); control.BringIntoView(); Dispatcher.UIThread.RunJobs();
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        Assert.NotNull(centre);
        window.MouseMove(centre.Value);
        window.MouseDown(centre.Value, MouseButton.Left);
        window.MouseUp(centre.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
