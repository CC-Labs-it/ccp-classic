using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CCP.Classic.App.Controls;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class LoomEditorTests
{
    [Fact]
    public Task ThreePrimaryControlsAndAdvancedFieldsUpdateTheActualDefinition() => WithEditor(async (editor, store) =>
    {
        await UiDispatcher.Run(() =>
        {
            Assert.Equal(3, Named<StackPanel>(editor, "LoomPrimary").GetLogicalDescendants().OfType<NumericUpDown>().Count());
            Assert.False(Named<Expander>(editor, "Group_More").IsExpanded);
            void Number(string id, decimal value) => Named<NumericUpDown>(editor, id).Value = value;
            Number("LoomArms", 5); Number("LoomTurns", 4.75m); Number("LoomBody", 75);
            Named<ComboBox>(editor, "LoomStyle").SelectedIndex = 4;
            Named<ComboBox>(editor, "LoomDirection").SelectedIndex = 1;
            Named<ComboBox>(editor, "LoomFormat").SelectedIndex = 1;
            Named<CheckBox>(editor, "LoomGradient").IsChecked = true;
            Named<CheckBox>(editor, "LoomRadial").IsChecked = true;
            Number("LoomSpeed", 4); Number("LoomLayerSpeed", 2);
            Named<CheckBox>(editor, "LoomSecondEnabled").IsChecked = true;
            Number("LoomSecondArms", 8); Number("LoomSecondTurns", 3.25m); Number("LoomSecondBody", 35); Number("LoomSecondLayerSpeed", 3);
            Named<ComboBox>(editor, "LoomSecondStyle").SelectedIndex = 5;
            Number("LoomGlow", 80); Number("LoomPulse", 16); Number("LoomPulseCycles", 4);
            Number("LoomWobble", 24); Number("LoomRipples", 6); Number("LoomWobbleCycles", 3); Number("LoomHue", 2);
            Named<ComboBox>(editor, "LoomCenterpiece").SelectedIndex = 5;
            Named<TextBox>(editor, "LoomWord").Text = "focus"; Number("LoomFlash", 3); Number("LoomCenterSize", 30);
            Number("LoomColour0_LoomRed", 16); Number("LoomColour0_LoomGreen", 32); Number("LoomColour0_LoomBlue", 48);
            var q = editor.Definition;
            Assert.Equal((5, 4.75, .75, "tunnel", -1, 2), (q.Layer.Arms, q.Layer.Turns, q.Layer.Duty, q.Layer.Style, q.Layer.Direction, q.Layer.SpeedMul));
            Assert.Equal("gradient", q.Layer.BandMode); Assert.Equal("#102030", q.Layer.Colors[0]);
            Assert.Equal("wide", q.Format); Assert.Equal("radial", q.Bg.Kind); Assert.Equal(4, q.Speed);
            Assert.True(q.Layer2.Enabled); Assert.Equal((8, 3.25, "petal", 3), (q.Layer2.Arms, q.Layer2.Turns, q.Layer2.Style, q.Layer2.SpeedMul)); Assert.Equal(.35, q.Layer2.Duty, 8);
            Assert.Equal(new LoomPulse(.16, 4), q.Pulse); Assert.Equal(new LoomWobble(.24, 6, 3), q.Wobble);
            Assert.Equal(.8, q.Glow); Assert.Equal(2, q.HueCycles);
            Assert.Equal(("mantra", "focus", 3, .3), (q.Centerpiece.Kind, q.Centerpiece.Text, q.Centerpiece.FlashCycles, q.Centerpiece.SizeFrac));
            Click(editor, "LoomAddColour"); Assert.Equal(2, editor.Definition.Layer.Colors.Length);
            Click(editor, "LoomRemoveColour1"); Assert.Single(editor.Definition.Layer.Colors);
        });
    });

    [Fact]
    public Task SavedRackLoadsOverwritesAndDeletesWithNativeThumbnails() => WithEditor(async (editor, store) =>
    {
        var selected = new List<SavedLoom>(); string? deleted = null;
        await UiDispatcher.Run(() =>
        {
            editor.SavedSelection += selected.Add; editor.Deleted += id => deleted = id;
            Named<TextBox>(editor, "LoomName").Text = "Custom";
            Named<NumericUpDown>(editor, "LoomArms").Value = 7; Click(editor, "LoomSave");
        });
        await Idle(editor);
        var saved = Assert.Single(store.LoadAll());
        await UiDispatcher.Run(() =>
        {
            Assert.Equal("LoomSaved", editor.StatusKey); Assert.Equal(saved.Id, editor.SelectedId);
            Assert.Single(editor.GetLogicalDescendants().OfType<Image>());
            Assert.Single(selected);
            Click(editor, "LoomNew"); Assert.Null(editor.SelectedId); Assert.Equal(4, editor.Definition.Layer.Arms);
            Click(editor, "LoomLoad_" + saved.Id); Assert.Equal(7, editor.Definition.Layer.Arms);
            Named<TextBox>(editor, "LoomName").Text = "Renamed"; Click(editor, "LoomSave");
        });
        await Idle(editor);
        Assert.Equal(saved.Id, Assert.Single(store.LoadAll()).Id); Assert.Equal("Renamed", Assert.Single(store.LoadAll()).Name);
        await UiDispatcher.Run(() => Click(editor, "LoomDelete_" + saved.Id)); await Idle(editor);
        Assert.Empty(store.LoadAll());
        await UiDispatcher.Run(() => { Assert.Equal(saved.Id, deleted); Assert.Null(editor.SelectedId); Assert.Empty(editor.GetLogicalDescendants().OfType<Image>()); });
    });

    [Fact]
    public Task InvalidNamesShowALocalizedFailureWithoutASavedSelection() => WithEditor(async (editor, store) =>
    {
        var selected = 0;
        await UiDispatcher.Run(() => { editor.SavedSelection += _ => selected++; Named<TextBox>(editor, "LoomName").Text = "  "; Click(editor, "LoomSave"); });
        await Idle(editor);
        Assert.Empty(store.LoadAll()); Assert.Equal(0, selected);
        await UiDispatcher.Run(() =>
        {
            Assert.Equal("LoomSaveFailed", editor.StatusKey);
            Assert.Equal(new LocalizationService()["LoomSaveFailed"], Named<TextBlock>(editor, "LoomStatus").Text);
        });
    });

    private static async Task Idle(LoomEditor editor)
    {
        for (var count = 0; count < 200; count++)
        {
            var ready = false; await UiDispatcher.Run(() => ready = !editor.IsBusy);
            if (ready) return;
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        Assert.Fail("Spiral editor did not finish its local operation");
    }
    private static T Named<T>(LoomEditor editor, string name) where T : Control =>
        Assert.Single(editor.GetLogicalDescendants().OfType<T>(), control => control.Name == name);
    private static void Click(LoomEditor editor, string name)
    {
        var button = Named<Button>(editor, name); Assert.True(button.IsEffectivelyEnabled);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private static async Task WithEditor(Func<LoomEditor, LoomStore, Task> action)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var store = new LoomStore(root);
        LoomEditor? editor = null; Window? window = null;
        try
        {
            await UiDispatcher.Run(() => { editor = new(new() { ReducedMotion = true }, new(), store); window = new() { Width = 600, Height = 700, Content = editor }; window.Show(); });
            await Idle(editor!); await action(editor!, store);
        }
        finally
        {
            await UiDispatcher.Run(() => window?.Close());
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
