using MoergoLayerViz.Core.Keymap;
using Xunit;

namespace MoergoLayerViz.Tests;

public class ZmkKeycodeLabelTests
{
    [Theory]
    [InlineData("FSLH", "/")]
    [InlineData("BSLH", "\\")]
    [InlineData("SEMI", ";")]
    [InlineData("SQT", "'")]
    [InlineData("COMMA", ",")]
    [InlineData("DOT", ".")]
    [InlineData("SPACE", "␣")]
    [InlineData("UP", "↑")]
    // Long-form aliases emitted by the Moergo editor.
    [InlineData("SINGLE_QUOTE", "'")]
    [InlineData("APOSTROPHE", "'")]
    [InlineData("DOUBLE_QUOTES", "\"")]
    [InlineData("LEFT_BRACKET", "[")]
    [InlineData("RIGHT_PARENTHESIS", ")")]
    [InlineData("SEMICOLON", ";")]
    [InlineData("BACKSLASH", "\\")]
    [InlineData("RETURN", "⏎")]
    // Modifier keys as bare keycodes — rendered as icons.
    [InlineData("LCTRL", "⌃")]
    [InlineData("RCTRL", "⌃")]
    [InlineData("LSHFT", "⇪")]
    [InlineData("LEFT_SHIFT", "⇪")]
    [InlineData("RGUI", "⌘")]
    [InlineData("LALT", "⌥")]
    // Numpad: KP_ prefix strips and routes through the main map.
    [InlineData("KP_N1", "1")]
    [InlineData("KP_N9", "9")]
    [InlineData("KP_NUMBER_3", "3")]
    [InlineData("KP_PLUS", "+")]
    [InlineData("KP_MINUS", "-")]
    [InlineData("KP_DIVIDE", "/")]
    [InlineData("KP_MULTIPLY", "*")]
    [InlineData("KP_SUBTRACT", "-")]
    [InlineData("KP_SLASH", "/")]
    [InlineData("KP_ASTERISK", "*")]
    [InlineData("KP_DOT", ".")]
    [InlineData("KP_COMMA", ",")]
    [InlineData("KP_ENTER", "⏎")]
    [InlineData("KP_EQUAL", "=")]
    [InlineData("KP_NUMLOCK", "Num")]
    [InlineData("NUM_LOCK", "Num")]
    public void Display_MapsPunctuationAndGlyphs(string input, string expected)
    {
        Assert.Equal(expected, ZmkKeycodeLabel.Display(input));
    }

    [Theory]
    [InlineData("N0", "0")]
    [InlineData("N7", "7")]
    [InlineData("NUMBER_3", "3")]
    [InlineData("NUMBER_9", "9")]
    public void Display_NumberAliasesMapToDigit(string input, string expected)
    {
        Assert.Equal(expected, ZmkKeycodeLabel.Display(input));
    }

    [Theory]
    [InlineData("ESC")]
    [InlineData("F14")]
    [InlineData("A")]
    [InlineData("UNKNOWN_KC")]
    public void Display_UnknownKeycodesFallThrough(string input)
    {
        Assert.Equal(input, ZmkKeycodeLabel.Display(input));
    }

    [Fact]
    public void Display_TabRendersAsGlyph()
    {
        Assert.Equal("⇥", ZmkKeycodeLabel.Display("TAB"));
    }

    [Theory]
    [InlineData("LSHFT", "⇪")]
    [InlineData("RSHFT", "⇪")]
    [InlineData("LCTRL", "⌃")]
    [InlineData("RCTRL", "⌃")]
    [InlineData("LALT", "⌥")]
    [InlineData("RALT", "⌥")]
    [InlineData("LGUI", "⌘")]
    [InlineData("RGUI", "⌘")]
    public void ModifierSubscript_MapsModifierKeycodes(string input, string expected)
    {
        Assert.Equal(expected, ZmkKeycodeLabel.ModifierSubscript(input));
    }

