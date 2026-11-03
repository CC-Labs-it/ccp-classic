using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Classic.App;
using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class PanicKeysTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Banana")]
    [InlineData("None")]
    [InlineData("LeftShift")]
    [InlineData("RightCtrl")]
    [InlineData("LWin")]
    [InlineData("System")]
    [InlineData("ImeProcessed")]
    [InlineData("DeadCharProcessed")]
    [InlineData("NoName")]
    [InlineData("42")]
    [InlineData("+42")]
    [InlineData("-1")]
    [InlineData("99999")]
    [InlineData("F8, Escape")]
    public void InvalidPersistedKeysRestoreEscape(string? value) =>
        Assert.Equal("Escape", PanicKeys.Normalize(value));

    [Theory]
    [InlineData(" f8 ", "F8")]
    [InlineData(" escape ", "Escape")]
    [InlineData(" a ", "A")]
    public void ValidNamesAreTrimmedAndCanonical(string value, string expected) =>
        Assert.Equal(expected, PanicKeys.Normalize(value));

    [Fact]
    public void KeyCaptureRejectsModifiersAndSyntheticInput()
    {
        Assert.False(PanicKeys.IsBindable(Key.RightAlt));
        Assert.False(PanicKeys.IsBindable(Key.DeadCharProcessed));
        Assert.False(PanicKeys.IsBindable((Key)int.MaxValue));
        Assert.True(PanicKeys.IsBindable(Key.F8));
    }

    [Theory]
    [InlineData(" f8 ", Key.F8, PhysicalKey.F8, "F8")]
    [InlineData("Banana", Key.Escape, PhysicalKey.Escape, "Escape")]
    public Task PersistedBindingStillStopsThroughTheWindow(string stored, Key key,
        PhysicalKey physical, string canonical) => UiDispatcher.Run(() =>
    {
        var window = new MainWindow(new ClassicSettings { PanicKey = stored }, windows: true);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(canonical, window.Settings.PanicKey);
            window.Session.Start();
            window.OpenSheet("Settings");
            Dispatcher.UIThread.RunJobs();
            var child = window.SettingsWindow;
            Assert.NotNull(child);
            Assert.Same(window, child.Owner);
            var close = child.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "SheetClose");
            Assert.True(close.Focus());
            child.KeyPress(key, RawInputModifiers.None, physical, "");
            Assert.False(window.Session.IsRunning);
            Assert.Null(window.SettingsWindow);
            window.KeyRelease(key, RawInputModifiers.None, physical, "");
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
