using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using CCP.Classic.App.Controls;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class OptionLayoutTests
{
    [Fact]
    public Task PairedOptionsStackWithoutClippingLongLabelsOnNarrowWindows() => UiDispatcher.Run(() =>
    {
        var panel = new OptionPairsPanel();
        foreach (var label in new[] { "Bilder mit aufgezeichnetem Audio verknüpfen", "Gelegentlich animierte Bildzusammenstellungen anzeigen" })
        {
            var option = OptionHelp.Wrap(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, "FlashAudio", new LocalizationService("de"));
            option.Classes.Add("option-compact"); panel.Children.Add(option);
        }
        panel.Measure(new Size(700, double.PositiveInfinity)); panel.Arrange(new Rect(0, 0, 700, panel.DesiredSize.Height));
        Assert.Equal(panel.Children[0].Bounds.Y, panel.Children[1].Bounds.Y);
        Assert.True(panel.Children[1].Bounds.X > panel.Children[0].Bounds.Right);
        panel.Measure(new Size(290, double.PositiveInfinity)); panel.Arrange(new Rect(0, 0, 290, panel.DesiredSize.Height));
        Assert.True(panel.Children[1].Bounds.Y >= panel.Children[0].Bounds.Bottom);
        Assert.All(panel.Children, child => Assert.True(child.Bounds.Right <= 290));
        Assert.All(panel.GetLogicalDescendants().OfType<TextBlock>(), label => Assert.True(label.DesiredSize.Width <= 260));
    });

    [Fact]
    public Task OpeningHelpDoesNotToggleOrSaveTheSetting() => UiDispatcher.Run(() =>
    {
        var settings = new ClassicSettings(); var changes = 0;
        var editor = new FlashOptionsEditor(settings, new LocalizationService(), () => changes++);
        var controls = editor.GetLogicalDescendants().OfType<Control>().ToArray();
        var toggle = controls.OfType<CheckBox>().Single(control => control.Name == "FlashStay");
        var initial = toggle.IsChecked;
        controls.OfType<Button>().Single(control => control.Name == "Help_FlashStay").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(initial, toggle.IsChecked); Assert.Equal(0, changes);
        toggle.IsChecked = !initial; Assert.Equal(1, changes);
    });
}
