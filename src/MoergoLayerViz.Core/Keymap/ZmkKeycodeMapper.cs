using MoergoLayerViz.Core.Models;

namespace MoergoLayerViz.Core.Keymap;

/// <summary>
/// Translates ZMK binding params (keycodes + modifier wrappers) into the
/// (modifier-set, base-keycode) pair the host OS will see when the key
/// fires. Pure functions; no state. Used by the highlight-lookup pipeline
/// to align "what the firmware emits" with "what the OS reports".
/// </summary>
public static class ZmkKeycodeMapper
{
    /// <summary>
    /// Returns the (modifier-set, base-keycode) pair a binding emits on press,
    /// or null if the binding does not surface a keycode to the host.
    /// After <c>MoergoJsonLoader.FlattenParams</c>, modifier wrappers appear
    /// as flat prefix tokens (LS, LC, ...) before the innermost key at the
    /// last position. We also fold in the implicit Shift that ZMK's
    /// shifted-symbol aliases carry (LPAR, STAR, ...).
    /// </summary>
    public static (HashSet<string> Mods, string Code)? ExtractEmittedKeypress(KeyBinding b)
    {
        int startIndex;
        switch (b.Behavior)
        {
            case "&kp" when b.Params.Count >= 1: startIndex = 0; break;
            case "&lt" when b.Params.Count >= 2: startIndex = 1; break;
            default:
                if (b.Behavior.StartsWith("&HRM_", StringComparison.Ordinal) && b.Params.Count >= 2)
                    startIndex = 1;
                else
                    return null;
                break;
        }

        var mods = new HashSet<string>(StringComparer.Ordinal);
        for (int i = startIndex; i < b.Params.Count - 1; i++)
        {
            var cat = CategoryForWrapperPrefix(b.Params[i]);
            if (cat is not null) mods.Add(cat);
        }
        var (extraMods, code) = CanonicalizeKeycode(b.Params[^1]);
        foreach (var m in extraMods) mods.Add(m);
        return (mods, code);
    }

    /// <summary>
    /// Canonicalizes a ZMK keycode token, returning any modifiers the token
    /// itself implicitly carries (shifted-symbol aliases → Shift).
    /// </summary>
    public static (IEnumerable<string> Mods, string Code) CanonicalizeKeycode(string raw)
    {
        var s = raw.Trim();
        if (ZmkShiftedSymbols.TryGetValue(s, out var shiftedBase))
            return (new[] { "shift" }, shiftedBase);
        if (ZmkPlainAliases.TryGetValue(s, out var plain))
            return (Array.Empty<string>(), plain);
        return (Array.Empty<string>(), s);
    }

    /// <summary>
    /// Normalizes a modifier keycode (as emitted by the hook) to its
    /// left/right-agnostic category. Returns null for non-modifier keycodes.
    /// </summary>
    public static string? CategoryForModifier(string zmkCode) => zmkCode switch
    {
        "LSHFT" or "RSHFT" => "shift",
        "LCTRL" or "RCTRL" => "ctrl",
        "LALT" or "RALT" => "alt",
        "LGUI" or "RGUI" => "gui",
        _ => null,
    };

    /// <summary>
    /// Categorizes a ZMK modifier-wrapper prefix (LS/RS/LC/RC/LA/RA/LG/RG).
    /// Returns null if the token is not a recognized wrapper.
    /// </summary>
    public static string? CategoryForWrapperPrefix(string p) => p switch
    {
        "LS" or "RS" => "shift",
        "LC" or "RC" => "ctrl",
        "LA" or "RA" => "alt",
        "LG" or "RG" => "gui",
        _ => null,
    };

    /// <summary>
    /// Canonical lookup-key format: <c>"shift+ctrl|N8"</c> — sorted modifier
    /// categories, then '|', then the base keycode. Modifiers are already
    /// folded to 4 categories so LS(...) and RS(...) collapse together.
    /// </summary>
    public static string BuildLookupKey(IEnumerable<string> modCategories, string code)
    {
        var sorted = modCategories.OrderBy(m => m, StringComparer.Ordinal);
        return $"{string.Join("+", sorted)}|{code}";
    }

