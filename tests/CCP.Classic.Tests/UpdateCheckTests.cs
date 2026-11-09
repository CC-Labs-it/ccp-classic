using System.Net;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Classic.App;
using CCP.Classic.Core;
using CCP.Classic.Core.Online;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class UpdateCheckTests
{
    [Theory]
    [InlineData("1.0.0-beta.6", "1.0.0-beta.7", -1)]
    [InlineData("1.0.0-beta.6", "1.0.0-beta.10", -1)]
    [InlineData("1.0.0-beta.10", "1.0.0", -1)]
    [InlineData("v1.0.0", "1.0.0", 0)]
    [InlineData("1.0.0+abc123", "1.0.0", 0)]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("1.2", "1.10.0", -1)]
    [InlineData("1.0.0-alpha", "1.0.0-beta", -1)]
    [InlineData("1.0.0-beta", "1.0.0-beta.1", -1)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.rc", -1)]
    [InlineData("2.0.0", "1.9.9", 1)]
    public void VersionsCompareBySemVerPrecedence(string left, string right, int expected)
    {
        Assert.True(ClassicVersion.TryParse(left, out var a));
        Assert.True(ClassicVersion.TryParse(right, out var b));
        Assert.Equal(expected, Math.Sign(a.CompareTo(b)));
        Assert.Equal(-expected, Math.Sign(b.CompareTo(a)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("beta")]
    [InlineData("1.x.0")]
    [InlineData("1.0.0-")]
    [InlineData("1.0.0.0")]
    [InlineData("1.0.0-be ta")]
    public void NonVersionsDoNotParse(string value) => Assert.False(ClassicVersion.TryParse(value, out _));

    [Fact]
    public void TheSwitchIsOffByDefaultAndAfterAReset()
    {
        Assert.False(new ClassicSettings().CheckForUpdates);
        Assert.False(JsonSerializer.Deserialize<ClassicSettings>("{}")!.CheckForUpdates);
        var settings = new ClassicSettings { CheckForUpdates = true };
        settings.ResetToDefaults();
        Assert.False(settings.CheckForUpdates);
    }

    [Fact]
    public void PicksTheNewestPublishedReleaseIncludingPrereleases()
    {
        var json = Releases(("v1.0.0-beta.6", false, true), ("v1.0.0-beta.9", true, true), ("v1.0.0-beta.7", false, true));
        Assert.True(ClassicVersion.TryParse("1.0.0-beta.6", out var current));
        var result = UpdateCheck.Pick(json, current);
        Assert.Equal(UpdateStatus.Available, result.Status);
        Assert.Equal("1.0.0-beta.7", result.Version);
        Assert.Equal(new Uri("https://github.com/CC-Labs-it/ccp-classic/releases/tag/v1.0.0-beta.7"), result.Page);
        Assert.True(ClassicVersion.TryParse("1.0.0-beta.7", out var same));
        Assert.Equal(UpdateStatus.UpToDate, UpdateCheck.Pick(json, same).Status);
        Assert.Equal(UpdateStatus.Failed, UpdateCheck.Pick("{ not json", same).Status);
        Assert.Equal(UpdateStatus.Failed, UpdateCheck.Pick("{}", same).Status);
    }

    [Fact]
    public void AReleasePageOffTheClassicRepositoryFallsBackToTheReleasesList()
    {
        var json = "[{\"tag_name\":\"v9.0.0\",\"draft\":false,\"html_url\":\"https://example.com/evil\"}]";
        Assert.True(ClassicVersion.TryParse("1.0.0", out var current));
        Assert.Equal(UpdateCheck.ReleasesPage, UpdateCheck.Pick(json, current).Page);
    }

    [Fact]
    public async Task ACheckSendsOnePlainGetWithOnlyAUserAgentAndAccept()
    {
        var network = FakeNetwork.Answer(HttpStatusCode.OK, Releases(("v1.0.0-beta.8", false, true)));
        var result = await new UpdateCheck(network).CheckAsync("1.0.0-beta.6", TestContext.Current.CancellationToken);
        Assert.Equal(UpdateStatus.Available, result.Status);
        var request = Assert.Single(network.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal(UpdateCheck.ReleasesApi, request.Uri);
        Assert.Null(request.Body);
        Assert.Equal(["Accept", "User-Agent"], request.Headers.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("CCP-Classic/1.0.0-beta.6", request.Headers["User-Agent"]);
    }

    [Fact]
    public async Task NetworkFailuresComeBackAsFailedWithoutThrowing()
    {
        var cancel = TestContext.Current.CancellationToken;
        Assert.Equal(UpdateStatus.Failed, (await new UpdateCheck(FakeNetwork.Unreachable()).CheckAsync("1.0.0", cancel)).Status);
        Assert.Equal(UpdateStatus.Failed, (await new UpdateCheck(FakeNetwork.Answer(HttpStatusCode.Forbidden, "{}")).CheckAsync("1.0.0", cancel)).Status);
        var silent = FakeNetwork.Answer(HttpStatusCode.OK, "[]");
        Assert.Equal(UpdateStatus.Failed, (await new UpdateCheck(silent).CheckAsync("not a version", cancel)).Status);
        Assert.Empty(silent.Requests);
    }

    internal static string Releases(params (string Tag, bool Draft, bool Prerelease)[] releases) =>
        JsonSerializer.Serialize(releases.Select(r => new Dictionary<string, object>
        {
            ["tag_name"] = r.Tag, ["draft"] = r.Draft, ["prerelease"] = r.Prerelease,
            ["html_url"] = "https://github.com/CC-Labs-it/ccp-classic/releases/tag/" + r.Tag,
        }));
}

[Collection("Avalonia UI")]
public sealed class UpdateCheckWindowTests
{
    [Fact]
    public Task WithTheSwitchOffStartupMakesNoRequest() => WithWindow(new ClassicSettings(), (window, network) =>
    {
        Pump(() => false, 10);
        Assert.Empty(network.Requests);
        Assert.Null(window.UpdateTask);
        Assert.False(Named<HyperlinkButton>(window, "UpdateLink").IsVisible);
    });

    [Fact]
    public Task WithTheSwitchOnStartupChecksOnceAndShowsTheLink() => WithWindow(new ClassicSettings { CheckForUpdates = true }, (window, network) =>
    {
        Pump(() => window.UpdateTask?.IsCompleted == true);
        Pump(() => Named<HyperlinkButton>(window, "UpdateLink").IsVisible);
        Assert.Single(network.Requests);
        var link = Named<HyperlinkButton>(window, "UpdateLink");
        Assert.Equal("Update available (v99.0.0). Click here to download", link.Content);
        Assert.Equal(new Uri("https://github.com/CC-Labs-it/ccp-classic/releases/tag/v99.0.0"), link.NavigateUri);
    });

    [Fact]
    public Task CheckNowWorksWithTheSwitchOffAndSaysWhenItFails() => UiDispatcher.Run(() =>
    {
        var network = FakeNetwork.Unreachable();
        var window = new MainWindow(new ClassicSettings(), windows: true, updateCheck: new UpdateCheck(network));
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            window.OpenSheet("Settings"); Dispatcher.UIThread.RunJobs();
            var settings = window.SettingsWindow!;
            Assert.False(Named<ToggleButton>(settings, "CheckUpdates").IsChecked);
            Click(settings, Named<Button>(settings, "CheckNow"));
            Pump(() => window.UpdateTask?.IsCompleted == true);
            Pump(() => Named<TextBlock>(settings, "UpdateResultText").Text == "Could not reach GitHub. Try again later.");
            Assert.Single(network.Requests);
            Assert.False(window.Settings.CheckForUpdates);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    private static Task WithWindow(ClassicSettings settings, Action<MainWindow, FakeNetwork> action) => UiDispatcher.Run(() =>
    {
        var network = FakeNetwork.Answer(HttpStatusCode.OK, UpdateCheckTests.Releases(("v99.0.0", false, false)));
        var window = new MainWindow(settings, windows: true, updateCheck: new UpdateCheck(network));
        try { window.Show(); Dispatcher.UIThread.RunJobs(); action(window, network); }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    private static void Pump(Func<bool> done, int rounds = 400)
    {
        for (var i = 0; i < rounds && !done(); i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        Dispatcher.UIThread.RunJobs();
    }

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
