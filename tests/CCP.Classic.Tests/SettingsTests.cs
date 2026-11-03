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
        Assert.Equal(180, settings.Effects["flash"].Values["rate"]);
        Assert.Equal(8, settings.Effects["video"].Values["cap"]);
        Assert.All(EffectCatalog.All, d => Assert.InRange(d.Primary.Count, 1, 3));
    }

    [Fact]
    public void OlderPreviewFilesAcquireExitAndBubbleDefaultsAndKeepPersistedChoices()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        try
        {
            File.WriteAllText(path, """
                {"SchemaVersion":1,"Language":"it","PanicKey":"F8","Effects":{
                  "flash":{"Enabled":true,"Values":{"rate":18,"opacity":71}},
                  "bubble":{"Enabled":true,"Values":{"rate":12,"click":1,"sound":0}}}}
                """);
            var store = new SettingsStore(path); var loaded = store.Load(out var recovered);
            Assert.False(recovered); Assert.Equal("it", loaded.Language); Assert.Equal("F8", loaded.PanicKey);
            Assert.Equal(18, loaded.Effects["flash"].Values["rate"]); Assert.Equal(0, loaded.Effects["bubble"].Values["sound"]);
            Assert.Equal(0, loaded.Effects["flash"].Values["exit"]);
            Assert.Equal(1, loaded.Effects["bubble"].Values["effects"]);
            Assert.Equal(10, loaded.Effects["bubble"].Values["chance"]); Assert.Equal(0, loaded.Effects["bubble"].Values["kind"]);
            loaded.Effects["flash"].Values["exit"] = 999;
            loaded.Effects["bubble"].Values["effects"] = 0; loaded.Effects["bubble"].Values["chance"] = 37;
            loaded.Effects["bubble"].Values["kind"] = 999;
            store.Save(loaded); var restored = store.Load(out recovered);
            Assert.False(recovered); Assert.Equal(6, restored.Effects["flash"].Values["exit"]);
            Assert.Equal(0, restored.Effects["bubble"].Values["effects"]);
            Assert.Equal(37, restored.Effects["bubble"].Values["chance"]); Assert.Equal(9, restored.Effects["bubble"].Values["kind"]);
            Assert.Equal(71, restored.Effects["flash"].Values["opacity"]); Assert.Equal(0, restored.Effects["bubble"].Values["sound"]);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
