namespace MoergoLayerViz.Core.Settings;

/// <summary>
/// One per-keyboard binding mapping a global hotkey to a layer index for the
/// overlay's display-only "layer view" override. Pressing the hotkey toggles
/// the override on; pressing it again clears it and the overlay reverts to
/// the underlying source (HID-reported layer when connected, else 0).
/// Names are neutral (<c>"F18"</c>, <c>"Ctrl+Alt"</c>) — parsed by the
/// platform hotkey registries.
/// </summary>
public sealed record HotkeyLayerBinding(string KeyName, string ModifiersName, int LayerIndex);
