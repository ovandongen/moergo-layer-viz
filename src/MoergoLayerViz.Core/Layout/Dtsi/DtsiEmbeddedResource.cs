using System.IO;
using System.Reflection;

namespace MoergoLayerViz.Core.Layout.Dtsi;

/// <summary>
/// Loads a ZMK <c>physical_layouts.dtsi</c> file embedded in the Core assembly.
/// Profiles bundle their canonical layout as an <c>&lt;EmbeddedResource&gt;</c>
/// so the geometry travels with the binary instead of living as hand-tuned
/// literals in C#.
/// </summary>
public static class DtsiEmbeddedResource
{
    /// <param name="resourceName">Full manifest name, e.g.
    /// <c>MoergoLayerViz.Core.Resources.go60.dtsi</c>.</param>
    public static DtsiPhysicalLayout Load(string resourceName)
    {
        var asm = typeof(DtsiEmbeddedResource).Assembly;
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded dtsi '{resourceName}' not found. Manifest contains: "
                + string.Join(", ", asm.GetManifestResourceNames()));
        using var reader = new StreamReader(stream);
        return DtsiPhysicalLayoutParser.Parse(reader.ReadToEnd());
    }
}
