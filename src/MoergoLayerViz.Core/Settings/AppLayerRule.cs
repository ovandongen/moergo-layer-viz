namespace MoergoLayerViz.Core.Settings;

/// <summary>
/// One per-keyboard rule mapping a focused-app process name to a layer index.
/// Match is case-insensitive substring against <c>ActiveWindowInfo.ProcessName</c>;
/// Phase 3 will use the first rule (list order) that matches and push that
/// layer to the keyboard. Phase 2 only persists + previews matches in the UI.
/// </summary>
public sealed record AppLayerRule(string ProcessMatch, int LayerIndex);
