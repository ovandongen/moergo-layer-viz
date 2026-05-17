namespace MoergoLayerViz.Core.Layout;

/// <summary>Which half of the split keyboard a key belongs to. Drives the stacked-layout split rendering.</summary>
public enum Hand
{
    Left,
    Right,
}

/// <summary>
/// Absolute position of a single physical key on the board canvas.
/// Coordinates are the upper-left corner of the rectangle, in canvas units.
/// </summary>
/// <param name="Index">
/// Matches the JSON keymap binding index (layers[layer][index]).
/// Do not reorder without reshuffling the JSON ordering too.
/// </param>
/// <param name="X">Upper-left X in canvas units.</param>
/// <param name="Y">Upper-left Y in canvas units.</param>
/// <param name="Width">Rectangle width. Defaults to <see cref="StandardKeySize"/>.</param>
/// <param name="Height">Rectangle height. Defaults to <see cref="StandardKeySize"/>.</param>
/// <param name="RotationDegrees">Rotation, around the key's centre unless <see cref="RotationOriginX"/>/<see cref="RotationOriginY"/> are set.</param>
/// <param name="Description">
/// Optional human-readable physical position label (e.g. "Row 2, L col 4",
/// "Left thumb 1") used for tooltips. Profiles populate this; null is fine
/// and callers fall back to the index.
/// </param>
/// <param name="Hand">
/// Which half of the split keyboard this key belongs to. Profiles assign
/// this at build time; the stacked-layout renderer partitions keys by hand
/// so each half can be translated independently.
/// </param>
/// <param name="RotationOriginX">
/// Optional rotation pivot X in canvas units (same frame as <see cref="X"/>).
/// Null means rotate around the key's own centre. Glove80 thumbs share a
/// pivot well outside any individual key, which the per-key center model
/// can't express.
/// </param>
/// <param name="RotationOriginY">Optional rotation pivot Y in canvas units. See <see cref="RotationOriginX"/>.</param>
public sealed record KeyPosition(
    int Index,
    double X,
    double Y,
    double Width = KeyPosition.StandardKeySize,
    double Height = KeyPosition.StandardKeySize,
    double RotationDegrees = 0,
    string? Description = null,
    Hand Hand = Hand.Left,
    double? RotationOriginX = null,
    double? RotationOriginY = null)
{
    public const double StandardKeySize = 60;

    /// <summary>
    /// Axis-aligned bounding box of this key after rotation about its pivot
    /// (the explicit <see cref="RotationOriginX"/>/<see cref="RotationOriginY"/>
    /// when set, otherwise the key's own centre). For unrotated keys this
    /// collapses to <c>(X, Y, X+Width, Y+Height)</c>.
    /// <para>
    /// Needed by anything that has to reason about where a key actually sits
    /// visually — Glove80's six thumbs per hand share an anatomical pivot
    /// well below the lowest matrix row, so their unrotated rectangle is
    /// nowhere near the rendered position.
    /// </para>
    /// </summary>
    public (double MinX, double MinY, double MaxX, double MaxY) RotatedBounds()
    {
        if (RotationDegrees == 0)
            return (X, Y, X + Width, Y + Height);

        var px = RotationOriginX ?? X + Width / 2;
        var py = RotationOriginY ?? Y + Height / 2;
        var rad = RotationDegrees * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        Span<(double X, double Y)> corners =
        [
            (X, Y),
            (X + Width, Y),
            (X, Y + Height),
            (X + Width, Y + Height),
        ];
        foreach (var (cx, cy) in corners)
        {
            var dx = cx - px;
            var dy = cy - py;
            var rx = px + dx * cos - dy * sin;
            var ry = py + dx * sin + dy * cos;
            if (rx < minX) minX = rx;
            if (ry < minY) minY = ry;
            if (rx > maxX) maxX = rx;
            if (ry > maxY) maxY = ry;
        }
        return (minX, minY, maxX, maxY);
    }
}

/// <summary>Geometry helpers over collections of <see cref="KeyPosition"/>.</summary>
public static class KeyPositionExtensions
{
    /// <summary>
    /// Axis-aligned bounding box that contains every key in the sequence after
    /// each is rotated about its own pivot. Empty sequence returns the zero
    /// rectangle. See <see cref="KeyPosition.RotatedBounds"/> for the per-key
    /// computation this aggregates.
    /// </summary>
    public static (double MinX, double MinY, double MaxX, double MaxY) RotatedBounds(
        this IEnumerable<KeyPosition> keys)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        var any = false;
        foreach (var k in keys)
        {
            any = true;
            var (kMinX, kMinY, kMaxX, kMaxY) = k.RotatedBounds();
            if (kMinX < minX) minX = kMinX;
            if (kMinY < minY) minY = kMinY;
            if (kMaxX > maxX) maxX = kMaxX;
            if (kMaxY > maxY) maxY = kMaxY;
        }
        return any ? (minX, minY, maxX, maxY) : (0, 0, 0, 0);
    }
}
