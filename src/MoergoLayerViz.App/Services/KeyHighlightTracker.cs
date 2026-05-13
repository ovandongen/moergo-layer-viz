using MoergoLayerViz.App.ViewModels;
using MoergoLayerViz.Core.Diagnostics;
using MoergoLayerViz.Core.Input;
using MoergoLayerViz.Core.Keymap;
using MoergoLayerViz.Core.Models;

namespace MoergoLayerViz.App.Services;

/// <summary>
/// Owns the press-highlight pipeline: the per-layer (mod-set + keycode) →
/// <see cref="KeyViewModel"/> lookup, the held-modifier set, modifier-grace
/// deferral, and the per-key pulse animation. Stateless w.r.t. the hook
/// thread — all internal state is private and only mutated from
/// <see cref="OnHookEvent"/> / <see cref="PulseAt"/> / <see cref="Rebuild"/>.
///
/// <para>The hook thread enters via <see cref="OnHookEvent"/>; HID enters
/// via <see cref="PulseAt"/>; the UI thread enters via <see cref="Rebuild"/>
/// and <see cref="Reset"/>. <see cref="_zmkLookup"/> is published via
/// reference-assignment so the hook never observes a half-built dict.</para>
/// </summary>
public sealed class KeyHighlightTracker
{
    private readonly IReadOnlyList<KeyViewModel> _keys;
    private readonly Func<int> _getActiveLayer;
    private readonly Func<bool> _isHidSourceActive;

    // Active-layer (modifier-set + keycode) → KeyViewModel(s) lookup, rebuilt
    // on every layer change so OnHookEvent can flash the right physical key.
    // Key format: "shift+ctrl|N8" — sorted mod categories, then '|', then the
    // base keycode. Modifiers are folded to 4 categories (shift/ctrl/alt/gui)
    // so LS(...) and RS(...) collapse together; matches the OS, which reports
    // "some shift was held" and doesn't distinguish left/right.
    private Dictionary<string, List<KeyViewModel>> _zmkLookup = new(StringComparer.Ordinal);
    private readonly HashSet<string> _heldModifierCategories = new(StringComparer.Ordinal);
    private readonly Dictionary<KeyViewModel, CancellationTokenSource> _pressCts = new();
    private const int PressHighlightMs = 90;

    // Pending modifier-keypress highlights: when the firmware synthesizes a
    // Shift to produce a shifted symbol (e.g. pressing `(` on a symbol layer),
    // the OS sees Shift + N9 in the same tick. If we flashed the modifier key
    // immediately we'd light up the thumb-shift on every shifted symbol —
    // visual noise. Instead, defer a mod-key highlight by ModifierGraceMs; if
    // a non-modifier press arrives inside that window we cancel it (treat it
    // as synthesized). A physical shift sits isolated for 50+ ms before the
    // next keypress, so its highlight fires.
    private readonly List<CancellationTokenSource> _pendingModHighlights = new();
    private const int ModifierGraceMs = 25;

    public KeyHighlightTracker(
        IReadOnlyList<KeyViewModel> keys,
        Func<int> getActiveLayer,
        Func<bool> isHidSourceActive)
    {
        _keys = keys;
        _getActiveLayer = getActiveLayer;
        _isHidSourceActive = isHidSourceActive;
    }

    /// <summary>
    /// Rebuilds the (mods + keycode) → KeyViewModel lookup for the layer
    /// reported by the active-layer accessor. Called on every layout load,
    /// active-layer change, and signal-macro-table swap.
    /// </summary>
    public void Rebuild(
        KeyboardConfig? config,
        LayerBindingResolver? resolver,
        IReadOnlyDictionary<string, SignalMacro> signalByName)
    {
        // Build into a local then publish via a single reference assignment.
        // OnHookEvent reads _zmkLookup from the hook thread; if we populated
        // the field in place, hook callbacks landing mid-rebuild would observe
        // an empty / half-built dict and miss highlights.
        var next = new Dictionary<string, List<KeyViewModel>>(StringComparer.Ordinal);
        if (config is null)
        {
            _zmkLookup = next;
            return;
        }
        var layerIdx = _getActiveLayer();
        if (layerIdx < 0 || layerIdx >= config.Layers.Count) layerIdx = 0;
        for (int i = 0; i < _keys.Count; i++)
        {
            var binding = resolver?.ResolveEffectiveBinding(layerIdx, i) ?? KeyBinding.Transparent;
            var signal = signalByName.TryGetValue(binding.Behavior, out var s) ? s : null;
            var press = ZmkKeycodeMapper.ExtractEmittedKeypress(binding, signal);
            if (press is null) continue;
            var key = ZmkKeycodeMapper.BuildLookupKey(press.Value.Mods, press.Value.Code);
            if (!next.TryGetValue(key, out var list))
                next[key] = list = new List<KeyViewModel>();
            list.Add(_keys[i]);
        }
        _zmkLookup = next;
        DiagnosticLog.Debug("Highlight", $"lookup rebuilt layer={layerIdx} keys=[{string.Join(",", next.Keys)}]");
    }

