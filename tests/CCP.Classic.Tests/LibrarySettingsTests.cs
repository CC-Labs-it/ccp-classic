using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class LibrarySettingsTests
{
    [Fact]
    public void SettingsRecoverNullLibraryAndFilterRemotePaths()
    {
        var settings = new ClassicSettings { Library = null! };
        settings.Normalize();
        Assert.NotNull(settings.Library);
        settings.Library.VideoFolders = ["https://example.invalid/video", @"\\server\share", "relative"];
        settings.Normalize();
        Assert.Empty(settings.Library.VideoFolders);
    }

    [Fact]
    public void CheckedFileSelectionsRoundTripWithoutCopyingFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), "classic-library-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "settings.json");
        try
        {
            var selected = Path.Combine(folder, "selected.mp4");
            var excluded = Path.Combine(folder, "excluded.mp4");
            var settings = new ClassicSettings();
            settings.Library.VideoFolders.Add(folder);
            settings.Library.Include(excluded, false);
            settings.Library.Include(excluded, false);
            Assert.Single(settings.Library.ExcludedFiles);
            new SettingsStore(path).Save(settings);
            var loaded = new SettingsStore(path).Load(out var recovered);
            Assert.False(recovered);
            Assert.Equal(folder, Assert.Single(loaded.Library.VideoFolders));
            Assert.True(loaded.Library.Includes(selected));
            Assert.False(loaded.Library.Includes(excluded));
            loaded.Library.Include(excluded, true);
            Assert.Empty(loaded.Library.ExcludedFiles);
            Assert.Single(Directory.GetFiles(folder));
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }
}
