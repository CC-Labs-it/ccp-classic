using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Classic.App;
using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class GlobalPanicRoutingTests
{
    [Theory]
    [InlineData("Space", Key.Space, PhysicalKey.Space, false)]
    [InlineData("Space", Key.Space, PhysicalKey.Space, true)]
    [InlineData("Return", Key.Return, PhysicalKey.Enter, false)]
    [InlineData("Return", Key.Return, PhysicalKey.Enter, true)]
    public Task PanicCannotReactivateTheFocusedStartButton(string name, Key key, PhysicalKey physical, bool nativeFirst)
        => UiDispatcher.Run(() =>
        {
            var window = new MainWindow(new ClassicSettings { PanicKey = name }, globalPanicAvailable: () => true);
            try
            {
                window.Show(); window.Activate(); Dispatcher.UIThread.RunJobs();
                var start = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "StartStop");
                Assert.True(start.Focus());
                window.Session.Start();
                if (nativeFirst) Assert.True(window.Session.KeyDown(name));
                window.KeyPress(key, RawInputModifiers.None, physical, "");
                if (!nativeFirst) Assert.True(window.Session.KeyDown(name));
                window.KeyRelease(key, RawInputModifiers.None, physical, "");
                window.Session.KeyUp(name);
                Dispatcher.UIThread.RunJobs();
                Assert.False(window.Session.IsRunning);
            }
            finally { window.Close(); }
        });
}
