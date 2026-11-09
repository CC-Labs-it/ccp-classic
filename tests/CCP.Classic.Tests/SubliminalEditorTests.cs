using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Classic.App.Controls;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class SubliminalEditorTests
{
    private static readonly LocalizationService Text = new("en");

    [Fact]
    public Task TypingInALaterBlankRowEditsThatRow() => UiDispatcher.Run(() =>
    {
        var options = new SubliminalOptions { Phrases = [new("A")] };
        var editor = new SubliminalOptionsEditor(options, Text, () => { });
        Click(Button(editor, "Add")); Click(Button(editor, "Add"));
        Type(Boxes(editor)[2], "second");
        Click(Button(editor, "Add"));
        Assert.Equal(["A", "", "second", ""], Boxes(editor).Select(box => box.Text ?? ""));
        Assert.Equal(["A", "second"], options.Phrases.Select(phrase => phrase.Text));
    });

    [Fact]
    public Task RemoveTakesTheExactRowAmongBlankRows() => UiDispatcher.Run(() =>
    {
        var options = new SubliminalOptions { Phrases = [new("A")] };
        var editor = new SubliminalOptionsEditor(options, Text, () => { });
        Click(Button(editor, "Add")); Click(Button(editor, "Add"));
        Type(Boxes(editor)[2], "x");
        Click(Buttons(editor, "Remove")[1]);
        Assert.Equal(["A", "x"], Boxes(editor).Select(box => box.Text ?? ""));
        Assert.Equal(["A", "x"], options.Phrases.Select(phrase => phrase.Text));
    });

    [Fact]
    public Task AutosaveBetweenAddAndTypingKeepsTheNewText() => UiDispatcher.Run(() =>
    {
        // Saving normalizes the live settings: blank rows leave the list and text is trimmed.
        var options = new SubliminalOptions { Phrases = [new("A")] };
        var editor = new SubliminalOptionsEditor(options, Text, () => { });
        Click(Button(editor, "Add"));
        options.Normalize();
        Type(Boxes(editor)[1], "new ");
        options.Normalize();
        Type(Boxes(editor)[1], "new words");
        Assert.Equal(["A", "new words"], options.Phrases.Select(phrase => phrase.Text));
        Click(Buttons(editor, "Remove")[1]);
        Assert.Equal(["A"], options.Phrases.Select(phrase => phrase.Text));
        Assert.Equal(["A"], Boxes(editor).Select(box => box.Text ?? ""));
    });

    [Fact]
    public Task DuplicateTextRowsStaySeparate() => UiDispatcher.Run(() =>
    {
        var options = new SubliminalOptions { Phrases = [new("A")] };
        var editor = new SubliminalOptionsEditor(options, Text, () => { });
        Click(Button(editor, "Add")); Type(Boxes(editor)[1], "A");
        Type(Boxes(editor)[1], "AB");
        Assert.Equal(["A", "AB"], Boxes(editor).Select(box => box.Text ?? ""));
        Assert.Equal(["A", "AB"], options.Phrases.Select(phrase => phrase.Text));
    });

    [Fact]
    public Task AddFocusesTheNewBoxAndScrollsItIntoView() => UiDispatcher.Run(() =>
    {
        var options = new SubliminalOptions { Phrases = Enumerable.Range(1, 30).Select(i => new SubliminalPhrase("P" + i)).ToList() };
        var editor = new SubliminalOptionsEditor(options, Text, () => { });
        var scroller = new ScrollViewer { Content = editor };
        var window = new Window { Width = 500, Height = 300, Content = scroller };
        try
        {
            window.Show(); window.Activate(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, scroller.Offset.Y);
            var add = Button(editor, "Add"); add.BringIntoView(); Dispatcher.UIThread.RunJobs();
            Click(add); Dispatcher.UIThread.RunJobs();
            var added = Boxes(editor)[^1];
            Assert.Equal("", added.Text ?? "");
            Assert.True(added.IsFocused);
            var top = added.TranslatePoint(new Point(0, 0), scroller);
            Assert.NotNull(top);
            Assert.InRange(top.Value.Y, 0, scroller.Viewport.Height - added.Bounds.Height);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    private static TextBox[] Boxes(Control editor) =>
        editor.GetLogicalDescendants().OfType<TextBox>().Where(box => box.AcceptsReturn).ToArray();

    private static Button[] Buttons(Control editor, string key) =>
        editor.GetLogicalDescendants().OfType<Button>().Where(button => Equals(button.Content, Text[key])).ToArray();

    private static Button Button(Control editor, string key) => Assert.Single(Buttons(editor, key));

    private static void Type(TextBox box, string value) { box.Text = value; Dispatcher.UIThread.RunJobs(); }

    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }
}
