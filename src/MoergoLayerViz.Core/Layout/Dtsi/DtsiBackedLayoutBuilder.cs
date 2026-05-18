namespace MoergoLayerViz.Core.Layout.Dtsi;

/// <summary>
/// Shared loop that turns a parsed dtsi <see cref="DtsiPhysicalLayout"/> into
/// the <see cref="KeyPosition"/> list each profile exposes. The conversion is
/// purely numeric — centi-units → pixels with a margin, centi-degrees → degrees,
/// optional rotation pivot — so both shipped boards (and any future dtsi-backed
/// board) parameterize the same loop instead of copy-pasting it.
/// </summary>
internal static class DtsiBackedLayoutBuilder
{
    /// <summary>
    /// Build the <see cref="KeyPosition"/> list for a profile.
    /// </summary>
    /// <param name="resourceName">Embedded resource name of the dtsi file (e.g. <c>"MoergoLayerViz.Core.Resources.go60.dtsi"</c>).</param>
    /// <param name="keyCount">Number of bindings the profile exposes. Drives the iteration and the list capacity.</param>
    /// <param name="midlineCentiU">X cutoff in centi-units that separates left-hand keys from right-hand keys.</param>
    /// <param name="describeKey">
    /// Per-profile tooltip describer. Receives binding index, the raw dtsi
    /// entry (centi-units, centi-degrees), and the already-computed hand.
    /// </param>
    /// <param name="pxPerCentiU">Pixel multiplier. Both shipping boards use 0.6 (1u = 60 px).</param>
    /// <param name="marginX">Left-edge inset in pixels.</param>
    /// <param name="marginY">Top-edge inset in pixels.</param>
    public static IReadOnlyList<KeyPosition> Build(
        string resourceName,
        int keyCount,
        int midlineCentiU,
        Func<int, DtsiKeyPhysical, Hand, string> describeKey,
        double pxPerCentiU = 0.6,
        double marginX = 30,
        double marginY = 60)
    {
        var layout = DtsiEmbeddedResource.Load(resourceName);
        var dtsi = layout.Keys;
        var list = new List<KeyPosition>(keyCount);
        for (int binding = 0; binding < keyCount; binding++)
        {
            var entry = dtsi[binding];
            var hand = entry.X < midlineCentiU ? Hand.Left : Hand.Right;
            double? rotOx = null, rotOy = null;
            if (entry.Rotation != 0)
            {
                rotOx = entry.Rx * pxPerCentiU + marginX;
                rotOy = entry.Ry * pxPerCentiU + marginY;
            }
            list.Add(new KeyPosition(
                Index: binding,
                X: entry.X * pxPerCentiU + marginX,
                Y: entry.Y * pxPerCentiU + marginY,
                Width: entry.Width * pxPerCentiU,
                Height: entry.Height * pxPerCentiU,
                RotationDegrees: entry.Rotation / 100.0,
                Description: describeKey(binding, entry, hand),
                Hand: hand,
                RotationOriginX: rotOx,
                RotationOriginY: rotOy));
        }
        return list;
    }
}
