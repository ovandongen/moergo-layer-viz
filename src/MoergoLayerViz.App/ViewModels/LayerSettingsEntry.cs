namespace MoergoLayerViz.App.ViewModels;

/// <summary>
/// One row in the Settings window's per-layer table — layer name + color
/// picker. Wraps <see cref="LayerColorEntry"/> so the Settings UI binds to a
/// uniform shape.
/// </summary>
public sealed class LayerSettingsEntry
{
    public int Index { get; }
    public string DisplayName { get; }
    public LayerColorEntry Color { get; }

    public LayerSettingsEntry(LayerColorEntry color)
    {
        Index = color.Index;
        DisplayName = color.DisplayName;
        Color = color;
    }
}
