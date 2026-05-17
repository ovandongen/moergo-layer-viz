namespace MoergoLayerViz.Core.Layout;

/// <summary>
/// Moergo GO60: 60 keys, 6 columns per hand, three-key thumb clusters.
/// Geometry loaded from <c>Resources/go60.dtsi</c>.
/// <para>
/// Quirk: the dtsi's keys[] is in ZMK visual reading order (row 5 left → both
/// thumb clusters → row 5 right) while Moergo's layout-editor JSON exports in
/// a different order (row 5 both hands → left thumbs → right thumbs). The
/// 9-entry <see cref="BindingToDtsi"/> table fixes that up. Glove80's dtsi
/// happens to use the JSON order natively, so it doesn't need a remap.
/// </para>
/// </summary>
public sealed class Go60Profile() : DtsiKeyboardProfile(
    id: "GO60",
    displayName: "Moergo GO60",
    keyCount: 60,
    vendorId: ZmkHidIds.VendorId,
    productId: ZmkHidIds.ProductId,
    hidNameSubstring: "Go60",
    dtsiResourceName: "MoergoLayerViz.Core.Resources.go60.dtsi",
    bindingToDtsi: BindingToDtsi,
    midlineCentiU: 850,
    // Horizontal margin half a key-width on each side (30 px); leftmost key at
    // x=30, rightmost spans to 1170. Vertical margin one full key-height (60 px);
    // bottommost spans to ~444. Modest slack on both axes for visual breathing room.
    canvasWidth: 1200,
    canvasHeight: 504,
    rightmostMatrixXCentiU: 1650,
    thumbLabels: ThumbLabels)
{
    // JSON binding-index → dtsi keys[]-index remap. Indices 0..50 coincide;
    // only the bottom 9 keys reshuffle.
    private static readonly int[] BindingToDtsi =
    [
         0,  1,  2,  3,  4,  5,  6,  7,  8,  9, 10, 11, // row 1
        12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, // row 2
        24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, // row 3
        36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, // row 4
        48, 49, 50,                                     // L row 5
        57, 58, 59,                                     // R row 5 (dtsi appends after the thumbs)
        51, 52, 53,                                     // L thumbs outer → inner
        54, 55, 56,                                     // R thumbs inner → outer
    ];

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
