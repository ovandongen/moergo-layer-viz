namespace MoergoLayerViz.Core.Layout.Dtsi;

/// <summary>
/// One <c>&amp;key_physical_attrs</c> entry from a ZMK physical-layouts dtsi.
/// All values are in the dtsi's native units: centi-keyboard-units for the
/// geometry (100 = 1u) and centi-degrees for rotation. Negative rotations
/// appear as <c>(-3700)</c> in the source and arrive here as ordinary negatives.
/// Conversion to pixel coords happens at the profile boundary so this stays
/// format-pure.
/// </summary>
public sealed record DtsiKeyPhysical(
    int Width,
    int Height,
    int X,
    int Y,
    int Rotation,
    int Rx,
    int Ry);

/// <summary>Parsed dtsi physical layout: the keys list in dtsi declaration order.
/// This is the dtsi's visual reading order — it does <b>not</b> necessarily match
/// Moergo's JSON <c>bindings[]</c> / firmware matrix-transform order. Glove80 happens
/// to agree (identity); Go60 needs a 9-entry remap for the row-5 + thumb block. See
/// <c>Go60Profile.BindingToDtsi</c> and the
/// <c>moergo-json-vs-dtsi-order</c> memory.</summary>
public sealed class DtsiPhysicalLayout
{
    public IReadOnlyList<DtsiKeyPhysical> Keys { get; }

    public DtsiPhysicalLayout(IReadOnlyList<DtsiKeyPhysical> keys)
    {
        Keys = keys;
    }
}
