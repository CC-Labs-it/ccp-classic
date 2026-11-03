using System.Text.Json;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using CCP.Classic.App.Controls;
using CCP.Classic.Core.LocalDevices;
using CCP.Classic.Core.Localization;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class ToyInputEditorTests
{
    [Fact]
    public Task ButtonPreferencesAndIndependentScriptScaleAreRealControlsWithoutAnAutomaticConnection() => UiDispatcher.Run(() =>
    {
        var created = 0; using var controller = new LocalDeviceController(() => { created++; return new(); });
        var options = new LocalDevicePreferences(); var saves = 0;
        var editor = new ToyInputOptionsEditor(options, controller, new LocalizationService("en"), () => saves++);
        var checks = editor.GetLogicalDescendants().OfType<CheckBox>().ToArray();
        var input = checks.Single(control => control.Name == "ToyInputEnabled"); var attention = checks.Single(control => control.Name == "ToyAttentionButton");
        attention.IsChecked = true; Assert.True(options.AttentionCheckToyButton);
        input.IsChecked = false; Assert.False(controller.InputEnabled); Assert.False(attention.IsEnabled);
        input.IsChecked = true; Assert.True(controller.InputEnabled); Assert.True(attention.IsEnabled);
        editor.GetLogicalDescendants().OfType<NumericUpDown>().Single(control => control.Name == "FunScriptIntensity").Value = 25;
        Assert.Equal(.25, options.FunScriptIntensity); Assert.True(saves >= 4); Assert.Equal(0, created);
        var roundtrip = JsonSerializer.Deserialize<LocalDevicePreferences>(JsonSerializer.Serialize(options))!;
        Assert.True(roundtrip.ToyInputEnabled); Assert.True(roundtrip.AttentionCheckToyButton); Assert.Equal(.25, roundtrip.FunScriptIntensity);
    });
}
