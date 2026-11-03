using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class SettingsTests
{
    [Fact]
    public void SettingsRoundTripLocallyAndRecoverFromTruncation()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var store = new SettingsStore(path);
            Assert.Equal("Escape", store.Load(out var recovered).PanicKey);
            Assert.False(recovered);
            var settings = new ClassicSettings { Language = "it", GentleVisuals = true, RunMinutes = 30 };
            settings.Effects["flash"].Values["rate"] = 18;
            store.Save(settings);
            var loaded = store.Load(out recovered);
            Assert.False(recovered);
            Assert.Equal("it", loaded.Language);
            Assert.True(loaded.GentleVisuals);
            Assert.Equal(18, loaded.Effects["flash"].Values["rate"]);
            File.WriteAllText(path, "{");
            Assert.Equal("Escape", store.Load(out recovered).PanicKey);
            Assert.True(recovered);
            Assert.Single(Directory.GetFiles(directory));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }

    [Fact]
    public void SettingsClampUnsafeAndMissingValues()
    {
        var settings = new ClassicSettings { RunMinutes = -1 };
        settings.Effects["flash"].Values["rate"] = 999;
        settings.Effects["video"].Values.Clear();
        settings.Normalize();
        Assert.Equal(0, settings.RunMinutes);
        Assert.Equal(60, settings.Effects["flash"].Values["rate"]);
        Assert.Equal(8, settings.Effects["video"].Values["cap"]);
        Assert.All(EffectCatalog.All, d => Assert.InRange(d.Primary.Count, 1, 3));
    }
}
