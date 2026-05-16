namespace MoergoLayerViz.Core.Keymap;

/// <summary>
/// Which set of modifier glyphs <see cref="ZmkKeycodeLabel"/> renders for
/// Shift / Ctrl / Alt / Gui. User-controlled, independent of the host OS —
/// the toolbar toggle in the app writes <see cref="ZmkKeycodeLabel.CurrentModifierStyle"/>
/// and then refreshes every key label.
/// </summary>
public enum ModifierStyle
{
    /// <summary>⇪ ⌃ ⌥ ⌘ — the original styling.</summary>
    Mac,
    /// <summary>⇧ Ctrl Alt ⊞ — hybrid: universal Shift arrow and the Microsoft monochrome Windows-key glyph; text for Ctrl/Alt.</summary>
    Windows,
}

/// <summary>
/// Translates ZMK keycode identifiers (as they appear in Moergo JSON exports)
/// into short display glyphs suitable for rendering on a 40–80px-wide key cap.
///
/// Covers the common punctuation abbreviations (FSLH, BSLH, SEMI, …), the
/// number-row aliases (N0..N9, NUMBER_0..NUMBER_9) and a handful of arrow /
/// whitespace glyphs. Unknown keycodes are returned unchanged so callers can
/// still show *something* rather than swallow a keycode we haven't mapped.
/// </summary>
public static class ZmkKeycodeLabel
{
    /// <summary>
    /// Active modifier glyph set. Mutated by the UI when the user toggles the
    /// Mac/Windows toolbar button; reads are unsynchronised because writes
    /// happen on the UI thread and label rebuilds are also UI-dispatched.
    /// </summary>
    public static ModifierStyle CurrentModifierStyle { get; set; } = ModifierStyle.Mac;
    private static readonly Dictionary<string, string> DisplayMap = new(StringComparer.Ordinal)
    {
        // Punctuation — ZMK short abbreviations + long-form aliases the Moergo
        // editor emits (e.g. SINGLE_QUOTE alongside SQT). All aliases of the
        // same keycode map to the same glyph.
        ["FSLH"] = "/",      ["SLASH"] = "/",              ["FORWARD_SLASH"] = "/",
        ["BSLH"] = "\\",     ["BACKSLASH"] = "\\",
        ["SEMI"] = ";",      ["SEMICOLON"] = ";",
        ["SQT"] = "'",       ["SINGLE_QUOTE"] = "'",       ["APOS"] = "'",       ["APOSTROPHE"] = "'",
        ["DQT"] = "\"",      ["DOUBLE_QUOTES"] = "\"",
        ["GRAVE"] = "`",
        ["COMMA"] = ",",
        ["DOT"] = ".",       ["PERIOD"] = ".",
        ["MINUS"] = "-",
        ["EQUAL"] = "=",     ["EQUALS"] = "=",
        ["LBKT"] = "[",      ["LEFT_BRACKET"] = "[",
        ["RBKT"] = "]",      ["RIGHT_BRACKET"] = "]",
        ["LBRC"] = "{",      ["LEFT_BRACE"] = "{",
        ["RBRC"] = "}",      ["RIGHT_BRACE"] = "}",
        ["LPAR"] = "(",      ["LEFT_PARENTHESIS"] = "(",
        ["RPAR"] = ")",      ["RIGHT_PARENTHESIS"] = ")",
        ["PIPE"] = "|",
        ["TILDE"] = "~",
        ["COLON"] = ":",
        ["EXCL"] = "!",      ["EXCLAMATION"] = "!",
        ["AT"] = "@",        ["AT_SIGN"] = "@",
        ["HASH"] = "#",      ["POUND"] = "#",
        ["DLLR"] = "$",      ["DOLLAR"] = "$",
        ["PRCNT"] = "%",     ["PERCENT"] = "%",
        ["CARET"] = "^",
        ["AMPS"] = "&",      ["AMPERSAND"] = "&",
        ["STAR"] = "*",      ["ASTERISK"] = "*",
        ["UNDER"] = "_",     ["UNDERSCORE"] = "_",
        ["PLUS"] = "+",
        ["QMARK"] = "?",     ["QUESTION"] = "?",
        ["LT"] = "<",        ["LESS_THAN"] = "<",
        ["GT"] = ">",        ["GREATER_THAN"] = ">",

        // Whitespace / editing
        ["SPACE"] = "␣",
        ["BSPC"] = "⌫",      ["BACKSPACE"] = "⌫",
        ["DEL"] = "⌦",       ["DELETE"] = "⌦",
        ["RET"] = "⏎",       ["ENTER"] = "⏎",             ["RETURN"] = "⏎",
        ["TAB"] = "⇥",

        // Numpad-specific long forms (base names — a `KP_` prefix is stripped
        // in Display() before lookup, so `KP_DIVIDE` resolves to `/` via these).
        ["DIVIDE"] = "/",
        ["MULTIPLY"] = "*",
        ["SUBTRACT"] = "-",
        ["NUMLOCK"] = "Num", ["NUM_LOCK"] = "Num", ["NLCK"] = "Num",

        // Consumer / media keycodes (ZMK `C_*` usage codes, passed as params to &kp).
        ["C_BRI_UP"] = "☀+", ["C_BRIGHTNESS_INC"] = "☀+",
        ["C_BRI_DN"] = "☀−", ["C_BRIGHTNESS_DEC"] = "☀−",
        // Speaker glyphs (🔊 🔉 🔇) are in the emoji plane and render in color
        // in most fonts — use plain text to stay monochrome with the rest.
        ["C_VOL_UP"] = "Vol +", ["C_VOLUME_UP"] = "Vol +",
        ["C_VOL_DN"] = "Vol −", ["C_VOLUME_DOWN"] = "Vol −",
        ["C_MUTE"] = "Mute",
        ["C_PP"] = "⏯", ["C_PLAY_PAUSE"] = "⏯",
        ["C_PREV"] = "⏮", ["C_PREVIOUS"] = "⏮",
        ["C_NEXT"] = "⏭",
        ["C_STOP"] = "⏹",
        ["C_FF"] = "⏩", ["C_FAST_FORWARD"] = "⏩",
        ["C_REWIND"] = "⏪",
        ["C_EJECT"] = "⏏",
        ["PSCRN"] = "PrtSc", ["PRINTSCREEN"] = "PrtSc",
        ["SLCK"] = "ScrLk", ["SCROLLLOCK"] = "ScrLk",
        ["PAUSE_BREAK"] = "Pause", ["PAUSE"] = "Pause",

        // Arrows — thin monochrome variants (the heavy forms ⬆⬇⬅➡ render as
        // color emoji in many fonts). KeyViewModel.LabelFontSize gives these
        // a size bump so the slim strokes still read at a glance.
        ["UP"] = "↑",        ["UP_ARROW"] = "↑",
        ["DOWN"] = "↓",      ["DOWN_ARROW"] = "↓",
        ["LEFT"] = "←",      ["LEFT_ARROW"] = "←",
        ["RIGHT"] = "→",     ["RIGHT_ARROW"] = "→",

        // Modifier glyphs (LSHFT/LCTRL/LALT/LGUI etc.) are *not* in DisplayMap —
        // they're style-dependent and resolved through CurrentModifierStyle via
        // ModifierGlyphForDisplay/ModifierSubscript/ShortModifierGlyph.
    };

