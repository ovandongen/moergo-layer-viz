using MoergoLayerViz.App.Services.Hotkeys;
using Xunit;

namespace MoergoLayerViz.Tests;

public class HotkeyNameMapTests
{
    [Theory]
    [InlineData("F1", 1)]
    [InlineData("F9", 9)]
    [InlineData("F12", 12)]
    [InlineData("F24", 24)]
    [InlineData("f12", 12)]  // case-insensitive prefix
    public void TryParseFKeyIndex_AcceptsF1ThroughF24(string input, int expected)
    {
        Assert.Equal(expected, HotkeyNameMap.TryParseFKeyIndex(input));
    }

    [Theory]
    [InlineData("F0")]      // below range
    [InlineData("F25")]     // above range
    [InlineData("F100")]    // way above
    [InlineData("")]        // empty
    [InlineData("F")]       // missing digits
    [InlineData("X12")]     // wrong prefix
    [InlineData("12")]      // no prefix
    [InlineData("F-1")]     // negative parsed but out-of-range
    [InlineData("FA")]      // non-numeric tail
    public void TryParseFKeyIndex_RejectsOutOfRangeOrMalformed(string input)
    {
        Assert.Null(HotkeyNameMap.TryParseFKeyIndex(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("None")]
    [InlineData("none")]
    [InlineData("NONE")]
    public void TryParseModifiers_EmptyOrNoneFiresNothing(string? input)
    {
        var captured = new List<string>();
        var ok = HotkeyNameMap.TryParseModifiers(input, captured.Add);
        Assert.True(ok);
        Assert.Empty(captured);
    }

    [Theory]
    [InlineData("Ctrl", "Ctrl")]
    [InlineData("Control", "Ctrl")]
    [InlineData("ctrl", "Ctrl")]
    [InlineData("Alt", "Alt")]
    [InlineData("Option", "Alt")]
    [InlineData("OPTION", "Alt")]
    [InlineData("Shift", "Shift")]
    [InlineData("shift", "Shift")]
    [InlineData("Meta", "Meta")]
    [InlineData("Cmd", "Meta")]
    [InlineData("Command", "Meta")]
    [InlineData("Win", "Meta")]
    public void TryParseModifiers_SingleModifierCanonicalises(string input, string expected)
    {
        var captured = new List<string>();
        var ok = HotkeyNameMap.TryParseModifiers(input, captured.Add);
        Assert.True(ok);
        Assert.Equal(new[] { expected }, captured);
    }

    [Fact]
    public void TryParseModifiers_MultipleModifiersAllReported()
    {
        var captured = new List<string>();
        var ok = HotkeyNameMap.TryParseModifiers("Ctrl+Alt+Shift", captured.Add);
        Assert.True(ok);
        Assert.Equal(new[] { "Ctrl", "Alt", "Shift" }, captured);
    }

    [Fact]
    public void TryParseModifiers_TrimsWhitespaceAroundTokens()
    {
        var captured = new List<string>();
        var ok = HotkeyNameMap.TryParseModifiers("Ctrl + Shift +  Alt", captured.Add);
        Assert.True(ok);
        Assert.Equal(new[] { "Ctrl", "Shift", "Alt" }, captured);
    }

    [Fact]
    public void TryParseModifiers_PreservesCallerOrder()
    {
        var captured = new List<string>();
        HotkeyNameMap.TryParseModifiers("Meta+Ctrl", captured.Add);
        Assert.Equal(new[] { "Meta", "Ctrl" }, captured);
    }

    [Theory]
    [InlineData("Hyper")]            // not a recognised modifier
    [InlineData("Ctr")]              // typo
    [InlineData("Ctrl+Hyper")]       // first ok, second unknown → whole call fails
    [InlineData("Foo+Bar")]
    public void TryParseModifiers_UnknownTokenReturnsFalse(string input)
    {
        var captured = new List<string>();
        var ok = HotkeyNameMap.TryParseModifiers(input, captured.Add);
        Assert.False(ok);
    }

    [Fact]
    public void TryParseModifiers_UnknownTokenStillEmitsPriorKnownTokens()
    {
        // Documents observed behaviour: the parser short-circuits on unknown,
        // but tokens parsed before the failure are already pushed to the callback.
        // Callers should treat the whole result as failed (and discard) when
        // the return value is false.
        var captured = new List<string>();
        HotkeyNameMap.TryParseModifiers("Ctrl+Hyper", captured.Add);
        Assert.Equal(new[] { "Ctrl" }, captured);
    }

    [Fact]
    public void TryParseModifiers_EmptySegmentsBetweenPlusesIgnored()
    {
        // Split(..., RemoveEmptyEntries) drops "Ctrl++Alt" empty middle.
        var captured = new List<string>();
        var ok = HotkeyNameMap.TryParseModifiers("Ctrl++Alt", captured.Add);
        Assert.True(ok);
        Assert.Equal(new[] { "Ctrl", "Alt" }, captured);
    }
}
