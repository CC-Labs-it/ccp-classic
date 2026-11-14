using System.Text.RegularExpressions;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class NetworkBoundaryTests
{
    // Owner approval 2026-11-09: exactly two opt-in online features, both in one folder. Anything that
    // adds a file or a host there has to change these lists on purpose.
    private const string OnlineFolder = "src/CCP.Classic.Core/Online/";
    private static readonly string[] OnlineFiles = ["ClassicVersion.cs", "UpdateCheck.cs"];
    private static readonly string[] OnlineHosts = ["api.github.com", "github.com"];

    [Fact]
    public void SolutionContainsNoNetworkOrBrowserApis()
    {
        var root = Root();
        var extensions = new[] { ".cs", ".axaml", ".csproj", ".props", ".targets" };
        var forbidden = new[] { "System.Net." + "Http", "System.Net." + "Sockets", "Web" + "View", "Http" + "Listener", "Http" + "Client" };
        var hits = Directory.EnumerateFiles(root.FullName, "*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f)))
            .Where(f => !Path.GetRelativePath(root.FullName, f).Split(Path.DirectorySeparatorChar)
                .Any(p => p is "bin" or "obj" or ".git" or "artifacts"))
            .Where(f => Path.GetRelativePath(root.FullName, f).Replace('\\', '/') is var relative
                && !relative.StartsWith("src/CCP.Classic.Core/LocalDevices/", StringComparison.Ordinal)
                && !relative.StartsWith(OnlineFolder, StringComparison.Ordinal))
            .Where(f => forbidden.Any(token => File.ReadAllText(f).Contains(token, StringComparison.Ordinal)))
            .ToArray();
        Assert.Empty(hits);
    }

    [Fact]
    public void OnlineFolderHoldsOnlyTheOptInFeaturesAndTheirFixedHosts()
    {
        var folder = Path.Combine(Root().FullName, OnlineFolder);
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(Path.GetFileName).Order().ToArray();
        Assert.Equal(OnlineFiles.Order(), files);
        var sources = files.Select(name => File.ReadAllText(Path.Combine(folder, name!))).ToArray();
        var hosts = sources.SelectMany(source => Regex.Matches(source, "https?://([a-z0-9.-]+)").Select(m => m.Groups[1].Value))
            .Distinct().Order().ToArray();
        Assert.Equal(OnlineHosts.Order(), hosts);
        Assert.DoesNotContain(sources, source => source.Contains("http://", StringComparison.Ordinal));
    }

    private static DirectoryInfo Root()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CCP.Classic.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        return root;
    }
}