    /// <summary>
    /// Canonical modifier categories that participate in style switching.
    /// L/R hand distinction is dropped — the modifier *type* is what matters
    /// when scanning a keymap.
    /// </summary>
    private enum Mod { Shift, Ctrl, Alt, Gui }

    /// <summary>
    /// Maps every ZMK alias for a modifier keycode (short and long form, both
    /// hands, plus the Mac-specific LEFT_COMMAND / generic L/RWIN names) onto
    /// its <see cref="Mod"/> category. Membership here is the single source of
    /// truth for "is this a modifier keycode?".
    /// </summary>
    private static readonly Dictionary<string, Mod> ModifierKeycodes = new(StringComparer.Ordinal)
    {
        ["LSHFT"] = Mod.Shift, ["RSHFT"] = Mod.Shift,
        ["LEFT_SHIFT"] = Mod.Shift, ["RIGHT_SHIFT"] = Mod.Shift,
        ["LCTRL"] = Mod.Ctrl, ["RCTRL"] = Mod.Ctrl,
        ["LEFT_CONTROL"] = Mod.Ctrl, ["RIGHT_CONTROL"] = Mod.Ctrl,
        ["LALT"] = Mod.Alt, ["RALT"] = Mod.Alt,
        ["LEFT_ALT"] = Mod.Alt, ["RIGHT_ALT"] = Mod.Alt,
        ["LGUI"] = Mod.Gui, ["RGUI"] = Mod.Gui,
        ["LEFT_GUI"] = Mod.Gui, ["RIGHT_GUI"] = Mod.Gui,
        ["LEFT_COMMAND"] = Mod.Gui, ["RIGHT_COMMAND"] = Mod.Gui,
        ["LWIN"] = Mod.Gui, ["RWIN"] = Mod.Gui,
    };

