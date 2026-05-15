using System.Text;
using MoergoLayerViz.Core.Models;

namespace MoergoLayerViz.Core.Keymap;

/// <summary>
/// Pure string-formatting helpers for rendering a <see cref="KeyBinding"/>
/// into label / subscript / badge triples, the per-section tooltip lines,
/// and the param-formatters for ZMK behaviors. No view-model state — every
/// method here is static and side-effect-free. <see cref="KeyViewModel"/>
/// composes these into its observable display properties.
/// </summary>
public static class KeyLabelFormatter
{
    // ─── Label / subscript / badge ────────────────────────────────────────

    /// <summary>
    /// Picks the (label, subscript, top-left-badge) triple for a binding in
    /// strict precedence order:
    /// <list type="number">
    /// <item>User-authored <c>decoration.label</c> — overrides everything.</item>
    /// <item>Hold-tap — tap-side keycode as main label, hold-side layer or keycode as subscript.</item>
    /// <item>Standard ZMK behavior with declared arity (<c>&amp;kp</c>, <c>&amp;mo</c>, <c>&amp;bt</c>, …).</item>
    /// <item>Moergo macro conventions (<c>&amp;HRM_*</c>, <c>&amp;bt_*</c>).</item>
    /// <item>Fallback — bare behavior name or joined params.</item>
    /// </list>
    /// </summary>
    public static (string Label, string Subscript, string TopLeft) FormatBinding(
        KeyBinding b, string? targetLayerName, HoldTap? holdTap = null)
    {
        if (!string.IsNullOrEmpty(b.DecorationLabel))
            return (b.DecorationLabel, "", "");

        // Display-only camelCase/underscore split so TextBlock.TextWrapping
        // can break long layer names onto multiple lines; tooltip keeps the raw form.
        var layerName = targetLayerName is null ? null : FormatLayerName(targetLayerName);

        if (holdTap is not null)
            return FormatHoldTap(b, holdTap, targetLayerName, layerName);

        return TryFormatStandardBehavior(b, layerName)
            ?? TryFormatMacroConvention(b)
            ?? FormatFallback(b);
    }

    /// <summary>
    /// Renders a hold-tap as: tap-side keycode in the centre, hold-side
    /// layer name (or hold-keycode label, e.g. "⌥" for an &amp;kp LALT hold
    /// on a homerow-mod) in the subscript, "Hold-Tap" badge in the corner.
    /// Recurses into <see cref="FormatBinding"/> for each side so any
    /// behavior expressible as a standalone binding (kp, mo, …) renders
    /// consistently here.
    /// </summary>
    private static (string Label, string Subscript, string TopLeft) FormatHoldTap(
        KeyBinding b, HoldTap holdTap, string? rawLayerName, string? formattedLayerName)
    {
        var (holdParams, tapParams) = SplitHoldTapParams(b.Params, holdTap);
        var tap = FormatBinding(new KeyBinding(holdTap.TapBinding, tapParams), null);
        var hold = FormatBinding(new KeyBinding(holdTap.HoldBinding, holdParams), rawLayerName);
        var sub = !string.IsNullOrEmpty(formattedLayerName) ? formattedLayerName : hold.Label;
        return (tap.Label, sub, "Hold-Tap");
    }

    private static (string Label, string Subscript, string TopLeft)? TryFormatStandardBehavior(
        KeyBinding b, string? layerName)
    {
        string LayerOrIndex(string idxParam) => layerName ?? ("L" + idxParam);

        return b.Behavior switch
        {
            "&trans" => ("▽", "", ""),
            "&none"  => ("", "", ""),

            "&kp" when b.Params.Count >= 1 => FormatKp(b.Params),

            "&to"  when b.Params.Count >= 1 => (LayerOrIndex(b.Params[0]), "", "To Layer"),
            "&mo"  when b.Params.Count >= 1 => (LayerOrIndex(b.Params[0]), "", "Momentary"),
            "&tog" when b.Params.Count >= 1 => (LayerOrIndex(b.Params[0]), "", "Toggle Layer"),
            "&sl"  when b.Params.Count >= 1 => (LayerOrIndex(b.Params[0]), "", "Sticky Layer"),
            "&lt"  when b.Params.Count == 2 && int.TryParse(b.Params[0], out _)
                => (ZmkKeycodeLabel.Display(b.Params[1]), LayerOrIndex(b.Params[0]), "Layer Tap"),

            "&bt"        when b.Params.Count >= 1 => (FormatBtParams(b.Params),        "", "Bluetooth"),
            "&out"       when b.Params.Count >= 1 => (FormatOutParam(b.Params[0]),     "", "Output"),
            "&sys_reset"                          => ("Reset",                         "", "System"),
            "&bootloader"                         => ("Boot",                          "", "System"),
            "&ext_power" when b.Params.Count >= 1 => (FormatExtPowerParam(b.Params[0]), "", "Ext Power"),
            "&rgb_ug"    when b.Params.Count >= 1 => (FormatRgbParam(b.Params[0]),     "", "Underglow"),

            _ => null,
        };
    }

