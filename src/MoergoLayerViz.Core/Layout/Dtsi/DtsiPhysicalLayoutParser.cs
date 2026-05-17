using System.Globalization;
using System.Text.RegularExpressions;

namespace MoergoLayerViz.Core.Layout.Dtsi;

/// <summary>
/// Parses ZMK <c>physical_layouts.dtsi</c> files into a flat list of
/// <see cref="DtsiKeyPhysical"/>. Only <c>&amp;key_physical_attrs</c> lines
/// matter — surrounding devicetree scaffolding is ignored. Negative
/// rotations use devicetree's <c>(-NNNN)</c> wrapper, which is normalized
/// here.
/// </summary>
public static class DtsiPhysicalLayoutParser
{
    // Seven integer fields after &key_physical_attrs. Each can be a bare
    // number (signed) or a (-NNNN) wrapper that devicetree requires for
    // negatives inside <...> cell arrays. Whitespace between fields is
    // arbitrary (the upstream files line-align with extra spaces).
    private const string IntField = @"(-?\d+|\(\s*-\d+\s*\))";
    private static readonly Regex EntryRegex = new(
        @"&key_physical_attrs\s+" +
        IntField + @"\s+" + IntField + @"\s+" +
        IntField + @"\s+" + IntField + @"\s+" +
        IntField + @"\s+" + IntField + @"\s+" + IntField,
        RegexOptions.Compiled);

    public static DtsiPhysicalLayout Parse(string text)
    {
        var keys = new List<DtsiKeyPhysical>();
        foreach (Match m in EntryRegex.Matches(text))
        {
            keys.Add(new DtsiKeyPhysical(
                Width: ParseField(m.Groups[1].Value),
                Height: ParseField(m.Groups[2].Value),
                X: ParseField(m.Groups[3].Value),
                Y: ParseField(m.Groups[4].Value),
                Rotation: ParseField(m.Groups[5].Value),
                Rx: ParseField(m.Groups[6].Value),
                Ry: ParseField(m.Groups[7].Value)));
        }
        return new DtsiPhysicalLayout(keys);
    }

    private static int ParseField(string raw)
    {
        // Strip the devicetree (-NNNN) wrapper if present, then parse as a
        // signed integer. The regex already guarantees one of these shapes.
        var trimmed = raw.Trim();
        if (trimmed.StartsWith('(') && trimmed.EndsWith(')'))
            trimmed = trimmed[1..^1].Trim();
        return int.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }
}