    /// <summary>Two-character ZMK modifier-function shorthands (used inside <c>LS(...)</c> wrappers).</summary>
    private static readonly Dictionary<string, Mod> ShortModifierCategories = new(StringComparer.Ordinal)
    {
        ["LS"] = Mod.Shift, ["RS"] = Mod.Shift,
        ["LC"] = Mod.Ctrl,  ["RC"] = Mod.Ctrl,
        ["LA"] = Mod.Alt,   ["RA"] = Mod.Alt,
        ["LG"] = Mod.Gui,   ["RG"] = Mod.Gui,
    };

    private static readonly Dictionary<Mod, string> MacModifierGlyphs = new()
    {
        [Mod.Shift] = "⇪",
        [Mod.Ctrl]  = "⌃",
        [Mod.Alt]   = "⌥",
        [Mod.Gui]   = "⌘",
    };

    /// <summary>
    /// Hybrid Windows convention: ⇧ (universal Shift arrow) and ⊞ (Microsoft's
    /// monochrome Windows-key glyph, U+229E). Ctrl/Alt have no single-char
    /// Windows glyph in widespread use, so they render as plain text.
    /// </summary>
    private static readonly Dictionary<Mod, string> WindowsModifierGlyphs = new()
    {
        [Mod.Shift] = "⇧",
        [Mod.Ctrl]  = "Ctrl",
        [Mod.Alt]   = "Alt",
        [Mod.Gui]   = "⊞",
    };

    private static string GlyphFor(Mod m) =>
        CurrentModifierStyle == ModifierStyle.Windows
            ? WindowsModifierGlyphs[m]
            : MacModifierGlyphs[m];

    /// <summary>
    /// Returns a short display glyph for a ZMK keycode. Falls through to the
    /// raw input for anything not in the map (e.g. TAB, ESC, F1..F24, letters).
    /// </summary>
    public static string Display(string zmkKeycode)
    {
        if (string.IsNullOrEmpty(zmkKeycode)) return "";

        if (DisplayMap.TryGetValue(zmkKeycode, out var mapped)) return mapped;
        if (ModifierKeycodes.TryGetValue(zmkKeycode, out var mod)) return GlyphFor(mod);

        // Numpad keycodes share the same glyphs as their main-row counterparts.
        // Strip the `KP_` prefix and retry so `KP_N1`, `KP_PLUS`, `KP_DIVIDE`,
        // etc. all route through the existing logic.
        if (zmkKeycode.StartsWith("KP_", StringComparison.Ordinal))
            return Display(zmkKeycode.Substring(3));

        // N0..N9 and NUMBER_0..NUMBER_9 both map to the underlying digit.
        if (zmkKeycode.Length == 2 && zmkKeycode[0] == 'N' && zmkKeycode[1] >= '0' && zmkKeycode[1] <= '9')
            return zmkKeycode[1].ToString();

        if (zmkKeycode.StartsWith("NUMBER_", StringComparison.Ordinal) && zmkKeycode.Length == 8
            && zmkKeycode[7] >= '0' && zmkKeycode[7] <= '9')
            return zmkKeycode[7].ToString();

        return zmkKeycode;
    }

    /// <summary>
    /// Maps a modifier keycode (LSHFT/RSHFT/LCTRL/RCTRL/LALT/RALT/LGUI/RGUI)
    /// to its single-character subscript glyph. L/R hand distinction is dropped
    /// — labels read cleaner without it, and knowing *which* shift fires is
    /// rarely what the user is checking when glancing at a key. Returns null
    /// for non-modifier keycodes so callers can fall back to the raw param.
    /// </summary>
    public static string? ModifierSubscript(string zmkKeycode) =>
        ModifierKeycodes.TryGetValue(zmkKeycode, out var mod) ? GlyphFor(mod) : null;

