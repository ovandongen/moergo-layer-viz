using MoergoLayerViz.Core.Layout.Dtsi;

namespace MoergoLayerViz.Core.Layout;

/// <summary>
/// Data-driven <see cref="IKeyboardProfile"/> for any board whose physical
/// geometry comes from a ZMK <c>physical_layouts.dtsi</c> file. Pass the
/// per-board metadata to the constructor; everything else (key construction,
/// hand assignment, tooltip text, HID matching) is shared.
/// </summary>
public class DtsiKeyboardProfile : IKeyboardProfile
{
    private readonly int _vendorId;
    private readonly int _productId;
    private readonly string _hidNameSubstring;
    private readonly int _rightmostMatrixXCentiU;
    private readonly IReadOnlyDictionary<int, string>? _thumbLabels;

    /// <summary>
    /// Per-column finger names. Boards address columns 1..N from the outer
    /// pinky inward; Glove80's 5-key outer rows simply stop at index 5.
    /// </summary>
    private static readonly string[] FingerByCol =
        ["outer pinky", "pinky", "ring", "middle", "index", "inner index"];

    /// <param name="id">Stable id matching <c>UserSettings.Keyboard</c> (e.g. "GO60", "Glove80").</param>
    /// <param name="displayName">UI label.</param>
    /// <param name="keyCount">Total binding count.</param>
    /// <param name="vendorId">USB HID vendor id used for device matching.</param>
    /// <param name="productId">USB HID product id used for device matching.</param>
    /// <param name="hidNameSubstring">Case-insensitive product-name substring (e.g. "Go60", "Glove80").</param>
    /// <param name="dtsiResourceName">Embedded-resource name of the dtsi geometry file.</param>
    /// <param name="midlineCentiU">Centi-unit X that separates left-hand keys from right-hand keys.</param>
    /// <param name="canvasWidth">Visual canvas width in px.</param>
    /// <param name="canvasHeight">Visual canvas height in px.</param>
    /// <param name="rightmostMatrixXCentiU">Centi-unit X of the right-hand outer-pinky column (used to derive RH column numbers in tooltips).</param>
    /// <param name="thumbLabels">Optional binding-index → tooltip override for rotated keys (the thumb cluster). Falls back to <c>"Thumb (idx N)"</c>.</param>
    protected DtsiKeyboardProfile(
        string id,
        string displayName,
        int keyCount,
        int vendorId,
        int productId,
        string hidNameSubstring,
        string dtsiResourceName,
        int midlineCentiU,
        double canvasWidth,
        double canvasHeight,
        int rightmostMatrixXCentiU,
        IReadOnlyDictionary<int, string>? thumbLabels = null)
    {
        Id = id;
        DisplayName = displayName;
        KeyCount = keyCount;
        CanvasWidth = canvasWidth;
        CanvasHeight = canvasHeight;
        _vendorId = vendorId;
        _productId = productId;
        _hidNameSubstring = hidNameSubstring;
        _rightmostMatrixXCentiU = rightmostMatrixXCentiU;
        _thumbLabels = thumbLabels;
        Keys = DtsiBackedLayoutBuilder.Build(
            resourceName: dtsiResourceName,
            keyCount: keyCount,
            midlineCentiU: midlineCentiU,
            describeKey: DescribeKey);
    }

    public string Id { get; }
    public string DisplayName { get; }
    public int KeyCount { get; }
    public double CanvasWidth { get; }
    public double CanvasHeight { get; }
    public IReadOnlyList<KeyPosition> Keys { get; }

    public bool MatchesHidDevice(int vendorId, int productId, string? productName) =>
        vendorId == _vendorId
        && productId == _productId
        && NameMatches(productName);

    public bool NameMatches(string? productName) =>
        productName is not null
        && productName.Contains(_hidNameSubstring, StringComparison.OrdinalIgnoreCase);

    private string DescribeKey(int binding, DtsiKeyPhysical entry, Hand hand)
    {
        // Rotated keys are always thumbs on the boards we know about. Custom
        // labels disambiguate row A vs row B (Glove80) or outer/inner (Go60);
        // missing entries fall back to a numeric label so future boards
        // don't have to enumerate every rotated key.
        if (entry.Rotation != 0)
            return _thumbLabels?.TryGetValue(binding, out var label) == true
                ? label
                : $"Thumb (idx {binding})";

        var row = entry.Y / 100 + 1;
        var col = hand == Hand.Left
            ? entry.X / 100 + 1
            : (_rightmostMatrixXCentiU - entry.X) / 100 + 1;
        return $"Row {row}, {(hand == Hand.Left ? "L" : "R")} col {col} ({FingerByCol[col - 1]})";
    }
}
