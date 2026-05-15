using MoergoLayerViz.Core.Models;

namespace MoergoLayerViz.Core.Keymap;

/// <summary>
/// Resolves the binding that actually fires at a given (layer, key) position,
/// walking the <c>&amp;trans</c> fall-through chain via a precomputed
/// predecessor graph. Stateless w.r.t. callers — construct once per
/// <see cref="KeyboardConfig"/> and reuse.
/// </summary>
public sealed class LayerBindingResolver
{
    private readonly KeyboardConfig _config;
    private readonly Dictionary<int, HashSet<int>> _layerPredecessors;

    public LayerBindingResolver(KeyboardConfig config)
    {
        _config = config;
        _layerPredecessors = BuildLayerPredecessors(config);
    }

    /// <summary>
    /// Reverse adjacency: <c>layer → set of layers that can push this layer onto
    /// the active stack</c> (via <c>&amp;mo / &amp;lt / &amp;sl / &amp;tog</c>).
    /// <c>&amp;to</c> is excluded — it replaces the default layer rather than
    /// stacking above it.
    /// </summary>
    public IReadOnlyDictionary<int, HashSet<int>> LayerPredecessors => _layerPredecessors;

    /// <summary>
    /// Walks the predecessor graph from <paramref name="layerIdx"/> to find
    /// the binding that would actually fire at <paramref name="keyIdx"/>.
    /// If the binding at the given layer is <c>&amp;trans</c>, recursively
    /// checks each predecessor layer and returns the first non-transparent
    /// result. Base-layer (0) <c>&amp;trans</c> stays transparent.
    /// </summary>
    public KeyBinding ResolveEffectiveBinding(int layerIdx, int keyIdx)
        => ResolveEffectiveBinding(layerIdx, keyIdx, new HashSet<int>());

    private KeyBinding ResolveEffectiveBinding(int layerIdx, int keyIdx, HashSet<int> visited)
    {
        if (!visited.Add(layerIdx)) return KeyBinding.Transparent;
        if (layerIdx < 0 || layerIdx >= _config.Layers.Count) return KeyBinding.Transparent;

        var layer = _config.Layers[layerIdx];
        var binding = keyIdx < layer.Bindings.Count ? layer.Bindings[keyIdx] : KeyBinding.Transparent;
        if (binding.Behavior != "&trans") return binding;

        // Try direct predecessors in index order — deterministic, and usually
        // puts the closer-to-base layers first.
        if (_layerPredecessors.TryGetValue(layerIdx, out var preds))
        {
            foreach (var p in preds.OrderBy(x => x))
            {
                var ft = ResolveEffectiveBinding(p, keyIdx, visited);
                if (ft.Behavior != "&trans") return ft;
            }
        }

        // Fallback: if we're not on base and base wasn't in the predecessor
        // chain (orphan layer with no recorded activation), fall through to
        // base directly so the label is at least meaningful.
        if (layerIdx != 0)
        {
            var ft = ResolveEffectiveBinding(0, keyIdx, visited);
            if (ft.Behavior != "&trans") return ft;
        }

        return binding;
    }

    /// <summary>
    /// For a key binding, returns which layer it activates when pressed
    /// (if any). The bare ZMK layer-switch behaviors (<c>&amp;to / &amp;mo /
    /// &amp;tog / &amp;lt / &amp;sl</c>) read their first param directly.
    /// Returns null for non-layer-switching bindings.
    /// </summary>
    public static int? ResolveTargetLayer(KeyBinding binding, HoldTap? holdTap = null)
    {
        if ((binding.Behavior == "&to" || binding.Behavior == "&mo"
             || binding.Behavior == "&tog" || binding.Behavior == "&lt"
             || binding.Behavior == "&sl")
            && binding.Params.Count >= 1
            && int.TryParse(binding.Params[0], out var paramLayer))
            return paramLayer;

        if (holdTap is not null
            && (holdTap.HoldBinding == "&to" || holdTap.HoldBinding == "&mo"
                || holdTap.HoldBinding == "&tog" || holdTap.HoldBinding == "&lt"
                || holdTap.HoldBinding == "&sl")
            && holdTap.HoldArity >= 1
            && binding.Params.Count >= 1
            && int.TryParse(binding.Params[0], out var holdLayer))
            return holdLayer;

        return null;
    }

    private static Dictionary<int, HashSet<int>> BuildLayerPredecessors(KeyboardConfig config)
    {
        var result = new Dictionary<int, HashSet<int>>();

        for (int m = 0; m < config.Layers.Count; m++)
        {
            foreach (var b in config.Layers[m].Bindings)
            {
                int? target = null;

                if ((b.Behavior == "&mo" || b.Behavior == "&lt"
                     || b.Behavior == "&sl" || b.Behavior == "&tog")
                    && b.Params.Count >= 1 && int.TryParse(b.Params[0], out var bareLayer))
                {
                    target = bareLayer;
                }

                if (target is int n && n >= 0 && n < config.Layers.Count && n != m)
                {
                    if (!result.TryGetValue(n, out var preds))
                        result[n] = preds = new HashSet<int>();
                    preds.Add(m);
                }
            }
        }

        return result;
    }
}