    /// <summary>
    /// ZMK modifier-function shorthands used when wrapping another keycode:
    /// e.g. <c>&amp;kp LS(LBKT)</c> flattens to params ["LS", "LBKT"].
    /// </summary>
    public static bool IsShortModifier(string s) => ShortModifierCategories.ContainsKey(s);

    /// <summary>Short-modifier glyph (e.g. LS / RS → Shift, LC / RC → Ctrl), style-aware.</summary>
    public static string ShortModifierGlyph(string s) =>
        ShortModifierCategories.TryGetValue(s, out var mod) ? GlyphFor(mod) : s;

    /// <summary>
    /// US-QWERTY shifted symbol for a keycode — e.g. LBKT → "{", N1 → "!".
    /// Returns null if the keycode has no distinct shifted glyph on US layout
    /// (letters, function keys, navigation keys, etc).
    /// </summary>
    private static readonly Dictionary<string, string> ShiftedMap = new(StringComparer.Ordinal)
    {
        ["N1"] = "!", ["N2"] = "@", ["N3"] = "#", ["N4"] = "$", ["N5"] = "%",
        ["N6"] = "^", ["N7"] = "&", ["N8"] = "*", ["N9"] = "(", ["N0"] = ")",
        ["NUMBER_1"] = "!", ["NUMBER_2"] = "@", ["NUMBER_3"] = "#",
        ["NUMBER_4"] = "$", ["NUMBER_5"] = "%", ["NUMBER_6"] = "^",
        ["NUMBER_7"] = "&", ["NUMBER_8"] = "*", ["NUMBER_9"] = "(",
        ["NUMBER_0"] = ")",
        ["MINUS"] = "_",
        ["EQUAL"] = "+", ["EQUALS"] = "+",
        ["LBKT"] = "{", ["LEFT_BRACKET"] = "{",
        ["RBKT"] = "}", ["RIGHT_BRACKET"] = "}",
        ["BSLH"] = "|", ["BACKSLASH"] = "|",
        ["SEMI"] = ":", ["SEMICOLON"] = ":",
        ["SQT"] = "\"", ["SINGLE_QUOTE"] = "\"", ["APOS"] = "\"", ["APOSTROPHE"] = "\"",
        ["COMMA"] = "<",
        ["DOT"] = ">", ["PERIOD"] = ">",
        ["FSLH"] = "?", ["SLASH"] = "?", ["FORWARD_SLASH"] = "?",
        ["GRAVE"] = "~",
    };

    public static string? Shifted(string zmkKeycode) =>
        ShiftedMap.TryGetValue(zmkKeycode, out var s) ? s : null;

    /// <summary>
    /// Formats the flattened params of a <c>&amp;kp</c> binding into a display
    /// (Label, Subscript) pair. Handles modifier-function wrappers (LS, RS, LC,
    /// …): a single shift over a shiftable key collapses to the shifted glyph
    /// (e.g. LS + LBKT → "{"), other modifier chains render as base-key label
    /// with the modifier glyphs as subscript.
    /// </summary>
    public static (string Label, string Subscript) FormatKpParams(IReadOnlyList<string> paramList)
    {
        if (paramList.Count == 0) return ("", "");
        if (paramList.Count == 1) return (Display(paramList[0]), "");

        // Strip leading short-modifier wrappers.
        int i = 0;
        while (i < paramList.Count - 1 && IsShortModifier(paramList[i])) i++;
        var baseKc = paramList[i];
        var modCount = i;

        if (modCount == 0)
            return (Display(baseKc), "");

        // Single shift over a shiftable key → use the shifted glyph.
        if (modCount == 1 && (paramList[0] == "LS" || paramList[0] == "RS"))
        {
            var shifted = Shifted(baseKc);
            if (shifted is not null) return (shifted, "");
        }

        // Otherwise: base keycode as main, modifier glyphs concatenated as subscript.
        var modGlyphs = new System.Text.StringBuilder();
        for (int m = 0; m < modCount; m++) modGlyphs.Append(ShortModifierGlyph(paramList[m]));
        return (Display(baseKc), modGlyphs.ToString());
    }
}
