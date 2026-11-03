using CCP.Classic.App.Input;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class GlobalPanicTests
{
    [Theory]
    [InlineData("Escape", 0x1B, "Escape")]
    [InlineData("F1", 0x70, "F1")]
    [InlineData("F24", 0x87, "F24")]
    [InlineData("A", 0x41, "a")]
    [InlineData("D9", 0x39, "9")]
    [InlineData("NumPad7", 0x67, "KP_7")]
    [InlineData("Enter", 0x0D, "Return")]
    [InlineData("Space", 0x20, "space")]
    [InlineData("PageDown", 0x22, "Next")]
    public void CommonBindingsMapToNativeCodes(string name, int windows, string x11)
    {
        Assert.True(NativePanicKeys.TryMap(name, out var actual, out var symbol));
        Assert.Equal(windows, actual); Assert.Equal(x11, symbol);
    }

    [Theory]
    [InlineData("None")]
    [InlineData("LeftCtrl")]
    [InlineData("42")]
    [InlineData("F8, Escape")]
    [InlineData("MediaTvGuide")]
    public void UnsupportedBindingsNeverClaimGlobalCoverage(string name) =>
        Assert.False(NativePanicKeys.TryMap(name, out _, out _));

    [Theory]
    [InlineData(":0", true)]
    [InlineData(":1.0", true)]
    [InlineData("unix:0", true)]
    [InlineData("unix/:0", true)]
    [InlineData("localhost:10.0", false)]
    [InlineData("remote:0", false)]
    [InlineData("tcp/remote:0", false)]
    [InlineData(":0;remote", false)]
    [InlineData(":", false)]
    [InlineData(null, false)]
    public void OnlyLocalUnixDisplaysAreAllowed(string? display, bool allowed) =>
        Assert.Equal(allowed, NativePanicKeys.IsLocalDisplay(display));

    [Fact]
    public void NativeRepeatsAreOnePressAndReleaseKeepsTheOriginalBinding()
    {
        var events = new List<(string, bool)>();
        var state = new PanicKeyTransitions((name, down) => events.Add((name, down)));
        state.Down(27, "Escape"); state.Down(27, "Escape");
        state.Down(119, "F8"); state.Up(27); state.Up(119); state.Up(119);
        Assert.Equal(new[] { ("Escape", true), ("F8", true), ("Escape", false), ("F8", false) }, events);
    }

    [Fact]
    public void BindingAnAlreadyHeldKeyWaitsForReleaseAndRearmsTheNextPress()
    {
        var events = new List<(string, bool)>();
        var state = new PanicKeyTransitions((name, down) => events.Add((name, down)));
        state.Seed(119, "F8"); state.Down(119, "F8"); state.Down(119, "F8");
        Assert.Empty(events);
        state.Up(119); state.Down(119, "F8"); state.Up(119);
        Assert.Equal(new[] { ("F8", false), ("F8", true), ("F8", false) }, events);
    }

    [Fact]
    public void AnIgnoredNativeDownStillReleasesTheUiCapturedBinding()
    {
        var events = new List<(string, bool)>();
        var state = new PanicKeyTransitions((name, down) => events.Add((name, down)));
        state.Up(119, "F8");
        Assert.Equal(new[] { ("F8", false) }, events);
    }

    [Fact]
    public void X11RebindingAfterAQuickTapReleasesTheUiCapture()
    {
        var events = new List<(string, bool)>();
        var state = new PanicKeyTransitions((name, down) => events.Add((name, down)));
        var map = new byte[32];
        state.Bind(map, 74, "F8");
        Assert.Equal(new[] { ("F8", false) }, events);
        map[74 >> 3] |= 1 << (74 & 7);
        state.ReadMap(map, 74, "F8");
        Assert.Equal(new[] { ("F8", false), ("F8", true) }, events);
    }

    [Fact]
    public void X11BitmapTracksBothEndsAcrossRebindingWithoutRepeats()
    {
        var events = new List<(string, bool)>();
        var state = new PanicKeyTransitions((name, down) => events.Add((name, down)));
        var map = new byte[32];
        map[9 >> 3] |= 1 << (9 & 7);
        state.ReadMap(map, 9, "Escape"); state.ReadMap(map, 9, "Escape");
        state.ReadMap(map, 74, "F8");
        Array.Clear(map); state.ReadMap(map, 74, "F8");
        map[74 >> 3] |= 1 << (74 & 7); state.ReadMap(map, 74, "F8");
        Array.Clear(map); state.ReadMap(map, 74, "F8");
        Assert.Equal(new[] { ("Escape", true), ("Escape", false), ("F8", true), ("F8", false) }, events);
        Assert.False(PanicKeyTransitions.IsDown(map, 0));
        Assert.False(PanicKeyTransitions.IsDown(map, 256));
        Assert.False(PanicKeyTransitions.IsDown([], 9));
    }

    // Keyword triggers are gone: the listener reads only the panic binding and has no text path left.
    [Fact]
    public void TheListenerOnlyWatchesThePanicKeyAndKeepsNoTypedText()
    {
        var type = typeof(GlobalPanicMonitor);
        Assert.Empty(type.GetEvents());
        Assert.DoesNotContain(type.GetProperties(), property => property.Name.Contains("Text", StringComparison.Ordinal));
        var constructor = Assert.Single(type.GetConstructors());
        Assert.Equal(new[] { typeof(Func<string>), typeof(Action<string, bool>) },
            constructor.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.DoesNotContain(type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
            field => field.FieldType == typeof(string[]) || field.FieldType == typeof(char[]) || field.FieldType == typeof(System.Text.StringBuilder));
    }

    // The hook passes every key through the transitions; ordinary typing around the panic key must never
    // swallow, delay or repeat its press.
    [Fact]
    public void TypingAroundThePanicKeyStillFiresItOncePerPress()
    {
        var events = new List<(string, bool)>();
        var state = new PanicKeyTransitions((name, down) => events.Add((name, down)));
        foreach (var letter in "focus relax") state.Up(char.ToUpperInvariant(letter), null);
        state.Down(27, "Escape"); state.Down(27, "Escape");
        foreach (var letter in "deeper") state.Up(char.ToUpperInvariant(letter), null);
        state.Up(27, "Escape"); state.Down(27, "Escape"); state.Up(27, "Escape");
        Assert.Equal(new[] { ("Escape", true), ("Escape", false), ("Escape", true), ("Escape", false) }, events);
        var map = new byte[32];
        void Press(int code) => map[code >> 3] |= (byte)(1 << (code & 7));
        var x11 = new List<(string, bool)>();
        var poll = new PanicKeyTransitions((name, down) => x11.Add((name, down)));
        Press(41); Press(32); poll.ReadMap(map, 9, "Escape");
        Press(9); poll.ReadMap(map, 9, "Escape"); poll.ReadMap(map, 9, "Escape");
        Array.Clear(map); Press(41); poll.ReadMap(map, 9, "Escape");
        Assert.Equal(new[] { ("Escape", true), ("Escape", false) }, x11);
    }
}
