using System.Globalization;

namespace MoergoLayerViz.Core.Colors;

/// <summary>
/// Parses <c>#RRGGBB</c> / <c>#RRGGBBAA</c> hex color strings. Tolerates an
/// optional leading <c>#</c> and accepts the 8-character form by discarding
/// the trailing alpha pair. Used by tray-icon tinting and key-stroke darkening.
/// </summary>
public static class HexRgb
{
    public static bool TryParse(string? hex, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var s = hex.AsSpan().TrimStart('#');
        if (s.Length == 8) s = s[..6];
        if (s.Length != 6) return false;
        return byte.TryParse(s[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
            && byte.TryParse(s.Slice(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
            && byte.TryParse(s.Slice(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
    }
}
