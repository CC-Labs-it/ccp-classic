using System.Text.Json;
using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class PresetTests
{
    [Fact]
    public void BuiltInsHaveDeterministicIndependentEffectProfiles()
    {
        var presets = PresetStore.BuiltIns;
        var gentle = presets[0]; var daily = presets[1]; var deep = presets[2];
        Assert.Equal(["Gentle", "Daily", "Deep"], presets.Select(p => p.Name));
        Assert.Equal("builtin-gentle", gentle.Id);
        Assert.True(gentle.GentleVisuals);
        Assert.Equal(["audio", "flash", "sub"], Enabled(gentle));
        Assert.Equal(["audio", "bubble", "flash", "pink", "spiral", "sub"], Enabled(daily));
        Assert.Equal(11, Enabled(deep).Length); Assert.False(deep.Effects["wall"].Enabled);
        Assert.True(deep.Effects["video"].Enabled); Assert.Equal(0, deep.Effects["video"].Values["strict"]);
        Assert.Equal(6, gentle.Effects["flash"].Values["rate"]);
        Assert.Equal(30, gentle.Effects["flash"].Values["size"]);
        Assert.Equal(55, gentle.Effects["flash"].Values["opacity"]);
        Assert.Equal(4, daily.Effects["sub"].Values["rate"]);
        Assert.Equal(25, daily.Effects["sub"].Values["opacity"]);
        gentle.Effects["flash"].Values["rate"] = 60;
        Assert.Equal(6, PresetStore.BuiltIns[0].Effects["flash"].Values["rate"]);
        Assert.Equal(6, daily.Effects["flash"].Values["rate"]);
    }

    [Fact]
    public void CaptureAndApplyDoNotShareEffectsOrReplacePersonalSettings()
    {
        var source = new ClassicSettings { RunMinutes = 30, GentleVisuals = true, ReducedMotion = true };
        source.Effects["flash"].Values["rate"] = 18;
        var preset = PresetStore.Capture("  Study  ", source);
        source.Effects["flash"].Values["rate"] = 50;
        var target = new ClassicSettings
        {
            Language = "it", PanicKey = "F8", StartWithSystem = true, StartHidden = true,
            AutoRun = true, FillAllMonitors = false, PerformanceMode = true,
            KeywordTriggersEnabled = true, CompanionEnabled = true
        };
        target.Effects["audio"].Values["output"] = 123;
        PresetStore.Apply(preset, target);
        Assert.Equal(18, target.Effects["flash"].Values["rate"]);
        Assert.Equal(123, target.Effects["audio"].Values["output"]);
        Assert.Equal("Study", target.Preset); Assert.Equal(30, target.RunMinutes);
        Assert.True(target.GentleVisuals); Assert.True(target.ReducedMotion);
        Assert.Equal("it", target.Language); Assert.Equal("F8", target.PanicKey);
        Assert.True(target.StartWithSystem); Assert.True(target.StartHidden); Assert.True(target.AutoRun);
        Assert.False(target.FillAllMonitors); Assert.True(target.PerformanceMode);
        Assert.True(target.KeywordTriggersEnabled); Assert.True(target.CompanionEnabled);
        target.Effects["flash"].Values["rate"] = 40;
        Assert.Equal(18, preset.Effects["flash"].Values["rate"]);
        Assert.False(preset.Effects["audio"].Values.ContainsKey("output"));
    }

    [Fact]
    public void CustomPresetsRoundTripAtomicallyAndDeleteByIdentifier()
    {
        using var fixture = new PresetFixture();
        var preset = PresetStore.Capture("My preset", new ClassicSettings());
        var saved = fixture.Store.Save(preset);
        preset.Effects["flash"].Enabled = false;
        var loaded = Assert.Single(fixture.Store.LoadAll(out var recovered));
        Assert.False(recovered); Assert.Equal(saved.Id, loaded.Id);
        Assert.True(loaded.Effects["flash"].Enabled);
        loaded.Name = "Renamed"; fixture.Store.Save(loaded);
        Assert.Single(Directory.GetFiles(fixture.Root));
        Assert.Equal("Renamed", Assert.Single(fixture.Store.LoadAll(out recovered)).Name);
        fixture.Store.Delete(saved.Id); fixture.Store.Delete(saved.Id);
        Assert.Empty(fixture.Store.LoadAll(out recovered)); Assert.False(recovered);
    }

    [Fact]
    public void DamagedOversizedUnknownVersionAndMisnamedFilesRecoverBesideValidPresets()
    {
        using var fixture = new PresetFixture();
        fixture.Store.Save(PresetStore.Capture("Valid", new ClassicSettings()));
        fixture.Write(Guid.NewGuid().ToString("N"), "{");
        fixture.Write(Guid.NewGuid().ToString("N"), new string(' ', 256 * 1024 + 1));
        var future = PresetStore.Capture("Future", new ClassicSettings()); future.SchemaVersion = 2;
        fixture.Write(future.Id, JsonSerializer.Serialize(future));
        var mismatch = PresetStore.Capture("Mismatch", new ClassicSettings());
        fixture.Write(Guid.NewGuid().ToString("N"), JsonSerializer.Serialize(mismatch));
        fixture.Write("unexpected", "{}");
        Assert.Equal("Valid", Assert.Single(fixture.Store.LoadAll(out var recovered)).Name);
        Assert.True(recovered);
    }

    [Fact]
    public void ApplyingUnknownAndIncompleteEffectsFiltersAndNormalizesWithoutActivatingMissingEffects()
    {
        var definition = new PresetDefinition { Name = "Imported", RunMinutes = -10 };
        definition.Effects["unknown"] = new() { Enabled = true };
        definition.Effects["flash"] = new() { Enabled = true, Values = new() { ["rate"] = 999, ["size"] = double.NaN, ["other"] = 3 } };
        var target = new ClassicSettings();
        PresetStore.Apply(definition, target);
        Assert.False(target.Effects.ContainsKey("unknown"));
        Assert.False(target.Effects["flash"].Values.ContainsKey("other"));
        Assert.Equal(60, target.Effects["flash"].Values["rate"]);
        Assert.Equal(45, target.Effects["flash"].Values["size"]);
        Assert.False(target.Effects["spiral"].Enabled); Assert.Equal(0, target.RunMinutes);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("builtin-gentle")]
    [InlineData("")]
    public void IdentifiersCannotEscapeTheStoreOrOverwriteBuiltIns(string id)
    {
        using var fixture = new PresetFixture();
        var definition = PresetStore.Capture("Name", new ClassicSettings()); definition.Id = id;
        Assert.Throws<ArgumentException>(() => fixture.Store.Save(definition));
        Assert.Throws<ArgumentException>(() => fixture.Store.Delete(id));
        Assert.Empty(Directory.GetFiles(fixture.Root));
    }

    [Fact]
    public void NamesAreBoundedAndMissingStoreIsEmpty()
    {
        using var fixture = new PresetFixture();
        Assert.Throws<ArgumentException>(() => PresetStore.Capture(" ", new ClassicSettings()));
        Assert.Throws<ArgumentException>(() => PresetStore.Capture("a\nb", new ClassicSettings()));
        Assert.Throws<ArgumentException>(() => PresetStore.Capture(new string('a', 81), new ClassicSettings()));
        Directory.Delete(fixture.Root);
        Assert.Empty(fixture.Store.LoadAll(out var recovered)); Assert.False(recovered);
    }

    private static string[] Enabled(PresetDefinition preset) => preset.Effects.Where(p => p.Value.Enabled)
        .Select(p => p.Key).Order(StringComparer.Ordinal).ToArray();

    private sealed class PresetFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public PresetStore Store { get; }
        public PresetFixture() { Directory.CreateDirectory(Root); Store = new(Root); }
        public void Write(string id, string value) => File.WriteAllText(Path.Combine(Root, id + ".json"), value);
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
    }
}