    /// <summary>
    /// Hook-thread entry. Resolves the modifier-aware lookup key, applies
    /// modifier-grace deferral, and pulses targets on a hit.
    /// </summary>
    public void OnHookEvent(KeyEvent ev)
    {
        // HID-position highlights take precedence whenever the HID source is
        // active — running both pipelines would double-pulse on every press.
        if (_isHidSourceActive()) return;

        var modCat = ZmkKeycodeMapper.CategoryForModifier(ev.Keycode);

        if (ev.Kind == KeyEventKind.Released)
        {
            if (modCat is not null) _heldModifierCategories.Remove(modCat);
            return;
        }

        // For modifier keypresses themselves, look up with NO modifiers held —
        // the binding `&kp LSHFT` has no wrappers and is keyed as "|LSHFT".
        // For all other keys, use the currently-held modifier set so the
        // lookup discriminates between (e.g.) `&kp N8` and `&kp LS(N8)`.
        var contextMods = modCat is not null ? Array.Empty<string>() : (IEnumerable<string>)_heldModifierCategories;
        var key = ZmkKeycodeMapper.BuildLookupKey(contextMods, ev.Keycode);

        if (!_zmkLookup.TryGetValue(key, out var targets) || targets.Count == 0)
        {
            DiagnosticLog.Debug("Highlight", $"miss key={key} layer={_getActiveLayer()} tableSize={_zmkLookup.Count}");
        }
        else
        {
            DiagnosticLog.Debug("Highlight", $"hit key={key} layer={_getActiveLayer()} → {targets.Count} key(s)");
            if (modCat is not null)
            {
                // Defer modifier highlight — cancelled below if a companion
                // non-modifier press follows within ModifierGraceMs.
                var cts = new CancellationTokenSource();
                lock (_pendingModHighlights) _pendingModHighlights.Add(cts);
                _ = Task.Delay(ModifierGraceMs, cts.Token).ContinueWith(t =>
                {
                    // Remove inside the lock so the cancel path's foreach can
                    // never see a disposed instance. Dispose unconditionally —
                    // both the elapsed and cancelled branches need it, and the
                    // earlier cancel-path-leaks-CTS bug came from skipping it.
                    bool stillPending;
                    lock (_pendingModHighlights) stillPending = _pendingModHighlights.Remove(cts);
                    if (stillPending && !t.IsCanceled)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            foreach (var vm in targets) PulseKeyPress(vm);
                        });
                    }
                    cts.Dispose();
                }, TaskScheduler.Default);
            }
            else
            {
                // Real (non-modifier) keypress — cancel any pending mod
                // highlights; they were synthesized by the firmware.
                lock (_pendingModHighlights)
                {
                    foreach (var c in _pendingModHighlights) c.Cancel();
                    _pendingModHighlights.Clear();
                }
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    foreach (var vm in targets) PulseKeyPress(vm);
                });
            }
        }

        // Update held-mod state AFTER the lookup so a modifier key's own
        // press still matches its no-mod binding.
        if (modCat is not null) _heldModifierCategories.Add(modCat);
    }

    /// <summary>HID-thread entry — bypasses the lookup, pulses the key at
    /// <paramref name="position"/> directly. Posts to UI thread internally.</summary>
    public void PulseAt(int position)
    {
        if (position < 0 || position >= _keys.Count) return;
        var vm = _keys[position];
        Avalonia.Threading.Dispatcher.UIThread.Post(() => PulseKeyPress(vm));
    }

    /// <summary>Clears modifier state on layer reset. Press timers are left to
    /// expire naturally — they're per-keypress and short-lived.</summary>
    public void Reset() => _heldModifierCategories.Clear();

    private void PulseKeyPress(KeyViewModel vm)
    {
        if (_pressCts.TryGetValue(vm, out var existing))
        {
            existing.Cancel();
            existing.Dispose();
        }
        var cts = new CancellationTokenSource();
        _pressCts[vm] = cts;
        vm.IsPressed = true;

        _ = Task.Delay(PressHighlightMs, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_pressCts.TryGetValue(vm, out var stored) && stored == cts)
                {
                    vm.IsPressed = false;
                    _pressCts.Remove(vm);
                    cts.Dispose();
                }
            });
        }, TaskScheduler.Default);
    }
}
