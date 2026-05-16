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
///
/// <para><see cref="EndlessTimeout"/> suppresses the idle-driven revert
/// entirely — the engine still captures the pre-move layer at move-start,
/// but only an external exit (e.g. one of the transparent/empty key triggers
/// below) can unwind the push. The host is expected to enforce that at least
/// one of <see cref="ExitOnTransparentKey"/> / <see cref="ExitOnEmptyKey"/>
/// is true while endless is on, otherwise the push can never be released.</para>
///
/// <para><see cref="ExitOnTransparentKey"/> and <see cref="ExitOnEmptyKey"/>
/// are independent opt-ins. The coordinator reverts this engine's push when a
/// keyboard keypress lands on a binding that is literally <c>&amp;trans</c>
/// (transparent flag) or <c>&amp;none</c> (empty flag) respectively. Both work
/// alongside any timeout as an "exit earlier than idle" gesture.</para>
/// </summary>
public sealed record MouseLayerSettings(
    bool Enabled = false,
    int? MouseLayerIndex = null,
    int IdleTimeoutMs = 500,
    bool EndlessTimeout = false,
    bool ExitOnTransparentKey = false,
    bool ExitOnEmptyKey = false);