    /// <summary>
    /// Long-form ZMK aliases mapping to the short canonical form
    /// (EQUALS→EQUAL, LEFT_SHIFT→LSHFT). Pure renames; do not change the
    /// modifier set.
    /// </summary>
    private static readonly Dictionary<string, string> ZmkPlainAliases = new(StringComparer.Ordinal)
    {
        // Long-form modifier aliases
        ["LEFT_SHIFT"] = "LSHFT",       ["RIGHT_SHIFT"] = "RSHFT",
        ["LEFT_CONTROL"] = "LCTRL",     ["RIGHT_CONTROL"] = "RCTRL",
        ["LEFT_ALT"] = "LALT",          ["RIGHT_ALT"] = "RALT",
        ["LEFT_GUI"] = "LGUI",          ["RIGHT_GUI"] = "RGUI",
        ["LEFT_COMMAND"] = "LGUI",      ["RIGHT_COMMAND"] = "RGUI",
        ["LEFT_WIN"] = "LGUI",          ["RIGHT_WIN"] = "RGUI",
        ["LEFT_META"] = "LGUI",         ["RIGHT_META"] = "RGUI",

        // Punctuation long-form
        ["EQUALS"] = "EQUAL",
        ["SLASH"] = "FSLH",             ["FORWARD_SLASH"] = "FSLH",
        ["BACKSLASH"] = "BSLH",
        ["SEMICOLON"] = "SEMI",
        ["SINGLE_QUOTE"] = "SQT",       ["APOS"] = "SQT",       ["APOSTROPHE"] = "SQT",
        ["PERIOD"] = "DOT",
        ["LEFT_BRACKET"] = "LBKT",      ["RIGHT_BRACKET"] = "RBKT",

        // Edit / whitespace long-form
        ["BACKSPACE"] = "BSPC",
        ["ENTER"] = "RET",              ["RETURN"] = "RET",
        ["ESCAPE"] = "ESC",
        ["DELETE"] = "DEL",
        ["CAPSLOCK"] = "CAPS",          ["CAPS_LOCK"] = "CAPS",

        // Arrows / nav
        ["UP_ARROW"] = "UP",            ["DOWN_ARROW"] = "DOWN",
        ["LEFT_ARROW"] = "LEFT",        ["RIGHT_ARROW"] = "RIGHT",
        ["PAGE_UP"] = "PG_UP",          ["PAGE_DOWN"] = "PG_DN",
        ["INSERT"] = "INS",

        // Number long-form
        ["NUMBER_0"] = "N0",            ["NUMBER_1"] = "N1",
        ["NUMBER_2"] = "N2",            ["NUMBER_3"] = "N3",
        ["NUMBER_4"] = "N4",            ["NUMBER_5"] = "N5",
        ["NUMBER_6"] = "N6",            ["NUMBER_7"] = "N7",
        ["NUMBER_8"] = "N8",            ["NUMBER_9"] = "N9",

        // Keypad long-form → short form (keypad keys are not shift-wrapped)
        ["KP_NUMBER_0"] = "KP_N0",      ["KP_NUMBER_1"] = "KP_N1",
        ["KP_NUMBER_2"] = "KP_N2",      ["KP_NUMBER_3"] = "KP_N3",
        ["KP_NUMBER_4"] = "KP_N4",      ["KP_NUMBER_5"] = "KP_N5",
        ["KP_NUMBER_6"] = "KP_N6",      ["KP_NUMBER_7"] = "KP_N7",
        ["KP_NUMBER_8"] = "KP_N8",      ["KP_NUMBER_9"] = "KP_N9",
        ["KP_EQUALS"] = "KP_EQUAL",
        ["KP_ASTERISK"] = "KP_MULTIPLY",
        ["KP_PERIOD"] = "KP_DOT",
        ["KP_SLASH"] = "KP_DIVIDE",
    };

    /// <summary>
    /// Shifted-symbol aliases: these ZMK names implicitly include Shift.
    /// A binding like <c>&amp;kp LPAR</c> emits Shift+9, so the lookup
    /// entry is keyed on the <em>shift+base</em> combo, not bare N9.
    /// </summary>
    private static readonly Dictionary<string, string> ZmkShiftedSymbols = new(StringComparer.Ordinal)
    {
        ["EXCL"] = "N1",                ["EXCLAMATION"] = "N1",
        ["AT"] = "N2",                  ["AT_SIGN"] = "N2",
        ["HASH"] = "N3",                ["POUND"] = "N3",
        ["DLLR"] = "N4",                ["DOLLAR"] = "N4",
        ["PRCNT"] = "N5",               ["PERCENT"] = "N5",
        ["CARET"] = "N6",
        ["AMPS"] = "N7",                ["AMPERSAND"] = "N7",
        ["STAR"] = "N8",                ["ASTERISK"] = "N8",
        ["LPAR"] = "N9",                ["LEFT_PARENTHESIS"] = "N9",
        ["RPAR"] = "N0",                ["RIGHT_PARENTHESIS"] = "N0",
        ["LBRC"] = "LBKT",              ["LEFT_BRACE"] = "LBKT",
        ["RBRC"] = "RBKT",              ["RIGHT_BRACE"] = "RBKT",
        ["COLON"] = "SEMI",
        ["DQT"] = "SQT",                ["DOUBLE_QUOTES"] = "SQT",
        ["TILDE"] = "GRAVE",
        ["PIPE"] = "BSLH",
        ["QMARK"] = "FSLH",             ["QUESTION"] = "FSLH",
        ["UNDER"] = "MINUS",            ["UNDERSCORE"] = "MINUS",
        ["PLUS"] = "EQUAL",
        ["LT"] = "COMMA",               ["LESS_THAN"] = "COMMA",
        ["GT"] = "DOT",                 ["GREATER_THAN"] = "DOT",
    };
}
