using MoergoLayerViz.Core.Diagnostics;
using MoergoLayerViz.Core.Input;
using MoergoLayerViz.Core.Layout;
using ZmkHidProtocol.Building;
using ZmkHidProtocol.Protocol;
using ZmkHidProtocol.Transport;

namespace MoergoLayerViz.App.Services;

/// <summary>
/// Owns the HID stack lifecycle: the <see cref="ILayerSource"/> + paired
/// <see cref="ICommandSink"/>, the <see cref="CommandSender"/> for app-issued
/// pushes, and the <see cref="LayerStateTracker"/> that distinguishes
/// app-pushed bitmasks from external (firmware-side) changes.
///
/// <para>The trio is constructed fresh on every <see cref="Start"/> and
/// torn down on <see cref="Stop"/>. Stop/Start cycles (toggling live
/// highlighting) get a brand-new <see cref="RawHidLayerSource"/> rather
/// than reusing the previous instance — the underlying hidapi handle
/// doesn't always recover cleanly from a reopen, so this avoids the
/// "UI freezes after re-enabling tracking" failure mode.</para>
///
/// <para><see cref="PushLayer"/> is safe to call at any point — it no-ops
/// when the stack isn't running.</para>
/// </summary>
public interface IHidPipeline : IDisposable
{
    /// <summary>Fires on the HID source's thread when the active layer changes (or on connect).</summary>
    event Action<int>? ActiveLayerChanged;

    /// <summary>Fires on the HID source's thread for physical key press / release.</summary>
    event Action<int, bool>? KeyPositionEvent;

    /// <summary>Fires on the HID source's thread when <see cref="IsConnected"/> changes.</summary>
    event Action? ConnectionChanged;

    bool IsConnected { get; }

    /// <summary>Status-bar label ("Raw HID (Go60 Left)" / "Raw HID (searching)"). Empty before <see cref="Start"/>.</summary>
    string SourceLabel { get; }

    int CurrentLayer { get; }

    /// <summary>
    /// True when the last confirmed layer state matched a bitmask that the
    /// app had armed via <see cref="PushLayer"/> / <see cref="PushLayerSync"/>.
    /// Flips false when the firmware reports a change the app didn't push
    /// (user pressed a layer key, etc.). Used at shutdown to distinguish
    /// "app left the keyboard on the mouse layer" from "user picked it".
    /// </summary>
    bool IsLastChangeAppControlled { get; }

    void Start();
    void Stop();
    void SetActiveProfile(IKeyboardProfile profile);

    /// <summary>Fire-and-forget app push. No-op if the source isn't started.</summary>
    void PushLayer(int index);

    /// <summary>Shutdown-only synchronous push with bounded wait so teardown can't hang.</summary>
    void PushLayerSync(int index, TimeSpan timeout);

    Task<DeviceInfo?> QueryDeviceInfoAsync(TimeSpan timeout, CancellationToken ct);
    Task<string?> QueryConfigIdAsync(TimeSpan timeout, CancellationToken ct);
}

public sealed class HidPipeline : IHidPipeline
{
    private IKeyboardProfile _profile;
    private ILayerSource? _source;
    private CommandSender? _sender;
    private LayerStateTracker? _tracker;
    private bool _started;
    private bool _disposed;

    public event Action<int>? ActiveLayerChanged;
    public event Action<int, bool>? KeyPositionEvent;
    public event Action? ConnectionChanged;

    public HidPipeline(IKeyboardProfile profile)
    {
        _profile = profile;
    }

    public bool IsConnected => _source?.IsConnected ?? false;
    public int CurrentLayer => _source?.CurrentLayer ?? 0;
    public bool IsLastChangeAppControlled => _tracker?.IsAppControlled ?? false;

    public string SourceLabel
    {
        get
        {
            if (!_started || _source is null) return "";
            return _source.IsConnected ? _source.SourceName : $"{_source.SourceName} (searching)";
        }
    }

