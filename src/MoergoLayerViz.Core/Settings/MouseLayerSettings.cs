namespace MoergoLayerViz.Core.Settings;

/// <summary>
/// Per-keyboard configuration for the mouse-movement layer-push engine.
/// While mouse movement is detected, the engine pushes
/// <see cref="MouseLayerIndex"/> to the keyboard via HID; when movement stops
/// for <see cref="IdleTimeoutMs"/> milliseconds, it pushes back the layer
/// that was active at move-start. The revert target is intentionally not
/// user-configurable — see <c>MouseLayerEngine</c>'s class doc. Inert when
/// <see cref="Enabled"/> is false, <see cref="MouseLayerIndex"/> is null, or
/// HID is disconnected.
/// </summary>
public sealed record MouseLayerSettings(
    bool Enabled = false,
    int? MouseLayerIndex = null,
    int IdleTimeoutMs = 500);