    private static (string Label, string Subscript, string TopLeft) FormatKp(IReadOnlyList<string> p)
    {
        var (label, sub) = ZmkKeycodeLabel.FormatKpParams(p);
        return (label, sub, "");
    }

    /// <summary>
    /// Moergo-specific macro naming conventions that aren't ZMK keywords —
    /// home-row mods (<c>&amp;HRM_*</c>) and the magic-layer Bluetooth
    /// wrappers (<c>&amp;bt_*</c>).
    /// </summary>
    private static (string Label, string Subscript, string TopLeft)? TryFormatMacroConvention(KeyBinding b)
    {
        // &HRM_<name> <modifier-keycode> <base-keycode>
        if (b.Behavior.StartsWith("&HRM_", StringComparison.Ordinal) && b.Params.Count == 2)
        {
            var mod = ZmkKeycodeLabel.ModifierSubscript(b.Params[0]) ?? b.Params[0];
            return (ZmkKeycodeLabel.Display(b.Params[1]), mod, "");
        }

        // &bt_0..4, &bt_clr, … — Moergo magic-layer wrappers.
        if (b.Behavior.StartsWith("&bt_", StringComparison.Ordinal))
        {
            var tail = b.Behavior.Substring(4).Replace('_', ' ').ToUpperInvariant();
            return (tail, "", "Bluetooth");
        }

        return null;
    }

    private static (string Label, string Subscript, string TopLeft) FormatFallback(KeyBinding b) =>
        b.Params.Count == 0
            ? (b.Behavior.TrimStart('&'), "", "")
            : (string.Join(' ', b.Params), "", "");

