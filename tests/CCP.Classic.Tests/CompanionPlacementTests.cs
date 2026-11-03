using System.Reflection;
using Avalonia.Controls;
using CCP.Classic.App.Companion;
using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class CompanionPlacementTests
{
    [Fact]
    public void PersistedPlacementUsesTheOriginalScaleBoundsAndRejectsInvalidCoordinates()
    {
        var options = new CompanionPlacementOptions { Scale = double.NaN, Left = int.MaxValue, Top = 10 };
        options.Normalize(); Assert.Equal(1, options.Scale); Assert.Null(options.Left); Assert.Null(options.Top);
        options.Scale = 2; options.Normalize(); Assert.Equal(1.5, options.Scale);
        options.Scale = .1; options.Normalize(); Assert.Equal(.5, options.Scale);
    }

    [Fact]
    public Task DetachedScaleAndTopmostApplyWithoutAPlaybackOrDesktopChange() => UiDispatcher.Run(() =>
    {
        var owner = new Window(); var settings = new ClassicSettings();
        var companion = new CompanionWindow(owner, settings, _ => { }, requireTransparency: false);
        try
        {
            var apply = typeof(CompanionWindow).GetMethod("ApplyPlacement", BindingFlags.Instance | BindingFlags.NonPublic)!;
            settings.CompanionPlacement.Detached = true; settings.CompanionPlacement.Scale = 1.5;
            settings.CompanionPlacement.AlwaysOnTop = false; apply.Invoke(companion, null);
            Assert.Equal(360, companion.Width); Assert.Equal(585, companion.Height); Assert.False(companion.Topmost);
            settings.CompanionPlacement.Detached = false; apply.Invoke(companion, null);
            Assert.Equal(240, companion.Width); Assert.Equal(390, companion.Height); Assert.False(companion.IsVisible);
        }
        finally { companion.FinishImmediately(); owner.Close(); }
    });
}
