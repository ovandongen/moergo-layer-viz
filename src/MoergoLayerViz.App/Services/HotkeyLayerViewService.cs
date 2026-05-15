using MoergoLayerViz.App.Services.Hotkeys;
using MoergoLayerViz.Core.Diagnostics;
using MoergoLayerViz.Core.Settings;

namespace MoergoLayerViz.App.Services;

/// <summary>
/// Owns the per-keyboard-profile set of "layer view" hotkeys. Each binding
/// maps a global hotkey to a layer index; pressing the hotkey invokes the
/// supplied toggle callback so the host can pin / clear the overlay's
/// displayed layer (tap-to-toggle, see <c>MainWindowViewModel.ToggleLayerViewOverride</c>).
///
/// The active binding set is swapped via <see cref="ApplyBindings"/> — typical
/// callers swap on profile change and after settings save. Per-row failures
/// (key already owned, unsupported name) are returned synchronously so the
/// Settings UI can render inline warnings.
/// </summary>
public sealed class HotkeyLayerViewService : IDisposable
{
    private readonly INativeHotkeyRegistry _registry;
    private readonly Action<int> _onToggle;
    private readonly List<int> _tokens = new();
    private bool _disposed;

    public HotkeyLayerViewService(INativeHotkeyRegistry registry, Action<int> onToggle)
    {
        _registry = registry;
        _onToggle = onToggle;
    }

    /// <summary>
    /// Releases every prior registration and re-registers the supplied bindings
    /// in order. Returns one result per input binding so the UI can mark each
    /// row's success / failure state.
    /// </summary>
    public IReadOnlyList<HotkeyLayerViewBindingResult> ApplyBindings(IReadOnlyList<HotkeyLayerBinding> bindings)
    {
        if (_disposed) return Array.Empty<HotkeyLayerViewBindingResult>();

        UnregisterAll();

        var results = new List<HotkeyLayerViewBindingResult>(bindings.Count);
        foreach (var binding in bindings)
        {
            var layer = binding.LayerIndex;
            var result = _registry.TryRegister(binding.KeyName, binding.ModifiersName, () => _onToggle(layer));
            if (result.Success)
            {
                _tokens.Add(result.Token);
                DiagnosticLog.Info("LayerHotkey",
                    $"registered '{binding.KeyName}' modifiers='{binding.ModifiersName}' → layer {binding.LayerIndex}");
                results.Add(new HotkeyLayerViewBindingResult(binding, true, null));
            }
            else
            {
                DiagnosticLog.Warn("LayerHotkey",
                    $"failed to register '{binding.KeyName}' modifiers='{binding.ModifiersName}': {result.ErrorMessage}");
                results.Add(new HotkeyLayerViewBindingResult(binding, false, result.ErrorMessage));
            }
        }
        return results;
    }

    /// <summary>Releases every active registration without disposing the service.</summary>
    public void UnregisterAll()
    {
        foreach (var token in _tokens)
            _registry.Unregister(token);
        _tokens.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterAll();
    }
}

/// <summary>Per-binding outcome from <see cref="HotkeyLayerViewService.ApplyBindings"/>.</summary>
public sealed record HotkeyLayerViewBindingResult(HotkeyLayerBinding Binding, bool Success, string? ErrorMessage);
