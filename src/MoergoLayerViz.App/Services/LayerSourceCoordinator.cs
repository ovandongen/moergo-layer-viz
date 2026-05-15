using MoergoLayerViz.Core.Diagnostics;
using MoergoLayerViz.Core.Input;
using MoergoLayerViz.Core.Layout;
using ZmkHidProtocol.Transport;

namespace MoergoLayerViz.App.Services;

/// <summary>
/// Wraps the HID <see cref="ILayerSource"/> and forwards its layer / key-position
/// events. After the macro-signal subsystem was removed, HID is the only source.
/// </summary>
public sealed class LayerSourceCoordinator : IDisposable
{
    // Retained for settings-file backwards compatibility; only RawHid is
    // honored at runtime. Phase 2 of the refactor removes the mode concept
    // entirely.
    public const string ModeAuto = "Auto";
    public const string ModeRawHid = "RawHid";
    public const string ModeSharpHook = "SharpHook";

    private readonly ILayerSource? _hid;
    private bool _started;

    public LayerSourceCoordinator(ILayerSource? hid)
    {
        _hid = hid;
        if (_hid is not null)
        {
            _hid.ConnectionChanged += OnSourceConnectionChanged;
            _hid.LayerChanged += ForwardLayer;
            _hid.KeyPositionEvent += ForwardKey;
        }
    }

    /// <summary>Raised on the HID source's thread when the layer changes.</summary>
    public event Action<int>? ActiveLayerChanged;

    /// <summary>Raised on the HID thread when the source reports a physical key event.</summary>
    public event Action<int, bool>? ActiveKeyPositionEvent;

    /// <summary>Raised on the HID source's thread whenever its connection state changes.</summary>
    public event Action? ActiveSourceChanged;

    /// <summary>HID source name with a "(searching)" suffix when not currently connected. Empty when no source is configured.</summary>
    public string ActiveSourceLabel { get; private set; } = "";

    /// <summary>True when HID is connected.</summary>
    public bool IsHidActive => _hid is not null && _hid.IsConnected;

    public void Start()
    {
        if (_started) return;
        _started = true;
        _hid?.Start();
        UpdateLabel();
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        _hid?.Stop();
    }

    public void Dispose()
    {
        Stop();
        if (_hid is not null)
        {
            _hid.ConnectionChanged -= OnSourceConnectionChanged;
            _hid.LayerChanged -= ForwardLayer;
            _hid.KeyPositionEvent -= ForwardKey;
            _hid.Dispose();
        }
    }

    /// <summary>
    /// Tell the HID source which keyboard the user has selected. The HID
    /// source uses this to ignore reports from any *other* connected ZMK
    /// device (both Moergo boards share VID:PID, so without this filter a
    /// Go60 plugged in next to a Glove80 would feed Go60 layer indices into
    /// the Glove80 layout). Safe to call any time; closes and re-discovers
    /// in the background.
    /// </summary>
    public void SetActiveProfile(IKeyboardProfile? profile)
    {
        _hid?.SetMatcher(profile is null ? null : new KeyboardProfileMatcher(profile));
    }

    private void OnSourceConnectionChanged()
    {
        UpdateLabel();
        DiagnosticLog.Info("LayerSrc",
            $"HID connection changed: {_hid?.SourceName ?? "(none)"} connected={_hid?.IsConnected ?? false}");
        ActiveSourceChanged?.Invoke();
        if (_hid is not null && _hid.IsConnected)
            ActiveLayerChanged?.Invoke(_hid.CurrentLayer);
    }

    private void UpdateLabel()
    {
        if (_hid is null) { ActiveSourceLabel = ""; return; }
        ActiveSourceLabel = _hid.IsConnected ? _hid.SourceName : $"{_hid.SourceName} (searching)";
    }

    private void ForwardLayer(int layer) => ActiveLayerChanged?.Invoke(layer);
    private void ForwardKey(int pos, bool pressed) => ActiveKeyPositionEvent?.Invoke(pos, pressed);
}