    [Theory]
    [InlineData("TAB")]
    [InlineData("A")]
    [InlineData("SHIFT")]
    [InlineData("")]
    public void ModifierSubscript_NonModifierReturnsNull(string input)
    {
        Assert.Null(ZmkKeycodeLabel.ModifierSubscript(input));
    }

    [Theory]
    [InlineData("LBKT", "{")]
    [InlineData("RBKT", "}")]
    [InlineData("BSLH", "|")]
    [InlineData("SEMI", ":")]
    [InlineData("SQT", "\"")]
    [InlineData("COMMA", "<")]
    [InlineData("DOT", ">")]
    [InlineData("FSLH", "?")]
    [InlineData("GRAVE", "~")]
    [InlineData("N1", "!")]
    [InlineData("N0", ")")]
    [InlineData("NUMBER_7", "&")]
    [InlineData("MINUS", "_")]
    [InlineData("EQUAL", "+")]
    public void Shifted_MapsShiftableKeycodes(string input, string expected)
    {
        Assert.Equal(expected, ZmkKeycodeLabel.Shifted(input));
    }

    [Theory]
    [InlineData("A")]
    [InlineData("TAB")]
    [InlineData("F1")]
    public void Shifted_UnshiftableReturnsNull(string input)
    {
        Assert.Null(ZmkKeycodeLabel.Shifted(input));
    }

    [Fact]
    public void FormatKpParams_PlainKeycode()
    {
        var (label, sub) = ZmkKeycodeLabel.FormatKpParams(new[] { "FSLH" });
        Assert.Equal("/", label);
        Assert.Equal("", sub);
    }

    [Fact]
    public void FormatKpParams_ShiftedSymbolCollapses()
    {
        var (label, sub) = ZmkKeycodeLabel.FormatKpParams(new[] { "LS", "LBKT" });
        Assert.Equal("{", label);
        Assert.Equal("", sub);
    }

    [Fact]
    public void FormatKpParams_RightShiftedSymbolCollapses()
    {
        var (label, sub) = ZmkKeycodeLabel.FormatKpParams(new[] { "RS", "N1" });
        Assert.Equal("!", label);
        Assert.Equal("", sub);
    }

    [Fact]
    public void FormatKpParams_CtrlCombo_RendersAsSubscript()
    {
        var (label, sub) = ZmkKeycodeLabel.FormatKpParams(new[] { "LC", "V" });
        Assert.Equal("V", label);
        Assert.Equal("⌃", sub);
    }

    [Fact]
    public void FormatKpParams_ChainedModifiers_RenderAllInSubscript()
    {
        var (label, sub) = ZmkKeycodeLabel.FormatKpParams(new[] { "LS", "LC", "V" });
        Assert.Equal("V", label);
        Assert.Equal("⇪⌃", sub);
    }

    [Fact]
    public void FormatKpParams_ShiftOnNonShiftable_FallsBackToSubscript()
    {
        // LS(A) — "A" has no shifted glyph, so render as A with ⇧ subscript.
        var (label, sub) = ZmkKeycodeLabel.FormatKpParams(new[] { "LS", "A" });
        Assert.Equal("A", label);
        Assert.Equal("⇪", sub);
    }

    /// <summary>
    /// CurrentModifierStyle is static — tests that mutate it must restore it
    /// or sibling tests asserting against the Mac default will fail under
    /// unpredictable runner order.
    /// </summary>
    private sealed class ScopedModifierStyle : IDisposable
    {
        private readonly ModifierStyle _previous;
        public ScopedModifierStyle(ModifierStyle s)
        {
            _previous = ZmkKeycodeLabel.CurrentModifierStyle;
            ZmkKeycodeLabel.CurrentModifierStyle = s;
        }
        public void Dispose() => ZmkKeycodeLabel.CurrentModifierStyle = _previous;
    }

