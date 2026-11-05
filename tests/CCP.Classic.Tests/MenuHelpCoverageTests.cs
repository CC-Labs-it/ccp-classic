using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Classic.App;
using CCP.Classic.App.Controls;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class MenuHelpCoverageTests
{
    [Fact]
    public Task EveryMenuOptionHasHelpAndEveryDescriptionExistsInAllLanguages() => UiDispatcher.Run(() =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "classic-menu-help-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var missing = new List<string>();
        var settings = new ClassicSettings { ReducedMotion = true, GentleVisuals = true };
        var window = new MainWindow(settings, windows: true, desktopEffects: false,
            presetStore: new PresetStore(Path.Combine(folder, "presets")));
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            foreach (var effect in EffectCatalog.All)
            {
                window.OpenEffect(effect.Id); Dispatcher.UIThread.RunJobs();
                Inspect(window.SettingsWindow!, effect.Id, keys, missing);
                if (effect.Id is "flash" or "video")
                {
                    var editor = window.SettingsWindow!;
                    Proof(editor, "menu-" + effect.Id + "-compact");
                    editor.GetLogicalDescendants().OfType<Button>().Single(button => button.Name == "More_" + effect.Id)
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Proof(editor, "menu-" + effect.Id + "-expanded");
                    if (effect.Id == "flash")
                    {
                        editor.Width = 420;
                        Proof(editor, "menu-flash-narrow");
                    }
                }
            }
            foreach (var sheet in new[] { "Settings", "Companion", "Toys", "Library" })
            {
                window.OpenSheet(sheet); Dispatcher.UIThread.RunJobs();
                Inspect(window.SettingsWindow!, sheet, keys, missing);
                if (sheet is "Settings" or "Companion" or "Toys")
                {
                    Proof(window.SettingsWindow!, "menu-" + sheet.ToLowerInvariant());
                    foreach (var expander in window.SettingsWindow!.GetLogicalDescendants().OfType<Expander>().ToArray())
                        expander.IsExpanded = true;
                    Proof(window.SettingsWindow!, "menu-" + sheet.ToLowerInvariant() + "-expanded");
                }
            }
            window.SelectTab("Presets"); Dispatcher.UIThread.RunJobs();
            Inspect(Assert.Single(window.GetLogicalDescendants().OfType<PresetPanel>()), "Presets", keys, missing);
            using var loom = new LoomEditor(settings, new(), new LoomStore(Path.Combine(folder, "loom")));
            Inspect(loom, "Loom", keys, missing);
            Assert.True(missing.Count == 0, "Options without nearby help: " + string.Join(", ", missing));
            Assert.True(keys.Count > 100, "The inventory must include advanced controls, not just menu headings.");
            var absent = new List<string>();
            foreach (var language in LocalizationService.Languages)
            {
                var text = new LocalizationService(language.Code);
                foreach (var key in keys)
                {
                    var description = OptionHelp.Description(key, text);
                    if (string.IsNullOrWhiteSpace(description) || description == text["HelpSettingsBody"]
                        || description == key || description == text[key] || description.StartsWith("OptionHelp_", StringComparison.Ordinal))
                        absent.Add(language.Code + "/" + key);
                }
            }
            Assert.True(absent.Count == 0, "Missing option descriptions: " + string.Join(", ", absent));
        }
        finally
        {
            window.Close(); Dispatcher.UIThread.RunJobs();
            Directory.Delete(folder, true);
        }
    });

    private static void Proof(Window window, string name)
    {
        var folder = Environment.GetEnvironmentVariable("CCP_CLASSIC_PROOF_DIR");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        Assert.True(window.ClientSize.Width > 0 && window.ClientSize.Height > 0);
        using var image = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(window.ClientSize.Width),
            (int)Math.Ceiling(window.ClientSize.Height)));
        image.Render(window); Directory.CreateDirectory(folder);
        image.Save(Path.Combine(folder, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    private static void Inspect(Control surface, string name, HashSet<string> keys, List<string> missing)
    {
        var controls = surface.GetLogicalDescendants().OfType<Control>().ToArray();
        foreach (var button in controls.OfType<Button>().Where(button => button.Name?.StartsWith("Help_", StringComparison.Ordinal) == true
            && button.GetLogicalParent() is Control parent && parent.Classes.Contains("option-help-row")))
        {
            keys.Add(button.Name![5..]);
            Assert.NotNull(button.Flyout);
            Assert.NotNull(ToolTip.GetTip(button));
        }
        foreach (var control in controls.Where(control => control.TemplatedParent is null && control.IsEnabled
            && control is CheckBox or ToggleSwitch or ToggleButton or Slider or NumericUpDown or ComboBox or TextBox))
        {
            if (ToolTip.GetTip(control) is not null) continue;
            var nearby = control.GetLogicalAncestors().Take(3).OfType<Control>().Any(parent =>
                parent.Classes.Contains("option-help-row") || parent is Grid && parent.GetLogicalDescendants()
                    .OfType<Button>().Any(button => button.Name?.StartsWith("Help_", StringComparison.Ordinal) == true));
            if (!nearby) missing.Add(name + "/" + (control.Name ?? control.GetType().Name));
        }
    }
}