    /// <summary>
    /// Display-only camelCase/underscore split — used for the subscript so a
    /// long layer name like "HRM_WinLinx" wraps as "HRM Win Linx". Underscores
    /// collapse to a single space; uppercase letters following lowercase or
    /// digits get a space prefix; runs of uppercase ("HRM", "TKZ") stay intact.
    /// </summary>
    public static string FormatLayerName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '_')
            {
                if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
                continue;
            }
            // camelCase break: insert space when an uppercase letter follows a
            // lowercase letter or digit. Runs of uppercase ("HRM", "TKZ") are
            // preserved so acronyms read as a unit.
            if (i > 0 && char.IsUpper(c) && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1])))
            {
                if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
            }
            sb.Append(c);
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Splits the keymap-binding params between the hold and tap sides of a
    /// hold-tap, using each side's declared arity. The hold side consumes the
    /// leading params, the tap side the trailing ones — matching ZMK's order.
    /// If the keymap supplied fewer params than expected (e.g. a malformed
    /// entry), missing params are silently truncated.
    /// </summary>
    public static (IReadOnlyList<string> Hold, IReadOnlyList<string> Tap) SplitHoldTapParams(
        IReadOnlyList<string> all, HoldTap ht)
    {
        var holdCount = Math.Min(ht.HoldArity, all.Count);
        var tapCount = Math.Min(ht.TapArity, Math.Max(0, all.Count - holdCount));
        var hold = new string[holdCount];
        var tap = new string[tapCount];
        for (int i = 0; i < holdCount; i++) hold[i] = all[i];
        for (int i = 0; i < tapCount; i++) tap[i] = all[holdCount + i];
        return (hold, tap);
    }

    // ─── Tooltip sections ─────────────────────────────────────────────────

    /// <summary>
    /// Hold-tap / standard layer-switch tooltip section, or null when the
    /// binding isn't one of those categories.
    /// </summary>
    public static string? BuildCategorySection(
        KeyBinding b, string? targetLayerName, HoldTap? holdTap)
    {
        var layerLabel = targetLayerName ?? "?";

        if (holdTap is not null)
            return BuildHoldTapSection(b, targetLayerName, holdTap);

        var category = StandardLayerCategory(b);
        return category is null ? null : $"{category} → {layerLabel}";
    }

    private static string BuildHoldTapSection(
        KeyBinding b, string? targetLayerName, HoldTap holdTap)
    {
        var (holdParams, tapParams) = SplitHoldTapParams(b.Params, holdTap);
        var heading = targetLayerName is null ? "Hold-Tap" : $"Hold-Tap → {targetLayerName}";
        var holdTail = holdParams.Count > 0 ? " " + string.Join(' ', holdParams) : "";
        var tapTail = tapParams.Count > 0 ? " " + string.Join(' ', tapParams) : "";
        return $"{heading}\n  Hold: {holdTap.HoldBinding}{holdTail}\n  Tap:  {holdTap.TapBinding}{tapTail}";
    }

    /// <summary>
    /// Maps a standard ZMK layer-switching behavior + arity to its human
    /// category name for the tooltip — null when the binding isn't one of
    /// these (and so contributes no category section).
    /// </summary>
    private static string? StandardLayerCategory(KeyBinding b) => b.Behavior switch
    {
        "&lt"  when b.Params.Count == 2 => "Layer Tap",
        "&mo"  when b.Params.Count >= 1 => "Momentary",
        "&to"  when b.Params.Count >= 1 => "To Layer",
        "&tog" when b.Params.Count >= 1 => "Toggle Layer",
        "&sl"  when b.Params.Count >= 1 => "Sticky Layer",
        _ => null,
    };

    /// <summary>
    /// User-authored decoration tooltip section (label and/or background hex),
    /// or null when neither is set.
    /// </summary>
    public static string? BuildDecorationSection(KeyBinding b)
    {
        var hasLabel = !string.IsNullOrEmpty(b.DecorationLabel);
        var hasBackground = !string.IsNullOrEmpty(b.DecorationBackground);
        if (!hasLabel && !hasBackground) return null;

        var lines = new List<string>();
        if (hasLabel) lines.Add($"Label: {b.DecorationLabel}");
        if (hasBackground) lines.Add($"Background: {b.DecorationBackground}");
        return string.Join('\n', lines);
    }

    /// <summary>Combo tooltip section — heading + participating-key labels.</summary>
    public static string BuildComboSection(MoergoCombo combo, Func<int, string> labelLookup)
    {
        var heading = string.IsNullOrEmpty(combo.Name)
            ? $"Combo → {combo.Binding.Display}"
            : $"Combo \"{combo.Name}\" → {combo.Binding.Display}";
        var keys = string.Join(" + ", combo.KeyPositions.Select(idx => DescribeComboKey(idx, labelLookup)));
        return $"{heading}\n  Keys: {keys}";
    }

    private static string DescribeComboKey(int idx, Func<int, string> labelLookup)
    {
        var label = labelLookup(idx);
        return string.IsNullOrWhiteSpace(label) ? idx.ToString() : label;
    }

    // ─── ZMK behavior-param formatters ────────────────────────────────────

    private static string FormatBtParams(IReadOnlyList<string> p) => p[0] switch
    {
        "BT_SEL" when p.Count >= 2 => p[1],
        "BT_CLR" => "Clear",
        "BT_CLR_ALL" => "Clr All",
        "BT_NXT" => "Next",
        "BT_PRV" => "Prev",
        "BT_DISC" when p.Count >= 2 => "Disc " + p[1],
        _ => p[0],
    };

    private static string FormatOutParam(string p) => p switch
    {
        "OUT_TOG" => "Toggle",
        "OUT_USB" => "USB",
        "OUT_BLE" => "BT",
        _ => p,
    };

    private static string FormatExtPowerParam(string p) => p switch
    {
        "EP_ON" => "On",
        "EP_OFF" => "Off",
        "EP_TOG" => "Toggle",
        _ => p,
    };

    private static string FormatRgbParam(string p) => p switch
    {
        "RGB_ON"  => "On",
        "RGB_OFF" => "Off",
        "RGB_TOG" => "Toggle",
        "RGB_EFF" => "Effect +",
        "RGB_EFR" => "Effect −",
        "RGB_HUI" => "Hue +",
        "RGB_HUD" => "Hue −",
        "RGB_SAI" => "Sat +",
        "RGB_SAD" => "Sat −",
        "RGB_BRI" => "Bri +",
        "RGB_BRD" => "Bri −",
        "RGB_SPI" => "Spd +",
        "RGB_SPD" => "Spd −",
        _ => p,
    };

    /// <summary>
    /// Translates Moergo editor's icon identifiers to Font Awesome 6 names.
    /// Handles (a) Ionicons prefixed with <c>io-</c> that have FA equivalents,
    /// and (b) FA4 / FA5 names that were renamed in FA6 Free.
    /// </summary>
    public static string NormalizeIconName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        return raw switch
        {
            // Ionicons → FA
            "io-finger-print" => "fa-fingerprint",

            // FA4/FA5 → FA6 renames (only the ones actually used in Moergo's JSON).
            "fa-search" => "fa-magnifying-glass",
            "fa-search-plus" => "fa-magnifying-glass-plus",
            "fa-search-minus" => "fa-magnifying-glass-minus",
            "fa-redo" => "fa-rotate-right",
            "fa-undo" => "fa-rotate-left",
            "fa-cut" => "fa-scissors",

            _ => raw,
        };
    }
}
