using Xunit;

namespace CCP.Classic.Tests;

public sealed class NetworkBoundaryTests
{
    [Fact]
    public void SolutionContainsNoNetworkOrBrowserApis()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CCP.Classic.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var extensions = new[] { ".cs", ".axaml", ".csproj", ".props", ".targets" };
        var forbidden = new[] { "System.Net." + "Http", "System.Net." + "Sockets", "Web" + "View", "Http" + "Listener", "Http" + "Client" };
        var hits = Directory.EnumerateFiles(root.FullName, "*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f)))
            .Where(f => !Path.GetRelativePath(root.FullName, f).Split(Path.DirectorySeparatorChar)
                .Any(p => p is "bin" or "obj" or ".git" or "artifacts"))
            .Where(f => !Path.GetRelativePath(root.FullName, f).Replace('\\', '/')
                .StartsWith("src/CCP.Classic.Core/LocalDevices/", StringComparison.Ordinal))
            .Where(f => forbidden.Any(token => File.ReadAllText(f).Contains(token, StringComparison.Ordinal)))
            .ToArray();
        Assert.Empty(hits);
    }
}
