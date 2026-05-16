namespace MoergoLayerViz.Core.Models;

/// <summary>
/// Classifies a ZMK binding as one of the two "no-op on this layer" flags
/// that the transparent / empty-key exit gesture watches for. Anything
/// else (a real <c>&amp;kp</c>, <c>&amp;mo</c>, hold-tap, etc.) is
/// represented by a null <see cref="TransparentBindingKind"/>? — see the
/// classifier on <c>LayerPushCoordinator</c>.
/// </summary>
public enum TransparentBindingKind
{
    /// <summary>Literal <c>&amp;trans</c> — fall through to the layer underneath.</summary>
    Transparent,

    /// <summary>Literal <c>&amp;none</c> — do nothing.</summary>
    Empty,
}
