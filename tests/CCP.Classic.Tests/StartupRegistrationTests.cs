using System.Text;
using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class StartupRegistrationTests
{
    [Fact]
    public void WindowsCommandsQuoteEitherNativeHostOrDotnetPlusApplication()
    {
        Assert.Equal("\"C:\\Program Files\\CCP Classic\\CCP.Classic.App.exe\" --startup",
            StartupRegistration.BuildWindowsCommand(@"C:\Program Files\CCP Classic\CCP.Classic.App.exe"));
        Assert.Equal("\"C:\\Program Files\\dotnet\\dotnet.exe\" \"C:\\My Apps\\CCP.Classic.App.dll\" --startup",
            StartupRegistration.BuildWindowsCommand(@"C:\Program Files\dotnet\dotnet.exe", @"C:\My Apps\CCP.Classic.App.dll"));
        Assert.Equal("\"C:\\$literal%name\\app.exe\" --startup", StartupRegistration.BuildWindowsCommand(@"C:\$literal%name\app.exe"));
    }

    [Fact]
    public void DesktopExecUsesTwoStageEscapingAndLiteralPercentFieldCodes()
    {
        var entry = StartupRegistration.BuildDesktopEntry("/opt/My Apps/$value%/CCP.Classic.App");
        Assert.Contains("Exec=\"/opt/My Apps/\\\\$value%%/CCP.Classic.App\" --startup\n", entry);
        Assert.Contains("Terminal=false\n", entry); Assert.DoesNotContain("sh -c", entry);
        var framework = StartupRegistration.BuildDesktopEntry("/usr/share/dotnet/dotnet", "/opt/My App/CCP.Classic.App.dll");
        Assert.Contains("Exec=\"/usr/share/dotnet/dotnet\" \"/opt/My App/CCP.Classic.App.dll\" --startup\n", framework);
        var slash = StartupRegistration.BuildDesktopEntry("/opt/back\\slash/app");
        Assert.Contains("back\\\\\\\\slash", slash);
        var quote = StartupRegistration.BuildDesktopEntry("/opt/quoted\"name/app");
        Assert.Contains("quoted\\\\\"name", quote);
    }

    [Theory]
    [InlineData("https://127.0.0.1/app.exe")]
    [InlineData("file:///C:/app.exe")]
    [InlineData("C:app.exe")]
    [InlineData("app.exe")]
    [InlineData("\\\\server\\share\\app.exe")]
    [InlineData("C:\\bad`name\\app.exe")]
    [InlineData("C:\\bad\nname\\app.exe")]
    [InlineData("C:\\bad\"name\\app.exe")]
    [InlineData("C:\\app.exe:stream")]
    public void WindowsBuilderRefusesAmbiguousOrNonlocalPaths(string path)
        => Assert.Throws<ArgumentException>(() => StartupRegistration.BuildWindowsCommand(path));

    [Theory]
    [InlineData("https://127.0.0.1/app")]
    [InlineData("file:///opt/app")]
    [InlineData("app")]
    [InlineData("//server/share/app")]
    [InlineData("/opt/bad`name/app")]
    [InlineData("/opt/bad\rname/app")]
    [InlineData("/opt/bad\nname/app")]
    [InlineData("/opt/name=value/app")]
    public void LinuxBuilderRefusesAmbiguousOrNonlocalPaths(string path)
        => Assert.Throws<ArgumentException>(() => StartupRegistration.BuildDesktopEntry(path));

    [Fact]
    public void WindowsRegistrationOnlyWritesOnExplicitCallsAndDisableIgnoresStalePaths()
    {
        var writes = new List<string?>(); var registration = new StartupRegistration(windows: true, windowsWriter: writes.Add);
        Assert.Empty(writes);
        Assert.True(registration.SetEnabled(true, @"C:\Programs\CCP Classic\app.exe").Succeeded);
        Assert.Equal(StartupRegistration.BuildWindowsCommand(@"C:\Programs\CCP Classic\app.exe"), Assert.Single(writes));
        Assert.False(registration.SetEnabled(true, "https://127.0.0.1/app.exe").Succeeded); Assert.Single(writes);
        Assert.True(registration.SetEnabled(false, "missing old executable").Succeeded); Assert.Null(writes[1]);
        Assert.True(registration.SetEnabled(false).Succeeded); Assert.Null(writes[2]);
    }

    [Fact]
    public void WindowsWriterFailuresAreReturnedAndRunCommandsHaveABoundedLength()
    {
        var registration = new StartupRegistration(windows: true, windowsWriter: _ => throw new UnauthorizedAccessException("Denied"));
        Assert.False(registration.SetEnabled(true, @"C:\app.exe").Succeeded);
        Assert.False(registration.SetEnabled(false).Succeeded);
        Assert.Throws<ArgumentException>(() => StartupRegistration.BuildWindowsCommand("C:\\" + new string('a', 260) + "\\app.exe"));
        Assert.Throws<ArgumentException>(() => StartupRegistration.BuildWindowsCommand(@"C:\dotnet.exe", @"C:\not-an-app.txt"));
    }

    [Fact]
    public void LinuxWritesOnlyItsOwnedEntryAtomicallyAndPreservesOtherApplications()
    {
        using var temporary = new Temporary(); var registration = new StartupRegistration(temporary.Path, windows: false);
        Assert.False(Directory.Exists(temporary.Path));
        Assert.True(registration.SetEnabled(false).Succeeded); Assert.False(Directory.Exists(temporary.Path));
        Assert.True(registration.SetEnabled(true, "/opt/My App/CCP.Classic.App").Succeeded);
        var directory = System.IO.Path.Combine(temporary.Path, "autostart");
        var entry = System.IO.Path.Combine(directory, StartupRegistration.DesktopFileName);
        Assert.Equal(StartupRegistration.BuildDesktopEntry("/opt/My App/CCP.Classic.App"), File.ReadAllText(entry));
        Assert.False(File.ReadAllBytes(entry).AsSpan().StartsWith(Encoding.UTF8.Preamble));
        var unrelated = System.IO.Path.Combine(directory, "other.desktop"); File.WriteAllText(unrelated, "Other application");
        Assert.True(registration.SetEnabled(true, "/opt/New App/CCP.Classic.App").Succeeded);
        Assert.DoesNotContain("/opt/My App", File.ReadAllText(entry)); Assert.Equal(2, Directory.GetFiles(directory).Length);
        Assert.True(registration.SetEnabled(false, "missing-app").Succeeded); Assert.False(File.Exists(entry));
        Assert.Equal("Other application", File.ReadAllText(unrelated));
    }

    [Fact]
    public void LinuxIoFailureIsReturnedWithoutCreatingAnEntryOrTemporaryRemnants()
    {
        using var temporary = new Temporary(); Directory.CreateDirectory(temporary.Path);
        File.WriteAllText(System.IO.Path.Combine(temporary.Path, "autostart"), "A file blocks the directory");
        var registration = new StartupRegistration(temporary.Path, windows: false);
        var result = registration.SetEnabled(true, "/opt/app"); Assert.False(result.Succeeded); Assert.NotNull(result.Error);
        Assert.Single(Directory.GetFiles(temporary.Path));
    }

    private sealed class Temporary : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ccp-classic-startup-" + Guid.NewGuid().ToString("N"));
        public void Dispose()
        {
            // This fixture creates only its three named files; never recurse into user configuration.
            foreach (var file in new[] { "autostart/ccp-classic.desktop", "autostart/other.desktop", "autostart" })
            {
                var path = System.IO.Path.Combine(Path, file);
                if (File.Exists(path)) File.Delete(path);
            }
            var directory = System.IO.Path.Combine(Path, "autostart");
            if (Directory.Exists(directory)) Directory.Delete(directory);
            if (Directory.Exists(Path)) Directory.Delete(Path);
        }
    }
}