    public void Start()
    {
        if (_started) return;
        var (source, sink) = LayerSourceFactory.Create(new KeyboardProfileMatcher(_profile));
        _source = source;
        _sender = new CommandSender(source, sink);
        _tracker = new LayerStateTracker();
        _source.ReportReceived += _tracker.OnReport;
        _tracker.StateChanged += OnLayerStateConfirmed;
        _source.LayerChanged += OnLayerChanged;
        _source.KeyPositionEvent += OnKeyPosition;
        _source.ConnectionChanged += OnSourceConnectionChanged;
        _started = true;
        _source.Start();
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        _sender?.Dispose();
        _sender = null;
        if (_source is not null)
        {
            _source.Stop();
            _source.ReportReceived -= _tracker!.OnReport;
            _source.LayerChanged -= OnLayerChanged;
            _source.KeyPositionEvent -= OnKeyPosition;
            _source.ConnectionChanged -= OnSourceConnectionChanged;
            _source.Dispose();
            _source = null;
        }
        if (_tracker is not null)
        {
            _tracker.StateChanged -= OnLayerStateConfirmed;
            _tracker = null;
        }
    }

    public void SetActiveProfile(IKeyboardProfile profile)
    {
        _profile = profile;
        _source?.SetMatcher(new KeyboardProfileMatcher(profile));
    }

    public void PushLayer(int index)
    {
        if (!_started || _sender is null || _tracker is null) return;
        uint bitmask = 1u << index;
        _tracker.ExpectAppState(bitmask);
        var sender = _sender;
        _ = Task.Run(async () =>
        {
            try
            {
                await sender.SetLayerStateAsync(bitmask, CancellationToken.None);
                DiagnosticLog.Info("LayerPush", $"sent layer {index} (mask 0x{bitmask:X})");
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("LayerPush", $"layer {index} failed: {ex.Message}");
            }
        });
    }

    public void PushLayerSync(int index, TimeSpan timeout)
    {
        if (!_started || _sender is null || _tracker is null) return;
        uint bitmask = 1u << index;
        _tracker.ExpectAppState(bitmask);
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            _sender.SetLayerStateAsync(bitmask, cts.Token).GetAwaiter().GetResult();
            DiagnosticLog.Info("LayerPush", $"sync sent layer {index} (mask 0x{bitmask:X})");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn("LayerPush", $"sync layer {index} failed: {ex.Message}");
        }
    }

    public async Task<DeviceInfo?> QueryDeviceInfoAsync(TimeSpan timeout, CancellationToken ct)
    {
        if (!_started || _sender is null) return null;
        try
        {
            return await _sender.QueryDeviceInfoAsync(timeout, ct);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn("HidQuery", $"device-info query failed: {ex.Message}");
            return null;
        }
    }

    public async Task<string?> QueryConfigIdAsync(TimeSpan timeout, CancellationToken ct)
    {
        if (!_started || _sender is null) return null;
        try
        {
            return await _sender.QueryConfigIdAsync(timeout, ct);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn("HidQuery", $"config-id query failed: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    private void OnLayerStateConfirmed(uint bitmask)
    {
        if (_tracker is null) return;
        if (_tracker.IsAppControlled)
            DiagnosticLog.Info("LayerState", $"app push acknowledged: layer {_tracker.HighestActiveLayer} (mask 0x{bitmask:X})");
        else
            DiagnosticLog.Info("LayerState", $"external change: layer {_tracker.HighestActiveLayer} (mask 0x{bitmask:X})");
    }

    private void OnSourceConnectionChanged()
    {
        var source = _source;
        if (source is null) return;
        DiagnosticLog.Info("LayerSrc",
            $"HID connection changed: {source.SourceName} connected={source.IsConnected}");
        ConnectionChanged?.Invoke();
        // Resync layer on connect: the source may already have a CurrentLayer
        // from its initial report before our subscription delivered events.
        if (source.IsConnected)
            ActiveLayerChanged?.Invoke(source.CurrentLayer);
    }

    private void OnLayerChanged(int layer) => ActiveLayerChanged?.Invoke(layer);
    private void OnKeyPosition(int pos, bool pressed) => KeyPositionEvent?.Invoke(pos, pressed);
}
