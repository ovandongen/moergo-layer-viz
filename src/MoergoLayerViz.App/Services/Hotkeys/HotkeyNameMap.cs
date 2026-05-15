namespace MoergoLayerViz.App.Services.Hotkeys;

/// <summary>
/// Neutral hotkey-name parsers. Keys are F1–F24 today (the only set our
/// Settings UI exposes); modifiers are "+"-joined "Ctrl/Alt/Shift/Meta".
/// Platform-specific registries translate these neutral names into their
/// native virtual-key / modifier-mask types.
/// </summary>
internal static class HotkeyNameMap
{
    /// <summary>Parses an F-key name ("F1".."F24") into its 1-based F-index, or null when unsupported.</summary>
    public static int? TryParseFKeyIndex(string keyName)
    {
        if (string.IsNullOrEmpty(keyName) || keyName.Length < 2) return null;
        if (keyName[0] != 'F' && keyName[0] != 'f') return null;
        if (!int.TryParse(keyName.AsSpan(1), out var n)) return null;
        if (n < 1 || n > 24) return null;
        return n;
    }

    /// <summary>
    /// Parses a modifier expression ("None" / "" / "Ctrl+Shift" / "Alt").
    /// Calls <paramref name="addModifier"/> once per recognized modifier with
    /// the canonical name (Ctrl / Alt / Shift / Meta). Returns false on any
    /// unrecognized token.
    /// </summary>
    public static bool TryParseModifiers(string? modifiersName, Action<string> addModifier)
    {
        if (string.IsNullOrWhiteSpace(modifiersName)) return true;
        if (modifiersName.Equals("None", StringComparison.OrdinalIgnoreCase)) return true;

        foreach (var raw in modifiersName.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var canonical = raw switch
            {
                _ when raw.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) => "Ctrl",
                _ when raw.Equals("Control", StringComparison.OrdinalIgnoreCase) => "Ctrl",
                _ when raw.Equals("Alt", StringComparison.OrdinalIgnoreCase) => "Alt",
                _ when raw.Equals("Option", StringComparison.OrdinalIgnoreCase) => "Alt",
                _ when raw.Equals("Shift", StringComparison.OrdinalIgnoreCase) => "Shift",
                _ when raw.Equals("Meta", StringComparison.OrdinalIgnoreCase) => "Meta",
                _ when raw.Equals("Cmd", StringComparison.OrdinalIgnoreCase) => "Meta",
                _ when raw.Equals("Command", StringComparison.OrdinalIgnoreCase) => "Meta",
                _ when raw.Equals("Win", StringComparison.OrdinalIgnoreCase) => "Meta",
                _ => null,
            };
            if (canonical is null) return false;
            addModifier(canonical);
        }
        return true;
    }
}
