using CCP.Classic.App.Input;
using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class KeywordInputTests
{
    [Fact]
    public void DisabledInputNeverTranslatesCharactersAndHeldKeysDoNotRepeat()
    {
        var output = new List<string>();
        var translated = 0;
        var input = new KeywordInput(output.Add);
        string Translate() { translated++; return "f"; }
        input.Key(70, true, false, Translate);
        input.Key(70, true, true, Translate);
        Assert.Equal(0, translated);
        Assert.Equal([KeywordInput.Clear], output);
        input.Key(70, false, true, Translate);
        input.Key(70, true, true, Translate); input.Key(70, true, true, Translate);
        Assert.Equal(1, translated);
        Assert.Equal(new[] { KeywordInput.Clear, "f" }, output);
    }

    [Fact]
    public void DisableAndReenableInvalidatesQueuedTextAndWaitsForHeldRelease()
    {
        var output = new List<string>();
        var input = new KeywordInput(output.Add);
        input.Key(65, true, true, () => "a");
        var oldGeneration = input.Generation;
        input.SetEnabled(false);
        input.Key(65, true, false, () => throw new InvalidOperationException());
        input.SetEnabled(true);
        Assert.NotEqual(oldGeneration, input.Generation);
        input.Key(65, true, true, () => "a");
        Assert.Equal(1, output.Count(value => value == "a"));
        input.Key(65, false, true, () => null); input.Key(65, true, true, () => "a");
        Assert.Equal(2, output.Count(value => value == "a"));
    }

    [Fact]
    public void ShortcutAndFocusChangesDiscardAPartialKeyword()
    {
        var settings = new ClassicSettings { KeywordTriggersEnabled = true };
        var timeline = new CompanionTimeline(settings); timeline.Start();
        var input = new KeywordInput(timeline.FeedText);
        var code = 1;
        void Type(string value)
        {
            foreach (var character in value)
            {
                input.Key(code, true, true, () => character.ToString());
                input.Key(code, false, true, () => null); code++;
            }
        }
        input.SetContext(100);
        Type("fo"); input.Key(17, true, true, () => throw new InvalidOperationException(), shortcut: true);
        input.Key(17, false, true, () => null);
        Type("cus "); Assert.Empty(timeline.Step(0).Triggers);
        Type("fo"); input.SetContext(200); Type("cus "); Assert.Empty(timeline.Step(0).Triggers);
        Type("FoCuS ");
        Assert.Equal(CompanionEffect.Whisper, Assert.Single(timeline.Step(0).Triggers).Effect);
    }

    [Fact]
    public void X11PollingSeedsHeldKeysAndEmitsOnlyAFreshUnambiguousPress()
    {
        var output = new List<string>();
        var input = new KeywordInput(output.Add);
        var map = new byte[32];
        void Down(int code) => map[code >> 3] |= (byte)(1 << (code & 7));
        void Poll(bool enabled = true) => input.ReadMap(map, enabled, _ => "a", shortcut: false);
        Down(38); Poll(); Poll();
        Assert.DoesNotContain("a", output);
        Array.Clear(map); Poll(); Down(38); Poll(); Poll();
        Assert.Equal(1, output.Count(value => value == "a"));
        Poll(false); Poll(true); Poll();
        Assert.Equal(1, output.Count(value => value == "a"));
        Array.Clear(map); Poll(); Down(38); Down(39); Poll();
        Assert.Equal(1, output.Count(value => value == "a"));
        Assert.Equal(KeywordInput.Clear, output[^1]);
    }

    [Theory]
    [InlineData("f", "f")]
    [InlineData("F", "F")]
    [InlineData("é", "é")]
    [InlineData(" ", " ")]
    [InlineData("\t", "\t")]
    [InlineData("\r", "\r")]
    [InlineData("\b", "\u001b")]
    [InlineData("\0", "\u001b")]
    [InlineData("", "\u001b")]
    [InlineData(null, "\u001b")]
    public void OnlyRealTextOrWordBoundariesReachTheMatcher(string? text, string expected)
        => Assert.Equal(expected, KeywordInput.Filter(text));

    [Fact]
    public void ShortcutModifiersNeverBecomeCharactersButShiftMayChangeCase()
    {
        foreach (var modifier in new[] { 0x11, 0x12, 0xa2, 0xa3, 0xa4, 0xa5, 0x5b, 0x5c })
        {
            Assert.True(KeywordInput.WindowsShortcut(70, code => code == modifier));
            Assert.True(KeywordInput.WindowsShortcut(modifier, _ => false));
        }
        Assert.False(KeywordInput.WindowsShortcut(70, code => code == 0x10));
        Assert.Equal(KeywordInput.Clear, KeywordInput.Filter("f", shortcut: true));
    }

    [Theory]
    [InlineData(0x66u, "f")]
    [InlineData(0x46u, "F")]
    [InlineData(0xff0du, "\n")]
    [InlineData(0xffb7u, "7")]
    [InlineData(0x010003bbu, "λ")]
    [InlineData(0xffe3u, null)]
    [InlineData(0xff08u, null)]
    public void X11KeysymsTranslatePrintedCharactersAndBoundaries(uint symbol, string? expected)
        => Assert.Equal(expected, KeywordInput.X11Text(symbol));

    [Fact]
    public void ContextChangesInvalidateOldDeliveryWithoutRetainingText()
    {
        var output = new List<string>();
        var input = new KeywordInput(output.Add);
        input.SetEnabled(true); input.SetContext(1);
        var previous = input.Generation;
        input.SetContext(1); Assert.Equal(previous, input.Generation);
        input.SetContext(2); Assert.NotEqual(previous, input.Generation);
        Assert.All(output, item => Assert.Equal(KeywordInput.Clear, item));
        Assert.DoesNotContain(typeof(KeywordInput).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
            field => field.FieldType == typeof(string) || field.FieldType == typeof(char[]));
    }
}
