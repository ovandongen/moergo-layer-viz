namespace MoergoLayerViz.Core.Layout;

/// <summary>
/// Moergo GO60: 60 keys, 6 columns per hand, three-key thumb clusters.
/// Geometry loaded from <c>Resources/go60.dtsi</c>.
/// </summary>
public sealed class Go60Profile() : DtsiKeyboardProfile(
    id: "GO60",
    displayName: "Moergo GO60",
    keyCount: 60,
    vendorId: ZmkHidIds.VendorId,
    productId: ZmkHidIds.ProductId,
    hidNameSubstring: "Go60",
    dtsiResourceName: "MoergoLayerViz.Core.Resources.go60.dtsi",
    midlineCentiU: 850,
    // Horizontal margin half a key-width on each side (30 px); leftmost key at
    // x=30, rightmost spans to 1170. Vertical margin one full key-height (60 px);
    // bottommost spans to ~444. Modest slack on both axes for visual breathing room.
    canvasWidth: 1200,
    canvasHeight: 504,
    rightmostMatrixXCentiU: 1650,
    thumbLabels: ThumbLabels)
{
    private static readonly IReadOnlyDictionary<int, string> ThumbLabels =
        new Dictionary<int, string>
        {
            [54] = "Left thumb 1 (outer)",
            [55] = "Left thumb 2",
            [56] = "Left thumb 3 (inner)",
            [57] = "Right thumb 1 (inner)",
            [58] = "Right thumb 2",
            [59] = "Right thumb 3 (outer)",
        };
}