    [Theory]
    [InlineData("LSHFT", "⇧")]
    [InlineData("RSHFT", "⇧")]
    [InlineData("LCTRL", "Ctrl")]
    [InlineData("RCTRL", "Ctrl")]
    [InlineData("LALT", "Alt")]
    [InlineData("RALT", "Alt")]
    [InlineData("LGUI", "⊞")]
    [InlineData("RGUI", "⊞")]
    [InlineData("LEFT_COMMAND", "⊞")]
    [InlineData("LWIN", "⊞")]
    public void Display_WindowsStyle_UsesWindowsGlyphs(string input, string expected)
    {
        using var _ = new ScopedModifierStyle(ModifierStyle.Windows);
        Assert.Equal(expected, ZmkKeycodeLabel.Display(input));
    }

    [Theory]
    [InlineData("LSHFT", "⇧")]
    [InlineData("LCTRL", "Ctrl")]
    [InlineData("LALT", "Alt")]
    [InlineData("LGUI", "⊞")]
    public void ModifierSubscript_WindowsStyle_UsesWindowsGlyphs(string input, string expected)
    {
        using var _ = new ScopedModifierStyle(ModifierStyle.Windows);
        Assert.Equal(expected, ZmkKeycodeLabel.ModifierSubscript(input));
    }

    [Theory]
    [InlineData("LS", "⇧")]
    [InlineData("RS", "⇧")]
    [InlineData("LC", "Ctrl")]
    [InlineData("LA", "Alt")]
    [InlineData("LG", "⊞")]
    [InlineData("RG", "⊞")]
    public void ShortModifierGlyph_WindowsStyle(string input, string expected)
    {
        using var _ = new ScopedModifierStyle(ModifierStyle.Windows);
        Assert.Equal(expected, ZmkKeycodeLabel.ShortModifierGlyph(input));
    }

    [Theory]
    [InlineData("LS", "⇪")]
    [InlineData("LC", "⌃")]
    [InlineData("LA", "⌥")]
    [InlineData("LG", "⌘")]
    public void ShortModifierGlyph_MacStyle(string input, string expected)
    {
        using var _ = new ScopedModifierStyle(ModifierStyle.Mac);
        Assert.Equal(expected, ZmkKeycodeLabel.ShortModifierGlyph(input));
    }

    [Fact]
    public void FormatKpParams_WindowsStyle_CtrlComboRendersAsCtrlSubscript()
    {
        using var _ = new ScopedModifierStyle(ModifierStyle.Windows);
        var (label, sub) = ZmkKeycodeLabel.FormatKpParams(new[] { "LC", "V" });
        Assert.Equal("V", label);
        Assert.Equal("Ctrl", sub);
    }

    [Fact]
    public void FormatKpParams_WindowsStyle_GuiComboRendersAsWinSubscript()
    {
        using var _ = new ScopedModifierStyle(ModifierStyle.Windows);
        var (label, sub) = ZmkKeycodeLabel.FormatKpParams(new[] { "LG", "L" });
        Assert.Equal("L", label);
        Assert.Equal("⊞", sub);
    }

    [Fact]
    public void FormatKpParams_WindowsStyle_ShiftedSymbolStillCollapses()
    {
        // The "single shift over a shiftable key" branch is style-independent —
        // LS(LBKT) renders the literal '{' on both Mac and Windows.
        using var _ = new ScopedModifierStyle(ModifierStyle.Windows);
        var (label, sub) = ZmkKeycodeLabel.FormatKpParams(new[] { "LS", "LBKT" });
        Assert.Equal("{", label);
        Assert.Equal("", sub);
    }

    [Fact]
    public void FormatKpParams_WindowsStyle_ChainedModifiers()
    {
        using var _ = new ScopedModifierStyle(ModifierStyle.Windows);
        var (label, sub) = ZmkKeycodeLabel.FormatKpParams(new[] { "LC", "LS", "T" });
        Assert.Equal("T", label);
        Assert.Equal("Ctrl⇧", sub);
    }
}
